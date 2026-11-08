using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A stored theme of the library (ADR-0015).</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">Its key.</param>
/// <param name="Alias">The alias forms name it by, unique without regard to case and fixed once created.</param>
/// <param name="Name">The name editors see.</param>
/// <param name="BaseTheme">The theme it builds on, such as <c>default</c>.</param>
/// <param name="Tokens">The design tokens as JSON.</param>
/// <param name="CustomCss">The custom CSS, or null.</param>
/// <param name="ConcurrencyVersion">The version every change raises, starting at 1.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
internal sealed record StoredTheme(
    int Id,
    Guid Key,
    string Alias,
    string Name,
    string BaseTheme,
    string Tokens,
    string? CustomCss,
    int ConcurrencyVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>A theme to create.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Alias">Its alias, fixed from now on.</param>
/// <param name="Name">Its name.</param>
/// <param name="BaseTheme">The theme it builds on.</param>
/// <param name="Tokens">The design tokens as JSON.</param>
/// <param name="CustomCss">The custom CSS, or null.</param>
internal sealed record NewTheme(Guid Key, string Alias, string Name, string BaseTheme, string Tokens = "{}", string? CustomCss = null);

/// <summary>A change to a theme, based on the version the editor read. The alias stays: forms name the theme by it.</summary>
/// <param name="Id">The theme.</param>
/// <param name="ExpectedConcurrencyVersion">The version the change is based on.</param>
/// <param name="Name">The name.</param>
/// <param name="BaseTheme">The theme it builds on.</param>
/// <param name="Tokens">The design tokens as JSON.</param>
/// <param name="CustomCss">The custom CSS, or null.</param>
internal sealed record ThemeUpdate(int Id, int ExpectedConcurrencyVersion, string Name, string BaseTheme, string Tokens, string? CustomCss);

/// <summary>The themes of the library in <c>jfbTheme</c> (ADR-0015, ADR-0037).</summary>
internal interface IThemeRepository
{
    /// <summary>
    /// Creates a theme at concurrency version 1. Refused with <see cref="FormBuilderConflict.DuplicateAlias"/> or
    /// <see cref="FormBuilderConflict.DuplicateKey"/>.
    /// </summary>
    Task<StoredTheme> CreateAsync(NewTheme theme, CancellationToken cancellationToken = default);

    /// <summary>The theme with this key, or null.</summary>
    Task<StoredTheme?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The theme with this id, or null.</summary>
    Task<StoredTheme?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The theme with this alias in any letter case, or null.</summary>
    Task<StoredTheme?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>A page of the themes in name order, optionally those whose name or alias contains the search text.</summary>
    Task<Page<StoredTheme>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a theme; null when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it
    /// changed meanwhile.
    /// </summary>
    Task<StoredTheme?> UpdateAsync(ThemeUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a theme; false when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when it
    /// changed meanwhile, and with <see cref="FormBuilderConflict.InUse"/> while a form's draft or live version names it
    /// (<c>jfbFormLibraryUse</c> by the theme's key, and <c>jfbForm.themeAlias</c>, trashed forms included; ADR-0215). The
    /// defaults of the General settings that name it are the library service's to check (ADR-0015). A save at the same
    /// moment may still name a theme that is gone; that form renders with <c>default</c>.
    /// </summary>
    Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class ThemeRepository(FormBuilderDatabase database) : IThemeRepository
{
    /// <summary>The longest base theme name the column holds.</summary>
    public const int MaxBaseThemeLength = 64;

    // A theme stays while a form's draft or live version names it (ADR-0215): its uses go by the theme's key, which the
    // library gives as the name-based key of its alias, and a form's published alias counts too, for a theme made before
    // that. The alias comparison follows the alias collation, so any letter case counts (ADR-0033).
    private static readonly InUseGuard UsedByForms = new(
        n => $"NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.FormLibraryUse)} u{n.LockingRead} WHERE u.{n.C("itemKind")} = 'theme' AND u.{n.C("itemKey")} = {n.T(FormBuilderTables.Theme)}.{n.C("key")})"
            + $" AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)} f WHERE f.{n.C("themeAlias")} = {n.T(FormBuilderTables.Theme)}.{n.C("alias")})",
        "A form uses the theme.");

    private readonly AliasedLibraryTable<ThemeDto> _table = new(database, FormBuilderTables.Theme, "theme");

    /// <inheritdoc />
    public async Task<StoredTheme> CreateAsync(NewTheme theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var content = Content(theme.BaseTheme, theme.Tokens, theme.CustomCss);
        return FromDto(await _table.CreateAsync(theme.Key, theme.Alias, theme.Name, content, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<StoredTheme?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        Maybe(await _table.GetAsync("key", key, cancellationToken));

    /// <inheritdoc />
    public async Task<StoredTheme?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Maybe(await _table.GetAsync("id", id, cancellationToken));

    /// <inheritdoc />
    public async Task<StoredTheme?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return Maybe(await _table.GetAsync("alias", alias, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Page<StoredTheme>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await _table.ListAsync(search, page, cancellationToken);
        return new Page<StoredTheme>(rows.Items.Select(FromDto).ToList(), rows.Total);
    }

    /// <inheritdoc />
    public async Task<StoredTheme?> UpdateAsync(ThemeUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var content = Content(update.BaseTheme, update.Tokens, update.CustomCss);
        return Maybe(await _table.UpdateAsync(update.Id, update.ExpectedConcurrencyVersion, null, update.Name, content, cancellationToken));
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        _table.DeleteAsync(id, expectedConcurrencyVersion, UsedByForms, cancellationToken);

    private static LibraryColumn[] Content(string baseTheme, string tokens, string? customCss)
    {
        ArgumentNullException.ThrowIfNull(baseTheme);
        if (baseTheme.Length is 0 or > MaxBaseThemeLength)
        {
            throw new ArgumentException($"A base theme holds 1 to {MaxBaseThemeLength} characters.", nameof(baseTheme));
        }

        ArgumentNullException.ThrowIfNull(tokens);

        // Null stands for none, so an empty text would be a second way to say it.
        if (customCss is { Length: 0 })
        {
            throw new ArgumentException("A theme's custom CSS is null or at least 1 character.", nameof(customCss));
        }

        return [new("baseTheme", baseTheme), new("tokens", tokens), new("customCss", customCss)];
    }

    private static StoredTheme? Maybe(ThemeDto? d) => d is null ? null : FromDto(d);

    private static StoredTheme FromDto(ThemeDto d) => new(
        d.Id,
        d.Key,
        d.Alias,
        d.Name,
        d.BaseTheme,
        d.Tokens,
        d.CustomCss,
        d.ConcurrencyVersion,
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.UpdatedUtc));
}
