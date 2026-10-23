using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>The value types of the model: localised texts, default values, settings objects, instants, and the canonical writer.</summary>
public sealed class DefinitionValueTests
{
    private static readonly LocalizedText Labels = new(
    [
        new(string.Empty, "Email"),
        new("de", "E-Mail"),
        new("de-CH", "E-Mail-Adresse"),
        new("sr-Latn", "Imejl"),
    ]);

    [TestCase("de-CH", "E-Mail-Adresse")]
    [TestCase("DE-ch", "E-Mail-Adresse")]
    [TestCase("de-AT", "E-Mail")]
    [TestCase("de", "E-Mail")]
    [TestCase("sr-Latn-RS", "Imejl")]
    [TestCase("sr-Cyrl-RS", "Email")]
    [TestCase("fr", "Email")]
    [TestCase("", "Email")]
    [TestCase(null, "Email")]
    public void LocalizedText_Resolves_ThroughTheMoreGeneralCulturesToTheDefault(string? culture, string expected)
    {
        Assert.That(Labels.Resolve(culture), Is.EqualTo(expected));
    }

    [Test]
    public void LocalizedText_SortsItsCultures_DefaultFirst_AndComparesByContent()
    {
        var other = new LocalizedText([new("sr-Latn", "Imejl"), new("de-CH", "E-Mail-Adresse"), new(string.Empty, "Email"), new("de", "E-Mail")]);

        Assert.Multiple(() =>
        {
            Assert.That(Labels.Texts.Select(text => text.Key), Is.EqualTo(new[] { string.Empty, "de", "de-CH", "sr-Latn" }));
            Assert.That(other, Is.EqualTo(Labels));
            Assert.That(other.GetHashCode(), Is.EqualTo(Labels.GetHashCode()));
            Assert.That(new LocalizedHtml("Email"), Is.Not.EqualTo(new LocalizedText("Email")), "a rich text is not a plain text");
            Assert.That(Labels.Default, Is.EqualTo("Email"));
            Assert.That(Labels.TryGetText("DE", out var german) ? german : null, Is.EqualTo("E-Mail"));
            Assert.That(Labels.TryGetText("fr", out _), Is.False);
        });
    }

    // The cultures, separated by |; an empty part is the default culture, and null is no text at all.
    [TestCase("de", TestName = "LocalizedText_Refuses(no default)")]
    [TestCase("|de|DE", TestName = "LocalizedText_Refuses(a culture twice in two cases)")]
    [TestCase("|d", TestName = "LocalizedText_Refuses(a one-letter culture)")]
    [TestCase("|de_CH", TestName = "LocalizedText_Refuses(an underscore)")]
    [TestCase(null, TestName = "LocalizedText_Refuses(no text at all)")]
    public void LocalizedText_RefusesTextsThatAreNotOne(string? cultures)
    {
        var texts = (cultures?.Split('|') ?? []).Select(culture => new KeyValuePair<string, string>(culture, "x"));

        Assert.Throws<ArgumentException>(() => _ = new LocalizedText(texts));
    }

    [TestCase("#Form.Email", "Form.Email")]
    [TestCase("#jfb_contact-2", "jfb_contact-2")]
    [TestCase("#", null)]
    [TestCase("# Form", null)]
    [TestCase("#Form Email", null)]
    [TestCase("Form.Email", null)]
    [TestCase("#Form.Email ", null)]
    public void DictionaryKeys_AreTheWholeTextAfterAHash(string text, string? key)
    {
        Assert.That(LocalizedString.TryGetDictionaryKey(text, out var found) ? found : null, Is.EqualTo(key));
    }

