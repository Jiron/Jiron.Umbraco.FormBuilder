using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// An outcome's live preview (ROADMAP 10.1c, ADR-0187): the outcome of the draft the outcome's sidebar holds, as visitors
/// get it after a send, for the sample entry, rendered by the server as it renders a real entry's and written by the
/// site's own view. It needs <c>design</c> on the form, as the draft it reads is the designer's, and it changes nothing.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/outcome-preview")]
[ApiExplorerSettings(GroupName = "Form settings")]
internal sealed class OutcomePreviewController(
    OutcomePreviews previews,
    FormDefinitionSerializer serializer,
    IFormBuilderFormService forms,
    ILanguageService languages) : FormBuilderManagementApiController
{
    /// <summary>The outcome as visitors get it for the sample entry, in a language of the site.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The draft's definition, the outcome and the language.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(OutcomePreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Render(Guid key, OutcomePreviewRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = serializer.Read(request.Definition.GetRawText());
        if (read.Definition is not { } definition)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        if (definition.Outcomes.All(outcome => outcome.Key != request.Outcome))
        {
            return FormBuilderProblems.OutcomeNotFound();
        }

        var culture = (request.Culture is { Length: > 0 } asked ? await languages.GetAsync(asked) : null)?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var preview = await previews.RenderAsync(ControllerContext, form, definition, request.Outcome, culture, cancellationToken);
        return Ok(new OutcomePreviewResponseModel { Html = preview.Html, Address = preview.Address, DelaySeconds = preview.DelaySeconds, Direction = preview.Direction });
    }
}

/// <summary>What an outcome's preview is asked for.</summary>
internal sealed record OutcomePreviewRequestModel
{
    /// <summary>The draft's definition as the outcome's sidebar would make it with Update, read as a draft save reads it.</summary>
    public required JsonElement Definition { get; init; }

    /// <summary>The key of the outcome to preview, one of the definition's.</summary>
    public required Guid Outcome { get; init; }

    /// <summary>A language of the site, such as <c>de-DE</c>, the outcome is shown in; the site's default language when it is none of them.</summary>
    public string? Culture { get; init; }
}

/// <summary>An outcome as visitors get it for the sample entry (ADR-0187).</summary>
internal sealed record OutcomePreviewResponseModel
{
    /// <summary>
    /// The outcome's markup after a send, as the site's own view writes it in the default theme: the message, the
    /// reference, "Go now" and the countdown before a redirect, and the copy of the answers; null for a redirect, which
    /// shows visitors no page.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>The address visitors go to for the sample entry, after the message or at once; null for none.</summary>
    public string? Address { get; init; }

    /// <summary>The seconds the message waits before its redirect, as the outcome sets them; 0 without a redirect.</summary>
    public required int DelaySeconds { get; init; }

    /// <summary>Which way the language's text runs, <c>ltr</c> or <c>rtl</c>, as the visitor's page sets it.</summary>
    public required string Direction { get; init; }
}
