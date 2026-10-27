using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Outbound;

/// <summary>
/// The hosts a site calls (ADR-0076): <c>JironFormBuilder:Webhooks:AllowedHosts</c> holds host names and IP addresses,
/// each compared as an address writes its host, and start-up refuses anything else and a host named twice.
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
            $"{Hosts}0 ('https://events.example.com') must be a host name such as 'events.example.com' or an IP address, with no scheme, port, path or wildcard.",
            $"{Hosts}2 ('*.example.com') must be a host name such as 'events.example.com' or an IP address, with no scheme, port, path or wildcard.",
            $"{Hosts}3 ('EVENTS.example.com') names a host again; {Hosts}1 names it already. Keep one.",
            $"{Hosts}5 ('[::1]') names a host again; {Hosts}4 names it already. Keep one.",
        }));
    }

    [Test]
    public void AnEmptyAllowedHost_IsRefused()
    {
        var options = new FormBuilderOptions();
        options.Webhooks.AllowedHosts.Add(null!);

        Assert.That(HostOptions.Failures(options), Is.EqualTo(new[] { $"{Hosts}0 ('') must be a host name such as 'events.example.com' or an IP address, with no scheme, port, path or wildcard." }));
    }
}
