using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Net.Http.Headers;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Outbound;

/// <summary>How long a call to another host may take and how large its answer may be (ADR-0076).</summary>
/// <param name="Timeout">The whole exchange: redirects, headers and body.</param>
/// <param name="MaxBytes">The answer's body after decompression.</param>
internal readonly record struct OutboundLimits(TimeSpan Timeout, int MaxBytes);

/// <summary>An entry of <c>JironFormBuilder:Webhooks:AllowedHosts</c>: a host, and the port it names, if any (ADR-0082).</summary>
/// <param name="Host">The host as <see cref="OutboundHosts.Normalize"/> gives it.</param>
/// <param name="Port">The port, from 1 to 65535; null for the scheme's standard port.</param>
internal readonly record struct OutboundHost(string Host, int? Port);

/// <summary>The hosts of <c>JironFormBuilder:Webhooks:AllowedHosts</c> as calls compare them (ADR-0076, ADR-0082).</summary>
internal static class OutboundHosts
{
    /// <summary>
    /// An entry as calls compare it: a host alone, which a call reaches on its scheme's standard port only, or a host
    /// and a port after a colon (<c>events.example.com:8443</c>, <c>[2001:db8::1]:8443</c>); null for anything else.
    /// An IPv6 address with a port is written in brackets, as an address writes it.
    /// </summary>
    public static OutboundHost? Parse(string? entry)
    {
        if (string.IsNullOrEmpty(entry))
        {
            return null;
        }

        var colon = entry.LastIndexOf(':');
        var hasPort = colon > 0 && (entry[0] == '[' ? entry[colon - 1] == ']' : entry.IndexOf(':') == colon);
        if (!hasPort)
        {
            return Normalize(entry) is { } host ? new OutboundHost(host, null) : null;
        }

        return int.TryParse(entry.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            && Normalize(entry[..colon]) is { } named
            ? new OutboundHost(named, port)
            : null;
    }

    /// <summary>
    /// The host as an address gives it to compare (<see cref="Uri.IdnHost"/>: in its ASCII form, in lowercase, an IPv6
    /// address without its brackets), or null for anything but one host name or IP address. Brackets hold an IPv6
    /// address only, as an address writes them.
    /// </summary>
    public static string? Normalize(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253)
        {
            return null;
        }

        var bracketed = host is ['[', .., ']'];
        var bare = bracketed ? host[1..^1] : host;
        var type = Uri.CheckHostName(bare);
        if (type is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) || (bracketed && type != UriHostNameType.IPv6))
        {
            return null;
        }

        var written = type == UriHostNameType.IPv6 ? $"[{bare}]" : bare;
        return Uri.TryCreate($"https://{written}/", UriKind.Absolute, out var address) ? address.IdnHost : null;
    }
}

/// <summary>
/// Calls to another host (section 4.10, ADR-0076): only an absolute address without a user name or password, over
/// <c>https</c> (or <c>http</c> when the host configuration allows it), to one of the allowed hosts, on its scheme's
/// standard port or one its entry names (ADR-0082); at most three redirects, each to the same host and checked again;
/// one time limit for the whole exchange and a cap on the answer. The transport refuses addresses that are not globally
/// reachable. A connection that breaks off and a body that does not decompress are failures like any other (ADR-0081).
/// </summary>
/// <param name="transport">How a request is sent.</param>
/// <param name="options">The host configuration, for the allowed hosts and <c>http</c>.</param>
/// <param name="time">The clock of the time limit.</param>
internal sealed class OutboundHttp(IFormBuilderHttpTransport transport, IOptions<FormBuilderOptions> options, TimeProvider time)
{
    /// <summary>The most redirects a call follows.</summary>
    public const int MaxRedirects = 3;

    // Each allowed host with the ports its entries name; null among them for the scheme's standard port (ADR-0082).
    private readonly FrozenDictionary<string, FrozenSet<int?>> _hosts = options.Value.Webhooks.AllowedHosts
        .Select(OutboundHosts.Parse)
        .OfType<OutboundHost>()
        .GroupBy(entry => entry.Host, StringComparer.Ordinal)
        .ToFrozenDictionary(entries => entries.Key, entries => entries.Select(entry => entry.Port).ToFrozenSet(), StringComparer.Ordinal);

