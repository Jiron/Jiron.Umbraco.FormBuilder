using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Where a library theme's layer is served (ADR-0215): <c>/jfb/assets/themes/library/&lt;alias&gt;.css?v=&lt;version&gt;</c>,
/// beside the package's own stylesheets, cached for good under its version as they are under their fingerprints.
/// </summary>
internal static class LibraryThemeLayers
{
    /// <summary>The folder below <c>/jfb/assets/</c> that holds the layers.</summary>
    public const string Folder = "/themes/library/";

    /// <summary>The address of a library theme's layer, with its version.</summary>
    /// <param name="pathBase">The site's path base.</param>
    /// <param name="theme">The theme.</param>
    public static string Url(PathString pathBase, LibraryTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return $"{pathBase.Add($"{WebsiteAssets.BasePath}{Folder}{theme.Name}.css")}?v={theme.Version.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>The alias of the layer a path below <c>/jfb/assets</c> names, or null for any other path.</summary>
    /// <param name="path">The path below <c>/jfb/assets</c>, such as <c>/themes/library/autumn.css</c>.</param>
    public static string? AliasOf(string? path)
    {
        if (path is null
            || !path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var alias = path[Folder.Length..^".css".Length];
        return FormBuilderThemes.IsAlias(alias) ? alias : null;
    }
}
