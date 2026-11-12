using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Privacy;

/// <summary>
/// Privacy > People (IA 9, ADR-0223): a person's entries in every form, found by an email address or a member; the export
/// of everything about them; legal holds on their entries; and the erase, previewed first. Every request names the person
/// again and the server finds the entries again, so no request reaches another's. Each needs <c>privacyTools</c>, but the
/// download of an export, which its ticket stands for.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/people")]
[ApiExplorerSettings(GroupName = "Privacy")]
internal sealed class PeopleController(
    PersonData people,
    EntryExportJobs jobs,
    ExportDownloadTickets tickets,
    ExportDownloadHolders holders,
    IUserService users,
    TimeProvider time) : FormBuilderManagementApiController
{
    /// <summary>The person's entries in every form, newest first, at most 500; the search is audited, never whom it was for.</summary>
    /// <param name="request">The person.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("find")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(PersonFoundResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Find(PersonRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await people.FindAsync(PersonQuery.Of(request.Email, request.Member), RequestAccess.User.Key, cancellationToken);
        var now = time.GetUtcNow();
        return Ok(new PersonFoundResponseModel
        {
            More = found.More,
            Forms = found.Entries.Select(entry => entry.Form.Id).Distinct().Count(),
            Entries =
            [
                .. found.Entries.Select(entry => new PersonEntryResponseModel
                {
                    Key = entry.Key,
                    Number = entry.Number,
                    Form = new PersonFormResponseModel { Key = entry.Form.Key, Name = entry.Form.Name, Trashed = entry.Form.TrashedUtc is not null },
                    State = StoredNames.Of(entry.State),
                    Test = entry.IsTest,
                    Received = entry.Received,
                    Files = entry.Files,
                    Emails = entry.Emails,
                    HeldUntil = HeldNow(entry.HeldUntil, now),
                }),
            ],
        });
    }

    /// <summary>What an erase of the person would take: per form, with their files and mail, and those a hold keeps.</summary>
    /// <param name="request">The person.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("erase/preview")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(PersonErasePreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(PersonRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var preview = await people.PreviewAsync(PersonQuery.Of(request.Email, request.Member), cancellationToken);
        return Ok(new PersonErasePreviewResponseModel
        {
            Entries = preview.Entries,
            Drafts = preview.Drafts,
            Held = preview.Held,
            Files = preview.Files,
            Emails = preview.Emails,
            More = preview.More,
            Forms =
            [
                .. preview.Forms.Select(form => new PersonEraseFormResponseModel
                {
                    Key = form.Form.Key,
                    Name = form.Form.Name,
                    Trashed = form.Form.TrashedUtc is not null,
                    Entries = form.Entries,
                    Drafts = form.Drafts,
                    Held = form.Held,
                    Files = form.Files,
                    Emails = form.Emails,
                }),
            ],
        });
    }

    /// <summary>Erases the person: their entries deleted or anonymised, their drafts and every other mail to them deleted.</summary>
    /// <param name="request">The person and how their entries go: <c>delete</c> or <c>anonymise</c>.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("erase")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(PersonErasedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Erase(PersonEraseRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = request.Mode switch
        {
            "delete" => PersonEraseMode.Delete,
            "anonymise" => PersonEraseMode.Anonymise,
            _ => throw FormBuilderValidationException.For("mode_invalid", "mode", "Choose delete or anonymise."),
        };
        var erased = await people.EraseAsync(PersonQuery.Of(request.Email, request.Member), mode, RequestAccess.User.Key, cancellationToken);
        return Ok(new PersonErasedResponseModel
        {
            Mode = request.Mode,
            Entries = erased.Entries,
            Drafts = erased.Drafts,
            Files = erased.Files,
            Emails = erased.Emails,
            Held = erased.Held,
            AuditId = erased.AuditId,
        });
    }

    /// <summary>
    /// Holds the person's entries among those named until the end given, at most a year ahead, for the reason given; an end
    /// or reason out of bounds answers <c>400</c>.
    /// </summary>
    /// <param name="request">The person, the entries, the end and the reason.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("hold")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(PersonHeldResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Hold(PersonHoldRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (refusal, held) = await people.HoldAsync(PersonQuery.Of(request.Email, request.Member), request.Entries, request.Until, request.Reason ?? string.Empty, RequestAccess.User.Key, cancellationToken);
        return refusal switch
        {
            EntryHoldRefusal.End => throw FormBuilderValidationException.For("hold_end", "until", $"Choose an end after now and at most {EntryHolds.MaxDays} days ahead."),
            EntryHoldRefusal.Reason => throw FormBuilderValidationException.For("hold_reason", "reason", "Give the reason on one line, in at most 500 characters."),
            _ => Ok(new PersonHeldResponseModel { Entries = held }),
        };
    }

    /// <summary>Ends the legal holds of the person's entries among those named, now.</summary>
    /// <param name="request">The person and the entries.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("release")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(PersonHeldResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Release(PersonEntriesRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(new PersonHeldResponseModel { Entries = await people.ReleaseAsync(PersonQuery.Of(request.Email, request.Member), request.Entries, RequestAccess.User.Key, cancellationToken) });
    }

    /// <summary>Starts the export of everything about the person in the background; its status follows.</summary>
    /// <param name="request">The person.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("export")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(EntryExportStatusResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(PersonRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.GetAsync(RequestAccess.User.Key);
        var record = await people.ExportAsync(PersonQuery.Of(request.Email, request.Member), new EntryExporter(RequestAccess.User.Key, MaySeeSensitive: true, user?.Language ?? "en-US"), cancellationToken);
        return Ok(EntryExportMapping.Model(record));
    }

    /// <summary>An export of a person's data the user started: how far it is, and its file's name once it is ready.</summary>
    /// <param name="exportKey">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("export/{exportKey:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(EntryExportStatusResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(Guid exportKey, CancellationToken cancellationToken = default) =>
        await jobs.GetAsync(PersonExports.Scope, exportKey, RequestAccess.User.Key, cancellationToken) is { } record
            ? Ok(EntryExportMapping.Model(record))
            : FormBuilderProblems.ExportNotFound();

    /// <summary>A link that downloads a ready export of a person's data for one minute, as a plain link carries no token.</summary>
    /// <param name="exportKey">The export.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("export/{exportKey:guid}/ticket")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.PrivacyTools)]
    [ProducesResponseType(typeof(EntryExportTicketResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ticket(Guid exportKey, CancellationToken cancellationToken = default)
    {
        var user = RequestAccess.User.Key;
        if (await jobs.GetAsync(PersonExports.Scope, exportKey, user, cancellationToken) is not { State: EntryExportState.Ready })
        {
            return FormBuilderProblems.ExportNotFound();
        }

        var ticket = tickets.Issue(user, PersonExports.Scope, exportKey);
        var path = Request.Path.Value ?? string.Empty;
        return Ok(new EntryExportTicketResponseModel
        {
            Url = $"{Request.PathBase}{path[..(path.LastIndexOf('/') + 1)]}file?ticket={ticket.Value}",
            ExpiresUtc = ticket.ExpiresUtc,
        });
    }

    /// <summary>Downloads a ready export of a person's data with a ticket, until the ticket or the export expires.</summary>
    /// <param name="exportKey">The export.</param>
    /// <param name="ticket">The ticket.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("export/{exportKey:guid}/file")]
    [MapToApiVersion("1.0")]
    [AllowAnonymous]
    [FormBuilderTicketAccess]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Octet)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid exportKey, [FromQuery] string? ticket, CancellationToken cancellationToken = default)
    {
        if (tickets.Read(ticket, PersonExports.Scope) is not { } read || read.ExportKey != exportKey
            || !await holders.MayDownloadPersonExportAsync(read, cancellationToken)
            || await jobs.OpenAsync(PersonExports.Scope, exportKey, read.UserKey, cancellationToken) is not { } download)
        {
            return FormBuilderProblems.ExportNotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(download.Content, download.ContentType, download.FileName);
    }

    // The end of a hold that keeps its entry now, or none.
    private static DateTimeOffset? HeldNow(DateTimeOffset? until, DateTimeOffset now) => until is { } end && end > now ? end : null;
}

/// <summary>A person: an email address or a member, never both (ADR-0223).</summary>
public sealed class PersonRequestModel
{
    /// <summary>An email address; or none, for a member.</summary>
    [MaxLength(254)]
    public string? Email { get; init; }

    /// <summary>A member's key; or none, for an email address.</summary>
    public Guid? Member { get; init; }
}

/// <summary>A person and some of their entries.</summary>
public sealed class PersonEntriesRequestModel
{
    /// <summary>An email address; or none, for a member.</summary>
    [MaxLength(254)]
    public string? Email { get; init; }

    /// <summary>A member's key; or none, for an email address.</summary>
    public Guid? Member { get; init; }

    /// <summary>The entries, at most 500.</summary>
    [Required]
    [MaxLength(PersonData.MaxFound)]
    public required IReadOnlyList<Guid> Entries { get; init; }
}

/// <summary>A legal hold on some of a person's entries.</summary>
public sealed class PersonHoldRequestModel
{
    /// <summary>An email address; or none, for a member.</summary>
    [MaxLength(254)]
    public string? Email { get; init; }

    /// <summary>A member's key; or none, for an email address.</summary>
    public Guid? Member { get; init; }

    /// <summary>The entries, at most 500.</summary>
    [Required]
    [MaxLength(PersonData.MaxFound)]
    public required IReadOnlyList<Guid> Entries { get; init; }

    /// <summary>When the hold ends: after now, and at most a year ahead.</summary>
    public required DateTimeOffset Until { get; init; }

    /// <summary>Why, on one line.</summary>
    [MaxLength(500)]
    public string? Reason { get; init; }
}

/// <summary>An erase of a person.</summary>
public sealed class PersonEraseRequestModel
{
    /// <summary>An email address; or none, for a member.</summary>
    [MaxLength(254)]
    public string? Email { get; init; }

    /// <summary>A member's key; or none, for an email address.</summary>
    public Guid? Member { get; init; }

    /// <summary>How their entries go: <c>delete</c> or <c>anonymise</c>.</summary>
    [Required]
    public required string Mode { get; init; }
}

/// <summary>A person's entries in every form.</summary>
public sealed class PersonFoundResponseModel
{
    /// <summary>The entries, newest first, at most 500.</summary>
    public required IReadOnlyList<PersonEntryResponseModel> Entries { get; init; }

    /// <summary>Whether the person has more entries than listed.</summary>
    public required bool More { get; init; }

    /// <summary>How many forms the entries are in.</summary>
    public required int Forms { get; init; }
}

/// <summary>One of a person's entries.</summary>
public sealed class PersonEntryResponseModel
{
    /// <summary>The entry.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its number in its form.</summary>
    public required int Number { get; init; }

    /// <summary>Its form.</summary>
    public required PersonFormResponseModel Form { get; init; }

    /// <summary>Its state; <c>draft</c> for a form saved to finish later.</summary>
    public required string State { get; init; }

    /// <summary>Whether it is a test entry.</summary>
    public required bool Test { get; init; }

    /// <summary>When it came.</summary>
    public required DateTimeOffset Received { get; init; }

    /// <summary>Its files.</summary>
    public required int Files { get; init; }

    /// <summary>Its mail.</summary>
    public required int Emails { get; init; }

    /// <summary>The end of its legal hold, while one keeps it.</summary>
    public DateTimeOffset? HeldUntil { get; init; }
}

/// <summary>A form a person's entries are in.</summary>
public sealed class PersonFormResponseModel
{
    /// <summary>The form.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is in the trash.</summary>
    public required bool Trashed { get; init; }
}

/// <summary>What an erase of a person would take.</summary>
public sealed class PersonErasePreviewResponseModel
{
    /// <summary>The entries that would be deleted or anonymised.</summary>
    public required int Entries { get; init; }

    /// <summary>The drafts that would be deleted.</summary>
    public required int Drafts { get; init; }

    /// <summary>The entries a legal hold keeps, which the erase leaves.</summary>
    public required int Held { get; init; }

    /// <summary>The files that would be deleted.</summary>
    public required int Files { get; init; }

    /// <summary>The mail that would be deleted.</summary>
    public required int Emails { get; init; }

    /// <summary>Whether the person has more entries than one erase takes.</summary>
    public required bool More { get; init; }

    /// <summary>Per form, most first.</summary>
    public required IReadOnlyList<PersonEraseFormResponseModel> Forms { get; init; }
}

/// <summary>What an erase of a person would take of one form.</summary>
public sealed class PersonEraseFormResponseModel
{
    /// <summary>The form.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is in the trash.</summary>
    public required bool Trashed { get; init; }

    /// <summary>Its entries that would be deleted or anonymised.</summary>
    public required int Entries { get; init; }

    /// <summary>Its drafts that would be deleted.</summary>
    public required int Drafts { get; init; }

    /// <summary>Its entries a legal hold keeps.</summary>
    public required int Held { get; init; }

    /// <summary>The files of its entries that would be deleted.</summary>
    public required int Files { get; init; }

    /// <summary>The mail of its entries that would be deleted.</summary>
    public required int Emails { get; init; }
}

/// <summary>What an erase of a person took.</summary>
public sealed class PersonErasedResponseModel
{
    /// <summary>How the entries went: <c>delete</c> or <c>anonymise</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>The entries deleted or anonymised.</summary>
    public required int Entries { get; init; }

    /// <summary>The drafts deleted.</summary>
    public required int Drafts { get; init; }

    /// <summary>The files deleted.</summary>
    public required int Files { get; init; }

    /// <summary>The mail deleted.</summary>
    public required int Emails { get; init; }

    /// <summary>The entries a legal hold kept.</summary>
    public required int Held { get; init; }

    /// <summary>The audit entry that records the erase.</summary>
    public required long AuditId { get; init; }
}

/// <summary>How many entries a hold or a release changed.</summary>
public sealed class PersonHeldResponseModel
{
    /// <summary>The entries.</summary>
    public required int Entries { get; init; }
}
