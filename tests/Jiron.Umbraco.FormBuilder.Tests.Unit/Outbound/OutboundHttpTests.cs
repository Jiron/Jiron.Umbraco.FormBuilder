using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Microsoft.Extensions.DependencyInjection;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Outbound;

/// <summary>
/// Calls to another host (section 4.10, ADR-0076) over a stub on a port the system chooses: only allowed hosts, over
/// <c>https</c> unless the host allows <c>http</c>; a name that resolves to an address that is not globally reachable
/// is refused before anything connects, and the site's own transport refuses loopback; only the scheme's standard port
/// or one an entry names (ADR-0082); redirects only to the same host, at most three; one time limit and a cap on the
/// answer; and failures that name their reason, a body that breaks off or does not decompress among them (ADR-0081),
/// never the address.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class OutboundHttpTests
{
    private const string Host = "options.example.com";

    private static readonly OutboundLimits Limits = new(TimeSpan.FromSeconds(10), 1024);

    private StubServer _stub = null!;

    private int Port => _stub.BaseAddress.Port;

    [SetUp]
    public async Task SetUp() => _stub = await StubServer.StartAsync(port: 0);

    [TearDown]
    public async Task TearDown() => await _stub.DisposeAsync();

    [Test]
    public async Task AnAllowedHost_IsCalledWithAGet_ForTheMediaType_AsJironFormBuilder()
    {
        _stub.Answer(new StubAnswer("GET", "/list", Body: """[{"value":"a"}]"""));
        using var transport = StubTransport();

        var body = await Outbound(transport, allowHttp: true, Host).GetAsync(At("/list?page=1"), "application/json", Limits, CancellationToken.None);

        var call = _stub.Calls.Single();
        Assert.Multiple(async () =>
        {
            Assert.That(Encoding.UTF8.GetString(body), Is.EqualTo("""[{"value":"a"}]"""));
            Assert.That((call.Method, call.Path, call.Query), Is.EqualTo(("GET", "/list", "?page=1")));
            Assert.That(call.Header("Accept"), Is.EqualTo("application/json"));
            Assert.That(call.Header("User-Agent"), Is.EqualTo($"JironFormBuilder/{FormBuilderVersion.Package}"));
            Assert.That(call.Header("Cookie"), Is.Null);
        });
    }

    [Test]
    public void OnlyHttps_IsCalled_UnlessTheHostAllowsHttp()
    {
        var https = Outbound(new ScriptedTransport(_ => ScriptedTransport.Json("[]")), allowHttp: false, Host);
        var http = Outbound(new ScriptedTransport(_ => ScriptedTransport.Json("[]")), allowHttp: true, Host);

        Assert.Multiple(async () =>
        {
            Assert.That(https.Refusal(new Uri($"https://{Host}/list")), Is.Null);
            Assert.That(https.Refusal(new Uri($"http://{Host}/list")), Is.EqualTo(OutboundFailure.SchemeNotAllowed));
            Assert.That(http.Refusal(new Uri($"http://{Host}/list")), Is.Null);
            Assert.That(http.Refusal(new Uri($"ftp://{Host}/list")), Is.EqualTo(OutboundFailure.SchemeNotAllowed));
            Assert.That(http.Refusal(new Uri($"ws://{Host}/list")), Is.EqualTo(OutboundFailure.SchemeNotAllowed));
            Assert.That(await ReasonAsync(() => https.GetAsync(new Uri($"http://{Host}:{Port}/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.SchemeNotAllowed));
        });
        Assert.That(_stub.Calls, Is.Empty);
    }

    [Test]
    public void AHostNotAllowed_IsRefused_BeforeAnythingIsSent()
    {
        var transport = new ScriptedTransport(_ => ScriptedTransport.Json("[]"));
        var outbound = Outbound(transport, allowHttp: false, Host, $"B{(char)0xFC}cher.example");

        Assert.Multiple(async () =>
        {
            Assert.That(outbound.Refusal(new Uri("https://other.example.com/list")), Is.EqualTo(OutboundFailure.HostNotAllowed));
            Assert.That(outbound.Refusal(new Uri($"https://sub.{Host}/list")), Is.EqualTo(OutboundFailure.HostNotAllowed), "a host's sub-domains are other hosts");
            Assert.That(outbound.Refusal(new Uri("https://example.com/list")), Is.EqualTo(OutboundFailure.HostNotAllowed));
            Assert.That(outbound.Refusal(new Uri("https://OPTIONS.Example.COM/list")), Is.Null, "letter case does not count");
            Assert.That(outbound.Refusal(new Uri("https://xn--bcher-kva.example/list")), Is.Null, "an international name in its ASCII form");
            Assert.That(outbound.Refusal(new Uri($"https://b{(char)0xFC}cher.example/list")), Is.Null, "and as it is written");
            Assert.That(await ReasonAsync(() => outbound.GetAsync(new Uri("https://other.example.com/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.HostNotAllowed));
        });
        Assert.That(transport.Requests, Is.Empty);
    }

    [Test]
    public void AHost_IsCalledOnItsSchemesStandardPort_OrOnAPortAnEntryNames()
    {
        var transport = new ScriptedTransport(_ => ScriptedTransport.Json("[]"));
        var standard = Calling(transport, allowHttp: true, Host);
        var named = Calling(transport, allowHttp: true, "events.example.com:8443", "[2001:db8::1]:8443");
        var both = Calling(transport, allowHttp: true, Host, $"{Host}:8443");

        Assert.Multiple(async () =>
        {
            Assert.That(standard.Refusal(new Uri($"https://{Host}/list")), Is.Null);
            Assert.That(standard.Refusal(new Uri($"https://{Host}:443/list")), Is.Null, "the standard port written out");
            Assert.That(standard.Refusal(new Uri($"http://{Host}/list")), Is.Null);
            Assert.That(standard.Refusal(new Uri($"http://{Host}:443/list")), Is.EqualTo(OutboundFailure.PortNotAllowed), "https's port is not http's");
            Assert.That(standard.Refusal(new Uri($"https://{Host}:8443/list")), Is.EqualTo(OutboundFailure.PortNotAllowed));
            Assert.That(named.Refusal(new Uri("https://events.example.com:8443/list")), Is.Null);
            Assert.That(named.Refusal(new Uri("http://EVENTS.example.com:8443/list")), Is.Null, "on either scheme");
            Assert.That(named.Refusal(new Uri("https://events.example.com/list")), Is.EqualTo(OutboundFailure.PortNotAllowed), "an entry with a port allows that port only");
            Assert.That(named.Refusal(new Uri("https://[2001:db8::1]:8443/list")), Is.Null);
            Assert.That(named.Refusal(new Uri("https://[2001:db8::1]/list")), Is.EqualTo(OutboundFailure.PortNotAllowed));
            Assert.That(both.Refusal(new Uri($"https://{Host}/list")), Is.Null);
            Assert.That(both.Refusal(new Uri($"https://{Host}:8443/list")), Is.Null);
            Assert.That(both.Refusal(new Uri($"https://{Host}:9443/list")), Is.EqualTo(OutboundFailure.PortNotAllowed));
            Assert.That(await ReasonAsync(() => standard.GetAsync(new Uri($"https://{Host}:8443/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.PortNotAllowed));
        });
        Assert.That(transport.Requests, Is.Empty);
    }

    [Test]
    public void AnAddressWithAUserNameOrPassword_OrNoHost_IsRefused()
    {
        var outbound = Outbound(new ScriptedTransport(_ => ScriptedTransport.Json("[]")), allowHttp: false, Host);

        Assert.Multiple(async () =>
        {
            Assert.That(outbound.Refusal(new Uri($"https://user:secret@{Host}/list")), Is.EqualTo(OutboundFailure.AddressInvalid));
            Assert.That(outbound.Refusal(new Uri($"https://user@{Host}/list")), Is.EqualTo(OutboundFailure.AddressInvalid));
            Assert.That(outbound.Refusal(new Uri("/list", UriKind.Relative)), Is.EqualTo(OutboundFailure.AddressInvalid));
            Assert.That(new FormBuilderOptions().Webhooks.AllowedHosts, Is.Empty);
            Assert.That(Outbound(new ScriptedTransport(_ => ScriptedTransport.Json("[]")), allowHttp: false).Refusal(new Uri($"https://{Host}/list")), Is.EqualTo(OutboundFailure.HostNotAllowed), "a site that names no hosts calls none");
        });
    }

    [TestCase("127.0.0.1", Description = "loopback")]
    [TestCase("127.1.2.3", Description = "loopback")]
    [TestCase("10.1.2.3", Description = "private")]
    [TestCase("172.16.0.10", Description = "private")]
    [TestCase("192.168.1.10", Description = "private")]
    [TestCase("169.254.169.254", Description = "link-local")]
    [TestCase("0.0.0.0", Description = "this host")]
    [TestCase("::1", Description = "IPv6 loopback")]
    [TestCase("fe80::1", Description = "IPv6 link-local")]
    [TestCase("fd00::1", Description = "IPv6 unique local")]
    [TestCase("::ffff:127.0.0.1", Description = "loopback in IPv6 form")]
    [TestCase("93.184.215.14,10.0.0.1", Description = "a public address beside a private one")]
    [TestCase("2606:4700:4700::1111,::1", Description = "a public address beside loopback")]
    public async Task ANameThatResolvesToAnAddressThatIsNotGloballyReachable_IsRefused_BeforeAnythingConnects(string resolved)
    {
        var addresses = resolved.Split(',').Select(IPAddress.Parse).ToArray();
        var asked = new List<string>();
        using var transport = new FormBuilderHttpTransport(
            (name, _) =>
            {
                asked.Add(name);
                return Task.FromResult(addresses);
            },
            null);

        var reason = await ReasonAsync(() => Outbound(transport, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(reason, Is.EqualTo(OutboundFailure.AddressNotPublic));
            Assert.That(asked, Is.EqualTo(new[] { Host }), "the name is resolved by the transport itself");
            Assert.That(_stub.Calls, Is.Empty, "a loopback address would have reached the stub");
        });
    }

    [Test]
    public void TheSitesTransport_RefusesLoopback_ByNameAndAsAnAddress()
    {
        using var transport = new FormBuilderHttpTransport();
        var outbound = Outbound(transport, allowHttp: true, "127.0.0.1", "localhost", "[::1]");

        Assert.Multiple(async () =>
        {
            Assert.That(await ReasonAsync(() => outbound.GetAsync(new Uri($"http://127.0.0.1:{Port}/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AddressNotPublic));
            Assert.That(await ReasonAsync(() => outbound.GetAsync(new Uri($"http://localhost:{Port}/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AddressNotPublic));
            Assert.That(await ReasonAsync(() => outbound.GetAsync(new Uri($"http://[::1]:{Port}/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AddressNotPublic));
        });
        Assert.That(_stub.Calls, Is.Empty);
    }

    [Test]
    public void TheRegisteredTransport_IsTheSitesOwn_UnlessOneIsRegisteredBefore()
    {
        var options = new FormBuilderOptions();
        options.Webhooks.AllowedHosts.Add($"127.0.0.1:{Port}");
        options.Webhooks.AllowHttp = true;
        var services = new ServiceCollection().AddSingleton(TimeProvider.System).AddSingleton(MsOptions.Create(options));
        using var provider = services.AddFormBuilderOutbound().BuildServiceProvider();
        var scripted = new ScriptedTransport(_ => ScriptedTransport.Json("[]"));
        using var replaced = new ServiceCollection().AddSingleton<IFormBuilderHttpTransport>(scripted).AddFormBuilderOutbound().BuildServiceProvider();

        Assert.Multiple(async () =>
        {
            Assert.That(provider.GetRequiredService<IFormBuilderHttpTransport>(), Is.TypeOf<FormBuilderHttpTransport>());
            Assert.That(provider.GetRequiredService<OutboundHttp>(), Is.SameAs(provider.GetRequiredService<OutboundHttp>()));
            Assert.That(
                await ReasonAsync(() => provider.GetRequiredService<OutboundHttp>().GetAsync(new Uri($"http://127.0.0.1:{Port}/list"), "application/json", Limits, CancellationToken.None)),
                Is.EqualTo(OutboundFailure.AddressNotPublic),
                "the site's registration refuses the stub");
            Assert.That(replaced.GetRequiredService<IFormBuilderHttpTransport>(), Is.SameAs(scripted));
        });
        Assert.That(_stub.Calls, Is.Empty);
    }

    [Test]
    public async Task TheTestTransport_ReachesTheStub_AndNoOtherLoopbackAddressOrPort()
    {
        _stub.Answer(new StubAnswer("GET", "/list", Body: "[]"));
        using var stub = StubTransport();
        using var otherAddress = new FormBuilderHttpTransport((_, _) => Task.FromResult(new[] { IPAddress.Parse("127.0.0.2") }), new IPEndPoint(IPAddress.Loopback, Port));
        using var ipv6 = new FormBuilderHttpTransport((_, _) => Task.FromResult(new[] { IPAddress.IPv6Loopback }), new IPEndPoint(IPAddress.Loopback, Port));
        var other = OtherPort();

        var reached = await Outbound(stub, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(Encoding.UTF8.GetString(reached), Is.EqualTo("[]"));
            Assert.That(await ReasonAsync(() => Calling(stub, allowHttp: true, $"{Host}:{other}").GetAsync(new Uri($"http://{Host}:{other}/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AddressNotPublic));
            Assert.That(await ReasonAsync(() => Outbound(otherAddress, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AddressNotPublic));
            Assert.That(await ReasonAsync(() => Outbound(ipv6, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AddressNotPublic));
        });
        Assert.That(_stub.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ARedirectToTheSameHost_IsFollowed_AtMostThreeTimes()
    {
        _stub.Answer(new StubAnswer("GET", "/one", Status: 302, Location: "/two"));
        _stub.Answer(new StubAnswer("GET", "/two", Status: 301, Location: $"http://{Host}:{Port}/three"));
        _stub.Answer(new StubAnswer("GET", "/three", Status: 307, Location: "four"));
        _stub.Answer(new StubAnswer("GET", "/four", Body: """["found"]"""));
        _stub.Answer(new StubAnswer("GET", "/r1", Status: 302, Location: "/r2"));
        _stub.Answer(new StubAnswer("GET", "/r2", Status: 302, Location: "/r3"));
        _stub.Answer(new StubAnswer("GET", "/r3", Status: 302, Location: "/r4"));
        _stub.Answer(new StubAnswer("GET", "/r4", Status: 302, Location: "/r5"));
        _stub.Answer(new StubAnswer("GET", "/r5", Body: "[]"));
        using var transport = StubTransport();
        var outbound = Outbound(transport, allowHttp: true, Host);

        var body = await outbound.GetAsync(At("/one"), "application/json", Limits, CancellationToken.None);
        var calls = _stub.Calls.Select(call => call.Path).ToList();
        var tooMany = await ReasonAsync(() => outbound.GetAsync(At("/r1"), "application/json", Limits, CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(Encoding.UTF8.GetString(body), Is.EqualTo("""["found"]"""));
            Assert.That(calls, Is.EqualTo(new[] { "/one", "/two", "/three", "/four" }), "relative and absolute, each checked again");
            Assert.That(tooMany, Is.EqualTo(OutboundFailure.TooManyRedirects));
            Assert.That(_stub.Calls.Select(call => call.Path).Skip(4), Is.EqualTo(new[] { "/r1", "/r2", "/r3", "/r4" }), "the fourth redirect is not followed");
            Assert.That(OutboundHttp.MaxRedirects, Is.EqualTo(3));
        });
    }

    [Test]
    public void ARedirectToAnotherHost_IsRefused_EvenToAnAllowedOne_WithoutCallingIt()
    {
        _stub.Answer(new StubAnswer("GET", "/away", Status: 302, Location: "http://other.example.com/list"));
        _stub.Answer(new StubAnswer("GET", "/allowed", Status: 303, Location: $"http://allowed.example.com:{Port}/list"));
        _stub.Answer(new StubAnswer("GET", "/list", Body: "[]"));
        using var transport = new FormBuilderHttpTransport((_, _) => Task.FromResult(new[] { IPAddress.Loopback }), new IPEndPoint(IPAddress.Loopback, Port));
        var outbound = Outbound(transport, allowHttp: true, Host, "allowed.example.com");

        Assert.Multiple(async () =>
        {
            Assert.That(await ReasonAsync(() => outbound.GetAsync(At("/away"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.RedirectToOtherHost));
            Assert.That(await ReasonAsync(() => outbound.GetAsync(At("/allowed"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.RedirectToOtherHost));
        });
        Assert.That(_stub.Calls.Select(call => call.Path), Is.EqualTo(new[] { "/away", "/allowed" }));
    }

    [Test]
    public async Task ARedirectToAnotherPort_IsRefused_UnlessAnEntryNamesIt()
    {
        var transport = new ScriptedTransport(request => request.RequestUri!.AbsolutePath == "/start" ? ScriptedTransport.Redirect($"https://{Host}:8443/list") : ScriptedTransport.Json("[]"));

        var refused = await ReasonAsync(() => Calling(transport, allowHttp: false, Host).GetAsync(new Uri($"https://{Host}/start"), "application/json", Limits, CancellationToken.None));
        var followed = await Calling(transport, allowHttp: false, Host, $"{Host}:8443").GetAsync(new Uri($"https://{Host}/start"), "application/json", Limits, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.EqualTo(OutboundFailure.PortNotAllowed));
            Assert.That(Encoding.UTF8.GetString(followed), Is.EqualTo("[]"));
            Assert.That(transport.Requests.Select(request => request.Address.AbsoluteUri), Is.EqualTo(new[] { $"https://{Host}/start", $"https://{Host}/start", $"https://{Host}:8443/list" }), "the refused port is never called");
        });
    }

    [Test]
    public async Task ARedirectFromHttpsToHttp_IsRefused_WhereHttpIsNotAllowed()
    {
        var transport = new ScriptedTransport(request => request.RequestUri!.Scheme == "https" ? ScriptedTransport.Redirect($"http://{Host}/list") : ScriptedTransport.Json("[]"));

        var reason = await ReasonAsync(() => Outbound(transport, allowHttp: false, Host).GetAsync(new Uri($"https://{Host}/start"), "application/json", Limits, CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(reason, Is.EqualTo(OutboundFailure.SchemeNotAllowed));
            Assert.That(transport.Requests.Select(request => request.Address.AbsoluteUri), Is.EqualTo(new[] { $"https://{Host}/start" }));
        });
    }

    [Test]
    public async Task AnAnswerOverTheCap_IsRefused_WhetherItDeclaresItsLengthOrNot()
    {
        _stub.Answer(new StubAnswer("GET", "/large", Body: new string('x', Limits.MaxBytes + 1)));
        _stub.Answer(new StubAnswer("GET", "/fits", Body: new string('x', Limits.MaxBytes)));
        using var stub = StubTransport();
        var https = new Uri($"https://{Host}/list");
        var declared = new ScriptedTransport(_ => ScriptedTransport.Bytes(new byte[Limits.MaxBytes + 1]));
        var undeclared = new ScriptedTransport(_ => ScriptedTransport.Undeclared(new byte[Limits.MaxBytes + 1]));
        var exact = new ScriptedTransport(_ => ScriptedTransport.Undeclared(new byte[Limits.MaxBytes]));

        var fits = await Outbound(stub, allowHttp: true, Host).GetAsync(At("/fits"), "application/json", Limits, CancellationToken.None);
        var fitsUndeclared = await Outbound(exact, allowHttp: false, Host).GetAsync(https, "application/json", Limits, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(await ReasonAsync(() => Outbound(stub, allowHttp: true, Host).GetAsync(At("/large"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AnswerTooLarge), "from the stub");
            Assert.That(await ReasonAsync(() => Outbound(declared, allowHttp: false, Host).GetAsync(https, "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AnswerTooLarge), "with its length declared");
            Assert.That(await ReasonAsync(() => Outbound(undeclared, allowHttp: false, Host).GetAsync(https, "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.AnswerTooLarge), "without");
            Assert.That((fits.Length, fitsUndeclared.Length), Is.EqualTo((Limits.MaxBytes, Limits.MaxBytes)), "an answer of the cap's size");
        });
    }

    [Test]
    public async Task AnAnswerThatDeclaresMoreThanTheCap_IsRefused_BeforeItsBodyIsRead()
    {
        var transport = new ScriptedTransport(_ => ScriptedTransport.Declaring(Limits.MaxBytes + 1L));

        var reason = await ReasonAsync(() => Outbound(transport, allowHttp: false, Host).GetAsync(new Uri($"https://{Host}/list"), "application/json", Limits, CancellationToken.None));

        Assert.That(reason, Is.EqualTo(OutboundFailure.AnswerTooLarge));
    }

    [Test]
    public async Task AnAnswerTooSlow_TimesOut_AtTheTimeLimit()
    {
        _stub.Answer(new StubAnswer("GET", "/slow", Body: "[]", DelayMs: 10_000));
        using var transport = StubTransport();
        var watch = Stopwatch.StartNew();

        var reason = await ReasonAsync(() => Outbound(transport, allowHttp: true, Host).GetAsync(At("/slow"), "application/json", Limits with { Timeout = TimeSpan.FromMilliseconds(300) }, CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(reason, Is.EqualTo(OutboundFailure.TimedOut));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void ACallerThatStopsWaiting_IsCancelled_NotTimedOut()
    {
        _stub.Answer(new StubAnswer("GET", "/slow", Body: "[]", DelayMs: 10_000));
        using var transport = StubTransport();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.That(() => Outbound(transport, allowHttp: true, Host).GetAsync(At("/slow"), "application/json", Limits, stop.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [TestCase(404)]
    [TestCase(500)]
    [TestCase(503)]
    [TestCase(304, Description = "a redirect status without an address")]
    public void AnAnswerThatIsNoSuccess_IsRefused_WithItsStatus(int status)
    {
        _stub.Answer(new StubAnswer("GET", "/list", Status: status));
        using var transport = StubTransport();
        var outbound = Outbound(transport, allowHttp: true, Host);

        var refused = Assert.ThrowsAsync<FormBuilderOutboundException>(() => outbound.GetAsync(At("/list"), "application/json", Limits, CancellationToken.None))!;
        Assert.That((refused.Reason, refused.Status), Is.EqualTo((OutboundFailure.Refused, (int?)status)));
    }

    [Test]
    public async Task AHostThatCannotBeReached_IsNoConnection()
    {
        var closed = await StubServer.StartAsync(port: 0);
        var closedPort = closed.BaseAddress.Port;
        await closed.DisposeAsync();
        using var unresolved = new FormBuilderHttpTransport((_, _) => Task.FromResult(Array.Empty<IPAddress>()), null);
        using var unknown = new FormBuilderHttpTransport((_, _) => Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)), null);
        using var refused = new FormBuilderHttpTransport((_, _) => Task.FromResult(new[] { IPAddress.Loopback }), new IPEndPoint(IPAddress.Loopback, closedPort));

        Assert.Multiple(async () =>
        {
            Assert.That(await ReasonAsync(() => Outbound(unresolved, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.NoConnection), "a name with no address");
            Assert.That(await ReasonAsync(() => Outbound(unknown, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.NoConnection), "a name that does not resolve");
            Assert.That(await ReasonAsync(() => Calling(refused, allowHttp: true, $"{Host}:{closedPort}").GetAsync(new Uri($"http://{Host}:{closedPort}/list"), "application/json", Limits, CancellationToken.None)), Is.EqualTo(OutboundFailure.NoConnection), "a port nothing listens on");
        });
    }

    [TestCase("gzip")]
    [TestCase("deflate")]
    [TestCase("br")]
    public async Task ABodyThatDoesNotDecompress_ThroughTheRealTransport_IsUnreadable(string encoding)
    {
        _stub.Answer(new StubAnswer("GET", "/list", Body: "JFB_TEST: these bytes are compressed in no encoding at all", ContentEncoding: encoding));
        using var transport = StubTransport();

        var reason = await ReasonAsync(() => Outbound(transport, allowHttp: true, Host).GetAsync(At("/list"), "application/json", Limits, CancellationToken.None));

        Assert.That(reason, Is.EqualTo(OutboundFailure.AnswerUnreadable));
    }

    [TestCase(false, Description = "the connection breaks off while the body is read")]
    [TestCase(true, Description = "the body does not decompress as its encoding says")]
    public async Task ABodyThatCannotBeRead_IsAFailure_NamingWhy(bool compressed)
    {
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(compressed ? new GZipStream(new MemoryStream(Encoding.ASCII.GetBytes("JFB_TEST: no gzip at all")), CompressionMode.Decompress) : new BreakingStream()),
        });

        var reason = await ReasonAsync(() => Calling(transport, allowHttp: false, Host).GetAsync(new Uri($"https://{Host}/list"), "application/json", Limits, CancellationToken.None));

        Assert.That(reason, Is.EqualTo(compressed ? OutboundFailure.AnswerUnreadable : OutboundFailure.NoConnection));
    }

    [Test]
    public void AStreamDisposedWhileItIsRead_IsNoAnswerOfTheHost_AndStaysWhatItIs()
    {
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BreakingStream(new ObjectDisposedException("JFB_TEST: the body"))),
        });

        Assert.ThrowsAsync<ObjectDisposedException>(() => Calling(transport, allowHttp: false, Host).GetAsync(new Uri($"https://{Host}/list"), "application/json", Limits, CancellationToken.None), "a fault of the code that read it, not an unreadable answer");
    }

    [Test]
    public void AFailure_NamesItsReasonAndStatus_NeverTheAddress()
    {
        _stub.Answer(new StubAnswer("GET", "/token-abc123", Status: 503));
        using var transport = StubTransport();
        var outbound = Outbound(transport, allowHttp: true, Host);

        var refused = Assert.ThrowsAsync<FormBuilderOutboundException>(() => outbound.GetAsync(At("/token-abc123"), "application/json", Limits, CancellationToken.None))!;
        var notAllowed = Assert.ThrowsAsync<FormBuilderOutboundException>(() => outbound.GetAsync(new Uri("http://other.example.com/token-abc123"), "application/json", Limits, CancellationToken.None))!;

        Assert.Multiple(async () =>
        {
            Assert.That(refused.Message, Is.EqualTo("The call to another host failed: Refused (503)."));
            Assert.That(notAllowed.Message, Is.EqualTo("The call to another host failed: HostNotAllowed."));
            Assert.That(refused.ToString() + notAllowed.ToString(), Does.Not.Contain("token-abc123").And.Not.Contain("example.com"));
        });
    }

    // Calls allowed to each of the hosts on its scheme's standard port and on the stub's.
    private OutboundHttp Outbound(IFormBuilderHttpTransport transport, bool allowHttp, params string[] hosts) =>
        Calling(transport, allowHttp, [.. hosts.SelectMany(host => new[] { host, $"{host}:{Port}" })]);

    // Calls allowed to exactly the entries given, as the host configuration names them.
    private static OutboundHttp Calling(IFormBuilderHttpTransport transport, bool allowHttp, params string[] entries)
    {
        var options = new FormBuilderOptions();
        foreach (var entry in entries)
        {
            options.Webhooks.AllowedHosts.Add(entry);
        }

        options.Webhooks.AllowHttp = allowHttp;
        return new OutboundHttp(transport, MsOptions.Create(options), TimeProvider.System);
    }

    private static async Task<OutboundFailure?> ReasonAsync(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (FormBuilderOutboundException exception)
        {
            return exception.Reason;
        }
    }

    // A loopback port nothing of this test listens on.
    private static int OtherPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private Uri At(string pathAndQuery) => new($"http://{Host}:{Port}{pathAndQuery}");

    // The transport the Testing assembly registers: the test's name resolves to loopback, and only the stub is reached.
    private FormBuilderHttpTransport StubTransport() =>
        new((name, _) => Task.FromResult(name == Host ? new[] { IPAddress.Loopback } : []), new IPEndPoint(IPAddress.Loopback, Port));

    // A body that sends one byte and then fails, by default as a connection that broke off.
    private sealed class BreakingStream(Exception? breaksWith = null) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (_sent)
            {
                throw breaksWith ?? new IOException("JFB_TEST: the connection broke off.");
            }

            _sent = true;
            buffer[offset] = (byte)'[';
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
