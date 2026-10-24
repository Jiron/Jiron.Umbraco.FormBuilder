using Jiron.Umbraco.FormBuilder.Core.Forms;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The notifications of the folder service (ADR-0047): what each carries, including the folders and forms a change of a
/// tree takes with it, and the arguments it refuses.
/// </summary>
[TestFixture]
public sealed class FolderNotificationTests
{
    private static readonly Guid FolderKey = Guid.Parse("61000000-0000-4000-8000-000000000001");
    private static readonly Guid User = Guid.Parse("61000000-0000-4000-8000-000000000002");
    private static readonly Guid Parent = Guid.Parse("61000000-0000-4000-8000-000000000003");
    private static readonly Guid Previous = Guid.Parse("61000000-0000-4000-8000-000000000004");
    private static readonly IReadOnlyList<FolderSummary> Below = [new(Guid.Parse("61000000-0000-4000-8000-000000000005"), "Archive")];
    private static readonly IReadOnlyList<FormSummary> Inside = [new(Guid.Parse("61000000-0000-4000-8000-000000000006"), "invite", "Invite")];

    [Test]
    public void TheNotificationsOfAFolder_CarryWhatTheChangeIs()
    {
        var creating = new FolderCreatingNotification(FolderKey, "Events", Parent, User, new EventMessages());
        var created = new FolderCreatedNotification(FolderKey, "Events", null, null);
        var renaming = new FolderRenamingNotification(FolderKey, "Events", "Meetings", User, new EventMessages());
        var renamed = new FolderRenamedNotification(FolderKey, "Events", "Meetings", User);
        var moving = new FolderMovingNotification(FolderKey, "Events", Parent, Previous, User, new EventMessages());
        var moved = new FolderMovedNotification(FolderKey, "Events", null, Previous, User);

        Assert.Multiple(() =>
        {
            Assert.That((creating.FolderKey, creating.Name, creating.ParentKey, creating.UserKey), Is.EqualTo((FolderKey, "Events", (Guid?)Parent, (Guid?)User)));
            Assert.That((created.FolderKey, created.Name, created.ParentKey, created.UserKey), Is.EqualTo((FolderKey, "Events", (Guid?)null, (Guid?)null)), "at the root, by Jiron Form Builder");
            Assert.That((renaming.Name, renaming.PreviousName), Is.EqualTo(("Events", "Meetings")));
            Assert.That((renamed.Name, renamed.PreviousName, renamed.UserKey), Is.EqualTo(("Events", "Meetings", (Guid?)User)));
            Assert.That((moving.ParentKey, moving.PreviousParentKey), Is.EqualTo(((Guid?)Parent, (Guid?)Previous)));
            Assert.That((moved.ParentKey, moved.PreviousParentKey), Is.EqualTo(((Guid?)null, (Guid?)Previous)), "to the root");
            Assert.That(new[] { creating.Cancel, renaming.Cancel, moving.Cancel }, Is.All.False);
        });
    }

    [Test]
    public void TheNotificationsOfATree_NameTheFoldersAndFormsThatGoWithIt()
    {
        var trashing = new FolderTrashingNotification(FolderKey, "Events", Below, Inside, User, new EventMessages());
        var trashed = new FolderTrashedNotification(FolderKey, "Events", Below, Inside, User);
        var restoring = new FolderRestoringNotification(FolderKey, "Events", Parent, Below, Inside, User, new EventMessages());
        var restored = new FolderRestoredNotification(FolderKey, "Events", null, Below, Inside, User);
        var deleting = new FolderDeletingNotification(FolderKey, "Events", Below, Inside, User, new EventMessages());
        var deleted = new FolderDeletedNotification(FolderKey, "Events", Below, Inside, null);
        var emptying = new TrashEmptyingNotification(Below, Inside, User, new EventMessages());
        var emptied = new TrashEmptiedNotification(Below, Inside, null);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { trashing.Folders, trashed.Folders, restoring.Folders, restored.Folders, deleting.Folders, deleted.Folders, emptying.Folders, emptied.Folders }, Is.All.SameAs(Below));
            Assert.That(new[] { trashing.Forms, trashed.Forms, restoring.Forms, restored.Forms, deleting.Forms, deleted.Forms, emptying.Forms, emptied.Forms }, Is.All.SameAs(Inside));
            Assert.That((trashing.FolderKey, trashing.Name, trashing.UserKey), Is.EqualTo((FolderKey, "Events", (Guid?)User)));
            Assert.That((restoring.ParentKey, restored.ParentKey), Is.EqualTo(((Guid?)Parent, (Guid?)null)));
            Assert.That((deleted.FolderKey, deleted.UserKey), Is.EqualTo((FolderKey, (Guid?)null)));
            Assert.That((emptying.UserKey, emptied.UserKey), Is.EqualTo(((Guid?)User, (Guid?)null)));
            Assert.That(new[] { trashing.Cancel, restoring.Cancel, deleting.Cancel, emptying.Cancel }, Is.All.False);
        });
    }

    [Test]
    public void ANotification_NeedsTheFoldersNameAndItsLists()
    {
        var messages = new EventMessages();

        Assert.Multiple(() =>
        {
            Assert.That(() => new FolderCreatingNotification(FolderKey, string.Empty, null, null, messages), Throws.ArgumentException);
            Assert.That(() => new FolderCreatedNotification(FolderKey, null!, null, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderRenamingNotification(FolderKey, "Events", string.Empty, null, messages), Throws.ArgumentException);
            Assert.That(() => new FolderRenamedNotification(FolderKey, "Events", null!, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderMovedNotification(FolderKey, string.Empty, null, null, null), Throws.ArgumentException);
            Assert.That(() => new FolderTrashingNotification(FolderKey, "Events", null!, Inside, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FolderTrashingNotification(FolderKey, "Events", Below, null!, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FolderTrashedNotification(FolderKey, "Events", null!, Inside, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderTrashedNotification(FolderKey, "Events", Below, null!, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderRestoringNotification(FolderKey, "Events", null, null!, Inside, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FolderRestoringNotification(FolderKey, "Events", null, Below, null!, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FolderRestoredNotification(FolderKey, "Events", null, null!, Inside, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderRestoredNotification(FolderKey, "Events", null, Below, null!, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderDeletingNotification(FolderKey, "Events", null!, Inside, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FolderDeletingNotification(FolderKey, "Events", Below, null!, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FolderDeletedNotification(FolderKey, "Events", null!, Inside, null), Throws.ArgumentNullException);
            Assert.That(() => new FolderDeletedNotification(FolderKey, "Events", Below, null!, null), Throws.ArgumentNullException);
            Assert.That(() => new TrashEmptyingNotification(null!, Inside, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new TrashEmptyingNotification(Below, null!, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new TrashEmptiedNotification(null!, Inside, null), Throws.ArgumentNullException);
            Assert.That(() => new TrashEmptiedNotification(Below, null!, null), Throws.ArgumentNullException);
        });
    }
}
