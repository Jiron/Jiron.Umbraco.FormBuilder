using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A version of a form (ADR-0045, ADR-0052).</summary>
internal sealed record FormVersionResponseModel
{
    /// <summary>Its number, counting up per form from 1.</summary>
    public required int Number { get; init; }

    /// <summary><c>draft</c> for a save of the draft, <c>published</c> for a publish.</summary>
    public required string Kind { get; init; }

    /// <summary>The public revision a publish gave visitors, or null for a draft save.</summary>
    public int? PublicRevision { get; init; }

    /// <summary>The editor's comment, or null.</summary>
    public string? Comment { get; init; }

    /// <summary>Whether it is pinned, so the cleanup keeps it.</summary>
    public required bool IsPinned { get; init; }

    /// <summary>When it was saved.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Who saved it, or null when Jiron Form Builder did.</summary>
    public Guid? CreatedByUserKey { get; init; }

    /// <summary>When it was published, or null for a draft save.</summary>
    public DateTimeOffset? PublishedUtc { get; init; }

    /// <summary>Who published it, or null.</summary>
    public Guid? PublishedByUserKey { get; init; }

    /// <summary>Whether it is the form's draft.</summary>
    public required bool IsDraft { get; init; }

    /// <summary>Whether it is the version visitors get.</summary>
    public required bool IsLive { get; init; }

    /// <summary>The model of a version.</summary>
    public static FormVersionResponseModel Of(FormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new FormVersionResponseModel
        {
            Number = version.Number,
            Kind = ApiNames.Of(version.Kind),
            PublicRevision = version.PublicRevision,
            Comment = version.Comment,
            IsPinned = version.IsPinned,
            CreatedUtc = version.CreatedUtc,
            CreatedByUserKey = version.CreatedByUserKey,
            PublishedUtc = version.PublishedUtc,
            PublishedByUserKey = version.PublishedByUserKey,
            IsDraft = version.IsDraft,
            IsLive = version.IsLive,
        };
    }
}

/// <summary>A version as the History lists it, with what changed.</summary>
internal sealed record FormVersionEntryResponseModel
{
    /// <summary>The version.</summary>
    public required FormVersionResponseModel Version { get; init; }

    /// <summary>
    /// What changed: for a publish, since the publish before it; for a draft save, since the version before it. Null for
    /// the first of either, and when one of the two cannot be read.
    /// </summary>
    public IReadOnlyList<DefinitionChangeSummaryResponseModel>? Changes { get; init; }

    /// <summary>
    /// The name of who made it: for a publish who published it, for a draft save who saved it (ADR-0196). Null when Jiron
    /// Form Builder made it, or the user no longer exists.
    /// </summary>
    public string? By { get; init; }

    /// <summary>The model of an entry, with the names of the users who made the versions by key.</summary>
    public static FormVersionEntryResponseModel Of(FormVersionEntry entry, IReadOnlyDictionary<Guid, string> names)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(names);
        return new FormVersionEntryResponseModel
        {
            Version = FormVersionResponseModel.Of(entry.Version),
            Changes = entry.Changes is { } changes ? [.. changes.Select(DefinitionChangeSummaryResponseModel.Of)] : null,
            By = MakerOf(entry.Version) is { } key ? names.GetValueOrDefault(key) : null,
        };
    }

    /// <summary>Who made a version: who published a publish, who saved a draft save.</summary>
    public static Guid? MakerOf(FormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.Kind == FormVersionKind.Published ? version.PublishedByUserKey ?? version.CreatedByUserKey : version.CreatedByUserKey;
    }
}

/// <summary>A version with its definition document (ADR-0051).</summary>
internal sealed record FormVersionDocumentResponseModel
{
    /// <summary>The version.</summary>
    public required FormVersionResponseModel Version { get; init; }

    /// <summary>Its definition document in its canonical form, or null when it cannot be read.</summary>
    public JsonElement? Definition { get; init; }

    /// <summary>Why restoring it would be refused now, or why it cannot be read; usually empty.</summary>
    public required IReadOnlyList<DefinitionProblemResponseModel> Problems { get; init; }

    /// <summary>The model of a version with its definition.</summary>
    public static FormVersionDocumentResponseModel Of(FormVersionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new FormVersionDocumentResponseModel
        {
            Version = FormVersionResponseModel.Of(document.Version),
            Definition = document.Definition is { } definition ? DefinitionJson.Of(definition) : null,
            Problems = [.. document.Problems.Select(DefinitionProblemResponseModel.Of)],
        };
    }
}

/// <summary>An element that differs between two versions, without its properties (ADR-0045).</summary>
internal sealed record DefinitionChangeSummaryResponseModel
{
    /// <summary>What the element is: <c>settings</c>, <c>page</c>, <c>group</c>, <c>field</c>, <c>rule</c>, <c>validationRule</c>, <c>action</c> or <c>outcome</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Its key, or null for a settings group.</summary>
    public Guid? Key { get; init; }

    /// <summary>The settings group's name, such as <c>appearance</c>, or null for other elements.</summary>
    public string? SettingsGroup { get; init; }

    /// <summary>The element that holds it in the newer version (in the older one when removed), or null.</summary>
    public Guid? ParentKey { get; init; }

    /// <summary><c>added</c>, <c>removed</c> or <c>changed</c>.</summary>
    public required string Change { get; init; }

    /// <summary>Whether its place changed: another parent, or another order among the siblings both versions share.</summary>
    public required bool Moved { get; init; }

    /// <summary>Its default-culture title, label or name, or null.</summary>
    public string? Label { get; init; }

