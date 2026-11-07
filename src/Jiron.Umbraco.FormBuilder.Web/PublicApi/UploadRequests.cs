using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the upload endpoint reads of a request and answers with (ADR-0087): the multipart body as it streams, held to
/// the field's limit before and while it is read, its challenge part, then its file part, which
/// <see cref="FileUploads"/> checks and stores; the answer carries the upload's token. Every refusal closes the
/// connection on HTTP/1.x, as the rest of the body is never read.
/// </summary>
/// <param name="uploads">The checks and the store of uploads.</param>
/// <param name="tokens">The upload tokens.</param>
/// <param name="challenges">The challenges, which an upload's first part holds (ADR-0138).</param>
/// <param name="limiter">Holds a client to its share of what may wait for an entry (ADR-0148).</param>
/// <param name="logger">Notes which server key uploaded a file.</param>
internal sealed partial class UploadRequests(FileUploads uploads, UploadTokens tokens, ChallengeTokens challenges, FormBuilderRateLimiter limiter, ILogger<UploadRequests> logger)
{
    /// <summary>The longest multipart boundary (RFC 2046).</summary>
    public const int MaxBoundaryLength = 70;

    /// <summary>The most headers one part has.</summary>
    public const int MaxPartHeaders = 8;

    /// <summary>The most bytes of one part's headers.</summary>
    public const int MaxPartHeaderBytes = 4096;

    /// <summary>The most bytes of the challenge part.</summary>
    public const int MaxChallengeBytes = 4096;

    private const string FormData = "form-data";

    private static readonly JsonSerializerOptions Json = FrozenOptions.Of(new(JsonSerializerDefaults.Web));

