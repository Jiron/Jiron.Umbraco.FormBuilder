using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library's themes (ROADMAP 12.1, ADR-0215): the list with how many forms use each, the themes in code they build on
/// with their tokens, a theme to edit, the forms that use it, its changes, a duplicate, export and import, and the layer
/// the editor's preview applies. Every endpoint needs <c>manageLibrary</c>, which is granted for the whole section.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/theme")]
[ApiExplorerSettings(GroupName = "Theme")]
internal sealed class ThemeLibraryController(ThemeLibrary library, FormBuilderThemes registry) : FormBuilderManagementApiController
{
    /// <summary>A page of the themes by name, each with how many forms use it, whether it is the default and its swatch.</summary>
    /// <param name="skip">How many themes to skip.</param>
    /// <param name="take">How many themes to return, at most 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("library")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeRowsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rows([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default)
    {
        var page = await library.RowsAsync(new PageRequest(skip, take), cancellationToken);
        return Ok(new ThemeRowsResponseModel { Total = page.Total, Items = [.. page.Items.Select(ThemeRowResponseModel.Of)] });
    }

    /// <summary>
    /// What the editor needs besides a theme: the themes in code it may build on, each with the tokens it declares and
    /// their defaults, dark ones for a theme with a dark scheme; and the site's default theme.
    /// </summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("editor")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeEditorResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Editor(CancellationToken cancellationToken = default) => Ok(new ThemeEditorResponseModel
    {
        Bases =
        [
            .. registry.All.Select(theme => new ThemeBaseResponseModel
            {
                Alias = theme.Name,
                Label = theme.Label,
                DarkScheme = theme.DarkScheme,
                Stylesheet = theme.Stylesheet,
                Tokens = [.. theme.Tokens.Select(token => new ThemeTokenResponseModel { Name = token.Key, Default = token.Value, DarkDefault = theme.DarkScheme ? theme.DarkTokens.GetValueOrDefault(token.Key) : null })],
            }),
        ],
        DefaultTheme = await library.DefaultAliasAsync(cancellationToken),
        MaxCustomCssLength = ThemeCss.MaxLength,
        PreviewAlias = ThemeLibrary.PreviewAlias,
    });

    /// <summary>A theme as its editor shows it.</summary>
    /// <param name="key">The theme.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid key, CancellationToken cancellationToken = default) =>
        await library.GetAsync(key, cancellationToken) is { } theme ? Ok(ThemeResponseModel.Of(theme)) : FormBuilderProblems.ThemeNotFound();

    /// <summary>
    /// The forms whose draft or live version names the theme, forms in the recycle bin included: those the user may open
    /// by name, the others only counted.
    /// </summary>
    /// <param name="key">The theme.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/used-by")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(LibraryUsersResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UsedBy(Guid key, CancellationToken cancellationToken = default) =>
        await library.GetAsync(key, cancellationToken) is null
            ? FormBuilderProblems.ThemeNotFound()
            : Ok(LibraryUsersResponseModel.Of(await library.UsersAsync(RequestAccess.User, key, cancellationToken)));

    /// <summary>Creates a theme, its key the name-based key of its alias; answers <c>400</c> with every problem, each at its path.</summary>
    /// <param name="request">The theme.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateThemeRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Created(Stored(await library.CreateAsync(request.Theme.ToInput(), RequestAccess.User.Key, cancellationToken)));
    }

    /// <summary>
    /// Saves a theme, based on the version the editor read; its alias stays: <c>409 concurrency_conflict</c> when it
    /// changed meanwhile, and <c>400</c> with every problem.
    /// </summary>
    /// <param name="key">The theme.</param>
    /// <param name="request">The theme, with the version it is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid key, UpdateThemeRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var changed = await library.UpdateAsync(key, request.ConcurrencyVersion, request.Theme.ToInput(), RequestAccess.User.Key, cancellationToken);
        return changed is { Theme: null, Problems.Count: 0 } ? FormBuilderProblems.ThemeNotFound() : Ok(ThemeResponseModel.Of(Stored(changed)));
    }

    /// <summary>
    /// Deletes a theme for good, based on the version read: <c>409 in_use</c> while a form uses it, <c>409 is_default</c>
    /// while it is the site's default, and <c>409 concurrency_conflict</c> when it changed meanwhile.
    /// </summary>
    /// <param name="key">The theme.</param>
    /// <param name="concurrencyVersion">The version the delete is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid key, int concurrencyVersion, CancellationToken cancellationToken = default) =>
        await library.DeleteAsync(key, concurrencyVersion, RequestAccess.User.Key, cancellationToken) switch
        {
            ThemeDelete.Deleted => NoContent(),
            ThemeDelete.IsDefault => FormBuilderProblems.IsDefault(),
            _ => FormBuilderProblems.ThemeNotFound(),
        };

    /// <summary>A copy of a theme with the name given and the first free alias after its own.</summary>
    /// <param name="key">The theme.</param>
    /// <param name="request">The copy's name.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/duplicate")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(Guid key, DuplicateThemeRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await library.DuplicateAsync(key, request.Name, RequestAccess.User.Key, cancellationToken) is { } created
            ? Created(Stored(created))
            : FormBuilderProblems.ThemeNotFound();
    }

    /// <summary>A theme as the JSON document an import reads, with a file name to save it under.</summary>
    /// <param name="key">The theme.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/export")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeExportResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid key, CancellationToken cancellationToken = default) =>
        await library.GetAsync(key, cancellationToken) is { } theme
            ? Ok(new ThemeExportResponseModel { FileName = $"{theme.Alias}.jfb-theme.json", Document = ThemeLibrary.Export(theme) })
            : FormBuilderProblems.ThemeNotFound();

    /// <summary>A new theme from an exported document; answers <c>400</c> with every problem, each at its path.</summary>
    /// <param name="request">The document.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("import")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemeResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import(ImportThemeRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Created(Stored(await library.ImportAsync(request.Document ?? string.Empty, RequestAccess.User.Key, cancellationToken)));
    }

    /// <summary>The layer the editor's preview applies for a theme as it holds it, with what the sanitiser took out and the problems a save would refuse.</summary>
    /// <param name="request">The theme as the editor holds it.</param>
    [HttpPost("preview")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(ThemePreviewResponseModel), StatusCodes.Status200OK)]
    public IActionResult Preview(PreviewThemeRequestModel request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var alias = FormBuilderThemes.IsAlias(request.Alias) ? request.Alias : ThemeLibrary.PreviewAlias;
        var preview = library.Preview(alias, request.BaseTheme ?? string.Empty, request.Tokens?.ToTokens() ?? ThemeTokens.None, request.CustomCss);
        return Ok(new ThemePreviewResponseModel
        {
            Alias = alias,
            Css = preview.Css,
            Removed = [.. preview.Removed.Select(removal => new ThemeCssRemovalResponseModel { Code = removal.Code, Text = removal.Text })],
            Problems = [.. preview.Problems.Select(LibraryProblemResponseModel.Of)],
        });
    }

    private ObjectResult Created(StoredTheme theme)
    {
        Response.Headers[Constants.Headers.GeneratedResource] = theme.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, ThemeResponseModel.Of(theme));
    }

    // The stored theme of a change, or the problems that refused it as a 400 with each at its path.
    private static StoredTheme Stored(ThemeResult result) =>
        result.Theme ?? throw new FormBuilderValidationException(result.Problems);
}
