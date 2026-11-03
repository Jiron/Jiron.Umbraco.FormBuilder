using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// A field's options as a draft holds them, resolved for the designer's canvas (ADR-0172): read as strictly as a
/// definition's, without the parent, so the canvas shows every option rather than those of one answer, and through the
/// option service in the canvas's culture. A configured source's configuration is held to its kind's schema and its own
/// check first: one that fails them, half typed in the inspector, is unavailable without a source being asked or a
/// failure logged.
/// </summary>
internal sealed class DraftOptions(IFormBuilderOptionService service, OptionKindRegistry kinds)
{
    // The model's own metadata, which the context keeps: strict, as a definition is read.
    private static JsonTypeInfo<FieldOptions> Info => (JsonTypeInfo<FieldOptions>)DefinitionJsonContext.Default.GetTypeInfo(typeof(FieldOptions))!;

    /// <summary>The options, or null when <paramref name="options"/> does not read as a field's options.</summary>
    /// <param name="field">The field's key, which a source's logs and kept copies name.</param>
    /// <param name="type">The field's type.</param>
    /// <param name="options">The field's <c>options</c> as the draft holds them.</param>
    /// <param name="culture">The culture of the labels.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<FormBuilderResolvedOptions?> ResolveAsync(Guid field, string type, JsonElement options, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(culture);
        // The shape reader refuses what a definition's reader refuses and fills in what is left out, as a draft is read.
        FieldOptions? read;
        try
        {
            read = options.ValueKind == JsonValueKind.Object && new DefinitionShapeReader().ReadRoot(options, typeof(FieldOptions), FrozenSet<string>.Empty) is { } node
                ? node.Deserialize(Info)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (read is null)
        {
            return null;
        }

        var shown = read with { Parent = null };
        if (shown.Source is not (OptionSource.Items or OptionSource.List))
        {
            if (KindOf(shown.Source) is not { OnField: true } kind || !kind.Check(shown.Config, "$.config", $"A {kind.Name} source", new DefinitionProblems()))
            {
                return FormBuilderResolvedOptions.Unavailable;
            }
        }

        var asked = AskedCulture.Of(culture);
        return await service.GetAsync(new FormField { Key = field, Type = type, Options = shown }, new FormBuilderOptionContext(asked), cancellationToken);
    }

    private OptionKind? KindOf(OptionSource source) => source switch
    {
        OptionSource.Content => kinds.Find(OptionListKind.Content),
        OptionSource.DataType => kinds.Find(OptionListKind.DataType),
        OptionSource.Url => kinds.Find(OptionListKind.Url),
        OptionSource.Countries => kinds.Find(OptionListKind.Countries),
        OptionSource.Code => kinds.Find(OptionListKind.Code),
        _ => null,
    };
}
