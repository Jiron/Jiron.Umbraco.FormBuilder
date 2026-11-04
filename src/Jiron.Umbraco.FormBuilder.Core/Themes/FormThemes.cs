using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>
/// Whether the site has the theme a form names (ADR-0015, ADR-0180): a theme of the registry, built in or the site's own
/// (ADR-0105), or one of the library (<c>jfbTheme</c>, ADR-0037). The form check warns about a form whose theme it lacks,
/// and an import clears such a theme (ADR-0046).
/// </summary>
/// <param name="registry">The themes in code.</param>
/// <param name="library">The library's themes.</param>
internal sealed class FormThemes(FormBuilderThemes registry, IThemeRepository library)
{
    /// <summary>Whether the site has a theme of this alias.</summary>
    /// <param name="alias">The alias the form names.</param>
    /// <param name="cancellationToken">Cancels the library's read.</param>
    public async Task<bool> HasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return registry.Find(alias) is not null || await library.GetByAliasAsync(alias, cancellationToken) is not null;
    }
}
