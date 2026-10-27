using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Outbound;

/// <summary>
/// The hosts a site calls (ADR-0076): <c>JironFormBuilder:Webhooks:AllowedHosts</c> holds host names and IP addresses,
/// each compared as an address writes its host, with a port after a colon where a call may use another than the
/// scheme's standard one (ADR-0082), and start-up refuses anything else and a host and port named twice.
/// </summary>
[TestFixture]
public sealed class OutboundHostsTests
{
    private const string Hosts = "JironFormBuilder:Webhooks:AllowedHosts:";

    [TestCase("events.example.com", "events.example.com")]
    [TestCase("Events.Example.COM", "events.example.com")]
    [TestCase("localhost", "localhost")]
    [TestCase("127.0.0.1", "127.0.0.1")]
    [TestCase("::1", "::1")]
    [TestCase("[::1]", "::1")]
    [TestCase("2001:DB8::1", "2001:db8::1")]
    [TestCase("[2001:0db8:0000::1]", "2001:db8::1")]
    public void AHost_IsComparedAsAnAddressWritesIt(string host, string compared) =>
        Assert.That(OutboundHosts.Normalize(host), Is.EqualTo(compared));

    [Test]
    public void AnInternationalName_IsComparedInItsAsciiForm() =>
        Assert.That(OutboundHosts.Normalize($"B{(char)0xFC}cher.example"), Is.EqualTo("xn--bcher-kva.example"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("https://events.example.com")]
    [TestCase("events.example.com:443")]
    [TestCase("[::1]:443")]
    [TestCase("events.example.com/path")]
    [TestCase("*.example.com")]
    [TestCase("events example.com")]
    [TestCase("user@events.example.com")]
    [TestCase("events.example.com?query")]
    [TestCase("[events.example.com]")]
    [TestCase("[127.0.0.1]")]
    public void AnythingButOneHost_IsNoHost(string? host) =>
        Assert.That(OutboundHosts.Normalize(host), Is.Null);

    [Test]
    public void ANameOfMoreThan253Characters_IsNoHost()
    {
        var longest = string.Join('.', Enumerable.Repeat(new string('a', 63), 3)) + "." + new string('b', 61);

        Assert.Multiple(() =>
        {
            Assert.That(longest, Has.Length.EqualTo(253));
            Assert.That(OutboundHosts.Normalize(longest), Is.EqualTo(longest));
            Assert.That(OutboundHosts.Normalize(longest + "b"), Is.Null);
        });
    }

    [TestCase("events.example.com", "events.example.com", null)]
    [TestCase("Events.Example.COM:8443", "events.example.com", 8443)]
    [TestCase("events.example.com:443", "events.example.com", 443)]
    [TestCase("events.example.com:1", "events.example.com", 1)]
    [TestCase("events.example.com:65535", "events.example.com", 65535)]
    [TestCase("203.0.113.7:8080", "203.0.113.7", 8080)]
    [TestCase("[2001:DB8::1]:8443", "2001:db8::1", 8443)]
    [TestCase("[::1]", "::1", null)]
    [TestCase("2001:db8::1:8443", "2001:db8::1:8443", null, Description = "without brackets, the colons are the address's own")]
    public void AnEntry_IsAHost_AndThePortItNames(string entry, string host, int? port)
    {
        var parsed = OutboundHosts.Parse(entry);

        Assert.That((parsed?.Host, parsed?.Port), Is.EqualTo((host, port)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(":8443")]
    [TestCase("events.example.com:")]
    [TestCase("events.example.com:0")]
    [TestCase("events.example.com:65536")]
    [TestCase("events.example.com:-1")]
    [TestCase("events.example.com:+443")]
    [TestCase("events.example.com: 443")]
    [TestCase("events.example.com:8443:1")]
    [TestCase("events.example.com:443/path")]
    [TestCase("https://events.example.com:443")]
    [TestCase("*.example.com:443")]
    [TestCase("[2001:db8::1]:")]
    [TestCase("[2001:db8::1]8443")]
    [TestCase("[events.example.com]:8443")]
    public void AnEntryWithAnythingButOneHostAndOnePort_IsNone(string? entry) =>
        Assert.That(OutboundHosts.Parse(entry), Is.Null);

    [Test]
    public void TheAllowedHosts_AreNoneByDefault_AndHttpIsOff() =>
        Assert.That((new FormBuilderOptions().Webhooks.AllowedHosts.Count, new FormBuilderOptions().Webhooks.AllowHttp), Is.EqualTo((0, false)));

    [Test]
    public void TheAllowedHosts_Bind_AndPass()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Hosts}0"] = "events.example.com",
            [$"{Hosts}1"] = "203.0.113.7",
            [$"{Hosts}2"] = "[2001:db8::1]",
            ["JironFormBuilder:Webhooks:AllowHttp"] = "true",
        };

        var options = HostOptions.Bind(settings);

        Assert.Multiple(() =>
        {
            Assert.That(options.Webhooks.AllowedHosts, Is.EqualTo(new[] { "events.example.com", "203.0.113.7", "[2001:db8::1]" }));
            Assert.That(options.Webhooks.AllowHttp, Is.True);
            Assert.That(HostOptions.Failures(settings), Is.Empty);
        });
    }

