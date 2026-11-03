using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library's screens (ADR-0166): a folder's list, with each item's recent entries and who changed it last, and the
/// trash's, with when and by whom each item went there. Any user of the Forms section may ask, and gets what they see, as
/// the tree answers (ADR-0048); a folder the user does not see answers <c>404 folder_not_found</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/library")]
[ApiExplorerSettings(GroupName = "Library")]
internal sealed class LibraryListController(LibraryLists lists, IFormBuilderLibraryService library, IUserService users) : FormBuilderManagementApiController
{
    /// <summary>The items directly in a folder, or at the root, that the user sees: folders first, then forms, by name.</summary>
    /// <param name="folderKey">The folder, or none for the root.</param>
    /// <param name="skip">How many items to pass over.</param>
    /// <param name="take">How many items to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("items")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection(On = FormBuilderAccessResourceKind.Folder, Key = "folderKey")]
    [ProducesResponseType(typeof(PagedViewModel<LibraryRowResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Items(
        [FromQuery] Guid? folderKey = null,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (await lists.RowsAsync(RequestAccess.User, folderKey, new PageRequest(skip, take), cancellationToken) is not { } rows)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
        }

        var names = await UserNames.OfAsync(users, rows.Items.Select(row => row.Item.UpdatedByUserKey));
        return Ok(new PagedViewModel<LibraryRowResponseModel> { Total = rows.Total, Items = [.. rows.Items.Select(row => Row(row.Item, names, row.RecentEntries))] });
    }

    /// <summary>The trash's top items that the user sees, with when and by whom each went there.</summary>
    /// <param name="skip">How many items to pass over.</param>
    /// <param name="take">How many items to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("trash")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(PagedViewModel<LibraryRowResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Trash(
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var items = await library.ListTrashAsync(RequestAccess.User, new PageRequest(skip, take), cancellationToken);
        var names = await UserNames.OfAsync(users, items.Items.Select(item => item.UpdatedByUserKey));
        return Ok(new PagedViewModel<LibraryRowResponseModel> { Total = items.Total, Items = [.. items.Items.Select(item => Row(item, names, null))] });
    }

    private static LibraryRowResponseModel Row(LibraryItem item, IReadOnlyDictionary<Guid, string> names, long? recentEntries) => new()
    {
        Item = LibraryItemResponseModel.Of(item),
        UpdatedUtc = item.UpdatedUtc,
        UpdatedBy = item.UpdatedByUserKey is { } key ? names.GetValueOrDefault(key) : null,
        TrashedUtc = item.TrashedUtc,
        RecentEntries = recentEntries,
    };
}

/// <summary>The names of backoffice users, each read once for one answer (ADR-0166).</summary>
internal static class UserNames
{
    /// <summary>The name of each user of these keys who still exists: the name, or the user name when it has none.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> OfAsync(IUserService users, IEnumerable<Guid?> keys)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(keys);
        var names = new Dictionary<Guid, string>();
        foreach (var key in keys.OfType<Guid>().Distinct())
        {
            if (await users.GetAsync(key) is { } user)
            {
                names[key] = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
            }
        }

        return names;
    }
}
