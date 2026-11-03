using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.MergeTags;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The paths the inspector's tag picker offers (ADR-0173): a place's paths in the draft the designer holds, built-in
/// sources first and the site's after, a field's labelled in the culture asked, held for a form with fields and the
/// site's sources in two places to the committed snapshot <c>docs/api/merge-tag-paths.json</c> (after a deliberate
/// change, run the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so the diff gets
/// reviewed).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class MergeTagPathApiTests : ManagementApiTestBase
{
    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(MergeTagPathApiTests).Assembly, "docs", "api", "merge-tag-paths.json");

    /// <summary>Every test's site adds its own merge-tag sources, as a site's composers do.</summary>
    public MergeTagPathApiTests()
    {
        InMemoryConfiguration[SiteMergeTags.Switch] = "true";
    }

    [Test]
    public async Task APlacesPaths_InTheDraftHeld_AreTheBuiltInSourcesThenTheSites_AsTheReviewedSnapshotHolds()
    {
        var (form, definition) = await FormAsync();

        var visitor = await PathsAsync(form, "visitorText", "en-US", definition);
        var consent = await PathsAsync(form, "consentText", "en-US", definition);

        Assert.Multiple(() =>
        {
            Assert.That(Paths(visitor), Does.Contain("field.name").And.Contain("field.guests.guestName").And.Contain("greeting.text"), "fields, a repeatable group's child, a site's source");
            Assert.That(Paths(visitor).IndexOf("field.name"), Is.LessThan(Paths(visitor).IndexOf("greeting.text")), "built-in sources first");
            Assert.That(Paths(consent).Any(path => path.StartsWith("field.", StringComparison.Ordinal)), Is.False, "a consent's wording takes no answers");
            Assert.That(Entry(visitor, "greeting.text"), Is.EqualTo(("greeting", true, "Greeting")), "the site's source, as it labels its path");
            Assert.That(Entry(visitor, "field.name"), Is.EqualTo(("field", false, "Your name")));
            Assert.That(Entry(visitor, "field.email"), Is.EqualTo(("field", false, "Email address")), "a label that names a dictionary item shows its text");
        });

        var served = new JsonObject { ["visitorText"] = visitor.DeepClone(), ["consentText"] = consent.DeepClone() }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await System.IO.File.WriteAllTextAsync(SnapshotPath, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(served, Is.EqualTo((await System.IO.File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n")), "docs/api/merge-tag-paths.json is reviewed; rerun with JFB_UPDATE_SNAPSHOTS=1 after a deliberate change");
    }

    [Test]
    public async Task AFieldsLabel_IsTheCultureAsked_ItsDictionaryItemsText_AndANonsenseCultureAsksForTheDefaults()
    {
        var (form, definition) = await FormAsync();

        var german = await PathsAsync(form, "visitorText", "de-DE", definition);
        var nonsense = await PathsAsync(form, "visitorText", "<b>", definition);
        var defaults = await PathsAsync(form, "defaultValue", "en-US", definition);

        Assert.Multiple(() =>
        {
            Assert.That((Entry(german, "field.name").Label, Entry(german, "field.email").Label), Is.EqualTo(("Ihr Name", "E-Mail-Adresse")));
            Assert.That(Entry(nonsense, "field.name").Label, Is.EqualTo("Your name"));
            Assert.That(Paths(defaults).Any(path => path.StartsWith("field.", StringComparison.Ordinal)), Is.False, "a default value takes no answers");
            Assert.That(Paths(defaults), Does.Contain("form.name"), "the form's own paths");
        });
    }

    [Test]
    public async Task AVisitorText_IsOfferedOnlyTheAnswersOfTheFieldsBeforeIt()
    {
        var (form, definition) = await FormAsync();
        var fields = definition["pages"]![0]!["groups"]![0]!["fields"]!.AsArray();
        var heading = fields.Single(field => field!["alias"]!.GetValue<string>() == "title")!["key"]!.GetValue<string>();
        var name = fields.Single(field => field!["alias"]!.GetValue<string>() == "name")!["key"]!.GetValue<string>();
        var guests = fields.Single(field => field!["alias"]!.GetValue<string>() == "guests")!;
        var child = guests["fields"]![0]!["key"]!.GetValue<string>();

        var answers = async (string place, string owner) =>
        {
            var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/merge-tag-paths", new { place, culture = "en-US", definition, owner });
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            return Paths(answer.Body!["paths"]!.AsArray()).Where(path => path.StartsWith("field.", StringComparison.Ordinal)).ToList();
        };

        var afterHeading = await answers("visitorText", heading);
        var afterName = await answers("visitorText", name);
        var inGroup = await answers("visitorText", child);
        var unknown = await answers("visitorText", Guid.NewGuid().ToString());
        var defaults = await answers("consentText", heading);
        Assert.Multiple(() =>
        {
            Assert.That(afterHeading, Is.EqualTo(new[] { "field.name", "field.email" }), "the heading comes after the name and the email, before the guests");
            Assert.That(afterName, Is.Empty, "the first field's text recalls nothing");
            Assert.That(inGroup, Does.Contain("field.email").And.Not.Contain("field.guests.guestName"), "nor does a child its own group's");
            Assert.That(unknown, Does.Contain("field.guests.guestName"), "a key the form lacks: after every field");
            Assert.That(defaults, Is.Empty, "a consent's wording takes no answers, wherever it is");
        });
    }

    [Test]
    public async Task AnUnknownPlace_OrADefinitionThatDoesNotRead_IsRefused()
    {
        var (form, definition) = await FormAsync();

        var place = await CallAsync("designer", HttpMethod.Post, $"form/{form}/merge-tag-paths", new { place = "everywhere", culture = "en-US", definition });
        var unreadable = await CallAsync("designer", HttpMethod.Post, $"form/{form}/merge-tag-paths", new { place = "visitorText", culture = "en-US", definition = new { pages = "none" } });

        Assert.That((place.Outcome, unreadable.Outcome.Split(' ')[0]), Is.EqualTo(("400 invalid_request", "400")), $"{place} | {unreadable}");
    }

    [Test]
    public async Task ThePaths_AreServedOnlyToAUserWhoMayDesignTheForm()
    {
        var (form, definition) = await FormAsync();
        var request = new { place = "visitorText", culture = "en-US", definition };

        var viewer = await CallAsync("viewer", HttpMethod.Post, $"form/{form}/merge-tag-paths", request);
        var designer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/merge-tag-paths", request);

        Assert.That((viewer.Outcome, designer.Outcome), Is.EqualTo(("403 forbidden", "200")), viewer.ToString());
    }

    private static List<string> Paths(JsonArray paths) => [.. paths.Select(path => path!["path"]!.GetValue<string>())];

    private static (string Source, bool IsSiteSource, string? Label) Entry(JsonArray paths, string path)
    {
        var found = paths.Single(each => each!["path"]!.GetValue<string>() == path)!;
        return (found["source"]!.GetValue<string>(), found["isSiteSource"]!.GetValue<bool>(), found["label"]?.GetValue<string>());
    }

    private async Task<JsonArray> PathsAsync(Guid form, string place, string culture, JsonNode definition)
    {
        var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/merge-tag-paths", new { place, culture, definition });
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        return answer.Body!["paths"]!.AsArray();
    }

    // A form whose draft the designer changed without saving it: a name, an email whose label names a dictionary item,
    // a repeatable group with a child, and a heading, which holds no answer; German beside English.
    private async Task<(Guid Form, JsonNode Definition)> FormAsync()
    {
        var languages = GetRequiredService<ILanguageService>();
        var english = await languages.GetDefaultLanguageAsync() ?? throw new InvalidOperationException("No default language.");
        var german = (await languages.CreateAsync(new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode }, Constants.Security.SuperUserKey)).Result!;
        await GetRequiredService<IDictionaryItemService>().CreateAsync(
            new DictionaryItem("Forms.Email") { Translations = [new DictionaryTranslation(english, "Email address"), new DictionaryTranslation(german, "E-Mail-Adresse")] },
            Constants.Security.SuperUserKey);

        var forms = GetRequiredService<IFormBuilderFormService>();
        var form = (await forms.CreateAsync(new NewFormRequest("Spring fair"))).Key;
        var draft = await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        Assert.That(draft.Outcome, Is.EqualTo("200"), draft.ToString());
        var definition = draft.Body!["definition"]!.DeepClone();
        definition["pages"]![0]!["groups"]![0]!["fields"] = new JsonArray(
            Field("text", "name", new JsonObject { [string.Empty] = "Your name", ["de-DE"] = "Ihr Name" }),
            Field("email", "email", new JsonObject { [string.Empty] = "#Forms.Email" }),
            Field("heading", "title", null, new JsonObject { ["text"] = new JsonObject { [string.Empty] = "Welcome" } }),
            Repeater());
        return (form, definition);
    }

    private static JsonObject Field(string type, string alias, JsonObject? label, JsonObject? settings = null)
    {
        var field = new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["type"] = type, ["alias"] = alias };
        if (label is not null)
        {
            field["label"] = label;
        }

        if (settings is not null)
        {
            field["settings"] = settings;
        }

        return field;
    }

    private static JsonObject Repeater()
    {
        var group = Field("repeater", "guests", new JsonObject { [string.Empty] = "Guests" });
        group["fields"] = new JsonArray(Field("text", "guestName", new JsonObject { [string.Empty] = "Guest name" }));
        return group;
    }
}
