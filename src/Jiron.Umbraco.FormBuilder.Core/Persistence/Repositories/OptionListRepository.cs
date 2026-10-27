using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A stored option list of the library (section 4.3.2).</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">The key definitions name it by.</param>
/// <param name="Alias">The alias, unique without regard to case.</param>
/// <param name="Name">The name editors see.</param>
/// <param name="Kind">Where its options come from (ADR-0073), stored as its camelCase name.</param>
/// <param name="Config">The source's configuration as JSON, never in the public projection.</param>
/// <param name="Items">The items of a manual or uploaded list as JSON.</param>
/// <param name="CacheSeconds">How long the options of a source are cached; 0 for no cache.</param>
/// <param name="ConcurrencyVersion">The version every change raises, starting at 1.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
internal sealed record StoredOptionList(
    int Id,
    Guid Key,
    string Alias,
    string Name,
    OptionListKind Kind,
    string Config,
    string Items,
    int CacheSeconds,
    int ConcurrencyVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>An option list to create.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Alias">Its alias.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Where its options come from.</param>
/// <param name="Config">The source's configuration as JSON.</param>
/// <param name="Items">The items as JSON.</param>
/// <param name="CacheSeconds">How long the options are cached.</param>
internal sealed record NewOptionList(Guid Key, string Alias, string Name, OptionListKind Kind, string Config = "{}", string Items = "[]", int CacheSeconds = 0);

/// <summary>A change to an option list, based on the version the editor read.</summary>
/// <param name="Id">The list.</param>
/// <param name="ExpectedConcurrencyVersion">The version the change is based on.</param>
/// <param name="Alias">The alias, which may change: definitions name the list by its key.</param>
/// <param name="Name">The name.</param>
/// <param name="Kind">Where its options come from.</param>
/// <param name="Config">The source's configuration as JSON.</param>
/// <param name="Items">The items as JSON.</param>
/// <param name="CacheSeconds">How long the options are cached.</param>
internal sealed record OptionListUpdate(int Id, int ExpectedConcurrencyVersion, string Alias, string Name, OptionListKind Kind, string Config, string Items, int CacheSeconds);

/// <summary>The option lists of the library in <c>jfbOptionList</c> (ADR-0037).</summary>
internal interface IOptionListRepository
{
    /// <summary>
    /// Creates a list at concurrency version 1. Refused with <see cref="FormBuilderConflict.DuplicateAlias"/> or
    /// <see cref="FormBuilderConflict.DuplicateKey"/>.
    /// </summary>
    Task<StoredOptionList> CreateAsync(NewOptionList list, CancellationToken cancellationToken = default);

    /// <summary>The list with this key, or null.</summary>
    Task<StoredOptionList?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The list with this id, or null.</summary>
    Task<StoredOptionList?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The list with this alias in any letter case, or null.</summary>
    Task<StoredOptionList?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>A page of the lists in name order, optionally those whose name or alias contains the search text.</summary>
    Task<Page<StoredOptionList>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>The aliases of the lists that start with the prefix in any letter case, in alias order.</summary>
    Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a list; null when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it
    /// changed meanwhile, and with <see cref="FormBuilderConflict.DuplicateAlias"/>.
    /// </summary>
    Task<StoredOptionList?> UpdateAsync(OptionListUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a list; false when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it
    /// changed meanwhile. Definitions name lists inside their JSON, so the library service checks them (12.1).
    /// </summary>
    Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class OptionListRepository(FormBuilderDatabase database) : IOptionListRepository
{
    private readonly AliasedLibraryTable<OptionListDto> _table = new(database, FormBuilderTables.OptionList, "option list");

    /// <inheritdoc />
    public async Task<StoredOptionList> CreateAsync(NewOptionList list, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        var content = Content(list.Kind, list.Config, list.Items, list.CacheSeconds);
        return FromDto(await _table.CreateAsync(list.Key, list.Alias, list.Name, content, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<StoredOptionList?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        Maybe(await _table.GetAsync("key", key, cancellationToken));

    /// <inheritdoc />
    public async Task<StoredOptionList?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Maybe(await _table.GetAsync("id", id, cancellationToken));

    /// <inheritdoc />
    public async Task<StoredOptionList?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return Maybe(await _table.GetAsync("alias", alias, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Page<StoredOptionList>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await _table.ListAsync(search, page, cancellationToken);
        return new Page<StoredOptionList>(rows.Items.Select(FromDto).ToList(), rows.Total);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken = default) =>
        _table.ListAliasesAsync(prefix, cancellationToken);

    /// <inheritdoc />
    public async Task<StoredOptionList?> UpdateAsync(OptionListUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.Alias);
        var content = Content(update.Kind, update.Config, update.Items, update.CacheSeconds);
        return Maybe(await _table.UpdateAsync(update.Id, update.ExpectedConcurrencyVersion, update.Alias, update.Name, content, cancellationToken));
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        _table.DeleteAsync(id, expectedConcurrencyVersion, null, cancellationToken);

    private static LibraryColumn[] Content(OptionListKind kind, string config, string items, int cacheSeconds)
    {
        var stored = StoredNames.Of(kind);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheSeconds);
        return [new("kind", stored), new("config", config), new("items", items), new("cacheSeconds", cacheSeconds)];
    }

    private static StoredOptionList? Maybe(OptionListDto? d) => d is null ? null : FromDto(d);

    private static StoredOptionList FromDto(OptionListDto d) => new(
        d.Id,
        d.Key,
        d.Alias,
        d.Name,
        StoredNames.Parse<OptionListKind>(d.Kind),
        d.Config,
        d.Items,
        d.CacheSeconds,
        d.ConcurrencyVersion,
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.UpdatedUtc));
}
