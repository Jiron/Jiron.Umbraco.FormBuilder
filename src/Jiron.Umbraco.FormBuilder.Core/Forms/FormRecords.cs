using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A form as the service answers with it: named by keys, never by row ids (ADR-0044).</summary>
/// <param name="Key">The form's key.</param>
/// <param name="Alias">Its alias, unique among all forms in any letter case.</param>
/// <param name="Name">Its name.</param>
/// <param name="FolderKey">Its folder, or null at the root.</param>
/// <param name="ConcurrencyVersion">Raised by every change; a change sends the value it read.</param>
/// <param name="IsPublished">Whether visitors get a version of it.</param>
/// <param name="PublicRevision">0 until the first publish, then up by one whenever what visitors see changes.</param>
/// <param name="IsRestricted">Whether only the grants on it count below its folders (ADR-0013).</param>
/// <param name="TrashedUtc">When it went to the trash, or null.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
/// <param name="CreatedByUserKey">Who created it, or null when Jiron Form Builder did.</param>
/// <param name="UpdatedByUserKey">Who changed it last, or null when Jiron Form Builder did.</param>
internal sealed record FormInfo(
    Guid Key,
    string Alias,
    string Name,
    Guid? FolderKey,
    int ConcurrencyVersion,
    bool IsPublished,
    int PublicRevision,
    bool IsRestricted,
    DateTimeOffset? TrashedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? CreatedByUserKey,
    Guid? UpdatedByUserKey);

/// <summary>A form's published version as the website runs it (ADR-0060).</summary>
/// <param name="Form">The form.</param>
/// <param name="Definition">The published version, read, upgraded and with the host's designer locks applied.</param>
/// <param name="Version">The number of the published version.</param>
internal sealed record PublishedForm(FormInfo Form, FormDefinition Definition, int Version);

/// <summary>A form opened for editing: its draft with what the designer needs to know about it.</summary>
/// <param name="Form">The form.</param>
/// <param name="Definition">
/// The draft, read with the highest limits a host may set and upgraded to the current schema (ADR-0042); null when the
/// stored document cannot be read at all.
/// </param>
/// <param name="Problems">
/// Why a save of the draft as it is would be refused now, such as <c>limit_exceeded</c> under a lowered limit, or why it
/// cannot be read; empty for almost every form.
/// </param>
/// <param name="DraftVersion">The number of the draft's version.</param>
/// <param name="PublishedVersion">The number of the version visitors get, or null while unpublished.</param>
/// <param name="HasUnpublishedChanges">Whether the draft differs from the version visitors get, or the form is unpublished.</param>
internal sealed record FormDraft(
    FormInfo Form,
    FormDefinition? Definition,
    IReadOnlyList<DefinitionProblem> Problems,
    int DraftVersion,
    int? PublishedVersion,
    bool HasUnpublishedChanges);

/// <summary>What a form's permanent delete removed (ADR-0047).</summary>
/// <param name="Key">The form's key.</param>
/// <param name="Entries">How many entries went with it.</param>
/// <param name="Files">How many files went with it, the uploads that waited for an entry included.</param>
internal sealed record FormDeletion(Guid Key, long Entries, long Files);

/// <summary>A form to create from blank (ADR-0044).</summary>
/// <param name="Name">Its name: 1 to 255 characters, not only white space.</param>
/// <param name="Alias">Its alias, or null to take one from the name.</param>
/// <param name="FolderKey">A folder outside the trash, or null for the root.</param>
/// <param name="UserKey">Who creates it, or null when Jiron Form Builder does.</param>
internal sealed record NewFormRequest(string Name, string? Alias = null, Guid? FolderKey = null, Guid? UserKey = null);

/// <summary>A version of a form, named by its number (ADR-0045).</summary>
/// <param name="Number">Its number, counting up per form from 1.</param>
/// <param name="Kind">A draft save or a publish.</param>
/// <param name="PublicRevision">The public revision a publish gave visitors, or null for a draft save.</param>
/// <param name="Comment">The editor's comment, or null.</param>
/// <param name="IsPinned">Whether it is pinned, so the cleanup keeps it.</param>
/// <param name="CreatedUtc">When it was saved.</param>
/// <param name="CreatedByUserKey">Who saved it, or null when Jiron Form Builder did.</param>
/// <param name="PublishedUtc">When it was published, or null for a draft save.</param>
/// <param name="PublishedByUserKey">Who published it, or null.</param>
/// <param name="IsDraft">Whether it is the form's draft.</param>
/// <param name="IsLive">Whether it is the version visitors get.</param>
internal sealed record FormVersion(
    int Number,
    FormVersionKind Kind,
    int? PublicRevision,
    string? Comment,
    bool IsPinned,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserKey,
    DateTimeOffset? PublishedUtc,
    Guid? PublishedByUserKey,
    bool IsDraft,
    bool IsLive);

