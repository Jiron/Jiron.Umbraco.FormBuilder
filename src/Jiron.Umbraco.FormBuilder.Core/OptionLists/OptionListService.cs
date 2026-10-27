using System.Buffers;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>What an option list holds when it is created or changed (ADR-0073).</summary>
/// <param name="Alias">The alias, as a field's: a lowercase ASCII letter, then up to 63 ASCII letters and digits.</param>
/// <param name="Name">The name editors see, 1 to 255 characters.</param>
/// <param name="Kind">Where its options come from.</param>
/// <param name="Config">The kind's configuration as a JSON object.</param>
/// <param name="Items">A manual or uploaded list's options as a JSON array, written as a field's own; <c>[]</c> for the other kinds.</param>
/// <param name="CacheSeconds">
/// How long a code or URL list keeps its options, from 0 (not kept) to 30 days; 0 for any other kind (ADR-0075).
/// </param>
internal sealed record OptionListInput(string Alias, string Name, OptionListKind Kind, string Config = "{}", string Items = "[]", int CacheSeconds = 0);

/// <summary>The list a change stored, or the problems that refused it, each at its path.</summary>
/// <param name="List">The stored list, or null when the change was refused or the list does not exist.</param>
/// <param name="Problems">What refused the change; empty when it was stored, or when the list does not exist.</param>
internal sealed record OptionListResult(StoredOptionList? List, IReadOnlyList<DefinitionProblem> Problems)
{
    /// <summary>Whether the change was stored.</summary>
    public bool Succeeded => List is not null;
}

/// <summary>A library list as resolving reads it: its row, its kind, and its configuration and items as they read.</summary>
/// <param name="Row">The stored row.</param>
/// <param name="Kind">The kind, or null when this version does not have it.</param>
/// <param name="Config">The configuration, or null when it does not read or its kind refuses it.</param>
/// <param name="Items">The items, empty when they do not read or the kind refuses them.</param>
/// <param name="IsReadable">Whether the kind, the configuration and the items all read and keep the kind's checks.</param>
internal sealed record OptionListEntry(StoredOptionList Row, OptionKind? Kind, SettingsObject? Config, IReadOnlyList<FieldOption> Items, bool IsReadable);

