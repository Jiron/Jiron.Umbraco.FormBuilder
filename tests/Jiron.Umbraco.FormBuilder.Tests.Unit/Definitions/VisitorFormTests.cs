using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// A form's public projection as a visitor's page gets it in one culture (ADR-0084): every text of the full form, the
/// texts of public settings among them, becomes the one string the culture shows, rich text as rich text, and nothing
/// else changes; a setting that is no proper text shows nothing.
/// </summary>
[TestFixture]
public sealed class VisitorFormTests
{
    private const string Mapped = "MAPPED";

    [Test]
    public void EveryTextOfTheFullForm_BecomesOneString_AndNothingElseChanges()
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), TestFieldTypes.Registry);
        var perCulture = JsonNode.Parse(PublicJson(projection))!;
        var visitor = JsonNode.Parse(VisitorForm.Write(VisitorForm.Map(projection, _ => Mapped, TestFieldTypes.Registry)))!;

        var texts = new List<string>();
        var differences = new List<string>();
        Compare(perCulture, visitor, "$", texts, differences);

        Assert.Multiple(() =>
        {
            Assert.That(differences, Is.Empty);
            Assert.That(texts, Has.Count.GreaterThan(40), "the full form holds texts of every kind");
            Assert.That(texts.Count(path => path.Contains("].settings.", StringComparison.Ordinal)), Is.EqualTo(4), "the texts of the fields' public settings among them");
            Assert.That(texts, Does.Contain("$.settings.closedMessage").And.Contain("$.validationRules[0].message"));
        });
    }

    [Test]
    public void ARichText_IsShownAsRichText()
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), TestFieldTypes.Registry);

        var visitor = JsonNode.Parse(VisitorForm.Write(VisitorForm.Map(projection, text => text is LocalizedHtml ? "html" : "text", TestFieldTypes.Registry)))!;

        Assert.Multiple(() =>
        {
            Assert.That(visitor["settings"]!["submitLabel"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(visitor["settings"]!["closedMessage"]!.GetValue<string>(), Is.EqualTo("html"));
            Assert.That(visitor["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(visitor["pages"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("html"));
            Assert.That(visitor["pages"]![0]!["groups"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("html"));
            Assert.That(visitor["pages"]![1]!["groups"]![0]!["fields"]![6]!["settings"]!["text"]!.GetValue<string>(), Is.EqualTo("html"), "a consent's text");
            Assert.That(visitor["pages"]![1]!["groups"]![0]!["fields"]![5]!["settings"]!["text"]!.GetValue<string>(), Is.EqualTo("text"), "a heading's text");
            Assert.That(visitor["pages"]![0]!["groups"]![0]!["fields"]![1]!["settings"]!["confirmLabel"]!.GetValue<string>(), Is.EqualTo("text"));
        });
    }

    [TestCase("""{"caption":{"":"Hello","de":"Hallo"},"count":2}""", """{"caption":"text:Hello","count":2}""")]
    [TestCase("""{"box":{"note":{"":"<p>N</p>"},"size":3}}""", """{"box":{"note":"html:<p>N</p>","size":3}}""")]
    [TestCase("""{"box":{"note":"plain"}}""", """{"box":{"note":""}}""")]
    [TestCase("""{"caption":"plain"}""", """{"caption":""}""")]
    [TestCase("""{"caption":{"":5}}""", """{"caption":""}""")]
    [TestCase("""{"caption":{"de":"Hallo"}}""", """{"caption":""}""")]
    [TestCase("""{"caption":{"":"Hello","not a culture":"x"}}""", """{"caption":""}""")]
    [TestCase("""{"box":"plain","count":"many"}""", """{"box":"plain","count":"many"}""")]
    [TestCase("""{"unknown":{"":"Hello"}}""", """{"unknown":{"":"Hello"}}""")]
    public void ATypesTextSettings_BecomeStrings_AndOneThatIsNoProperText_ShowsNothing(string settings, string shown)
    {
        var types = TestFieldTypes.Build(builder => builder.Append<NotesFieldType>());
        var form = WithField(new PublicField { Key = Guid.NewGuid(), Type = "notes", Alias = "notes", Settings = SettingsObject.Parse(settings) });

        var mapped = VisitorForm.Map(form, text => (text is LocalizedHtml ? "html:" : "text:") + text.Default, types);

        Assert.That(mapped.Pages[0].Groups[0].Fields[0].Settings?.ToString(), Is.EqualTo(shown));
    }

    [Test]
    public void SettingsWithoutTexts_OrOfATypeTheSiteLacks_StayAsTheyAre()
    {
        var settings = SettingsObject.Parse("""{"rows":4,"label":{"":"kept"}}""");
        var form = WithField(
            new PublicField { Key = Guid.NewGuid(), Type = "textarea", Alias = "story", Settings = settings },
            new PublicField { Key = Guid.NewGuid(), Type = "gone", Alias = "gone", Settings = settings });

        var mapped = VisitorForm.Map(form, _ => Mapped, TestFieldTypes.Registry);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Pages[0].Groups[0].Fields[0].Settings, Is.SameAs(settings));
            Assert.That(mapped.Pages[0].Groups[0].Fields[1].Settings, Is.SameAs(settings));
        });
    }

    // Walks the projection as it writes texts per culture beside the visitor's form: a text per culture is one string
    // there, and everything else is the same.
    private static void Compare(JsonNode? perCulture, JsonNode? visitor, string path, List<string> texts, List<string> differences)
    {
        switch (perCulture)
        {
            case JsonObject text when text.ContainsKey(string.Empty):
                texts.Add(path);
                if (visitor is not JsonValue value || value.GetValue<string>() != Mapped)
                {
                    differences.Add($"{path}: the text is {visitor?.ToJsonString() ?? "missing"}");
                }

                break;
            case JsonObject node when visitor is JsonObject other:
                foreach (var name in node.Select(property => property.Key).Union(other.Select(property => property.Key)))
                {
                    Compare(node[name], other[name], $"{path}.{name}", texts, differences);
                }

                break;
            case JsonArray list when visitor is JsonArray other && list.Count == other.Count:
                for (var i = 0; i < list.Count; i++)
                {
                    Compare(list[i], other[i], $"{path}[{i}]", texts, differences);
                }

                break;
            default:
                if (!JsonNode.DeepEquals(perCulture, visitor))
                {
                    differences.Add($"{path}: {perCulture?.ToJsonString()} became {visitor?.ToJsonString()}");
                }

                break;
        }
    }

    private static string PublicJson(PublicForm projection) =>
        System.Text.Json.JsonSerializer.Serialize(projection, PublicJsonContext.Default.PublicForm);

    private static PublicForm WithField(params PublicField[] fields)
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), TestFieldTypes.Registry);
        var page = projection.Pages[0];
        return projection with { Pages = [page with { Groups = [page.Groups[0] with { Fields = fields }] }] };
    }

    // A package's type whose public settings hold a text and an object with a rich text.
    private sealed class NotesFieldType() : FormBuilderFieldTypeBase<NotesFieldSettings>(new FieldTypeDeclaration
    {
        Alias = "notes",
        Name = "Notes",
        Icon = "icon-notepad",
        PaletteGroup = FieldPaletteGroup.Content,
    });
}

/// <summary>The settings of the test type <c>notes</c>.</summary>
public sealed class NotesFieldSettings
{
    /// <summary>A text.</summary>
    [FormBuilderSetting("Caption", Public = true)]
    public LocalizedText? Caption { get; init; }

    /// <summary>An object with a rich text.</summary>
    [FormBuilderSetting("Box", Public = true)]
    public NotesBox Box { get; init; } = new();

    /// <summary>No text.</summary>
    [FormBuilderSetting("Count", Public = true)]
    public int Count { get; init; }
}

/// <summary>The object setting of the test type <c>notes</c>.</summary>
public sealed class NotesBox
{
    /// <summary>A rich text.</summary>
    [FormBuilderSetting("Note")]
    public LocalizedHtml? Note { get; init; }

    /// <summary>No text.</summary>
    [FormBuilderSetting("Size")]
    public int Size { get; init; }
}
