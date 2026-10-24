using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Creates, saves, publishes and changes forms (ADR-0044). Every change is a unit of work of its own, refused inside a
/// caller's unit or an Umbraco scope: the checks that need no write, then the cancelable notification, then one unit
/// with the change and its audit entry, then the notification that it happened. Stored definitions are read with the
/// highest limits a host may set; what the service writes meets the site's limits (ADR-0042). A missing form gives null;
/// a refusal throws <see cref="FormBuilderValidationException"/>, <see cref="FormBuilderConcurrencyException"/>,
/// <see cref="FormBuilderConflictException"/> or <see cref="FormBuilderCancelledException"/>, each with a code from
/// <see cref="FormBuilderErrorCodes"/>.
/// </summary>
internal interface IFormBuilderFormService
{
    /// <summary>A form by key, or null.</summary>
    Task<FormInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>A form by alias in any letter case (ADR-0033), trashed ones included, or null.</summary>
    Task<FormInfo?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>A form opened for editing, with its draft, or null.</summary>
    Task<FormDraft?> GetDraftAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>Creates a form from blank, with the new-form defaults of the general settings (section 4.8).</summary>
    /// <exception cref="FormBuilderValidationException">The name or the alias is not allowed.</exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, or the folder is missing or in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormInfo> CreateAsync(NewFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new draft. A definition that is the draft already writes nothing. Null when the form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">A save refuses the definition, for example above a lowered limit.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the save.</exception>
    Task<FormVersionResult?> SaveDraftAsync(Guid key, int expectedConcurrencyVersion, FormDefinition definition, string? comment, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the stored draft after every publish check, and updates the field catalogue. A draft that is the live
    /// version already writes nothing. Null when the form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">A publish check refuses the draft.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the publish.</exception>
    Task<FormVersionResult?> PublishAsync(Guid key, int expectedConcurrencyVersion, string? comment, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a form offline; its versions and entries stay. A form in the trash can be taken offline too. Null when it does
    /// not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormInfo?> UnpublishAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>Renames a form, and changes its alias when one is given. Null when it does not exist.</summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">The name or the alias is not allowed.</exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, or the form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormInfo?> RenameAsync(Guid key, int expectedConcurrencyVersion, string name, string? alias, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>Moves a form to a folder outside the trash, or to the root. Null when it does not exist.</summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderConflictException">The folder is missing or in the trash, or the form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormInfo?> MoveAsync(Guid key, int expectedConcurrencyVersion, Guid? folderKey, Guid? userKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed partial class FormBuilderFormService(
    IFormRepository forms,
    IFolderRepository folders,
    IAuditRepository audit,
    FormBuilderDatabase database,
    FormDefinitionSerializer serializer,
    FieldTypeCatalog types,
    IEnumerable<IFormPublishCheck> publishChecks,
    IFormBuilderSettingsService settings,
    IEventAggregator eventAggregator,
    ILogger<FormBuilderFormService> logger)
    : IFormBuilderFormService
{
    /// <summary>The audit entity type of a form; the item is the form's key.</summary>
    public const string EntityType = "form";

    /// <summary>The audit action of a create.</summary>
    public const string CreatedAction = "form.created";

    /// <summary>The audit action of a draft save.</summary>
    public const string SavedAction = "form.saved";

    /// <summary>The audit action of a publish.</summary>
    public const string PublishedAction = "form.published";

    /// <summary>The audit action of an unpublish.</summary>
    public const string UnpublishedAction = "form.unpublished";

    /// <summary>The audit action of a rename.</summary>
    public const string RenamedAction = "form.renamed";

    /// <summary>The audit action of a move.</summary>
    public const string MovedAction = "form.moved";

    /// <summary>How often a create takes the next free alias when a create at the same moment took the one it chose.</summary>
    public const int AliasAttempts = 3;

    private readonly IReadOnlyList<IFormPublishCheck> _publishChecks = publishChecks.ToList();

    /// <inheritdoc />
    public async Task<FormInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        await forms.GetAsync(key, cancellationToken) is { } form ? await InfoAsync(form, cancellationToken) : null;

    /// <inheritdoc />
    public async Task<FormInfo?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return await forms.GetByAliasAsync(alias, cancellationToken) is { } form ? await InfoAsync(form, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task<FormDraft?> GetDraftAsync(Guid key, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form
            || form.DraftVersionId is not { } draftId
            || await forms.GetVersionByIdAsync(draftId, cancellationToken) is not { } draft)
        {
            return null;
        }

        var read = serializer.ReadStored(draft.Definition);
        var problems = read.Definition is { } definition ? serializer.CheckSave(definition) : read.Problems;
        var live = form.PublishedVersionId is { } liveId ? await forms.GetVersionByIdAsync(liveId, cancellationToken) : null;
        var changes = live is null || read.Definition is null || HashOf(live) != HashOf(read.Definition);
        return new FormDraft(await InfoAsync(form, cancellationToken), read.Definition, problems, draft.Info.Number, live?.Info.Number, changes);
    }

    // The stored document of a definition: its canonical form, its hash and the hash of its public projection.
    private VersionDocument Document(FormDefinition definition)
    {
        var canonical = FormDefinitionSerializer.Write(definition);
        return new VersionDocument(
            FormDefinition.CurrentSchemaVersion,
            Encoding.UTF8.GetString(canonical),
            FormDefinitionSerializer.Hash(canonical),
            PublicProjection.Hash(definition, types));
    }

    // The hash of a definition's canonical form in the current schema.
    private static string HashOf(FormDefinition definition) => FormDefinitionSerializer.Hash(FormDefinitionSerializer.Write(definition));

    // The hash of a stored version once read and upgraded, or null when it cannot be read.
    private string? HashOf(StoredFormVersion version) =>
        serializer.ReadStored(version.Definition).Definition is { } definition ? HashOf(definition) : null;

    private async Task<FormInfo> InfoAsync(StoredForm form, CancellationToken cancellationToken) =>
        Info(form, form.FolderId is { } folderId ? (await folders.GetByIdAsync(folderId, cancellationToken))?.Key : null);

    private static FormInfo Info(StoredForm form, Guid? folderKey) => new(
        form.Key,
        form.Alias,
        form.Name,
        folderKey,
        form.ConcurrencyVersion,
        form.PublishedVersionId is not null,
        form.PublicRevision,
        form.IsRestricted,
        form.TrashedUtc,
        form.CreatedUtc,
        form.UpdatedUtc,
        form.CreatedByUserKey,
        form.UpdatedByUserKey);

    // A folder a form may go to: outside the trash. Null for the root.
    private async Task<StoredFolder?> LiveFolderAsync(Guid? folderKey, CancellationToken cancellationToken)
    {
        if (folderKey is not { } key)
        {
            return null;
        }

        return await folders.GetAsync(key, cancellationToken) is { TrashedUtc: null } folder
            ? folder
            : throw new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The folder does not exist or is in the trash.");
    }

    // A change starts from the version the caller read, on a form outside the trash unless it applies in the trash.
    private static void CheckCurrent(StoredForm form, int expectedConcurrencyVersion, bool inTrash = false)
    {
        if (form.ConcurrencyVersion != expectedConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException($"The form was changed by someone else meanwhile (version {form.ConcurrencyVersion}, not {expectedConcurrencyVersion}).");
        }

        if (form.TrashedUtc is not null && !inTrash)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.Trashed, "The form is in the trash; restore it first.");
        }
    }

