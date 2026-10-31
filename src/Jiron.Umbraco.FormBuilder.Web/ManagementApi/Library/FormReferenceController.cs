using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Factories;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Security.Authorization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// "Used on" (ADR-0017): the pages whose content picks a form, shaped like Umbraco's own document references, so the
/// form's Info view lists them with Umbraco's references box and trash and delete can name them. Seeing the form is
/// enough; a page outside the user's content start nodes is counted, not named.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/referenced-by")]
[ApiExplorerSettings(GroupName = "Form")]
internal sealed class FormReferenceController(
    FormUsage usage,
    IAuthorizationHelper users,
    IContentTypeService contentTypes,
    IDocumentPresentationFactory documents)
    : FormBuilderManagementApiController
{
    /// <summary>A page of the pages that use the form, by name, with how many more the user may not see.</summary>
    /// <param name="key">The form.</param>
    /// <param name="skip">How many pages to pass over.</param>
    /// <param name="take">How many pages to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection(On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormReferencesResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid key,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, PageRequest.MaxTake)] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (RequestAccess.Form is not { } form || form.Key != key)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var uses = await usage.OfAsync(key, users.GetUmbracoUser(User), cancellationToken);
        var typeNames = new Dictionary<Guid, string?>();
        return Ok(new FormReferencesResponseModel
        {
            Total = uses.Shown.Count,
            Items = [.. uses.Shown.Skip(skip).Take(take).Select(page => Item(page, typeNames))],
            NotShown = uses.NotShown,
        });
    }

    private FormDocumentReferenceResponseModel Item(IDocumentEntitySlim page, Dictionary<Guid, string?> typeNames)
    {
        if (!typeNames.TryGetValue(page.ContentTypeKey, out var typeName))
        {
            typeName = contentTypes.Get(page.ContentTypeKey)?.Name;
            typeNames[page.ContentTypeKey] = typeName;
        }

        return new FormDocumentReferenceResponseModel
        {
            Id = page.Key,
            Name = page.Name,
            Published = page.Published,
            DocumentType = new FormDocumentTypeReferenceResponseModel
            {
                Id = page.ContentTypeKey,
                Icon = page.ContentTypeIcon,
                Alias = page.ContentTypeAlias,
                Name = typeName,
            },
            Variants = [.. documents.CreateVariantsItemResponseModels(page).Select(variant => new FormDocumentVariantResponseModel
            {
                Name = variant.Name,
                Culture = variant.Culture,
                State = variant.State.ToString(),
            })],
        };
    }
}

/// <summary>A page of the pages that use a form (ADR-0017), as Umbraco pages its references, plus the pages not shown.</summary>
internal sealed record FormReferencesResponseModel
{
    /// <summary>How many pages the user may see use the form.</summary>
    public required long Total { get; init; }

    /// <summary>The page's pages, by name.</summary>
    public required IReadOnlyList<FormDocumentReferenceResponseModel> Items { get; init; }

    /// <summary>How many more pages use the form, outside the user's content start nodes, so not named.</summary>
    public required int NotShown { get; init; }
}

/// <summary>A page that uses a form, in the shape of Umbraco's document reference, which the backoffice maps by its type.</summary>
internal sealed record FormDocumentReferenceResponseModel
{
    /// <summary>The name of Umbraco's model, which the backoffice's references box maps by.</summary>
    public const string TypeName = "DocumentReferenceResponseModel";

    /// <summary>Always <see cref="TypeName"/>.</summary>
    [JsonPropertyName("$type")]
    public string Type { get; } = TypeName;

    /// <summary>The page's key.</summary>
    public required Guid Id { get; init; }

    /// <summary>The page's name.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the page is published.</summary>
    public bool? Published { get; init; }

    /// <summary>The page's document type.</summary>
    public required FormDocumentTypeReferenceResponseModel DocumentType { get; init; }

    /// <summary>The page's cultures with their names and states; one without a culture for an invariant page.</summary>
    public required IReadOnlyList<FormDocumentVariantResponseModel> Variants { get; init; }
}

/// <summary>The document type of a page that uses a form.</summary>
internal sealed record FormDocumentTypeReferenceResponseModel
{
    /// <summary>The document type's key.</summary>
    public required Guid Id { get; init; }

    /// <summary>Its icon.</summary>
    public string? Icon { get; init; }

    /// <summary>Its alias.</summary>
    public string? Alias { get; init; }

    /// <summary>Its name.</summary>
    public string? Name { get; init; }
}

/// <summary>A culture of a page that uses a form.</summary>
internal sealed record FormDocumentVariantResponseModel
{
    /// <summary>The page's name in it.</summary>
    public required string Name { get; init; }

    /// <summary>The culture, or null for an invariant page.</summary>
    public string? Culture { get; init; }

    /// <summary>Umbraco's state of the culture: <c>NotCreated</c>, <c>Draft</c>, <c>Published</c> or <c>PublishedPendingChanges</c>.</summary>
    public required string State { get; init; }
}
