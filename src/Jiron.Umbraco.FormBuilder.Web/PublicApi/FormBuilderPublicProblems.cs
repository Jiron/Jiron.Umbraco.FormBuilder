using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

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

    /// <summary>The body is larger than the endpoint takes.</summary>
    public const string PayloadTooLargeCode = "payload_too_large";

    /// <summary>The body, or the file it holds, is of a kind the endpoint does not take.</summary>
    public const string UnsupportedMediaTypeCode = "unsupported_media_type";

    /// <summary>Where the errors of an upload point: its file part.</summary>
    public const string FilePath = "file";

    /// <summary>The endpoint ran out of time.</summary>
    public const string TimeoutCode = "timeout";

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

    /// <summary>The tables are not ready yet; the answer says when to ask again (ADR-0033).</summary>
    public static FormBuilderProblemResult MigrationsPending(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Headers.RetryAfter = ((int)FormBuilderProblems.RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return FormBuilderProblems.Problem(StatusCodes.Status503ServiceUnavailable, FormBuilderProblems.MigrationsPendingCode, "The forms are being set up. Please try again in a moment.");
    }
}
