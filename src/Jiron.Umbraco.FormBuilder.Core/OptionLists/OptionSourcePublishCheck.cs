using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// Holds each choice field's source configuration to its kind before the form goes live (ADR-0073): the kind's schema
/// and its own check, at <c>...options.config</c>; a kind the site lacks and an upload as a field's source are refused at
/// <c>...options.source</c>. A field's own options and a library list are the structural validation's (ADR-0035).
/// </summary>
internal sealed class OptionSourcePublishCheck(OptionKindRegistry kinds) : IFormPublishCheck
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var problems = new DefinitionProblems();
        var pagesPath = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var groupsPath = JsonPaths.Property(JsonPaths.Index(pagesPath, p), "groups");
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                Fields(definition.Pages[p].Groups[g].Fields, JsonPaths.Property(JsonPaths.Index(groupsPath, g), "fields"), problems);
            }
        }

        return ValueTask.FromResult(problems.ToReadOnlyList());
    }

    private void Fields(IReadOnlyList<FormField> fields, string path, DefinitionProblems problems)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldPath = JsonPaths.Index(path, i);
            if (field.Options is { } options)
            {
                Check(options, JsonPaths.Property(fieldPath, "options"), problems);
            }

            Fields(field.Fields, JsonPaths.Property(fieldPath, "fields"), problems);
        }
    }

    private void Check(FieldOptions options, string path, DefinitionProblems problems)
    {
        OptionListKind kind;
        switch (options.Source)
        {
            case OptionSource.Items or OptionSource.List:
                return;
            case OptionSource.File:
                problems.Add(DefinitionErrors.OptionsInvalid, JsonPaths.Property(path, "source"), "A field takes no upload as its source: an upload becomes the field's own options, or a library list's.");
                return;
            case OptionSource.Content:
                kind = OptionListKind.Content;
                break;
            case OptionSource.DataType:
                kind = OptionListKind.DataType;
                break;
            case OptionSource.Url:
                kind = OptionListKind.Url;
                break;
            case OptionSource.Countries:
                kind = OptionListKind.Countries;
                break;
            case OptionSource.Code:
                kind = OptionListKind.Code;
                break;
            default:
                return;
        }

        if (kinds.Find(kind) is not { OnField: true } found)
        {
            problems.Add(DefinitionErrors.OptionsInvalid, JsonPaths.Property(path, "source"), $"The site has no option source of the kind '{EnumNames<OptionListKind>.ByValue[kind]}'.");
            return;
        }

        found.Check(options.Config, JsonPaths.Property(path, "config"), $"A {found.Name} source", problems);
    }
}
