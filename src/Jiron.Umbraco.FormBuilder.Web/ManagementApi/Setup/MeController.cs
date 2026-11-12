using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Setup;

/// <summary>
/// What the signed-in user holds, so that the section shows only what they can use (ADR-0013, ADR-0165). Each endpoint still checks
/// for itself: this only spares a user the menus that would refuse them.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/me")]
[ApiExplorerSettings(GroupName = "Setup")]
internal sealed class MeController(IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    /// <summary>The user's permissions, by name: those they hold globally, and those they hold anywhere, also on one folder or form.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(MyPermissionsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Permissions(CancellationToken cancellationToken = default) => Ok(new MyPermissionsResponseModel
    {
        Global = PermissionRules.NamesOf(await access.GetGlobalAsync(RequestAccess.User, cancellationToken)),
        Anywhere = PermissionRules.NamesOf(await access.GetAnywhereAsync(RequestAccess.User, cancellationToken)),
    });
}

/// <summary>What the signed-in user holds.</summary>
internal sealed class MyPermissionsResponseModel
{
    /// <summary>The permissions they hold globally.</summary>
    public required IReadOnlyList<string> Global { get; init; }

    /// <summary>The permissions they hold anywhere: globally, or on any folder or form.</summary>
    public required IReadOnlyList<string> Anywhere { get; init; }
}
