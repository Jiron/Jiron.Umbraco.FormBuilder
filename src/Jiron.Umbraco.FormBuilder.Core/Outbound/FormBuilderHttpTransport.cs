using System.Net;
using System.Net.Sockets;

namespace Jiron.Umbraco.FormBuilder.Core.Outbound;

/// <summary>
/// How Jiron Form Builder sends one request to another host (ADR-0076), without following redirects. Only the Testing
/// assembly replaces it, to reach its stub on loopback.
/// </summary>
internal interface IFormBuilderHttpTransport
{
    /// <summary>Sends <paramref name="request"/> and gives the answer once its headers came, its body still to read.</summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>
/// The transport of every site (ADR-0076): it resolves a name itself, refuses a name with any address that is not
/// globally reachable before it connects, and connects only to the addresses it checked, so a name that resolves
/// elsewhere a moment later is never reached. No proxy, no cookies, no automatic redirects; names resolve anew every
/// two minutes.
/// </summary>
internal sealed class FormBuilderHttpTransport : IFormBuilderHttpTransport, IDisposable
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly IPEndPoint? _alsoReachable;
    private readonly HttpMessageInvoker _invoker;

    /// <summary>The transport of every site, over the system's name resolution.</summary>
    public FormBuilderHttpTransport()
        : this(null, null)
    {
    }

    /// <summary>
    /// A transport over <paramref name="resolve"/> (tests give names their addresses), which also reaches
    /// <paramref name="alsoReachable"/>: the Testing assembly's stub, and nothing else, in a test host only.
    /// </summary>
    internal FormBuilderHttpTransport(Func<string, CancellationToken, Task<IPAddress[]>>? resolve, IPEndPoint? alsoReachable)
    {
        _resolve = resolve ?? Dns.GetHostAddressesAsync;
        _alsoReachable = alsoReachable;
        _invoker = new HttpMessageInvoker(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = ConnectAsync,
            },
            disposeHandler: true);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _invoker.SendAsync(request, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _invoker.Dispose();

    // The connect step: the name's addresses, all of them reachable, then the first that accepts a connection; a name
    // without addresses connects nowhere.
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var target = context.DnsEndPoint;
        var host = target.Host is ['[', .., ']'] ? target.Host[1..^1] : target.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await _resolve(host, cancellationToken);
        if (!addresses.All(address => IsReachable(address, target.Port)))
        {
            throw new FormBuilderOutboundException(OutboundFailure.AddressNotPublic);
        }

        SocketException? refused = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, target.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                refused = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new FormBuilderOutboundException(OutboundFailure.NoConnection, innerException: refused);
    }

    private bool IsReachable(IPAddress address, int port) =>
        PublicAddresses.IsPublic(address) || (_alsoReachable is { } stub && stub.Port == port && stub.Address.Equals(address));
}
