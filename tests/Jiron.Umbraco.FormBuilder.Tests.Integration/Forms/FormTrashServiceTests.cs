using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// The trash and the permanent delete of a form through the form service (section 4.5, ADR-0047): a trash and a restore
/// into its folder or the root; a delete, only from the trash, that removes the form with its entries, files, versions,
/// grants, references and daily statistics, and the bytes of its files from the file store, in batches that a stopped
/// delete leaves part done; with the refusals, notifications and audit entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormTrashServiceTests : RepositoryTestBase
{
    private static readonly Guid Designers = Guid.Parse("76000000-0000-4000-8000-000000000001");

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private FormNotificationLog Notifications => GetRequiredService<FormNotificationLog>();

    private MemoryFileStore Store => GetRequiredService<MemoryFileStore>();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    [Test]
    public async Task ATrash_PutsTheFormInTheTrash_AndARestore_BringsItBackIntoItsFolder()
    {
        var folder = await CreateFolderAsync();
        var created = await CreateAsync("Contact", folder.Key);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var before = Notifications.Seen.Count;

        var trashed = (await Service.TrashAsync(created.Key, created.ConcurrencyVersion, Editor))!;
        var again = (await Service.TrashAsync(created.Key, trashed.ConcurrencyVersion, Editor))!;
        var inTrash = await RowAsync(created);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var restored = (await Service.RestoreAsync(created.Key, trashed.ConcurrencyVersion, Editor))!;
        var still = (await Service.RestoreAsync(created.Key, restored.ConcurrencyVersion, Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();
        var entries = await AuditAsync(created.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(trashed, Is.EqualTo(created with { ConcurrencyVersion = created.ConcurrencyVersion + 1, TrashedUtc = Now.AddMinutes(1), UpdatedUtc = Now.AddMinutes(1) }), "it keeps its folder in the trash");
            Assert.That(inTrash.TrashKey, Is.Not.Null, "a key of its own");
            Assert.That(again, Is.EqualTo(trashed), "a form in the trash already stays as it is");
            Assert.That(restored, Is.EqualTo(trashed with { ConcurrencyVersion = trashed.ConcurrencyVersion + 1, TrashedUtc = null, UpdatedUtc = Now.AddMinutes(2) }));
            Assert.That((await RowAsync(created)).TrashKey, Is.Null);
            Assert.That(still, Is.EqualTo(restored), "a form outside the trash stays as it is");
            Assert.That(await Service.GetAsync(created.Key), Is.EqualTo(restored));
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormTrashingNotification), typeof(FormTrashedNotification), typeof(FormRestoringNotification), typeof(FormRestoredNotification) }));
            Assert.That(seen.OfType<FormNotification>().Select(notification => (notification.FormKey, notification.Alias, notification.UserKey)), Is.All.EqualTo((created.Key, "contact", (Guid?)Editor)));
            Assert.That(seen.OfType<FormCancelableNotification>().Select(notification => (notification.FormKey, notification.Name, notification.UserKey)), Is.All.EqualTo((created.Key, "Contact", (Guid?)Editor)));
            Assert.That(seen.OfType<FormRestoringNotification>().Single().FolderKey, Is.EqualTo(folder.Key), "the folder it goes back into");
            Assert.That(seen.OfType<FormRestoredNotification>().Single().FolderKey, Is.EqualTo(folder.Key));
            Assert.That(Notifications.AnyInUnitOfWork, Is.False);
            Assert.That(entries, Is.EqualTo(new[] { ("form.trashed", (Guid?)Editor, "{}"), ("form.restored", Editor, $$"""{"folder":"{{folder.Key}}"}""") }));
        });
    }

    [Test]
    public async Task ARestore_GoesToTheRoot_WhenTheFormsFolderIsInTheTrash()
    {
        var folder = await CreateFolderAsync();
        var created = await CreateAsync("Contact", folder.Key);
        var trashed = (await Service.TrashAsync(created.Key, created.ConcurrencyVersion, Editor))!;
        await Folders.TrashAsync(folder.Id, Editor);
        var before = Notifications.Seen.Count;

        var restored = (await Service.RestoreAsync(created.Key, trashed.ConcurrencyVersion, Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That((restored.FolderKey, restored.TrashedUtc), Is.EqualTo(((Guid?)null, (DateTimeOffset?)null)));
            Assert.That((await RowAsync(created)).FolderId, Is.Null);
            Assert.That(seen.OfType<FormRestoringNotification>().Single().FolderKey, Is.Null, "the root");
            Assert.That(seen.OfType<FormRestoredNotification>().Single().FolderKey, Is.Null);
            Assert.That((await AuditAsync(created.Key)).Last(), Is.EqualTo(("form.restored", (Guid?)Editor, "{}")), "a restore to the root names no folder");
            Assert.That((await Folders.GetAsync(folder.Key))!.TrashedUtc, Is.Not.Null, "the folder stays in the trash");
        });
    }

    [Test]
    public async Task AStaleSave_TrashRestoreOrDelete_ReturnsConcurrencyConflict_AndChangesNothing()
    {
        var created = await CreateAsync("Contact");
        var definition = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var trashed = (await Service.TrashAsync(created.Key, created.ConcurrencyVersion, Editor))!;
        var row = await RowAsync(created);
        var before = Notifications.Seen.Count;

        Assert.Multiple(async () =>
        {
            AssertCode(async () => await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, definition, null, Editor), "concurrency_conflict");
            AssertCode(async () => await Service.TrashAsync(created.Key, created.ConcurrencyVersion, Editor), "concurrency_conflict");
            AssertCode(async () => await Service.RestoreAsync(created.Key, created.ConcurrencyVersion, Editor), "concurrency_conflict");
            AssertCode(async () => await Service.DeleteAsync(created.Key, created.ConcurrencyVersion, Editor), "concurrency_conflict");
            AssertCode(async () => await Service.SaveDraftAsync(created.Key, trashed.ConcurrencyVersion, definition, null, Editor), "trashed");
            Assert.That(await RowAsync(created), Is.EqualTo(row));
            Assert.That(Notifications.Seen, Has.Count.EqualTo(before), "a refusal publishes nothing");
        });
    }

    [Test]
    public async Task AFormThatChangesWhileItsDeleteWaits_IsKept_AndTheDeleteReturnsConcurrencyConflict()
    {
        var (created, row, _) = await FormForEntriesAsync();
        var entry = await AddEntryAsync(row.Id, row.DraftVersionId!.Value);
        var trashed = await TrashAsync(created);
        var inTrash = await RowAsync(created);
        Notifications.Before = async notification =>
        {
            if (notification is FormDeletingNotification)
            {
                await Forms.RestoreAsync(inTrash.Id, inTrash.ConcurrencyVersion, inTrash.FolderId, null);
            }
        };

        AssertCode(async () => await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor), "concurrency_conflict");
        Notifications.Before = null;

        Assert.Multiple(async () =>
        {
            Assert.That(await Service.GetAsync(created.Key), Has.Property(nameof(FormInfo.TrashedUtc)).Null, "restored meanwhile, and kept");
            Assert.That(await Entries.GetByIdAsync(entry), Is.Not.Null, "with its entries");
            Assert.That(Notifications.Seen.OfType<FormDeletedNotification>(), Is.Empty);
        });
    }

    [Test]
    public async Task ADelete_RemovesTheFormWithEverythingOfIt_AndItsFilesFromTheFileStore()
    {
        var (created, row, upload) = await FormForEntriesAsync();
        var (other, otherRow, _) = await FormForEntriesAsync("Other");
        var (entry, cv) = await EntryWithFileAsync(row, upload, "cv.pdf");
        await AddEntryAsync(row.Id, row.DraftVersionId!.Value);
        var waiting = await StoredFileAsync(row.Id, "waiting.pdf");
        var otherFile = await StoredFileAsync(otherRow.Id, "other.pdf");
        await EntryEvents.AddAsync(new NewEntryEvent(entry, "submitted"));
        await EntryNotes.AddAsync(entry, Editor, "JFB_TEST: a note");
        var sent = await SentMailAsync(entry);
        var queued = (await Emails.AddAsync(Mail(entry)))!;
        await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Form, created.Key, 0, 5));
        await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Form, other.Key, 0, 5));
        await Stats.IncrementAsync(row.Id, [new DailyStatCount("views"), new DailyStatCount("submissions")]);
        await Stats.IncrementAsync(otherRow.Id, [new DailyStatCount("views")]);
        await ReferenceAsync(row.Id);
        await ReferenceAsync(otherRow.Id);
        var trashed = await TrashAsync(created);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var before = Notifications.Seen.Count;

        var deleted = await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor);
        var seen = Notifications.Seen.Skip(before).ToList();
        var formRows = $"{Q("formId")} = @0";

        Assert.Multiple(async () =>
        {
            Assert.That(deleted, Is.EqualTo(new FormDeletion(created.Key, 2, 2)), "two entries, the file of one and the upload that waited");
            Assert.That(await Service.GetAsync(created.Key), Is.Null);
            Assert.That(await Store.OpenReadAsync(cv.StoredName), Is.Null, "delete removes the files from the file store");
            Assert.That(await Store.OpenReadAsync(waiting.StoredName), Is.Null, "the upload that waited for an entry too");
            Assert.That(await Store.OpenReadAsync(otherFile.StoredName), Is.Not.Null, "another form's file stays");
            Assert.That(Count(FormBuilderTables.Reference, formRows, row.Id), Is.Zero, "delete leaves no reference of the form");
            Assert.That(Count(FormBuilderTables.DailyStat, formRows, row.Id), Is.Zero, "and no daily stat of the form");
            Assert.That(Count(FormBuilderTables.Reference, formRows, otherRow.Id), Is.EqualTo(1), "another form keeps its references");
            Assert.That(Count(FormBuilderTables.DailyStat, formRows, otherRow.Id), Is.EqualTo(1), "and its daily stats");
            Assert.That(Count(FormBuilderTables.Entry, formRows, row.Id), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryValue, $"{Q("entryId")} = @0", entry), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryFile, formRows, row.Id), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryEvent, $"{Q("entryId")} = @0", entry), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryNote, $"{Q("entryId")} = @0", entry), Is.Zero);
            Assert.That(Count(FormBuilderTables.FormVersion, formRows, row.Id), Is.Zero, "its versions");
            Assert.That(Count(FormBuilderTables.FormField, formRows, row.Id), Is.Zero, "its field catalogue");
            Assert.That(await Permissions.ListForTargetAsync(PermissionTarget.Form, created.Key), Is.Empty, "its grants");
            Assert.That(await Permissions.ListForTargetAsync(PermissionTarget.Form, other.Key), Has.Count.EqualTo(1));
            Assert.That(await Emails.GetAsync(queued.Id), Is.Null, "unsent mail goes");
            Assert.That(await Emails.GetAsync(sent.Id), Has.Property(nameof(StoredEmail.EntryId)).Null, "sent mail stays in the log without its entry");
            Assert.That(await Service.GetAsync(other.Key), Is.Not.Null);
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormDeletingNotification), typeof(FormDeletedNotification) }));
            Assert.That(seen.OfType<FormDeletedNotification>().Single() is { EntryCount: 2, FileCount: 2, Alias: "contact" } done && done.FormKey == created.Key && done.UserKey == Editor, Is.True);
            Assert.That(Notifications.AnyInUnitOfWork, Is.False);
            Assert.That((await AuditAsync(created.Key)).Last(), Is.EqualTo((FormRemoval.DeletedAction, (Guid?)Editor, """{"entries":2,"files":2}""")), "the audit entry outlives the form");
            Assert.That(await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor), Is.Null, "gone");
        });
    }

    [Test]
    public async Task ADelete_IsRefused_ForAFormOutsideTheTrash_OrByAHandler_AndAMissingFormIsNull()
    {
        var (created, row, _) = await FormForEntriesAsync();
        var entry = await AddEntryAsync(row.Id, row.DraftVersionId!.Value);

        AssertCode(async () => await Service.DeleteAsync(created.Key, created.ConcurrencyVersion, Editor), "not_trashed");
        Assert.That(Notifications.Seen, Is.Empty, "refused before its handlers hear of it");
        Notifications.Cancel = true;
        AssertCode(async () => await Service.TrashAsync(created.Key, created.ConcurrencyVersion, Editor), "cancelled");
        Notifications.Cancel = false;
        var trashed = await TrashAsync(created);
        Notifications.Cancel = true;
        AssertCode(async () => await Service.RestoreAsync(created.Key, trashed.ConcurrencyVersion, Editor), "cancelled");
        AssertCode(async () => await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor), "cancelled");
        Notifications.Cancel = false;

        Assert.Multiple(async () =>
        {
            Assert.That(await Service.GetAsync(created.Key), Is.EqualTo(trashed), "still in the trash");
            Assert.That(await Entries.GetByIdAsync(entry), Is.Not.Null, "with its entries");
            Assert.That(await Service.TrashAsync(Guid.NewGuid(), 1, Editor), Is.Null);
            Assert.That(await Service.RestoreAsync(Guid.NewGuid(), 1, Editor), Is.Null);
            Assert.That(await Service.DeleteAsync(Guid.NewGuid(), 1, Editor), Is.Null);
            Assert.That((await AuditAsync(created.Key)).Select(audit => audit.Action), Is.EqualTo(new[] { "form.trashed" }), "a refusal audits nothing");
        });
    }

    [Test]
    public async Task ADeleteThatStops_LeavesTheRestInTheTrash_AndDeletingAgainFinishes()
    {
        var (created, row, upload) = await FormForEntriesAsync();
        var (_, first) = await EntryWithFileAsync(row, upload, "first.pdf");
        for (var i = 0; i < 2 * FormRemoval.EntriesPerBatch; i++)
        {
            await AddEntryAsync(row.Id, row.DraftVersionId!.Value);
        }

        var trashed = await TrashAsync(created);
        using var stop = new CancellationTokenSource();
        Store.AfterDelete = async _ => await stop.CancelAsync();

        Assert.That(async () => await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor, stop.Token), Throws.InstanceOf<OperationCanceledException>());
        Store.AfterDelete = null;
        var left = Count(FormBuilderTables.Entry, $"{Q("formId")} = @0", row.Id);
        var kept = await Service.GetAsync(created.Key);
        var finished = await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.EqualTo(FormRemoval.EntriesPerBatch + 1), "the first batch went, with the bytes of its file");
            Assert.That(await Store.OpenReadAsync(first.StoredName), Is.Null);
            Assert.That(kept, Is.EqualTo(trashed), "the form stays in the trash as it was");
            Assert.That(finished, Is.EqualTo(new FormDeletion(created.Key, FormRemoval.EntriesPerBatch + 1, 0)), "the second delete counts what it deleted");
            Assert.That(await Service.GetAsync(created.Key), Is.Null);
            Assert.That(Notifications.Seen.OfType<FormDeletedNotification>().Select(notification => notification.EntryCount), Is.EqualTo(new[] { FormRemoval.EntriesPerBatch + 1L }), "announced once, when it is done");
            Assert.That((await AuditAsync(created.Key)).Count(audit => audit.Action == FormRemoval.DeletedAction), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BytesThatCannotBeDeleted_AreLoggedWithoutTheirName_AndTheDeleteStands()
    {
        var (created, row, upload) = await FormForEntriesAsync();
        var (_, file) = await EntryWithFileAsync(row, upload, "cv.pdf");
        var trashed = await TrashAsync(created);
        Store.FailDeletes = true;

        var deleted = await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor);
        Store.FailDeletes = false;
        var warnings = Logs.Entries.Where(entry => entry.Category == typeof(FormRemoval).FullName && entry.Level == LogLevel.Warning).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(deleted, Is.EqualTo(new FormDeletion(created.Key, 1, 1)));
            Assert.That(await Service.GetAsync(created.Key), Is.Null);
            Assert.That(Store.Ids, Does.Contain(file.StoredName), "the bytes wait for retention");
            Assert.That(warnings.Single().Message, Does.Contain(file.Key.ToString()).And.Contain(nameof(IOException)).And.Not.Contain(file.StoredName).And.Not.Contain("cv.pdf"));
            Assert.That(warnings.Single().Exception, Is.Null, "the failure's message could name the path");
        });
    }

    [Test]
    public async Task AnEntryThatArrivesAfterTheLastBatch_IsDeletedToo()
    {
        var (created, row, _) = await FormForEntriesAsync();
        await StoredFileAsync(row.Id, "waiting.pdf");
        var trashed = await TrashAsync(created);
        long? late = null;
        Store.AfterDelete = async _ => late ??= await AddEntryAsync(row.Id, row.DraftVersionId!.Value);

        var deleted = await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(late, Is.Not.Null, "an entry came in once the uploads had gone");
            Assert.That(deleted, Is.EqualTo(new FormDeletion(created.Key, 1, 1)));
            Assert.That(await Entries.GetByIdAsync(late!.Value), Is.Null);
            Assert.That(await Service.GetAsync(created.Key), Is.Null);
        });
    }

    [Test]
    public async Task EntriesThatKeepArriving_StopTheDeleteWithInUse_AfterItsAttempts()
    {
        var (created, row, upload) = await FormForEntriesAsync();
        var uploads = new HashSet<string>(StringComparer.Ordinal) { (await StoredFileAsync(row.Id, "waiting.pdf")).StoredName };
        var trashed = await TrashAsync(created);
        var arrived = 0;

        // After each upload, an entry with a file arrives; after each entry's file, an upload: every round ends with a
        // new entry once the delete has looked for the last time.
        Store.AfterDelete = async id =>
        {
            if (uploads.Contains(id))
            {
                await EntryWithFileAsync(row, upload, "late.pdf");
                arrived++;
            }
            else
            {
                uploads.Add((await StoredFileAsync(row.Id, "waiting.pdf")).StoredName);
            }
        };

        AssertCode(async () => await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor), "in_use");
        Store.AfterDelete = null;
        var kept = await Service.GetAsync(created.Key);
        var finished = await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(arrived, Is.EqualTo(FormRemoval.Attempts), "one entry after each attempt's last batch");
            Assert.That(kept, Is.EqualTo(trashed), "the form stays in the trash");
            Assert.That(finished, Is.EqualTo(new FormDeletion(created.Key, 1, 1)), "the last entry and its file, once they stop arriving");
        });
    }

    [Test]
    public async Task AFailingHandler_IsLogged_AndTheDeleteStands()
    {
        var created = await CreateAsync("Contact");
        var trashed = await TrashAsync(created);
        Notifications.Fail = true;

        var deleted = await Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor);

        var errors = Logs.Entries.Where(entry => entry.Category == typeof(FormBuilderFormService).FullName && entry.Level == LogLevel.Error).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(deleted, Is.EqualTo(new FormDeletion(created.Key, 0, 0)));
            Assert.That(await Service.GetAsync(created.Key), Is.Null);
            Assert.That(errors.Single().Message, Does.Contain(nameof(FormDeletedNotification)).And.Contain("the change stands"));
        });
    }

    [Test]
    public async Task ATrashRestoreOrDelete_InsideAUnitOfWorkOrAnUmbracoScope_IsRefused()
    {
        var created = await CreateAsync("Contact");
        var trashed = await TrashAsync(created);
        var row = await RowAsync(created);
        var changes = new (string Name, Func<Task> Change)[]
        {
            ("trash", () => Service.TrashAsync(created.Key, trashed.ConcurrencyVersion, Editor)),
            ("restore", () => Service.RestoreAsync(created.Key, trashed.ConcurrencyVersion, Editor)),
            ("delete", () => Service.DeleteAsync(created.Key, trashed.ConcurrencyVersion, Editor)),
        };

        Assert.Multiple(async () =>
        {
            foreach (var (name, change) in changes)
            {
                Assert.That(() => Database.WriteAsync(async _ => { await change(); return true; }), Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"), name);
                using (ScopeProvider.CreateScope(autoComplete: true))
                {
                    Assert.That(() => change(), Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"), name + " in a scope");
                }
            }

            Assert.That(await RowAsync(created), Is.EqualTo(row));
        });
    }

    [Test]
    public void TheRemoval_IsRegistered_WithTheFileStoreOfTheSite()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<FormRemoval>(), Is.SameAs(GetRequiredService<FormRemoval>()));
            Assert.That(GetRequiredService<IFormBuilderFileStore>(), Is.SameAs(Store), "this fixture's store in memory");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.Services.AddSingleton<FormNotificationLog>();
        builder.AddNotificationAsyncHandler<FormTrashingNotification, FormNotificationRecorder<FormTrashingNotification>>();
        builder.AddNotificationAsyncHandler<FormTrashedNotification, FormNotificationRecorder<FormTrashedNotification>>();
        builder.AddNotificationAsyncHandler<FormRestoringNotification, FormNotificationRecorder<FormRestoringNotification>>();
        builder.AddNotificationAsyncHandler<FormRestoredNotification, FormNotificationRecorder<FormRestoredNotification>>();
        builder.AddNotificationAsyncHandler<FormDeletingNotification, FormNotificationRecorder<FormDeletingNotification>>();
        builder.AddNotificationAsyncHandler<FormDeletedNotification, FormNotificationRecorder<FormDeletedNotification>>();
    }

    private static NewEmail Mail(long entryId) =>
        new(EmailKind.Action, "sales@jfb.test", "Jiron Form Builder <forms@jfb.test>", "A new entry", "JFB_TEST", "en-US", EntryId: entryId);

    private Task<FormInfo> CreateAsync(string name, Guid? folderKey = null) => Service.CreateAsync(new NewFormRequest(name, FolderKey: folderKey, UserKey: Editor));

    private async Task<FormInfo> TrashAsync(FormInfo form) => (await Service.TrashAsync(form.Key, form.ConcurrencyVersion, Editor))!;

    private async Task<StoredForm> RowAsync(FormInfo form) => (await Forms.GetAsync(form.Key))!;

    // A form made by the service with an upload field in its catalogue, for the entries a test stores itself.
    private async Task<(FormInfo Form, StoredForm Row, int Upload)> FormForEntriesAsync(string name = "Contact")
    {
        var created = await CreateAsync(name);
        var row = await RowAsync(created);
        var upload = (await Forms.AddFieldAsync(row.Id, FormRepositoryTests.Field("upload")))!.Id;
        return (created, row, upload);
    }

    // An upload with its bytes in the file store, waiting for an entry.
    private async Task<StoredEntryFile> StoredFileAsync(int formId, string name)
    {
        var file = (await Entries.AddFileAsync(EntryRepositoryTests.NewFile(formId, name)))!;
        await Store.SaveAsync(file.StoredName, new MemoryStream(Encoding.UTF8.GetBytes("JFB_TEST")));
        return file;
    }

    // An entry that took an upload with its bytes in the file store.
    private async Task<(long Id, StoredEntryFile File)> EntryWithFileAsync(StoredForm row, int upload, string name)
    {
        var file = await StoredFileAsync(row.Id, name);
        var entry = await Entries.CreateAsync(NewEntry(row.Id, row.DraftVersionId!.Value, new NewEntryValue(upload, name, FileId: file.Id)) with { FileIds = [file.Id] });
        return (entry!.Id, file);
    }

    private async Task<StoredEmail> SentMailAsync(long entryId)
    {
        var mail = (await Emails.AddAsync(Mail(entryId)))!;
        var claimed = (await Emails.ClaimAsync(mail.Id, "server-a JFB_TEST", TimeSpan.FromMinutes(5)))!;
        await Emails.SentAsync(mail.Id, claimed.LockedBy!, TimeSpan.FromDays(30));
        return mail;
    }

    // A reference of a content property to the form; 6.7 writes them, so the test writes the row itself.
    private Task<int> ReferenceAsync(int formId) =>
        Database.WriteAsync(db => db.ExecAsync(
            $"INSERT INTO {T(FormBuilderTables.Reference)} ({Q("formId")}, {Q("contentKey")}, {Q("propertyAlias")}, {Q("culture")}, {Q("updatedUtc")}) VALUES (@0, @1, @2, @3, @4)",
            formId,
            Guid.NewGuid(),
            "contactForm",
            "en-US",
            Now.UtcDateTime));

    // The form's audit entries after its create, oldest first: action, user and details.
    private async Task<List<(string Action, Guid? UserKey, string Details)>> AuditAsync(Guid formKey) =>
        (await Audit.QueryAsync(new AuditQuery { EntityKey = formKey }, PageRequest.First(50))).Items
            .OrderBy(entry => entry.Id)
            .Where(entry => entry.Action != FormBuilderFormService.CreatedAction)
            .Select(entry => (entry.Action, entry.UserKey, entry.Details))
            .ToList();
}