    /// <summary>A field's alias, or null.</summary>
    public string? Alias { get; init; }

    /// <summary>The model of a summary.</summary>
    public static DefinitionChangeSummaryResponseModel Of(DefinitionChangeSummary change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new DefinitionChangeSummaryResponseModel
        {
            Kind = ApiNames.Of(change.ElementKind),
            Key = change.Key,
            SettingsGroup = change.SettingsGroup,
            ParentKey = change.ParentKey,
            Change = ApiNames.Of(change.ChangeKind),
            Moved = change.Moved,
            Label = change.Label,
            Alias = change.Alias,
        };
    }
}

/// <summary>What changed from one version of a form to another (ADR-0045).</summary>
internal sealed record FormComparisonResponseModel
{
    /// <summary>The older version's number.</summary>
    public required int FromVersion { get; init; }

    /// <summary>The newer version's number.</summary>
    public required int ToVersion { get; init; }

    /// <summary>The elements that differ.</summary>
    public required IReadOnlyList<DefinitionChangeResponseModel> Changes { get; init; }

    /// <summary>The model of a comparison.</summary>
    public static FormComparisonResponseModel Of(FormComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return new FormComparisonResponseModel
        {
            FromVersion = comparison.FromVersion,
            ToVersion = comparison.ToVersion,
            Changes = [.. comparison.Changes.Select(DefinitionChangeResponseModel.Of)],
        };
    }
}

/// <summary>An element that differs between two versions, with every property that differs (ADR-0045).</summary>
internal sealed record DefinitionChangeResponseModel
{
    /// <summary>What the element is, as in <see cref="DefinitionChangeSummaryResponseModel.Kind"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Its key, or null for a settings group.</summary>
    public Guid? Key { get; init; }

    /// <summary>The settings group's name, or null for other elements.</summary>
    public string? SettingsGroup { get; init; }

    /// <summary>The element that holds it in the newer version (in the older one when removed), or null.</summary>
    public Guid? ParentKey { get; init; }

    /// <summary>The element that held it in the older version, when it moved to another one; otherwise null.</summary>
    public Guid? PreviousParentKey { get; init; }

    /// <summary><c>added</c>, <c>removed</c> or <c>changed</c>.</summary>
    public required string Change { get; init; }

    /// <summary>Whether its place changed.</summary>
    public required bool Moved { get; init; }

    /// <summary>Its default-culture title, label or name, or null.</summary>
    public string? Label { get; init; }

    /// <summary>A field's alias, or null.</summary>
    public string? Alias { get; init; }

    /// <summary>A field's or an action's type, or null.</summary>
    public string? Type { get; init; }

    /// <summary>Its own properties in the older version, as a JSON object without its key and the elements it holds; null when added.</summary>
    public JsonElement? Before { get; init; }

    /// <summary>Its own properties in the newer version, as a JSON object without its key and the elements it holds; null when removed.</summary>
    public JsonElement? After { get; init; }

    /// <summary>The properties that differ, in the newer version's order; empty when added, removed or only moved.</summary>
    public required IReadOnlyList<DefinitionPropertyChangeResponseModel> Properties { get; init; }

    /// <summary>The model of a change.</summary>
    public static DefinitionChangeResponseModel Of(DefinitionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new DefinitionChangeResponseModel
        {
            Kind = ApiNames.Of(change.ElementKind),
            Key = change.Key,
            SettingsGroup = change.SettingsGroup,
            ParentKey = change.ParentKey,
            PreviousParentKey = change.PreviousParentKey,
            Change = ApiNames.Of(change.ChangeKind),
            Moved = change.Moved,
            Label = change.Label,
            Alias = change.Alias,
            Type = change.Type,
            Before = ElementOf(change.Before),
            After = ElementOf(change.After),
            Properties = [.. change.Properties.Select(property => new DefinitionPropertyChangeResponseModel(property.Path, ElementOf(property.From), ElementOf(property.To)))],
        };
    }

    // A value of the comparison as the API carries it: JSON, which the OpenAPI document leaves as any value.
    private static JsonElement? ElementOf(JsonNode? node) => node is null ? null : JsonSerializer.SerializeToElement(node);
}

/// <summary>A property that differs between two versions.</summary>
/// <param name="Path">Its path from the element, one segment per step, as in <c>["options", "items", "phone", "label", ""]</c>.</param>
/// <param name="From">The older value as JSON, or null where the older version has none.</param>
/// <param name="To">The newer value as JSON, or null where the newer version has none.</param>
internal sealed record DefinitionPropertyChangeResponseModel(IReadOnlyList<string> Path, JsonElement? From, JsonElement? To);

/// <summary>A restore of a version into a new draft.</summary>
internal sealed record RestoreVersionRequestModel
{
    /// <summary>The form's concurrency version that the restore is based on.</summary>
    public required int ConcurrencyVersion { get; init; }
}

/// <summary>A version's pin.</summary>
internal sealed record PinVersionRequestModel
{
    /// <summary>Whether the cleanup keeps it.</summary>
    public required bool IsPinned { get; init; }
}

/// <summary>A version's comment.</summary>
internal sealed record CommentVersionRequestModel
{
    /// <summary>The comment, at most 500 characters; null, empty or blank clears it.</summary>
    public string? Comment { get; init; }
}

/// <summary>The stable names the management API gives the kinds of the model: their names in camel case (ADR-0052).</summary>
internal static class ApiNames
{
    /// <summary>The name of a kind, such as <c>validationRule</c>.</summary>
    public static string Of<TKind>(TKind kind)
        where TKind : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
}