/// <summary>A version as the History lists it, with what changed (ADR-0045).</summary>
/// <param name="Version">The version.</param>
/// <param name="Changes">
/// What changed: for a publish, since the publish before it; for a draft save, since the version before it. Null for the
/// first of either, and when one of the two cannot be read.
/// </param>
internal sealed record FormVersionEntry(FormVersion Version, IReadOnlyList<DefinitionChangeSummary>? Changes);

/// <summary>A version with its definition.</summary>
/// <param name="Version">The version.</param>
/// <param name="Definition">Its definition, read with the highest limits a host may set and upgraded; null when it cannot be read.</param>
/// <param name="Problems">Why restoring it would be refused now, or why it cannot be read; usually empty.</param>
internal sealed record FormVersionDocument(FormVersion Version, FormDefinition? Definition, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>What changed from one version of a form to another (ADR-0045).</summary>
/// <param name="FromVersion">The older version's number.</param>
/// <param name="ToVersion">The newer version's number.</param>
/// <param name="Changes">The elements that differ.</param>
internal sealed record FormComparison(int FromVersion, int ToVersion, IReadOnlyList<DefinitionChange> Changes);

/// <summary>What changed from a version of a form to a definition not saved yet (ADR-0200).</summary>
/// <param name="FromVersion">The version's number.</param>
/// <param name="Changes">The elements that differ.</param>
internal sealed record DraftComparison(int FromVersion, IReadOnlyList<DefinitionChange> Changes);

/// <summary>What a save or a publish did.</summary>
/// <param name="Form">The form after it.</param>
/// <param name="Version">The number of the version it wrote, or null when it wrote nothing because nothing changed.</param>
internal sealed record FormVersionResult(FormInfo Form, int? Version);

/// <summary>A copy of a form to create from its draft (ADR-0046).</summary>
/// <param name="Name">The copy's name: 1 to 255 characters, not only white space.</param>
/// <param name="IncludeActions">Whether the copy gets the form's actions; the caller always chooses.</param>
/// <param name="Alias">The copy's alias, or null to take one from the name.</param>
/// <param name="FolderKey">A folder outside the trash, or null for the root.</param>
/// <param name="UserKey">Who duplicates the form, or null when Jiron Form Builder does.</param>
internal sealed record DuplicateFormRequest(string Name, bool IncludeActions, string? Alias = null, Guid? FolderKey = null, Guid? UserKey = null);

/// <summary>A form to create from a built-in or saved template (ADR-0046).</summary>
/// <param name="TemplateKey">The template.</param>
/// <param name="Name">The form's name, or null for the template's.</param>
/// <param name="Alias">Its alias, or null to take one from the name.</param>
/// <param name="FolderKey">A folder outside the trash, or null for the root.</param>
/// <param name="UserKey">Who creates it, or null when Jiron Form Builder does.</param>
internal sealed record TemplateFormRequest(Guid TemplateKey, string? Name = null, string? Alias = null, Guid? FolderKey = null, Guid? UserKey = null);

/// <summary>A form to create from an export file, always with new keys (ADR-0046).</summary>
/// <param name="Json">The file's text.</param>
/// <param name="Name">The form's name, or null for the one in the file.</param>
/// <param name="Alias">
/// Its alias, or null for the one in the file (or one from the name when the file's is not valid), with the next free
/// number when another form has it.
/// </param>
/// <param name="FolderKey">A folder outside the trash, or null for the root.</param>
/// <param name="UserKey">
/// Who imports it, or null when Jiron Form Builder does; the import creates the option lists the file carries only for
/// a user who may manage the library (ADR-0078).
/// </param>
internal sealed record ImportFormRequest(string Json, string? Name = null, string? Alias = null, Guid? FolderKey = null, Guid? UserKey = null)
{
    /// <summary>
    /// Whether the new form keeps the file's keys, its own key among them, instead of new ones: for the mirror, whose file
    /// is the form on another site (ADR-0220). A copy never does, so two forms never share a key (ADR-0046).
    /// </summary>
    public bool KeepKeys { get; init; }
}

/// <summary>An export file to replace a form's draft with, keeping the file's keys (ADR-0220).</summary>
/// <param name="Key">The form whose draft it replaces.</param>
/// <param name="Json">The file.</param>
/// <param name="ExpectedConcurrencyVersion">The form's concurrency version as the editor read it.</param>
/// <param name="UserKey">Who replaces it; null for Jiron Form Builder's own work, as the mirror's.</param>
internal sealed record ReplaceFormRequest(Guid Key, string Json, int ExpectedConcurrencyVersion, Guid? UserKey = null);

/// <summary>What a replace saved, and what it changed or could not resolve (ADR-0220).</summary>
/// <param name="Result">The form and the draft version saved; no version when the file held the draft as it was.</param>
/// <param name="Notes">What the backoffice tells the editor, in file order; empty when everything came along.</param>
internal sealed record FormReplace(FormVersionResult Result, IReadOnlyList<FormImportNote> Notes);

/// <summary>What an import made, and what it changed or could not resolve (ADR-0046).</summary>
/// <param name="Form">The new form.</param>
/// <param name="Notes">What the backoffice tells the editor, in file order; empty when everything came along.</param>
internal sealed record FormImport(FormInfo Form, IReadOnlyList<FormImportNote> Notes);

/// <summary>Something an import changed or could not resolve.</summary>
/// <param name="Code">One of <see cref="FormImportNotes"/>.</param>
/// <param name="Path">Where it is in the file.</param>
/// <param name="Alias">The alias of the theme, option list or action type it is about or the key of the configuration, or null when the file does not name one.</param>
internal sealed record FormImportNote(string Code, string Path, string? Alias);

/// <summary>The codes of an import's notes (ADR-0046).</summary>
internal static class FormImportNotes
{
    /// <summary>The site has no theme with the file's alias, so the form uses the default theme.</summary>
    public const string ThemeMissing = "theme_missing";

    /// <summary>The site's option list with the same alias and options takes the place of the file's list.</summary>
    public const string OptionListReused = "option_list_reused";

    /// <summary>
    /// The file's option list was created with the form, under the alias the note names (ADR-0078): the user may manage
    /// the library, and the list's kind's checks on this site took it.
    /// </summary>
    public const string OptionListCreated = "option_list_created";

    /// <summary>
    /// The site has no such option list, and the import did not create it: the file does not carry it, the user may not
    /// manage the library, or the list's kind's checks on this site refused it (ADR-0078). The fields keep its key.
    /// </summary>
    public const string OptionListMissing = "option_list_missing";

    /// <summary>
    /// The site lacks configuration the action needs, so it arrives switched off (ADR-0046, ADR-0158): the path names the
    /// action, and the alias the configuration's key.
    /// </summary>
    public const string ActionConfigurationMissing = "action_configuration_missing";

    /// <summary>
    /// The site has no action type of the alias the note names, so the action arrives switched off (ADR-0158); publishing
    /// refuses the form until it has one, or the action is removed.
    /// </summary>
    public const string ActionTypeMissing = "action_type_missing";

    /// <summary>
    /// An outcome sends visitors to a page this site does not have, or has in its recycle bin (ADR-0166): the path names
    /// the outcome, which keeps the page's key until the editor chooses one of this site's pages.
    /// </summary>
    public const string PageMissing = "page_missing";
}

/// <summary>What an import would make, before it makes it (ADR-0166): what the file holds, and the import's notes.</summary>
/// <param name="Name">The form's name in the file.</param>
/// <param name="Alias">The form's alias in the file.</param>
/// <param name="PackageVersion">The version of Jiron Form Builder that wrote the file.</param>
/// <param name="ExportedUtc">When it was written.</param>
/// <param name="Counts">What its definition holds.</param>
/// <param name="Notes">What the import would change or could not resolve, as <see cref="FormImport.Notes"/> says it.</param>
internal sealed record FormImportCheck(string Name, string Alias, string PackageVersion, DateTimeOffset ExportedUtc, DefinitionCounts Counts, IReadOnlyList<FormImportNote> Notes);

/// <summary>An export file (ADR-0046).</summary>
/// <param name="FileName">Its name: the form's alias with <c>.json</c>.</param>
/// <param name="Json">Its text.</param>
internal sealed record FormExport(string FileName, string Json);

/// <summary>Actions to copy from one form's draft to another's (ADR-0046).</summary>
/// <param name="SourceKey">The form the actions come from.</param>
/// <param name="TargetKey">The form they go to.</param>
/// <param name="ExpectedConcurrencyVersion">The target's concurrency version the copy is based on.</param>
/// <param name="ActionKeys">The source's actions to copy, or null for all; the copies keep the source's order.</param>
/// <param name="UserKey">Who copies them, or null when Jiron Form Builder does.</param>
internal sealed record CopyActionsRequest(Guid SourceKey, Guid TargetKey, int ExpectedConcurrencyVersion, IReadOnlyList<Guid>? ActionKeys = null, Guid? UserKey = null);

/// <summary>What copying actions did (ADR-0046).</summary>
/// <param name="Target">The target after the copy; its version is null when there was nothing to copy.</param>
/// <param name="Actions">
/// Each copy, with the references it keeps because the target has nothing that matches them, and the merge tags in its
/// settings that name a field the target lacks.
/// </param>
internal sealed record FormActionsCopy(FormVersionResult Target, IReadOnlyList<CopiedAction> Actions);
