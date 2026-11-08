using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A copy of a form to make from its draft (ADR-0046): it lands in the form's folder.</summary>
internal sealed record DuplicateFormRequestModel
{
    /// <summary>The copy's name: 1 to 255 characters, not only white space.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the copy gets the form's actions, which needs <c>manageActions</c> in its folder.</summary>
    public required bool IncludeActions { get; init; }
}

/// <summary>Actions to copy from a form to another (ADR-0046).</summary>
internal sealed record CopyActionsRequestModel
{
    /// <summary>The form the actions go to, which the user may design and manage the actions of.</summary>
    public required Guid TargetKey { get; init; }

    /// <summary>The target's concurrency version that the copy is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>The source's actions to copy, or null for all of them.</summary>
    public IReadOnlyList<Guid>? ActionKeys { get; init; }
}

/// <summary>What copying actions did (ADR-0046).</summary>
internal sealed record ActionsCopiedResponseModel
{
    /// <summary>The target after the copy; its version is null when there was nothing to copy.</summary>
    public required FormSavedResponseModel Target { get; init; }

    /// <summary>Each copy, in the source's order, with what it could not map.</summary>
    public required IReadOnlyList<CopiedActionResponseModel> Actions { get; init; }
}

/// <summary>An action copied to another form, with the references it keeps until the editor fixes them (ADR-0046).</summary>
internal sealed record CopiedActionResponseModel
{
    /// <summary>The action's key in the source.</summary>
    public required Guid SourceKey { get; init; }

    /// <summary>The copy's key in the target.</summary>
    public required Guid Key { get; init; }

    /// <summary>The references to the source that the target has nothing to match.</summary>
    public required IReadOnlyList<UnmappedReferenceResponseModel> Unmapped { get; init; }

    /// <summary>The merge tags in the copy's settings that name a field the target lacks.</summary>
    public required IReadOnlyList<UnmappedMergeTagResponseModel> MergeTags { get; init; }

    /// <summary>The model of a copy.</summary>
    public static CopiedActionResponseModel Of(CopiedAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new CopiedActionResponseModel
        {
            SourceKey = action.SourceKey,
            Key = action.Key,
            Unmapped = [.. action.Unmapped.Select(reference => new UnmappedReferenceResponseModel(reference.Path, reference.SourceKey, reference.Alias))],
            MergeTags = [.. action.MergeTags.Select(tag => new UnmappedMergeTagResponseModel(tag.Path, tag.Tag, tag.At))],
        };
    }
}

/// <summary>A reference of a copied action that the target has nothing to match.</summary>
/// <param name="Path">Where it is in the target's definition.</param>
/// <param name="SourceKey">The key it names in the source.</param>
/// <param name="Alias">The alias of the source's field it names, or null for another element.</param>
internal sealed record UnmappedReferenceResponseModel(string Path, Guid SourceKey, string? Alias);

/// <summary>A merge tag of a copied action that names a field the target lacks.</summary>
/// <param name="Path">Where the text that holds it is in the target's definition.</param>
/// <param name="Tag">The tag's path, such as <c>field.topic</c>.</param>
/// <param name="At">Where the tag's path starts in the text, from 0.</param>
internal sealed record UnmappedMergeTagResponseModel(string Path, string Tag, int At);

/// <summary>A form to create from a template of the gallery.</summary>
internal sealed record CreateFormFromTemplateRequestModel
{
    /// <summary>The template: a built-in one or one saved on this site.</summary>
    [Required]
    public required Guid? TemplateKey { get; init; }

    /// <summary>The form's name, or null for the template's.</summary>
    public string? Name { get; init; }

    /// <summary>The folder outside the trash to create it in, or null for the root.</summary>
    public Guid? FolderKey { get; init; }
}

/// <summary>An export file to import as a new form, or to check before.</summary>
internal sealed record ImportFormRequestModel
{
    /// <summary>The file's text.</summary>
    public required string Json { get; init; }

    /// <summary>The form's name, or null for the one in the file.</summary>
    public string? Name { get; init; }

    /// <summary>The folder outside the trash the form goes to, or null for the root.</summary>
    public Guid? FolderKey { get; init; }
}

/// <summary>What an import made (ADR-0046).</summary>
internal sealed record FormImportedResponseModel
{
    /// <summary>The new form, a draft.</summary>
    public required FormResponseModel Form { get; init; }

    /// <summary>What the import changed or could not resolve, in file order; empty when everything came along.</summary>
    public required IReadOnlyList<ImportNoteResponseModel> Notes { get; init; }
}

/// <summary>What an import would make (ADR-0166).</summary>
internal sealed record ImportCheckResponseModel
{
    /// <summary>The form's name in the file.</summary>
    public required string Name { get; init; }

    /// <summary>The form's alias in the file.</summary>
    public required string Alias { get; init; }

    /// <summary>The version of Jiron Form Builder that wrote the file.</summary>
    public required string PackageVersion { get; init; }

    /// <summary>When it was written.</summary>
    public required DateTimeOffset ExportedUtc { get; init; }

    /// <summary>What its definition holds.</summary>
    public required DefinitionCountsResponseModel Holds { get; init; }

    /// <summary>What the import would change or could not resolve, as the import's own notes say it.</summary>
    public required IReadOnlyList<ImportNoteResponseModel> Notes { get; init; }

