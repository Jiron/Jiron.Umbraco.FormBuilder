using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A stored email layout of the library: logo, colours, header and footer per culture.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">The key actions and settings name it by.</param>
/// <param name="Alias">The alias, unique without regard to case.</param>
/// <param name="Name">The name editors see.</param>
/// <param name="Layout">The layout as JSON.</param>
/// <param name="ConcurrencyVersion">The version every change raises, starting at 1.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
internal sealed record StoredEmailLayout(
    int Id,
    Guid Key,
    string Alias,
    string Name,
    string Layout,
    int ConcurrencyVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>An email layout to create.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Alias">Its alias.</param>
/// <param name="Name">Its name.</param>
/// <param name="Layout">The layout as JSON.</param>
internal sealed record NewEmailLayout(Guid Key, string Alias, string Name, string Layout = "{}");

/// <summary>A change to an email layout, based on the version the editor read.</summary>
/// <param name="Id">The layout.</param>
/// <param name="ExpectedConcurrencyVersion">The version the change is based on.</param>
/// <param name="Alias">The alias, which may change: actions and settings name the layout by its key.</param>
/// <param name="Name">The name.</param>
/// <param name="Layout">The layout as JSON.</param>
internal sealed record EmailLayoutUpdate(int Id, int ExpectedConcurrencyVersion, string Alias, string Name, string Layout);

/// <summary>The email layouts of the library in <c>jfbEmailLayout</c> (ADR-0037).</summary>
internal interface IEmailLayoutRepository
{
    /// <summary>
    /// Creates a layout at concurrency version 1. Refused with <see cref="FormBuilderConflict.DuplicateAlias"/> or
    /// <see cref="FormBuilderConflict.DuplicateKey"/>.
    /// </summary>
    Task<StoredEmailLayout> CreateAsync(NewEmailLayout layout, CancellationToken cancellationToken = default);

    /// <summary>The layout with this key, or null.</summary>
    Task<StoredEmailLayout?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The layout with this id, or null.</summary>
    Task<StoredEmailLayout?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The layout with this alias in any letter case, or null.</summary>
    Task<StoredEmailLayout?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>A page of the layouts in name order, optionally those whose name or alias contains the search text.</summary>
    Task<Page<StoredEmailLayout>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a layout; null when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it
    /// changed meanwhile, and with <see cref="FormBuilderConflict.DuplicateAlias"/>.
    /// </summary>
    Task<StoredEmailLayout?> UpdateAsync(EmailLayoutUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a layout; false when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it
    /// changed meanwhile. Actions and settings name layouts inside their JSON, so the library service checks them (12.2).
    /// </summary>
    Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class EmailLayoutRepository(FormBuilderDatabase database) : IEmailLayoutRepository
{
    private readonly AliasedLibraryTable<EmailLayoutDto> _table = new(database, FormBuilderTables.EmailLayout, "email layout");

    /// <inheritdoc />
    public async Task<StoredEmailLayout> CreateAsync(NewEmailLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var content = Content(layout.Layout);
        return FromDto(await _table.CreateAsync(layout.Key, layout.Alias, layout.Name, content, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<StoredEmailLayout?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        Maybe(await _table.GetAsync("key", key, cancellationToken));

    /// <inheritdoc />
    public async Task<StoredEmailLayout?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Maybe(await _table.GetAsync("id", id, cancellationToken));

    /// <inheritdoc />
    public async Task<StoredEmailLayout?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return Maybe(await _table.GetAsync("alias", alias, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Page<StoredEmailLayout>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await _table.ListAsync(search, page, cancellationToken);
        return new Page<StoredEmailLayout>(rows.Items.Select(FromDto).ToList(), rows.Total);
    }

    /// <inheritdoc />
    public async Task<StoredEmailLayout?> UpdateAsync(EmailLayoutUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.Alias);
        var content = Content(update.Layout);
        return Maybe(await _table.UpdateAsync(update.Id, update.ExpectedConcurrencyVersion, update.Alias, update.Name, content, cancellationToken));
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        _table.DeleteAsync(id, expectedConcurrencyVersion, null, cancellationToken);

    private static LibraryColumn[] Content(string layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return [new("layout", layout)];
    }

    private static StoredEmailLayout? Maybe(EmailLayoutDto? d) => d is null ? null : FromDto(d);

    private static StoredEmailLayout FromDto(EmailLayoutDto d) => new(
        d.Id,
        d.Key,
        d.Alias,
        d.Name,
        d.Layout,
        d.ConcurrencyVersion,
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.UpdatedUtc));
}
