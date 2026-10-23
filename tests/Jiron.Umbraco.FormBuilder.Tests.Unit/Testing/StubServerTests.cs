using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The HTTP stub, on a port the system chooses, so these tests run beside a host that uses 5560.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class StubServerTests
{
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    private StubServer _stub = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _stub = await StubServer.StartAsync(port: 0, _time);
        _client = new HttpClient { BaseAddress = _stub.BaseAddress };
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _stub.DisposeAsync();
    }

    [Test]
    public void StartAsync_PortZero_ListensOnLoopbackOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_stub.BaseAddress.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(_stub.BaseAddress.Port, Is.GreaterThan(0));
        });
    }

    [Test]
    public void StartAsync_PortInUse_SaysSo()
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => StubServer.StartAsync(_stub.BaseAddress.Port));

        Assert.That(exception!.Message, Does.Contain("in use"));
    }

    [Test]
    public async Task Webhook_Posted_IsRecordedAndAnswered()
    {
        using var content = new StringContent("""{"entry":"E-1"}""", Encoding.UTF8, "application/json");
        content.Headers.Add("X-Jfb-Signature", "sha256=JFB_FAKE-signature");

        using var response = await _client.PostAsync(new Uri("webhooks/entries?attempt=1", UriKind.Relative), content);
        var answer = await response.Content.ReadAsStringAsync();
        var call = _stub.CallsTo("/webhooks/entries").Single();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer, Is.EqualTo("""{"received":true}"""));
            Assert.That(call.Id, Is.EqualTo(1));
            Assert.That(call.Method, Is.EqualTo("POST"));
            Assert.That(call.Query, Is.EqualTo("?attempt=1"));
            Assert.That(call.Body, Is.EqualTo("""{"entry":"E-1"}"""));
            Assert.That(call.Header("x-jfb-signature"), Is.EqualTo("sha256=JFB_FAKE-signature"));
            Assert.That(call.ReceivedUtc, Is.EqualTo(_time.GetUtcNow()));
            Assert.That(call.Truncated, Is.False);
        });
    }

    [Test]
    public async Task UnknownPath_IsRecordedAndAnswered404()
    {
        using var response = await _client.GetAsync(new Uri("nothing/here", UriKind.Relative));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(_stub.Calls.Select(call => call.Path), Is.EqualTo(new[] { "/nothing/here" }));
        });
    }

    [Test]
    public async Task Answer_ForAPath_OverridesTheDefaultUntilReset()
    {
        _stub.Answer(new StubAnswer("POST", "/webhooks/failing", 503, "text/plain", "busy"));
        _stub.Answer(new StubAnswer("*", "/options/countries", Body: """[{"value":"ch","label":"Switzerland"}]"""));

        using var failing = await _client.PostAsync(new Uri("webhooks/failing", UriKind.Relative), null);
        using var options = await _client.GetAsync(new Uri("Options/Countries", UriKind.Relative));
        var list = await options.Content.ReadAsStringAsync();
        _stub.Reset();
        using var afterReset = await _client.PostAsync(new Uri("webhooks/failing", UriKind.Relative), null);

        Assert.Multiple(() =>
        {
            Assert.That(failing.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(options.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(list, Does.Contain("Switzerland"));
            Assert.That(afterReset.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_stub.Calls, Has.Count.EqualTo(1), "reset forgot the earlier calls");
        });
    }

    [Test]
    public void Answer_WithDelay_HoldsTheAnswerBack()
    {
        _stub.Answer(new StubAnswer("GET", "/slow", DelayMs: 2000));
        using var impatient = new HttpClient { BaseAddress = _stub.BaseAddress, Timeout = TimeSpan.FromMilliseconds(200) };

        Assert.ThrowsAsync<TaskCanceledException>(() => impatient.GetAsync(new Uri("slow", UriKind.Relative)));
    }

    [Test]
    public async Task ControlPaths_ScriptAndReadTheStubWithoutBeingRecorded()
    {
        using var put = await _client.PutAsJsonAsync(new Uri("_stub/answers", UriKind.Relative), new { method = "GET", path = "/options/sizes", status = 200, body = "[]" });
        using var options = await _client.GetAsync(new Uri("options/sizes", UriKind.Relative));
        var calls = await _client.GetFromJsonAsync<JsonElement>(new Uri("_stub/calls", UriKind.Relative));
        using var cleared = await _client.DeleteAsync(new Uri("_stub/calls", UriKind.Relative));
        using var answersCleared = await _client.DeleteAsync(new Uri("_stub/answers", UriKind.Relative));
        using var unanswered = await _client.GetAsync(new Uri("options/sizes", UriKind.Relative));
        using var badAnswer = await _client.PutAsJsonAsync(new Uri("_stub/answers", UriKind.Relative), new { method = "GET" });
        using var reset = await _client.PostAsync(new Uri("_stub/reset", UriKind.Relative), null);
        using var unknownControl = await _client.GetAsync(new Uri("_stub/other", UriKind.Relative));

        Assert.Multiple(() =>
        {
            Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(options.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(calls.GetArrayLength(), Is.EqualTo(1));
            Assert.That(calls[0].GetProperty("path").GetString(), Is.EqualTo("/options/sizes"));
            Assert.That(cleared.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(answersCleared.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unanswered.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(badAnswer.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(unknownControl.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(_stub.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task Body_LargerThanTheLimit_IsCutAndAnswered413()
    {
        using var content = new ByteArrayContent(new byte[StubServer.MaxBodyBytes + 10]);

        using var response = await _client.PostAsync(new Uri("webhooks/huge", UriKind.Relative), content);
        var call = _stub.Calls.Single();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(call.Truncated, Is.True);
            Assert.That(Encoding.UTF8.GetByteCount(call.Body), Is.EqualTo(StubServer.MaxBodyBytes));
        });
    }

    [TestCase("captcha/siteverify")]
    [TestCase("captcha/second/siteverify")]
    public async Task CaptchaVerify_PassToken_VerifiesAndIsRecorded(string path)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = "JFB_FAKE-secret", ["response"] = StubCaptcha.Pass });

        using var response = await _client.PostAsync(new Uri(path, UriKind.Relative), form);
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(answer.GetProperty("challenge_ts").GetString(), Is.EqualTo("2026-03-01T12:00:00Z"));
            Assert.That(answer.GetProperty("error-codes").GetArrayLength(), Is.Zero);
            Assert.That(_stub.CallsTo("/" + path).Single().Body, Does.Contain("JFB_FAKE-captcha-pass"));
        });
    }
}
