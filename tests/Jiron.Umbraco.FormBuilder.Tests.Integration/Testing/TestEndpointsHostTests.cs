using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.TestServerTest;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// A real Umbraco site with the test-only endpoints switched on, but outside the Development environment: it has no
/// <c>/jfb-testing/</c> endpoint, even for a request from this machine (ROADMAP 3.4a, ADR-0029).
/// <see cref="TestEndpointsDevelopmentHostTests"/> is the same host in Development, where they answer, so this fixture
/// cannot pass because the endpoints are missing altogether.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class TestEndpointsHostTests : UmbracoTestServerTestBase
{
    [SetUp]
    public override void Setup()
    {
        TestEndpointsHost.SwitchOn(InMemoryConfiguration);
        base.Setup();
    }

    [TestCase("/jfb-testing/status")]
    [TestCase("/jfb-testing/clock")]
    public async Task TestEndpoint_OnAHostOutsideDevelopment_IsNotThere(string path)
    {
        var environment = Services.GetRequiredService<IHostEnvironment>().EnvironmentName;

        var context = await TestEndpointsHost.FromAsync(Factory.Server, path, IPAddress.Loopback);

        Assert.Multiple(() =>
        {
            Assert.That(environment, Is.Not.EqualTo(Environments.Development));
            Assert.That(context.Response.StatusCode, Is.EqualTo(TestEndpointsHost.ReachedTheSite));
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        TestEndpointsHost.AddSiteStandIn(services);
    }
}

/// <summary>The control for <see cref="TestEndpointsHostTests"/>: the same real site, in Development, answers them.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class TestEndpointsDevelopmentHostTests : UmbracoTestServerTestBase
{
    [SetUp]
    public override void Setup()
    {
        TestEndpointsHost.SwitchOn(InMemoryConfiguration);
        base.Setup();
    }

    [Test]
    public async Task Status_FromThisMachine_SaysItIsATestHostWithAClock()
    {
        var context = await TestEndpointsHost.FromAsync(Factory.Server, "/jfb-testing/status", IPAddress.Loopback);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        using var body = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Multiple(() =>
        {
            Assert.That(Services.GetRequiredService<IHostEnvironment>().IsDevelopment(), Is.True);
            Assert.That(body.RootElement.GetProperty("testHost").GetBoolean(), Is.True);
            Assert.That(body.RootElement.GetProperty("clock").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task Status_FromAnotherMachine_IsNotThere()
    {
        var context = await TestEndpointsHost.FromAsync(Factory.Server, "/jfb-testing/status", IPAddress.Parse("192.0.2.10"));

        Assert.That(context.Response.StatusCode, Is.EqualTo(TestEndpointsHost.ReachedTheSite));
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        TestEndpointsHost.AddSiteStandIn(services);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder) => TestEndpointsHost.UseDevelopment(builder);
}

/// <summary>What both fixtures share.</summary>
internal static class TestEndpointsHost
{
    /// <summary>The status of <see cref="AddSiteStandIn"/>: the request went on to the site.</summary>
    public const int ReachedTheSite = 299;

    /// <summary>Switches the test-only endpoints and the clock on, as the browser tests' host does.</summary>
    public static void SwitchOn(IDictionary<string, string> configuration)
    {
        configuration[FormBuilderTestingOptions.SectionName + ":Endpoints"] = "true";
        configuration[FormBuilderTestingOptions.SectionName + ":Clock"] = "true";
    }

    /// <summary>
    /// Answers every <c>/jfb-testing/</c> request that gets past the test-only endpoints with <see cref="ReachedTheSite"/>.
    /// It sits right behind them, because Umbraco 17's test host keeps its front-end routing in the start-up window and
    /// answers every page request with its maintenance page; a refused request would otherwise end there.
    /// </summary>
    public static void AddSiteStandIn(IServiceCollection services) => services.AddTransient<IStartupFilter, SiteStandIn>();

    /// <summary>
    /// Runs the site in the Development environment, which the gate reads: registered inside Umbraco's own service
    /// set-up, after the host's. The web host registers its IWebHostEnvironment (Umbraco's "Tests") again later, so
    /// that one is left alone.
    /// </summary>
    public static void UseDevelopment(IUmbracoBuilder builder) => builder.Services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());

    /// <summary>A <c>GET</c> straight into the test server, from the given remote address.</summary>
    public static async Task<HttpContext> FromAsync(TestServer server, string path, IPAddress remote)
    {
        var context = await server.SendAsync(request =>
        {
            request.Request.Method = HttpMethods.Get;
            request.Request.Path = path;
            request.Connection.RemoteIpAddress = remote;
        });
        return context;
    }

    /// <summary>A <c>POST</c> of a JSON body straight into the test server, from this machine.</summary>
    public static async Task<HttpContext> PostAsync(TestServer server, string path, string json)
    {
        var context = await server.SendAsync(request =>
        {
            request.Request.Method = HttpMethods.Post;
            request.Request.Path = path;
            request.Request.ContentType = "application/json";
            request.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            request.Connection.RemoteIpAddress = IPAddress.Loopback;
        });
        return context;
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Jiron.Umbraco.FormBuilder.Tests.Integration";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Registered after the Testing composer's filter, so its middleware runs after the test-only endpoints.</summary>
    private sealed class SiteStandIn : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Path.StartsWithSegments("/jfb-testing", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = ReachedTheSite;
                    return;
                }

                await following(context);
            });
            next(app);
        };
    }
}
