using System.Net;
using System.Net.Sockets;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The stub inside a test host: it starts only with a port and only in Development, and stops with the host.</summary>
[TestFixture]
public sealed class StubHostedServiceTests
{
    [Test]
    public async Task StartAsync_DevelopmentWithAPort_ListensThereUntilStopped()
    {
        var port = FreePort();
        var service = Create(port, Environments.Development);

        await service.StartAsync(CancellationToken.None);
        var address = service.Server?.BaseAddress;
        using var client = new HttpClient();
        using var answer = await client.PostAsync(new Uri($"http://127.0.0.1:{port}/webhooks/started"), null);
        await service.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(address, Is.EqualTo(new Uri($"http://127.0.0.1:{port}/")));
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(service.Server, Is.Null);
            Assert.That(PortIsFree(port), Is.True, "stopping released the port");
        });
    }

    [TestCase(0, "Development")]
    [TestCase(5560, "Production")]
    [TestCase(5560, "Tests")]
    public async Task StartAsync_WithoutAPortOrOutsideDevelopment_StartsNothing(int port, string environment)
    {
        var service = Create(port, environment);

        await service.StartAsync(CancellationToken.None);

        Assert.That(service.Server, Is.Null);
        await service.DisposeAsync();
    }

    private static StubHostedService Create(int port, string environment) => new(
        Options.Create(new FormBuilderTestingOptions { StubPort = port }),
        new FakeEnvironment(environment),
        TimeProvider.System,
        NullLogger<StubHostedService>.Instance);

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool PortIsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
