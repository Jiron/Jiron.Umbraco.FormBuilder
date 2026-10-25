using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
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

    /// <summary>A move would give a group more access, and was not confirmed.</summary>
    public const string WidensAccessCode = "widens_access";

    /// <summary>A form's version that does not exist.</summary>
    public const string VersionNotFoundCode = "version_not_found";

    /// <summary>How long a client waits before asking again while the plan has not run.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    /// <summary>The code of a folder or form that does not exist, or that the user does not see.</summary>
    public static string NotFoundCode(FormBuilderAccessResourceKind kind) => kind == FormBuilderAccessResourceKind.Form ? "form_not_found" : "folder_not_found";

    /// <summary>The user lacks the Forms section.</summary>
    public static FormBuilderProblemResult NoSection() =>
        Problem(StatusCodes.Status403Forbidden, NoSectionCode, "You do not have access to the Forms section.");

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

    /// <summary>The user sees the item but lacks <paramref name="permission"/>, which the answer names.</summary>
    public static FormBuilderProblemResult Missing(FormBuilderPermissions permission, IDictionary<string, object?>? extensions = null)
    {
        var name = PermissionRules.NameOf(permission);
        var problem = Problem(StatusCodes.Status403Forbidden, ForbiddenCode, "You do not have the permission to do this.", $"It needs the permission '{name}'.", extensions);
        problem.Problem.Extensions["permission"] = name;
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

    /// <summary>A ProblemDetails answer with its code.</summary>
    public static FormBuilderProblemResult Problem(int status, string code, string title, string? detail = null, IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
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

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The problem it answers with.</summary>
    public ProblemDetails Problem { get; } = problem ?? throw new ArgumentNullException(nameof(problem));

    /// <inheritdoc />
    public int? StatusCode => Problem.Status;

    /// <inheritdoc />
    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = context.HttpContext.Response;
        response.StatusCode = Problem.Status ?? StatusCodes.Status500InternalServerError;
        response.ContentType = MediaType;
        await JsonSerializer.SerializeAsync(response.Body, Problem, Json, context.HttpContext.RequestAborted);
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