    /// <summary>The model of a check.</summary>
    public static ImportCheckResponseModel Of(FormImportCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new ImportCheckResponseModel
        {
            Name = check.Name,
            Alias = check.Alias,
            PackageVersion = check.PackageVersion,
            ExportedUtc = check.ExportedUtc,
            Holds = DefinitionCountsResponseModel.Of(check.Counts),
            Notes = [.. check.Notes.Select(ImportNoteResponseModel.Of)],
        };
    }
}

/// <summary>Something an import changed or could not resolve.</summary>
/// <param name="Code">
/// <c>theme_missing</c>, <c>option_list_reused</c>, <c>option_list_created</c>, <c>option_list_missing</c>,
/// <c>action_configuration_missing</c>, <c>action_type_missing</c> or <c>page_missing</c>.
/// </param>
/// <param name="Path">Where it is in the file.</param>
/// <param name="Alias">The alias of the theme, option list or action type, or the configuration key, it is about; null when the file names none.</param>
internal sealed record ImportNoteResponseModel(string Code, string Path, string? Alias)
{
    /// <summary>The model of a note.</summary>
    public static ImportNoteResponseModel Of(FormImportNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new ImportNoteResponseModel(note.Code, note.Path, note.Alias);
    }
}

/// <summary>How much a definition holds (ADR-0166).</summary>
/// <param name="Pages">The pages.</param>
/// <param name="Fields">The fields, the children of repeatable groups included.</param>
/// <param name="Rules">The rules.</param>
/// <param name="Outcomes">The outcomes.</param>
/// <param name="Actions">The actions.</param>
internal sealed record DefinitionCountsResponseModel(int Pages, int Fields, int Rules, int Outcomes, int Actions)
{
    /// <summary>The model of counts.</summary>
    public static DefinitionCountsResponseModel Of(DefinitionCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return new DefinitionCountsResponseModel(counts.Pages, counts.Fields, counts.Rules, counts.Outcomes, counts.Actions);
    }
}

/// <summary>A template of the gallery (ADR-0166).</summary>
internal sealed record TemplateResponseModel
{
    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Whether it ships with Jiron Form Builder.</summary>
    public required bool BuiltIn { get; init; }

    /// <summary>A built-in template's alias, by which the backoffice names and describes it; null for a saved one.</summary>
    public string? Alias { get; init; }

    /// <summary>Its name; a built-in template's in English.</summary>
    public required string Name { get; init; }

    /// <summary>A saved template's description, or null.</summary>
    public string? Description { get; init; }

    /// <summary>What it holds.</summary>
    public required DefinitionCountsResponseModel Holds { get; init; }

    /// <summary>Its definition document, for the preview: a group's is one page holding the group.</summary>
    public required JsonElement Definition { get; init; }

    /// <summary>What it is a template of: <c>form</c> or <c>group</c> (ADR-0217).</summary>
    public required string Kind { get; init; }

    /// <summary>The model of a template.</summary>
    public static TemplateResponseModel Of(GalleryTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new TemplateResponseModel
        {
            Key = template.Key,
            BuiltIn = template.BuiltIn,
            Alias = template.Alias,
            Name = template.Name,
            Description = template.Description,
            Holds = DefinitionCountsResponseModel.Of(DefinitionCounts.Of(template.Definition)),
            Definition = DefinitionJson.Of(template.Definition),
            Kind = template.Kind == Core.Persistence.Repositories.FormTemplateKind.Group ? "group" : "form",
        };
    }

    /// <summary>The model of a template just saved, read as the gallery reads it.</summary>
    public static TemplateResponseModel Of(Core.Persistence.Repositories.StoredFormTemplate template, FormTemplateGallery gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        return Of(gallery.Read(template) ?? throw new InvalidOperationException($"The template {template?.Key} does not read as it was saved."));
    }
}

/// <summary>A template's name and description, as a save or a rename gives them (ADR-0217).</summary>
internal sealed record TemplateNameRequestModel
{
    /// <summary>Its name, 1 to 255 characters.</summary>
    public required string Name { get; init; }

    /// <summary>A description, at most 2000 characters, or null.</summary>
    public string? Description { get; init; }
}

/// <summary>A group to save as a template, as the canvas shows it, with a name and a description (ADR-0217).</summary>
internal sealed record SaveGroupTemplateRequestModel
{
    /// <summary>Its name, 1 to 255 characters.</summary>
    public required string Name { get; init; }

    /// <summary>A description, at most 2000 characters, or null.</summary>
    public string? Description { get; init; }

    /// <summary>The group as a definition holds it, its fields and rules included.</summary>
    public required JsonElement Group { get; init; }
}

/// <summary>A group saved as a template (ADR-0217).</summary>
internal sealed record GroupTemplateSavedResponseModel
{
    /// <summary>The template.</summary>
    public required TemplateResponseModel Template { get; init; }

    /// <summary>How many of its rules lost a condition naming an element outside the group, or went for it.</summary>
    public required int RulesChanged { get; init; }
}

/// <summary>What a permanent delete takes (ADR-0166).</summary>
internal sealed record HoldingsResponseModel
{
    /// <summary>The folders that go; 0 for a form.</summary>
    public required int Folders { get; init; }

    /// <summary>The forms that go.</summary>
    public required int Forms { get; init; }

    /// <summary>Their entries, in any state; null for a form whose entries the user may neither view nor delete.</summary>
    public required long? Entries { get; init; }

    /// <summary>Their uploaded files; null where the entries are.</summary>
    public required long? Files { get; init; }

    /// <summary>How many of the trash's top items stay, because the user may not delete them; 0 for a form.</summary>
    public required int Left { get; init; }
}
