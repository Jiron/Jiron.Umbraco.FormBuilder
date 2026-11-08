using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A page of the library's option lists.</summary>
internal sealed record OptionListRowsResponseModel
{
    /// <summary>How many lists the library holds.</summary>
    public required long Total { get; init; }

    /// <summary>The page's lists, by name.</summary>
    public required IReadOnlyList<OptionListRowResponseModel> Items { get; init; }
}

/// <summary>A row of the library's option lists.</summary>
internal sealed record OptionListRowResponseModel
{
    /// <summary>The list's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Its kind, such as <c>manual</c>; null for a kind this version lacks.</summary>
    public string? Kind { get; init; }

    /// <summary>How many options a list that holds them has; null for one that resolves them.</summary>
    public int? Count { get; init; }

    /// <summary>How many forms' drafts or live versions use it, forms in the recycle bin included.</summary>
    public required int UsedBy { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>The model of a row.</summary>
    public static OptionListRowResponseModel Of(OptionListRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new OptionListRowResponseModel
        {
            Key = row.List.Key,
            Alias = row.List.Alias,
            Name = row.List.Name,
            Kind = row.List.Kind?.Name,
            Count = row.List.Count,
            UsedBy = row.UsedBy,
            UpdatedUtc = row.Row.UpdatedUtc,
        };
    }
}

/// <summary>What the option list editor needs besides a list.</summary>
internal sealed record OptionListEditorResponseModel
{
    /// <summary>The site's languages, the default first.</summary>
    public required IReadOnlyList<OptionListLanguageResponseModel> Languages { get; init; }

    /// <summary>Every kind this version has, in the order of the enum.</summary>
    public required IReadOnlyList<OptionListKindResponseModel> Kinds { get; init; }

    /// <summary>The longest time a list keeps its options, in seconds.</summary>
    public required int MaxCacheSeconds { get; init; }

    /// <summary>The most options a list holds.</summary>
    public required int MaxOptions { get; init; }

    /// <summary>The largest file of options, in bytes.</summary>
    public required int MaxFileBytes { get; init; }
}

/// <summary>A language of the site.</summary>
internal sealed record OptionListLanguageResponseModel
{
    /// <summary>Its culture, such as <c>de-DE</c>.</summary>
    public required string Culture { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is the site's default language.</summary>
    public required bool IsDefault { get; init; }
}

/// <summary>A kind of option list, as the editor needs to know it.</summary>
internal sealed record OptionListKindResponseModel
{
    /// <summary>The kind, such as <c>url</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Whether its options are the list's own items.</summary>
    public required bool HasItems { get; init; }

    /// <summary>Whether a list of it keeps what it resolves for its <c>cacheSeconds</c>.</summary>
    public required bool KeepsOptions { get; init; }

    /// <summary>The shortest <c>cacheSeconds</c> of a list of it that keeps options.</summary>
    public required int MinCacheSeconds { get; init; }
}

/// <summary>A library option list as its editor shows it.</summary>
internal sealed record OptionListResponseModel
{
    /// <summary>Its key, which fields name it by.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Its kind; null for a kind this version lacks, which its editor cannot change.</summary>
    public string? Kind { get; init; }

    /// <summary>Its kind's configuration as a JSON object.</summary>
    public required JsonElement Config { get; init; }

    /// <summary>A manual or uploaded list's options, written as a field's own; empty for any other kind.</summary>
    public required JsonElement Items { get; init; }

    /// <summary>How long a code or URL list keeps its options, in seconds.</summary>
    public required int CacheSeconds { get; init; }

    /// <summary>The version a save or a delete is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>The model of a stored list; JSON that does not read shows as none, as a save writes it again.</summary>
    public static OptionListResponseModel Of(StoredOptionList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new OptionListResponseModel
        {
            Key = list.Key,
            Alias = list.Alias,
            Name = list.Name,
            Kind = list.Kind is { } kind ? EnumNames<OptionListKind>.ByValue[kind] : null,
            Config = Parse(list.Config, JsonValueKind.Object, "{}"),
            Items = Parse(list.Items, JsonValueKind.Array, "[]"),
            CacheSeconds = list.CacheSeconds,
            ConcurrencyVersion = list.ConcurrencyVersion,
            UpdatedUtc = list.UpdatedUtc,
        };
    }

    private static JsonElement Parse(string json, JsonValueKind kind, string none)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == kind)
            {
                return document.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
        }

        using var empty = JsonDocument.Parse(none);
        return empty.RootElement.Clone();
    }
}

