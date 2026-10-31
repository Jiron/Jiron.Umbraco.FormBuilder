using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>Whether a version is a saved draft or a publish (ADR-0002).</summary>
internal enum FormVersionKind
{
    /// <summary>A save of the draft.</summary>
    Draft,

    /// <summary>A publish.</summary>
    Published,
}

/// <summary>The stored document of a version (ADR-0002, ADR-0034).</summary>
/// <param name="SchemaVersion">The definition's schema version.</param>
/// <param name="Definition">The definition's JSON.</param>
/// <param name="DefinitionHash">The SHA-256 of the definition's UTF-8 bytes, in lowercase hex.</param>
/// <param name="PublicHash">The SHA-256 of its canonical public projection, in lowercase hex (ADR-0035).</param>
internal sealed record VersionDocument(int SchemaVersion, string Definition, string DefinitionHash, string PublicHash);

/// <summary>A form's row: its name, alias and folder, and its draft and published versions (ADR-0002).</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">The form's key.</param>
/// <param name="FolderId">Its folder, or null at the root.</param>
/// <param name="Alias">Its alias, unique among all forms in any letter case.</param>
/// <param name="Name">Its name.</param>
/// <param name="DraftVersionId">The version row of its draft.</param>
/// <param name="PublishedVersionId">The version row visitors get, or null while unpublished.</param>
/// <param name="PublicRevision">0 until the first publish, then up by one whenever the public projection changes.</param>
/// <param name="ConcurrencyVersion">Raised by every change; a change sends the value it read.</param>
/// <param name="LastEntryNumber">The number of its latest entry.</param>
/// <param name="ThemeAlias">The published version's theme, or null.</param>
/// <param name="IsRestricted">Whether only the grants on it count below its folders (ADR-0013).</param>
/// <param name="TrashedUtc">When it went to the trash, or null.</param>
/// <param name="TrashKey">The trash operation that put it there, its own or its folder's (ADR-0047), or null.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
/// <param name="CreatedByUserKey">Who created it, or null when Jiron Form Builder did.</param>
/// <param name="UpdatedByUserKey">Who changed it last, or null when Jiron Form Builder did.</param>
internal sealed record StoredForm(
    int Id,
    Guid Key,
    int? FolderId,
    string Alias,
    string Name,
    int? DraftVersionId,
    int? PublishedVersionId,
    int PublicRevision,
    int ConcurrencyVersion,
    int LastEntryNumber,
    string? ThemeAlias,
    bool IsRestricted,
    DateTimeOffset? TrashedUtc,
    Guid? TrashKey,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? CreatedByUserKey,
    Guid? UpdatedByUserKey);

/// <summary>A version without its definition, for the version list.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="FormId">The form's id.</param>
/// <param name="Number">Its number, counting up per form from 1.</param>
/// <param name="Kind">Draft or published.</param>
/// <param name="SchemaVersion">The definition's schema version.</param>
/// <param name="DefinitionHash">The SHA-256 of the definition, in lowercase hex.</param>
/// <param name="PublicHash">The SHA-256 of its public projection, in lowercase hex.</param>
/// <param name="PublicRevision">The public revision it published, or null for a draft.</param>
/// <param name="Comment">The editor's comment, or null.</param>
/// <param name="IsPinned">Whether the retention job keeps it.</param>
/// <param name="CreatedUtc">When it was saved.</param>
/// <param name="CreatedByUserKey">Who saved it, or null when Jiron Form Builder did.</param>
/// <param name="PublishedUtc">When it was published, or null for a draft.</param>
/// <param name="PublishedByUserKey">Who published it, or null.</param>
internal sealed record FormVersionInfo(
    int Id,
    int FormId,
    int Number,
    FormVersionKind Kind,
    int SchemaVersion,
    string DefinitionHash,
    string PublicHash,
    int? PublicRevision,
    string? Comment,
    bool IsPinned,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserKey,
    DateTimeOffset? PublishedUtc,
    Guid? PublishedByUserKey);

