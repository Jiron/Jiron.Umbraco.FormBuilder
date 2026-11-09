using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// What an entry keeps of the visitor's IP address (ADR-0009, ADR-0224): nothing, the address with its end removed (an
/// IPv4 /24, an IPv6 /48), or all of it; and the site's rule that keeps a stored full mode anonymised unless the host allows it.
/// </summary>
[TestFixture]
public sealed class VisitorAddressesTests
{
    [TestCase("203.0.113.42", "none", null)]
    [TestCase("203.0.113.42", "inherit", null)]
    [TestCase("203.0.113.42", "anonymised", "203.0.113.0")]
    [TestCase("203.0.113.42", "full", "203.0.113.42")]
    [TestCase("2001:db8:85a3:8d3:1319:8a2e:370:7348", "anonymised", "2001:db8:85a3::")]
    [TestCase("2001:db8:85a3:8d3:1319:8a2e:370:7348", "full", "2001:db8:85a3:8d3:1319:8a2e:370:7348")]
    [TestCase("::ffff:203.0.113.42", "anonymised", "203.0.113.0")]
    [TestCase("::ffff:203.0.113.42", "full", "203.0.113.42")]
    [TestCase("fe80::1%3", "full", "fe80::1")]
    [TestCase("127.0.0.1", "anonymised", "127.0.0.0")]
    public void AnAddress_IsKeptAsItsModeSays(string address, string mode, string? kept)
    {
        var handling = Enum.Parse<IPHandling>(mode, ignoreCase: true);

        Assert.That(VisitorAddresses.Kept(IPAddress.Parse(address), handling), Is.EqualTo(kept));
    }

    [Test]
    public void TheSitesFullMode_IsRefusedAndReadAsAnonymised_UnlessTheHostAllowsIt()
    {
        var stored = new PrivacySettings { IP = IPHandling.Full };
        var refused = new SettingsLocks();
        var allowing = new FormBuilderOptions();
        allowing.Privacy.AllowFullIp = true;
        var allowed = new SettingsLocks();

        var read = new FullIPRule(Options.Create(new FormBuilderOptions())).Apply(stored, refused);
        var kept = new FullIPRule(Options.Create(allowing)).Apply(stored, allowed);

        Assert.Multiple(() =>
        {
            Assert.That(read.IP, Is.EqualTo(IPHandling.Anonymised));
            Assert.That(refused.All, Is.EqualTo(new Dictionary<string, SettingsLock> { ["ip"] = new(SettingsLockKind.Restricted, "JironFormBuilder:Privacy:AllowFullIp") }));
            Assert.That(kept, Is.SameAs(stored));
            Assert.That(allowed.All, Is.Empty);
        });
    }

    [Test]
    public void TheSite_KeepsNoAddressByDefault_AndCannotInheritAMode()
    {
        var validator = new PrivacySettingsValidator();
        var errors = new SettingsErrors("privacy");

        validator.Validate(new PrivacySettings { IP = IPHandling.Inherit }, errors);

        Assert.Multiple(() =>
        {
            Assert.That(new PrivacySettings().IP, Is.EqualTo(IPHandling.None));
            Assert.That(errors.Errors.Select(error => (error.Field, error.Code)), Is.EqualTo(new[] { ("privacy.ip", SettingsErrors.IPHandlingCode) }));
        });
    }
}
