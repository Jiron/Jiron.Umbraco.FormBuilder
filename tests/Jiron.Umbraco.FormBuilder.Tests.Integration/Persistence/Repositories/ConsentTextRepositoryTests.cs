using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The consent wordings visitors were shown (ADR-0009, ADR-0038): stored once under the hash of their culture and text,
/// the first time they were seen kept, and a writer at the same moment answered by the wording already stored.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class ConsentTextRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AConsentWording_IsStoredOnce_UnderTheHashOfItsCultureAndText()
    {
        const string Text = "I agree to the privacy policy. JFB_TEST";

        var hash = await ConsentTexts.AddAsync("en", Text);
        Clock.Advance(TimeSpan.FromDays(1));
        var again = await ConsentTexts.AddAsync("en", Text);
        var invariant = await ConsentTexts.AddAsync(string.Empty, Text);

        Assert.Multiple(async () =>
        {
            Assert.That(hash, Is.EqualTo(ConsentTextHash.For("en", Text)));
            Assert.That(again, Is.EqualTo(hash));
            Assert.That(invariant, Is.Not.EqualTo(hash), "the culture is part of the name");
            Assert.That(await ConsentTexts.GetAsync(hash), Is.EqualTo(new StoredConsentText(hash, "en", Text, Now)), "first seen when first stored");
            Assert.That(await ConsentTexts.GetAsync(invariant), Is.EqualTo(new StoredConsentText(invariant, string.Empty, Text, Now.AddDays(1))));
            Assert.That(await ConsentTexts.GetAsync(new string('0', 64)), Is.Null);
            Assert.That(Count(FormBuilderTables.ConsentText), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AConsentWordingTheDatabaseLetThrough_IsTheAnswer()
    {
        var hash = await ConsentTexts.AddAsync("en", "JFB_TEST wording");
        var rewriter = new CommandRewriter(" WHERE NOT EXISTS", " WHERE 1 = 1 OR NOT EXISTS");

        var again = await WatchedAsync(rewriter, () => ConsentTexts.AddAsync("en", "JFB_TEST wording"));

        Assert.Multiple(() =>
        {
            Assert.That(rewriter.Rewritten, Is.EqualTo(1), "the insert's condition was lifted");
            Assert.That(again, Is.EqualTo(hash), "the primary key refused it, and the stored wording is the same");
            Assert.That(Count(FormBuilderTables.ConsentText), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheLongestCulture_TheColumnHolds_IsStored()
    {
        var culture = new string('c', ConsentTextHash.MaxCultureLength);

        var hash = await ConsentTexts.AddAsync(culture, "JFB_TEST");

        Assert.That((await ConsentTexts.GetAsync(hash))?.Culture, Is.EqualTo(culture));
    }
}
