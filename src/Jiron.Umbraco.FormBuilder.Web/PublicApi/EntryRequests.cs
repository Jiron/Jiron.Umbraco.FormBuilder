using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the entries, validate and receipt endpoints read of a request and answer with (ADR-0088, ADR-0089): a JSON
/// body, counted while it is read, which <see cref="EntryBodies"/> reads strictly and <see cref="FormSubmissions"/> runs
/// through the pipeline with this site's upload tokens; the answer is an entry's receipt and outcome, nothing for a page
/// that passes, or the refusal with its code.
/// </summary>
/// <param name="submissions">The submission pipeline.</param>
/// <param name="tokens">The upload tokens, which the values of fields storing files post.</param>
/// <param name="challenges">The challenges, which every request but a server key's posts (ADR-0138).</param>
internal sealed class EntryRequests(FormSubmissions submissions, UploadTokens tokens, ChallengeTokens challenges)
{
    /// <summary>The most bytes a submission's body holds, and a page's.</summary>
    public const int MaxBodyBytes = 1_048_576;

    /// <summary>
    /// The most bytes a receipt's body holds: a submission id and the longest challenge
    /// (<see cref="EntryBodies.MaxChallengeLength"/>), with room for escapes.
    /// </summary>
    public const int MaxReceiptBodyBytes = 16_384;

    /// <summary>The media type of the body.</summary>
    public const string MediaType = "application/json";

    private static readonly JsonSerializerOptions Json = FrozenOptions.Of(new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    /// <summary>Reads a submission to a form and answers with its receipt and outcome, or its refusal.</summary>
    /// <param name="http">The request.</param>
    /// <param name="formKey">The form's key, from the route.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> SubmitAsync(HttpContext http, Guid formKey, CancellationToken cancellationToken)
    {
        var body = await ReadAsync(http, MaxBodyBytes, cancellationToken);
        if (body.Refusal is { } refusal)
        {
            return refusal;
        }

        var invalid = new List<string>();
        return EntryBodies.Read(body.Bytes, formKey, invalid) is { } submission
            ? Answer(await submissions.SubmitAsync(submission with { Tokens = tokens, Challenges = challenges, Address = http.Connection.RemoteIpAddress, Member = await MemberAsync(http), ServerKey = ServerKeyRequest.NameOf(http) }, cancellationToken))
            : FormBuilderPublicProblems.InvalidRequest([.. invalid]);
    }

    /// <summary>Reads one page's answers to a form and checks them (ADR-0089): <c>204</c> when they pass, or the refusal.</summary>
    /// <param name="http">The request.</param>
    /// <param name="formKey">The form's key, from the route.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> ValidateAsync(HttpContext http, Guid formKey, CancellationToken cancellationToken)
    {
        var body = await ReadAsync(http, MaxBodyBytes, cancellationToken);
        if (body.Refusal is { } refusal)
        {
            return refusal;
        }

        var invalid = new List<string>();
        return EntryBodies.ReadPage(body.Bytes, formKey, invalid) is ({ } submission, var page)
            ? Answer(await submissions.ValidateAsync(submission with { Tokens = tokens, Challenges = challenges, Address = http.Connection.RemoteIpAddress, Member = await MemberAsync(http), ServerKey = ServerKeyRequest.NameOf(http) }, page, cancellationToken))
            : FormBuilderPublicProblems.InvalidRequest([.. invalid]);
    }

    // The member signed in on the request, whose values a field prefilled from the member takes (ADR-0124).
    private static Task<Guid?> MemberAsync(HttpContext http) => http.RequestServices.GetRequiredService<FormMembers>().KeyAsync();

    /// <summary>Reads a receipt's submission id and answers with its entry's receipt and outcome, or the refusal (ADR-0089).</summary>
    /// <param name="http">The request.</param>
    /// <param name="formKey">The form's key, from the route.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> ReceiptAsync(HttpContext http, Guid formKey, CancellationToken cancellationToken)
    {
        var body = await ReadAsync(http, MaxReceiptBodyBytes, cancellationToken);
        if (body.Refusal is { } refusal)
        {
            return refusal;
        }

        var invalid = new List<string>();
        return EntryBodies.ReadReceipt(body.Bytes, invalid) is ({ } submissionId, var challenge)
            ? Answer(await submissions.ReceiptAsync(formKey, submissionId, ServerKeyRequest.NameOf(http), challenge, challenges, cancellationToken))
            : FormBuilderPublicProblems.InvalidRequest([.. invalid]);
    }

    /// <summary>A body that may be left out: no bytes for none, otherwise as every other endpoint reads its body.</summary>
    /// <param name="http">The request.</param>
    /// <param name="limit">The most bytes it may hold.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public static async Task<Body> ReadOptionalAsync(HttpContext http, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Request.ContentLength == 0 || (http.Request.ContentLength is null && http.Request.ContentType is null)
            ? new Body([], null)
            : await ReadAsync(http, limit, cancellationToken);
    }

