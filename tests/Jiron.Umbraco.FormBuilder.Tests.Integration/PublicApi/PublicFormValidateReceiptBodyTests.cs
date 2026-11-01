using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The bodies of the validate and receipt endpoints (ADR-0005, ADR-0012, ADR-0089): JSON objects in UTF-8, of at most
/// 1 MiB for a page and 16 KiB for a receipt, read as strictly as a submission's, so a lone surrogate, an unknown member,
/// a member of the other endpoints and a missing one are refused with where they are, never with <c>500</c>; another
/// media type is refused, a declared length past the limit before a byte is read, and a body without one once it holds
/// more. Middleware of the test site counts the bytes the server reads of each request.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormValidateReceiptBodyTests : PublicApiTestBase
{
    /// <summary>A fixture of bodies and files, for which the challenge is beside the point: PublicFormChallengeTests holds its tests.</summary>
    public PublicFormValidateReceiptBodyTests() => SendsWithServerKey = true;

    private static readonly char Backslash = (char)92;

    private RequestBodies Bodies => GetRequiredService<RequestBodies>();

    [Test]
    public async Task ABodyThatCannotBeRead_IsRefusedWithWhereItIs()
    {
        var key = await PublishAsync();
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;
        var cases = new (string What, string Endpoint, byte[] Body)[]
        {
            ("a page: an escaped lone surrogate in an answer", "validate", Utf8($$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"{{{Backslash}}}uD800"}}""")),
            ("a page: an escaped lone surrogate as its key", "validate", Utf8($$$"""{"revision":1,"pageKey":"{{{Backslash}}}uD800"}""")),
            ("a page: bytes that are not UTF-8", "validate", [.. Utf8($$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ad"""), 0xFF, .. Utf8("\"}}")]),
            ("a page: no page key", "validate", Utf8("""{"revision":1,"values":{"name":"Ada"}}""")),
            ("a page: a page key that is no UUID", "validate", Utf8("""{"revision":1,"pageKey":"first"}""")),
            ("a page: a submission id", "validate", Utf8($$$"""{"revision":1,"pageKey":"{{{page}}}","submissionId":"{{{Guid.NewGuid()}}}"}""")),
            ("a page: a captcha token", "validate", Utf8($$$"""{"revision":1,"pageKey":"{{{page}}}","captchaToken":"JFB_FAKE token"}""")),
            ("a page: no revision", "validate", Utf8($$$"""{"pageKey":"{{{page}}}"}""")),
            ("a receipt: an escaped lone surrogate in the challenge", "receipt", Utf8($$$"""{"submissionId":"{{{Guid.NewGuid()}}}","challenge":"{{{Backslash}}}uDC00"}""")),
            ("a receipt: an escaped lone surrogate as the id", "receipt", Utf8($$$"""{"submissionId":"{{{Backslash}}}uDC00"}""")),
            ("a receipt: no submission id", "receipt", Utf8("""{"challenge":"a challenge"}""")),
            ("a receipt: a submission id of null", "receipt", Utf8("""{"submissionId":null}""")),
            ("a receipt: answers", "receipt", Utf8($$$"""{"submissionId":"{{{Guid.NewGuid()}}}","values":{"name":"Ada"}}""")),
            ("a receipt: a challenge too long", "receipt", Utf8($$$"""{"submissionId":"{{{Guid.NewGuid()}}}","challenge":"{{{new string('x', 4_097)}}}"}""")),
            ("a receipt: an array", "receipt", Utf8("""[]""")),
            ("a receipt: no JSON", "receipt", Utf8("""{"submissionId":""")),
        };

        var answers = new Dictionary<string, string>();
        foreach (var (what, endpoint, body) in cases)
        {
            using var response = await PostAsync(key, endpoint, new ByteArrayContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse("application/json") } });
            answers[what] = await AnswerAsync(response);
        }

        Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
        {
            ["a page: an escaped lone surrogate in an answer"] = "400 invalid_request invalid_value at values.name",
            ["a page: an escaped lone surrogate as its key"] = "400 invalid_request invalid_value at pageKey",
            ["a page: bytes that are not UTF-8"] = "400 invalid_request invalid_value at body",
            ["a page: no page key"] = "400 invalid_request invalid_value at pageKey",
            ["a page: a page key that is no UUID"] = "400 invalid_request invalid_value at pageKey",
            ["a page: a submission id"] = "400 invalid_request invalid_value at submissionId",
            ["a page: a captcha token"] = "400 invalid_request invalid_value at captchaToken",
            ["a page: no revision"] = "400 invalid_request invalid_value at revision",
            ["a receipt: an escaped lone surrogate in the challenge"] = "400 invalid_request invalid_value at challenge",
            ["a receipt: an escaped lone surrogate as the id"] = "400 invalid_request invalid_value at submissionId",
            ["a receipt: no submission id"] = "400 invalid_request invalid_value at submissionId",
            ["a receipt: a submission id of null"] = "400 invalid_request invalid_value at submissionId",
            ["a receipt: answers"] = "400 invalid_request invalid_value at values",
            ["a receipt: a challenge too long"] = "400 invalid_request invalid_value at challenge",
            ["a receipt: an array"] = "400 invalid_request invalid_value at body",
            ["a receipt: no JSON"] = "400 invalid_request invalid_value at body",
        }), string.Join("\n", answers));
    }

    [TestCase("validate", EntryRequests.MaxBodyBytes, "204")]
    [TestCase("receipt", EntryRequests.MaxReceiptBodyBytes, "200")]
    public async Task AnotherMediaType_IsRefused_AndABodyPastTheLimit_IsCutOff_DeclaredOrNot(string endpoint, int limit, string within)
    {
        var key = await PublishAsync();
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;
        var submissionId = Guid.NewGuid();
        using (var submitted = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent($$$"""{"submissionId":"{{{submissionId}}}","revision":1,"values":{"name":"Ada"}}""", Encoding.UTF8, "application/json")))
        {
            Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await submitted.Content.ReadAsStringAsync());
        }

        var json = endpoint == "validate" ? $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada"}}""" : $$$"""{"submissionId":"{{{submissionId}}}"}""";
        var answers = new Dictionary<string, string>();
        var read = new Dictionary<string, long>();
        async Task CallAsync(string what, HttpContent content)
        {
            using var response = await PostAsync(key, endpoint, content);
            answers[what] = await AnswerAsync(response);
            read[what] = Bodies.Read;
        }

        await CallAsync("text/plain", new StringContent(json, Encoding.UTF8, "text/plain"));
        await CallAsync("JSON in UTF-16", new ByteArrayContent(Encoding.Unicode.GetBytes(json)) { Headers = { ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-16") } });
        await CallAsync("a declared length past the limit", new PaddedBody(json, limit + 1, declared: true));
        await CallAsync("a body past the limit without a length", new PaddedBody(json, limit + 1, declared: false));
        await CallAsync("a body of exactly the limit", new PaddedBody(json, limit, declared: true));
        await CallAsync("a body of exactly the limit without a length", new PaddedBody(json, limit, declared: false));

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["text/plain"] = "415 unsupported_media_type",
                ["JSON in UTF-16"] = "415 unsupported_media_type",
                ["a declared length past the limit"] = "413 payload_too_large, close",
                ["a body past the limit without a length"] = "413 payload_too_large, close",
                ["a body of exactly the limit"] = within,
                ["a body of exactly the limit without a length"] = within,
            }), string.Join("\n", answers));
            Assert.That(read["a declared length past the limit"], Is.Zero, "refused before a byte is read");
            Assert.That(read["a body past the limit without a length"], Is.GreaterThan(limit).And.LessThan(limit + 65_536), "read up to just past the limit");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<RequestBodies>();
        builder.Services.AddSingleton<IStartupFilter, CountRequestBodies>();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // The status, the code, each error with where it is and whether the connection closes; "200" for a receipt and "204"
    // for a page that passes.
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
        {
            return ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        var close = response.Headers.ConnectionClose == true ? ", close" : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}{close}";
    }

    private async Task<HttpResponseMessage> PostAsync(Guid form, string endpoint, HttpContent content) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/{endpoint}", request => request.Content = content);
}