/// <summary>A list's values as its editor holds them.</summary>
internal sealed record OptionListRequestModel
{
    /// <summary>The alias: a lowercase ASCII letter, then up to 63 ASCII letters and digits.</summary>
    public required string Alias { get; init; }

    /// <summary>The name, 1 to 255 characters.</summary>
    public required string Name { get; init; }

    /// <summary>The kind, such as <c>manual</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The kind's configuration as a JSON object; none for <c>{}</c>.</summary>
    public JsonElement? Config { get; init; }

    /// <summary>A manual or uploaded list's options as a JSON array, written as a field's own; none for <c>[]</c>.</summary>
    public JsonElement? Items { get; init; }

    /// <summary>How long a code or URL list keeps its options, in seconds; 0 for any other kind.</summary>
    public int CacheSeconds { get; init; }
}

/// <summary>A list to create.</summary>
internal sealed record CreateOptionListRequestModel
{
    /// <summary>Its key, or none for a new one.</summary>
    public Guid? Key { get; init; }

    /// <summary>Its values.</summary>
    public required OptionListRequestModel List { get; init; }
}

/// <summary>A list's new values, based on the version the editor read.</summary>
internal sealed record UpdateOptionListRequestModel
{
    /// <summary>The version the change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>Its values.</summary>
    public required OptionListRequestModel List { get; init; }
}

/// <summary>The name of a list's copy.</summary>
internal sealed record DuplicateOptionListRequestModel
{
    /// <summary>The copy's name, 1 to 255 characters.</summary>
    public required string Name { get; init; }
}

/// <summary>Contents not saved yet, to preview.</summary>
internal sealed record PreviewOptionListRequestModel
{
    /// <summary>The list's key, which a source's log lines name; none for a list not saved yet.</summary>
    public Guid? Key { get; init; }

    /// <summary>The kind, such as <c>url</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The kind's configuration as a JSON object; none for <c>{}</c>.</summary>
    public JsonElement? Config { get; init; }

    /// <summary>A manual or uploaded list's options as a JSON array; none for <c>[]</c>.</summary>
    public JsonElement? Items { get; init; }

    /// <summary>How long a URL list keeps what it fetched, in seconds.</summary>
    public int CacheSeconds { get; init; }

    /// <summary>The culture of the labels, such as <c>de-DE</c>; empty for the default language's texts.</summary>
    public required string Culture { get; init; }
}

/// <summary>The options contents give, or why they give none.</summary>
internal sealed record OptionListPreviewResponseModel
{
    /// <summary>The options in the order visitors see them; empty when the contents do not read.</summary>
    public required IReadOnlyList<OptionListOptionResponseModel> Options { get; init; }

    /// <summary>Whether the source gave its options; false for one that failed, took too long or does not read.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>Why the contents do not read, each at its path, as a save would refuse them; empty when they read.</summary>
    public required IReadOnlyList<LibraryProblemResponseModel> Problems { get; init; }
}

/// <summary>One option of a preview.</summary>
internal sealed record OptionListOptionResponseModel
{
    /// <summary>What entries store.</summary>
    public required string Value { get; init; }

    /// <summary>The label in the culture asked for, or null for the value.</summary>
    public string? Label { get; init; }

    /// <summary>A text below the label, or null.</summary>
    public string? Description { get; init; }
}

/// <summary>A CSV or TXT file of options.</summary>
internal sealed record ReadOptionFileRequestModel
{
    /// <summary>The file's name, whose extension says how it reads: <c>.csv</c> or <c>.txt</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>The file's bytes, as Base64 in JSON, at most 4 MiB.</summary>
    public required byte[] Content { get; init; }
}

/// <summary>A file's options.</summary>
internal sealed record OptionFileResponseModel
{
    /// <summary>The options as a JSON array, written as a field's own.</summary>
    public required JsonElement Items { get; init; }

    /// <summary>How many there are.</summary>
    public required int Count { get; init; }
}

/// <summary>What this server keeps of a URL list's address.</summary>
internal sealed record OptionListFetchResponseModel
{
    /// <summary>When the copy in use was fetched; null when no fetch has given options yet.</summary>
    public DateTimeOffset? FetchedUtc { get; init; }

    /// <summary>How many options that copy holds.</summary>
    public required int Count { get; init; }

    /// <summary>Whether the latest fetch failed.</summary>
    public required bool Failing { get; init; }

    /// <summary>The model of what a server keeps.</summary>
    public static OptionListFetchResponseModel Of(UrlListFetch fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return new OptionListFetchResponseModel { FetchedUtc = fetch.FetchedUtc, Count = fetch.Count, Failing = fetch.Failing };
    }
}
