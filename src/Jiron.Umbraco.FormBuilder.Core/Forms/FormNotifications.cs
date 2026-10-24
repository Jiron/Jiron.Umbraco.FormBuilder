using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// A notification about a change to a form that has happened: published on the server that made the change, after it
/// committed (ADR-0044). A handler that fails is logged; the change stands.
/// </summary>
public abstract class FormNotification : INotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias after the change.</param>
    /// <param name="name">The form's name after the change.</param>
    /// <param name="userKey">The backoffice user who made the change, or null when Jiron Form Builder did.</param>
    protected FormNotification(Guid formKey, string alias, string name, Guid? userKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        ArgumentException.ThrowIfNullOrEmpty(name);
        FormKey = formKey;
        Alias = alias;
        Name = name;
        UserKey = userKey;
    }

    /// <summary>The form's key.</summary>
    public Guid FormKey { get; }

    /// <summary>The form's alias after the change.</summary>
    public string Alias { get; }

    /// <summary>The form's name after the change.</summary>
    public string Name { get; }

    /// <summary>The backoffice user who made the change, or null when Jiron Form Builder did.</summary>
    public Guid? UserKey { get; }
}

/// <summary>
/// A notification about a change to a form that is about to happen: published before anything is written (ADR-0044). A
/// handler stops the change with <see cref="CancelableNotification.CancelOperation(EventMessage)"/>, whose message the
/// refusal carries.
/// </summary>
public abstract class FormCancelableNotification : CancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias after the change.</param>
    /// <param name="name">The form's name after the change.</param>
    /// <param name="userKey">The backoffice user who makes the change, or null when Jiron Form Builder does.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    protected FormCancelableNotification(Guid formKey, string alias, string name, Guid? userKey, EventMessages messages)
        : base(messages)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        ArgumentException.ThrowIfNullOrEmpty(name);
        FormKey = formKey;
        Alias = alias;
        Name = name;
        UserKey = userKey;
    }

    /// <summary>The form's key.</summary>
    public Guid FormKey { get; }

    /// <summary>The form's alias after the change.</summary>
    public string Alias { get; }

    /// <summary>The form's name after the change.</summary>
    public string Name { get; }

    /// <summary>The backoffice user who makes the change, or null when Jiron Form Builder does.</summary>
    public Guid? UserKey { get; }
}

/// <summary>A form is about to be created, or its draft about to be saved.</summary>
public sealed class FormSavingNotification : FormCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="definition">The definition to be saved; its key is the form's.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="isNew">True when the save creates the form.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder saves.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FormSavingNotification(FormDefinition definition, string alias, string name, bool isNew, Guid? userKey, EventMessages messages)
        : base(ArgumentOrThrow(definition).Key, alias, name, userKey, messages)
    {
        Definition = definition;
        IsNew = isNew;
    }

    /// <summary>The definition to be saved.</summary>
    public FormDefinition Definition { get; }

    /// <summary>True when the save creates the form.</summary>
    public bool IsNew { get; }

    private static FormDefinition ArgumentOrThrow(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition;
    }
}

/// <summary>A form was created, or its draft saved, as a new version.</summary>
public sealed class FormSavedNotification : FormNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="version">The number of the draft version the save wrote.</param>
    /// <param name="isNew">True when the save created the form.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder saved.</param>
    public FormSavedNotification(Guid formKey, string alias, string name, int version, bool isNew, Guid? userKey)
        : base(formKey, alias, name, userKey)
    {
        Version = version;
        IsNew = isNew;
    }

    /// <summary>The number of the draft version the save wrote.</summary>
    public int Version { get; }

    /// <summary>True when the save created the form.</summary>
    public bool IsNew { get; }
}

/// <summary>A form's draft is about to be published, after it passed every publish check.</summary>
public sealed class FormPublishingNotification : FormCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="definition">The definition to be published.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder publishes.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FormPublishingNotification(FormDefinition definition, string alias, string name, Guid? userKey, EventMessages messages)
        : base(ArgumentOrThrow(definition).Key, alias, name, userKey, messages)
    {
        Definition = definition;
    }

    /// <summary>The definition to be published.</summary>
    public FormDefinition Definition { get; }

    private static FormDefinition ArgumentOrThrow(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition;
    }
}

