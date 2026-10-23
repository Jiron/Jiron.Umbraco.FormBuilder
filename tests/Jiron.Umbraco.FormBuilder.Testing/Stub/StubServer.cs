using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Testing.Stub;

/// <summary>One request the stub received.</summary>
/// <param name="Id">The request's number, from 1, in the order received.</param>
/// <param name="ReceivedUtc">When it arrived, by the stub's clock.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, without the query.</param>
/// <param name="Query">The query string with its <c>?</c>, or empty.</param>
/// <param name="Headers">Every header in order, several values joined by a comma.</param>
/// <param name="Body">The body as UTF-8 text, at most <see cref="StubServer.MaxBodyBytes"/>.</param>
/// <param name="Truncated">True when the body was larger, and the stub answered <c>413</c>.</param>
public sealed record StubCall(
    long Id,
    DateTimeOffset ReceivedUtc,
    string Method,
    string Path,
    string Query,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string Body,
    bool Truncated)
{
    /// <summary>The first value of a header, compared without regard to case, or null.</summary>
    public string? Header(string name) =>
        Headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>A scripted answer: the stub gives it to every request with this method and path until it is reset.</summary>
/// <param name="Method">The HTTP method, or <c>*</c> for any.</param>
/// <param name="Path">The path, compared without regard to case and without the query.</param>
/// <param name="Status">The status code.</param>
/// <param name="ContentType">The content type of the body.</param>
/// <param name="Body">The body.</param>
/// <param name="DelayMs">How long to wait before answering, for timeout tests.</param>
public sealed record StubAnswer(
    string Method,
    string Path,
    int Status = 200,
    string ContentType = "application/json",
    string Body = "",
    int DelayMs = 0);

/// <summary>
/// The local HTTP stub of the test hosts (ADR-0011, ADR-0029; ROADMAP section 4.11: port 5560): it stands in for the
/// outside services Jiron Form Builder calls, so no test ever reaches a real one (D22).
/// <list type="bullet">
/// <item><description><c>/webhooks/*</c>: any method, answered <c>200</c> with <c>{"received":true}</c>.</description></item>
/// <item><description><c>POST /captcha/siteverify</c> and <c>/captcha/{name}/siteverify</c>: a captcha verification,
/// answered by the token the browser sent (<see cref="StubCaptcha"/>).</description></item>
/// <item><description>Any other path: <c>404</c>, unless a scripted <see cref="StubAnswer"/> matches, which also
/// overrides the two above (URL option lists, failing webhooks, slow services).</description></item>
/// </list>
/// Every such request is recorded. The control paths under <c>/_stub/</c> are not: <c>GET</c> and <c>DELETE
/// /_stub/calls</c> read and clear the calls, <c>PUT /_stub/answers</c> adds an answer, <c>DELETE /_stub/answers</c>
/// removes them, and <c>POST /_stub/reset</c> does both. It listens on 127.0.0.1 only.
/// </summary>
public sealed class StubServer : IAsyncDisposable
{
    /// <summary>The stub's port on test hosts (ROADMAP section 4.11).</summary>
    public const int DefaultPort = 5560;

    /// <summary>The largest body the stub records; a larger one is answered <c>413</c>.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly Lock _gate = new();
    private readonly List<StubCall> _calls = [];
    private readonly List<StubAnswer> _answers = [];
    private readonly TimeProvider _time;
    private WebApplication? _app;
    private long _nextId;

    private StubServer(TimeProvider time) => _time = time;

    /// <summary>The stub's address, for example <c>http://127.0.0.1:5560/</c>.</summary>
    public Uri BaseAddress { get; private set; } = new("http://127.0.0.1/");

    /// <summary>The requests received so far, oldest first.</summary>
    public IReadOnlyList<StubCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    /// <summary>
    /// Starts a stub on 127.0.0.1. Port 0 lets the system choose a free one, which tests that run side by side use.
    /// </summary>
    /// <exception cref="InvalidOperationException">The port is in use.</exception>
    public static async Task<StubServer> StartAsync(int port = DefaultPort, TimeProvider? time = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        var stub = new StubServer(time ?? TimeProvider.System);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
        var app = builder.Build();
        app.Run(stub.HandleAsync);
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            await app.DisposeAsync();
            throw new InvalidOperationException($"The stub cannot listen on 127.0.0.1:{port}: the port is in use.", exception);
        }

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        stub.BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
        stub._app = app;
        return stub;
    }

    /// <summary>The requests received at a path (compared without regard to case), oldest first.</summary>
    public IReadOnlyList<StubCall> CallsTo(string path) =>
        Calls.Where(call => string.Equals(call.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Adds a scripted answer; a later answer for the same method and path wins.</summary>
    public void Answer(StubAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer.Path);
        lock (_gate)
        {
            _answers.Add(answer);
        }
    }

    /// <summary>Forgets every call and every scripted answer.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _answers.Clear();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_app is { } app)
        {
            _app = null;
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? "/";
        if (path.StartsWith("/_stub/", StringComparison.OrdinalIgnoreCase))
        {
            await ControlAsync(context, path);
            return;
        }

        var (body, truncated) = await ReadBodyAsync(request, context.RequestAborted);
        var call = new StubCall(
            Interlocked.Increment(ref _nextId),
            _time.GetUtcNow(),
            request.Method,
            path,
            request.QueryString.Value ?? string.Empty,
            request.Headers.Select(header => new KeyValuePair<string, string>(header.Key, header.Value.ToString())).ToList(),
            body,
            truncated);
        lock (_gate)
        {
            _calls.Add(call);
        }

        if (truncated)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        if (FindAnswer(request.Method, path) is { } answer)
        {
            if (answer.DelayMs > 0)
            {
                await Task.Delay(answer.DelayMs, context.RequestAborted);
            }

            context.Response.StatusCode = answer.Status;
            context.Response.ContentType = answer.ContentType;
            await context.Response.WriteAsync(answer.Body, context.RequestAborted);
            return;
        }

        if (path.StartsWith("/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            await context.Response.WriteAsJsonAsync(new { received = true }, Json, context.RequestAborted);
            return;
        }

        if (HttpMethods.IsPost(request.Method) && StubCaptcha.IsVerifyPath(path))
        {
            await context.Response.WriteAsJsonAsync(StubCaptcha.Verify(body, request.ContentType, _time.GetUtcNow()), Json, context.RequestAborted);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private async Task ControlAsync(HttpContext context, string path)
    {
        var method = context.Request.Method;
        switch (path.TrimEnd('/').ToLowerInvariant())
        {
            case "/_stub/calls" when HttpMethods.IsGet(method):
                await context.Response.WriteAsJsonAsync(Calls, Json, context.RequestAborted);
                return;
            case "/_stub/calls" when HttpMethods.IsDelete(method):
                lock (_gate)
                {
                    _calls.Clear();
                }

                break;
            case "/_stub/answers" when HttpMethods.IsPut(method):
                var answer = await JsonSerializer.DeserializeAsync<StubAnswer>(context.Request.Body, Json, context.RequestAborted);
                if (answer is null || string.IsNullOrWhiteSpace(answer.Path) || string.IsNullOrWhiteSpace(answer.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                Answer(answer);
                break;
            case "/_stub/answers" when HttpMethods.IsDelete(method):
                lock (_gate)
                {
                    _answers.Clear();
                }

                break;
            case "/_stub/reset" when HttpMethods.IsPost(method):
                Reset();
                break;
            default:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private StubAnswer? FindAnswer(string method, string path)
    {
        lock (_gate)
        {
            return _answers.LastOrDefault(answer =>
                (answer.Method == "*" || string.Equals(answer.Method, method, StringComparison.OrdinalIgnoreCase))
                && string.Equals(answer.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task<(string Body, bool Truncated)> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                await buffer.WriteAsync(chunk.AsMemory(0, (int)(MaxBodyBytes - buffer.Length)), cancellationToken);
                return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), false);
    }
}
