namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>
/// Jiron Form Builder's permissions (ADR-0013, ADR-0048), granted to Umbraco user groups globally, per folder or per
/// form. The values are stored in the database and never change; the API, the audit log and the host configuration
/// name each permission by the word in its summary.
/// </summary>
[Flags]
public enum FormBuilderPermissions
{
    /// <summary>No permission.</summary>
    None = 0,

    /// <summary>
    /// <c>design</c>: see forms in the library, create, edit drafts, preview, publish, unpublish, duplicate, move, trash
    /// and restore forms and folders.
    /// </summary>
    Design = 1,

    /// <summary><c>manageActions</c>: add, change and test actions, and retry deliveries. Needs <see cref="Design"/>.</summary>
    ManageActions = 2,

    /// <summary>
    /// <c>viewEntries</c>: list and open entries, notes and the timeline, download files, see insights and answer
    /// summaries.
    /// </summary>
    ViewEntries = 4,

    /// <summary><c>viewSensitive</c>: see sensitive values unmasked, each reveal audited. Needs <see cref="ViewEntries"/>.</summary>
    ViewSensitive = 8,

    /// <summary><c>editEntries</c>: change values and states, add notes, forward. Needs <see cref="ViewEntries"/>.</summary>
    EditEntries = 16,

    /// <summary><c>deleteEntries</c>: delete entries and empty Spam. Needs <see cref="ViewEntries"/>.</summary>
    DeleteEntries = 32,

    /// <summary><c>export</c>: export entries and download the archive of their files. Needs <see cref="ViewEntries"/>.</summary>
    Export = 64,

    /// <summary><c>manageLibrary</c>: option lists, themes, email layouts and templates. Granted globally only.</summary>
    ManageLibrary = 128,

    /// <summary><c>manageSettings</c>: the settings sections, the audit log and the deliveries log. Granted globally only.</summary>
    ManageSettings = 256,

    /// <summary><c>managePermissions</c>: grants and restrictions. Granted globally only.</summary>
    ManagePermissions = 512,

    /// <summary>
    /// <c>privacyTools</c>: find, export and erase a person's data across every form, and the retention dry run. Granted
    /// globally only.
    /// </summary>
    PrivacyTools = 1024,
}