    // The body of JSON in UTF-8, read whole when it holds at most the limit; otherwise the refusal, and a body past the
    // limit closes the connection.
    private static async Task<Body> ReadAsync(HttpContext http, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var request = http.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            || !media.MediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase)
            || (media.Charset.HasValue && !media.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
        {
            return new Body([], FormBuilderPublicProblems.UnsupportedMediaType());
        }

        byte[]? bytes;
        try
        {
            bytes = await BytesAsync(request, limit, cancellationToken);
        }
        catch (BadHttpRequestException exception)
        {
            FormBuilderPublicProblems.CloseConnection(http);
            return new Body([], exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? FormBuilderPublicProblems.PayloadTooLarge()
                : FormBuilderPublicProblems.InvalidRequest(EntryBodies.Body));
        }

        if (bytes is null)
        {
            FormBuilderPublicProblems.CloseConnection(http);
            return new Body([], FormBuilderPublicProblems.PayloadTooLarge());
        }

        return new Body(bytes, null);
    }

    // The body's bytes, or null once they are more than the limit; the server's own limit throws past it too.
    private static async Task<byte[]?> BytesAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(request.ContentLength is { } length and > 0 && length <= limit ? (int)length : Math.Min(limit, 16_384));
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.GetMemory(16_384), cancellationToken);
            if (read == 0)
            {
                return buffer.WrittenSpan.ToArray();
            }

            buffer.Advance(read);
            if (buffer.WrittenCount > limit)
            {
                return null;
            }
        }
    }

    // The pipeline's result as the public API answers it.
    private static IActionResult Answer(SubmissionResult result)
    {
        if (result is { Receipt: { } receipt, Outcome: { } outcome })
        {
            var model = new EntryResponseModel
            {
                Receipt = new EntryReceiptResponseModel
                {
                    SubmissionId = receipt.SubmissionId,
                    Reference = receipt.Reference,
                    SubmittedUtc = receipt.Submitted.UtcDateTime,
                },
                Outcome = new EntryOutcomeResponseModel
                {
                    Kind = outcome.Kind == OutcomeKind.Redirect ? EntryOutcomeResponseModel.Redirect : EntryOutcomeResponseModel.Message,
                    Html = outcome.Html,
                    Url = outcome.Url,
                    DelaySeconds = outcome is { Kind: OutcomeKind.Message, Url: not null } ? outcome.DelaySeconds : null,
                    ShowAnswers = outcome.ShowAnswers ? true : null,
                },
            };
            return new FileContentResult(JsonSerializer.SerializeToUtf8Bytes(model, Json), "application/json; charset=utf-8");
        }

        return result.Refusal switch
        {
            null => new NoContentResult(),
            SubmissionRefusal.FormNotFound => FormBuilderPublicProblems.FormNotFound(),
            SubmissionRefusal.ChallengeRequired => FormBuilderPublicProblems.ChallengeRequired(),
            SubmissionRefusal.MemberRequired => FormBuilderPublicProblems.MemberRequired(),
            SubmissionRefusal.DuplicateSubmission => FormBuilderPublicProblems.DuplicateSubmission(),
            SubmissionRefusal.FormChanged => FormBuilderPublicProblems.FormChanged(),
            SubmissionRefusal.FormClosed => FormBuilderPublicProblems.FormClosed(),
            SubmissionRefusal.EntryLimitReached => FormBuilderPublicProblems.EntryLimitReached(),
            SubmissionRefusal.InvalidRequest => FormBuilderPublicProblems.InvalidRequest([.. result.Problems.Select(problem => problem.Path)]),
            SubmissionRefusal.ValidationFailed => FormBuilderPublicProblems.ValidationFailed(result.Problems),
            SubmissionRefusal.EntryNotFound => FormBuilderPublicProblems.NotFound(),
            _ => throw new InvalidOperationException("A submission's result is an entry, a page that passes or a refusal."),
        };
    }

    /// <summary>A body as it was read, or the refusal of it.</summary>
    /// <param name="Bytes">Its bytes.</param>
    /// <param name="Refusal">The refusal, or null.</param>
    public readonly record struct Body(byte[] Bytes, IActionResult? Refusal);
}

/// <summary>
/// A submission (ADR-0088), as the document describes it; the endpoint reads the body itself, strictly. Every member
/// but <c>revision</c> may be left out or null, and any other member is refused.
/// </summary>
internal sealed record EntryRequestModel
{
    /// <summary>The visitor's id of this submission, a UUID that makes a retry safe; the server makes one when it is left out.</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>The form's public revision the page showed, as the form endpoint gives it.</summary>
    public required int Revision { get; init; }

    /// <summary>The culture the page is in: one of the site's languages, otherwise the default language.</summary>
    public string? Culture { get; init; }

    /// <summary>The page the form is on, whose site's settings apply.</summary>
    public Guid? ContentKey { get; init; }

