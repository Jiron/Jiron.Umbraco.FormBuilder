using System.Text.Json.Nodes;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// What a text that names a dictionary item shows in the designer (ADR-0228): each item's text in a language of the site,
/// found through the language's more general ones and Umbraco's fallbacks to the default language, for any user of the
/// Forms section; a key no item has says so, and a request past the limit or with a key that is none is refused.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class DictionaryTextApiTests : ManagementApiTestBase
{
    private const string Texts = "dictionary-text";

    [Test]
    public async Task EachKey_GetsItsItemsTextInTheLanguage_OrItsFallbacks_AndAKeyNoItemHas_SaysSo()
    {
        var languages = GetRequiredService<ILanguageService>();
        var english = (await languages.GetDefaultLanguageAsync())!;
        var german = new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode };
        await languages.CreateAsync(german, Constants.Security.SuperUserKey);
        var dictionary = GetRequiredService<IDictionaryItemService>();
        await dictionary.CreateAsync(new DictionaryItem("Forms.Email") { Translations = [new DictionaryTranslation(english, "Email address"), new DictionaryTranslation(german, "E-Mail-Adresse")] }, Constants.Security.SuperUserKey);
        await dictionary.CreateAsync(new DictionaryItem("Forms.Name") { Translations = [new DictionaryTranslation(english, "Your name")] }, Constants.Security.SuperUserKey);
        await dictionary.CreateAsync(new DictionaryItem("Forms.Empty"), Constants.Security.SuperUserKey);

        var answer = await CallAsync("noAccess", HttpMethod.Get, $"{Texts}?culture=de-DE&key=Forms.Email&key=Forms.Name&key=Forms.Empty&key=Forms.Gone");
        var french = await CallAsync("noAccess", HttpMethod.Get, $"{Texts}?culture=fr-FR&key=Forms.Email");

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(Text(answer.Body!["culture"]), Is.EqualTo("de-DE"));
            Assert.That(
                answer.Body!["texts"]!.AsArray().Select(each => (Text(each!["key"]), each["found"]!.GetValue<bool>(), Text(each["text"]))),
                Is.EqualTo(new[]
                {
                    ("Forms.Email", true, "E-Mail-Adresse"),
                    ("Forms.Name", true, "Your name"),
                    ("Forms.Empty", true, string.Empty),
                    ("Forms.Gone", false, string.Empty),
                }));
            Assert.That((Text(french.Body!["culture"]), Text(french.Body!["texts"]![0]!["text"])), Is.EqualTo(("en-US", "Email address")), "French is no language of the site");
        });
    }

    [Test]
    public async Task NoKeys_AnswerNoTexts_AndTooManyOrOneThatIsNoKey_AreRefused()
    {
        var none = await CallAsync("designer", HttpMethod.Get, Texts);
        var many = await CallAsync("designer", HttpMethod.Get, $"{Texts}?{string.Join('&', Enumerable.Range(0, 101).Select(at => $"key=K{at}"))}");
        var bad = await CallAsync("designer", HttpMethod.Get, $"{Texts}?key=%3Cscript%3E");

        Assert.Multiple(() =>
        {
            Assert.That((none.Outcome, none.Body!["texts"]!.AsArray().Count), Is.EqualTo(("200", 0)));
            Assert.That(many.Outcome, Is.EqualTo("400 invalid_request"), many.ToString());
            Assert.That(bad.Outcome, Is.EqualTo("400 invalid_request"), bad.ToString());
        });
    }

    private static string Text(JsonNode? node) => node?.GetValue<string>() ?? string.Empty;
}
