using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Translating;

/// <summary>
/// Translate with AI (ROADMAP 15.3, ADR-0018): the saved draft's texts that lack one of the site's other languages go to
/// the AI and come back, each beside its path, for the designer to apply. A dictionary item stays out, merge tags and
/// HTML stay as they are, and a language that is not the site's other language is refused.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TranslationApiTests : AITestBase
{
    [SetUp]
    public Task AddGermanAsync() => GetRequiredService<ILanguageService>().CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);

    [Test]
    public async Task TheTextsThatLackTheLanguage_ComeBackTranslated_KeepingMergeTagsAndHtml_WithoutDictionaryItems()
    {
        var key = await TheFormAsync();

        var answer = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/translation", new { culture = "de-de" });

        Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.ToString());
        var texts = answer.Body!["texts"]!.AsArray().Select(text => (Path: Value(text, "path"), Source: Value(text, "source"), Text: Value(text, "text"))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(answer.Body!["culture"]!.GetValue<string>(), Is.EqualTo("de-DE"), "the language as the site names it");
            Assert.That(texts, Has.Some.EqualTo(("$.pages[0].groups[0].fields[0].label", "Your name", "Your name (de)")));
            Assert.That(texts, Has.Some.EqualTo(("$.pages[0].groups[0].fields[0].help", "As in your passport", "As in your passport (de)")));
            Assert.That(texts.Single(text => text.Path.EndsWith(".settings.html", StringComparison.Ordinal)).Text, Is.EqualTo("<p>Thanks, {{ name }}. (de)</p>"));
            Assert.That(texts.Select(text => text.Source), Has.None.EqualTo("#contact.email"), "the dictionary translates its own items");
            Assert.That(texts.Select(text => text.Source), Has.None.EqualTo("Phone"), "already in German");
            Assert.That(answer.Body!["left"]!.GetValue<int>(), Is.Zero);
            Assert.That(Journal.Entries, Has.Some.Contains("into German (Germany) (de-DE)"));
        });

        var again = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/translation", new { culture = "de-DE", replaceExisting = true });
        Assert.That(again.Body!["texts"]!.AsArray().Select(text => Value(text, "text")), Has.Some.EqualTo("Phone (de)"), "translated again when the editor asks");
    }

    [Test]
    public async Task AFormWhoseTextsAreAllInTheLanguage_HasNothingToTranslate_AndAsksNothing()
    {
        var key = await FormWithAsync(FieldOf("text", "phone", new LocalizedText([new("", "Phone"), new("de-DE", "Telefon")])));

        var answer = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/translation", new { culture = "de-DE" });

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer.Body!["texts"]!.AsArray(), Is.Empty);
            Assert.That(Journal.Entries, Has.None.StartsWith("request:"));
        });
    }

    [TestCase("fr-FR")]
    [TestCase("en-US")]
    [TestCase("not a culture")]
    public async Task ALanguageThatIsNotOneOfTheSitesOthers_IsRefused(string culture)
    {
        var key = await TheFormAsync();

        var answer = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/translation", new { culture });

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("400 ai_language_invalid"), answer.ToString());
            Assert.That(Journal.Entries, Has.None.StartsWith("request:"));
        });
    }

    [Test]
    public async Task OnlyAUserWhoMayDesignTheForm_MayTranslateIt()
    {
        var key = await TheFormAsync();
        var folder = await FolderAsync("Restricted");
        var hidden = await FormAsync("Hidden", folder);
        await FillAsync(hidden);
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);

        Assert.Multiple(async () =>
        {
            Assert.That((await CallAsync("viewer", HttpMethod.Post, $"ai/form/{key}/translation", new { culture = "de-DE" })).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That((await CallAsync("outsider", HttpMethod.Post, $"ai/form/{key}/translation", new { culture = "de-DE" })).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That((await CallAsync("designer", HttpMethod.Post, $"ai/form/{hidden}/translation", new { culture = "de-DE" })).Status, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await CallAsync("designer", HttpMethod.Post, $"ai/form/{Guid.NewGuid()}/translation", new { culture = "de-DE" })).Status, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(Journal.Entries, Has.None.StartsWith("request:"));
        });
    }

    private Task<Guid> TheFormAsync() => FormWithAsync(
        FieldOf("text", "name", new LocalizedText("Your name"), "As in your passport"),
        FieldOf("email", "email", new LocalizedText("#contact.email")),
        FieldOf("text", "phone", new LocalizedText([new("", "Phone"), new("de-DE", "Telefon")])),
        new FormField { Key = Guid.NewGuid(), Type = "paragraph", Settings = SettingsObject.Parse("""{"html":{"":"<p>Thanks, {{ name }}.</p>"}}""") });

    private static string Value(JsonNode? node, string name) => node![name]!.GetValue<string>();
}
