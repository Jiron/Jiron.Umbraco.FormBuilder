using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Authorization;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi;

/// <summary>
/// The management API (ADR-0050): <c>/umbraco/jfb/management/api/v1</c>, the OpenAPI document <c>jfb</c>, which the
/// backoffice's generated client is made from.
/// </summary>
internal static class FormBuilderManagementApi
{
    /// <summary>The OpenAPI document's name.</summary>
    public const string ApiName = "jfb";

    /// <summary>The route below <c>/umbraco</c>, before each controller's own part.</summary>
    public const string Route = "jfb/management/api/v{version:apiVersion}";
}

/// <summary>
/// The base of every controller of the management API (ADR-0050): a user signed in with the backoffice's token, the
/// backoffice's JSON settings, the document <c>jfb</c>, the access filter that reads each action's declaration
/// (<see cref="FormBuilderPermissionAttribute"/> or <see cref="FormBuilderSectionAttribute"/>), and the exception filter
/// that turns refusals into answers. Each controller adds its route with <c>[BackOfficeRoute]</c> and its version with
/// <c>[ApiVersion("1.0")]</c>, which is not inherited. The controllers are internal like the services they use; the
/// management API's registration adds them to MVC.
/// </summary>
[MapToApi(FormBuilderManagementApi.ApiName)]
[JsonOptionsName(Constants.JsonOptionsNames.BackOffice)]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[ServiceFilter(typeof(FormBuilderAccessFilter), Order = FormBuilderAccessFilter.FilterOrder)]
[ServiceFilter(typeof(FormBuilderExceptionFilter))]
internal abstract class FormBuilderManagementApiController : ManagementApiControllerBase
{
    /// <summary>The request's user and what the access filter read for it.</summary>
    /// <exception cref="InvalidOperationException">The action runs without the access filter.</exception>
    protected FormBuilderRequestAccess RequestAccess =>
        HttpContext.Features.Get<FormBuilderRequestAccess>() ?? throw new InvalidOperationException("The access filter did not run for this action.");
}

/// <summary>
/// The document <c>jfb</c> with the backoffice's security requirements. It is registered here only, because Umbraco 18
/// replaces Swashbuckle (ROADMAP 3.4, Phase 19).
/// </summary>
internal sealed class ConfigureFormBuilderManagementApiSwagger : IConfigureOptions<SwaggerGenOptions>
{
    /// <inheritdoc />
    public void Configure(SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SwaggerDoc(FormBuilderManagementApi.ApiName, new OpenApiInfo
        {
            Title = "Jiron Form Builder management API",
            Version = "1.0",
            Description = "The Form Builder section's API: backoffice users of the section, each call by its Jiron Form Builder permission.",
        });
        options.OperationFilter<FormBuilderBackOfficeSecurityRequirements>();
    }
}

/// <summary>
/// Names the document's operations after their controller and action (<c>FolderCreate</c>, <c>TreeChildren</c>), so the
/// generated client has short, stable method names. Umbraco's own handler names only its own API's operations.
/// </summary>
internal sealed class FormBuilderOperationIdHandler : IOperationIdHandler
{
    /// <inheritdoc />
    public bool CanHandle(ApiDescription apiDescription) =>
        apiDescription?.ActionDescriptor is ControllerActionDescriptor action && typeof(FormBuilderManagementApiController).IsAssignableFrom(action.ControllerTypeInfo);

    /// <inheritdoc />
    public string Handle(ApiDescription apiDescription)
    {
        ArgumentNullException.ThrowIfNull(apiDescription);
        var action = (ControllerActionDescriptor)apiDescription.ActionDescriptor;
        return action.ControllerName + action.ActionName;
    }
}

/// <summary>
/// Names the schemas of Jiron Form Builder's models as Umbraco names its own (<c>FolderResponseModel</c>), so that two
/// models of one generic type never share a name, which makes the document fail (1.2b).
/// </summary>
internal sealed class FormBuilderSchemaIdHandler : SchemaIdHandler
{
    /// <inheritdoc />
    public override bool CanHandle(Type type) =>
        type?.Namespace?.StartsWith("Jiron.Umbraco.FormBuilder", StringComparison.Ordinal) == true;
}

/// <summary>Marks the document's operations as needing a backoffice sign-in.</summary>
internal sealed class FormBuilderBackOfficeSecurityRequirements : BackOfficeSecurityRequirementsOperationFilterBase
{
    /// <inheritdoc />
    protected override string ApiName => FormBuilderManagementApi.ApiName;
}
