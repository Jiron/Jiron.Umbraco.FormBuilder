using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// Every endpoint of the public API as the API explorer describes it (ADR-0084), for the tests that call them all: the
/// pending-migrations check and the base path. A new endpoint joins them by being written.
/// </summary>
internal static partial class PublicApiEndpoints
{
    /// <summary>Every endpoint of the public API, once each; the catch-all for unknown addresses is none.</summary>
    public static List<PublicApiEndpoint> All(IServiceProvider services) =>
        services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor is ControllerActionDescriptor action && typeof(FormBuilderPublicApiController).IsAssignableFrom(action.ControllerTypeInfo))
            .Select(description => new PublicApiEndpoint(description))
            .DistinctBy(endpoint => endpoint.Name)
            .ToList();

    /// <summary>The route of every action of the public API as the site routes it, the catch-all's included.</summary>
    public static List<string> Routes(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action && typeof(FormBuilderPublicApiController).IsAssignableFrom(action.ControllerTypeInfo))
            .Select(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .ToList();

    [GeneratedRegex(@"\{([^}:]+)[^}]*\}")]
    internal static partial Regex RouteValue();
}

/// <summary>An endpoint, which fills its route with the key of a check.</summary>
/// <param name="Description">What the API explorer says about it.</param>
internal sealed record PublicApiEndpoint(ApiDescription Description)
{
    /// <summary>Its HTTP method.</summary>
    public string Method => Description.HttpMethod!;

    /// <summary>Its method and route, such as <c>GET jfb/api/v1/forms/{keyOrAlias}</c>.</summary>
    public string Name => $"{Method} {Description.RelativePath}";

    /// <summary>Its path with every route value filled with <paramref name="key"/>.</summary>
    public string PathFor(Guid key) => "/" + PublicApiEndpoints.RouteValue().Replace(Description.RelativePath!, key.ToString("D"));

    /// <summary>Whether it takes a body; none does before the first that posts (6.1c).</summary>
    public bool TakesBody => HttpMethods.IsPost(Method) || HttpMethods.IsPut(Method) || HttpMethods.IsPatch(Method);
}
