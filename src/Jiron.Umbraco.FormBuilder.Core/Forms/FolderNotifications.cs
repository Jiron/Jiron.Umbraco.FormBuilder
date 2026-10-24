using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A folder that a change of the library takes with it (ADR-0047).</summary>
/// <param name="Key">The folder's key.</param>
/// <param name="Name">Its name.</param>
public sealed record FolderSummary(Guid Key, string Name);

/// <summary>A form that a change of the library takes with it (ADR-0047).</summary>
/// <param name="Key">The form's key.</param>
/// <param name="Alias">Its alias.</param>
/// <param name="Name">Its name.</param>
public sealed record FormSummary(Guid Key, string Alias, string Name);

/// <summary>
/// A notification about a change to a folder of the form library that has happened: published on the server that made
/// the change, after it committed (ADR-0044, ADR-0047). A handler that fails is logged; the change stands.
/// </summary>
public abstract class FolderNotification : INotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">The folder's name after the change.</param>
    /// <param name="userKey">The backoffice user who made the change, or null when Jiron Form Builder did.</param>
    protected FolderNotification(Guid folderKey, string name, Guid? userKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        FolderKey = folderKey;
        Name = name;
        UserKey = userKey;
    }

    /// <summary>The folder's key.</summary>
    public Guid FolderKey { get; }

    /// <summary>The folder's name after the change.</summary>
    public string Name { get; }

    /// <summary>The backoffice user who made the change, or null when Jiron Form Builder did.</summary>
    public Guid? UserKey { get; }
}

/// <summary>
/// A notification about a change to a folder that is about to happen: published before anything is written (ADR-0044,
/// ADR-0047). A handler stops the change with <see cref="CancelableNotification.CancelOperation(EventMessage)"/>, whose
/// message the refusal carries.
/// </summary>
public abstract class FolderCancelableNotification : CancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">The folder's name after the change.</param>
    /// <param name="userKey">The backoffice user who makes the change, or null when Jiron Form Builder does.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    protected FolderCancelableNotification(Guid folderKey, string name, Guid? userKey, EventMessages messages)
        : base(messages)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        FolderKey = folderKey;
        Name = name;
        UserKey = userKey;
    }

    /// <summary>The folder's key.</summary>
    public Guid FolderKey { get; }

    /// <summary>The folder's name after the change.</summary>
    public string Name { get; }

    /// <summary>The backoffice user who makes the change, or null when Jiron Form Builder does.</summary>
    public Guid? UserKey { get; }
}

/// <summary>A folder is about to be created, at the root or in a folder outside the trash.</summary>
public sealed class FolderCreatingNotification : FolderCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The new folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentKey">The folder it goes in, or null for the root.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder creates it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FolderCreatingNotification(Guid folderKey, string name, Guid? parentKey, Guid? userKey, EventMessages messages)
        : base(folderKey, name, userKey, messages)
    {
        ParentKey = parentKey;
    }

    /// <summary>The folder it goes in, or null for the root.</summary>
    public Guid? ParentKey { get; }
}

/// <summary>A folder was created.</summary>
public sealed class FolderCreatedNotification : FolderNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentKey">The folder it is in, or null for the root.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder created it.</param>
    public FolderCreatedNotification(Guid folderKey, string name, Guid? parentKey, Guid? userKey)
        : base(folderKey, name, userKey)
    {
        ParentKey = parentKey;
    }

    /// <summary>The folder it is in, or null for the root.</summary>
    public Guid? ParentKey { get; }
}

/// <summary>A folder's name is about to change.</summary>
public sealed class FolderRenamingNotification : FolderCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">The new name.</param>
    /// <param name="previousName">The name before.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder renames it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FolderRenamingNotification(Guid folderKey, string name, string previousName, Guid? userKey, EventMessages messages)
        : base(folderKey, name, userKey, messages)
    {
        ArgumentException.ThrowIfNullOrEmpty(previousName);
        PreviousName = previousName;
    }

    /// <summary>The name before.</summary>
    public string PreviousName { get; }
}

/// <summary>A folder's name changed.</summary>
public sealed class FolderRenamedNotification : FolderNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">The new name.</param>
    /// <param name="previousName">The name before.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder renamed it.</param>
    public FolderRenamedNotification(Guid folderKey, string name, string previousName, Guid? userKey)
        : base(folderKey, name, userKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(previousName);
        PreviousName = previousName;
    }

    /// <summary>The name before.</summary>
    public string PreviousName { get; }
}

/// <summary>A folder is about to move, with everything in it, into another folder outside the trash or to the root.</summary>
public sealed class FolderMovingNotification : FolderCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentKey">The folder it moves into, or null for the root.</param>
    /// <param name="previousParentKey">The folder it is in, or null for the root.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder moves it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FolderMovingNotification(Guid folderKey, string name, Guid? parentKey, Guid? previousParentKey, Guid? userKey, EventMessages messages)
        : base(folderKey, name, userKey, messages)
    {
        ParentKey = parentKey;
        PreviousParentKey = previousParentKey;
    }

    /// <summary>The folder it moves into, or null for the root.</summary>
    public Guid? ParentKey { get; }

    /// <summary>The folder it is in, or null for the root.</summary>
    public Guid? PreviousParentKey { get; }
}

/// <summary>A folder moved, with everything in it.</summary>
public sealed class FolderMovedNotification : FolderNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentKey">The folder it moved into, or null for the root.</param>
    /// <param name="previousParentKey">The folder it was in, or null for the root.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder moved it.</param>
    public FolderMovedNotification(Guid folderKey, string name, Guid? parentKey, Guid? previousParentKey, Guid? userKey)
        : base(folderKey, name, userKey)
    {
        ParentKey = parentKey;
        PreviousParentKey = previousParentKey;
    }

    /// <summary>The folder it moved into, or null for the root.</summary>
    public Guid? ParentKey { get; }

    /// <summary>The folder it was in, or null for the root.</summary>
    public Guid? PreviousParentKey { get; }
}

