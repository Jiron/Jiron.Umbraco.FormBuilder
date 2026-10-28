using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A consent's wording on the form endpoint (ADR-0009, ADR-0070, ADR-0085): the visitor's form shows it rendered,
/// sanitised and in the answer's culture, and the answer gives the hash of what it shows, which the submission posts,
/// so a change of the dictionary item it names changes both.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormConsentTests : PublicApiTestBase
{
    private static readonly FormField Consent = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000085"),
        Type = "consent",
        Alias = "privacy",
        Required = true,
        Settings = SettingsObject.Parse("""{"text":{"":"#Forms.Consent"}}"""),
    };

    [Test]
    public async Task TheHash_IsTheOneOfTheWordingTheFormShows_AndChangesWithTheDictionary()
    {
        var dictionary = GetRequiredService<IDictionaryItemService>();
        var english = (await GetRequiredService<ILanguageService>().GetDefaultLanguageAsync())!;
        var item = new DictionaryItem("Forms.Consent")
        {
            Translations = [new DictionaryTranslation(english, """I agree to the <a href="/privacy" onclick="steal()">notice</a> of {{ form.name }}.""")],
        };
        Assert.That((await dictionary.CreateAsync(item, Constants.Security.SuperUserKey)).Success, Is.True);
        var key = await PublishAsync("Contact us", fields: [NameField, Consent]);

        var before = await ConsentAsync(key);
        var changed = (await dictionary.GetAsync("Forms.Consent"))!;
        changed.Translations.Single(translation => translation.LanguageIsoCode == english.IsoCode).Value = "I agree to the <b>new</b> notice.";
        Assert.That((await dictionary.UpdateAsync(changed, Constants.Security.SuperUserKey)).Success, Is.True);
        var after = await ConsentAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(before.Shown, Is.EqualTo("""I agree to the <a href="/privacy">notice</a> of Contact us."""), "rendered and sanitised");
            Assert.That(before.Hash, Is.EqualTo(ConsentTextHash.For("en-US", before.Shown)));
            Assert.That(after.Shown, Is.EqualTo("I agree to the <b>new</b> notice."));
            Assert.That(after.Hash, Is.EqualTo(ConsentTextHash.For("en-US", after.Shown)));
            Assert.That(after.Hash, Is.Not.EqualTo(before.Hash));
            Assert.That(before.Consents, Is.EqualTo(new[] { Consent.Key.ToString() }), "only the consent has a hash");
        });
    }

    // The consent's text in the visitor's form, its hash in the answer, and the fields that have a hash.
    private async Task<(string Shown, string Hash, string[] Consents)> ConsentAsync(Guid key)
    {
        using var response = await GetAsync($"{Root}/forms/{key}?culture=en-US");
        var answer = await JsonAsync(response);
        var field = answer["form"]!["pages"]![0]!["groups"]![0]!["fields"]!.AsArray().Single(each => each!["key"]!.GetValue<string>() == Consent.Key.ToString())!;
        var consents = answer["consents"]!.AsObject();
        return (field["settings"]!["text"]!.GetValue<string>(), consents[Consent.Key.ToString()]!.GetValue<string>(), [.. consents.Select(pair => pair.Key)]);
    }
}
