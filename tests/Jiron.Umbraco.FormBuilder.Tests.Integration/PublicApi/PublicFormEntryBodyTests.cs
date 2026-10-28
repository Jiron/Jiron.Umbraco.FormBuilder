using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The body of the entries endpoint (ADR-0005, ADR-0012, ADR-0088): a JSON object in UTF-8 of at most 1 MiB, read
/// strictly, so a lone surrogate, a member twice, an unknown member, an answer of the wrong shape and a value past its
/// limit are refused with where they are, never with <c>500</c>; another media type is refused, a declared length past
/// the limit before a byte is read, a body without one once it holds more, and a body the server itself refuses as its
/// kind of refusal says; none of them stores anything. Middleware of the test site counts the bytes the server reads of
/// each request, and refuses a body as the server would when a test asks.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormEntryBodyTests : PublicApiTestBase
{
    private static readonly char Backslash = (char)92;

    // The header by which a test asks the site to refuse the body at its end, with the status the server refuses it with.
    private const string RefuseHeader = "X-Jfb-Test-Refuse";

    private static readonly FormField Guests = new()
    {
        Key = Guid.NewGuid(),
        Type = "repeater",
        Alias = "guests",
        Label = new LocalizedText("Guests"),
        Fields = [new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "guestName", Label = new LocalizedText("Name") }],
    };

    private RequestBodies Bodies => GetRequiredService<RequestBodies>();

    [Test]
    public async Task ABodyThatCannotBeRead_IsRefusedWithWhereItIs_AndStoresNothing()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Settings = definition.Settings with { Developer = definition.Settings.Developer with { QueryValues = ["source"], ContextValues = ["campaign"] } },
        }, fields: [NameField, Guests]);
        var deep = string.Concat(Enumerable.Repeat("[", 70)) + string.Concat(Enumerable.Repeat("]", 70));
        var many = string.Join(",", Enumerable.Repeat("\"a\"", 21));
        var cases = new Dictionary<string, byte[]>
        {
            ["an escaped lone surrogate in an answer"] = Utf8($$$"""{"revision":1,"values":{"name":"{{{Backslash}}}uD800"}}"""),
            ["an escaped lone surrogate in a name"] = Utf8($$$"""{"revision":1,"values":{"{{{Backslash}}}uDC00":"Ada"}}"""),
            ["an escaped lone surrogate as the submission id"] = Utf8($$$"""{"revision":1,"submissionId":"{{{Backslash}}}uD800","values":{"name":"Ada"}}"""),
            ["an escaped lone surrogate in a query value"] = Utf8($$$"""{"revision":1,"values":{"name":"Ada"},"query":{"source":"{{{Backslash}}}uD800"}}"""),
            ["bytes that are not UTF-8"] = [.. Utf8("""{"revision":1,"values":{"name":"Ad"""), 0xFF, .. Utf8("\"}}")],
            ["no JSON"] = Utf8("""{"revision":1,"""),
            ["an array"] = Utf8("""[{"revision":1}]"""),
            ["more than 64 levels"] = Utf8($$$"""{"revision":1,"values":{"name":{{{deep}}}}}"""),
            ["no revision"] = Utf8("""{"values":{"name":"Ada"}}"""),
            ["a revision of 0"] = Utf8("""{"revision":0,"values":{"name":"Ada"}}"""),
            ["a revision as text"] = Utf8("""{"revision":"1","values":{"name":"Ada"}}"""),
            ["a member twice"] = Utf8("""{"revision":1,"revision":1,"values":{"name":"Ada"}}"""),
            ["an unknown member"] = Utf8("""{"revision":1,"uploads":[],"values":{"name":"Ada"}}"""),
            ["an unknown alias"] = Utf8("""{"revision":1,"values":{"name":"Ada","nope":"x"}}"""),
            ["rows for a field that holds none"] = Utf8("""{"revision":1,"values":{"name":[{"first":"Ada"}]}}"""),
            ["rows for an unknown alias"] = Utf8("""{"revision":1,"values":{"name":"Ada","nope":[{"a":"b"}]}}"""),
            ["an unknown alias in a row"] = Utf8("""{"revision":1,"values":{"name":"Ada","guests":[{"guestName":"Grace"},{"nope":"x"}]}}"""),
            ["an object as an answer"] = Utf8("""{"revision":1,"values":{"name":{"first":"Ada"}}}"""),
            ["a number in a list"] = Utf8("""{"revision":1,"values":{"name":["Ada",1]}}"""),
            ["a submission id that is no UUID"] = Utf8("""{"revision":1,"submissionId":"abc","values":{"name":"Ada"}}"""),
            ["a culture that is no culture"] = Utf8("""{"revision":1,"culture":"not a culture","values":{"name":"Ada"}}"""),
            ["a query value too long"] = Utf8($$$"""{"revision":1,"values":{"name":"Ada"},"query":{"source":"{{{new string('q', 2_001)}}}"}}"""),
            ["more than 20 query values"] = Utf8($$$"""{"revision":1,"values":{"name":"Ada"},"query":{"source":[{{{many}}}]}}"""),
            ["a context value too long"] = Utf8($$$"""{"revision":1,"values":{"name":"Ada"},"context":{"campaign":"{{{new string('c', 2_001)}}}"}}"""),
            ["a challenge too long"] = Utf8($$$"""{"revision":1,"values":{"name":"Ada"},"challenge":"{{{new string('x', 4_097)}}}"}"""),
        };

        var answers = new Dictionary<string, string>();
        foreach (var (what, body) in cases)
        {
            using var response = await PostAsync(key, new ByteArrayContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse("application/json") } });
            answers[what] = await AnswerAsync(response);
        }

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["an escaped lone surrogate in an answer"] = "400 invalid_request invalid_value at values.name",
                ["an escaped lone surrogate in a name"] = "400 invalid_request invalid_value at values",
                ["an escaped lone surrogate as the submission id"] = "400 invalid_request invalid_value at submissionId",
                ["an escaped lone surrogate in a query value"] = "400 invalid_request invalid_value at query.source",
                ["bytes that are not UTF-8"] = "400 invalid_request invalid_value at body",
                ["no JSON"] = "400 invalid_request invalid_value at body",
                ["an array"] = "400 invalid_request invalid_value at body",
                ["more than 64 levels"] = "400 invalid_request invalid_value at body",
                ["no revision"] = "400 invalid_request invalid_value at revision",
                ["a revision of 0"] = "400 invalid_request invalid_value at revision",
                ["a revision as text"] = "400 invalid_request invalid_value at revision",
                ["a member twice"] = "400 invalid_request invalid_value at revision",
                ["an unknown member"] = "400 invalid_request invalid_value at uploads",
                ["an unknown alias"] = "400 invalid_request invalid_value at values.nope",
                ["rows for a field that holds none"] = "400 invalid_request invalid_value at values.name",
                ["rows for an unknown alias"] = "400 invalid_request invalid_value at values.nope",
                ["an unknown alias in a row"] = "400 invalid_request invalid_value at values.guests[1].nope",
                ["an object as an answer"] = "400 invalid_request invalid_value at values.name",
                ["a number in a list"] = "400 invalid_request invalid_value at values.name",
                ["a submission id that is no UUID"] = "400 invalid_request invalid_value at submissionId",
                ["a culture that is no culture"] = "400 invalid_request invalid_value at culture",
                ["a query value too long"] = "400 invalid_request invalid_value at query.source",
                ["more than 20 query values"] = "400 invalid_request invalid_value at query.source",
                ["a context value too long"] = "400 invalid_request invalid_value at context.campaign",
                ["a challenge too long"] = "400 invalid_request invalid_value at challenge",
            }), string.Join("\n", answers));
            Assert.That(await StoredAsync(key), Is.Zero);
        });
    }

    [Test]
    public async Task AnotherMediaType_IsRefused_AndABodyPastTheLimit_IsCutOff_DeclaredOrNot()
    {
        var key = await PublishAsync();
        var ada = """{"revision":1,"values":{"name":"Ada"}}""";
        var answers = new Dictionary<string, string>();
        var read = new Dictionary<string, long>();
        async Task SendAsync(string what, HttpContent content)
        {
            using var response = await PostAsync(key, content);
            answers[what] = await AnswerAsync(response);
            read[what] = Bodies.Read;
        }

        await SendAsync("text/plain", new StringContent(ada, Encoding.UTF8, "text/plain"));
        await SendAsync("JSON in UTF-16", new ByteArrayContent(Encoding.Unicode.GetBytes(ada)) { Headers = { ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-16") } });
        await SendAsync("no media type", new ByteArrayContent(Utf8(ada)));
        await SendAsync("a declared length past the limit", new PaddedBody(ada, EntryRequests.MaxBodyBytes + 1, declared: true));
        await SendAsync("a body past the limit without a length", new PaddedBody(ada, EntryRequests.MaxBodyBytes + 1, declared: false));
        await SendAsync("a body of exactly the limit", new PaddedBody(ada, EntryRequests.MaxBodyBytes, declared: true));
        await SendAsync("a body of exactly the limit without a length", new PaddedBody(ada, EntryRequests.MaxBodyBytes, declared: false));

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["text/plain"] = "415 unsupported_media_type",
                ["JSON in UTF-16"] = "415 unsupported_media_type",
                ["no media type"] = "415 unsupported_media_type",
                ["a declared length past the limit"] = "413 payload_too_large, close",
                ["a body past the limit without a length"] = "413 payload_too_large, close",
                ["a body of exactly the limit"] = "200",
                ["a body of exactly the limit without a length"] = "200",
            }), string.Join("\n", answers));
            Assert.That(read["a declared length past the limit"], Is.Zero, "refused before a byte is read");
            Assert.That(read["a body past the limit without a length"], Is.GreaterThan(EntryRequests.MaxBodyBytes).And.LessThan(EntryRequests.MaxBodyBytes + 65_536), "read up to just past the limit");
            Assert.That(await StoredAsync(key), Is.EqualTo(2), "the two bodies within the limit");
        });
    }

    [TestCase(StatusCodes.Status413PayloadTooLarge, "413 payload_too_large, close", TestName = "TheServersOwnRefusalOfABody_AnswersByItsKind(past its size)")]
    [TestCase(StatusCodes.Status400BadRequest, "400 invalid_request invalid_value at body, close", TestName = "TheServersOwnRefusalOfABody_AnswersByItsKind(malformed)")]
    public async Task TheServersOwnRefusalOfABody_AnswersByItsKind(int status, string expected)
    {
        var key = await PublishAsync();
        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada"}}""", Encoding.UTF8, "application/json");

        using var response = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
        {
            request.Content = content;
            request.Headers.Add(RefuseHeader, status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(response), Is.EqualTo(expected));
            Assert.That(await StoredAsync(key), Is.Zero);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<RequestBodies>();
        builder.Services.AddSingleton<IStartupFilter, CountRequestBodies>();
        builder.Services.AddSingleton<IStartupFilter, RefuseBodies>();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // The status, the code, each error with where it is and whether the connection closes; "200" for an entry.
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return "200";
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        var close = response.Headers.ConnectionClose == true ? ", close" : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}{close}";
    }

    private async Task<HttpResponseMessage> PostAsync(Guid form, HttpContent content) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);

    private async Task<int> StoredAsync(Guid form)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        return (await GetRequiredService<IEntryRepository>().ListIdsAsync(row.Id, 10)).Count;
    }

    // Refuses a request's body once its bytes are read, as the server does past its own limit or for a malformed body,
    // when the request asks for it.
    private sealed class RefuseBodies : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (int.TryParse(context.Request.Headers[RefuseHeader], System.Globalization.CultureInfo.InvariantCulture, out var status))
                {
                    context.Request.Body = new Refusing(context.Request.Body, status);
                }

                await following(context);
            });
            next(app);
        };

        private sealed class Refusing(Stream inner, int status) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var read = await inner.ReadAsync(buffer, cancellationToken);
                return read > 0 ? read : throw new BadHttpRequestException("The server refuses the body.", status);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The site reads bodies asynchronously.");

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}

/// <summary>
/// A JSON body padded with spaces after its object to the given size, written in chunks as it is sent: with its length,
/// or chunked; for the tests of the public API's JSON bodies.
/// </summary>
internal sealed class PaddedBody : HttpContent
{
    private readonly byte[] _json;
    private readonly long _size;
    private readonly bool _declared;

    public PaddedBody(string json, long size, bool declared)
    {
        _json = Encoding.UTF8.GetBytes(json);
        _size = size;
        _declared = declared;
        Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await stream.WriteAsync(_json);
        var spaces = new byte[65_536];
        Array.Fill(spaces, (byte)' ');
        for (var written = (long)_json.Length; written < _size;)
        {
            var length = (int)Math.Min(spaces.Length, _size - written);
            await stream.WriteAsync(spaces.AsMemory(0, length));
            written += length;
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _size;
        return _declared;
    }
}
