using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DependencyInjection;
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
/// The OpenAPI documents of the backoffice's APIs on Umbraco 18 (ROADMAP 3.4, Phase 19, ADR-0250): Umbraco's document
/// builder over Microsoft.AspNetCore.OpenApi, served at <c>/umbraco/openapi/{name}.json</c> outside production. Each
/// takes the controllers with <c>[MapToApi(name)]</c>, the backoffice's JSON settings and sign-in, Jiron Form Builder's
/// operation names and the schema rules that keep the generated client's types as on Umbraco 17.
/// </summary>
internal static class FormBuilderManagementApiDocuments
{
    /// <summary>
    /// Registers a backoffice document once: Umbraco adds a document's transformers each time it is registered, so a
    /// second registration of the same name is left out.
    /// </summary>
    /// <param name="builder">The Umbraco builder.</param>
    /// <param name="name">The document's name, as the controllers' <c>[MapToApi]</c> names it.</param>
    /// <param name="title">The document's title.</param>
    /// <param name="description">What the document holds.</param>
    public static IUmbracoBuilder AddFormBuilderBackOfficeDocument(this IUmbracoBuilder builder, string name, string title, string description)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!FormBuilderOpenApiDocuments.Claim(builder.Services, name))
        {
            return builder;
        }

        return builder.AddBackOfficeOpenApiDocument(name, document => document
            .WithTitle(title)
            // The controllers' own JSON settings ([JsonOptionsName(BackOffice)]): enums as text, as the API answers.
            .WithJsonOptions(Constants.JsonOptionsNames.BackOffice)
            .WithBackOfficeAuthentication()
            .ConfigureOpenApiOptions(options =>
            {
                options.AddDocumentTransformer(FormBuilderOpenApiDocuments.Describe("1.0", description));
                options.AddOperationTransformer<FormBuilderOperationIds>();
                FormBuilderOpenApiSchemas.Apply(options);
            }));
    }

    /// <summary>Registers the document <c>jfb</c>.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public static IUmbracoBuilder AddFormBuilderManagementApiDocument(this IUmbracoBuilder builder) =>
        builder.AddFormBuilderBackOfficeDocument(
            FormBuilderManagementApi.ApiName,
            "Jiron Form Builder management API",
            "The Form Builder section's API: backoffice users of the section, each call by its Jiron Form Builder permission.");
}