    [Test]
    public void FieldDefaultValue_KeepsWhetherItIsAList()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldDefaultValue.Of("a"), Is.Not.EqualTo(FieldDefaultValue.OfList(["a"])));
            Assert.That(FieldDefaultValue.OfList(["a", "b"]), Is.EqualTo(FieldDefaultValue.OfList(["a", "b"])));
            Assert.That(FieldDefaultValue.OfList(["a", "b"]).Values, Is.EqualTo(new[] { "a", "b" }));
            Assert.Throws<ArgumentException>(() => FieldDefaultValue.OfList([]));
        });
    }

    [Test]
    public void SettingsObject_IsCanonical_SortedAndWithoutEmptyProperties()
    {
        var settings = SettingsObject.Parse("""{ "z": 1, "a": { "y": [ 2, { "q": null, "p": true } ], "x": null, "w": [] }, "m": {}, "b": "text" }""");

        Assert.Multiple(() =>
        {
            Assert.That(settings.ToString(), Is.EqualTo("""{"a":{"y":[2,{"p":true}]},"b":"text","z":1}"""));
            Assert.That(settings, Is.EqualTo(SettingsObject.Parse("""{"b":"text","a":{"y":[2,{"p":true}]},"z":1}""")));
            Assert.That(settings.TryGetProperty("z", out var z) ? z.GetInt32() : 0, Is.EqualTo(1));
        });
    }

    [TestCase("""{ "max": 2.5 }""", TestName = "SettingsObject_Refuses(a decimal)")]
    [TestCase("""{ "max": 1e3 }""", TestName = "SettingsObject_Refuses(an exponent)")]
    [TestCase("""{ "max": -0 }""", TestName = "SettingsObject_Refuses(minus zero)")]
    [TestCase("""{ "max": 1000000000000000 }""", TestName = "SettingsObject_Refuses(16 digits)")]
    [TestCase("""{ "list": [ "a", null ] }""", TestName = "SettingsObject_Refuses(a null in an array)")]
    [TestCase("""{ "a": 1, "a": 2 }""", TestName = "SettingsObject_Refuses(a property twice)")]
    [TestCase("""{ "a": null, "b": {} }""", TestName = "SettingsObject_Refuses(nothing in it)")]
    [TestCase("""[ 1 ]""", TestName = "SettingsObject_Refuses(an array)")]
    public void SettingsObject_RefusesWhatADefinitionCannotHold(string json)
    {
        Assert.Throws<ArgumentException>(() => SettingsObject.Parse(json));
    }

    [Test]
    public void SettingsObject_AcceptsTheLargestIntegers()
    {
        Assert.That(SettingsObject.Parse("""{ "a": 999999999999999, "b": -999999999999999 }""").ToString(), Is.EqualTo("""{"a":999999999999999,"b":-999999999999999}"""));
    }

    [Test]
    public void CanonicalStrings_EscapeOnlyQuotesBackslashesAndControlCharacters()
    {
        var text = "a\"b\\c/<>&'\b\f\n\r\t" + (char)0x1 + (char)0x1F + (char)0x7F + (char)0x85 + (char)0x2028 + (char)0x2029 + (char)0xE9 + char.ConvertFromUtf32(0x1F39F);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(text));

        var written = CanonicalJson.Write(document.RootElement, sortProperties: false);

        Assert.That(written, Is.EqualTo("\"a\\\"b\\\\c/<>&'\\b\\f\\n\\r\\t\\u0001\\u001f" + (char)0x7F + (char)0x85 + (char)0x2028 + (char)0x2029 + (char)0xE9 + char.ConvertFromUtf32(0x1F39F) + "\""));
    }

    [Test]
    public void TheWriter_RefusesALoneSurrogate_AndTheSaveChecksNameIt()
    {
        var definition = Minimal() with
        {
            Pages = [Minimal().Pages[0] with { Title = new LocalizedText("Ti" + (char)0xD800 + "tle") }],
        };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => FormDefinitionSerializer.Write(definition));
            Assert.That(
                DefinitionFixtures.Serializer().CheckSave(definition).Select(problem => (problem.Code, problem.Path)),
                Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$.pages[0].title['']") }));
        });
    }

    [TestCase(DateTimeKind.Local, 0)]
    [TestCase(DateTimeKind.Unspecified, 0)]
    [TestCase(DateTimeKind.Utc, 1)]
    public void Instants_AreUtcWithWholeSeconds(DateTimeKind kind, int milliseconds)
    {
        var instant = new DateTime(2026, 11, 1, 8, 0, 0, milliseconds, kind);
        var definition = Minimal() with { Settings = new FormSettings { Availability = new FormAvailabilitySettings { OpensAt = instant } } };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => FormDefinitionSerializer.Write(definition));
            Assert.That(
                DefinitionFixtures.Serializer().CheckSave(definition).Select(problem => (problem.Code, problem.Path)),
                Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$.settings.availability.opensAt") }));
        });
    }

    [Test]
    public void AnInstant_ReadsAsUtc_AndWritesTheSame()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Settings.Availability.OpensAt, Is.EqualTo(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(definition.Settings.Availability.OpensAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void ADefinition_IsAlwaysWrittenInTheCurrentSchemaVersion()
    {
        using var document = JsonDocument.Parse(FormDefinitionSerializer.Write(Minimal()));

        Assert.That(document.RootElement.EnumerateObject().First().ToString(), Is.EqualTo($"\"schemaVersion\":{FormDefinition.CurrentSchemaVersion}"));
    }

    private static FormDefinition Minimal() => new()
    {
        Key = new Guid("f0000000-0000-4000-8000-000000000001"),
        Pages = [new FormPage { Key = new Guid("10000000-0000-4000-8000-000000000001"), Groups = [new FormGroup { Key = new Guid("20000000-0000-4000-8000-000000000001") }] }],
    };
}