/// <summary>
/// A folder is about to go to the trash with the folders below it and the forms in them, as they were read before the
/// change. Their published versions stay, and the website answers the forms as missing.
/// </summary>
public sealed class FolderTrashingNotification : FolderCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="folders">The folders below it that go with it.</param>
    /// <param name="forms">The forms in it and in them that go with it.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder trashes it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FolderTrashingNotification(Guid folderKey, string name, IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey, EventMessages messages)
        : base(folderKey, name, userKey, messages)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        Folders = folders;
        Forms = forms;
    }

    /// <summary>The folders below it that go with it.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in it and in them that go with it.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }
}

/// <summary>A folder went to the trash with exactly these folders below it and forms in them.</summary>
public sealed class FolderTrashedNotification : FolderNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="folders">The folders below it that went with it.</param>
    /// <param name="forms">The forms in it and in them that went with it.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder trashed it.</param>
    public FolderTrashedNotification(Guid folderKey, string name, IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey)
        : base(folderKey, name, userKey)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        Folders = folders;
        Forms = forms;
    }

    /// <summary>The folders below it that went with it.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in it and in them that went with it.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }
}

/// <summary>
/// A folder is about to come out of the trash, back into its parent or to the root when the parent is in the trash,
/// with the folders and forms that went to the trash with it, as they were read before the change.
/// </summary>
public sealed class FolderRestoringNotification : FolderCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentKey">The folder it goes back into, or null for the root.</param>
    /// <param name="folders">The folders below it that come back with it.</param>
    /// <param name="forms">The forms in it and in them that come back with it.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder restores it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FolderRestoringNotification(Guid folderKey, string name, Guid? parentKey, IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey, EventMessages messages)
        : base(folderKey, name, userKey, messages)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        ParentKey = parentKey;
        Folders = folders;
        Forms = forms;
    }

    /// <summary>The folder it goes back into, or null for the root.</summary>
    public Guid? ParentKey { get; }

    /// <summary>The folders below it that come back with it.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in it and in them that come back with it.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }
}

/// <summary>A folder came out of the trash with exactly these folders below it and forms in them.</summary>
public sealed class FolderRestoredNotification : FolderNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="parentKey">The folder it went back into, or null for the root.</param>
    /// <param name="folders">The folders below it that came back with it.</param>
    /// <param name="forms">The forms in it and in them that came back with it.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder restored it.</param>
    public FolderRestoredNotification(Guid folderKey, string name, Guid? parentKey, IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey)
        : base(folderKey, name, userKey)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        ParentKey = parentKey;
        Folders = folders;
        Forms = forms;
    }

    /// <summary>The folder it went back into, or null for the root.</summary>
    public Guid? ParentKey { get; }

    /// <summary>The folders below it that came back with it.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in it and in them that came back with it.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }
}

/// <summary>
/// A folder in the trash is about to be deleted for good with the folders below it and the forms in them, each form with
/// everything of it. No form notifications of their own are published for these forms.
/// </summary>
public sealed class FolderDeletingNotification : FolderCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="folders">The folders below it that go with it.</param>
    /// <param name="forms">The forms in it and in them that go with it.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder deletes it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FolderDeletingNotification(Guid folderKey, string name, IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey, EventMessages messages)
        : base(folderKey, name, userKey, messages)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        Folders = folders;
        Forms = forms;
    }

    /// <summary>The folders below it that go with it.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in it and in them that go with it.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }
}

/// <summary>A folder was deleted for good with these folders below it and forms in them.</summary>
public sealed class FolderDeletedNotification : FolderNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folderKey">The folder's key.</param>
    /// <param name="name">Its name.</param>
    /// <param name="folders">The folders below it that went with it.</param>
    /// <param name="forms">The forms in it and in them that went with it.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder deleted it.</param>
    public FolderDeletedNotification(Guid folderKey, string name, IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey)
        : base(folderKey, name, userKey)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        Folders = folders;
        Forms = forms;
    }

    /// <summary>The folders below it that went with it.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in it and in them that went with it.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }
}

/// <summary>
/// The trash is about to be emptied: every folder and form in it is deleted for good, each form with everything of it.
/// No folder or form notifications of their own are published for them.
/// </summary>
public sealed class TrashEmptyingNotification : CancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folders">The folders in the trash.</param>
    /// <param name="forms">The forms in the trash.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder empties it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public TrashEmptyingNotification(IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey, EventMessages messages)
        : base(messages)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        Folders = folders;
        Forms = forms;
        UserKey = userKey;
    }

    /// <summary>The folders in the trash.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms in the trash.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }

    /// <summary>The backoffice user who empties it, or null when Jiron Form Builder does.</summary>
    public Guid? UserKey { get; }
}

/// <summary>The trash was emptied: these folders and forms were deleted for good.</summary>
public sealed class TrashEmptiedNotification : INotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="folders">The folders that went.</param>
    /// <param name="forms">The forms that went.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder emptied it.</param>
    public TrashEmptiedNotification(IReadOnlyList<FolderSummary> folders, IReadOnlyList<FormSummary> forms, Guid? userKey)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(forms);
        Folders = folders;
        Forms = forms;
        UserKey = userKey;
    }

    /// <summary>The folders that went.</summary>
    public IReadOnlyList<FolderSummary> Folders { get; }

    /// <summary>The forms that went.</summary>
    public IReadOnlyList<FormSummary> Forms { get; }

    /// <summary>The backoffice user who emptied it, or null when Jiron Form Builder did.</summary>
    public Guid? UserKey { get; }
}
