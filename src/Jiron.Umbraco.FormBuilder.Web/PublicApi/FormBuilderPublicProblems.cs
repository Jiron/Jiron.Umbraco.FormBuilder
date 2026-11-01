using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// One error of <c>422 validation_failed</c> (ADR-0088): the field's kind of error, <c>check</c>, <c>rule</c> or
/// <c>empty</c>; where it is; a neutral line for developers; and the index of the field's own check or the key of the
/// validation rule, so a page shows the form's own message for it from the visitor's form.
/// </summary>
/// <param name="Code">Its code.</param>
/// <param name="Path">The field's alias, <c>&lt;grid&gt;.&lt;rowKey&gt;</c>, <c>&lt;group&gt;[&lt;row&gt;].&lt;child&gt;</c>, or <c>""</c> for the form.</param>
/// <param name="Message">What is wrong, for developers.</param>
/// <param name="Check">The index of the field's own check that fails; only for <c>check</c>.</param>
/// <param name="Rule">The key of the validation rule whose message shows; only for <c>rule</c>.</param>
internal sealed record FormBuilderValidationError(
    string Code,
    string Path,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Check,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Rule);

/// <summary>
/// The public API's refusals as ProblemDetails (section 4.6, ADR-0012), each with its stable <c>code</c> and a title
/// written for visitors in neutral words, naming no internals.
/// </summary>
internal static class FormBuilderPublicProblems
{
    /// <summary>The form does not exist, is not published, is in the trash or is not served to headless sites.</summary>
    public const string FormNotFoundCode = "form_not_found";

    /// <summary>The address names no endpoint.</summary>
    public const string NotFoundCode = "not_found";

    /// <summary>The request's route, query or body holds values the endpoint cannot read.</summary>
    public const string InvalidRequestCode = "invalid_request";

    /// <summary>A browser on another site asked, and that site may not call the API.</summary>
    public const string OriginDeniedCode = "origin_denied";

    /// <summary>A request with the <c>Jfb-Key</c> scheme whose key is malformed or matches none (ADR-0126).</summary>
    public const string ServerKeyInvalidCode = "server_key_invalid";

    /// <summary>The body is larger than the endpoint takes.</summary>
    public const string PayloadTooLargeCode = "payload_too_large";

    /// <summary>The body, or the file it holds, is of a kind the endpoint does not take.</summary>
    public const string UnsupportedMediaTypeCode = "unsupported_media_type";

    /// <summary>Where the errors of an upload point: its file part.</summary>
    public const string FilePath = "file";

    /// <summary>The endpoint ran out of time.</summary>
    public const string TimeoutCode = "timeout";

    /// <summary>A rate limit is reached; <c>Retry-After</c> says when to send again (ADR-0012).</summary>
    public const string RateLimitedCode = "rate_limited";

    /// <summary>The form takes entries only from members it admits (ADR-0012).</summary>
    public const string MemberRequiredCode = "member_required";

    /// <summary>The request's challenge is missing, forged, expired, another's or used; the page fetches a new one (ADR-0012).</summary>
    public const string ChallengeRequiredCode = "challenge_required";

    /// <summary>The captcha's token does not pass (ADR-0142).</summary>
    public const string CaptchaFailedCode = "captcha_failed";

    /// <summary>The form has an entry with this submission id and other answers (section 4.4).</summary>
    public const string DuplicateSubmissionCode = "duplicate_submission";

    /// <summary>The page showed a revision of the form that cannot take the answers, or a consent wording that changed (ADR-0002).</summary>
    public const string FormChangedCode = "form_changed";

    /// <summary>The form does not take entries at this time.</summary>
    public const string FormClosedCode = "form_closed";

    /// <summary>The form holds as many entries as its limit allows.</summary>
    public const string EntryLimitReachedCode = "entry_limit_reached";

    /// <summary>The answers fail their checks, each error by where it is (ADR-0088).</summary>
    public const string ValidationFailedCode = "validation_failed";

    /// <summary>Something unexpected went wrong; the site logged it.</summary>
    public const string ServerErrorCode = "server_error";

    /// <summary>The form does not exist, or is not served: the same answer for each, so forms cannot be probed.</summary>
    public static FormBuilderProblemResult FormNotFound() =>
        FormBuilderProblems.Problem(StatusCodes.Status404NotFound, FormNotFoundCode, "The form does not exist.");

