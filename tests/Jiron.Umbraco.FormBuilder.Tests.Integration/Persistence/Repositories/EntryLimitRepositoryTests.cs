using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// What the submission pipeline asks of the repositories (ADR-0088): an entry limit counted inside the unit that would
/// store another entry, over the entries that are no tests and are unverified, submitted or approved; and the newest
/// published version with a public revision, which a submission from an older page is read by.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EntryLimitRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AnEntryLimit_IsCountedInTheUnitThatStoresTheEntry_OverTheEntriesThatCount()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { EntryLimit = 3 });
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { State = EntryState.Unverified, EntryLimit = 3 });
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { IsTest = true });
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { State = EntryState.Draft });
        var spam = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { State = EntryState.Spam });
        await Entries.CreateAsync(NewEntry(other.Id, other.VersionId));
        var counted = await Entries.CountTowardsLimitAsync(form.Id);

        var third = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { EntryLimit = 3 });
        var full = await Entries.CountTowardsLimitAsync(form.Id);
        var lastNumber = (await Forms.GetByIdAsync(form.Id))!.LastEntryNumber;
        var test = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { IsTest = true, EntryLimit = 3 });
        var rejected = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { State = EntryState.Rejected, EntryLimit = 3 });

        Assert.Multiple(async () =>
        {
            Assert.That(counted, Is.EqualTo(2), "a test, a draft, spam and another form's entry do not count");
            Assert.That(third, Is.Not.Null);
            Assert.That(full, Is.EqualTo(3));
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { EntryLimit = 3 }), FormBuilderConflict.EntryLimitReached);
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { State = EntryState.Unverified, EntryLimit = 3 }), FormBuilderConflict.EntryLimitReached);
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(lastNumber + 2), "a refused entry takes no number: only the test and the rejected one did");
            Assert.That(test, Is.Not.Null, "an entry that does not count is stored past the limit");
            Assert.That(rejected, Is.Not.Null);
            Assert.That(spam, Is.Not.Null);
            Assert.That(await Entries.CountTowardsLimitAsync(form.Id), Is.EqualTo(3));
            Assert.That(async () => await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { EntryLimit = 0 }), Throws.ArgumentException);
        });
    }

    [Test]
    public async Task ARevision_IsReadAsTheNewestPublishedVersionWithIt()
    {
        var form = await CreateFormAsync();
        var first = (await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Document("""{"v":1}""", "public 1"), null, null, Editor))!;
        var sameRevision = (await Forms.PublishAsync(form.Id, first.Form.ConcurrencyVersion, Document("""{"v":2}""", "public 1"), null, null, Editor))!;
        var draft = (await Forms.SaveDraftAsync(form.Id, sameRevision.Form.ConcurrencyVersion, Document("""{"v":3}""", "public 2"), null, Editor))!;
        var second = (await Forms.PublishAsync(form.Id, draft.Form.ConcurrencyVersion, Document("""{"v":4}""", "public 2"), null, null, Editor))!;

        var one = await Forms.GetRevisionAsync(form.Id, 1);
        var two = await Forms.GetRevisionAsync(form.Id, 2);

        Assert.Multiple(async () =>
        {
            Assert.That((first.Version.Info.PublicRevision, sameRevision.Version.Info.PublicRevision, second.Version.Info.PublicRevision), Is.EqualTo(((int?)1, (int?)1, (int?)2)));
            Assert.That(one?.Definition, Is.EqualTo("""{"v":2}"""), "the newer of the two versions with revision 1");
            Assert.That(two?.Definition, Is.EqualTo("""{"v":4}"""), "a draft has no revision of its own");
            Assert.That(await Forms.GetRevisionAsync(form.Id, 3), Is.Null);
            Assert.That(await Forms.GetRevisionAsync(form.Id + 1_000, 1), Is.Null, "another form's");
        });
    }
}
