using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Endpoints;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The test-only endpoints answer only when the host turned them on, runs in Development, and the request comes from
/// this machine (ADR-0011, ADR-0029). A request the gate refuses reaches the site as if they did not exist.
/// </summary>
[TestFixture]
public sealed class TestEndpointsGateTests
{
    private const int SiteAnswered = 299;

    [TestCase("127.0.0.1", true)]
    [TestCase("127.8.9.10", true)]
    [TestCase("::1", true)]
    [TestCase("::ffff:127.0.0.1", true)]
    [TestCase("10.0.0.5", false)]
    [TestCase("192.168.1.20", false)]
    [TestCase("::ffff:192.168.1.20", false)]
    [TestCase("2001:db8::1", false)]
    public void IsLocal_Addresses_OnlyThisMachine(string address, bool local)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.That(TestEndpointsGate.IsLocal(context), Is.EqualTo(local));
    }

    [Test]
    public void IsLocal_NoAddress_IsNotLocal()
    {
        Assert.That(TestEndpointsGate.IsLocal(new DefaultHttpContext()), Is.False);
    }

    [TestCase("/jfb-testing", true)]
    [TestCase("/jfb-testing/status", true)]
    [TestCase("/JFB-Testing/Clock", true)]
    [TestCase("/jfb-testingx", false)]
    [TestCase("/umbraco/jfb-testing", false)]
    [TestCase("/", false)]
    public void IsTestPath_Paths_AreSortedAsExpected(string path, bool test)
    {
        Assert.That(TestEndpointsGate.IsTestPath(new PathString(path)), Is.EqualTo(test));
    }

    [TestCase(true, "Development", "127.0.0.1", 200)]
    [TestCase(true, "Production", "127.0.0.1", SiteAnswered)]
    [TestCase(true, "Staging", "127.0.0.1", SiteAnswered)]
    [TestCase(true, "Tests", "127.0.0.1", SiteAnswered)]
    [TestCase(false, "Development", "127.0.0.1", SiteAnswered)]
    [TestCase(true, "Development", "10.0.0.5", SiteAnswered)]
    public async Task Status_ThroughTheGate_AnswersOnlyWhenEveryConditionHolds(bool switchedOn, string environment, string remote, int expected)
    {
        var context = await SendAsync(Pipeline(switchedOn, environment), "GET", "/jfb-testing/status", remote);

        Assert.That(context.Response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task Status_OnAnOpenGate_SaysItIsATestHost()
    {
        var context = await SendAsync(Pipeline(switchedOn: true, "Development"), "GET", "/jfb-testing/status");
        using var body = JsonDocument.Parse(ResponseText(context));

        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("testHost").GetBoolean(), Is.True);
            Assert.That(body.RootElement.GetProperty("clock").GetBoolean(), Is.False);
            Assert.That(body.RootElement.GetProperty("stub").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task OtherPaths_OnAnOpenGate_ReachTheSite()
    {
        var pipeline = Pipeline(switchedOn: true, "Development");

        var site = await SendAsync(pipeline, "GET", "/about");
        var unknown = await SendAsync(pipeline, "GET", "/jfb-testing/unknown");

        Assert.Multiple(() =>
        {
            Assert.That(site.Response.StatusCode, Is.EqualTo(SiteAnswered));
            Assert.That(unknown.Response.StatusCode, Is.EqualTo(404));
        });
    }

    [Test]
    public async Task Clock_WithoutATestClock_Answers404()
    {
        var context = await SendAsync(Pipeline(switchedOn: true, "Development"), "GET", "/jfb-testing/clock");

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Clock_WithATestClock_IsReadMovedAndReset()
    {
        var clock = new TestClock();
        var pipeline = Pipeline(switchedOn: true, "Development", clock);

        var moved = await SendAsync(pipeline, "POST", "/jfb-testing/clock", body: """{"advanceSeconds": 3600}""");
        var offsetAfterMove = clock.Offset;
        var set = await SendAsync(pipeline, "POST", "/jfb-testing/clock", body: """{"utc": "2030-01-01T00:00:00Z"}""");
        var timeAfterSet = clock.GetUtcNow();
        var reset = await SendAsync(pipeline, "DELETE", "/jfb-testing/clock");
        var put = await SendAsync(pipeline, "PUT", "/jfb-testing/clock");

        Assert.Multiple(() =>
        {
            Assert.That(moved.Response.StatusCode, Is.EqualTo(200));
            Assert.That(offsetAfterMove, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(set.Response.StatusCode, Is.EqualTo(200));
            Assert.That(timeAfterSet, Is.EqualTo(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)).Within(TimeSpan.FromSeconds(5)));
            Assert.That(reset.Response.StatusCode, Is.EqualTo(200));
            Assert.That(clock.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(put.Response.StatusCode, Is.EqualTo(405));
        });
    }

    /// <summary>A pipeline of the startup filter in front of a "site" that answers <see cref="SiteAnswered"/>.</summary>
    private static RequestDelegate Pipeline(bool switchedOn, string environment, TestClock? clock = null)
    {
        var services = new ServiceCollection();
        if (clock is not null)
        {
            services.AddSingleton(clock);
            services.AddSingleton<TimeProvider>(clock);
        }

        var provider = services.BuildServiceProvider();
        var filter = new TestEndpointsStartupFilter(
            Options.Create(new FormBuilderTestingOptions { Endpoints = switchedOn }),
            new FakeEnvironment(environment));
        var app = new ApplicationBuilder(provider);
        filter.Configure(site => site.Run(context =>
        {
            context.Response.StatusCode = SiteAnswered;
            return Task.CompletedTask;
        }))(app);
        var built = app.Build();
        return context =>
        {
            context.RequestServices = provider;
            return built(context);
        };
    }

    private static async Task<HttpContext> SendAsync(RequestDelegate pipeline, string method, string path, string remote = "127.0.0.1", string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        context.Response.Body = new MemoryStream();
        await pipeline(context);
        return context;
    }

    private static string ResponseText(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return reader.ReadToEnd();
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