/// <summary>A form was published: visitors get the new version.</summary>
public sealed class FormPublishedNotification : FormNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="version">The number of the published version.</param>
    /// <param name="publicRevision">The public revision visitors get (ADR-0002).</param>
    /// <param name="revisionChanged">Whether the revision moved, because visitors see something different.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder published.</param>
    public FormPublishedNotification(Guid formKey, string alias, string name, int version, int publicRevision, bool revisionChanged, Guid? userKey)
        : base(formKey, alias, name, userKey)
    {
        Version = version;
        PublicRevision = publicRevision;
        RevisionChanged = revisionChanged;
    }

    /// <summary>The number of the published version.</summary>
    public int Version { get; }

    /// <summary>The public revision visitors get (ADR-0002).</summary>
    public int PublicRevision { get; }

    /// <summary>Whether the revision moved, because visitors see something different.</summary>
    public bool RevisionChanged { get; }
}

/// <summary>A published form is about to be taken offline; its versions and entries stay.</summary>
public sealed class FormUnpublishingNotification : FormCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder unpublishes.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FormUnpublishingNotification(Guid formKey, string alias, string name, Guid? userKey, EventMessages messages)
        : base(formKey, alias, name, userKey, messages)
    {
    }
}

/// <summary>A form was taken offline; its versions and entries stay.</summary>
public sealed class FormUnpublishedNotification : FormNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder unpublished.</param>
    public FormUnpublishedNotification(Guid formKey, string alias, string name, Guid? userKey)
        : base(formKey, alias, name, userKey)
    {
    }
}

/// <summary>
/// A form's name or alias is about to change. A changed alias breaks the Razor views and tag helpers that name the form.
/// </summary>
public sealed class FormRenamingNotification : FormCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The new alias.</param>
    /// <param name="name">The new name.</param>
    /// <param name="previousAlias">The alias before.</param>
    /// <param name="previousName">The name before.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder renames.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FormRenamingNotification(Guid formKey, string alias, string name, string previousAlias, string previousName, Guid? userKey, EventMessages messages)
        : base(formKey, alias, name, userKey, messages)
    {
        ArgumentException.ThrowIfNullOrEmpty(previousAlias);
        ArgumentException.ThrowIfNullOrEmpty(previousName);
        PreviousAlias = previousAlias;
        PreviousName = previousName;
    }

    /// <summary>The alias before.</summary>
    public string PreviousAlias { get; }

    /// <summary>The name before.</summary>
    public string PreviousName { get; }
}

/// <summary>A form's name or alias changed.</summary>
public sealed class FormRenamedNotification : FormNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The new alias.</param>
    /// <param name="name">The new name.</param>
    /// <param name="previousAlias">The alias before.</param>
    /// <param name="previousName">The name before.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder renamed.</param>
    public FormRenamedNotification(Guid formKey, string alias, string name, string previousAlias, string previousName, Guid? userKey)
        : base(formKey, alias, name, userKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(previousAlias);
        ArgumentException.ThrowIfNullOrEmpty(previousName);
        PreviousAlias = previousAlias;
        PreviousName = previousName;
    }

    /// <summary>The alias before.</summary>
    public string PreviousAlias { get; }

    /// <summary>The name before.</summary>
    public string PreviousName { get; }
}

/// <summary>A form is about to move to another folder, or to the root.</summary>
public sealed class FormMovingNotification : FormCancelableNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="folderKey">The folder it moves to, or null for the root.</param>
    /// <param name="previousFolderKey">The folder it is in, or null for the root.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder moves it.</param>
    /// <param name="messages">Where a handler's message goes.</param>
    public FormMovingNotification(Guid formKey, string alias, string name, Guid? folderKey, Guid? previousFolderKey, Guid? userKey, EventMessages messages)
        : base(formKey, alias, name, userKey, messages)
    {
        FolderKey = folderKey;
        PreviousFolderKey = previousFolderKey;
    }

    /// <summary>The folder it moves to, or null for the root.</summary>
    public Guid? FolderKey { get; }

    /// <summary>The folder it is in, or null for the root.</summary>
    public Guid? PreviousFolderKey { get; }
}

/// <summary>A form moved to another folder, or to the root.</summary>
public sealed class FormMovedNotification : FormNotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="alias">The form's alias.</param>
    /// <param name="name">The form's name.</param>
    /// <param name="folderKey">The folder it moved to, or null for the root.</param>
    /// <param name="previousFolderKey">The folder it was in, or null for the root.</param>
    /// <param name="userKey">The backoffice user, or null when Jiron Form Builder moved it.</param>
    public FormMovedNotification(Guid formKey, string alias, string name, Guid? folderKey, Guid? previousFolderKey, Guid? userKey)
        : base(formKey, alias, name, userKey)
    {
        FolderKey = folderKey;
        PreviousFolderKey = previousFolderKey;
    }

    /// <summary>The folder it moved to, or null for the root.</summary>
    public Guid? FolderKey { get; }

    /// <summary>The folder it was in, or null for the root.</summary>
    public Guid? PreviousFolderKey { get; }
}
