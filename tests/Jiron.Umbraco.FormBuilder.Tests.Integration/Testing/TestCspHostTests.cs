using System.Net;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.TestServerTest;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// The strict Content Security Policy of a test host with <c>JironFormBuilderTesting:Csp</c> on, in Development (ROADMAP
/// 6.4k): every response says <c>script-src 'self'</c>; a request with <c>jfb-test-nonce</c> gets a nonce of its own,
/// which the site's <see cref="IFormBuilderCspNonceProvider"/> gives, and is never stored.
/// <see cref="TestCspProductionHostTests"/> is the same host outside Development, which sets no policy.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed partial class TestCspHostTests : UmbracoTestServerTestBase
{
    [SetUp]
    public override void Setup()
    {
        TestCspHost.SwitchOn(InMemoryConfiguration);
        base.Setup();
    }

    [Test]
    public async Task Page_WithoutANonce_AllowsTheSitesOwnScriptsAndNoNonce()
    {
        var context = await TestCspHost.GetAsync(Factory.Server, string.Empty);
        var nonce = await TestCspHost.BodyAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(Policy(context), Does.Contain("script-src 'self';"));
            Assert.That(Policy(context), Does.Contain("style-src 'self';").And.Contain("object-src 'none';"));
            Assert.That(nonce, Is.Empty, "the provider gives no nonce");
            Assert.That(context.Response.Headers.CacheControl.ToString(), Does.Not.Contain("no-store"));
        });
    }

    [TestCase("nonce", false)]
    [TestCase("strict-dynamic", true)]
    public async Task Page_AskingForANonce_AllowsItsOwnNonceOnlyAndIsNeverStored(string asked, bool strictDynamic)
    {
        var context = await TestCspHost.GetAsync(Factory.Server, "?jfb-test-nonce=" + asked);
        var nonce = await TestCspHost.BodyAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(nonce, Does.Match("^[A-Za-z0-9+/]{22}==$"), "16 random bytes, as base64");
            Assert.That(ScriptSources().Match(Policy(context)).Groups["sources"].Value, Is.EqualTo("'nonce-" + nonce + "'" + (strictDynamic ? " 'strict-dynamic'" : string.Empty)));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        });
    }

    [Test]
    public async Task Page_AskingForANonceTwice_GetsANewOneEachTime()
    {
        var first = await TestCspHost.BodyAsync(await TestCspHost.GetAsync(Factory.Server, "?jfb-test-nonce=nonce"));
        var second = await TestCspHost.BodyAsync(await TestCspHost.GetAsync(Factory.Server, "?jfb-test-nonce=nonce"));

        Assert.That(first, Is.Not.EqualTo(second));
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        TestCspHost.AddProbe(services);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder) => TestEndpointsHost.UseDevelopment(builder);

    private static string Policy(HttpContext context) => context.Response.Headers.ContentSecurityPolicy.ToString();

    [GeneratedRegex("script-src (?<sources>[^;]+);", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ScriptSources();
}

/// <summary>The control for <see cref="TestCspHostTests"/>: the same switch outside Development sets no policy and no nonce.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class TestCspProductionHostTests : UmbracoTestServerTestBase
{
    [SetUp]
    public override void Setup()
    {
        TestCspHost.SwitchOn(InMemoryConfiguration);
        base.Setup();
    }

    [Test]
    public async Task Page_OnAHostOutsideDevelopment_GetsNoPolicyAndNoNonce()
    {
        var context = await TestCspHost.GetAsync(Factory.Server, "?jfb-test-nonce=nonce");
        var nonce = await TestCspHost.BodyAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(Services.GetRequiredService<IHostEnvironment>().IsDevelopment(), Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.Headers.ContentSecurityPolicy.ToString(), Is.Empty);
            Assert.That(nonce, Is.Empty);
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        TestCspHost.AddProbe(services);
    }
}

/// <summary>What both fixtures share.</summary>
internal static class TestCspHost
{
    /// <summary>The path the probe answers, behind the policy's middleware.</summary>
    public const string ProbePath = "/csp-probe";

    /// <summary>Switches the strict policy on, as the strict-site spec's host does.</summary>
    public static void SwitchOn(IDictionary<string, string> configuration) =>
        configuration[FormBuilderTestingOptions.SectionName + ":Csp"] = "true";

    /// <summary>
    /// Answers <see cref="ProbePath"/> with the nonce the site's provider gives the request, or nothing. It runs after the
    /// Testing composer's filter, so behind the policy's middleware, and before Umbraco's routing, which answers every page
    /// with its maintenance page in the test host's start-up window.
    /// </summary>
    public static void AddProbe(IServiceCollection services) => services.AddTransient<IStartupFilter, Probe>();

    /// <summary>A <c>GET</c> of the probe from this machine, with the given query.</summary>
    public static Task<HttpContext> GetAsync(TestServer server, string query) => server.SendAsync(request =>
    {
        request.Request.Method = HttpMethods.Get;
        request.Request.Path = ProbePath;
        request.Request.QueryString = new QueryString(query);
        request.Connection.RemoteIpAddress = IPAddress.Loopback;
    });

    /// <summary>The probe's answer: the request's nonce, or the empty text.</summary>
    public static async Task<string> BodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private sealed class Probe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Path != ProbePath)
                {
                    await following(context);
                    return;
                }

                var nonce = context.RequestServices.GetService<IFormBuilderCspNonceProvider>()?.GetNonce(context);
                await context.Response.WriteAsync(nonce ?? string.Empty);
            });
            next(app);
        };
    }
}
