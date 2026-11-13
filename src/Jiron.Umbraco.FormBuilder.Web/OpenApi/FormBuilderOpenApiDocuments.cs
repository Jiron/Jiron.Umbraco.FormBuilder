using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Jiron.Umbraco.FormBuilder.Web.OpenApi;

/// <summary>
/// Names the operations of Jiron Form Builder's documents after their controller and action (<c>FolderCreate</c>,
/// <c>TreeChildren</c>, <c>FormDefinition</c>), so the generated clients have short, stable method names. It runs after
/// Umbraco's own operation names, which it replaces for Jiron Form Builder's controllers only.
/// </summary>
internal sealed class FormBuilderOperationIds : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        if (NameOf(context.Description.ActionDescriptor) is { } name)
        {
            operation.OperationId = name;
        }

        return Task.CompletedTask;
    }

    /// <summary>The name of an action of Jiron Form Builder's controllers; null for anyone else's.</summary>
    /// <param name="action">The action.</param>
    public static string? NameOf(ActionDescriptor? action) =>
        action is ControllerActionDescriptor controller
            && (typeof(FormBuilderManagementApiController).IsAssignableFrom(controller.ControllerTypeInfo) || typeof(FormBuilderPublicApiController).IsAssignableFrom(controller.ControllerTypeInfo))
            ? controller.ControllerName + controller.ActionName
            : null;
}

/// <summary>What Jiron Form Builder's three documents share: one registration per name, and their version and description.</summary>
internal static class FormBuilderOpenApiDocuments
{
    /// <summary>
    /// Records that a document is registered; false when it already was. Umbraco and Microsoft.AspNetCore.OpenApi add a
    /// document's transformers again with every registration, which would run each rule twice.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The document's name.</param>
    public static bool Claim(IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FormBuilderOpenApiDocument) && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, name)))
        {
            return false;
        }

        services.AddKeyedSingleton(name, new FormBuilderOpenApiDocument(name));
        return true;
    }

    /// <summary>A document transformer that gives the document its version and description.</summary>
    /// <param name="version">The API's version.</param>
    /// <param name="description">What the document holds.</param>
    public static Func<OpenApiDocument, OpenApiDocumentTransformerContext, CancellationToken, Task> Describe(string version, string description) =>
        (document, _, _) =>
        {
            ArgumentNullException.ThrowIfNull(document);
            document.Info ??= new OpenApiInfo();
            document.Info.Version = version;
            document.Info.Description = description;
            return Task.CompletedTask;
        };
}

/// <summary>The mark that a document of Jiron Form Builder is registered (<see cref="FormBuilderOpenApiDocuments.Claim"/>).</summary>
/// <param name="Name">The document's name.</param>
internal sealed record FormBuilderOpenApiDocument(string Name);
