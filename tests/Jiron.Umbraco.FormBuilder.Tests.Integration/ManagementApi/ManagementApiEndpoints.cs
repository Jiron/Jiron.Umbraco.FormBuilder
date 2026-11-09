using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Every endpoint of the management API as the API explorer describes it (ADR-0050), with its access declarations, for
/// the tests that call them all: the permission matrix and the pending-migrations check. A new endpoint joins them by
/// being written.
/// </summary>
internal static partial class ManagementApiEndpoints
{
    /// <summary>Every endpoint of the management API, once each.</summary>
    public static List<ManagementApiEndpoint> All(IServiceProvider services) =>
        services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor is ControllerActionDescriptor action && typeof(FormBuilderManagementApiController).IsAssignableFrom(action.ControllerTypeInfo))
            .Select(description => new ManagementApiEndpoint(description, ((ControllerActionDescriptor)description.ActionDescriptor).MethodInfo.GetCustomAttributes<FormBuilderAccessAttribute>(true).ToList()))
            .DistinctBy(endpoint => endpoint.Name)
            .ToList();

    /// <summary>Calls an endpoint as <paramref name="role"/>, with <paramref name="key"/> in its route, its query or its body.</summary>
    public static Task<ApiAnswer> CallAsync(this ManagementApiCaller api, string? role, ManagementApiEndpoint endpoint, Guid key)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(endpoint);
        return api.CallAsync(role, new HttpMethod(endpoint.Method), endpoint.PathFor(key), endpoint.BodyFor(key));
    }

    [GeneratedRegex(@"\{([^}:]+)[^}]*\}")]
    internal static partial Regex RouteValue();
}

/// <summary>
/// An endpoint, which fills its route, query and body with the key of a check: every value fits its parameter's type, so
/// the permission check, not routing, answers (an <c>int</c> route given a GUID would answer <c>404</c> first, ROADMAP 3.3).
/// </summary>
/// <param name="Description">What the API explorer says about it.</param>
/// <param name="Declarations">Its access declarations; one, unless it is broken.</param>
internal sealed record ManagementApiEndpoint(ApiDescription Description, IReadOnlyList<FormBuilderAccessAttribute> Declarations)
{
    /// <summary>The document type a route's alias names, which a test that calls such a route creates.</summary>
    public const string DocumentTypeAlias = "jfbMatrix";

    private const string Prefix = "umbraco/jfb/management/api/v1/";

    /// <summary>Its HTTP method.</summary>
    public string Method => Description.HttpMethod!;

    /// <summary>Its method and route below the base path, such as <c>GET folder/{key}</c>.</summary>
    public string Name => $"{Method} {Template[Prefix.Length..]}";

    /// <summary>Whether a request without the backoffice's token reaches it (<c>[AllowAnonymous]</c>).</summary>
    public bool AllowsAnonymous => Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();

    private string Template => Description.RelativePath!.Replace("v{version}", "v1", StringComparison.Ordinal);

    private string? Key => Declarations.Count == 1 ? Declarations[0].Key : null;

    /// <summary>Where a value of that name comes from: a parameter's source, the body for a property of it, or null.</summary>
    public BindingSource? KeySource(string key) =>
        Description.ParameterDescriptions.FirstOrDefault(parameter => parameter.Name == key)?.Source
        ?? (Description.ParameterDescriptions.Any(parameter => parameter.Source == BindingSource.Body && parameter.Type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null) ? BindingSource.Body : null);

    /// <summary>
    /// Its path with every route value filled, and in the query the declared key when it lives there and every other
    /// value it requires, each by its type; a field type's alias is the built-in <c>text</c>, which every site has, and a
    /// document type's is <see cref="DocumentTypeAlias"/>.
    /// </summary>
    public string PathFor(Guid key)
    {
        string RouteValueOf(string name) =>
            Description.ParameterDescriptions.FirstOrDefault(parameter => parameter.Source == BindingSource.Path && parameter.Name == name)?.Type is { } routed && (routed == typeof(int) || routed == typeof(long))
                ? "1"
                : name == "alias" ? (Template.Contains("/document-type/", StringComparison.Ordinal) ? DocumentTypeAlias : "text") : key.ToString();
        var path = ManagementApiEndpoints.RouteValue().Replace(Template[Prefix.Length..], match => RouteValueOf(match.Groups[1].Value));
        var query = Description.ParameterDescriptions
            .Where(parameter => parameter.Source == BindingSource.Query && (parameter.Name == Key || IsRequired(parameter)))
            .Select(parameter => $"{parameter.Name}={(parameter.Name == Key ? key.ToString() : QueryValueOf(parameter.Type))}")
            .ToList();
        return query.Count == 0 ? path : $"{path}?{string.Join("&", query)}";
    }

    /// <summary>
    /// A body that reads, when it takes one: each property filled by its type, the declared key with the key, and a
    /// property that holds one of a few words with one of them (a summary email's <c>frequency</c> is <c>daily</c>, a dry
    /// run's <c>span</c> 30 days).
    /// </summary>
    public Dictionary<string, object?>? BodyFor(Guid key) =>
        Description.ParameterDescriptions.FirstOrDefault(parameter => parameter.Source == BindingSource.Body)?.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                property => JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                property => string.Equals(property.Name, Key, StringComparison.OrdinalIgnoreCase) ? key : Words.GetValueOrDefault(property.Name) ?? ValueOf(property.PropertyType));

    private static readonly Dictionary<string, object> Words = new(StringComparer.Ordinal) { ["Frequency"] = "daily", ["Span"] = 30 };

    private static object? ValueOf(Type type) => type == typeof(string) ? "Calls" : type == typeof(bool) ? false : type == typeof(int) ? 1 : null;

    // Required by [BindRequired], which the API explorer reports, or by [Required], which it leaves to validation.
    private static bool IsRequired(ApiParameterDescription parameter) =>
        parameter.IsRequired || (parameter.ParameterDescriptor as ControllerParameterDescriptor)?.ParameterInfo.GetCustomAttribute<RequiredAttribute>() is not null;

    private static string QueryValueOf(Type type) => type == typeof(bool) ? "false" : Convert.ToString(ValueOf(type), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}
