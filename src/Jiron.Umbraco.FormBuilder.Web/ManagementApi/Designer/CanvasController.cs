using System.Reflection;
using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// What the designer's canvas draws a form with (ADR-0168): the words a rendered form brings of its own in one of the
/// site's languages, the default theme's classes and stylesheets, and the site's marker for a form whose own is
/// <c>inherit</c>. The canvas builds each field's view model from the draft with them and draws it with the headless
/// element's views, so it shows the markup visitors get. Any user of the Forms section may ask: these describe the
/// package and the site, the same for everyone.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/canvas")]
[ApiExplorerSettings(GroupName = "Canvas")]
internal sealed class CanvasController(ILanguageService languages, IFormBuilderSettingsService settings, PageAssets assets) : FormBuilderManagementApiController
{
    /// <summary>The canvas's words, classes, stylesheets and marker, in a language of the site.</summary>
    /// <param name="culture">A language of the site, such as <c>de-DE</c>; the site's default language when it is none of them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(CanvasResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string? culture = null, CancellationToken cancellationToken = default)
    {
        var language = (culture is null ? null : await languages.GetAsync(culture))?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var general = (await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken)).Value;
        var theme = new DefaultTheme();
        return Ok(new CanvasResponseModel
        {
            Culture = language,
            Words = new Dictionary<string, string>(RenderingWords.Of(language).ByName(), StringComparer.Ordinal) { ["yes"] = FieldDisplayWords.Yes(language), ["no"] = FieldDisplayWords.No(language) },
            Classes = ClassesOf(new ThemeClassesModel(theme.Classes)),
            Stylesheets = assets.Addresses(Request.PathBase, theme).Styles,
            RequiredMarker = DefinitionWords<RequiredMarker>.Of(general.RequiredMarker)!,
        });
    }

    // A theme's classes by name, as the element endpoint's answer names them.
    private static Dictionary<string, string> ClassesOf(ThemeClassesModel classes) => typeof(ThemeClassesModel)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(string))
        .OrderBy(property => property.MetadataToken)
        .Select(property => (Name: JsonNamingPolicy.CamelCase.ConvertName(property.Name), Value: (string?)property.GetValue(classes)))
        .Where(slot => slot.Value is not null)
        .ToDictionary(slot => slot.Name, slot => slot.Value!, StringComparer.Ordinal);
}

/// <summary>What the canvas draws a form with (ADR-0168).</summary>
internal sealed record CanvasResponseModel
{
    /// <summary>The site's language the words are in.</summary>
    public required string Culture { get; init; }

    /// <summary>
    /// The words a rendered form brings of its own, and the answers of a yes-or-no question (<c>yes</c>, <c>no</c>), by
    /// their names in camel case; a text with <c>{0}</c> takes a value there.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Words { get; init; }

    /// <summary>The default theme's classes by their names in camel case, as the views write them; a slot without any is left out.</summary>
    public required IReadOnlyDictionary<string, string> Classes { get; init; }

    /// <summary>The addresses of the default theme's stylesheets on the site: the base rules first.</summary>
    public required IReadOnlyList<string> Stylesheets { get; init; }

    /// <summary>How the site marks the fields of a form whose marker is <c>inherit</c>: <c>optional</c>, <c>required</c> or <c>none</c>.</summary>
    public required string RequiredMarker { get; init; }
}