    private void EnsureOwnUnit()
    {
        if (database.InUnitOfWork)
        {
            throw new InvalidOperationException("A change to a form is a unit of work of its own: it tells handlers once it has committed (ADR-0044).");
        }
    }

    // A form's name: 1 to 255 characters once trimmed, not only white space, on one line.
    private static string CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, "name", "A form needs a name that is not only white space.");
        }

        if (trimmed.Length > FormRepository.MaxNameLength)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.LimitExceeded, "name", $"A form name has at most {FormRepository.MaxNameLength} characters.");
        }

        return trimmed.Any(char.IsControl) || !StrictStringConverter.IsWellFormed(trimmed)
            ? throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, "name", "A form name is one line of text, without control characters.")
            : trimmed;
    }

    private static void CheckAlias(string? alias)
    {
        if (alias is not null && !FormAliases.IsValid(alias))
        {
            throw FormBuilderValidationException.For(DefinitionErrors.AliasInvalid, "alias", "An alias is a lowercase letter, then letters and digits, at most 64 characters.");
        }
    }

    private static void CheckComment(string? comment)
    {
        if (comment is null)
        {
            return;
        }

        if (comment.Length > FormRepository.MaxCommentLength)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.LimitExceeded, "comment", $"A comment has at most {FormRepository.MaxCommentLength} characters.");
        }

        if (!StrictStringConverter.IsWellFormed(comment))
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, "comment", "The comment holds a lone surrogate.");
        }
    }

    private static NewAuditEntry Entry(string action, StoredForm form, Guid? userKey, AuditDetails details) =>
        new(action, EntityType, form.Key, userKey, details);

    // Publishes a notification before a change: a handler that cancels it stops the change.
    private async Task CancelableAsync<T>(T notification)
        where T : FormCancelableNotification
    {
        if (await eventAggregator.PublishCancelableAsync(notification))
        {
            throw new FormBuilderCancelledException(notification.Messages);
        }
    }

    // Publishes a notification after a change committed: a handler that fails is logged, and the change stands.
    private async Task PublishedAsync<T>(T notification)
        where T : FormNotification
    {
        try
        {
            await eventAggregator.PublishAsync(notification, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogHandlerFailed(logger, typeof(T).Name, notification.FormKey, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of {Notification} failed after the form {FormKey} changed; the change stands")]
    private static partial void LogHandlerFailed(ILogger logger, string notification, Guid formKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The consent field of the new-form defaults is not one a definition takes, so the new form {FormKey} starts without it; save the general settings again")]
    private static partial void LogConsentSkipped(ILogger logger, Guid formKey);
}
