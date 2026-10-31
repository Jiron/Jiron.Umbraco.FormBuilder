using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Content;

/// <summary>
/// The form picker's endpoints (section 4.7 "Content", ADR-0013, ADR-0133): the library's folders and forms level by level, a
/// search, the forms of given keys and the site's themes, for the picker in the content editor and for its data type's
/// settings. They are authorised by content access, not by Jiron Form Builder's permissions, and answer names and keys
/// only: published forms outside the trash that are not restricted, the restricted ones the user sees, and the folders
/// that lead to them; with <c>dataTypeKey</c>, only what that picker's settings allow.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/picker")]
[ApiExplorerSettings(GroupName = "Picker")]
internal sealed class PickerController(IFormBuilderPickerService picker, IDataTypeService dataTypes, FormBuilderThemes themes) : FormBuilderManagementApiController
{
    /// <summary>How long a search text may be.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>A page of a folder's folders and forms (none: the root, or the allowed ones of the picker's settings), folders first.</summary>
    /// <param name="parentKey">The folder, or none for the top level.</param>
    /// <param name="dataTypeKey">The picker's data type, whose settings limit the answer, or none.</param>
    /// <param name="skip">How many items to pass over.</param>
    /// <param name="take">How many items to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("children")]
    [MapToApiVersion("1.0")]
    [FormBuilderContentAccess]
    [ProducesResponseType(typeof(PagedViewModel<PickerItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Children(
        [FromQuery] Guid? parentKey = null,
        [FromQuery] Guid? dataTypeKey = null,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (await ScopeAsync(dataTypeKey) is not { } scope)
        {
            return FormBuilderProblems.DataTypeNotFound();
        }

        return await picker.ListChildrenAsync(RequestAccess.User, scope, parentKey == Guid.Empty ? null : parentKey, new PageRequest(skip, take), cancellationToken) is { } page
            ? Ok(Paged(page))
            : FormBuilderProblems.NotFound(Authorization.FormBuilderAccessResourceKind.Folder);
    }

    /// <summary>A page of the forms the picker may pick whose name or alias contains the text, by name.</summary>
    /// <param name="text">What the name or the alias contains, in any letter case.</param>
    /// <param name="dataTypeKey">The picker's data type, whose settings limit the answer, or none.</param>
    /// <param name="skip">How many forms to pass over.</param>
    /// <param name="take">How many forms to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("search")]
    [MapToApiVersion("1.0")]
    [FormBuilderContentAccess]
    [ProducesResponseType(typeof(PagedViewModel<PickerItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Search(
        [FromQuery, Required, StringLength(MaxSearchLength)] string text,
        [FromQuery] Guid? dataTypeKey = null,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (await ScopeAsync(dataTypeKey) is not { } scope)
        {
            return FormBuilderProblems.DataTypeNotFound();
        }

        return string.IsNullOrWhiteSpace(text)
            ? Ok(new PagedViewModel<PickerItemResponseModel> { Total = 0, Items = [] })
            : Ok(Paged(await picker.SearchAsync(RequestAccess.User, scope, text, new PageRequest(skip, take), cancellationToken)));
    }

    /// <summary>The forms of these keys the picker may pick, in the order asked: what a picked value and a block's label show.</summary>
    /// <param name="keys">The forms' keys, at most 100.</param>
    /// <param name="dataTypeKey">The picker's data type, whose settings limit the answer, or none.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("forms")]
    [MapToApiVersion("1.0")]
    [FormBuilderContentAccess]
    [ProducesResponseType(typeof(IReadOnlyList<PickerItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Forms(
        [FromQuery, MaxLength(FormBuilderPickerService.MaxKeys)] Guid[]? keys = null,
        [FromQuery] Guid? dataTypeKey = null,
        CancellationToken cancellationToken = default)
    {
        if (await ScopeAsync(dataTypeKey) is not { } scope)
        {
            return FormBuilderProblems.DataTypeNotFound();
        }

        var found = await picker.ListFormsAsync(RequestAccess.User, scope, keys ?? [], cancellationToken);
        return Ok(found.Select(PickerItemResponseModel.Of).ToList());
    }

    /// <summary>The site's themes, in the order the backoffice lists them: what a picker's settings may name.</summary>
    [HttpGet("themes")]
    [MapToApiVersion("1.0")]
    [FormBuilderContentAccess]
    [ProducesResponseType(typeof(IReadOnlyList<PickerThemeResponseModel>), StatusCodes.Status200OK)]
    public IActionResult Themes() => Ok(themes.All.Select(theme => new PickerThemeResponseModel { Alias = theme.Name, Label = theme.Label }).ToList());

    private static PagedViewModel<PickerItemResponseModel> Paged(Page<PickerItem> page) =>
        new() { Total = page.Total, Items = [.. page.Items.Select(PickerItemResponseModel.Of)] };

    // The scope of a picker's settings; none limits nothing, and a key that names no picker's data type, null.
    private async Task<PickerScope?> ScopeAsync(Guid? dataTypeKey)
    {
        if (dataTypeKey is not { } key || key == Guid.Empty)
        {
            return PickerScope.Everything;
        }

        if (await dataTypes.GetAsync(key) is not { EditorAlias: FormPickerPropertyEditor.EditorAlias } dataType)
        {
            return null;
        }

        var settings = dataType.ConfigurationObject as FormPickerConfiguration ?? new FormPickerConfiguration();
        return new PickerScope(settings.Folders, settings.Forms);
    }
}

/// <summary>A folder or form as the form picker shows it: its name and key only (ADR-0013).</summary>
internal sealed record PickerItemResponseModel
{
    /// <summary>The folder's or form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary><c>folder</c> or <c>form</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>A form's alias; null for a folder.</summary>
    public string? Alias { get; init; }

    /// <summary>Whether a folder leads to a form the picker may pick; false for a form.</summary>
    public required bool HasChildren { get; init; }

    internal static PickerItemResponseModel Of(PickerItem item) => new()
    {
        Key = item.Key,
        Kind = item.Kind == LibraryItemKind.Folder ? "folder" : "form",
        Name = item.Name,
        Alias = item.Alias,
        HasChildren = item.HasChildren,
    };
}

/// <summary>A theme a form picker's settings may name.</summary>
internal sealed record PickerThemeResponseModel
{
    /// <summary>The theme's alias, which the settings store.</summary>
    public required string Alias { get; init; }

    /// <summary>The theme's label.</summary>
    public required string Label { get; init; }
}
