using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Stops the site when a theme's stylesheet lies below <c>/jfb/assets/</c> but is none of the website assets the Web
/// assembly holds (ADR-0092, ADR-0105): nothing else is served there, so its forms would never get their styles, and a
/// page could not write the stylesheet's fingerprint. It runs with the other start-up checks, beside the registry's own
/// check of the themes, and names every such theme at once.
/// </summary>
/// <param name="themes">The registered themes.</param>
/// <param name="assets">The website assets the Web assembly holds.</param>
internal sealed class ThemeStylesheetStartupCheck(FormBuilderThemeCollection themes, WebsiteAssets assets) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        var problems = themes
            .Where(theme => theme.Stylesheet is { } stylesheet && PageAssets.AssetName(stylesheet) is { } asset && assets.Find(asset) is null)
            .Select(theme => $"The theme '{theme.Name}' names the stylesheet '{theme.Stylesheet}', which is none of the website assets below {WebsiteAssets.BasePath}/ that the Web assembly holds: build the client, or give the theme a stylesheet the site serves itself (ADR-0105).")
            .ToList();
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
