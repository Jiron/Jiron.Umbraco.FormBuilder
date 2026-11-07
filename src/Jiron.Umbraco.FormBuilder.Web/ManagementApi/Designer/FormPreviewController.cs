using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
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
/// The form's preview (ROADMAP 10.3e and 10.3f, ADR-0203, ADR-0204): the saved draft as a page of the site shows it,
/// empty or with the sample answers, in a document the backoffice writes into a sandboxed frame, with the ticket that lets
/// the frame call the public API for that version, and new tickets for an open preview; and the test entries the frame
/// sends, whose actions Run actions runs. All need <c>design</c> on the form, as the draft is the designer's; a ticket
/// whose test entries run their actions, and running a test entry's actions, need <c>manageActions</c> too, as their mail
/// goes out. No answer is kept, as no answer of Umbraco's management API is (<c>no-store</c>).
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/preview")]
[ApiExplorerSettings(GroupName = "Form preview")]
internal sealed class FormPreviewController(
    FormPreviewDocuments documents,
    PreviewTickets tickets,
    IFormBuilderFormService forms,
    IFormBuilderFormVersionService versions,
    ILanguageService languages,
    IFormBuilderAccessService access,
    TestEntryActions testEntries) : FormBuilderManagementApiController
{
    /// <summary>The saved draft as visitors would get it, in a language of the site, light or dark, empty or with the sample answers.</summary>
    /// <param name="key">The form.</param>
    /// <param name="culture">A language of the site, such as <c>de-DE</c>; the site's default language when it is none of them.</param>
    /// <param name="dark">Whether the page is dark, which a theme with the dark scheme shows.</param>
    /// <param name="sample">Whether the pages start with the sample answers (ADR-0204).</param>
    /// <param name="actions">Whether the test entries the document sends run their actions, which needs <c>manageActions</c> (ADR-0204).</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormPreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Render(Guid key, [FromQuery] string? culture, [FromQuery] bool dark = false, [FromQuery] bool sample = false, [FromQuery] bool actions = false, CancellationToken cancellationToken = default)
    {
        if (await forms.GetDraftAsync(key, cancellationToken) is not { } draft)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        if (actions && !await ManagesActionsAsync(draft.Form, cancellationToken))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
        }

        var shown = (culture is { Length: > 0 } asked ? await languages.GetAsync(asked) : null)?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();
        var result = await documents.RenderAsync(ControllerContext, key, draft.Form.Name, draft.DraftVersion, new FormPreviewChoices(shown, dark, sample, actions), cancellationToken);
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

    /// <summary>
    /// A new ticket for a version of the form, which an open preview hands its frame (ADR-0203): when its time runs short,
    /// or when Run actions for test entries is switched (ADR-0204).
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The version the preview shows, and whether its test entries run their actions.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("ticket")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormPreviewTicketResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ticket(Guid key, FormPreviewTicketRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetAsync(key, cancellationToken) is not { TrashedUtc: null } form)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        if (request.Version < 1 || await versions.GetVersionAsync(key, request.Version, cancellationToken) is null)
        {
            return FormBuilderProblems.VersionNotFound();
        }

        if (request.RunActions && !await ManagesActionsAsync(form, cancellationToken))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
        }

        var ticket = tickets.Issue(key, request.Version, request.RunActions);
        return Ok(new FormPreviewTicketResponseModel { Ticket = ticket.Value, LifetimeSeconds = (int)PreviewTickets.Lifetime.TotalSeconds });
    }

    /// <summary>
    /// A test entry the preview sent (ADR-0204), found by the submission id its frame sent it with: its number, whether its
    /// actions ran, and the actions Run actions would run for it, in the order shown.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="submissionId">The submission id.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("entries/{submissionId:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(TestEntryResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestEntry(Guid key, Guid submissionId, CancellationToken cancellationToken = default) =>
        await testEntries.FindAsync(key, submissionId, cancellationToken) is { } entry
            ? Ok(new TestEntryResponseModel { Number = entry.Number, ActionsRan = entry.ActionsRan, AwaitsConfirmation = entry.AwaitsConfirmation, Actions = entry.Actions })
            : FormBuilderProblems.TestEntryNotFound();

    /// <summary>
    /// Runs a test entry's actions, once, as its submission would have queued them (ADR-0204): their emails and messages
    /// really go out.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="submissionId">The submission id the frame sent the test entry with.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("entries/{submissionId:guid}/actions")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageActions, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RunActions(Guid key, Guid submissionId, CancellationToken cancellationToken = default) =>
        await testEntries.RunAsync(key, submissionId, cancellationToken) switch
        {
            TestEntryRun.Queued => NoContent(),
            TestEntryRun.AlreadyRan => FormBuilderProblems.ActionsRan(),
            _ => FormBuilderProblems.TestEntryNotFound(),
        };

    // Whether the user may manage the form's actions, as a ticket whose entries run them asks (ADR-0204).
    private async Task<bool> ManagesActionsAsync(FormInfo form, CancellationToken cancellationToken) =>
        (await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(form), cancellationToken) & FormBuilderPermissions.ManageActions) != FormBuilderPermissions.None;
}

/// <summary>A test entry the preview sent (ADR-0204).</summary>
internal sealed record TestEntryResponseModel
{
    /// <summary>Its number in the form.</summary>
    public required int Number { get; init; }

    /// <summary>Whether its actions ran, as its ticket said, or wait for its address to be confirmed: Run actions has nothing to run.</summary>
    public required bool ActionsRan { get; init; }

    /// <summary>Whether its actions wait for its address to be confirmed, as a visitor's entry's do; they then count as ran.</summary>
    public required bool AwaitsConfirmation { get; init; }

    /// <summary>The keys of the actions Run actions would run for it, in the order shown; none once they ran.</summary>
    public required IReadOnlyList<Guid> Actions { get; init; }
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

    /// <summary>Whether the test entries sent with it run their actions, which needs <c>manageActions</c> (ADR-0204).</summary>
    public bool RunActions { get; init; }
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