/// <summary>
/// The library's option lists (ADR-0073): each change holds a list to its kind's checks and drops what every server
/// kept of it; a read keeps the list, read once per server while it is missing, until it changes. The library's
/// endpoints (12.1) change lists through it; an import holds the lists it creates to its checks and writes them in the
/// form's unit of work, then tells it they changed (ADR-0078).
/// </summary>
internal sealed partial class OptionListService(
    IOptionListRepository repository,
    OptionKindRegistry kinds,
    AppCaches appCaches,
    DistributedCache distributedCache,
    ILogger<OptionListService> logger)
{
    /// <summary>The most options a list holds or a source gives.</summary>
    public const int MaxOptions = 10_000;

    /// <summary>The longest time a source's options are kept.</summary>
    public const int MaxCacheSeconds = 30 * 24 * 60 * 60;

    // A list is kept until it changes; the time only bounds what a server keeps of lists nobody reads.
    private static readonly TimeSpan RowDuration = TimeSpan.FromHours(12);

    // A list the site lacks is kept as missing, so a page that names one does not read the database on every request.
    private static readonly MissingList Missing = new();

    /// <summary>The list with this key as resolving reads it, or null when the site lacks it.</summary>
    public async Task<OptionListEntry?> GetAsync(Guid key, CancellationToken cancellationToken = default)
    {
        var kept = await RuntimeCacheLoads.GetAsync(
            appCaches.RuntimeCache,
            OptionListCacheKeys.Row(key),
            OptionListCacheKeys.RowLoading(key),
            async () => await repository.GetAsync(key, CancellationToken.None) is { } row ? await EntryAsync(row) : (object)Missing,
            RowDuration,
            cancellationToken);
        return kept as OptionListEntry;
    }

    /// <summary>
    /// The problems of a list's contents, each at its path: <c>$.alias</c>, <c>$.config...</c>, <c>$.items...</c>, with
    /// what the configuration names on the site (ADR-0075).
    /// </summary>
    public async Task<IReadOnlyList<DefinitionProblem>> CheckAsync(OptionListInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new DefinitionProblems();
        if (!DefinitionStructureChecks.IsAlias(input.Alias ?? string.Empty))
        {
            problems.Add(DefinitionErrors.AliasInvalid, "$.alias", "An alias is a lowercase ASCII letter, then up to 63 ASCII letters and digits.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.name", "A list has a name.");
        }
        else if (input.Name.Length > AliasedLibraryTable<Persistence.Dtos.OptionListDto>.MaxNameLength)
        {
            problems.Add(DefinitionErrors.LimitExceeded, "$.name", $"A name holds at most {AliasedLibraryTable<Persistence.Dtos.OptionListDto>.MaxNameLength} characters.");
        }

        var ranged = input.CacheSeconds is >= 0 and <= MaxCacheSeconds;
        if (!ranged)
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.cacheSeconds", $"A list's options are kept for 0 to {MaxCacheSeconds} seconds (30 days).");
        }

        var kind = await ContentsAsync(input.Kind, input.Config, input.Items, onSite: true, problems, cancellationToken);
        if (ranged && input.CacheSeconds != 0 && kind is { UsesCacheSeconds: false })
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.cacheSeconds", $"A {kind.Name} list keeps nothing for a time, so its cacheSeconds is 0.");
        }
        else if (ranged && kind is { UsesCacheSeconds: true } && input.CacheSeconds < kind.MinCacheSeconds)
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.cacheSeconds", $"A {kind.Name} list keeps what it fetches for {kind.MinCacheSeconds} seconds at least.");
        }

        return problems.ToReadOnlyList();
    }

    /// <summary>Creates a list, unless its contents or its alias refuse it.</summary>
    public async Task<OptionListResult> CreateAsync(Guid key, OptionListInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await CheckAsync(input, cancellationToken) is { Count: > 0 } problems)
        {
            return new OptionListResult(null, problems);
        }

        try
        {
            var created = await repository.CreateAsync(new NewOptionList(key, input.Alias, input.Name, input.Kind, input.Config, input.Items, input.CacheSeconds), cancellationToken);
            Changed(created.Key);
            return new OptionListResult(created, []);
        }
        catch (FormBuilderConflictException exception) when (exception.Conflict is FormBuilderConflict.DuplicateAlias or FormBuilderConflict.DuplicateKey)
        {
            return new OptionListResult(null, [Taken(exception.Conflict)]);
        }
    }

    /// <summary>
    /// Changes a list, based on the version the editor read, unless its contents or its alias refuse it; no list and no
    /// problems when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile.
    /// </summary>
    public async Task<OptionListResult> UpdateAsync(Guid key, int expectedConcurrencyVersion, OptionListInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await CheckAsync(input, cancellationToken) is { Count: > 0 } problems)
        {
            return new OptionListResult(null, problems);
        }

        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return new OptionListResult(null, []);
        }

        try
        {
            var changed = await repository.UpdateAsync(
                new OptionListUpdate(row.Id, expectedConcurrencyVersion, input.Alias, input.Name, input.Kind, input.Config, input.Items, input.CacheSeconds),
                cancellationToken);
            Changed(key);
            return new OptionListResult(changed, []);
        }
        catch (FormBuilderConflictException exception) when (exception.Conflict is FormBuilderConflict.DuplicateAlias)
        {
            return new OptionListResult(null, [Taken(exception.Conflict)]);
        }
        catch (FormBuilderConcurrencyException)
        {
            // Changed elsewhere meanwhile: this server's copy may be older than the row.
            Changed(key);
            throw;
        }
    }

    /// <summary>Deletes a list; false when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile.</summary>
    public async Task<bool> DeleteAsync(Guid key, int expectedConcurrencyVersion, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return false;
        }

        try
        {
            return await repository.DeleteAsync(row.Id, expectedConcurrencyVersion, cancellationToken);
        }
        finally
        {
            Changed(key);
        }
    }

    // The configuration and items of a kind, each problem at its path; on the site, also what the configuration names
    // there, which a stored list is not held to when it is read, as the site changes on its own (ADR-0075).
    private async ValueTask<OptionKind?> ContentsAsync(OptionListKind kind, string? config, string? items, bool onSite, DefinitionProblems problems, CancellationToken cancellationToken)
    {
        if (kinds.Find(kind) is not { } found)
        {
            problems.Add(DefinitionErrors.OptionsInvalid, "$.kind", $"The site has no option lists of the kind '{StoredNames.Describe(kind)}'.");
            return null;
        }

        var owner = $"A {found.Name} list";
        if (TryReadConfig(config, problems, out var read))
        {
            if (onSite)
            {
                await found.CheckOnSiteAsync(read, "$.config", owner, problems, cancellationToken);
            }
            else
            {
                _ = found.Check(read, "$.config", owner, problems);
            }
        }

        if (ReadItems(items, problems) is { } options)
        {
            if (!found.HasItems && options.Count > 0)
            {
                problems.Add(DefinitionErrors.OptionsInvalid, "$.items", $"{owner} takes its options from its source and has no items of its own.");
            }

            foreach (var problem in DefinitionSaveChecks.CheckOptionItems(options, MaxOptions, "$.items").Concat(DefinitionStructureChecks.CheckOptionValues(options, "$.items")))
            {
                problems.Add(problem);
            }
        }

        return found;
    }

    // A configuration as the settings checks read it: none for an object with nothing in it, as settings are written.
    private static bool TryReadConfig(string? config, DefinitionProblems problems, out SettingsObject? read)
    {
        read = null;
        try
        {
            using var document = JsonDocument.Parse(config ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && CanonicalJson.IsLeftOut(root))
            {
                return true;
            }

            if (!SettingsJson.TryCanonicalize(root, "$.config", out _, out var problem))
            {
                problems.Add(problem);
                return false;
            }

            read = SettingsObject.From(root);
            return true;
        }
        catch (JsonException)
        {
            problems.Add(DefinitionErrors.InvalidJson, "$.config", "The configuration is a JSON object.");
            return false;
        }
    }

    // The items as a field's own options are read (ADR-0002): the same shape checks, codes and paths, as a field's
    // options.items, which is $.items here.
    private static IReadOnlyList<FieldOption>? ReadItems(string? items, DefinitionProblems problems)
    {
        JsonObject? read;
        try
        {
            using var document = JsonDocument.Parse(items ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                problems.Add(DefinitionErrors.InvalidJson, "$.items", "The items are a JSON array of options, as a field's own options are written.");
                return null;
            }

            // No items is the empty list, which the reader would leave out with the whole object.
            if (document.RootElement.GetArrayLength() == 0)
            {
                return [];
            }

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("items");
                document.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }

            using var wrapped = JsonDocument.Parse(buffer.WrittenMemory);
            var shape = new DefinitionShapeReader();
            read = shape.ReadRoot(wrapped.RootElement, typeof(FieldOptions), FrozenSet<string>.Empty);
            foreach (var problem in shape.Problems)
            {
                problems.Add(problem);
            }
        }
        catch (JsonException)
        {
            problems.Add(DefinitionErrors.InvalidJson, "$.items", "The items are a JSON array of options, as a field's own options are written.");
            return null;
        }

        try
        {
            return read?.Deserialize(DefinitionJsonContext.Default.FieldOptions)?.Items;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            // The shape reader refuses everything System.Text.Json would; this is the safety net, as a definition's is.
            problems.Add(DefinitionErrors.InvalidValue, "$.items", "The items do not read as a field's own options.");
            return null;
        }
    }

    private static DefinitionProblem Taken(FormBuilderConflict conflict) => conflict == FormBuilderConflict.DuplicateKey
        ? new DefinitionProblem(DefinitionErrors.InvalidValue, "$.key", "Another option list has this key.")
        : new DefinitionProblem(DefinitionErrors.AliasDuplicate, "$.alias", "Another option list has this alias (letter case does not count).");

    // A list as resolving reads it: what does not read, or what its kind refuses, leaves it unreadable.
    private async ValueTask<OptionListEntry> EntryAsync(StoredOptionList row)
    {
        var problems = new DefinitionProblems();
        var kind = await ContentsAsync(row.Kind, row.Config, row.Items, onSite: false, problems, CancellationToken.None);
        if (kind is null || problems.Found > 0 || !TryReadConfig(row.Config, problems, out var config) || ReadItems(row.Items, problems) is not { } items)
        {
            // Logged once per server and load, as the list is kept until it changes.
            LogUnreadable(logger, row.Key, problems.Found);
            return new OptionListEntry(row, kind, null, [], IsReadable: false);
        }

        return new OptionListEntry(row, kind, config, items, IsReadable: true);
    }

    /// <summary>
    /// Forgets what every server kept of the list with this key: this server at once, whatever the server messenger
    /// delivers locally, and the others when the cache instruction reaches them. Each change of this service calls it,
    /// and so does an import once the lists it created with its form have committed (ADR-0078).
    /// </summary>
    public void Changed(Guid key)
    {
        var change = new OptionListCacheChange(key);
        FormBuilderOptionListCacheRefresher.Forget(appCaches.RuntimeCache, change);
        distributedCache.RefreshByPayload(FormBuilderOptionListCacheRefresher.UniqueId, [change]);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The option list {ListKey} does not read as its kind, so it offers no options until it is saved again. Problems: {Problems}")]
    private static partial void LogUnreadable(ILogger logger, Guid listKey, int problems);

    // The mark a list the site lacks is kept as: a type of its own, which holds nothing that could change.
    private sealed class MissingList;
}
