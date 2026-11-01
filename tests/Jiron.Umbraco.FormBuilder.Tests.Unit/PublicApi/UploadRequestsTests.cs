using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// An upload's request as the endpoint reads it (ADR-0087), where a test server does not reach: the server's own limit
/// for the request becomes the field's while the server lets it change; a form that goes before the file is stored
/// answers as a missing one; the server's own refusal of the request's size answers as the field's limit does; a part
/// after the file that cannot be read leaves nothing; and the answer's model names only its type in logs.
/// </summary>
public sealed class UploadRequestsTests
{
    private const string Boundary = "jfb-upload-boundary";
    private const string Json = "200 application/json; charset=utf-8";
    private const int FormId = 7;

    private static readonly Guid FormKey = Guid.Parse("f0000000-0000-4000-8000-000000000087");
    private static readonly string Crlf = new([(char)13, (char)10]);
    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8];

    private IFormRepository _forms = null!;
    private Store _store = null!;
    private FileUploads _uploads = null!;
    private UploadRequests _requests = null!;
    private UploadField _field = null!;

    [SetUp]
    public void CreateTheEndpoint()
    {
        _forms = Substitute.For<IFormRepository>();
        _forms.GetAsync(FormKey, Arg.Any<CancellationToken>()).Returns(Form());
        var entries = Substitute.For<IEntryRepository>();
        entries.AddFileAsync(Arg.Any<NewEntryFile>(), Arg.Any<CancellationToken>()).Returns(call => Recorded(call.Arg<NewEntryFile>()));
        _store = new Store();
        _uploads = new FileUploads(TestFieldTypes.Registry, FieldHostLimits.Default, _store, _forms, entries, new FormBuilderFileScannerCollection(() => []), NullLogger<FileUploads>.Instance);
        var protection = new EphemeralDataProtectionProvider();
        _requests = new UploadRequests(_uploads, new UploadTokens(protection, new TestTimeProvider()), new ChallengeTokens(protection, new TestTimeProvider()));
        var field = new FormField { Key = Guid.Parse("30000000-0000-4000-8000-000000000087"), Type = "file", Alias = "upload", Label = new LocalizedText("Upload") };
        _field = new UploadField(field, _uploads.TypeOf(field)!);
    }

    [Test]
    public async Task TheServersOwnLimit_BecomesTheFields_WhileTheServerLetsItChange()
    {
        var writable = new BodySize();
        var fixedSize = new BodySize { IsReadOnly = true, MaxRequestBodySize = 30_000_000 };
        using var first = new MemoryStream(Body(Pdf));
        using var second = new MemoryStream(Body(Pdf));

        var withWritable = await UploadAsync(first, writable);
        var withFixed = await UploadAsync(second, fixedSize);

        Assert.Multiple(() =>
        {
            Assert.That(withWritable, Is.EqualTo(Json));
            Assert.That(writable.MaxRequestBodySize, Is.EqualTo(_uploads.RequestLimitOf(_field.Type, _field.Field)), "the field's limit and what a request carries besides");
            Assert.That(withFixed, Is.EqualTo(Json));
            Assert.That(fixedSize.MaxRequestBodySize, Is.EqualTo(30_000_000));
            Assert.That(_store.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AFormThatGoesBeforeTheFileIsStored_AnswersAsAMissingForm_AndStoresNothing()
    {
        _forms.GetAsync(FormKey, Arg.Any<CancellationToken>()).Returns((StoredForm?)null);
        using var body = new MemoryStream(Body(Pdf));

        var answer = await UploadAsync(body);

        Assert.Multiple(() =>
        {
            Assert.That(answer, Is.EqualTo("404 form_not_found, close"));
            Assert.That(_store.Count, Is.Zero);
        });
    }

    [TestCase(StatusCodes.Status413PayloadTooLarge, "413 payload_too_large fileSize at file, close", TestName = "TheServersOwnRefusal_AnswersByItsKind(past its size, as the field's limit)")]
    [TestCase(StatusCodes.Status400BadRequest, "400 invalid_request invalid_value at file, close", TestName = "TheServersOwnRefusal_AnswersByItsKind(malformed, as a request it cannot take)")]
    public async Task TheServersOwnRefusal_AnswersByItsKind(int status, string expected)
    {
        using var body = new Refusing(Body(Pdf, close: false), status);

        var answer = await UploadAsync(body);

        Assert.Multiple(() =>
        {
            Assert.That(answer, Is.EqualTo(expected));
            Assert.That(_store.Count, Is.Zero);
        });
    }

    [Test]
    public async Task APartAfterTheFileThatCannotBeRead_IsARequestItCannotTake_AndLeavesNothing()
    {
        var after = $"--{Boundary}{Crlf}{string.Join(Crlf, Enumerable.Range(1, UploadRequests.MaxPartHeaders + 1).Select(i => $"X-Part-{i}: {i}"))}{Crlf}{Crlf}x{Crlf}--{Boundary}--{Crlf}";
        using var body = new MemoryStream([.. Body(Pdf, close: false), .. Encoding.ASCII.GetBytes(after)]);

        var answer = await UploadAsync(body);

        Assert.Multiple(() =>
        {
            Assert.That(answer, Is.EqualTo("400 invalid_request invalid_value at file, close"));
            Assert.That(_store.Count, Is.Zero, "the file's bytes deleted again");
        });
    }

    [Test]
    public void TheAnswersModel_NamesOnlyItsType()
    {
        var model = new FileUploadResponseModel { Token = "a token", Name = "Secret plans.pdf", Size = 1, ContentType = "application/pdf" };

        Assert.That(model.ToString(), Is.EqualTo(nameof(FileUploadResponseModel)));
    }

    // A multipart body of one file, closed by its boundary unless told otherwise.
    private static byte[] Body(byte[] file, bool close = true) =>
    [
        .. Encoding.ASCII.GetBytes($"--{Boundary}{Crlf}Content-Disposition: form-data; name=file; filename=report.pdf{Crlf}{Crlf}"),
        .. file,
        .. Encoding.ASCII.GetBytes(Crlf),
        .. close ? Encoding.ASCII.GetBytes($"--{Boundary}--{Crlf}") : [],
    ];

    private static StoredForm Form() => new(
        FormId, FormKey, null, "contact", "Contact", 12, 13, 1, 1, 0, null, false, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);

    private static StoredEntryFile Recorded(NewEntryFile file) => new(
        1, file.Key, null, file.FormId, file.FieldKey, file.OriginalName, file.StoredName, file.ContentType, file.SizeBytes, file.FileHash, file.ScanState, DateTimeOffset.UnixEpoch);

    // The status, the code, the errors and whether the connection closes, such as "404 form_not_found, close".
    private async Task<string> UploadAsync(Stream body, IHttpMaxRequestBodySizeFeature? size = null)
    {
        // A server key's request: these tests are of the file, and the challenge's are PublicFormChallengeTests.
        var http = new DefaultHttpContext();
        http.Features.Set(new ServerKeyRequest("test"));
        http.Request.Protocol = "HTTP/1.1";
        http.Request.Method = HttpMethods.Post;
        http.Request.ContentType = $"multipart/form-data; boundary={Boundary}";
        http.Request.Body = body;
        if (size is not null)
        {
            http.Features.Set(size);
        }

        var result = await _requests.UploadAsync(http, FormKey, _field, null, CancellationToken.None);
        var close = http.Response.Headers.Connection == "close" ? ", close" : string.Empty;
        return result switch
        {
            FormBuilderProblemResult problem => $"{problem.StatusCode} {problem.Problem.Extensions["code"]}{Errors(problem)}{close}",
            FileContentResult answer => $"200 {answer.ContentType}{close}",
            _ => result.GetType().Name,
        };
    }

    private static string Errors(FormBuilderProblemResult problem) =>
        problem.Problem.Extensions.TryGetValue("errors", out var errors) && errors is IEnumerable<FormBuilderProblemError> list
            ? " " + string.Join("; ", list.Select(error => $"{error.Code} at {error.Path}"))
            : string.Empty;

    // The server's limit for a request's body, which it lets change or not.
    private sealed class BodySize : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }

        public long? MaxRequestBodySize { get; set; }
    }

    // A request body of these bytes, after which the server refuses to read on, as Kestrel does past its own limit.
    private sealed class Refusing(byte[] bytes, int status) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : throw new BadHttpRequestException("The server refuses the request.", status);
        }
    }

    // A store that keeps whole files in memory.
    private sealed class Store : IFormBuilderFileStore
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public int Count => _files.Count;

        public async Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _files.Add(id, copy.ToArray());
        }

        public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(_files.TryGetValue(id, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _files.Remove(id);
            return Task.CompletedTask;
        }
    }
}
