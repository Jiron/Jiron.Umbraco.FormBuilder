using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Outbound;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Outbound;

/// <summary>
/// A transport that answers as a test scripts it and records what it was sent (ADR-0076), for what no socket shows: an
/// <c>https</c> address, a body whose length is not declared, and a fetch that waits for the test.
/// </summary>
/// <param name="answer">The answer to each request.</param>
internal sealed class ScriptedTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : IFormBuilderHttpTransport
{
    private readonly Lock _gate = new();
    private readonly List<ScriptedRequest> _requests = [];

    /// <summary>A transport that answers every request at once.</summary>
    public ScriptedTransport(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : this((request, _) => Task.FromResult(answer(request)))
    {
    }

    /// <summary>What was sent, oldest first.</summary>
    public IReadOnlyList<ScriptedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>A <c>200</c> answer with this JSON, its length declared.</summary>
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A <c>200</c> answer with these bytes, its length declared.</summary>
    public static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>A <c>200</c> answer with these bytes, whose length is not declared, as a chunked answer's is not.</summary>
    public static HttpResponseMessage Undeclared(byte[] body) => new(HttpStatusCode.OK) { Content = new UndeclaredContent(body) };

    /// <summary>A <c>200</c> answer that declares <paramref name="length"/> bytes and fails the test if its body is read.</summary>
    public static HttpResponseMessage Declaring(long length) => new(HttpStatusCode.OK) { Content = new UnreadContent(length) };

    /// <summary>A <c>302</c> answer to <paramref name="location"/>, absolute or relative.</summary>
    public static HttpResponseMessage Redirect(string location)
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return redirect;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add(new ScriptedRequest(request.Method, request.RequestUri!, request.Headers.Accept.ToString(), request.Headers.UserAgent.ToString()));
        }

        return answer(request, cancellationToken);
    }

    private sealed class UnreadContent(long length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The body of an answer that declares more than the cap was read.");

        protected override bool TryComputeLength(out long declared)
        {
            declared = length;
            return true;
        }
    }

    private sealed class UndeclaredContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

/// <summary>A request a <see cref="ScriptedTransport"/> was sent.</summary>
/// <param name="Method">The method.</param>
/// <param name="Address">The address.</param>
/// <param name="Accept">The <c>Accept</c> header.</param>
/// <param name="UserAgent">The <c>User-Agent</c> header.</param>
internal sealed record ScriptedRequest(HttpMethod Method, Uri Address, string Accept, string UserAgent);
