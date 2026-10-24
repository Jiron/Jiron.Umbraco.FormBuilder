using Jiron.Umbraco.FormBuilder.Core.Definitions;

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

/// <summary>A form to create from blank (ADR-0044).</summary>
/// <param name="Name">Its name: 1 to 255 characters, not only white space.</param>
/// <param name="Alias">Its alias, or null to take one from the name.</param>
/// <param name="FolderKey">A folder outside the trash, or null for the root.</param>
/// <param name="UserKey">Who creates it, or null when Jiron Form Builder does.</param>
internal sealed record NewFormRequest(string Name, string? Alias = null, Guid? FolderKey = null, Guid? UserKey = null);

/// <summary>What a save or a publish did.</summary>
/// <param name="Form">The form after it.</param>
/// <param name="Version">The number of the version it wrote, or null when it wrote nothing because nothing changed.</param>
internal sealed record FormVersionResult(FormInfo Form, int? Version);
