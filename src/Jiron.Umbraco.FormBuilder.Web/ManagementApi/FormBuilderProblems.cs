using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi;

/// <summary>
/// The management API's refusals as ProblemDetails (ADR-0050), each with its stable <c>code</c>, a title written for
/// editors as a complete sentence (the backoffice shows it as it is) and a detail that names no internals.
/// </summary>
internal static class FormBuilderProblems
{
    /// <summary>The user lacks the Forms section.</summary>
    public const string NoSectionCode = "no_section";

    /// <summary>Jiron Form Builder's migration plan has not run on this database yet.</summary>
    public const string MigrationsPendingCode = "migrations_pending";

    /// <summary>The user sees the item but lacks a permission it needs.</summary>
    public const string ForbiddenCode = "forbidden";

    /// <summary>The request's route, query or body holds values the endpoint cannot read.</summary>
    public const string InvalidRequestCode = "invalid_request";

    /// <summary>The type of every problem: none beyond its status and its code (RFC 9457).</summary>
    public const string AboutBlank = "about:blank";

    /// <summary>A move would give a group more access, and was not confirmed.</summary>
    public const string WidensAccessCode = "widens_access";

    /// <summary>A form's version that does not exist.</summary>
    public const string VersionNotFoundCode = "version_not_found";

    /// <summary>A file the form's entries do not hold (ADR-0145).</summary>
    public const string FileNotFoundCode = "file_not_found";

    /// <summary>An action run, or the entry it is asked for, that the form does not hold (ADR-0158).</summary>
    public const string ActionRunNotFoundCode = "action_run_not_found";

    /// <summary>The code of a change of state that names no entries, too many, or no state a user can choose.</summary>
    public const string EntryStateInvalidCode = "entry_state_invalid";

    /// <summary>The code of an Undo of a change the form does not hold.</summary>
    public const string EntryChangeNotFoundCode = "entry_change_not_found";

    /// <summary>The code of an Undo after the window.</summary>
    public const string EntryUndoTooLateCode = "entry_undo_too_late";

    /// <summary>A retry of an uncertain run that was not confirmed, as its first attempt may have been delivered (section 4.4).</summary>
    public const string RetryNeedsConfirmationCode = "retry_needs_confirmation";

    /// <summary>A retry of a run that neither failed nor is uncertain (ADR-0158).</summary>
    public const string RunNotRetryableCode = "run_not_retryable";

    /// <summary>An action the form's draft does not hold (ADR-0158).</summary>
    public const string ActionNotFoundCode = "action_not_found";

    /// <summary>An outcome the definition does not hold (ADR-0187).</summary>
    public const string OutcomeNotFoundCode = "outcome_not_found";

    /// <summary>An action whose type the site lacks (ADR-0158).</summary>
    public const string ActionTypeUnknownCode = "action_type_unknown";

    /// <summary>An action of a type that sends no mail of its own, asked for a mail's preview (ADR-0189).</summary>
    public const string ActionNotEmailCode = "action_not_email";

    /// <summary>A document type the site does not have, or an element type, which no content item has (ADR-0192).</summary>
    public const string DocumentTypeNotFoundCode = "document_type_not_found";

    /// <summary>A mail's preview whose action names a mail view the site lacks and has no body of its own (ADR-0189).</summary>
    public const string EmailViewMissingCode = "email_view_missing";

    /// <summary>A mail's preview whose action has settings its type cannot read, as the form check says (ADR-0189).</summary>
    public const string ActionSettingsInvalidCode = "action_settings_invalid";

    /// <summary>The code of a test mail while Umbraco can send no mail (ADR-0159).</summary>
    public const string EmailNotConfiguredCode = "email_not_configured";

    /// <summary>The code of a test mail for a user without an address (ADR-0159).</summary>
    public const string EmailNoAddressCode = "email_no_address";

    /// <summary>A test run that reaches what the action is set up to reach, and was not confirmed (section 4.4).</summary>
    public const string TestNeedsConfirmationCode = "test_needs_confirmation";

    /// <summary>A file a scanner quarantined (ADR-0144).</summary>
    public const string FileQuarantinedCode = "file_quarantined";