    /// <summary>The address names no endpoint of the public API.</summary>
    public static FormBuilderProblemResult NotFound() =>
        FormBuilderProblems.Problem(StatusCodes.Status404NotFound, NotFoundCode, "There is nothing at this address.");

    /// <summary>The request holds values the endpoint cannot read, named by where they are.</summary>
    /// <param name="fields">The names of the route, query or body values that cannot be read.</param>
    public static FormBuilderProblemResult InvalidRequest(params string[] fields) =>
        FormBuilderProblems.Problem(
            StatusCodes.Status400BadRequest,
            InvalidRequestCode,
            "The request has values that cannot be read.",
            extensions: new Dictionary<string, object?> { ["errors"] = fields.Select(field => new FormBuilderProblemError("invalid_value", field, "This value is not valid.")).ToList() });

    /// <summary>A browser on a site that may not call the API.</summary>
    public static FormBuilderProblemResult ServerKeyInvalid() =>
        FormBuilderProblems.Problem(StatusCodes.Status401Unauthorized, ServerKeyInvalidCode, "The server key is not one this site knows.");

    /// <summary>A request from a site that may not send requests here.</summary>
    public static FormBuilderProblemResult OriginDenied() =>
        FormBuilderProblems.Problem(StatusCodes.Status403Forbidden, OriginDeniedCode, "This site may not send requests here.");

    /// <summary>A body larger than the endpoint takes.</summary>
    public static FormBuilderProblemResult PayloadTooLarge() =>
        FormBuilderProblems.Problem(StatusCodes.Status413PayloadTooLarge, PayloadTooLargeCode, "The request is too large.");

    /// <summary>An upload larger than its field takes, with the field's kind of error (ADR-0087).</summary>
    /// <param name="kind">The kind: <c>fileSize</c>, or <c>invalid</c> for a type that does not declare it.</param>
    public static FormBuilderProblemResult FileTooLarge(FieldMessageKinds kind) =>
        FormBuilderProblems.Problem(StatusCodes.Status413PayloadTooLarge, PayloadTooLargeCode, "The file is too large.", extensions: FileErrors(kind));

    /// <summary>A body of a kind the endpoint does not take.</summary>
    public static FormBuilderProblemResult UnsupportedMediaType() =>
        FormBuilderProblems.Problem(StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaTypeCode, "The request is not of a kind this address takes.");

    /// <summary>An upload whose name or first bytes its field does not take, with the field's kind of error (ADR-0087).</summary>
    /// <param name="kind">The kind the checks reported.</param>
    public static FormBuilderProblemResult FileRefused(FieldMessageKinds kind) =>
        FormBuilderProblems.Problem(StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaTypeCode, "This file cannot be uploaded here.", extensions: FileErrors(kind));

    /// <summary>
    /// Closes the connection after the answer on HTTP/1.x, whose body the server did not read to its end, so the rest
    /// of it is never read; HTTP/2 and HTTP/3 reset the request's stream themselves.
    /// </summary>
    /// <param name="http">The request.</param>
    public static void CloseConnection(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (HttpProtocol.IsHttp11(http.Request.Protocol) || HttpProtocol.IsHttp10(http.Request.Protocol))
        {
            http.Response.Headers.Connection = "close";
        }
    }

    /// <summary>A request without a challenge that counts; the page fetches a new one and sends again (ADR-0012).</summary>
    public static FormBuilderProblemResult ChallengeRequired() =>
        FormBuilderProblems.Problem(StatusCodes.Status401Unauthorized, ChallengeRequiredCode, "Please send this form again.");

    /// <summary>A captcha's token that does not pass, so the visitor can try again (ADR-0142).</summary>
    public static FormBuilderProblemResult CaptchaFailed() =>
        FormBuilderProblems.Problem(StatusCodes.Status403Forbidden, CaptchaFailedCode, "Please solve the check again.");

    /// <summary>A form that takes entries only from members it admits; until 14.3 every public submission to one.</summary>
    public static FormBuilderProblemResult MemberRequired() =>
        FormBuilderProblems.Problem(StatusCodes.Status403Forbidden, MemberRequiredCode, "Please sign in to send this form.");

    /// <summary>Other answers sent with the submission id of an entry the form has.</summary>
    public static FormBuilderProblemResult DuplicateSubmission() =>
        FormBuilderProblems.Problem(StatusCodes.Status409Conflict, DuplicateSubmissionCode, "This form was sent already.");

