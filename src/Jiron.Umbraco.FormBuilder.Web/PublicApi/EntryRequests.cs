using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the entries endpoint reads of a request and answers with (ADR-0088): a JSON body of at most
/// <see cref="MaxBodyBytes"/>, counted while it is read, which <see cref="EntryBodies"/> reads strictly and
/// <see cref="FormSubmissions"/> runs through the pipeline with this site's upload tokens; the answer is the entry's
/// receipt and outcome, or the refusal with its code.
/// </summary>
/// <param name="submissions">The submission pipeline.</param>
/// <param name="tokens">The upload tokens, which the values of fields storing files post.</param>
internal sealed class EntryRequests(FormSubmissions submissions, UploadTokens tokens)
{
    /// <summary>The most bytes a submission's body holds.</summary>
    public const int MaxBodyBytes = 1_048_576;

    /// <summary>The media type of the body.</summary>
    public const string MediaType = "application/json";

    private static readonly JsonSerializerOptions Json = FrozenOptions.Of(new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    /// <summary>Reads a submission to a form and answers with its receipt and outcome, or its refusal.</summary>
    /// <param name="http">The request.</param>
    /// <param name="formKey">The form's key, from the route.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> SubmitAsync(HttpContext http, Guid formKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var request = http.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            || !media.MediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase)
            || (media.Charset.HasValue && !media.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
        {
            return FormBuilderPublicProblems.UnsupportedMediaType();
        }

        byte[]? body;
        try
        {
            body = await BodyAsync(request, cancellationToken);
        }
        catch (BadHttpRequestException exception)
        {
            FormBuilderPublicProblems.CloseConnection(http);
            return exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? FormBuilderPublicProblems.PayloadTooLarge()
                : FormBuilderPublicProblems.InvalidRequest(EntryBodies.Body);
        }

        if (body is null)
        {
            FormBuilderPublicProblems.CloseConnection(http);
            return FormBuilderPublicProblems.PayloadTooLarge();
        }

        var invalid = new List<string>();
        if (EntryBodies.Read(body, formKey, invalid) is not { } submission)
        {
            return FormBuilderPublicProblems.InvalidRequest([.. invalid]);
        }

        return Answer(await submissions.SubmitAsync(submission with { Tokens = tokens }, cancellationToken));
    }

    // The body, or null once it holds more than the limit; the server's own limit throws past it too.
    private static async Task<byte[]?> BodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(request.ContentLength is { } length and > 0 and <= MaxBodyBytes ? (int)length : 16_384);
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.GetMemory(16_384), cancellationToken);
            if (read == 0)
            {
                return buffer.WrittenSpan.ToArray();
            }

            buffer.Advance(read);
            if (buffer.WrittenCount > MaxBodyBytes)
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
            SubmissionRefusal.FormNotFound => FormBuilderPublicProblems.FormNotFound(),
            SubmissionRefusal.MemberRequired => FormBuilderPublicProblems.MemberRequired(),
            SubmissionRefusal.DuplicateSubmission => FormBuilderPublicProblems.DuplicateSubmission(),
            SubmissionRefusal.FormChanged => FormBuilderPublicProblems.FormChanged(),
            SubmissionRefusal.FormClosed => FormBuilderPublicProblems.FormClosed(),
            SubmissionRefusal.EntryLimitReached => FormBuilderPublicProblems.EntryLimitReached(),
            SubmissionRefusal.InvalidRequest => FormBuilderPublicProblems.InvalidRequest([.. result.Problems.Select(problem => problem.Path)]),
            SubmissionRefusal.ValidationFailed => FormBuilderPublicProblems.ValidationFailed(result.Problems),
            _ => throw new InvalidOperationException("A submission's result is an entry or a refusal."),
        };
    }
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

    /// <summary>The challenge (from 7.1).</summary>
    public string? Challenge { get; init; }

    /// <summary>The captcha's token, for a form whose protection uses one (from 7.1).</summary>
    public string? CaptchaToken { get; init; }
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
