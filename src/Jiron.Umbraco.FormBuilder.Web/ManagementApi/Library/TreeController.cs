using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library tree (ADR-0050): the folders and forms at the root or in a folder, as the signed-in user sees them
/// (ADR-0048), folders first, each with the user's permissions on it. Any user of the Forms section may ask; a folder
/// the user does not see answers <c>404 folder_not_found</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/tree")]
[ApiExplorerSettings(GroupName = "Tree")]
internal sealed class TreeController(IFormBuilderLibraryService library) : FormBuilderManagementApiController
{
    /// <summary>The items at the root that the user sees.</summary>
    /// <param name="skip">How many items to pass over.</param>
    /// <param name="take">How many items to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("root")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(PagedViewModel<LibraryItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Root([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default) =>
        Ok(Paged((await library.ListChildrenAsync(RequestAccess.User, null, new PageRequest(skip, take), cancellationToken))!));

    /// <summary>The items in a folder that the user sees.</summary>
    /// <param name="parentKey">The folder.</param>
    /// <param name="skip">How many items to pass over.</param>
    /// <param name="take">How many items to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("children")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection(On = FormBuilderAccessResourceKind.Folder, Key = "parentKey")]
    [ProducesResponseType(typeof(PagedViewModel<LibraryItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Children([FromQuery] Guid parentKey, [FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default) =>
        await library.ListChildrenAsync(RequestAccess.User, parentKey == Guid.Empty ? null : parentKey, new PageRequest(skip, take), cancellationToken) is { } page
            ? Ok(Paged(page))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);

    /// <summary>A page of items as the API answers with it.</summary>
    internal static PagedViewModel<LibraryItemResponseModel> Paged(Page<LibraryItem> page) =>
        new() { Total = page.Total, Items = page.Items.Select(LibraryItemResponseModel.Of).ToList() };
}
