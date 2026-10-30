using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Testing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// Answer recall from the form endpoint's answer alone (ADR-0068, ADR-0085): the visitor texts that hold tags, the
/// values of their tags but recall's for the page, and the form recall reads, cut down to what the texts need. The
/// server's renderings of those texts for fixed answers are kept with the answer in the reviewed fixture
/// <c>tests/fixtures/public-api/recall.json</c>, which the browser's code renders from the answer in
/// <c>packages/logic/test/recall-endpoint.test.ts</c>, so both runtimes are held to one text. After a deliberate change,
/// run the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the fixture and fails, so the diff gets reviewed. The
/// keys in it are numbered in the order they first appear, and the culture's formats, which come from the platform's
/// culture data, are left out of the comparison.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed partial class PublicFormRecallTests : PublicApiTestBase
{
    private static readonly string FixturePath = RepositoryRoot.Path(typeof(PublicFormRecallTests).Assembly, "tests", "fixtures", "public-api", "recall.json");

    private static readonly FormField FullName = Field("fullName", "text", "Your name", help: "Asked before {{ field.topic }}");
    private static readonly FormField Topic = Field("topic", "dropdown", "Topic") with
    {
        Options = new FieldOptions { Items = [new FieldOption { Value = "sales", Label = new LocalizedText("Sales") }, new FieldOption { Value = "help", Label = new LocalizedText("Support") }] },
    };

    private static readonly FormField Guests = Field("guests", "number", "Guests");
    private static readonly FormField Member = Field("subscriber", "yesno", "Subscriber");
    private static readonly FormField Card = Field("card", "text", "Card") with { Sensitive = true };
    private static readonly FormField Who = Field("who", "text", "Who");
    private static readonly FormField Party = Field("party", "repeater", "Party") with { Fields = [Who] };

    // It names a repeatable group's child alone, and a field after it, which is written as it stands.
    private static readonly FormField Heading = Field(null, "heading", null) with
    {
        Settings = SettingsObject.Parse("""{"text":{"":"{{ field.fullName }} from {{ site.name }} on {{ page.name }}: {{ field.topic }}, {{ field.guests }} guests, subscriber {{ field.subscriber }}, card {{ field.card }}, with {{ field.party.who }}; later {{ field.notes | upper }}"}}"""),
    };

    private static readonly FormField Paragraph = Field(null, "paragraph", null) with
    {
        Settings = SettingsObject.Parse("""{"html":{"":"<p>Hello {{ field.fullName | upper }}, this is <b>{{ form.name }}</b>.</p>"}}"""),
    };

    private static readonly FormField Notes = Field("notes", "textarea", "Anything else, {{ field.fullName }}?", placeholder: "{{ member.name }} writes here");

    [Test]
    public async Task TheAnswer_GivesTheBrowserEveryTextThatRenders_AndWhatItRendersThemWith_AsTheServerRendersThem()
    {
        var (site, page) = await ContentAsync();
        var key = await PublishAsync(
            "Contact us",
            change: definition => definition with
            {
                Pages = [definition.Pages[0] with
                {
                    Groups = [definition.Pages[0].Groups[0] with { Title = new LocalizedText("Welcome to {{ site.name }}"), Description = new LocalizedHtml("<p>{{ field.fullName }}</p>") }],
                }],
            },
            fields: [FullName, Topic, Guests, Member, Card, Party, Heading, Paragraph, Notes]);
        using var response = await GetAsync($"{Root}/forms/{key}?culture=en-US&contentKey={page}");
        var answer = await JsonAsync(response);
        var published = (await Forms.GetPublishedAsync(key))!;
        MergeTagAnswer[] given = [Answer(FullName, "Anna"), Answer(Topic, "help"), Answer(Guests, "1234.5"), Answer(Member, "yes"), Answer(Card, "4111")];
        MergeTagAnswer[] rows = [new(Who.Key, [new FieldValue("Ben")], row: 0), new(Who.Key, [new FieldValue("Cleo")], row: 1)];
        var answers = new MergeTagAnswers([.. given, .. rows]);

        // The server's rendering of a visitor text of the answer's form, as a page renders it for these answers.
        async Task<string> ServerAsync(Guid owner, string name, MergeTagContext context)
        {
            var scope = new MergeTagScope
            {
                Form = published.Definition,
                FormName = published.Form.Name,
                FormAlias = published.Form.Alias,
                Culture = "en-US",
                Site = site,
                Page = page,
                TextOwner = owner,
                Answers = answers,
            };
            var rendering = await GetRequiredService<IFormBuilderMergeTagService>().RenderAsync(TextOf(answer["form"]!, owner, name), MergeTagPlace.VisitorText, context, scope);
            return rendering.Text;
        }

        var rendered = new JsonArray();
        foreach (var text in answer["mergeTags"]!["texts"]!.AsArray())
        {
            var owner = Guid.Parse(text!["owner"]!.GetValue<string>());
            var name = text["text"]!.GetValue<string>();
            var server = await ServerAsync(owner, name, text["context"]!.GetValue<string>() == "html" ? MergeTagContext.Html : MergeTagContext.Text);
            rendered.Add(new JsonObject { ["owner"] = owner.ToString(), ["text"] = name, ["server"] = server });
        }

        // A tag of a field the text holds or comes before, and one of a source visitor texts do not take, are written as
        // they stand, so a text with only these renders as it is written and the browser needs nothing for it.
        var group = published.Definition.Pages[0].Groups[0].Key;
        var asWritten = new Dictionary<string, string>();
        foreach (var (owner, name, context) in new[] { (group, "description", MergeTagContext.Html), (FullName.Key, "help", MergeTagContext.Text), (Notes.Key, "placeholder", MergeTagContext.Text) })
        {
            asWritten[name] = await ServerAsync(owner, name, context) == TextOf(answer["form"]!, owner, name) ? "as written" : "rendered";
        }

        var fixture = new JsonObject
        {
            ["about"] = "The form endpoint's answer for a page and the server's renderings of its visitor texts for these answers (ADR-0085); written by PublicFormRecallTests.",
            ["answer"] = new JsonObject
            {
                ["form"] = answer["form"]!.DeepClone(),
                ["formats"] = answer["formats"]!.DeepClone(),
                ["words"] = answer["words"]!.DeepClone(),
                ["mergeTags"] = answer["mergeTags"]!.DeepClone(),
            },
            ["answers"] = new JsonObject
            {
                ["fields"] = new JsonObject(given.Select(each => KeyValuePair.Create<string, JsonNode?>(each.Field.ToString(), Values(each)))),
                ["rows"] = new JsonObject
                {
                    [Party.Key.ToString()] = new JsonArray([.. rows.Select(each => (JsonNode)new JsonObject { [each.Field.ToString()] = Values(each) })]),
                },
            },
            ["hidden"] = new JsonArray(),
            ["rendered"] = rendered,
        };
        var written = Numbered(fixture.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n");
        var masked = new string((char)0x2022, 3);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Select(each => $"{each!["text"]}: {each["server"]}"), Is.EqualTo(new[]
            {
                "title: Welcome to Shop",
                $"settings.text: Anna from Shop on Contact: Support, 1,234.5 guests, subscriber Yes, card {masked}, with Ben, Cleo; later {{{{ field.notes | upper }}}}",
                "settings.html: <p>Hello ANNA, this is <b>Contact us</b>.</p>",
                "label: Anything else, Anna?",
            }), "the texts that render, in the form's order");
            Assert.That(asWritten, Is.EqualTo(new Dictionary<string, string> { ["description"] = "as written", ["help"] = "as written", ["placeholder"] = "as written" }));
            Assert.That(answer["mergeTags"]!["values"]!.ToJsonString(), Is.EqualTo("""{"html":{"form.name":"Contact us"},"text":{"site.name":"Shop","page.name":"Contact"}}"""));
            Assert.That(answer["consents"]!.ToJsonString(), Is.EqualTo("{}"));
            Assert.That(answer["fields"]![Topic.Key.ToString()]!.ToJsonString(), Is.EqualTo("""{"kind":"choice","canBeRequired":true,"builtIn":true}"""), "on a form whose texts hold tags as on one without");
            Assert.That(answer["fields"]![Member.Key.ToString()]!.ToJsonString(), Is.EqualTo("""{"kind":"choice","canBeRequired":true,"builtIn":true}"""), "a yes or no is a choice");
            Assert.That(answer["fields"]![Party.Key.ToString()]!.ToJsonString(), Is.EqualTo("""{"canBeRequired":false,"builtIn":true}"""));
            Assert.That(answer["fields"]![Who.Key.ToString()]!.ToJsonString(), Is.EqualTo("""{"kind":"text","canBeRequired":true,"builtIn":true}"""), "a child field");
            Assert.That(answer["fields"]![Heading.Key.ToString()]!.ToJsonString(), Is.EqualTo("""{"canBeRequired":false,"builtIn":true}"""));
            Assert.That(answer["words"]!.ToJsonString(), Is.EqualTo("""{"yes":"Yes","no":"No"}"""));
            Assert.That(answer["formats"]!["number"]!["group"]!.GetValue<string>(), Is.EqualTo(","));
            Assert.That(answer["mergeTags"]!.AsObject().Select(property => property.Key), Is.EqualTo(new[] { "texts", "values", "recall" }));
        });

        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FixturePath)!);
            await System.IO.File.WriteAllTextAsync(FixturePath, written);
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {FixturePath}. Review the diff, run the logic package's tests, commit it, and run again without the variable.");
        }

        Assert.That(System.IO.File.Exists(FixturePath), Is.True, $"the reviewed fixture {FixturePath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That(WithoutFormats(await System.IO.File.ReadAllTextAsync(FixturePath)), Is.EqualTo(WithoutFormats(written)), "the answer or the server's renderings changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task TheRecallForm_HoldsOnlyWhatTheTextsNeed_ByTheRulesBothRuntimesHold()
    {
        var key = await PublishAsync("Contact us", fields: [FullName, Topic, Guests, Member, Card, Party, Heading, Notes]);
        using var response = await GetAsync($"{Root}/forms/{key}?culture=en-US");
        var recall = (await JsonAsync(response))["mergeTags"]!["recall"]!;
        var fields = recall["pages"]![0]!["groups"]![0]!["fields"]!.AsArray().ToDictionary(field => field!["id"]!.GetValue<string>(), field => field!.AsObject());
        var types = GetRequiredService<FieldTypeRegistry>();

        Assert.Multiple(() =>
        {
            Assert.That(fields.Keys, Is.EqualTo(new[] { FullName, Topic, Guests, Member, Card, Party, Heading, Notes }.Select(field => field.Key.ToString())), "the owners and the recalled fields, in the form's order");
            Assert.That(fields[Heading.Key.ToString()].Select(property => property.Key), Is.EqualTo(new[] { "id", "type", "holdsValue" }), "an owner that is not recalled only has its place");
            Assert.That(fields[Party.Key.ToString()]["alias"]!.GetValue<string>(), Is.EqualTo("party"), "a tag names a child through its repeatable group, so the group comes whole");
            Assert.That(fields[Party.Key.ToString()]["fields"]![0]!["alias"]!.GetValue<string>(), Is.EqualTo("who"));
            Assert.That(fields[Notes.Key.ToString()]["alias"]!.GetValue<string>(), Is.EqualTo("notes"), "a field after the text is given too, so the browser refuses it as the server does");
            Assert.That(fields[Topic.Key.ToString()]["options"]!.ToJsonString(), Is.EqualTo("""[{"value":"sales","label":"Sales"},{"value":"help","label":"Support"}]"""));
            Assert.That(fields[Card.Key.ToString()]["recall"]!.GetValue<string>(), Is.EqualTo("masked"));
            Assert.That(fields[Guests.Key.ToString()]["kind"]!.GetValue<string>(), Is.EqualTo("number"));
            Assert.That(fields[Member.Key.ToString()]["builtIn"]!.GetValue<bool>(), Is.True);
            Assert.That(fields[FullName.Key.ToString()].ContainsKey("recall"), Is.False, "a field that is neither sensitive nor kept on the server shows its answers");
            Assert.That(recall.ToJsonString(), Does.Not.Contain("sensitive"), "the projection names no field as sensitive");
            foreach (var (id, field) in fields.Where(pair => pair.Value.ContainsKey("alias")))
            {
                var traits = types.Find(field["type"]!.GetValue<string>())!;
                Assert.That(field["holdsValue"]!.GetValue<bool>(), Is.EqualTo(traits.HoldsValue), id);
                Assert.That(field.ContainsKey("parts"), Is.EqualTo(traits.Storage == FieldStorageKind.Json), id);
            }
        });
    }

    // The text a visitor text is: the property its name says of the element its owner names, in the visitor's form.
    private static string TextOf(JsonNode form, Guid owner, string name)
    {
        var element = Elements(form).Single(node => node["key"]!.GetValue<string>() == owner.ToString());
        return name.Split('.').Aggregate(element, (node, part) => node[part]!).GetValue<string>();
    }

    private static IEnumerable<JsonNode> Elements(JsonNode form)
    {
        foreach (var page in form["pages"]!.AsArray())
        {
            yield return page!;
            foreach (var group in page!["groups"]!.AsArray())
            {
                yield return group!;
                foreach (var field in group!["fields"]!.AsArray().SelectMany(field => field!["fields"]!.AsArray().Prepend(field)))
                {
                    yield return field!;
                }
            }
        }
    }

    // Each key numbered in the order it first appears, so the fixture stays the same from run to run.
    private static string Numbered(string json)
    {
        var numbers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return Keys().Replace(json, match => numbers.TryGetValue(match.Value, out var number)
            ? number
            : numbers[match.Value] = $"00000000-0000-4000-8000-{numbers.Count + 1:D12}");
    }

    // The fixture without the culture's formats, which come from the platform's culture data.
    private static string WithoutFormats(string json)
    {
        var node = JsonNode.Parse(json)!;
        node["answer"]!.AsObject().Remove("formats");
        return node.ToJsonString();
    }

    private static MergeTagAnswer Answer(FormField field, string value) => new(field.Key, [new FieldValue(value)]);

    // An answer as the browser's code takes it.
    private static JsonObject Values(MergeTagAnswer answer) => new()
    {
        ["values"] = new JsonArray([.. answer.Values.Select(value => (JsonNode)new JsonObject { ["text"] = value.Text })]),
    };

    private static FormField Field(string? alias, string type, string? label, string? help = null, string? placeholder = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = label is null ? null : new LocalizedText(label),
        Help = help is null ? null : new LocalizedText(help),
        Placeholder = placeholder is null ? null : new LocalizedText(placeholder),
    };

    // A site "Shop" with the page "Contact".
    private async Task<(Guid Site, Guid Page)> ContentAsync()
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        type.AllowedContentTypes = [new ContentTypeSort(type.Key, 0, type.Alias)];
        Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        var contents = GetRequiredService<IContentService>();
        var shop = new Content("Shop", -1, type);
        contents.Save(shop);
        contents.Publish(shop, ["*"]);
        var contact = new Content("Contact", shop, type);
        contents.Save(contact);
        contents.Publish(contact, ["*"]);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return (shop.Key, contact.Key);
    }

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex Keys();
}