    /// <summary>A file that waits for the site's scanners (ADR-0144).</summary>
    public const string FileNotScannedCode = "file_not_scanned";

    /// <summary>The code of a data type that does not exist or is no form picker's.</summary>
    public const string DataTypeNotFoundCode = "data_type_not_found";

    /// <summary>The code of a template that is neither built in nor saved on this site (ADR-0166).</summary>
    public const string TemplateNotFoundCode = "template_not_found";

    /// <summary>The code of a field type the site does not have (ADR-0059).</summary>
    public const string FieldTypeNotFoundCode = "field_type_not_found";

    /// <summary>The code of a draft whose rules the check both runtimes run refuses, which the preview does not show (ADR-0203).</summary>
    public const string RulesInvalidCode = "rules_invalid";

    /// <summary>How long a client waits before asking again while the plan has not run.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    /// <summary>The code of a folder or form that does not exist, or that the user does not see.</summary>
    public static string NotFoundCode(FormBuilderAccessResourceKind kind) => kind == FormBuilderAccessResourceKind.Form ? "form_not_found" : "folder_not_found";

    /// <summary>The user lacks the Forms section.</summary>
    public static FormBuilderProblemResult NoSection() =>
        Problem(StatusCodes.Status403Forbidden, NoSectionCode, "You do not have access to the Form Builder section.");

