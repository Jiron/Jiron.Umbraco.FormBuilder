using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Testing.Stub;

/// <summary>
/// Runs the stub inside a test host, on <c>JironFormBuilderTesting:StubPort</c> (5560 for the browser tests' host), so
/// the browser tests need no process of their own for it. Only a host in the Development environment starts it; it
/// stops with the host.
/// </summary>
public sealed partial class StubHostedService(
    IOptions<FormBuilderTestingOptions> options,
    IHostEnvironment environment,
    TimeProvider time,
    ILogger<StubHostedService> logger) : IHostedService, IAsyncDisposable
{
    /// <summary>The running stub, or null.</summary>
    public StubServer? Server { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var port = options.Value.StubPort;
        if (port <= 0 || !environment.IsDevelopment())
        {
            return;
        }

        Server = await StubServer.StartAsync(port, time, cancellationToken);
        LogListening(logger, Server.BaseAddress);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) => await DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Server is { } server)
        {
            Server = null;
            await server.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder test stub listening on {Address}")]
    private static partial void LogListening(ILogger logger, Uri address);
}