    [Test]
    public void AnAllowedHost_ThatIsNoHost_OrNamesAHostAgain_IsRefused_NamingItsKey()
    {
        var failures = HostOptions.Failures(new Dictionary<string, string?>
        {
            [$"{Hosts}0"] = "https://events.example.com",
            [$"{Hosts}1"] = "events.example.com",
            [$"{Hosts}2"] = "*.example.com",
            [$"{Hosts}3"] = "EVENTS.example.com",
            [$"{Hosts}4"] = "::1",
            [$"{Hosts}5"] = "[::1]",
        });

        Assert.That(failures, Is.EqualTo(new[]
        {
            $"{Hosts}0 ('https://events.example.com') must be a host name such as 'events.example.com' or an IP address, optionally with a port such as 'events.example.com:8443' (an IPv6 address in brackets: '[2001:db8::1]:8443'), and no scheme, path or wildcard.",
            $"{Hosts}2 ('*.example.com') must be a host name such as 'events.example.com' or an IP address, optionally with a port such as 'events.example.com:8443' (an IPv6 address in brackets: '[2001:db8::1]:8443'), and no scheme, path or wildcard.",
            $"{Hosts}3 ('EVENTS.example.com') names a host and port again; {Hosts}1 names them already. Keep one.",
            $"{Hosts}5 ('[::1]') names a host and port again; {Hosts}4 names them already. Keep one.",
        }));
    }

    [Test]
    public void AnEntryWithAPort_Passes_BesideTheSameHostWithout()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Hosts}0"] = "events.example.com",
            [$"{Hosts}1"] = "events.example.com:8443",
            [$"{Hosts}2"] = "events.example.com:9443",
            [$"{Hosts}3"] = "[2001:db8::1]:8443",
        };

        Assert.Multiple(() =>
        {
            Assert.That(HostOptions.Bind(settings).Webhooks.AllowedHosts, Has.Count.EqualTo(4));
            Assert.That(HostOptions.Failures(settings), Is.Empty);
        });
    }

    [Test]
    public void AnEntryWithAPort_ThatIsNone_OrNamesAHostAndPortAgain_IsRefused_NamingItsKey()
    {
        var failures = HostOptions.Failures(new Dictionary<string, string?>
        {
            [$"{Hosts}0"] = "events.example.com:8443",
            [$"{Hosts}1"] = "events.example.com:0",
            [$"{Hosts}2"] = "EVENTS.example.com:8443",
            [$"{Hosts}3"] = "[2001:db8::1]:8443",
            [$"{Hosts}4"] = "[2001:0db8:0000::1]:8443",
        });

        Assert.That(failures, Is.EqualTo(new[]
        {
            $"{Hosts}1 ('events.example.com:0') must be a host name such as 'events.example.com' or an IP address, optionally with a port such as 'events.example.com:8443' (an IPv6 address in brackets: '[2001:db8::1]:8443'), and no scheme, path or wildcard.",
            $"{Hosts}2 ('EVENTS.example.com:8443') names a host and port again; {Hosts}0 names them already. Keep one.",
            $"{Hosts}4 ('[2001:0db8:0000::1]:8443') names a host and port again; {Hosts}3 names them already. Keep one.",
        }));
    }

    [Test]
    public void AnEmptyAllowedHost_IsRefused()
    {
        var options = new FormBuilderOptions();
        options.Webhooks.AllowedHosts.Add(null!);

        Assert.That(HostOptions.Failures(options), Is.EqualTo(new[] { $"{Hosts}0 ('') must be a host name such as 'events.example.com' or an IP address, optionally with a port such as 'events.example.com:8443' (an IPv6 address in brackets: '[2001:db8::1]:8443'), and no scheme, path or wildcard." }));
    }
}
