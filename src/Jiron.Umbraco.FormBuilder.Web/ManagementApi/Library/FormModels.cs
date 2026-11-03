using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A form, with the signed-in user's permissions on it (ADR-0051).</summary>
internal sealed record FormResponseModel
{
    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias, unique among all forms in any letter case.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The folder it is in, or null at the root.</summary>
    public Guid? FolderKey { get; init; }

    /// <summary>Raised by every change; a change sends the value it read.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>Whether visitors get a version of it.</summary>
    public required bool IsPublished { get; init; }

    /// <summary>0 until the first publish, then up by one whenever what visitors see changes.</summary>
    public required int PublicRevision { get; init; }

    /// <summary>Whether only the grants on it count below its folders.</summary>
    public required bool IsRestricted { get; init; }

    /// <summary>When it went to the trash, or null outside it.</summary>
    public DateTimeOffset? TrashedUtc { get; init; }

    /// <summary>When it was created.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>The permissions the user holds on it, by their stable names.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The model of a form.</summary>
    public static FormResponseModel Of(FormInfo form, FormBuilderPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new FormResponseModel
        {
            Key = form.Key,
            Alias = form.Alias,
            Name = form.Name,
            FolderKey = form.FolderKey,
            ConcurrencyVersion = form.ConcurrencyVersion,
            IsPublished = form.IsPublished,
            PublicRevision = form.PublicRevision,
            IsRestricted = form.IsRestricted,
            TrashedUtc = form.TrashedUtc,
            CreatedUtc = form.CreatedUtc,
            UpdatedUtc = form.UpdatedUtc,
            Permissions = PermissionRules.NamesOf(permissions),
        };
    }
}

/// <summary>A form opened for editing: its draft and what the designer needs to know about it (ADR-0051).</summary>
internal sealed record FormDraftResponseModel
{
    /// <summary>The form.</summary>
    public required FormResponseModel Form { get; init; }

    /// <summary>The draft's definition document in its canonical form, or null when the stored one cannot be read.</summary>
    public JsonElement? Definition { get; init; }

    /// <summary>Why a save of the draft as it is would be refused now, or why it cannot be read; usually empty.</summary>
    public required IReadOnlyList<DefinitionProblemResponseModel> Problems { get; init; }

    /// <summary>The number of the draft's version.</summary>
    public required int DraftVersion { get; init; }

    /// <summary>The number of the version visitors get, or null while unpublished.</summary>
    public int? PublishedVersion { get; init; }

    /// <summary>Whether the draft differs from the version visitors get, or the form is unpublished.</summary>
    public required bool HasUnpublishedChanges { get; init; }

    /// <summary>
    /// Whether the form has entries, in any state: a field's alias changed from then on asks first and keeps the one
    /// before for the merge tags that use it (section 4.3.1). Only yes or no, so a designer who may not view entries learns no
    /// count.
    /// </summary>
    public required bool HasEntries { get; init; }

    /// <summary>The model of a draft.</summary>
    public static FormDraftResponseModel Of(FormDraft draft, FormResponseModel form, bool hasEntries)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new FormDraftResponseModel
        {
            Form = form,
            Definition = draft.Definition is { } definition ? DefinitionJson.Of(definition) : null,
            Problems = [.. draft.Problems.Select(DefinitionProblemResponseModel.Of)],
            DraftVersion = draft.DraftVersion,
            PublishedVersion = draft.PublishedVersion,
            HasUnpublishedChanges = draft.HasUnpublishedChanges,
            HasEntries = hasEntries,
        };
    }
}

/// <summary>A problem of a definition.</summary>
/// <param name="Code">Its stable code, such as <c>limit_exceeded</c>.</param>
/// <param name="Path">Where it is: a JSON path into the definition document.</param>
/// <param name="Message">What is wrong, for editors.</param>
internal sealed record DefinitionProblemResponseModel(string Code, string Path, string Message)
{
    /// <summary>The model of a problem.</summary>
    public static DefinitionProblemResponseModel Of(DefinitionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new DefinitionProblemResponseModel(problem.Code, problem.Path, problem.Message);
    }
}

/// <summary>What a save or a publish did (ADR-0051).</summary>
internal sealed record FormSavedResponseModel
{
    /// <summary>The form after it.</summary>
    public required FormResponseModel Form { get; init; }

    /// <summary>The number of the version it wrote, or null when it wrote nothing because nothing changed.</summary>
    public int? Version { get; init; }
}

/// <summary>A form to create from blank.</summary>
internal sealed record CreateFormRequestModel
{
    /// <summary>Its name: 1 to 255 characters, not only white space.</summary>
    public required string Name { get; init; }

    /// <summary>Its alias, or null to take one from the name.</summary>
    public string? Alias { get; init; }

    /// <summary>The folder outside the trash to create it in, or null for the root.</summary>
    public Guid? FolderKey { get; init; }
}

/// <summary>A new draft of a form.</summary>
internal sealed record SaveFormDraftRequestModel
{
    /// <summary>The form's concurrency version that the draft is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>The definition document; an older schema version is upgraded when read.</summary>
    public required JsonElement Definition { get; init; }

    /// <summary>A comment on the version, at most 500 characters, or null.</summary>
    public string? Comment { get; init; }
}

/// <summary>A publish of a form's stored draft.</summary>
internal sealed record PublishFormRequestModel
{
    /// <summary>The form's concurrency version that the publish is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>A comment on the version, at most 500 characters, or null.</summary>
    public string? Comment { get; init; }
}

/// <summary>A change of a form that needs nothing but the version it is based on: an unpublish or a trash.</summary>
internal sealed record FormChangeRequestModel
{
    /// <summary>The form's concurrency version that the change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }
}

/// <summary>A form's new name, and its new alias.</summary>
internal sealed record RenameFormRequestModel
{
    /// <summary>The form's concurrency version that the change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>The name: 1 to 255 characters, not only white space.</summary>
    public required string Name { get; init; }

    /// <summary>The new alias, or null to keep it.</summary>
    public string? Alias { get; init; }
}

/// <summary>Where a form moves.</summary>
internal sealed record MoveFormRequestModel
{
    /// <summary>The form's concurrency version that the move is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>The folder outside the trash to move it into, or null for the root.</summary>
    public Guid? FolderKey { get; init; }

    /// <summary>
    /// Confirms a move that gives user groups more access to the form, which the answer <c>widens_access</c> listed; it
    /// needs <c>managePermissions</c>.
    /// </summary>
    public bool WidenAccess { get; init; }
}

/// <summary>A form's restore from the trash.</summary>
internal sealed record RestoreFormRequestModel
{
    /// <summary>The form's concurrency version that the restore is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>
    /// Confirms a restore to the root, because the form's folder is in the trash, that gives user groups more access to
    /// it; it needs <c>managePermissions</c>.
    /// </summary>
    public bool WidenAccess { get; init; }
}

/// <summary>A definition as the management API carries it: the document itself (ADR-0051).</summary>
internal static class DefinitionJson
{
    /// <summary>The canonical document of a definition.</summary>
    public static JsonElement Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using var document = JsonDocument.Parse(FormDefinitionSerializer.Write(definition));
        return document.RootElement.Clone();
    }
}
