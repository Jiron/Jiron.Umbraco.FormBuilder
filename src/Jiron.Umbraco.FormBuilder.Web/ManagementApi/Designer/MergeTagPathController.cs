using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The paths a place's merge tags may use in the draft the designer holds, for the inspector's tag picker (ADR-0173).
/// It needs <c>design</c> on the form, as the draft it reads is the designer's.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/merge-tag-paths")]
[ApiExplorerSettings(GroupName = "Merge tag")]
internal sealed class MergeTagPathController(DraftMergeTagPaths paths, FormDefinitionSerializer serializer) : FormBuilderManagementApiController
{
    /// <summary>The place's paths in the definition the request holds, labelled in its culture, in the picker's order.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The place, the culture and the draft's definition.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(MergeTagPathsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid key, MergeTagPathsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The form's key is the permission's, which its filter read from the route before the action ran.
        _ = key;
        cancellationToken.ThrowIfCancellationRequested();
        if (!DefinitionWords<MergeTagPlace>.TryRead(request.Place, out var place))
        {
            ModelState.AddModelError("place", $"The place is one of {string.Join(", ", DefinitionWords<MergeTagPlace>.All)}.");
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        var read = serializer.Read(request.Definition.GetRawText());
        if (read.Definition is not { } definition)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        var found = await paths.PathsAsync(definition, place, request.Culture);
        return Ok(new MergeTagPathsResponseModel { Paths = [.. found.Select(MergeTagPathResponseModel.Of)] });
    }
}

/// <summary>What the tag picker asks for (ADR-0173).</summary>
internal sealed record MergeTagPathsRequestModel
{
    /// <summary>Where the tags are written, such as <c>visitorText</c>, <c>consentText</c> or <c>defaultValue</c>.</summary>
    public required string Place { get; init; }

    /// <summary>The culture of the labels; one that is no culture's name asks for the default texts.</summary>
    public required string Culture { get; init; }

    /// <summary>The draft's definition as the designer holds it, read as a draft save reads it.</summary>
    public required JsonElement Definition { get; init; }
}

/// <summary>The paths of a place.</summary>
internal sealed record MergeTagPathsResponseModel
{
    /// <summary>The paths, in the order the picker offers them: the built-in sources first, then the site's.</summary>
    public required IReadOnlyList<MergeTagPathResponseModel> Paths { get; init; }
}

/// <summary>A path the picker offers.</summary>
internal sealed record MergeTagPathResponseModel
{
    /// <summary>The whole path, its source's name first, such as <c>field.email</c>; the picker writes <c>{{ path }}</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The source's name.</summary>
    public required string Source { get; init; }

    /// <summary>Whether the site added the source, so the backoffice has no words of its own for it.</summary>
    public required bool IsSiteSource { get; init; }

    /// <summary>A field's label in the culture asked, or what the site's source shows; none to show the path.</summary>
    public string? Label { get; init; }

    /// <summary>The model of a path.</summary>
    public static MergeTagPathResponseModel Of(DraftMergeTagPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new MergeTagPathResponseModel { Path = path.Path, Source = path.Source, IsSiteSource = path.IsSiteSource, Label = path.Label };
    }
}
