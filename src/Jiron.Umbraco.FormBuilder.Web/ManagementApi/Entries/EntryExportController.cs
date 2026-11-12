using System.Net.Mime;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// A form's exports (ADR-0210): what the dialog opens with, the plan, the file of an export that fits a download, and the
/// background exports of larger ones with their files, which only their owner sees, until they expire. Everything needs
/// <c>export</c> on the form, and sensitive fields also <c>viewSensitive</c>; a background export's file downloads through
/// a link with a one-minute ticket that its owner is issued after those checks.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/entries/export")]
[ApiExplorerSettings(GroupName = "Entry export")]
internal sealed class EntryExportController(
    EntryExports exports,
    EntryExportJobs jobs,
    FormBuilderExportFormatCollection formats,
    EntryUserSettings settings,
    IFormBuilderAccessService access,
    IUserService users,
    ExportDownloadTickets tickets,
    ExportDownloadHolders holders) : FormBuilderManagementApiController
{
    /// <summary>What the export dialog opens with: the formats, the user's last choices, and their background exports.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryExportViewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> View(Guid key, CancellationToken cancellationToken = default)
    {
        var user = RequestAccess.User.Key;
        var choices = await settings.GetExportChoicesAsync(user, key, cancellationToken);
        return Ok(new EntryExportViewResponseModel
        {
            Formats = [.. formats.Offered().Select(format => EntryExportMapping.Model(format, RequestAccess.Form!.Alias))],
            Choices = choices is null ? null : EntryExportMapping.Model(choices),
            MaySeeSensitive = await MaySeeSensitiveAsync(cancellationToken),
            Exports = [.. (await jobs.MineAsync(key, user, cancellationToken)).Select(EntryExportMapping.Model)],
        });
    }

    /// <summary>Checks an export and says how many entries it holds, and whether it runs in the background.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("plan")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryExportPlanResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Plan(Guid key, EntryExportRequestModel request, CancellationToken cancellationToken = default)
    {
        var (asked, exporter) = await PrepareAsync(request, cancellationToken);
        return await exports.PlanAsync(key, asked, exporter, cancellationToken) is { } plan
            ? Ok(new EntryExportPlanResponseModel { Entries = plan.Entries, More = plan.More, Background = plan.Background, Unconfirmed = plan.Unconfirmed })
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
    }

    /// <summary>
    /// Downloads an export of at most 10,000 entries, streamed as it is written; a larger one is refused with
    /// <c>export_in_background</c>, as it runs in the background.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("file")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Octet)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> WriteFile(Guid key, EntryExportRequestModel request, CancellationToken cancellationToken = default)
    {
        var (asked, exporter) = await PrepareAsync(request, cancellationToken);
        if (await exports.PlanAsync(key, asked, exporter, cancellationToken) is not { } plan)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        if (plan.Background)
        {
            return FormBuilderProblems.ExportInBackground();
        }

        await RememberAsync(key, request, asked, cancellationToken);
        var format = formats.Find(asked.Format)!;
        HoldToAttachment();
        Response.Headers.ContentDisposition = new ContentDisposition { FileName = EntryExports.FileName(RequestAccess.Form!.Alias, format), Inline = false }.ToString();
        return new StreamedExport(format.ContentType, (body, aborted) => exports.WriteAsync(key, asked, exporter, body, cancellationToken: aborted));
    }

    /// <summary>Starts an export in the background, for one too large to download at once; its status follows.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("jobs")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryExportStatusResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(Guid key, EntryExportRequestModel request, CancellationToken cancellationToken = default)
    {
        var (asked, exporter) = await PrepareAsync(request, cancellationToken);
        if (await exports.PlanAsync(key, asked, exporter, cancellationToken) is not { } plan)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var record = await jobs.QueueAsync(key, asked, exporter, plan, cancellationToken);
        await RememberAsync(key, request, asked, cancellationToken);
        return Ok(EntryExportMapping.Model(record));
    }

    /// <summary>A background export of the user's: how far it is, and its file's name once it is ready.</summary>
    /// <param name="key">The form.</param>
    /// <param name="exportKey">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("jobs/{exportKey:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryExportStatusResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(Guid key, Guid exportKey, CancellationToken cancellationToken = default) =>
        await jobs.GetAsync(key, exportKey, RequestAccess.User.Key, cancellationToken) is { } record
            ? Ok(EntryExportMapping.Model(record))
            : FormBuilderProblems.ExportNotFound();

    /// <summary>
    /// A link that downloads the file of a background export of the user's, for one minute: the browser saves the file as
    /// it comes, whatever its size, as a plain link's request carries no backoffice token.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="exportKey">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("jobs/{exportKey:guid}/ticket")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryExportTicketResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ticket(Guid key, Guid exportKey, CancellationToken cancellationToken = default)
    {
        var user = RequestAccess.User.Key;
        if (await jobs.GetAsync(key, exportKey, user, cancellationToken) is not { State: EntryExportState.Ready } record)
        {
            return FormBuilderProblems.ExportNotFound();
        }

        // Sensitive fields went along: the user still needs to be allowed to see them.
        if (record.Request.Sensitive && !await MaySeeSensitiveAsync(cancellationToken))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ViewSensitive);
        }

        // The file's address is this one's, its last part "file" for "ticket", wherever the site serves the API.
        var ticket = tickets.Issue(user, key, exportKey);
        var path = Request.Path.Value ?? string.Empty;
        return Ok(new EntryExportTicketResponseModel
        {
            Url = $"{Request.PathBase}{path[..(path.LastIndexOf('/') + 1)]}file?ticket={ticket.Value}",
            ExpiresUtc = ticket.ExpiresUtc,
        });
    }

    /// <summary>
    /// Downloads the file of a background export with a ticket of <c>ticket</c>'s, until the ticket or the export expires;
    /// the ticket stands for the signed-in user, so the request needs none.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="exportKey">The export.</param>
    /// <param name="ticket">The ticket.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("jobs/{exportKey:guid}/file")]
    [MapToApiVersion("1.0")]
    [AllowAnonymous]
    [FormBuilderTicketAccess]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Octet)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid key, Guid exportKey, [FromQuery] string? ticket, CancellationToken cancellationToken = default)
    {
        // A ticket that does not hold, holds for another export, or whose user may no longer download it finds nothing, as an
        // export of someone else's does.
        if (tickets.Read(ticket, key) is not { } read || read.ExportKey != exportKey
            || !await holders.MayDownloadFormExportAsync(read, cancellationToken)
            || await jobs.OpenAsync(key, exportKey, read.UserKey, cancellationToken) is not { } download)
        {
            return FormBuilderProblems.ExportNotFound();
        }

        HoldToAttachment();
        return File(download.Content, download.ContentType, download.FileName);
    }

    // The request as the service takes it, with who asks; problems with the request's shape are refused at once.
    private async Task<(EntryExportRequest Request, EntryExporter Exporter)> PrepareAsync(EntryExportRequestModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<DefinitionProblem>();
        var asked = EntryExportMapping.Request(request, problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var user = await users.GetAsync(RequestAccess.User.Key);
        return (asked, new EntryExporter(RequestAccess.User.Key, await MaySeeSensitiveAsync(cancellationToken), user?.Language ?? "en-US"));
    }

    // What the dialog opens with next time, once the export was checked, when the dialog asked for it (ADR-0210).
    private Task RememberAsync(Guid key, EntryExportRequestModel request, EntryExportRequest asked, CancellationToken cancellationToken) =>
        request.Remember
            ? settings.SetExportChoicesAsync(RequestAccess.User.Key, key, new EntryExportChoices(asked.Format, asked.Fields, asked.Sensitive, asked.Options), cancellationToken)
            : Task.CompletedTask;

    private async Task<bool> MaySeeSensitiveAsync(CancellationToken cancellationToken)
    {
        var held = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(RequestAccess.Form!), cancellationToken);
        return (held & FormBuilderPermissions.ViewSensitive) != FormBuilderPermissions.None;
    }

    // Whatever the bytes hold, a browser saves them and neither guesses at them nor runs them; a printable view opens
    // only from a file the user saved. The management API's own filter keeps them from any cache.
    private void HoldToAttachment()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";

        // The link carries its ticket: nothing it leads to names it onward.
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    // An export written straight to the response, once the filters of the result have run, as nothing may touch the
    // headers after its first byte.
    private sealed class StreamedExport(string contentType, Func<Stream, CancellationToken, Task> write) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = contentType;
            return write(response.Body, context.HttpContext.RequestAborted);
        }
    }
}