    /// <summary>
    /// The answers by field alias: a string, a number (kept as written), <c>true</c> or <c>false</c>, <c>null</c> for
    /// none, or an array of strings; a part as <c>alias.part</c>, a grid's row as the part named by the row's key; a
    /// repeatable group's alias holds an array of rows, each an object of its children's answers. A field that stores
    /// files holds the tokens its uploads answered with.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }

    /// <summary>Each ticked consent's wording hash by its alias, as the form endpoint gives it (ADR-0009).</summary>
    public IReadOnlyDictionary<string, string>? Consents { get; init; }

    /// <summary>The page's query values by name, a string or an array of at most 20 strings; only the names the form lists are read.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Query { get; init; }

    /// <summary>Context values by name; only the names the form lists are read and stored.</summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }

    /// <summary>What the form's honeypot holds; a visitor never fills it in, and a request with a server key does not send it (ADR-0139).</summary>
    public string? Honeypot { get; init; }

    /// <summary>The challenge the form's challenge endpoint gave; a request with a server key needs none.</summary>
    public string? Challenge { get; init; }

    /// <summary>The captcha's token, for a form whose protection uses one (from 7.2).</summary>
    public string? CaptchaToken { get; init; }

    /// <summary>The number that solves the challenge's puzzle, in digits, for a form at the proof-of-work level (ADR-0141).</summary>
    public string? ProofOfWork { get; init; }
}

/// <summary>
/// One page's answers to check (ADR-0089), as the document describes them: a submission's body with the page's key and
/// without a submission id or a captcha token. Every member but <c>revision</c> and <c>pageKey</c> may be left out or
/// null, and any other member is refused.
/// </summary>
internal sealed record PageRequestModel
{
    /// <summary>The form's public revision the page showed, as the form endpoint gives it.</summary>
    public required int Revision { get; init; }

    /// <summary>The key of the page whose answers are checked, as the form endpoint gives it.</summary>
    public required Guid PageKey { get; init; }

    /// <summary>The culture the page is in: one of the site's languages, otherwise the default language.</summary>
    public string? Culture { get; init; }

    /// <summary>The page the form is on, whose site's settings apply.</summary>
    public Guid? ContentKey { get; init; }

    /// <summary>
    /// The answers the visitor gave so far by field alias, those of the pages before it among them, as a submission
    /// posts them; only the errors of the page's fields are answered.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }

    /// <summary>Each ticked consent's wording hash by its alias, as the form endpoint gives it (ADR-0009).</summary>
    public IReadOnlyDictionary<string, string>? Consents { get; init; }

    /// <summary>The page's query values by name, a string or an array of at most 20 strings; only the names the form lists are read.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Query { get; init; }

    /// <summary>Context values by name; only the names the form lists are read.</summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }

    /// <summary>What the form's honeypot holds; a visitor never fills it in, and a request with a server key does not send it (ADR-0139).</summary>
    public string? Honeypot { get; init; }

    /// <summary>The challenge the form's challenge endpoint gave; a request with a server key needs none.</summary>
    public string? Challenge { get; init; }
}

/// <summary>A submission whose answer was lost, to look up (ADR-0012, ADR-0089); any other member is refused.</summary>
internal sealed record ReceiptRequestModel
{
    /// <summary>The submission id the submission posted.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>The challenge the submission was sent with, which counts here for 24 hours past its expiry; a request with the server key that made the entry needs none.</summary>
    public string? Challenge { get; init; }
}

/// <summary>A stored entry's receipt and the outcome to show (ADR-0088), for a new submission and its retries alike.</summary>
internal sealed record EntryResponseModel
{
    /// <summary>The receipt.</summary>
    public required EntryReceiptResponseModel Receipt { get; init; }

    /// <summary>What the visitor gets now.</summary>
    public required EntryOutcomeResponseModel Outcome { get; init; }
}

/// <summary>The receipt of an entry (ADR-0012).</summary>
internal sealed record EntryReceiptResponseModel
{
    /// <summary>The submission id, the one posted or the one the server made.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>The entry's reference in the form's format, such as <c>REQ-2026-00042</c>.</summary>
    public required string Reference { get; init; }

    /// <summary>When it was submitted, in UTC.</summary>
    public required DateTime SubmittedUtc { get; init; }
}

/// <summary>What the visitor gets after sending (ADR-0088).</summary>
internal sealed record EntryOutcomeResponseModel
{
    /// <summary>A message, perhaps followed by a redirect.</summary>
    public const string Message = "message";

    /// <summary>A redirect at once.</summary>
    public const string Redirect = "redirect";

    /// <summary><c>message</c> or <c>redirect</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The message as sanitised HTML; left out for the page's own thank-you.</summary>
    public string? Html { get; init; }

    /// <summary>Where the visitor goes; left out for nowhere.</summary>
    public string? Url { get; init; }

    /// <summary>For a message with a URL: how long the message shows first, in seconds.</summary>
    public int? DelaySeconds { get; init; }

    /// <summary>True when the message shows a copy of the answers; left out otherwise.</summary>
    public bool? ShowAnswers { get; init; }
}
