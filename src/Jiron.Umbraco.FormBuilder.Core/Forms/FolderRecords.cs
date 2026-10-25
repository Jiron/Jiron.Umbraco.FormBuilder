using Jiron.Umbraco.FormBuilder.Core.Access;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A folder of the form library as the folder service answers with it: named by keys, never by row ids (ADR-0047).</summary>
/// <param name="Key">The folder's key.</param>
/// <param name="Name">Its name.</param>
/// <param name="ParentKey">The folder it is in, or null at the root.</param>
/// <param name="SortOrder">Its place among its siblings.</param>
/// <param name="IsRestricted">Whether only the grants on it and below count below it (ADR-0013).</param>
/// <param name="TrashedUtc">When it went to the trash, or null.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
/// <param name="CreatedByUserKey">Who created it, or null when Jiron Form Builder did.</param>
/// <param name="UpdatedByUserKey">Who changed it last, or null when Jiron Form Builder did.</param>
internal sealed record FolderInfo(
    Guid Key,
    string Name,
    Guid? ParentKey,
    int SortOrder,
    bool IsRestricted,
    DateTimeOffset? TrashedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? CreatedByUserKey,
    Guid? UpdatedByUserKey);

/// <summary>A folder to create (ADR-0047).</summary>
/// <param name="Name">Its name: 1 to 255 characters on one line, trimmed.</param>
/// <param name="ParentKey">The folder outside the trash to create it in, or null for the root.</param>
/// <param name="UserKey">Who creates it, or null when Jiron Form Builder does.</param>
internal sealed record NewFolderRequest(string Name, Guid? ParentKey = null, Guid? UserKey = null);

/// <summary>What a folder's trash or restore changed (ADR-0047).</summary>
/// <param name="Folder">The folder after the change.</param>
/// <param name="Folders">The folders below it that went or came back with it, nearest first; empty when it changed nothing.</param>
/// <param name="Forms">The forms in it and in them that went or came back with it.</param>
internal sealed record FolderChange(FolderInfo Folder, IReadOnlyList<FolderSummary> Folders, IReadOnlyList<FormSummary> Forms);

/// <summary>What a permanent delete of folders and forms removed (ADR-0047).</summary>
/// <param name="Folders">The folders that went, each folder at the top of what went before those below it, nearest first.</param>
/// <param name="Forms">The forms that went, each with everything of it.</param>
/// <param name="Entries">How many entries went with the forms.</param>
/// <param name="Files">How many files went with the forms.</param>
internal sealed record LibraryDeletion(IReadOnlyList<FolderSummary> Folders, IReadOnlyList<FormSummary> Forms, long Entries, long Files);

/// <summary>
/// What a permanent delete would remove (ADR-0050): a folder with the folders below it and the forms in them, or a form
/// in the trash on its own. A user may delete it only with <c>design</c> and <c>deleteEntries</c> on all of it.
/// </summary>
/// <param name="FolderKeys">The folders, the top one first.</param>
/// <param name="Forms">The forms, as the evaluation reads them.</param>
internal sealed record LibraryRemoval(IReadOnlyList<Guid> FolderKeys, IReadOnlyList<FormAccessTarget> Forms);

/// <summary>What emptying the trash removed, and how many of its top items it left (ADR-0050).</summary>
/// <param name="Deleted">What went.</param>
/// <param name="Left">The top items of the trash that stayed: folders with what is below them, and forms on their own.</param>
internal sealed record TrashEmptied(LibraryDeletion Deleted, int Left);
