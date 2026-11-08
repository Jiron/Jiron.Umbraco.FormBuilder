using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>
/// Whether the site has the theme a form names (ADR-0015, ADR-0180): a theme of the registry, built in or the site's own
/// (ADR-0105), or one of the library (<c>jfbTheme</c>, ADR-0037). The form check warns about a form whose theme it lacks,
/// and an import clears such a theme (ADR-0046). It also gives the theme a form renders with, a library theme as its
/// base theme with its layer (ADR-0215).
/// </summary>
/// <param name="registry">The themes in code.</param>
/// <param name="library">The library's themes.</param>
/// <param name="rendered">The library's themes as the website renders them.</param>
internal sealed class FormThemes(FormBuilderThemes registry, IThemeRepository library, LibraryThemes rendered)
{
    /// <summary>Whether the site has a theme of this alias.</summary>
    /// <param name="alias">The alias the form names.</param>
    /// <param name="cancellationToken">Cancels the library's read.</param>
    public async Task<bool> HasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return registry.Find(alias) is not null || await library.GetByAliasAsync(alias, cancellationToken) is not null;
    }

    /// <summary>
    /// The alias the site knows a theme by, for one a request names: the registry's theme's, else the library's, in the
    /// letter case the site has it; <c>default</c> for an alias that names neither, so a name the request makes up is
    /// neither kept nor logged (ADR-0105).
    /// </summary>
    /// <param name="alias">The alias the request names.</param>
    /// <param name="cancellationToken">Cancels the library's read.</param>
    public async Task<string> KnownAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return registry.Find(alias)?.Name ?? (await rendered.FindAsync(alias, cancellationToken))?.Name ?? BuiltInThemes.DefaultAlias;
    }

    /// <summary>
    /// The theme of the first alias given that is not blank: the registry's, else the library's; <c>default</c> when none
    /// is given, and for an alias that names neither, which the registry logs once.
    /// </summary>
    /// <param name="first">The first alias in order, such as the placement's.</param>
    /// <param name="second">The next, such as the form's.</param>
    /// <param name="cancellationToken">Cancels the library's read.</param>
    public async Task<IFormBuilderTheme> ResolveAsync(string? first, string? second, CancellationToken cancellationToken = default)
    {
        var alias = !string.IsNullOrWhiteSpace(first) ? first : second;
        if (string.IsNullOrWhiteSpace(alias) || registry.Find(alias) is not null)
        {
            return registry.Resolve(alias);
        }

        return await rendered.FindAsync(alias, cancellationToken) ?? registry.Resolve(alias);
    }
}
