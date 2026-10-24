using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Access;
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

    /// <summary>
    /// Puts a form in the trash on its own (ADR-0047). Its published version stays, and the website answers it as
    /// missing until a restore. A form in the trash already stays as it is. Null when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormInfo?> TrashAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a form out of the trash on its own, back into its folder, or to the root when the folder is in the trash
    /// (ADR-0047). A form outside the trash stays as it is. Null when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormInfo?> RestoreAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a form in the trash for good, with its entries and their files, versions, grants, the references of
    /// content to it and its statistics (ADR-0047). A large form goes in many short units of work; a delete that stops
    /// part way leaves the form in the trash with fewer entries, and deleting it again finishes the job. Null when it
    /// does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderConflictException">The form is outside the trash (<c>not_trashed</c>), or entries kept arriving.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the delete.</exception>
    Task<FormDeletion?> DeleteAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of a form's versions, newest first: the publishes, and the draft saves too when <paramref name="includeDrafts"/>,
    /// each with what changed (ADR-0045). Null when the form does not exist.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page holds more than <see cref="FormBuilderFormService.MaxVersionsPerPage"/> versions.</exception>
    Task<Page<FormVersionEntry>?> ListVersionsAsync(Guid key, bool includeDrafts, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>A version of a form with its definition, or null when the form or the version does not exist.</summary>
    Task<FormVersionDocument?> GetVersionAsync(Guid key, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// What changed from one version of a form to another, both read and upgraded first (ADR-0045). Null when the form or
    /// a version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">A stored version cannot be read.</exception>
    Task<FormComparison?> CompareAsync(Guid key, int fromNumber, int toNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a version's definition into a new draft; the current draft stays in the History (ADR-0045). A version whose
    /// definition is the draft's writes nothing. Null when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">A save refuses the version's definition, for example above a lowered limit.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the save.</exception>
    Task<FormVersionResult?> RestoreVersionAsync(Guid key, int expectedConcurrencyVersion, int number, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins or unpins a version, so the cleanup keeps it. The form's concurrency version stays as it is (ADR-0045). Null
    /// when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormVersion?> PinVersionAsync(Guid key, int number, bool isPinned, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a version's comment; null, empty or blank clears it. The form's concurrency version stays as it is
    /// (ADR-0045). Null when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The comment is longer than 500 characters.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormVersion?> CommentVersionAsync(Guid key, int number, string? comment, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a copy of a form's draft, with or without its actions, in which every element has a new key that the
    /// references follow (ADR-0046). Entries, versions, grants and the restriction stay with the form. Null when the
    /// form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">
    /// The name or the alias is not allowed, or the draft cannot be read or is above a lowered limit.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, the folder is missing or in the trash, or the form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormInfo?> DuplicateAsync(Guid key, DuplicateFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends copies of a form's actions to another form's draft as a draft save: the fields they name map by alias,
    /// and what has no match is kept and reported (ADR-0046). Nothing to copy writes nothing. Null when either form
    /// does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the target meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">
    /// An action key is not the source's, a draft cannot be read, or a save refuses the target with the copies.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The source or the target is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the save.</exception>
    Task<FormActionsCopy?> CopyActionsAsync(CopyActionsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a form from a built-in or saved form template, with new keys and the new-form defaults (ADR-0046). Null
    /// when no form template has the key.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The name or the alias is not allowed, or a save refuses the template.</exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, or the folder is missing or in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormInfo?> CreateFromTemplateAsync(TemplateFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a form from an export file, always with new keys, and says what it changed or could not resolve
    /// (ADR-0046).
    /// </summary>
    /// <exception cref="FormBuilderValidationException">
    /// The name or the alias is not allowed, or the file is refused, with every problem and its path in the file.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, or the folder is missing or in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormImport> ImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The export file of a form's draft, or of the version with this number (ADR-0046), with the library option lists
    /// it names. Null when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The stored version cannot be read.</exception>
    Task<FormExport?> ExportAsync(Guid key, int? version = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed partial class FormBuilderFormService(
    IFormRepository forms,
    IFolderRepository folders,
    IOptionListRepository optionLists,
    IThemeRepository themes,
    IFormTemplateRepository templates,
    IAuditRepository audit,
    FormBuilderDatabase database,
    FormDefinitionSerializer serializer,
    FieldTypeCatalog types,
    IEnumerable<IFormPublishCheck> publishChecks,
    IFormBuilderSettingsService settings,
    FormRemoval removal,
    FormBuilderAccessCache access,
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

        if (!inTrash)
        {
            CheckOutsideTrash(form);
        }
    }

    // Only a restore, an unpublish or a delete applies to a form in the trash; copies of it wait for its restore too.
    private static void CheckOutsideTrash(StoredForm form)
    {
        if (form.TrashedUtc is not null)
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

    // A form's name (ADR-0044). The path is where a refusal points: the request's name, or the name in an export file.
    private static string CheckName(string name, string path = "name") => LibraryNames.Check(name, "form", FormRepository.MaxNameLength, path);

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
