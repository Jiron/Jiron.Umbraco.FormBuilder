using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;

/// <summary>
/// URL lists in a real site in Development with the Testing assembly's stub (ADR-0076): the host configuration's hosts
/// bind, the Testing assembly's transport reaches the stub on loopback at its port, a library list saved through the
/// list service offers what the stub answers, kept for its time, and a host the configuration does not name is refused.
/// <see cref="UrlOptionListProductionTests"/> is the same site outside Development, which refuses loopback.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class UrlOptionListTests : FormBuilderTestServerTestBase
{
    private int _port;

    [Test]
    public async Task AUrlList_OffersWhatTheStubAnswers_KeptForItsTime()
    {
        var stub = Services.GetRequiredService<StubHostedService>().Server;
        Assert.That(stub, Is.Not.Null, "the stub runs in Development");
        stub!.Answer(new StubAnswer("GET", "/options.json", Body: """{"items":[{"id":"ch","name":"Switzerland"},{"id":"li","name":"Liechtenstein"}]}"""));
        var lists = Services.GetRequiredService<OptionListService>();
        var options = Services.GetRequiredService<IFormBuilderOptionService>();

        var created = await lists.CreateAsync(UrlOptionListHost.ListKey, UrlOptionListHost.List(_port, "/options.json"));
        var resolved = await options.GetListAsync(UrlOptionListHost.ListKey, new FormBuilderOptionContext("en"));
        var kept = await options.GetListAsync(UrlOptionListHost.ListKey, new FormBuilderOptionContext("de"));

        var call = stub.CallsTo("/options.json");
        Assert.Multiple(() =>
        {
            Assert.That(Services.GetRequiredService<AppCaches>().RuntimeCache, Is.InstanceOf<ObjectCacheAppCache>(), "the test of the keep time uses a real cache");
            Assert.That(created.Problems, Is.Empty);
            Assert.That(resolved.Options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("ch", "Switzerland"), ("li", "Liechtenstein") }));
            Assert.That(kept.Options.Select(option => option.Value), Is.EqualTo(new[] { "ch", "li" }));
            Assert.That(call, Has.Count.EqualTo(1), "kept for the list's 300 seconds");
            Assert.That(call[0].Header("Accept"), Is.EqualTo("application/json"));
            Assert.That(call[0].Header("User-Agent"), Is.EqualTo($"JironFormBuilder/{FormBuilderVersion.Package}"));
        });
    }

    [Test]
    public async Task AUrlList_OnAHostTheConfigurationDoesNotName_IsRefused_AndNothingIsCalled()
    {
        var stub = Services.GetRequiredService<StubHostedService>().Server!;
        var lists = Services.GetRequiredService<OptionListService>();

        var refused = await lists.CreateAsync(UrlOptionListHost.ListKey, UrlOptionListHost.List(_port, "/options.json") with { Config = $$"""{"url":"http://localhost:{{_port}}/options.json"}""" });

        Assert.Multiple(() =>
        {
            Assert.That(refused.List, Is.Null);
            Assert.That(refused.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.SettingInvalid, "$.config.url", "The site does not call the host 'localhost': its host configuration names the hosts it calls in JironFormBuilder:Webhooks:AllowedHosts.") }));
            Assert.That(stub.Calls, Is.Empty);
        });
    }

    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        _port = UrlOptionListHost.FreePort();
        UrlOptionListHost.Configure(configuration, _port);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        TestEndpointsHost.UseDevelopment(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
    }
}

/// <summary>
/// The same site outside Development (ADR-0076): the Testing assembly leaves the site's own transport in place, which
/// refuses loopback, so a list on the stub's address offers nothing and the stub, which runs, is never called.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class UrlOptionListProductionTests : FormBuilderTestServerTestBase
{
    private StubServer _stub = null!;

    [OneTimeSetUp]
    public async Task StartStubAsync() => _stub = await StubServer.StartAsync(port: 0);

    [OneTimeTearDown]
    public async Task StopStubAsync() => await _stub.DisposeAsync();

    [Test]
    public async Task TheSitesTransport_RefusesLoopback_SoTheListOffersNothing_AndTheStubIsNeverCalled()
    {
        var lists = Services.GetRequiredService<OptionListService>();
        var port = _stub.BaseAddress.Port;

        var created = await lists.CreateAsync(UrlOptionListHost.ListKey, UrlOptionListHost.List(port, "/options.json"));
        var resolved = await Services.GetRequiredService<IFormBuilderOptionService>().GetListAsync(UrlOptionListHost.ListKey, new FormBuilderOptionContext("en"));
        var direct = Assert.ThrowsAsync<FormBuilderOutboundException>(() => Services.GetRequiredService<OutboundHttp>().GetAsync(new Uri($"http://127.0.0.1:{port}/options.json"), "application/json", new OutboundLimits(TimeSpan.FromSeconds(4), 1024), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(Services.GetRequiredService<IHostEnvironment>().IsDevelopment(), Is.False);
            Assert.That(Services.GetRequiredService<StubHostedService>().Server, Is.Null, "the Testing assembly's stub runs in Development only");
            Assert.That(Services.GetRequiredService<IFormBuilderHttpTransport>(), Is.TypeOf<FormBuilderHttpTransport>());
            Assert.That(created.Problems, Is.Empty, "the host configuration names the address's host");
            Assert.That(resolved.IsAvailable, Is.False);
            Assert.That(direct?.Reason, Is.EqualTo(OutboundFailure.AddressNotPublic));
            Assert.That(_stub.Calls, Is.Empty);
        });
    }

    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        _stub.Reset();
        _stub.Answer(new StubAnswer("GET", "/options.json", Body: """{"items":[{"id":"ch","name":"Switzerland"}]}"""));
        UrlOptionListHost.Configure(configuration, _stub.BaseAddress.Port);
    }
}

/// <summary>What both fixtures share.</summary>
internal static class UrlOptionListHost
{
    /// <summary>The key of the list the fixtures save.</summary>
    public static readonly Guid ListKey = Guid.Parse("74000000-0000-4000-8000-000000000020");

    /// <summary>
    /// The host configuration of a site that calls 127.0.0.1 over <c>http</c> on <paramref name="stubPort"/>, with the
    /// Testing assembly's stub there.
    /// </summary>
    public static void Configure(IDictionary<string, string> configuration, int stubPort)
    {
        configuration["JironFormBuilder:Webhooks:AllowedHosts:0"] = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{stubPort}");
        configuration["JironFormBuilder:Webhooks:AllowHttp"] = "true";
        configuration[FormBuilderTestingOptions.SectionName + ":StubPort"] = stubPort.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A URL list of the stub's countries at <paramref name="path"/>, kept for 300 seconds.</summary>
    public static OptionListInput List(int port, string path) =>
        new("countries", "Countries", OptionListKind.Url, Config: $$"""{"url":"http://127.0.0.1:{{port}}{{path}}","listPath":"$.items","valueFrom":"$.id","labelFrom":"$.name"}""", CacheSeconds: 300);

    /// <summary>A loopback port that is free now, for the stub the host starts.</summary>
    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
