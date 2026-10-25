using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Creates, saves, publishes and changes forms, and puts them in the trash, back or away for good (ADR-0044, ADR-0047);
/// their versions are <see cref="IFormBuilderFormVersionService"/>'s, their copies, templates, imports and exports
/// <see cref="IFormBuilderFormExchangeService"/>'s (ADR-0053). Every change is a unit of work of its own, refused inside
/// a caller's unit or an Umbraco scope: the checks that need no write, then the cancelable notification, then one unit
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
    /// Where a restore of the form lands now: its folder while that is outside the trash, otherwise the root; for a form
    /// outside the trash, its folder. Null when the form does not exist.
    /// </summary>
    Task<RestoreLanding?> GetRestoreLandingAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a form out of the trash on its own, back into its folder, or to the root when the folder is in the trash
    /// (ADR-0047). With <paramref name="landing"/>, the landing the caller checked (<see cref="GetRestoreLandingAsync"/>),
    /// it lands there or nowhere. A form outside the trash stays as it is. Null when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderConflictException">
    /// The restore would land elsewhere than <paramref name="landing"/> or than it found before the write (<c>landing_changed</c>).
    /// </exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormInfo?> RestoreAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, RestoreLanding? landing = null, CancellationToken cancellationToken = default);

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
}

/// <inheritdoc />
internal sealed partial class FormBuilderFormService(
    IFormRepository forms,
    IFolderRepository folders,
    FormDocuments documents,
    FormCreation creation,
    FormDefinitionSerializer serializer,
    FieldTypeCatalogue types,
    IEnumerable<IFormPublishCheck> publishChecks,
    FormRemoval removal,
    AccessSnapshotInvalidator access,
    FormBuilderChanges<FormBuilderFormService> changes)
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

    /// <summary>The refusal of a change to a form inside a caller's unit of work or an Umbraco scope.</summary>
    internal const string OwnUnit = "A change to a form is a unit of work of its own: it tells handlers once it has committed (ADR-0044).";

    private readonly IReadOnlyList<IFormPublishCheck> _publishChecks = publishChecks.ToList();

    /// <inheritdoc />
    public async Task<FormInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        await forms.GetAsync(key, cancellationToken) is { } form ? await documents.InfoAsync(form, cancellationToken) : null;

    /// <inheritdoc />
    public async Task<FormInfo?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return await forms.GetByAliasAsync(alias, cancellationToken) is { } form ? await documents.InfoAsync(form, cancellationToken) : null;
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
        var unpublished = live is null || read.Definition is null || documents.HashOf(live) != FormDocuments.HashOf(read.Definition);
        return new FormDraft(await documents.InfoAsync(form, cancellationToken), read.Definition, problems, draft.Info.Number, live?.Info.Number, unpublished);
    }

    /// <summary>What a change names when a handler of its notification fails: the form.</summary>
    internal static string Subject(Guid formKey) => $"the form {formKey}";
}