    /// <summary>The form changed since the page showed it; the page loads it again.</summary>
    public static FormBuilderProblemResult FormChanged() =>
        FormBuilderProblems.Problem(StatusCodes.Status409Conflict, FormChangedCode, "This form has changed. Please check your answers and send it again.");

    /// <summary>A form that does not take entries at this time.</summary>
    public static FormBuilderProblemResult FormClosed() =>
        FormBuilderProblems.Problem(StatusCodes.Status403Forbidden, FormClosedCode, "This form is closed.");

    /// <summary>A form that holds as many entries as its limit allows.</summary>
    public static FormBuilderProblemResult EntryLimitReached() =>
        FormBuilderProblems.Problem(StatusCodes.Status403Forbidden, EntryLimitReachedCode, "This form takes no more entries.");

    /// <summary>Answers that fail their checks, each error with its code, where it is and a line for developers (ADR-0088).</summary>
    /// <param name="problems">The errors.</param>
    public static FormBuilderProblemResult ValidationFailed(IEnumerable<SubmissionProblem> problems) =>
        FormBuilderProblems.Problem(
            StatusCodes.Status422UnprocessableEntity,
            ValidationFailedCode,
            "Some answers need another look.",
            extensions: new Dictionary<string, object?> { ["errors"] = problems.Select(problem => new FormBuilderValidationError(problem.Code, problem.Path, LineOf(problem.Code), problem.Check, problem.Rule)).ToList() });

    /// <summary>A rate limit is reached: the answer says when to send again, and the visitor keeps every answer (ADR-0012).</summary>
    /// <param name="http">The request, whose response gets <c>Retry-After</c>.</param>
    /// <param name="decision">The limiter's refusal.</param>
    public static FormBuilderProblemResult RateLimited(HttpContext http, RateLimitDecision decision)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return FormBuilderProblems.Problem(StatusCodes.Status429TooManyRequests, RateLimitedCode, "Too many requests. Please wait a moment and try again.");
    }

    /// <summary>The endpoint ran out of time.</summary>
    public static FormBuilderProblemResult Timeout() =>
        FormBuilderProblems.Problem(StatusCodes.Status504GatewayTimeout, TimeoutCode, "This took too long. Please try again.");

    /// <summary>Something unexpected went wrong.</summary>
    public static FormBuilderProblemResult ServerError() =>
        FormBuilderProblems.Problem(StatusCodes.Status500InternalServerError, ServerErrorCode, "Something went wrong. Please try again later.");

    // The one error of an upload: the field's kind of error as the code, at the file part.
    private static Dictionary<string, object?> FileErrors(FieldMessageKinds kind)
    {
        var name = FieldMessageProperties.All.FirstOrDefault(property => property.Kind == kind)?.Name
            ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not one kind of error.");
        var message = kind switch
        {
            FieldMessageKinds.FileSize => "The file is larger than this field takes.",
            FieldMessageKinds.FileType => "This field does not take files of this kind.",
            _ => "This field cannot take this file.",
        };
        return new Dictionary<string, object?> { ["errors"] = new List<FormBuilderProblemError> { new(name, FilePath, message) } };
    }

    // A neutral line for developers about a validation error; the page shows the form's own message or its default.
    private static string LineOf(string code) => code switch
    {
        "required" => "This field needs an answer.",
        "minLength" or "minWords" => "This answer is too short.",
        "maxLength" or "maxWords" => "This answer is too long.",
        "min" or "max" or "step" or "dateWindow" => "This answer is outside what the field takes.",
        "selections" or "rowCount" or "fileCount" => "This field has too few or too many of its kind.",
        "fileType" or "fileSize" => "This field does not take this file.",
        "confirmation" => "The two answers differ.",
        SubmissionProblems.Check => "This answer fails one of the field's checks.",
        SubmissionProblems.Rule => "A rule of the form shows its message here.",
        SubmissionProblems.Empty => "The submission holds no answer.",
        _ => "This answer cannot be taken as it is.",
    };

    /// <summary>The tables are not ready yet; the answer says when to ask again (ADR-0033).</summary>
    public static FormBuilderProblemResult MigrationsPending(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Headers.RetryAfter = ((int)FormBuilderProblems.RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return FormBuilderProblems.Problem(StatusCodes.Status503ServiceUnavailable, FormBuilderProblems.MigrationsPendingCode, "The forms are being set up. Please try again in a moment.");
    }
}
