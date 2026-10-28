using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
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
            Assert.That(Labels.Texts.Select(text => text.Culture), Is.EqualTo(new[] { string.Empty, "de", "de-CH", "sr-Latn" }));
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
    [TestCase("|de\n", TestName = "LocalizedText_Refuses(a line break after a culture)")]
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
    [TestCase("#Form.Email\n", null)]
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
    public void SettingsObject_RefusesANameWithALoneSurrogate()
    {
        Assert.Throws<ArgumentException>(() => SettingsObject.Parse("{ \"" + (char)92 + "ud800\": 1 }"));
    }

    [Test]
    public void ACultureWithALineBreak_IsNotACulture()
    {
        var document = JsonNode.Parse(FormDefinitionSerializer.WriteString(Minimal()))!.AsObject();
        document["pages"]![0]!["title"] = new JsonObject { [string.Empty] = "Details", ["de\n"] = "Angaben" };

        var result = DefinitionFixtures.Serializer().Read(document.ToJsonString());

        Assert.That(result.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, JsonPaths.Property("$.pages[0].title", "de\n")) }));
    }

    [Test]
    public void AListOperand_HoldsAtLeastOneItem_AndNoList()
    {
        var items = new List<ConditionOperand> { new ConditionOperand.Text("a") };
        var list = new ConditionOperand.List(items);
        items.Add(new ConditionOperand.Text("b"));

        Assert.Multiple(() =>
        {
            Assert.That(list.Items, Has.Count.EqualTo(1), "the list keeps its own copy");
            Assert.Throws<ArgumentException>(() => _ = new ConditionOperand.List([]));
            Assert.Throws<ArgumentException>(() => _ = new ConditionOperand.List([list]));
            Assert.Throws<ArgumentException>(() => _ = new ConditionOperand.List([null!]));
        });
    }

    [Test]
    public void ADefinitionAsDeepAsADocumentMayBe_ReadsAndWritesTheSame()
    {
        // 29 fields inside each other put the innermost one's options 64 levels deep, the deepest a document may
        // nest; the reader then fills in their empty list of items one level further down.
        var json = FormDefinitionSerializer.WriteString(Nested(29, new FieldOptions { Source = OptionSource.List, OptionList = FieldKey(9) }));

        var read = DefinitionFixtures.Read(json);

        Assert.That(FormDefinitionSerializer.WriteString(read), Is.EqualTo(json));
    }

    [TestCase(40, TestName = "ADefinitionDeeperThanADocumentMayBe_IsRefusedByTheWriterAndTheSaveChecks(deeper)")]
    [TestCase(70, TestName = "ADefinitionDeeperThanADocumentMayBe_IsRefusedByTheWriterAndTheSaveChecks(deeper than the serializer goes)")]
    public void ADefinitionDeeperThanADocumentMayBe_IsRefusedByTheWriterAndTheSaveChecks(int fields)
    {
        var definition = Nested(fields);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => FormDefinitionSerializer.Write(definition));
            Assert.That(
                DefinitionFixtures.Serializer().CheckSave(definition).Select(problem => (problem.Code, problem.Path)),
                Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$") }));
        });
    }

    [Test]
    public void ADefinitionLargerThanTheLimit_IsRefusedByTheSaveChecks()
    {
        var definition = Minimal() with { Pages = [Minimal().Pages[0] with { Title = new LocalizedText(new string('a', 300)) }] };

        Assert.That(
            DefinitionFixtures.Serializer(new DefinitionLimits { MaxBytes = 200 }).CheckSave(definition).Select(problem => (problem.Code, problem.Path)),
            Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$") }));
    }

    [Test]
    public void AStringFromOutsideTheDefinition_IsMadeWellFormed_EachLoneSurrogateReplaced()
    {
        var paired = "a" + (char)0xD83D + (char)0xDE00 + "b";

        Assert.Multiple(() =>
        {
            Assert.That(StrictStringConverter.WellFormed(paired), Is.SameAs(paired), "a well-formed string as it is");
            Assert.That(StrictStringConverter.WellFormed((char)0xDC00 + "b" + (char)0xD800), Is.EqualTo((char)0xFFFD + "b" + (char)0xFFFD), "a low half first, and a high half last");
            Assert.That(StrictStringConverter.WellFormed(string.Empty), Is.Empty);
        });
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

    [Test]
    public void WhatTheWriterCannotWrite_IsReportedAsWhatItIs()
    {
        // Past the save checks, what the writer refuses is a value it cannot carry, not a document that nests too deep.
        var page = Minimal().Pages[0] with { Rules = [new FormRule { Key = FieldKey(5), Effect = RuleEffect.Show, Conditions = [new UnknownItem()] }] };
        var definition = Minimal() with { Pages = [page] };

        var problems = DefinitionFixtures.Serializer().CheckSave(definition);

        Assert.Multiple(() =>
        {
            Assert.That(problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$") }));
            Assert.That(problems[0].Message, Does.Contain("Unknown rule item"));
            Assert.That(() => FormDefinitionSerializer.Write(definition), Throws.ArgumentException.With.Message.Contains("Unknown rule item"));
        });
    }

    [Test]
    public void ACultureThatIsNull_IsRefusedAsDocumented()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new LocalizedText([new KeyValuePair<string, string>(null!, "Name")]));
            Assert.Throws<ArgumentException>(() => _ = new LocalizedText([new(string.Empty, "Name"), new KeyValuePair<string, string>(null!, "Name")]), "beside the default text");
        });
    }

    [Test]
    public void TheReaderOfWhatTheSiteStored_IsBuiltOnce_WithTheSerializer()
    {
        var serializer = DefinitionFixtures.Serializer(new DefinitionLimits { MaxBytes = 1000 });
        var ceiling = DefinitionFixtures.Serializer(DefinitionLimits.Ceiling);
        var seen = new FormDefinitionSerializer[64];
        Parallel.For(0, seen.Length, i => seen[i] = serializer.Stored);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.All.SameAs(serializer.Stored), "one reader, whoever asks first");
            Assert.That(serializer.Stored.Limits, Is.EqualTo(DefinitionLimits.Ceiling));
            Assert.That(ceiling.Stored, Is.SameAs(ceiling), "a serializer with the highest limits reads what is stored itself");
        });
    }

    [Test]
    public void BothReaders_RefuseTextAndBytesThatAreNotStrictUtf8_Alike()
    {
        var serializer = DefinitionFixtures.Serializer();
        var surrogate = "{\"a\":\"" + (char)0xD800 + "\"}";
        byte[] invalid = [0x7B, 0xFF, 0x7D];

        static (string, string, string) Only(IReadOnlyList<DefinitionProblem> problems) => (problems.Single().Code, problems.Single().Path, problems.Single().Message);

        Assert.Multiple(() =>
        {
            Assert.That(Only(serializer.Read(surrogate).Problems), Is.EqualTo(Only(FormExportFormat.Read(surrogate, serializer).Problems)), "a lone surrogate");
            Assert.That(Only(serializer.Read(invalid).Problems), Is.EqualTo(Only(FormExportFormat.Read(invalid, serializer).Problems)), "bytes that are not UTF-8");
            Assert.That(Only(serializer.Read(surrogate).Problems).Item1, Is.EqualTo(DefinitionErrors.InvalidJson));
        });
    }

    [Test]
    public void AGroupNested30Deep_ReadsInAboutTheTimeOfOneLevel()
    {
        // The innermost condition holds a long operand: read again at every level, as it was, it would cost 30 times
        // as much; read once, the levels cost next to nothing.
        var shallow = Rule(1);
        var deep = Rule(30);

        Assert.That(Fastest(deep), Is.LessThan(Fastest(shallow) * 4), "30 levels against one");

        static byte[] Rule(int depth)
        {
            JsonNode item = new JsonObject { ["field"] = FieldKey(1).ToString("D"), ["op"] = "equals", ["value"] = new string('a', 200_000) };
            for (var level = 0; level < depth; level++)
            {
                item = new JsonObject { ["match"] = "any", ["conditions"] = new JsonArray(item) };
            }

            return System.Text.Encoding.UTF8.GetBytes(item.ToJsonString());
        }

        static TimeSpan Fastest(byte[] json)
        {
            var info = DefinitionJsonContext.Default.Options.GetTypeInfo(typeof(RuleItem));
            var fastest = TimeSpan.MaxValue;
            for (var run = 0; run < 7; run++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var item = JsonSerializer.Deserialize(json, info);
                watch.Stop();
                Assert.That(item, Is.InstanceOf<RuleGroup>());
                fastest = watch.Elapsed < fastest ? watch.Elapsed : fastest;
            }

            return fastest;
        }
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

    private static Guid FieldKey(int number) => new($"30000000-0000-4000-8000-{number:000000000000}");

    // A rule item no converter knows, as a type of a later version might be.
    private sealed record UnknownItem : RuleItem;

    // The minimal definition with fields inside each other, as many as given, the innermost with the options given.
    private static FormDefinition Nested(int fields, FieldOptions? options = null)
    {
        var field = new FormField { Key = FieldKey(1000 + fields), Type = "text", Options = options };
        for (var i = fields - 1; i > 0; i--)
        {
            field = new FormField { Key = FieldKey(1000 + i), Type = "repeater", Fields = [field] };
        }

        var page = Minimal().Pages[0];
        return Minimal() with { Pages = [page with { Groups = [page.Groups[0] with { Fields = [field] }] }] };
    }

    private static FormDefinition Minimal() => new()
    {
        Key = new Guid("f0000000-0000-4000-8000-000000000001"),
        Pages = [new FormPage { Key = new Guid("10000000-0000-4000-8000-000000000001"), Groups = [new FormGroup { Key = new Guid("20000000-0000-4000-8000-000000000001") }] }],
    };
}
