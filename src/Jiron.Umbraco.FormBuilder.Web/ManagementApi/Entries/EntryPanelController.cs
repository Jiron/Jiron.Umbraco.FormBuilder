using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// The entry panel (ADR-0208): one entry of a form, opened and so marked seen, its sensitive answers revealed one at a
/// time, its timeline, its notes, an edit of its answers and a forward. Each needs <c>viewEntries</c> on the form; a
/// reveal needs <c>viewSensitive</c>, and an edit and a forward <c>editEntries</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/entry/{entryKey:guid}")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class EntryPanelController(
    EntryPanels panels,
    EntryTimelines timelines,
    EntryNotes notes,
    EntryEdits edits,
    EntryForwards forwards,
    IFormBuilderAccessService access,
    IUserService users) : FormBuilderManagementApiController
{
    /// <summary>Opens an entry, marking it seen the first time anyone opens it.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryPanelResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Open(Guid key, Guid entryKey, CancellationToken cancellationToken = default)
    {
        var held = await HeldAsync(cancellationToken);
        return await panels.OpenAsync(key, entryKey, RequestAccess.User.Key, held.HasFlag(FormBuilderPermissions.EditEntries), cancellationToken) is { } panel
            ? Ok(EntryPanelMapping.Panel(panel, PermissionRules.NamesOf(held)))
            : FormBuilderProblems.EntryNotFound();
    }

    /// <summary>Reveals one answer, and records that in the audit log when it is sensitive.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="request">The field.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("reveal")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewSensitive, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryPanelAnswerResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reveal(Guid key, Guid entryKey, EntryRevealRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mayEdit = (await HeldAsync(cancellationToken)).HasFlag(FormBuilderPermissions.EditEntries);
        return await panels.RevealAsync(key, entryKey, request.Field, RequestAccess.User.Key, mayEdit, cancellationToken) is { } answer
            ? Ok(EntryPanelMapping.Answer(answer))
            : FormBuilderProblems.EntryNotFound();
    }

    /// <summary>The entry's events and mail, newest first; its action runs are listed on their own.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("timeline")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryTimelineResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Timeline(Guid key, Guid entryKey, CancellationToken cancellationToken = default)
    {
        var maySeeSensitive = (await HeldAsync(cancellationToken)).HasFlag(FormBuilderPermissions.ViewSensitive);
        if (await timelines.ReadAsync(key, entryKey, maySeeSensitive, cancellationToken) is not { } timeline)
        {
            return FormBuilderProblems.EntryNotFound();
        }

        var names = await UserNames.OfAsync(users, timeline.Events.Select(entryEvent => entryEvent.User));
        return Ok(new EntryTimelineResponseModel
        {
            Events = [.. timeline.Events.Select(entryEvent => new EntryTimelineEventResponseModel
            {
                Id = entryEvent.Id,
                When = entryEvent.When,
                Kind = entryEvent.Kind,
                User = entryEvent.User is { } user ? names.GetValueOrDefault(user) : null,
                From = entryEvent.From,
                To = entryEvent.To,
                Count = entryEvent.Count,
                Recipient = entryEvent.Recipient,
                Changes = [.. entryEvent.Changes.Select(change => new EntryTimelineChangeResponseModel { Label = change.Label, Before = change.Before, After = change.After, Masked = change.Masked })],
            })],
            Mail = [.. timeline.Mail.Select(mail => new EntryTimelineMailResponseModel
            {
                Id = mail.Id,
                When = mail.When,
                Kind = StoredNames.Of(mail.Kind),
                To = mail.To,
                Subject = mail.Subject,
                State = StoredNames.Of(mail.State),
                ActionRun = mail.ActionRun,
            })],
        });
    }

    /// <summary>The entry's notes, newest first.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("notes")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(IReadOnlyList<EntryNoteResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Notes(Guid key, Guid entryKey, CancellationToken cancellationToken = default)
    {
        if (await notes.ListAsync(key, entryKey, cancellationToken) is not { } found)
        {
            return FormBuilderProblems.EntryNotFound();
        }

        var names = await UserNames.OfAsync(users, found.Select(note => (Guid?)note.Author));
        return Ok(found.Select(note => NoteOf(note, names)).ToList());
    }

    /// <summary>Adds a note.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="request">What it says.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("notes")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryNoteResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddNote(Guid key, Guid entryKey, EntryNoteRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsNote(request.Body))
        {
            return FormBuilderProblems.NoteInvalid();
        }

        if (await notes.AddAsync(key, entryKey, RequestAccess.User.Key, request.Body, cancellationToken) is not { } note)
        {
            return FormBuilderProblems.EntryNotFound();
        }

        return Ok(NoteOf(note, await UserNames.OfAsync(users, [note.Author])));
    }

    /// <summary>Changes a note its author wrote.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="noteId">The note.</param>
    /// <param name="request">What it says now.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("notes/{noteId:int}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryNoteResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeNote(Guid key, Guid entryKey, int noteId, EntryNoteRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsNote(request.Body))
        {
            return FormBuilderProblems.NoteInvalid();
        }

        var (change, note) = await notes.ChangeAsync(key, entryKey, noteId, RequestAccess.User.Key, request.Body, cancellationToken);
        return change switch
        {
            EntryNoteChange.Changed => Ok(NoteOf(note!, await UserNames.OfAsync(users, [note!.Author]))),
            EntryNoteChange.NotYours => FormBuilderProblems.NoteNotYours(),
            _ => FormBuilderProblems.NoteNotFound(),
        };
    }

    /// <summary>Edits the entry's answers, or answers what the edit would overbook or double first.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="request">The new answers.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("answers")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.EditEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryEditResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Edit(Guid key, Guid entryKey, EntryEditRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Answers.Count is 0 or > EntryEdits.MaxAnswers || request.Answers.Select(answer => answer.Field).Distinct().Count() != request.Answers.Count)
        {
            throw new FormBuilderValidationException([new DefinitionProblem("invalid_answers", "answers", $"Give 1 to {EntryEdits.MaxAnswers} answers, each field once.")]);
        }

        var maySeeSensitive = (await HeldAsync(cancellationToken)).HasFlag(FormBuilderPermissions.ViewSensitive);
        var result = await edits.EditAsync(key, entryKey, RequestAccess.User.Key, [.. request.Answers.Select(answer => new EntryEditValue(answer.Field, answer.Values ?? []))], maySeeSensitive, request.Overbook, cancellationToken);
        return result.Status switch
        {
            EntryEditStatus.NotFound => FormBuilderProblems.EntryNotFound(),
            EntryEditStatus.Invalid => throw new FormBuilderValidationException([.. result.Problems]),
            _ => Ok(new EntryEditResponseModel { Changed = result.Changed, Asks = result.Asks is { } asks ? EntryPanelMapping.Asks(asks) : null }),
        };
    }

    /// <summary>Whom the entry may be forwarded to.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("forward")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.EditEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryForwardRecipientsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recipients(Guid key, Guid entryKey, CancellationToken cancellationToken = default) =>
        await forwards.RecipientsAsync(key, entryKey, RequestAccess.User.Key, cancellationToken) is { } recipients
            ? Ok(new EntryForwardRecipientsResponseModel
            {
                Users = [.. recipients.Users.Select(user => new EntryForwardUserResponseModel { Key = user.Key, Name = user.Name })],
                Addresses = recipients.Addresses,
            })
            : FormBuilderProblems.EntryNotFound();

    /// <summary>Forwards the entry to a user who may view the form's entries, or to a forward address of its site.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="request">The recipient, the message and whether sensitive values go along.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("forward")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.EditEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Forward(Guid key, Guid entryKey, EntryForwardRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.User is null) == string.IsNullOrWhiteSpace(request.Address) || request.Message?.Length > EntryForwards.MaxMessageLength)
        {
            return FormBuilderProblems.ForwardInvalid();
        }

        var includeSensitive = request.IncludeSensitive && (await HeldAsync(cancellationToken)).HasFlag(FormBuilderPermissions.ViewSensitive);
        return await forwards.ForwardAsync(key, entryKey, RequestAccess.User.Key, request.User, request.Address, request.Message, includeSensitive, cancellationToken) switch
        {
            EntryForwardStatus.Queued => NoContent(),
            EntryForwardStatus.NotAllowed => FormBuilderProblems.ForwardRecipientNotAllowed(),
            _ => FormBuilderProblems.EntryNotFound(),
        };
    }

    private static bool IsNote(string? body) => !string.IsNullOrWhiteSpace(body) && body.Trim().Length <= EntryNotes.MaxLength;

    private EntryNoteResponseModel NoteOf(EntryNote note, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = note.Id,
        Author = names.GetValueOrDefault(note.Author) ?? string.Empty,
        Mine = note.Author == RequestAccess.User.Key,
        Body = note.Body,
        Created = note.Created,
        Edited = note.Edited,
    };

    // What the user may do on the route's form.
    private Task<FormBuilderPermissions> HeldAsync(CancellationToken cancellationToken) =>
        access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(RequestAccess.Form!), cancellationToken);
}

/// <summary>
/// The form an entry belongs to, for the entry's own link (ADR-0208): named only to a user who may view that form's
/// entries; anyone else gets the answer an entry that is gone gets, none, as every endpoint of the whole section answers
/// a user of the section.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/entry/{entryKey:guid}")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class EntryLocationController(IEntryRepository entries, IFormRepository storedForms, IFormBuilderFormService forms, IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    /// <summary>The entry's form.</summary>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("form")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryLocationResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Form(Guid entryKey, CancellationToken cancellationToken = default)
    {
        if (await entries.GetAsync(entryKey, cancellationToken) is not { State: not EntryState.Draft } entry
            || await storedForms.GetByIdAsync(entry.FormId, cancellationToken) is not { } stored
            || await forms.GetAsync(stored.Key, cancellationToken) is not { } form
            || !(await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(form), cancellationToken)).HasFlag(FormBuilderPermissions.ViewEntries))
        {
            return Ok(new EntryLocationResponseModel { Form = null });
        }

        return Ok(new EntryLocationResponseModel { Form = form.Key });
    }
}
