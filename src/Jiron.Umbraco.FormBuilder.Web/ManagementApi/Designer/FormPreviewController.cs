using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The form's preview (ROADMAP 10.3e, ADR-0203): the saved draft as a page of the site shows it, in a document the
/// backoffice writes into a sandboxed frame, with the ticket that lets the frame call the public API for that version,
/// and new tickets for an open preview. Both need <c>design</c> on the form, as the draft is the designer's; neither
/// changes anything, and neither answer is kept, as no answer of Umbraco's management API is (<c>no-store</c>).
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/preview")]
[ApiExplorerSettings(GroupName = "Form preview")]
internal sealed class FormPreviewController(
    FormPreviewDocuments documents,
    PreviewTickets tickets,
    IFormBuilderFormService forms,
    IFormBuilderFormVersionService versions,
    ILanguageService languages) : FormBuilderManagementApiController
{
    /// <summary>The saved draft as visitors would get it, in a language of the site, light or dark.</summary>
    /// <param name="key">The form.</param>
    /// <param name="culture">A language of the site, such as <c>de-DE</c>; the site's default language when it is none of them.</param>
    /// <param name="dark">Whether the page is dark, which a theme with the dark scheme shows.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormPreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Render(Guid key, [FromQuery] string? culture, [FromQuery] bool dark = false, CancellationToken cancellationToken = default)
    {
        if (await forms.GetDraftAsync(key, cancellationToken) is not { } draft)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var shown = (culture is { Length: > 0 } asked ? await languages.GetAsync(asked) : null)?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var result = await documents.RenderAsync(ControllerContext, key, draft.Form.Name, draft.DraftVersion, shown, dark, cancellationToken);
        if (result is not { Status: FormPreviewStatus.Done, Document: { } document })
        {
            return result.Status == FormPreviewStatus.RulesInvalid ? FormBuilderProblems.RulesInvalid() : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        return Ok(new FormPreviewResponseModel
        {
            Document = document.Html,
            Version = draft.DraftVersion,
            Culture = shown,
            DarkScheme = document.DarkScheme,
            LifetimeSeconds = (int)PreviewTickets.Lifetime.TotalSeconds,
        });
    }

    /// <summary>A new ticket for a version of the form, which an open preview hands its frame (ADR-0203).</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The version the preview shows.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("ticket")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormPreviewTicketResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ticket(Guid key, FormPreviewTicketRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetAsync(key, cancellationToken) is not { TrashedUtc: null })
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        if (request.Version < 1 || await versions.GetVersionAsync(key, request.Version, cancellationToken) is null)
        {
            return FormBuilderProblems.VersionNotFound();
        }

        var ticket = tickets.Issue(key, request.Version);
        return Ok(new FormPreviewTicketResponseModel { Ticket = ticket.Value, LifetimeSeconds = (int)PreviewTickets.Lifetime.TotalSeconds });
    }
}

/// <summary>The preview's document (ADR-0203).</summary>
internal sealed record FormPreviewResponseModel
{
    /// <summary>
    /// The whole document, which the backoffice writes into a frame sandboxed without <c>allow-same-origin</c>: the form
    /// in its theme and the language shown, its stylesheets, the website runtime, and the preview script with its ticket.
    /// </summary>
    public required string Document { get; init; }

    /// <summary>The number of the draft's version the document shows, which a new ticket names.</summary>
    public required int Version { get; init; }

    /// <summary>The language the document is in.</summary>
    public required string Culture { get; init; }

    /// <summary>Whether the form's theme offers the dark scheme, so the preview may show it.</summary>
    public required bool DarkScheme { get; init; }

    /// <summary>How many seconds the ticket in the document counts from now.</summary>
    public required int LifetimeSeconds { get; init; }

    /// <inheritdoc />
    public override string ToString() => nameof(FormPreviewResponseModel);
}

/// <summary>What a new ticket is for.</summary>
internal sealed record FormPreviewTicketRequestModel
{
    /// <summary>The number of the version the preview shows.</summary>
    public required int Version { get; init; }
}

/// <summary>A new ticket for the preview's frame (ADR-0203).</summary>
internal sealed record FormPreviewTicketResponseModel
{
    /// <summary>The ticket, which the frame sends as <c>Authorization: Jfb-Preview &lt;ticket&gt;</c>.</summary>
    public required string Ticket { get; init; }

    /// <summary>How many seconds it counts from now.</summary>
    public required int LifetimeSeconds { get; init; }

    /// <inheritdoc />
    public override string ToString() => nameof(FormPreviewTicketResponseModel);
}