/// <summary>A draft version the cleanup may remove (ADR-0045).</summary>
/// <param name="FormId">The form's id.</param>
/// <param name="Number">The version's number.</param>
internal sealed record DraftToPrune(int FormId, int Number);

/// <summary>A version with its definition.</summary>
/// <param name="Info">The version.</param>
/// <param name="Definition">The definition's JSON, as stored (never rewritten, ADR-0002).</param>
internal sealed record StoredFormVersion(FormVersionInfo Info, string Definition);

/// <summary>The form and the version that a create, a save or a publish wrote.</summary>
/// <param name="Form">The form after the change.</param>
/// <param name="Version">The new version.</param>
internal sealed record SavedVersion(StoredForm Form, StoredFormVersion Version);

/// <summary>A form to create, with its first draft.</summary>
/// <param name="Key">Its key.</param>
/// <param name="FolderId">Its folder, or null for the root.</param>
/// <param name="Alias">Its alias, 1 to 64 characters, unique among all forms in any letter case.</param>
/// <param name="Name">Its name, 1 to 255 characters.</param>
/// <param name="Draft">The document of version 1.</param>
/// <param name="IsRestricted">Whether it is restricted (ADR-0013).</param>
/// <param name="Comment">A comment on version 1, or null.</param>
/// <param name="UserKey">Who creates it, or null when Jiron Form Builder does.</param>
internal sealed record NewForm(
    Guid Key,
    int? FolderId,
    string Alias,
    string Name,
    VersionDocument Draft,
    bool IsRestricted = false,
    string? Comment = null,
    Guid? UserKey = null);

/// <summary>New values for a form's row.</summary>
/// <param name="Id">The form's id.</param>
/// <param name="ExpectedConcurrencyVersion">The concurrency version the caller read.</param>
/// <param name="Alias">The alias, 1 to 64 characters.</param>
/// <param name="Name">The name, 1 to 255 characters.</param>
/// <param name="FolderId">The folder, or null for the root.</param>
/// <param name="IsRestricted">Whether it is restricted.</param>
/// <param name="UserKey">Who changes it, or null when Jiron Form Builder does.</param>
internal sealed record FormUpdate(int Id, int ExpectedConcurrencyVersion, string Alias, string Name, int? FolderId, bool IsRestricted, Guid? UserKey);

/// <summary>Which forms a list holds.</summary>
internal sealed record FormFilter
{
    /// <summary>Every form outside the trash.</summary>
    public static FormFilter Live { get; } = new() { AllFolders = true };

    /// <summary>True for the forms of every folder; false for those directly in <see cref="FolderId"/>.</summary>
    public bool AllFolders { get; init; }

    /// <summary>The folder (null: the root) whose forms the list holds, unless <see cref="AllFolders"/>.</summary>
    public int? FolderId { get; init; }

    /// <summary>False for the forms outside the trash, true for those in it, null for both.</summary>
    public bool? Trashed { get; init; } = false;

    /// <summary>Text that the name or the alias contains, or null.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Folders whose forms the list leaves out, or null; the forms at the root stay. With <see cref="AllFolders"/> and
    /// <see cref="Trashed"/>, the folders in the trash leave the forms the trash holds on their own (ADR-0050).
    /// </summary>
    public IReadOnlyCollection<int>? OutsideFolderIds { get; init; }

    /// <summary>
    /// The forms a user may see (ADR-0048), applied in the query before paging; null for every form, which only Jiron
    /// Form Builder's own work asks for.
    /// </summary>
    public FormVisibility? Visibility { get; init; }

    /// <summary>True for the forms that have a published version only; false (the default) for every form.</summary>
    public bool PublishedOnly { get; init; }

    /// <summary>The forms with these keys only, at most <see cref="SqlArgs.MaxListLength"/>; null for any form.</summary>
    public IReadOnlyCollection<Guid>? Keys { get; init; }

    /// <summary>The forms a picker's settings allow only (ADR-0129); null for any form.</summary>
    public FormScope? Within { get; init; }
}

