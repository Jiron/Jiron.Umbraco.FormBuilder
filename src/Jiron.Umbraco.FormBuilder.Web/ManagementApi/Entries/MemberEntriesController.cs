using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// A member's entries (ROADMAP 14.3, ADR-0231), for the Entries view of Umbraco's member editor: those that keep the member,
/// of every form the user may view entries of, in any state but a draft, newest first, 50 at a time, with the forms they
/// belong to and what the user may do on each. A user who may view no form's entries gets an empty list, and a member
/// without entries, or one Umbraco no longer has, the same.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/members")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class MemberEntriesController(
    EntryLists lists,
    IFormBuilderFormService forms,
    IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    /// <summary>A page of the member's entries the user may view.</summary>
    /// <param name="key">The member's key.</param>
    /// <param name="after">The cursor of the page before, or none for the first page, which also counts them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/entries")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryListPageResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Entries(Guid key, string? after = null, CancellationToken cancellationToken = default)
    {
        var visibility = await access.GetFormVisibilityAsync(RequestAccess.User, FormBuilderPermissions.ViewEntries, cancellationToken);
        var page = await lists.ListOfMemberAsync(visibility, key, after, cancellationToken);
        var model = EntryListMapping.Model(page);
        return Ok(new EntryListPageResponseModel
        {
            Items = model.Items,
            Next = model.Next,
            Total = model.Total,
            More = model.More,
            Forms = await EntryListMapping.FormsAsync(page.Forms, forms, access, RequestAccess.User, cancellationToken),
        });
    }
}
