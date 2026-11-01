using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The spam protection settings (ROADMAP 7.1c, section 4.8, ADR-0139): set per site, blocking nothing by default; a range
/// is CIDR, written as the host writes it, once, and only one that can match is saved; and a request's address is held
/// against them, a mapped one as the IPv4 address it is.
/// </summary>
[TestFixture]
public sealed class ProtectionSettingsTests
{
    private readonly ProtectionSettingsValidator _validator = new();

    [Test]
    public void TheSection_IsProtection_AtSchemaVersion1_SetPerSite_AndBlocksNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProtectionSettings.Alias, Is.EqualTo("protection"));
            Assert.That(ProtectionSettings.SchemaVersion, Is.EqualTo(1));
            Assert.That(ProtectionSettings.PerSite, Is.True);
            Assert.That(new ProtectionSettings().BlockedRanges, Is.Empty);
            Assert.That(new ProtectionSettings().Blocks(IPAddress.Parse("203.0.113.7")), Is.False);
        });
    }

    [Test]
    public void ARange_IsWrittenAsTheHostWritesIt_Once()
    {
        var normal = _validator.Normalize(new ProtectionSettings { BlockedRanges = [" 203.0.113.0/24 ", "2001:DB8::/32", "2001:db8::/32", "198.51.100.7/24", "no range"] });

        Assert.That(normal.BlockedRanges, Is.EqualTo(new[] { "203.0.113.0/24", "2001:db8::/32", "198.51.100.0/24", "no range" }), "bits past a prefix mean nothing");
    }

    [TestCase("203.0.113.0/24")]
    [TestCase("203.0.113.7/32")]
    [TestCase("0.0.0.0/0")]
    [TestCase("2001:db8::/32")]
    public void ARangeInCidrNotation_IsSaved(string range) => Assert.That(ErrorsOf(range), Is.Empty);

    [TestCase("", TestName = "Nothing")]
    [TestCase("203.0.113.7", TestName = "An address without a prefix")]
    [TestCase("203.0.113.0/33", TestName = "A prefix past the address")]
    [TestCase("example.com/24", TestName = "A host name")]
    [TestCase("::ffff:203.0.113.0/120", TestName = "A mapped range, which nothing would match")]
    public void WhatIsNoRange_IsRefused(string range) =>
        Assert.That(ErrorsOf(range).Select(error => (error.Field, error.Code)), Is.EqualTo(new[] { ("protection.BlockedRanges", SettingsErrors.RangeCode) }));

    [Test]
    public void TooManyRanges_AreRefused()
    {
        string Range(int at) => $"10.{at / 256}.{at % 256}.0/24";
        var errors = new SettingsErrors(ProtectionSettings.Alias);
        _validator.Validate(new ProtectionSettings { BlockedRanges = [.. Enumerable.Range(0, ProtectionSettings.MaxBlockedRanges).Select(Range)] }, errors);
        var allowed = errors.Errors.Count;

        var over = new SettingsErrors(ProtectionSettings.Alias);
        _validator.Validate(new ProtectionSettings { BlockedRanges = [.. Enumerable.Range(0, ProtectionSettings.MaxBlockedRanges + 1).Select(Range)] }, over);

        Assert.Multiple(() =>
        {
            Assert.That(allowed, Is.Zero);
            Assert.That(over.Errors.Select(error => error.Code), Is.EqualTo(new[] { SettingsErrors.Range }));
        });
    }

    [TestCase("203.0.113.0/24", "203.0.113.0", true)]
    [TestCase("203.0.113.0/24", "203.0.113.255", true)]
    [TestCase("203.0.113.0/24", "203.0.114.0", false)]
    [TestCase("203.0.113.0/24", "::ffff:203.0.113.9", true)]
    [TestCase("203.0.113.0/24", "::ffff:203.0.114.9", false)]
    [TestCase("2001:db8::/32", "2001:db8:ffff::1", true)]
    [TestCase("2001:db8::/32", "2001:db9::1", false)]
    [TestCase("2001:db8::/32", "203.0.113.9", false)]
    public void AnAddress_LiesInARange_OrNot(string range, string address, bool blocks) =>
        Assert.That(new ProtectionSettings { BlockedRanges = [range] }.Blocks(IPAddress.Parse(address)), Is.EqualTo(blocks));

    [Test]
    public void ALaterRange_Counts_AndNoAddress_LiesInNone()
    {
        var settings = new ProtectionSettings { BlockedRanges = ["198.51.100.0/24", "203.0.113.0/24"] };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Blocks(IPAddress.Parse("203.0.113.9")), Is.True);
            Assert.That(settings.Blocks(null), Is.False, "the host could not tell");
        });
    }

    private IReadOnlyList<SettingsError> ErrorsOf(string range)
    {
        var errors = new SettingsErrors(ProtectionSettings.Alias);
        _validator.Validate(new ProtectionSettings { BlockedRanges = [range] }, errors);
        return errors.Errors;
    }
}
