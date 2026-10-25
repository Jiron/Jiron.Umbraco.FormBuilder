using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Access;
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
/// The folders of the form library through the folder service (section 4.5, ADR-0047): create, rename and move with
/// their refusals; a trash that takes the folders below and the forms in them with one key and a restore that brings back
/// exactly what went; the permanent delete of a folder in the trash and the emptying of the trash; with the
/// notifications before and after each change and the audit entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FolderServiceTests : RepositoryTestBase
{
    private static readonly Guid Designers = Guid.Parse("77000000-0000-4000-8000-000000000001");

    private IFormBuilderFolderService Service => GetRequiredService<IFormBuilderFolderService>();

    private IFormBuilderFormService FormService => GetRequiredService<IFormBuilderFormService>();

    private FormNotificationLog Notifications => GetRequiredService<FormNotificationLog>();

    private MemoryFileStore Store => GetRequiredService<MemoryFileStore>();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    [Test]
    public async Task ACreate_PutsAFolderAtTheRootOrInAFolder_AndIsAnnouncedAndAudited()
    {
        var before = Notifications.Seen.Count;

        var root = await Service.CreateAsync(new NewFolderRequest("  Marketing  ", UserKey: Editor));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var child = await Service.CreateAsync(new NewFolderRequest("Events", root.Key, Editor));
        var seen = Notifications.Seen.Skip(before).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(root, Is.EqualTo(new FolderInfo(root.Key, "Marketing", null, 0, false, null, Now, Now, Editor, Editor)));
            Assert.That(child, Is.EqualTo(new FolderInfo(child.Key, "Events", root.Key, 0, false, null, Now.AddMinutes(1), Now.AddMinutes(1), Editor, Editor)));
            Assert.That(await Service.GetAsync(child.Key), Is.EqualTo(child));
            Assert.That(await Service.GetAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await Service.ListAsync(), Is.EqualTo(new[] { child, root }), "by name");
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FolderCreatingNotification), typeof(FolderCreatedNotification), typeof(FolderCreatingNotification), typeof(FolderCreatedNotification) }));
            Assert.That(seen.OfType<FolderCreatingNotification>().Select(notification => (notification.FolderKey, notification.Name, notification.ParentKey, notification.UserKey)), Is.EqualTo(new[] { (root.Key, "Marketing", (Guid?)null, (Guid?)Editor), (child.Key, "Events", root.Key, Editor) }), "the key the folder gets");
            Assert.That(seen.OfType<FolderCreatedNotification>().Select(notification => (notification.FolderKey, notification.Name, notification.ParentKey, notification.UserKey)), Is.EqualTo(new[] { (root.Key, "Marketing", (Guid?)null, (Guid?)Editor), (child.Key, "Events", root.Key, Editor) }));
            Assert.That(Notifications.AnyInUnitOfWork, Is.False);
            Assert.That(await AuditAsync(root.Key), Is.EqualTo(new[] { ("folder.created", (Guid?)Editor, "{}") }), "the root names no parent");
            Assert.That(await AuditAsync(child.Key), Is.EqualTo(new[] { ("folder.created", (Guid?)Editor, $$"""{"parent":"{{root.Key}}"}""") }));
        });
    }

    [Test]
    public async Task ACreate_IsRefused_ForABadName_AMissingOrTrashedParent_ATooDeepTree_OrAHandler()
    {
        var old = await CreateAsync("Old");
        await Service.TrashAsync(old.Key, Editor);
        var deepest = await ChainAsync(FolderRepository.MaxDepth);
        var folders = Count(FormBuilderTables.Folder);
        var before = Notifications.Seen.Count;

        Assert.Multiple(() =>
        {
            AssertCode(async () => await CreateAsync(" "), "invalid_value", "name");
            AssertCode(async () => await CreateAsync("Two" + Environment.NewLine + "lines"), "invalid_value", "name");
            AssertCode(async () => await CreateAsync(new string('a', FolderRepository.MaxNameLength + 1)), "limit_exceeded", "name");
            AssertCode(async () => await CreateAsync("Inside", Guid.NewGuid()), "folder_missing");
            AssertCode(async () => await CreateAsync("Inside", old.Key), "folder_missing");
            AssertCode(async () => await CreateAsync("One too many", deepest.Key), "folder_too_deep");
            Assert.That(Notifications.Seen.Skip(before).Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FolderCreatingNotification) }), "only the create the tree refused got as far as its handlers");
            Notifications.Cancel = true;
            AssertCode(async () => await CreateAsync("Not now"), "cancelled");
            Notifications.Cancel = false;
            Assert.That(Count(FormBuilderTables.Folder), Is.EqualTo(folders), "nothing was written");
        });
    }

    [Test]
    public async Task ARename_ChangesTheName_AndTheSameNameWritesNothing()
    {
        var folder = await CreateAsync("Marketing");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var before = Notifications.Seen.Count;

        var renamed = (await Service.RenameAsync(folder.Key, "  Brand ", Editor))!;
        var same = (await Service.RenameAsync(folder.Key, "Brand", Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(renamed, Is.EqualTo(folder with { Name = "Brand", UpdatedUtc = Now.AddMinutes(1) }));
            Assert.That(same, Is.EqualTo(renamed));
            Assert.That(await Service.GetAsync(folder.Key), Is.EqualTo(renamed));
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FolderRenamingNotification), typeof(FolderRenamedNotification) }));
            Assert.That(seen.OfType<FolderRenamingNotification>().Select(notification => (notification.FolderKey, notification.Name, notification.PreviousName, notification.UserKey)).Single(), Is.EqualTo((folder.Key, "Brand", "Marketing", (Guid?)Editor)));
            Assert.That(seen.OfType<FolderRenamedNotification>().Select(notification => (notification.FolderKey, notification.Name, notification.PreviousName, notification.UserKey)).Single(), Is.EqualTo((folder.Key, "Brand", "Marketing", (Guid?)Editor)));
            Assert.That((await AuditAsync(folder.Key)).Last(), Is.EqualTo(("folder.renamed", (Guid?)Editor, "{}")), "never the name");
            Assert.That(await Service.RenameAsync(Guid.NewGuid(), "Brand", Editor), Is.Null);
        });
    }

    [Test]
    public async Task ARename_IsRefused_ForAFolderInTheTrash_ABadName_OrAHandler()
    {
        var folder = await CreateAsync("Marketing");
        var old = await CreateAsync("Old");
        await Service.TrashAsync(old.Key, Editor);

        AssertCode(async () => await Service.RenameAsync(old.Key, "New", Editor), "trashed");
        AssertCode(async () => await Service.RenameAsync(folder.Key, " ", Editor), "invalid_value", "name");
        Notifications.Cancel = true;
        AssertCode(async () => await Service.RenameAsync(folder.Key, "Brand", Editor), "cancelled");
        Notifications.Cancel = false;

        Assert.Multiple(async () =>
        {
            Assert.That(await Service.GetAsync(folder.Key), Is.EqualTo(folder));
            Assert.That((await Service.GetAsync(old.Key))!.Name, Is.EqualTo("Old"));
        });
    }

    [Test]
    public async Task AMove_TakesTheFolderWithEverythingInIt_IntoAnotherFolderOrToTheRoot()
    {
        var a = await CreateAsync("A");
        var b = await CreateAsync("B");
        var c = await CreateAsync("C", a.Key);
        var below = await CreateAsync("D", c.Key);
        var form = await FormService.CreateAsync(new NewFormRequest("Contact", FolderKey: c.Key, UserKey: Editor));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var before = Notifications.Seen.Count;

        var moved = (await Service.MoveAsync(c.Key, b.Key, Editor))!;
        var same = (await Service.MoveAsync(c.Key, b.Key, Editor))!;
        var toRoot = (await Service.MoveAsync(c.Key, null, Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(moved, Is.EqualTo(c with { ParentKey = b.Key, UpdatedUtc = Now.AddMinutes(1) }));
            Assert.That(same, Is.EqualTo(moved), "where it is already writes nothing");
            Assert.That(toRoot, Is.EqualTo(moved with { ParentKey = null }));
            Assert.That(await Service.GetAsync(below.Key), Is.EqualTo(below), "what is in it moves with it and keeps its place");
            Assert.That(await FormService.GetAsync(form.Key), Is.EqualTo(form));
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FolderMovingNotification), typeof(FolderMovedNotification), typeof(FolderMovingNotification), typeof(FolderMovedNotification) }));
            Assert.That(seen.OfType<FolderMovingNotification>().Select(notification => (notification.FolderKey, notification.ParentKey, notification.PreviousParentKey)), Is.EqualTo(new[] { (c.Key, (Guid?)b.Key, (Guid?)a.Key), (c.Key, null, b.Key) }));
            Assert.That(seen.OfType<FolderMovedNotification>().Select(notification => (notification.FolderKey, notification.ParentKey, notification.PreviousParentKey)), Is.EqualTo(new[] { (c.Key, (Guid?)b.Key, (Guid?)a.Key), (c.Key, null, b.Key) }));
            Assert.That((await AuditAsync(c.Key)).Skip(1), Is.EqualTo(new[]
            {
                ("folder.moved", (Guid?)Editor, $$"""{"parent":"{{b.Key}}","previousParent":"{{a.Key}}"}"""),
                ("folder.moved", Editor, $$"""{"previousParent":"{{b.Key}}"}"""),
            }));
        });
    }

    [Test]
    public async Task AMove_IsRefused_IntoItself_BelowItself_IntoAMissingOrTrashedFolder_TooDeep_OrByAHandler()
    {
        var a = await CreateAsync("A");
        var c = await CreateAsync("C", a.Key);
        var old = await CreateAsync("Old");
        await Service.TrashAsync(old.Key, Editor);
        var deep = await ChainAsync(FolderRepository.MaxDepth - 1);

        Assert.Multiple(async () =>
        {
            AssertCode(async () => await Service.MoveAsync(a.Key, a.Key, Editor), "folder_into_itself");
            AssertCode(async () => await Service.MoveAsync(a.Key, c.Key, Editor), "folder_into_itself");
            AssertCode(async () => await Service.MoveAsync(c.Key, Guid.NewGuid(), Editor), "folder_missing");
            AssertCode(async () => await Service.MoveAsync(c.Key, old.Key, Editor), "folder_missing");
            var heard = Notifications.Seen.Count;
            AssertCode(async () => await Service.MoveAsync(old.Key, null, Editor), "trashed");
            Assert.That(Notifications.Seen, Has.Count.EqualTo(heard), "a folder in the trash is refused before its handlers hear of it");
            AssertCode(async () => await Service.MoveAsync(a.Key, deep.Key, Editor), "folder_too_deep");
            Notifications.Cancel = true;
            AssertCode(async () => await Service.MoveAsync(c.Key, null, Editor), "cancelled");
            Notifications.Cancel = false;
            Assert.That(await Service.MoveAsync(Guid.NewGuid(), null, Editor), Is.Null);
            Assert.That(await Service.GetAsync(c.Key), Is.EqualTo(c), "the refused moves changed nothing");
            Assert.That(await Service.GetAsync(a.Key), Is.EqualTo(a));
        });
    }

    [Test]
    public async Task ATrash_TakesTheFoldersBelowAndTheFormsInThem_AndARestore_BringsBackExactlyWhatWent()
    {
        // Named so that the order of their names is not the order of the tree.
        var top = await CreateAsync("Events");
        var mid = await CreateAsync("Seasons", top.Key);
        var low = await CreateAsync("Autumn", mid.Key);
        var old = await CreateAsync("Old", mid.Key);
        var other = await CreateAsync("Other");
        var invite = await CreateFormAsync("Invite", top.Key);
        var feedback = await CreateFormAsync("Feedback", low.Key);
        var survey = await CreateFormAsync("Survey", mid.Key);
        var contact = await CreateFormAsync("Contact");
        var news = await CreateFormAsync("News", other.Key);
        await Service.TrashAsync(old.Key, Editor);
        var surveyTrashed = (await FormService.TrashAsync(survey.Key, survey.ConcurrencyVersion, Editor))!;
        var surveyKey = (await Forms.GetAsync(survey.Key))!.TrashKey;
        var oldKey = (await Folders.GetAsync(old.Key))!.TrashKey;
        Clock.Advance(TimeSpan.FromMinutes(1));
        var before = Notifications.Seen.Count;

        var trashed = (await Service.TrashAsync(top.Key, Editor))!;
        var again = (await Service.TrashAsync(top.Key, Editor))!;
        var keys = await FolderTrashKeysAsync(top.Key, mid.Key, low.Key);
        var formKeys = await FormTrashKeysAsync(invite.Key, feedback.Key);
        var inviteTrashed = (await FormService.GetAsync(invite.Key))!;
        Clock.Advance(TimeSpan.FromMinutes(1));
        var restored = (await Service.RestoreAsync(top.Key, Editor))!;
        var still = (await Service.RestoreAsync(top.Key, Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();
        var went = new[] { new FolderSummary(mid.Key, "Seasons"), new FolderSummary(low.Key, "Autumn") };
        var forms = new[] { Summary(feedback), Summary(invite) };

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.Folder, Is.EqualTo(top with { TrashedUtc = Now.AddMinutes(1), UpdatedUtc = Now.AddMinutes(1) }));
            Assert.That(trashed.Folders, Is.EqualTo(went), "the folders below it, nearest first, without the one trashed before");
            Assert.That(trashed.Forms, Is.EqualTo(forms), "the forms in them, without the one trashed before");
            Assert.That(keys.Distinct().Single(), Is.Not.Null.And.EqualTo(formKeys.Distinct().Single()), "one trash key for all that went");
            Assert.That((await Forms.GetAsync(survey.Key))!.TrashKey, Is.EqualTo(surveyKey).And.Not.EqualTo(keys[0]), "a form trashed on its own keeps its key");
            Assert.That((await Folders.GetAsync(old.Key))!.TrashKey, Is.EqualTo(oldKey).And.Not.EqualTo(keys[0]));
            Assert.That(inviteTrashed, Is.EqualTo(invite with { ConcurrencyVersion = invite.ConcurrencyVersion + 1, TrashedUtc = Now.AddMinutes(1), UpdatedUtc = Now.AddMinutes(1) }), "a form keeps its folder in the trash");
            Assert.That((again.Folder, again.Folders, again.Forms), Is.EqualTo((trashed.Folder, Array.Empty<FolderSummary>(), Array.Empty<FormSummary>())), "a folder in the trash stays as it is");

            Assert.That(restored.Folder, Is.EqualTo(top with { UpdatedUtc = Now.AddMinutes(2) }));
            Assert.That(restored.Folders, Is.EqualTo(went));
            Assert.That(restored.Forms, Is.EqualTo(forms));
            Assert.That((still.Folder, still.Folders, still.Forms), Is.EqualTo((restored.Folder, Array.Empty<FolderSummary>(), Array.Empty<FormSummary>())));
            Assert.That(await Service.GetAsync(low.Key), Has.Property(nameof(FolderInfo.TrashedUtc)).Null.And.Property(nameof(FolderInfo.ParentKey)).EqualTo(mid.Key));
            Assert.That(await FormService.GetAsync(invite.Key), Is.EqualTo(inviteTrashed with { ConcurrencyVersion = inviteTrashed.ConcurrencyVersion + 1, TrashedUtc = null, UpdatedUtc = Now.AddMinutes(2) }));
            Assert.That((await Forms.GetAsync(feedback.Key))!.TrashKey, Is.Null);
            Assert.That(await Service.GetAsync(old.Key), Has.Property(nameof(FolderInfo.TrashedUtc)).Not.Null, "what was in the trash before stays there");
            Assert.That(await FormService.GetAsync(survey.Key), Is.EqualTo(surveyTrashed));
            Assert.That(await FormService.GetAsync(contact.Key), Is.EqualTo(contact), "the rest of the library stays as it is");
            Assert.That(await FormService.GetAsync(news.Key), Is.EqualTo(news));
            Assert.That(await Service.GetAsync(other.Key), Is.EqualTo(other));

            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FolderTrashingNotification), typeof(FolderTrashedNotification), typeof(FolderRestoringNotification), typeof(FolderRestoredNotification) }), "no notification of each form");
            var trashing = seen.OfType<FolderTrashingNotification>().Single();
            var gone = seen.OfType<FolderTrashedNotification>().Single();
            var restoring = seen.OfType<FolderRestoringNotification>().Single();
            var back = seen.OfType<FolderRestoredNotification>().Single();
            Assert.That((trashing.FolderKey, trashing.Name, trashing.UserKey, trashing.Folders, trashing.Forms), Is.EqualTo((top.Key, "Events", (Guid?)Editor, went, forms)));
            Assert.That((gone.FolderKey, gone.Name, gone.UserKey, gone.Folders, gone.Forms), Is.EqualTo((top.Key, "Events", (Guid?)Editor, went, forms)));
            Assert.That((restoring.FolderKey, restoring.ParentKey, restoring.Folders, restoring.Forms), Is.EqualTo((top.Key, (Guid?)null, went, forms)));
            Assert.That((back.FolderKey, back.ParentKey, back.Folders, back.Forms), Is.EqualTo((top.Key, (Guid?)null, went, forms)));
            Assert.That(Notifications.AnyInUnitOfWork, Is.False);
            Assert.That((await AuditAsync(top.Key)).Skip(1), Is.EqualTo(new[]
            {
                ("folder.trashed", (Guid?)Editor, """{"folders":2,"forms":2}"""),
                ("folder.restored", Editor, """{"folders":2,"forms":2}"""),
            }));
        });
    }

    [Test]
    public async Task AFolderRestoredOnItsOwn_GoesToTheRoot_WithWhatWentWithItBelowIt()
    {
        var top = await CreateAsync("Events");
        var mid = await CreateAsync("Archive", top.Key);
        var low = await CreateAsync("Spring", mid.Key);
        var feedback = await CreateFormAsync("Feedback", low.Key);
        var invite = await CreateFormAsync("Invite", top.Key);
        await Service.TrashAsync(top.Key, Editor);
        var before = Notifications.Seen.Count;

        var restored = (await Service.RestoreAsync(mid.Key, Editor))!;
        var restoring = Notifications.Seen.Skip(before).OfType<FolderRestoringNotification>().Single();
        var rest = (await Service.RestoreAsync(top.Key, Editor))!;

        Assert.Multiple(async () =>
        {
            Assert.That((restored.Folder.Key, restored.Folder.ParentKey, restored.Folder.TrashedUtc), Is.EqualTo((mid.Key, (Guid?)null, (DateTimeOffset?)null)), "its parent is in the trash");
            Assert.That(restored.Folders, Is.EqualTo(new[] { new FolderSummary(low.Key, "Spring") }));
            Assert.That(restored.Forms, Is.EqualTo(new[] { Summary(feedback) }));
            Assert.That(restoring.ParentKey, Is.Null, "the root");
            Assert.That((await AuditAsync(mid.Key)).Last(), Is.EqualTo(("folder.restored", (Guid?)Editor, """{"folders":1,"forms":1}""")));
            Assert.That(rest.Folders, Is.Empty, "the folder that came back on its own left the trash for good");
            Assert.That(rest.Forms, Is.EqualTo(new[] { Summary(invite) }));
            Assert.That(await Service.GetAsync(mid.Key), Has.Property(nameof(FolderInfo.ParentKey)).Null, "and stays at the root");
        });
    }

    [Test]
    public async Task AFolderTrashedOnItsOwn_GoesBackIntoItsParent()
    {
        var top = await CreateAsync("Events");
        var old = await CreateAsync("Old", top.Key);
        await Service.TrashAsync(old.Key, Editor);
        var before = Notifications.Seen.Count;

        var restored = (await Service.RestoreAsync(old.Key, Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That((restored.Folder.ParentKey, restored.Folder.TrashedUtc, restored.Folders, restored.Forms), Is.EqualTo(((Guid?)top.Key, (DateTimeOffset?)null, Array.Empty<FolderSummary>(), Array.Empty<FormSummary>())));
            Assert.That(seen.OfType<FolderRestoringNotification>().Single().ParentKey, Is.EqualTo(top.Key));
            Assert.That(seen.OfType<FolderRestoredNotification>().Single().ParentKey, Is.EqualTo(top.Key));
            Assert.That((await AuditAsync(old.Key)).Last(), Is.EqualTo(("folder.restored", (Guid?)Editor, $$"""{"parent":"{{top.Key}}","folders":0,"forms":0}""")));
        });
    }

    [Test]
    public async Task ARestore_LandsWhereItWasChecked_OrIsRefusedWithLandingChanged()
    {
        var top = await CreateAsync("Events");
        var old = await CreateAsync("Old", top.Key);
        await Service.TrashAsync(old.Key, Editor);
        var landing = await Service.GetRestoreLandingAsync(old.Key);
        Notifications.Before = async notification =>
        {
            if (notification is FolderRestoringNotification)
            {
                await Service.TrashAsync(top.Key, Editor);
            }
        };

        AssertCode(async () => await Service.RestoreAsync(old.Key, Editor), "landing_changed");
        Notifications.Before = null;
        AssertCode(async () => await Service.RestoreAsync(old.Key, Editor, landing), "landing_changed");

        Assert.Multiple(async () =>
        {
            Assert.That(landing, Is.EqualTo(new RestoreLanding(top.Key)), "its parent, outside the trash");
            Assert.That(await Service.GetRestoreLandingAsync(old.Key), Is.EqualTo(new RestoreLanding(null)), "the root, now that its parent is in the trash");
            Assert.That((await Service.GetAsync(old.Key))!.TrashedUtc, Is.Not.Null, "still in the trash");
            Assert.That(await Service.GetRestoreLandingAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task ADelete_RemovesAFolderInTheTrash_WithTheFoldersBelowIt_AndTheFormsInThem()
    {
        var top = await CreateAsync("Events");
        var mid = await CreateAsync("Archive", top.Key);
        var invite = await CreateFormAsync("Invite", top.Key);
        var feedback = await CreateFormAsync("Feedback", mid.Key);
        var row = (await Forms.GetAsync(invite.Key))!;
        var file = (await Entries.AddFileAsync(EntryRepositoryTests.NewFile(row.Id, "cv.pdf")))!;
        await Store.SaveAsync(file.StoredName, new MemoryStream(Encoding.UTF8.GetBytes("JFB_TEST")));
        await AddEntryAsync(row.Id, row.DraftVersionId!.Value);
        await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, top.Key, 0, 5));
        await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, mid.Key, 0, 5));
        await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Form, invite.Key, 0, 5));
        await Service.TrashAsync(top.Key, Editor);
        var before = Notifications.Seen.Count;

        var deleted = (await Service.DeleteAsync(top.Key, Editor))!;
        var seen = Notifications.Seen.Skip(before).ToList();
        var forms = new[] { Summary(feedback), Summary(invite) };
        var below = new[] { new FolderSummary(mid.Key, "Archive") };
        var folderAudit = (await Audit.QueryAsync(new AuditQuery { Action = FormBuilderFolderService.DeletedAction }, PageRequest.First(10))).Items.OrderBy(entry => entry.Id).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(deleted.Folders, Is.EqualTo(new[] { new FolderSummary(top.Key, "Events"), below[0] }), "the folder, then those below it");
            Assert.That((deleted.Forms, deleted.Entries, deleted.Files), Is.EqualTo((forms, 1L, 1L)));
            Assert.That(Count(FormBuilderTables.Folder), Is.Zero);
            Assert.That(Count(FormBuilderTables.Form), Is.Zero);
            Assert.That(await Store.OpenReadAsync(file.StoredName), Is.Null, "the files of its forms leave the file store");
            Assert.That(await Permissions.ListAsync(), Is.Empty, "the grants on the folders and the forms");
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FolderDeletingNotification), typeof(FolderDeletedNotification) }), "no notification of each form");
            var deleting = seen.OfType<FolderDeletingNotification>().Single();
            var done = seen.OfType<FolderDeletedNotification>().Single();
            Assert.That((deleting.FolderKey, deleting.Name, deleting.UserKey, deleting.Folders, deleting.Forms), Is.EqualTo((top.Key, "Events", (Guid?)Editor, below, forms)));
            Assert.That((done.FolderKey, done.Name, done.UserKey, done.Folders, done.Forms), Is.EqualTo((top.Key, "Events", (Guid?)Editor, below, forms)));
            Assert.That(folderAudit.Select(entry => (entry.EntityType, entry.EntityKey, entry.UserKey, entry.Details)), Is.EqualTo(new[] { ("folder", (Guid?)mid.Key, (Guid?)Editor, "{}"), ("folder", top.Key, Editor, "{}") }), "the deepest first");
            Assert.That((await AuditAsync(invite.Key)).Last(), Is.EqualTo((FormRemoval.DeletedAction, (Guid?)Editor, """{"entries":1,"files":1}""")), "each form is audited as its own delete is");
            Assert.That((await AuditAsync(feedback.Key)).Last(), Is.EqualTo((FormRemoval.DeletedAction, (Guid?)Editor, """{"entries":0,"files":0}""")));
            Assert.That(await Service.DeleteAsync(top.Key, Editor), Is.Null, "gone");
        });
    }

    [Test]
    public async Task ADelete_IsRefused_ForAFolderOutsideTheTrash_OneWithAFormOutsideIt_OrByAHandler()
    {
        var live = await CreateAsync("Live");
        var mixed = await CreateAsync("Mixed");
        var form = await CreateFormAsync("Contact", mixed.Key);
        var row = (await Folders.GetAsync(mixed.Key))!;
        await Folders.TrashAsync(row.Id, Editor);
        var gone = await CreateAsync("Gone");
        await Service.TrashAsync(gone.Key, Editor);

        AssertCode(async () => await Service.DeleteAsync(live.Key, Editor), "not_trashed");
        AssertCode(async () => await Service.DeleteAsync(mixed.Key, Editor), "folder_not_empty");
        Assert.That(Notifications.Seen.OfType<FolderDeletingNotification>(), Is.Empty, "refused before its handlers hear of it");
        Notifications.Cancel = true;
        AssertCode(async () => await Service.DeleteAsync(gone.Key, Editor), "cancelled");
        AssertCode(async () => await EmptyAllAsync(Editor), "cancelled");
        Notifications.Cancel = false;

        Assert.Multiple(async () =>
        {
            Assert.That(Count(FormBuilderTables.Folder), Is.EqualTo(3), "nothing was deleted");
            Assert.That(await FormService.GetAsync(form.Key), Is.EqualTo(form));
            Assert.That(await Service.DeleteAsync(Guid.NewGuid(), Editor), Is.Null);
        });
    }

    [Test]
    public async Task EmptyingTheTrash_OfWhatAFilterLetsGo_NamesOnlyThat_AndCountsWhatStays()
    {
        var top = await CreateAsync("Events");
        var mid = await CreateAsync("Archive", top.Key);
        var invite = await CreateFormAsync("Invite", mid.Key);
        var spare = await CreateAsync("Spare");
        var loose = await CreateFormAsync("Loose");
        var kept = await CreateFormAsync("Kept");
        await Service.TrashAsync(top.Key, Editor);
        await Service.TrashAsync(spare.Key, Editor);
        await FormService.TrashAsync(loose.Key, loose.ConcurrencyVersion, Editor);
        await FormService.TrashAsync(kept.Key, kept.ConcurrencyVersion, Editor);
        var offered = new List<DeletionCandidate>();
        var before = Notifications.Seen.Count;

        var emptied = await Service.EmptyTrashAsync(
            (removal, _) =>
            {
                offered.Add(removal);
                return Task.FromResult(!removal.FolderKeys.Contains(top.Key) && !removal.Forms.Any(form => form.Key == kept.Key));
            },
            Editor);
        var seen = Notifications.Seen.Skip(before).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(offered.Select(removal => (string.Join(",", removal.FolderKeys), string.Join(",", removal.Forms.Select(form => form.Key)))), Is.EqualTo(new[] { ($"{top.Key},{mid.Key}", $"{invite.Key}"), ($"{spare.Key}", string.Empty), (string.Empty, $"{kept.Key}"), (string.Empty, $"{loose.Key}") }), "each top item once, with everything it takes");
            Assert.That(offered[0].Forms.Single(), Is.EqualTo(new FormAccessTarget(invite.Key, mid.Key, false)), "a form as the evaluation reads it");
            Assert.That(emptied.Left, Is.EqualTo(2), "Events with what is below it, and Kept");
            Assert.That(emptied.Deleted.Folders, Is.EqualTo(new[] { new FolderSummary(spare.Key, "Spare") }));
            Assert.That(emptied.Deleted.Forms, Is.EqualTo(new[] { Summary(loose) }));
            Assert.That(seen.OfType<TrashEmptyingNotification>().Single().Folders, Is.EqualTo(emptied.Deleted.Folders), "named before as they go, and only those");
            Assert.That(seen.OfType<TrashEmptiedNotification>().Single().Forms, Is.EqualTo(emptied.Deleted.Forms));
            Assert.That((await Service.GetAsync(mid.Key))!.TrashedUtc, Is.Not.Null, "what stayed is still in the trash");
            Assert.That(await FormService.GetAsync(invite.Key), Is.Not.Null);
            Assert.That(await FormService.GetAsync(kept.Key), Is.Not.Null);
        });
    }

    [Test]
    public async Task TheRemovalOfAFolder_IsTheFolderWithTheFoldersBelowIt_NearestFirst_AndTheirForms()
    {
        var top = await CreateAsync("Events");
        var mid = await CreateAsync("Archive", top.Key);
        var deep = await CreateAsync("Deep", mid.Key);
        var invite = await CreateFormAsync("Invite", top.Key);
        var notes = await CreateFormAsync("Notes", deep.Key);
        await CreateFormAsync("Elsewhere");

        var removal = await Service.GetDeletionCandidateAsync(top.Key);
        var ofDeep = await Service.GetDeletionCandidateAsync(deep.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(removal!.FolderKeys, Is.EqualTo(new[] { top.Key, mid.Key, deep.Key }));
            Assert.That(removal.Forms, Is.EquivalentTo(new[] { new FormAccessTarget(invite.Key, top.Key, false), new FormAccessTarget(notes.Key, deep.Key, false) }));
            Assert.That((ofDeep!.FolderKeys, ofDeep.Forms), Is.EqualTo((new[] { deep.Key }, new[] { new FormAccessTarget(notes.Key, deep.Key, false) })).Using<(IReadOnlyList<Guid>, IReadOnlyList<FormAccessTarget>)>((a, b) => a.Item1.SequenceEqual(b.Item1) && a.Item2.SequenceEqual(b.Item2)));
            Assert.That(await Service.GetDeletionCandidateAsync(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task EmptyingTheTrash_DeletesEveryFolderAndFormInIt_AndNothingElse()
    {
        var top = await CreateAsync("Events");
        var mid = await CreateAsync("Archive", top.Key);
        var keep = await CreateAsync("Keep");
        var old = await CreateAsync("Old", keep.Key);
        var invite = await CreateFormAsync("Invite", top.Key);
        var kept = await CreateFormAsync("Kept", keep.Key);
        var loose = await CreateFormAsync("Loose");
        var stays = await CreateFormAsync("Stays");
        var inKeep = await CreateFormAsync("Survey", keep.Key);
        var looseRow = (await Forms.GetAsync(loose.Key))!;
        await AddEntryAsync(looseRow.Id, looseRow.DraftVersionId!.Value);
        await Service.TrashAsync(top.Key, Editor);
        await Service.TrashAsync(old.Key, Editor);
        await FormService.TrashAsync(kept.Key, kept.ConcurrencyVersion, Editor);
        await FormService.TrashAsync(loose.Key, loose.ConcurrencyVersion, Editor);
        var before = Notifications.Seen.Count;

        var emptied = await EmptyAllAsync(Editor);
        var seen = Notifications.Seen.Skip(before).ToList();
        var again = await EmptyAllAsync(Editor);
        var folders = new[] { new FolderSummary(top.Key, "Events"), new FolderSummary(mid.Key, "Archive"), new FolderSummary(old.Key, "Old") };

        Assert.Multiple(async () =>
        {
            Assert.That(emptied.Folders, Is.EqualTo(folders), "each folder at the top of the trash, then those below it");
            Assert.That(emptied.Forms[0], Is.EqualTo(Summary(invite)), "the forms in its folders first");
            Assert.That(emptied.Forms.Skip(1), Is.EquivalentTo(new[] { Summary(kept), Summary(loose) }), "then those trashed on their own");
            Assert.That((emptied.Entries, emptied.Files), Is.EqualTo((1L, 0L)));
            Assert.That(await Service.ListAsync(), Is.EqualTo(new[] { keep }));
            Assert.That(await FormService.GetAsync(stays.Key), Is.EqualTo(stays));
            Assert.That(await FormService.GetAsync(inKeep.Key), Is.EqualTo(inKeep));
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(2));
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(TrashEmptyingNotification), typeof(TrashEmptiedNotification) }), "no notification of each folder or form");
            var emptying = seen.OfType<TrashEmptyingNotification>().Single();
            var done = seen.OfType<TrashEmptiedNotification>().Single();
            Assert.That((emptying.Folders, emptying.Forms, emptying.UserKey), Is.EqualTo((emptied.Folders, emptied.Forms, (Guid?)Editor)), "named before as they go");
            Assert.That((done.Folders, done.Forms, done.UserKey), Is.EqualTo((emptied.Folders, emptied.Forms, (Guid?)Editor)));
            Assert.That((again.Folders, again.Forms, again.Entries, again.Files), Is.EqualTo((Array.Empty<FolderSummary>(), Array.Empty<FormSummary>(), 0L, 0L)), "an empty trash empties to nothing");
        });
    }

    [Test]
    public async Task AFailingHandler_IsLogged_AndTheChangeStands()
    {
        Notifications.Fail = true;

        var created = await CreateAsync("Marketing");

        var errors = Logs.Entries.Where(entry => entry.Category == typeof(FormBuilderFolderService).FullName && entry.Level == LogLevel.Error).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(await Service.GetAsync(created.Key), Is.EqualTo(created));
            Assert.That(errors.Single().Message, Does.Contain(nameof(FolderCreatedNotification)).And.Contain("the change stands"));
            Assert.That(errors.Single().Exception?.Message, Is.EqualTo("JFB_TEST: a handler that fails"));
        });
    }

    [Test]
    public async Task AChange_InsideAUnitOfWorkOrAnUmbracoScope_IsRefused()
    {
        var folder = await CreateAsync("Marketing");
        var old = await CreateAsync("Old");
        await Service.TrashAsync(old.Key, Editor);
        var changes = new (string Name, Func<Task> Change)[]
        {
            ("create", () => CreateAsync("Inside")),
            ("rename", () => Service.RenameAsync(folder.Key, "Brand", Editor)),
            ("move", () => Service.MoveAsync(folder.Key, null, Editor)),
            ("trash", () => Service.TrashAsync(folder.Key, Editor)),
            ("restore", () => Service.RestoreAsync(old.Key, Editor)),
            ("delete", () => Service.DeleteAsync(old.Key, Editor)),
            ("empty the trash", () => EmptyAllAsync(Editor)),
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

            Assert.That(await Service.ListAsync(), Is.EqualTo(new[] { folder, (await Service.GetAsync(old.Key))! }));
            Assert.That(Count(FormBuilderTables.Folder), Is.EqualTo(2));
        });
    }

    [Test]
    public void TheService_IsRegistered()
    {
        Assert.That(GetRequiredService<IFormBuilderFolderService>(), Is.InstanceOf<FormBuilderFolderService>());
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.Services.AddSingleton<FormNotificationLog>();
        builder.AddNotificationAsyncHandler<FolderCreatingNotification, FormNotificationRecorder<FolderCreatingNotification>>();
        builder.AddNotificationAsyncHandler<FolderCreatedNotification, FormNotificationRecorder<FolderCreatedNotification>>();
        builder.AddNotificationAsyncHandler<FolderRenamingNotification, FormNotificationRecorder<FolderRenamingNotification>>();
        builder.AddNotificationAsyncHandler<FolderRenamedNotification, FormNotificationRecorder<FolderRenamedNotification>>();
        builder.AddNotificationAsyncHandler<FolderMovingNotification, FormNotificationRecorder<FolderMovingNotification>>();
        builder.AddNotificationAsyncHandler<FolderMovedNotification, FormNotificationRecorder<FolderMovedNotification>>();
        builder.AddNotificationAsyncHandler<FolderTrashingNotification, FormNotificationRecorder<FolderTrashingNotification>>();
        builder.AddNotificationAsyncHandler<FolderTrashedNotification, FormNotificationRecorder<FolderTrashedNotification>>();
        builder.AddNotificationAsyncHandler<FolderRestoringNotification, FormNotificationRecorder<FolderRestoringNotification>>();
        builder.AddNotificationAsyncHandler<FolderRestoredNotification, FormNotificationRecorder<FolderRestoredNotification>>();
        builder.AddNotificationAsyncHandler<FolderDeletingNotification, FormNotificationRecorder<FolderDeletingNotification>>();
        builder.AddNotificationAsyncHandler<FolderDeletedNotification, FormNotificationRecorder<FolderDeletedNotification>>();
        builder.AddNotificationAsyncHandler<TrashEmptyingNotification, FormNotificationRecorder<TrashEmptyingNotification>>();
        builder.AddNotificationAsyncHandler<TrashEmptiedNotification, FormNotificationRecorder<TrashEmptiedNotification>>();

        // The forms' own notifications, to show that a folder's changes publish none of them.
        builder.AddNotificationAsyncHandler<FormTrashingNotification, FormNotificationRecorder<FormTrashingNotification>>();
        builder.AddNotificationAsyncHandler<FormTrashedNotification, FormNotificationRecorder<FormTrashedNotification>>();
        builder.AddNotificationAsyncHandler<FormRestoringNotification, FormNotificationRecorder<FormRestoringNotification>>();
        builder.AddNotificationAsyncHandler<FormRestoredNotification, FormNotificationRecorder<FormRestoredNotification>>();
        builder.AddNotificationAsyncHandler<FormDeletingNotification, FormNotificationRecorder<FormDeletingNotification>>();
        builder.AddNotificationAsyncHandler<FormDeletedNotification, FormNotificationRecorder<FormDeletedNotification>>();
    }

    private static FormSummary Summary(FormInfo form) => new(form.Key, form.Alias, form.Name);

    private Task<FolderInfo> CreateAsync(string name, Guid? parentKey = null) => Service.CreateAsync(new NewFolderRequest(name, parentKey, Editor));

    private Task<FormInfo> CreateFormAsync(string name, Guid? folderKey = null) => FormService.CreateAsync(new NewFormRequest(name, FolderKey: folderKey, UserKey: Editor));

    // Folders nested this many levels from the root, made in the repository; the deepest.
    private async Task<StoredFolder> ChainAsync(int levels)
    {
        StoredFolder? folder = null;
        for (var level = 1; level <= levels; level++)
        {
            folder = await CreateFolderAsync($"Level {level}", folder?.Id);
        }

        return folder!;
    }

    private async Task<List<Guid?>> FolderTrashKeysAsync(params Guid[] keys)
    {
        var found = new List<Guid?>();
        foreach (var key in keys)
        {
            found.Add((await Folders.GetAsync(key))!.TrashKey);
        }

        return found;
    }

    private async Task<List<Guid?>> FormTrashKeysAsync(params Guid[] keys)
    {
        var found = new List<Guid?>();
        foreach (var key in keys)
        {
            found.Add((await Forms.GetAsync(key))!.TrashKey);
        }

        return found;
    }

    // Empties the whole trash: the filter lets everything go, as the product's own filter does for a user who may delete it all.
    private async Task<LibraryDeletion> EmptyAllAsync(Guid? userKey) =>
        (await Service.EmptyTrashAsync((_, _) => Task.FromResult(true), userKey)).Deleted;

    // The audit entries of a folder or form, oldest first: action, user and details.
    private async Task<List<(string Action, Guid? UserKey, string Details)>> AuditAsync(Guid key) =>
        (await Audit.QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(50))).Items
            .OrderBy(entry => entry.Id)
            .Select(entry => (entry.Action, entry.UserKey, entry.Details))
            .ToList();
}