/// <summary>
/// The forms a form picker's settings allow (ADR-0129): those directly in one of <paramref name="FolderIds"/> (the allowed
/// folders and every folder below them) and those of <paramref name="FormKeys"/>.
/// </summary>
/// <param name="FolderIds">The folders whose forms count.</param>
/// <param name="FormKeys">The forms that count wherever they are, at most <see cref="SqlArgs.MaxListLength"/>.</param>
internal sealed record FormScope(IReadOnlyCollection<int> FolderIds, IReadOnlyCollection<Guid> FormKeys);

/// <summary>A field of the catalogue, by which entry values refer to it (ADR-0003).</summary>
/// <param name="FieldKey">The field's key in the definition.</param>
/// <param name="Alias">Its alias, 1 to 64 characters.</param>
/// <param name="Label">Its label in the default culture, up to 500 characters.</param>
/// <param name="Type">Its field type's alias, 1 to 128 characters.</param>
/// <param name="StorageKind">Which typed column of the entry values holds its values.</param>
/// <param name="IsSensitive">Whether its values are sensitive (ADR-0009).</param>
/// <param name="SortOrder">Its place among the form's columns.</param>
/// <param name="FirstVersionNumber">The first published version that has it, or null while only a draft has it.</param>
/// <param name="LastVersionNumber">The last published version that had it, or null while the live version has it.</param>
/// <param name="ParentFieldKey">The repeater or grid it belongs to, or null.</param>
internal sealed record CatalogueFieldValues(
    Guid FieldKey,
    string Alias,
    string Label,
    string Type,
    FieldStorageKind StorageKind,
    bool IsSensitive,
    int SortOrder,
    int? FirstVersionNumber,
    int? LastVersionNumber,
    Guid? ParentFieldKey);

/// <summary>A stored field of the catalogue.</summary>
/// <param name="Id">The row's id, which entry values refer to.</param>
/// <param name="FormId">The form's id.</param>
/// <param name="Values">The field.</param>
internal sealed record CatalogueField(int Id, int FormId, CatalogueFieldValues Values);

/// <summary>
/// A form with its versions and its field catalogue, the one aggregate of <c>jfbForm</c>, <c>jfbFormVersion</c> and
/// <c>jfbFormField</c>. Every change to the form's row (create, save, publish, rename, move, trash) raises
/// <see cref="StoredForm.ConcurrencyVersion"/> and sends the one the caller read: a stale one throws
/// <see cref="FormBuilderConcurrencyException"/> and writes nothing (ADR-0002). A missing form gives null or false; a
/// change refused by other rows throws <see cref="FormBuilderConflictException"/>. A service combines several of these
/// into one transaction by running them inside one unit of work (ADR-0036).
/// </summary>
internal interface IFormRepository
{
    /// <summary>
    /// Creates a form with its version 1, a draft. Refused with <see cref="FormBuilderConflict.DuplicateAlias"/>,
    /// <see cref="FormBuilderConflict.DuplicateKey"/> or <see cref="FormBuilderConflict.MissingFolder"/>, also for a
    /// folder in the trash.
    /// </summary>
    Task<SavedVersion> CreateAsync(NewForm form, CancellationToken cancellationToken = default);

