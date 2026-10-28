using System.Net;
using System.Net.Http.Headers;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The form endpoint of the public API (section 4.6, ADR-0084) on a real site: a published form served to headless
/// sites, by its key or its alias in any letter case, with its texts in the culture, dictionary items resolved and rich
/// text sanitised, <c>no-cache</c> with an <c>ETag</c> that follows the answer, and the same <c>404 form_not_found</c>
/// for every form it does not serve.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormApiTests : PublicApiTestBase
{
    [Test]
    public async Task AHeadlessForm_IsServed_ByItsKeyAndItsAliasInAnyLetterCase()
    {
        var key = await PublishAsync();

        using var byKey = await GetAsync($"{Root}/forms/{key}");
        using var byAlias = await GetAsync($"{Root}/forms/contactUs");
        using var byOtherCase = await GetAsync($"{Root}/forms/CONTACTUS");
        var json = await JsonAsync(byKey);

        Assert.Multiple(async () =>
        {
            Assert.That(byKey.StatusCode, Is.EqualTo(HttpStatusCode.OK), await byKey.Content.ReadAsStringAsync());
            Assert.That(byKey.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(Header(byKey, "Cache-Control"), Is.EqualTo("no-cache"));
            Assert.That(byKey.Headers.ETag?.Tag, Does.Match("^\"[0-9a-f]{32}\"$"));
            Assert.That(Header(byKey, "Vary"), Does.Contain("Origin"));
            Assert.That(json.AsObject().Select(property => property.Key), Is.EqualTo(new[] { "alias", "revision", "culture", "timeZone", "open", "form" }));
            Assert.That((json["alias"]!.GetValue<string>(), json["revision"]!.GetValue<int>(), json["culture"]!.GetValue<string>(), json["timeZone"]!.GetValue<string>(), json["open"]!.GetValue<bool>()), Is.EqualTo(("contactUs", 1, "en-US", "UTC", true)));
            Assert.That(json["form"]!["key"]!.GetValue<Guid>(), Is.EqualTo(key));
            Assert.That(json["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!["label"]!.GetValue<string>(), Is.EqualTo("Name"), "a text is the culture's string");
            Assert.That(byAlias.Headers.ETag, Is.EqualTo(byKey.Headers.ETag));
            Assert.That(byOtherCase.Headers.ETag, Is.EqualTo(byKey.Headers.ETag));
        });
    }

    [Test]
    public async Task EveryFormItDoesNotServe_IsTheSameFormNotFound()
    {
        var unpublished = (await Forms.CreateAsync(new Core.Forms.NewFormRequest("Draft only", UserKey: Editor))).Key;
        var notHeadless = await PublishAsync("Not headless", headless: false);
        var trashed = await PublishAsync("Trashed");
        await Forms.TrashAsync(trashed, (await Forms.GetAsync(trashed))!.ConcurrencyVersion, Editor);

        var answers = new Dictionary<string, string>();
        foreach (var (what, path) in new[]
        {
            ("a key of no form", $"{Root}/forms/{Guid.NewGuid()}"),
            ("an alias of no form", $"{Root}/forms/nothingHere"),
            ("neither a key nor an alias", $"{Root}/forms/not-a-form!"),
            ("a key without hyphens", $"{Root}/forms/{notHeadless:N}"),
            ("an unpublished form", $"{Root}/forms/{unpublished}"),
            ("a form not served headless", $"{Root}/forms/{notHeadless}"),
            ("a form in the trash", $"{Root}/forms/{trashed}"),
            ("a form in the trash, by its alias", $"{Root}/forms/trashed"),
        })
        {
            using var response = await GetAsync(path);
            answers[what] = $"{await RefusalAsync(response)}, {Header(response, "Cache-Control")}";
        }

        Assert.That(answers.Values, Is.All.EqualTo("404 form_not_found, no-store"), string.Join("\n", answers));
    }

    [Test]
    public async Task TheTexts_AreTheCulturesOwn_WithTheDictionarysItems_AndRichTextSanitised()
    {
        var languages = GetRequiredService<ILanguageService>();
        var english = (await languages.GetDefaultLanguageAsync())!;
        var german = new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode };
        await languages.CreateAsync(german, Constants.Security.SuperUserKey);
        await GetRequiredService<IDictionaryItemService>().CreateAsync(
            new DictionaryItem("Forms.Help") { Translations = [new DictionaryTranslation(english, "We answer within a day."), new DictionaryTranslation(german, "Wir antworten binnen eines Tages.")] },
            Constants.Security.SuperUserKey);
        var description = new LocalizedHtml("""<p class="x" onclick="steal()">Hello <script>steal()</script><a href="javascript:steal()">bad</a> <a href="/privacy" target="_blank">privacy</a></p><img src="x.png">""");
        var key = await PublishAsync(
            change: definition => definition with { Pages = [definition.Pages[0] with { Description = description }] },
            fields: NameField with { Help = new LocalizedText("#Forms.Help") });

        using var inGerman = await GetAsync($"{Root}/forms/{key}?culture=DE-de");
        using var inFrench = await GetAsync($"{Root}/forms/{key}?culture=fr-FR");
        using var unreadable = await GetAsync($"{Root}/forms/{key}?culture=not%20a%20culture");
        var german1 = await JsonAsync(inGerman);
        var french = await JsonAsync(inFrench);
        var field = german1["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!;

        Assert.Multiple(async () =>
        {
            Assert.That(german1["culture"]!.GetValue<string>(), Is.EqualTo("de-DE"), "the site's language, as Umbraco spells it");
            Assert.That(field["label"]!.GetValue<string>(), Is.EqualTo("Name (de)"), "de-DE falls back to de");
            Assert.That(field["help"]!.GetValue<string>(), Is.EqualTo("Wir antworten binnen eines Tages."));
            Assert.That(german1["form"]!["pages"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("""<p>Hello <a>bad</a> <a href="/privacy" target="_blank" rel="noopener noreferrer">privacy</a></p>"""));
            Assert.That(french["culture"]!.GetValue<string>(), Is.EqualTo("en-US"), "a culture the site lacks is answered in the default language");
            Assert.That(french["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!["help"]!.GetValue<string>(), Is.EqualTo("We answer within a day."));
            Assert.That(inFrench.Headers.ETag, Is.Not.EqualTo(inGerman.Headers.ETag));
            Assert.That(await RefusalAsync(unreadable), Is.EqualTo("400 invalid_request"));
        });
    }

    [Test]
    public async Task TheETag_FollowsTheAnswer_AndAClientThatHoldsItGetsNotModified()
    {
        var key = await PublishAsync();
        using var first = await GetAsync($"{Root}/forms/{key}");
        var etag = first.Headers.ETag!;

        using var held = await GetAsync($"{Root}/forms/{key}", request => request.Headers.IfNoneMatch.Add(etag));
        using var heldWeakly = await GetAsync($"{Root}/forms/{key}", request => request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag, isWeak: true)));
        using var heldOther = await GetAsync($"{Root}/forms/{key}", request => request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"0123\"")));
        await SaveAndPublishAsync(key, definition => WithFields(definition, NameField with { Label = new LocalizedText("Full name") }));
        using var republished = await GetAsync($"{Root}/forms/{key}", request => request.Headers.IfNoneMatch.Add(etag));

        Assert.Multiple(async () =>
        {
            Assert.That(held.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
            Assert.That(held.Headers.ETag, Is.EqualTo(etag));
            Assert.That(Header(held, "Cache-Control"), Is.EqualTo("no-cache"));
            Assert.That(await held.Content.ReadAsByteArrayAsync(), Is.Empty);
            Assert.That(heldWeakly.StatusCode, Is.EqualTo(HttpStatusCode.NotModified), "If-None-Match compares weakly");
            Assert.That(heldOther.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(republished.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a publish reaches the client at once");
            Assert.That(republished.Headers.ETag, Is.Not.EqualTo(etag));
            Assert.That((await JsonAsync(republished))["revision"]!.GetValue<int>(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AFormIsOpen_FromItsOpeningTime_UntilItsClosingTime()
    {
        // A definition's instants have whole seconds.
        var now = new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        FormDefinition Window(FormDefinition definition, DateTime? opens, DateTime? closes) =>
            definition with { Settings = definition.Settings with { Availability = definition.Settings.Availability with { OpensAt = opens, ClosesAt = closes } } };
        var open = await PublishAsync("Open", change: d => Window(d, now.AddDays(-1), now.AddDays(1)));
        var early = await PublishAsync("Early", change: d => Window(d, now.AddDays(1), null));
        var closed = await PublishAsync("Closed", change: d => Window(d, null, now.AddMinutes(-1)));

        var answers = new List<bool>();
        foreach (var key in new[] { open, early, closed })
        {
            using var response = await GetAsync($"{Root}/forms/{key}");
            answers.Add((await JsonAsync(response))["open"]!.GetValue<bool>());
        }

        Assert.That(answers, Is.EqualTo(new[] { true, false, false }));
    }
}
