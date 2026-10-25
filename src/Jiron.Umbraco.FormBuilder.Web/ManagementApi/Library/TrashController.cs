using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The trash (ADR-0050): its top items that the signed-in user sees, and emptying it of what they may delete for good
/// (<c>design</c> and <c>deleteEntries</c> on everything an item takes). Any user of the Forms section may ask.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/trash")]
[ApiExplorerSettings(GroupName = "Trash")]
internal sealed class TrashController(IFormBuilderLibraryService library) : FormBuilderManagementApiController
{
    /// <summary>The trash's top items that the user sees: folders whose parent is outside it, then forms trashed on their own.</summary>
    /// <param name="skip">How many items to pass over.</param>
    /// <param name="take">How many items to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(PagedViewModel<LibraryItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default) =>
        Ok(TreeController.Paged(await library.ListTrashAsync(RequestAccess.User, new PageRequest(skip, take), cancellationToken)));

    /// <summary>Empties the trash of what the user may delete for good, and says how many top items stayed.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete]
    [ActionName("Empty")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(TrashEmptiedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmptyTrash(CancellationToken cancellationToken = default)
    {
        var emptied = await library.EmptyTrashAsync(RequestAccess.User, RequestAccess.User.Key, cancellationToken);
        return Ok(new TrashEmptiedResponseModel { Deleted = LibraryDeletionResponseModel.Of(emptied.Deleted), Left = emptied.Left });
    }
}
