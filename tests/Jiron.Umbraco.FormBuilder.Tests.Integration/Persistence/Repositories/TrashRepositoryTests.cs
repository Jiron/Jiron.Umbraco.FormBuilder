using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The statements of the trash and the permanent delete (ADR-0047): the forms of a folder's tree go to the trash with
/// its key and come back with it, a trashed form is held at its version for a batch, and a form's entries, files,
/// unsent mail and references go in batches.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class TrashRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task TheFormsOfAFoldersTree_GoToTheTrashWithItsKey_AndComeBackWithIt()
    {
        var folder = await CreateFolderAsync("Folder");
        var below = await CreateFolderAsync("Below", folder.Id);
        var other = await CreateFolderAsync("Other");
        var beta = await CreateFormAsync("beta", folder.Id, "Beta");
        var alpha = await CreateFormAsync("alpha", below.Id, "Alpha");
        var alone = await CreateFormAsync("alone", folder.Id, "Alone");
        var elsewhere = await CreateFormAsync("elsewhere", other.Id);
        var atRoot = await CreateFormAsync("atRoot");
        var trashedAlone = (await Forms.TrashAsync(alone.Id, alone.ConcurrencyVersion, Editor))!;
        Clock.Advance(TimeSpan.FromMinutes(1));

        var tree = (await Folders.TrashAsync(folder.Id, Editor))!;
        var ids = tree.Folders.Select(row => row.Id).ToList();
        var trashed = await Forms.TrashInFoldersAsync(ids, tree.TrashKey!.Value, Editor);
        var listed = await Forms.ListInFoldersAsync(ids);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var back = await Folders.RestoreAsync(folder.Id, folder.ParentId, Editor);
        var restored = await Forms.RestoreInFoldersAsync(ids, tree.TrashKey.Value, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.Select(form => form.Id), Is.EqualTo(new[] { alpha.Id, beta.Id }), "the forms outside the trash, by name");
            Assert.That(trashed.Select(form => (form.TrashKey, form.TrashedUtc, form.UpdatedUtc)), Has.All.EqualTo((tree.TrashKey, (DateTimeOffset?)Now.AddMinutes(1), Now.AddMinutes(1))));
            Assert.That(trashed.Select(form => form.ConcurrencyVersion), Is.EqualTo(new[] { alpha.ConcurrencyVersion + 1, beta.ConcurrencyVersion + 1 }));
            Assert.That(trashed.Select(form => form.FolderId), Is.EqualTo(new int?[] { below.Id, folder.Id }), "the trash keeps them in their folders");
            Assert.That(listed.Select(form => form.Id), Is.EqualTo(new[] { alone.Id, alpha.Id, beta.Id }), "the forms in the folders, in the trash or not, by name");
            Assert.That(back!.TrashKey, Is.EqualTo(tree.TrashKey));
            Assert.That(restored.Select(form => form.Id), Is.EqualTo(new[] { alpha.Id, beta.Id }), "what went with the folder");
            Assert.That(restored.Select(form => (form.TrashKey, form.TrashedUtc, form.UpdatedUtc)), Has.All.EqualTo(((Guid?)null, (DateTimeOffset?)null, Now.AddMinutes(2))));
            Assert.That(restored.Select(form => form.ConcurrencyVersion), Is.EqualTo(new[] { alpha.ConcurrencyVersion + 2, beta.ConcurrencyVersion + 2 }));
            Assert.That(restored, Is.EqualTo(new[] { await Forms.GetByIdAsync(alpha.Id), await Forms.GetByIdAsync(beta.Id) }), "as stored");
            Assert.That(await Forms.GetByIdAsync(alone.Id), Is.EqualTo(trashedAlone), "a form trashed on its own stays in the trash with its own key");
            Assert.That(await Forms.GetByIdAsync(elsewhere.Id), Is.EqualTo(elsewhere));
            Assert.That(await Forms.GetByIdAsync(atRoot.Id), Is.EqualTo(atRoot));
        });
    }

    [Test]
    public async Task TheFormsOfNoFolders_OrWithAnotherKey_StayAsTheyAre()
    {
        var folder = await CreateFolderAsync();
        var form = await CreateFormAsync(folderId: folder.Id);
        var trashed = (await Forms.TrashAsync(form.Id, form.ConcurrencyVersion, Editor))!;

        Assert.Multiple(async () =>
        {
            Assert.That(await Forms.TrashInFoldersAsync([], Guid.NewGuid(), Editor), Is.Empty);
            Assert.That(await Forms.RestoreInFoldersAsync([], Guid.NewGuid(), Editor), Is.Empty);
            Assert.That(await Forms.ListInFoldersAsync([]), Is.Empty);
            Assert.That(await Forms.RestoreInFoldersAsync([folder.Id], Guid.NewGuid(), Editor), Is.Empty, "a key the form does not carry");
            Assert.That(await Forms.TrashInFoldersAsync([folder.Id], Guid.NewGuid(), Editor), Is.Empty, "a form in the trash already");
            Assert.That(await Forms.GetByIdAsync(form.Id), Is.EqualTo(trashed));
        });
    }

    [Test]
    public async Task AHold_KeepsAFormInTheTrashAsItIs_AndRefusesOneThatChangedOrLeftIt()
    {
        var form = await CreateFormAsync();
        var trashed = (await Forms.TrashAsync(form.Id, form.ConcurrencyVersion, Editor))!;
        var live = await CreateFormAsync();
        Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Multiple(async () =>
        {
            Assert.That(await Forms.HoldTrashedAsync(trashed.Id, trashed.ConcurrencyVersion), Is.EqualTo(trashed), "held, and changed in nothing");
            Assert.That(async () => await Forms.HoldTrashedAsync(trashed.Id, trashed.ConcurrencyVersion - 1), Throws.InstanceOf<FormBuilderConcurrencyException>());
            FormRepositoryTests.AssertConflict(() => Forms.HoldTrashedAsync(live.Id, live.ConcurrencyVersion), FormBuilderConflict.NotTrashed);
            Assert.That(await Forms.HoldTrashedAsync(-1, 1), Is.Null);
        });
    }

    [Test]
    public async Task AFormsEntries_AreListedLowestNumberFirst_AndDeletedWithTheirFiles()
    {
        var form = await CreateTestFormAsync("upload");
        var cv = await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id, "cv.pdf"));
        var first = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["upload"], "cv.pdf", FileId: cv!.Id)) with { FileIds = [cv.Id] });
        var second = await AddEntryAsync(form.Id, form.VersionId);
        var third = await AddEntryAsync(form.Id, form.VersionId);
        var waiting = await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id, "waiting.pdf"));
        var later = await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id, "later.pdf"));
        var other = await CreateTestFormAsync();
        var otherEntry = await AddEntryAsync(other.Id, other.VersionId);
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(other.Id));

        var batch = await Entries.ListIdsAsync(form.Id, 2);
        var files = await Entries.ListFilesAsync(batch);
        var uploads = await Entries.ListUploadsAsync(form.Id, 10);
        var deleted = await Entries.DeleteAsync([.. batch, -1]);

        Assert.Multiple(async () =>
        {
            Assert.That(batch, Is.EqualTo(new[] { first!.Id, second }), "the lowest numbers first");
            Assert.That(files.Select(file => file.Key), Is.EqualTo(new[] { cv.Key }));
            Assert.That(uploads.Select(file => file.Key), Is.EqualTo(new[] { waiting!.Key, later!.Key }), "the form's uploads that wait for an entry, oldest first");
            Assert.That(await Entries.ListUploadsAsync(form.Id, 1), Has.Count.EqualTo(1));
            Assert.That(deleted, Is.EqualTo(2), "an id that is none counts nothing");
            Assert.That(await Entries.ListIdsAsync(form.Id, 10), Is.EqualTo(new[] { third }));
            Assert.That(Count(FormBuilderTables.EntryValue, $"{Q("entryId")} = @0", first.Id), Is.Zero, "the values went with the entry");
            Assert.That(await Entries.GetFileAsync(cv.Key), Is.Null, "and its files");
            Assert.That(await Entries.GetByIdAsync(otherEntry), Is.Not.Null);
            Assert.That(await Entries.ListFilesAsync(Array.Empty<long>()), Is.Empty);
            Assert.That(await Entries.DeleteAsync(Array.Empty<long>()), Is.Zero);
            Assert.That(() => Entries.ListIdsAsync(form.Id, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Entries.ListIdsAsync(form.Id, SqlArgs.MaxListLength + 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Entries.ListUploadsAsync(form.Id, SqlArgs.MaxListLength + 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public async Task TheUnsentMailOfABatch_IsDeleted_AndSentMailStaysInTheLog()
    {
        var form = await CreateTestFormAsync();
        var first = await AddEntryAsync(form.Id, form.VersionId);
        var second = await AddEntryAsync(form.Id, form.VersionId);
        var kept = await AddEntryAsync(form.Id, form.VersionId);
        var sent = (await Emails.AddAsync(Mail(first)))!;
        var claimed = (await Emails.ClaimAsync(sent.Id, "server-a JFB_TEST", TimeSpan.FromMinutes(5)))!;
        await Emails.SentAsync(sent.Id, claimed.LockedBy!, TimeSpan.FromDays(30));
        var queued = (await Emails.AddAsync(Mail(first)))!;
        var queuedToo = (await Emails.AddAsync(Mail(second)))!;
        var other = (await Emails.AddAsync(Mail(kept)))!;

        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.DeleteUnsentAsync([first, second, first]), Is.EqualTo(2));
            Assert.That(await Emails.GetAsync(queued.Id), Is.Null);
            Assert.That(await Emails.GetAsync(queuedToo.Id), Is.Null);
            Assert.That(await Emails.GetAsync(sent.Id), Is.Not.Null, "sent mail stays in the log");
            Assert.That(await Emails.GetAsync(other.Id), Is.Not.Null);
            Assert.That(await Emails.DeleteUnsentAsync(Array.Empty<long>()), Is.Zero);
        });
    }

    [Test]
    public async Task AFormsReferences_AreDeleted_AndOtherFormsKeepTheirs()
    {
        var form = await CreateFormAsync();
        var other = await CreateFormAsync();
        await ReferenceAsync(form.Id, "en-US");
        await ReferenceAsync(form.Id, "de-DE");
        await ReferenceAsync(other.Id, "en-US");

        Assert.Multiple(async () =>
        {
            Assert.That(await References.DeleteFormAsync(form.Id), Is.EqualTo(2));
            Assert.That(Count(FormBuilderTables.Reference, $"{Q("formId")} = @0", form.Id), Is.Zero);
            Assert.That(Count(FormBuilderTables.Reference, $"{Q("formId")} = @0", other.Id), Is.EqualTo(1));
            Assert.That(await References.DeleteFormAsync(form.Id), Is.Zero);
        });
    }

    private IReferenceRepository References => Repo<IReferenceRepository>();

    private static NewEmail Mail(long entryId) =>
        new(EmailKind.Action, "sales@jfb.test", "Jiron Form Builder <forms@jfb.test>", "A new entry", "Thank you, Anna.", "en-US", EntryId: entryId);

    // A reference of a content property to the form in one culture; 6.7 writes them, so the test writes the row itself.
    private Task<int> ReferenceAsync(int formId, string culture) =>
        Database.WriteAsync(db => db.ExecAsync(
            $"INSERT INTO {T(FormBuilderTables.Reference)} ({Q("formId")}, {Q("contentKey")}, {Q("propertyAlias")}, {Q("culture")}, {Q("updatedUtc")}) VALUES (@0, @1, @2, @3, @4)",
            formId,
            Guid.NewGuid(),
            "contactForm",
            culture,
            Now.UtcDateTime));
}