    private readonly bool _allowHttp = options.Value.Webhooks.AllowHttp;

    /// <summary>Why the host configuration refuses <paramref name="address"/>, or null when it allows it.</summary>
    public OutboundFailure? Refusal(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || address.UserInfo.Length > 0)
        {
            return OutboundFailure.AddressInvalid;
        }

        if (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && _allowHttp))
        {
            return OutboundFailure.SchemeNotAllowed;
        }

        if (!_hosts.TryGetValue(address.IdnHost, out var ports))
        {
            return OutboundFailure.HostNotAllowed;
        }

        return (address.IsDefaultPort && ports.Contains(null)) || ports.Contains(address.Port) ? null : OutboundFailure.PortNotAllowed;
    }

    /// <summary>The body of a <c>GET</c> of <paramref name="address"/>, or a <see cref="FormBuilderOutboundException"/>.</summary>
    /// <param name="address">The address.</param>
    /// <param name="accept">The media type asked for, such as <c>application/json</c>.</param>
    /// <param name="limits">The time limit and the cap.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    public async Task<byte[]> GetAsync(Uri address, string accept, OutboundLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var timeout = new CancellationTokenSource(limits.Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await GetCoreAsync(address, accept, limits.MaxBytes, linked.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new FormBuilderOutboundException(OutboundFailure.TimedOut, innerException: exception);
        }
        catch (HttpRequestException exception) when (exception.InnerException is FormBuilderOutboundException refused)
        {
            // The transport's own refusal, which the handler wraps.
            throw new FormBuilderOutboundException(refused.Reason, refused.Status, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new FormBuilderOutboundException(OutboundFailure.NoConnection, innerException: exception);
        }
        catch (IOException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new FormBuilderOutboundException(OutboundFailure.TimedOut, innerException: exception);
        }
        catch (IOException exception)
        {
            // The connection broke off while the body was read (ADR-0081).
            throw new FormBuilderOutboundException(OutboundFailure.NoConnection, innerException: exception);
        }
        catch (InvalidDataException exception)
        {
            // The body does not decompress as its encoding says (ADR-0081).
            throw new FormBuilderOutboundException(OutboundFailure.AnswerUnreadable, innerException: exception);
        }
    }

    private async Task<byte[]> GetCoreAsync(Uri first, string accept, int maxBytes, CancellationToken cancellationToken)
    {
        var address = first;
        for (var redirects = 0; ; redirects++)
        {
            if (Refusal(address) is { } refusal)
            {
                throw new FormBuilderOutboundException(refusal);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("JironFormBuilder", FormBuilderVersion.Package));
            using var response = await transport.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (redirects == MaxRedirects)
                {
                    throw new FormBuilderOutboundException(OutboundFailure.TooManyRedirects);
                }

                var next = location.IsAbsoluteUri ? location : new Uri(address, location);
                if (!string.Equals(next.IdnHost, address.IdnHost, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormBuilderOutboundException(OutboundFailure.RedirectToOtherHost);
                }

                address = next;
                continue;
            }

            if (status is < 200 or >= 300)
            {
                throw new FormBuilderOutboundException(OutboundFailure.Refused, status);
            }

            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new FormBuilderOutboundException(OutboundFailure.AnswerTooLarge);
            }

            return await ReadAsync(response.Content, maxBytes, cancellationToken);
        }
    }

    // The body up to the cap, which a body that does not declare its length may pass only by being refused.
    private static async Task<byte[]> ReadAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var body = new ArrayBufferWriter<byte>();
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (body.WrittenCount + read > maxBytes)
                {
                    throw new FormBuilderOutboundException(OutboundFailure.AnswerTooLarge);
                }

                body.Write(chunk.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return body.WrittenSpan.ToArray();
    }
}