    /// <summary>The field with this key, a repeatable group's child among them, when its type stores files; null otherwise.</summary>
    /// <param name="definition">The published form's definition.</param>
    /// <param name="key">The field's key.</param>
    public UploadField? FieldOf(FormDefinition definition, Guid key)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var field = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field))
            .FirstOrDefault(field => field.Key == key);
        return field is not null && uploads.TypeOf(field) is { } type ? new UploadField(field, type) : null;
    }

    /// <summary>
    /// Reads the upload of one file to a published form's field and answers: <c>200</c> with the token, the cleaned
    /// name, the size and the content type, or the refusal ADR-0087 names.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="formKey">The published form's key.</param>
    /// <param name="upload">The field (<see cref="FieldOf"/>).</param>
    /// <param name="member">On a form for members, the member signed in, whom the challenge must name.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> UploadAsync(HttpContext http, Guid formKey, UploadField upload, Guid? member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(upload);
        var request = http.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            || !media.MediaType.Equals(FormBuilderUploadBodyAttribute.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(http, FormBuilderPublicProblems.UnsupportedMediaType());
        }

        var boundary = HeaderUtilities.RemoveQuotes(media.Boundary);
        if (StringSegment.IsNullOrEmpty(boundary) || boundary.Length > MaxBoundaryLength)
        {
            return Refused(http, FormBuilderPublicProblems.InvalidRequest("Content-Type"));
        }

        var (field, type) = (upload.Field, upload.Type);
        var limit = uploads.RequestLimitOf(type, field);
        if (request.ContentLength > limit)
        {
            return Refused(http, FormBuilderPublicProblems.FileTooLarge(FileUploads.SizeError(type)));
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } server)
        {
            server.MaxRequestBodySize = limit;
        }

        using var body = new FileUploadStream(ReadOnlyMemory<byte>.Empty, request.Body, limit);
        var reader = new MultipartReader(boundary.Value!, body) { HeadersCountLimit = MaxPartHeaders, HeadersLengthLimit = MaxPartHeaderBytes };
        FilePart part;
        try
        {
            part = await FilePartAsync(reader, formKey, ServerKeyRequest.NameOf(http) is null, member, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return Refused(http, Fault(exception, type));
        }

        if (part.ChallengeRefused)
        {
            return Refused(http, FormBuilderPublicProblems.ChallengeRequired());
        }

        if (part.Section is null)
        {
            return Refused(http, FormBuilderPublicProblems.InvalidRequest(part.Invalid));
        }

        if (limiter.CheckUploadShare(http) is { Allowed: false } spent)
        {
            return Refused(http, FormBuilderPublicProblems.RateLimited(http, spent));
        }

        FileUploadResult result;
        try
        {
            result = await uploads.StoreAsync(formKey, type, field, part.Name, part.Section.Body, next => EndsAsync(reader, next), PreviewRequest.Of(http) is not null, cancellationToken);
        }
        catch (Exception exception) when (exception is FileUploadReadException or InvalidDataException)
        {
            return Refused(http, Fault(exception, type));
        }

        if (result is { Status: FileUploadStatus.Stored, File: { } kept })
        {
            limiter.CountUploadShare(http, kept.SizeBytes);
            if (ServerKeyRequest.NameOf(http) is { } serverKey)
            {
                LogStoredByKey(logger, serverKey, kept.Key, formKey);
            }
        }

        return result switch
        {
            { Status: FileUploadStatus.Stored, File: { } file } => Answer(new FileUploadResponseModel
            {
                Token = tokens.Issue(formKey, field.Key, file.Key, part.Challenge),
                Name = file.OriginalName,
                Size = file.SizeBytes,
                ContentType = file.ContentType,
            }),
            { Status: FileUploadStatus.Refused, Error: { } kind } => Refused(http, FormBuilderPublicProblems.FileRefused(kind)),
            { Status: FileUploadStatus.TooLarge, Error: { } kind } => Refused(http, FormBuilderPublicProblems.FileTooLarge(kind)),
            { Status: FileUploadStatus.FormGone } => Refused(http, FormBuilderPublicProblems.FormNotFound()),
            { Status: FileUploadStatus.NoRoom } => Refused(http, FormBuilderPublicProblems.UploadsFull(http)),
            _ => Refused(http, FormBuilderPublicProblems.InvalidRequest(FormBuilderUploadBodyAttribute.FilePart)),
        };
    }

    // The file part after its challenge, which is read and checked before the file's first byte (ADR-0006); a request with
    // a server key needs none. A part out of place names where the request went wrong.
    private async Task<FilePart> FilePartAsync(MultipartReader reader, Guid formKey, bool needsChallenge, Guid? member, CancellationToken cancellationToken)
    {
        var challenge = false;
        Guid? challengeId = null;
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !disposition.DispositionType.Equals(FormData, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var name = HeaderUtilities.RemoveQuotes(disposition.Name);
            var isFile = disposition.IsFileDisposition();
            if (!challenge && !isFile && name.Equals(FormBuilderUploadBodyAttribute.ChallengePart, StringComparison.Ordinal))
            {
                if (await TextAsync(section.Body, MaxChallengeBytes, cancellationToken) is not { } text)
                {
                    return new FilePart(null, null, FormBuilderUploadBodyAttribute.ChallengePart);
                }

                challenge = true;
                if (needsChallenge)
                {
                    if (challenges.Read(text, formKey, TimeSpan.Zero) is not { } ticket || (member is not null && ticket.Member != member))
                    {
                        return new FilePart(null, null, FormBuilderUploadBodyAttribute.ChallengePart) { ChallengeRefused = true };
                    }

                    challengeId = ticket.Id;
                }

                continue;
            }

            if (isFile && name.Equals(FormBuilderUploadBodyAttribute.FilePart, StringComparison.Ordinal))
            {
                // The name as the device gave it, decoded and without its quotes; browsers do not escape a backslash in
                // it, so none is unescaped, and a path's separators stay for the name's cleaning.
                var fileName = StringSegment.IsNullOrEmpty(disposition.FileNameStar) ? disposition.FileName : disposition.FileNameStar;
                return needsChallenge && challengeId is null
                    ? new FilePart(null, null, FormBuilderUploadBodyAttribute.ChallengePart) { ChallengeRefused = true }
                    : new FilePart(section, fileName.Value, FormBuilderUploadBodyAttribute.FilePart) { Challenge = challengeId };
            }

            break;
        }

        return new FilePart(null, null, FormBuilderUploadBodyAttribute.FilePart);
    }

    // A part's text when its bytes are no more than the limit, reading them to their end or one past the limit.
    private static async Task<string?> TextAsync(Stream part, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[limit + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await part.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
            }

            length += read;
        }

        return null;
    }

    // Whether the request ends after the file: the closing boundary, then at most a short epilogue.
    private static async Task<bool> EndsAsync(MultipartReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadNextSectionAsync(cancellationToken) is null;
        }
        catch (Exception exception) when (exception is InvalidDataException || exception is IOException and not (FileUploadReadException or FileTooLargeException))
        {
            return false;
        }
    }

    // A request that could not be read: past a limit, or malformed or cut short.
    private static FormBuilderProblemResult Fault(Exception exception, IFormBuilderFieldType type) => exception switch
    {
        FileTooLargeException or FileUploadReadException { InnerException: BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } } =>
            FormBuilderPublicProblems.FileTooLarge(FileUploads.SizeError(type)),
        _ => FormBuilderPublicProblems.InvalidRequest(FormBuilderUploadBodyAttribute.FilePart),
    };

    private static FormBuilderProblemResult Refused(HttpContext http, FormBuilderProblemResult problem)
    {
        FormBuilderPublicProblems.CloseConnection(http);
        return problem;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The server key {KeyName} uploaded the file {FileKey} to the form {FormKey}")]
    private static partial void LogStoredByKey(ILogger logger, string keyName, Guid fileKey, Guid formKey);

    private static FileContentResult Answer(FileUploadResponseModel model) =>
        new(JsonSerializer.SerializeToUtf8Bytes(model, Json), "application/json; charset=utf-8");

    // The file part with the name its device gave, or none and where the request went wrong.
    private sealed record FilePart(MultipartSection? Section, string? Name, string Invalid)
    {
        public Guid? Challenge { get; init; }

        public bool ChallengeRefused { get; init; }
    }
}

/// <summary>A field that takes uploads, with its type.</summary>
/// <param name="Field">The field.</param>
/// <param name="Type">Its type, which stores files.</param>
internal sealed record UploadField(FormField Field, IFormBuilderFieldType Type);

/// <summary>A stored upload: what a submission posts for the field, and what the visitor sees of it (ADR-0087).</summary>
internal sealed record FileUploadResponseModel
{
    /// <summary>The token a submission posts in the field's value, which counts for 24 hours.</summary>
    public required string Token { get; init; }

    /// <summary>The file's name, cleaned: its last path segment, at most 255 characters with its extension.</summary>
    public required string Name { get; init; }

    /// <summary>Its size in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>The content type its kind has, which the server detected; <c>application/octet-stream</c> for other kinds.</summary>
    public required string ContentType { get; init; }

    /// <summary>The type's name only: the token and the visitor's file name stay out of logs.</summary>
    public override string ToString() => nameof(FileUploadResponseModel);
}