    /// <summary>A form by key, or null.</summary>
    Task<StoredForm?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>A form by id, or null.</summary>
    Task<StoredForm?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>A form by alias in any letter case (ADR-0033), trashed ones included, or null.</summary>
    Task<StoredForm?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>A page of the forms that <paramref name="filter"/> picks, by name.</summary>
    Task<Page<StoredForm>> ListAsync(FormFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which of these folders hold a form in the trash (<paramref name="trashed"/>) or outside it that
    /// <paramref name="visibility"/> holds (null: any form): whether a folder of the library tree has forms to show (ADR-0050).
    /// </summary>
    Task<IReadOnlySet<int>> ListFolderIdsWithFormsAsync(IReadOnlyCollection<int> folderIds, bool trashed, FormVisibility? visibility, CancellationToken cancellationToken = default);

    /// <summary>
    /// The folders that directly hold a published form outside the trash that <paramref name="visibility"/> holds: where a
    /// form picker has something to pick (ADR-0013).
    /// </summary>
    Task<IReadOnlySet<int>> ListFolderIdsWithPublishedFormsAsync(FormVisibility? visibility, CancellationToken cancellationToken = default);

    /// <summary>
    /// The aliases of every form, trashed ones included, that start with <paramref name="prefix"/> in any letter case
    /// (ADR-0033), for choosing a free one (ADR-0044).
    /// </summary>
    Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames, moves or restricts a form. Null when it does not exist. Refused with
    /// <see cref="FormBuilderConflict.DuplicateAlias"/>, with <see cref="FormBuilderConflict.MissingFolder"/> when it moves
    /// to a folder that is missing or in the trash, and with <see cref="FormBuilderConflict.Trashed"/> while the form is in
    /// the trash.
    /// </summary>
    Task<StoredForm?> UpdateAsync(FormUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new draft version and makes it the form's draft. Null when the form does not exist. Refused with
    /// <see cref="FormBuilderConflict.Trashed"/> while the form is in the trash.
    /// </summary>
    Task<SavedVersion?> SaveDraftAsync(int formId, int expectedConcurrencyVersion, VersionDocument draft, string? comment, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a published version and makes it the one visitors get. Its public revision stays the one of the last
    /// publish when the public hash is the same, and goes up by one otherwise (1 for the first publish, ADR-0002).
    /// Null when the form does not exist. Refused with <see cref="FormBuilderConflict.Trashed"/> while the form is in the
    /// trash.
    /// </summary>
    Task<SavedVersion?> PublishAsync(int formId, int expectedConcurrencyVersion, VersionDocument document, string? themeAlias, string? comment, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The number of the form's last published version before <paramref name="beforeNumber"/>, or null when no version
    /// before it was published.
    /// </summary>
    Task<int?> GetLastPublishedNumberAsync(int formId, int beforeNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the form offline: its versions and entries stay. An unpublished form stays as it is. Null when it does not exist.
    /// A form in the trash can be taken offline too, which lets go of its theme (ADR-0044).
    /// </summary>
    Task<StoredForm?> UnpublishAsync(int formId, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a form in the trash with a trash key of its own (ADR-0047); a trashed one stays as it is. Null when it does
    /// not exist.
    /// </summary>
    Task<StoredForm?> TrashAsync(int formId, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a form out of the trash on its own, into <paramref name="landingFolderId"/>: its folder, which the caller
    /// found outside the trash, or null for the root, which the caller found it goes to as its folder is in the trash.
    /// The statement reads the folder with a lock and refuses with <see cref="FormBuilderConflict.LandingChanged"/> when
    /// it finds the other answer. A form outside the trash stays as it is. Null when it does not exist.
    /// </summary>
    Task<StoredForm?> RestoreAsync(int formId, int expectedConcurrencyVersion, int? landingFolderId, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the forms in these folders that are outside the trash in the trash with the folders' trash key, in the unit
    /// of work that trashed the folders (ADR-0047); each one's concurrency version goes up. The forms it trashed.
    /// </summary>
    Task<IReadOnlyList<StoredForm>> TrashInFoldersAsync(IReadOnlyCollection<int> folderIds, Guid trashKey, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the forms in these folders that carry <paramref name="trashKey"/> out of the trash, in the unit of work that
    /// restored the folders (ADR-0047); they stay in their folders, and each one's concurrency version goes up. The forms
    /// it restored.
    /// </summary>
    Task<IReadOnlyList<StoredForm>> RestoreInFoldersAsync(IReadOnlyCollection<int> folderIds, Guid trashKey, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The forms directly in these folders, in the trash or not, by name: what a change of the folders takes with it
    /// (ADR-0047).
    /// </summary>
    Task<IReadOnlyList<StoredForm>> ListInFoldersAsync(IReadOnlyCollection<int> folderIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The first write of a unit that deletes a batch of a form's entries (ADR-0047): it changes nothing, but holds the
    /// form's row until the unit ends, while the form is in the trash at the version the caller read. Null when it does
    /// not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile, and with
    /// <see cref="FormBuilderConflict.NotTrashed"/> outside the trash.
    /// </summary>
    Task<StoredForm?> HoldTrashedAsync(int formId, int expectedConcurrencyVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a form in the trash at the version the caller read, with its versions, catalogue and option places. False
    /// when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile, with
    /// <see cref="FormBuilderConflict.NotTrashed"/> outside the trash, and with <see cref="FormBuilderConflict.InUse"/>
    /// while it has entries, which the service deletes first (ADR-0047).
    /// </summary>
    Task<bool> DeleteAsync(int formId, int expectedConcurrencyVersion, CancellationToken cancellationToken = default);

    /// <summary>A version by its number, or null.</summary>
    Task<StoredFormVersion?> GetVersionAsync(int formId, int number, CancellationToken cancellationToken = default);

    /// <summary>A version by its row's id, or null.</summary>
    Task<StoredFormVersion?> GetVersionByIdAsync(int versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest published version of a form that published this public revision, or null when none did: the version
    /// a submission against an older revision is mapped through (ADR-0002, ADR-0088).
    /// </summary>
    Task<StoredFormVersion?> GetRevisionAsync(int formId, int publicRevision, CancellationToken cancellationToken = default);

    /// <summary>
    /// The versions of a form with these numbers, newest first, at most <see cref="FormRepository.MaxVersionsAtOnce"/>;
    /// the numbers it does not have are left out.
    /// </summary>
    Task<IReadOnlyList<StoredFormVersion>> GetVersionsAsync(int formId, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default);

    /// <summary>A page of a form's versions without their definitions, newest first, of one kind or, when null, of both.</summary>
    Task<Page<FormVersionInfo>> ListVersionsAsync(int formId, FormVersionKind? kind, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins or unpins a version. The form's row stays as it is (ADR-0045). Null when the version does not exist. Refused
    /// with <see cref="FormBuilderConflict.Trashed"/> while the form is in the trash.
    /// </summary>
    Task<FormVersionInfo?> PinVersionAsync(int formId, int number, bool isPinned, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a version's comment, or clears it with null. The form's row stays as it is (ADR-0045). Null when the version
    /// does not exist. Refused with <see cref="FormBuilderConflict.Trashed"/> while the form is in the trash.
    /// </summary>
    Task<FormVersionInfo?> CommentVersionAsync(int formId, int number, string? comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nominates up to <paramref name="take"/> draft versions for the cleanup (ADR-0045), by form and number: drafts past
    /// the newest <paramref name="keepNewest"/> draft versions of their form, saved before
    /// <paramref name="createdBefore"/>, not pinned, not a form's draft and without entries. Each is removed with
    /// <see cref="PruneDraftAsync"/>, which checks again what may have changed since.
    /// </summary>
    Task<IReadOnlyList<DraftToPrune>> ListDraftsToPruneAsync(DateTimeOffset createdBefore, int keepNewest, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a draft version the retention job may remove (ADR-0002): not pinned, not the form's draft, and no entry
    /// points at it. False when there is no such version; published versions always stay.
    /// </summary>
    Task<bool> PruneDraftAsync(int formId, int number, CancellationToken cancellationToken = default);

    /// <summary>A form's catalogue, in column order.</summary>
    Task<IReadOnlyList<CatalogueField>> ListFieldsAsync(int formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a field to a form's catalogue. Null when the form does not exist. Refused with
    /// <see cref="FormBuilderConflict.DuplicateKey"/> when the catalogue has the field already.
    /// </summary>
    Task<CatalogueField?> AddFieldAsync(int formId, CatalogueFieldValues field, CancellationToken cancellationToken = default);

    /// <summary>Updates a field of a form's catalogue by its key. Null when it does not exist.</summary>
    Task<CatalogueField?> UpdateFieldAsync(int formId, CatalogueFieldValues field, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a field from a form's catalogue. False when it does not exist. Refused with
    /// <see cref="FormBuilderConflict.InUse"/> while entry values refer to it.
    /// </summary>
    Task<bool> DeleteFieldAsync(int formId, Guid fieldKey, CancellationToken cancellationToken = default);
}