    /// <summary>Jiron Form Builder's tables are not ready; the answer says when to ask again.</summary>
    public static FormBuilderProblemResult MigrationsPending(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Headers.RetryAfter = ((int)RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Problem(StatusCodes.Status503ServiceUnavailable, MigrationsPendingCode, "Jiron Form Builder is being set up on this site. Try again in a moment.");
    }

    /// <summary>A folder or form that does not exist, or that the user does not see: the same answer for both.</summary>
    public static FormBuilderProblemResult NotFound(FormBuilderAccessResourceKind kind) =>
        Problem(StatusCodes.Status404NotFound, NotFoundCode(kind), kind == FormBuilderAccessResourceKind.Form ? "The form does not exist." : "The folder does not exist.");

    /// <summary>A version of a form the user sees that does not exist (ADR-0052).</summary>
    public static FormBuilderProblemResult VersionNotFound() =>
        Problem(StatusCodes.Status404NotFound, VersionNotFoundCode, "The version does not exist.");

    /// <summary>A file the form's entries do not hold, or whose bytes are gone (ADR-0145).</summary>
    public static FormBuilderProblemResult FileNotFound() =>
        Problem(StatusCodes.Status404NotFound, FileNotFoundCode, "The form's entries hold no such file.");

    /// <summary>An action run, or the entry it is asked for, that the form does not hold (ADR-0158).</summary>
    public static FormBuilderProblemResult ActionRunNotFound() =>
        Problem(StatusCodes.Status404NotFound, ActionRunNotFoundCode, "The form has no such entry or action run.");

    /// <summary>A change of state that names no entries, too many, or no state a user can choose (ADR-0162).</summary>
    public static FormBuilderProblemResult EntryStateInvalid() =>
        Problem(StatusCodes.Status400BadRequest, EntryStateInvalidCode, "Name 1 to 500 entries and the state submitted, approved, rejected or spam.");

    /// <summary>An Undo of a change the form does not hold (ADR-0162).</summary>
    public static FormBuilderProblemResult EntryChangeNotFound() =>
        Problem(StatusCodes.Status404NotFound, EntryChangeNotFoundCode, "The form has no such change.");

    /// <summary>An Undo after the window (ADR-0023).</summary>
    public static FormBuilderProblemResult EntryUndoTooLate() =>
        Problem(StatusCodes.Status409Conflict, EntryUndoTooLateCode, "It's too late to undo this. Change the state back by hand if needed.");

    /// <summary>A retry of an uncertain run without its confirmation (section 4.4).</summary>
    public static FormBuilderProblemResult ActionRetryNeedsConfirmation() =>
        Problem(StatusCodes.Status409Conflict, RetryNeedsConfirmationCode, "This action may already have been delivered. Confirm that it may go out a second time.");

    /// <summary>A retry of a run that neither failed nor is uncertain (ADR-0158).</summary>
    public static FormBuilderProblemResult ActionRunNotRetryable() =>
        Problem(StatusCodes.Status409Conflict, RunNotRetryableCode, "Only an action that failed or may have been delivered can be retried.");

    /// <summary>An action the form's draft does not hold (ADR-0158).</summary>
    public static FormBuilderProblemResult ActionNotFound() =>
        Problem(StatusCodes.Status404NotFound, ActionNotFoundCode, "The form's draft has no such action.");

    /// <summary>An outcome the definition does not hold (ADR-0187).</summary>
    public static FormBuilderProblemResult OutcomeNotFound() =>
        Problem(StatusCodes.Status404NotFound, OutcomeNotFoundCode, "The definition has no such outcome.");

    /// <summary>An action whose type the site lacks (ADR-0158).</summary>
    public static FormBuilderProblemResult ActionTypeUnknown() =>
        Problem(StatusCodes.Status409Conflict, ActionTypeUnknownCode, "This site does not have the type of this action, so it cannot be tested.");

    /// <summary>A document type the site does not have, or an element type, which no content item has (ADR-0192).</summary>
    public static FormBuilderProblemResult DocumentTypeNotFound() =>
        Problem(StatusCodes.Status404NotFound, DocumentTypeNotFoundCode, "This site has no document type of that alias that a content item may have.");

    /// <summary>An action of a type that sends no mail of its own, asked for a mail's preview (ADR-0189).</summary>
    public static FormBuilderProblemResult ActionNotEmail() =>
        Problem(StatusCodes.Status409Conflict, ActionNotEmailCode, "This action sends no mail, so it has no mail to preview.");

    /// <summary>A mail's preview whose action names a mail view the site lacks and has no body of its own (ADR-0189).</summary>
    public static FormBuilderProblemResult EmailViewMissing() =>
        Problem(StatusCodes.Status409Conflict, EmailViewMissingCode, "The site has no such mail view, and the email has no body of its own.");

    /// <summary>A mail's preview whose action has settings its type cannot read, as the form check says (ADR-0189).</summary>
    public static FormBuilderProblemResult ActionSettingsInvalid() =>
        Problem(StatusCodes.Status409Conflict, ActionSettingsInvalidCode, "The action's settings do not fit its type; the form check says where.");

    /// <summary>A test mail while no mail server or pickup folder is set up (ADR-0159).</summary>
    public static FormBuilderProblemResult EmailNotConfigured() =>
        Problem(StatusCodes.Status409Conflict, EmailNotConfiguredCode, "This site has no mail server or pickup folder set up, so it cannot send mail.");

    /// <summary>A test mail for a user whose account has no address it can go to (ADR-0159).</summary>
    public static FormBuilderProblemResult EmailNoAddress() =>
        Problem(StatusCodes.Status409Conflict, EmailNoAddressCode, "Your account has no address a test mail can go to.");

    /// <summary>A test run that reaches what the action is set up to reach, without its confirmation (section 4.4).</summary>
    public static FormBuilderProblemResult ActionTestNeedsConfirmation() =>
        Problem(StatusCodes.Status409Conflict, TestNeedsConfirmationCode, "This test sends to the address the action is set up with. Confirm that it may.");

    /// <summary>A file a scanner quarantined, which nobody gets (ADR-0144).</summary>
    public static FormBuilderProblemResult FileQuarantined() =>
        Problem(StatusCodes.Status409Conflict, FileQuarantinedCode, "A scanner quarantined this file.");

    /// <summary>A file that waits for the site's scanners (ADR-0144).</summary>
    public static FormBuilderProblemResult FileNotScanned() =>
        Problem(StatusCodes.Status409Conflict, FileNotScannedCode, "This file is still being scanned. Try again in a minute.");

    /// <summary>A data type that does not exist, or one that is no form picker's (ADR-0133).</summary>
    public static FormBuilderProblemResult DataTypeNotFound() =>
        Problem(StatusCodes.Status404NotFound, DataTypeNotFoundCode, "The form picker's data type does not exist.");

    /// <summary>A template that is neither built in nor saved on this site (ADR-0166).</summary>
    public static FormBuilderProblemResult TemplateNotFound() =>
        Problem(StatusCodes.Status404NotFound, TemplateNotFoundCode, "The template does not exist.");

    /// <summary>A field type the site does not have (ADR-0059).</summary>
    public static FormBuilderProblemResult FieldTypeNotFound() =>
        Problem(StatusCodes.Status404NotFound, FieldTypeNotFoundCode, "The site has no such field type.");

    /// <summary>A draft whose rules the check both runtimes run refuses (ADR-0002, ADR-0203): the preview evaluates none of them.</summary>
    public static FormBuilderProblemResult RulesInvalid() =>
        Problem(StatusCodes.Status422UnprocessableEntity, RulesInvalidCode, "The draft's rules have errors, which the form check lists. The preview shows the form once they are fixed.");

    /// <summary>The user sees the item but lacks <paramref name="permission"/>, which the answer names.</summary>
    public static FormBuilderProblemResult Missing(FormBuilderPermissions permission, IDictionary<string, object?>? extensions = null)
    {
        var name = PermissionRules.NameOf(permission);
        var problem = Problem(StatusCodes.Status403Forbidden, ForbiddenCode, "You do not have the permission to do this.", $"It needs the permission '{name}'.", extensions);
        problem.Problem.Extensions["permission"] = name;
        return problem;
    }

    /// <summary>The user holds the permissions but lacks a section of Umbraco's own, which the answer names.</summary>
    public static FormBuilderProblemResult MissingSection(string section)
    {
        var problem = Problem(StatusCodes.Status403Forbidden, ForbiddenCode, "You do not have the permission to do this.", $"It needs Umbraco's section '{section}'.");
        problem.Problem.Extensions["section"] = section;
        return problem;
    }

    /// <summary>The answer to a failed check of one of Jiron Form Builder's policies, by the denial it gave (ADR-0049).</summary>
    public static FormBuilderProblemResult Refused(AuthorizationResult result, FormBuilderAccessResourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(result);
        return FormBuilderAccessDenial.Of(result) switch
        {
            { Kind: FormBuilderDenialKind.NoSection } => NoSection(),
            { Kind: FormBuilderDenialKind.NotFound } => NotFound(kind),
            { Kind: FormBuilderDenialKind.MissingPermission, Permission: var permission } => Missing(permission),
            _ => Forbidden(),
        };
    }

    /// <summary>A refusal without a permission to name: an action that declares its access wrongly fails closed.</summary>
    public static FormBuilderProblemResult Forbidden() =>
        Problem(StatusCodes.Status403Forbidden, ForbiddenCode, "You do not have the permission to do this.");

    /// <summary>The request holds values the endpoint cannot read; the answer names where, by path, never why in the server's words.</summary>
    public static FormBuilderProblemResult InvalidRequest(ModelStateDictionary modelState)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        var errors = modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new FormBuilderProblemError("invalid_value", entry.Key, "This value is missing or not valid."))
            .ToList();
        return Problem(StatusCodes.Status400BadRequest, InvalidRequestCode, "The request has values that cannot be read.", extensions: new Dictionary<string, object?> { ["errors"] = errors });
    }

    /// <summary>
    /// The answer to a refusal of the services, or null for an exception that is none: values that are not allowed
    /// <c>400</c> with every problem, a change someone else made first, a conflict, a cancelled change or a locked setting
    /// <c>409</c>, a read that ran too long <c>503</c>.
    /// </summary>
    public static FormBuilderProblemResult? Of(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (FormBuilderErrorCodes.Of(exception) is not { } code)
        {
            return null;
        }

        return exception switch
        {
            FormBuilderValidationException invalid => Problem(
                StatusCodes.Status400BadRequest,
                code,
                "Some values are not allowed.",
                invalid.Problems[0].Message,
                new Dictionary<string, object?> { ["errors"] = invalid.Problems.Select(problem => new FormBuilderProblemError(problem.Code, problem.Path, problem.Message)).ToList() }),
            FormBuilderSettingsValidationException invalid => Problem(
                StatusCodes.Status400BadRequest,
                code,
                "Some values are not allowed.",
                invalid.Errors[0].Message,
                new Dictionary<string, object?> { ["errors"] = invalid.Errors.Select(error => new FormBuilderProblemError(error.Code, error.Field, error.Message)).ToList() }),
            FormBuilderConcurrencyException => Problem(StatusCodes.Status409Conflict, code, "Someone else changed this meanwhile. Load it again, then make your change."),
            FormBuilderConflictException conflict => Problem(StatusCodes.Status409Conflict, code, conflict.Message),
            FormBuilderCancelledException cancelled => Problem(
                StatusCodes.Status409Conflict,
                code,
                "The change was stopped by the site.",
                cancelled.Messages.Count == 0 ? null : string.Join(" ", cancelled.Messages.Select(message => message.Message)),
                new Dictionary<string, object?> { ["messages"] = cancelled.Messages.Select(message => message.Message).ToList() }),
            FormBuilderSettingLockedException locked => Problem(
                StatusCodes.Status409Conflict,
                code,
                "The site's configuration sets this, so it cannot be changed here.",
                extensions: new Dictionary<string, object?> { ["field"] = locked.Field, ["configurationKey"] = locked.ConfigurationKey }),
            FormBuilderQueryTimeoutException => Problem(StatusCodes.Status503ServiceUnavailable, code, "The list took too long to load. Try again, or narrow it down."),
            _ => null,
        };
    }

    /// <summary>
    /// A ProblemDetails answer with its code. Its type is <c>about:blank</c>, which RFC 9457 assumes when a problem has none; it is
    /// written all the same, because Umbraco's client reads an answer as a problem only when the member is there, and otherwise
    /// drops the status and the code (ADR-0166).
    /// </summary>
    public static FormBuilderProblemResult Problem(int status, string code, string title, string? detail = null, IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails { Type = AboutBlank, Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        foreach (var (name, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[name] = value;
        }

        return new FormBuilderProblemResult(problem);
    }
}

/// <summary>
/// A refusal written as <c>application/problem+json</c> by itself (ADR-0050). An object result that an action filter
/// returns goes through the result filters, and the backoffice's JSON filter sends it as <c>application/json</c>; this
/// one answers the same way from every filter.
/// </summary>
/// <param name="problem">The problem.</param>
internal sealed class FormBuilderProblemResult(ProblemDetails problem) : IActionResult, IStatusCodeActionResult
{
    /// <summary>The media type of every refusal.</summary>
    public const string MediaType = "application/problem+json";

    private static readonly JsonSerializerOptions Json = FrozenOptions.Of(new(JsonSerializerDefaults.Web));

    /// <summary>The problem it answers with.</summary>
    public ProblemDetails Problem { get; } = problem ?? throw new ArgumentNullException(nameof(problem));

    /// <inheritdoc />
    public int? StatusCode => Problem.Status;

    /// <inheritdoc />
    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return WriteAsync(context.HttpContext);
    }

    /// <summary>Writes the answer outside MVC, as a middleware does.</summary>
    public async Task WriteAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var response = http.Response;
        response.StatusCode = Problem.Status ?? StatusCodes.Status500InternalServerError;
        response.ContentType = MediaType;
        await JsonSerializer.SerializeAsync(response.Body, Problem, Json, http.RequestAborted);
    }
}

/// <summary>One problem of a refused request.</summary>
/// <param name="Code">Its stable code.</param>
/// <param name="Path">Where: a JSON path into a definition, a field of a request or a setting.</param>
/// <param name="Message">What is wrong, for editors.</param>
internal sealed record FormBuilderProblemError(string Code, string Path, string Message);

/// <summary>Turns the services' refusals into the management API's answers (ADR-0050); anything else stays an error.</summary>
internal sealed class FormBuilderExceptionFilter : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (FormBuilderProblems.Of(context.Exception) is { } answer)
        {
            context.Result = answer;
            context.ExceptionHandled = true;
        }
    }
}
