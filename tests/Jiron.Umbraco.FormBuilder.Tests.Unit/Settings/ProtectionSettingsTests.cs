using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

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
    public void TheStrictest_TakesTheHardestOfEach_AndEveryListedItem()
    {
        var strictest = ProtectionSettings.Strictest(
        [
            new ProtectionSettings { MaxLinks = 5, ProofBits = 12, BlockedRanges = ["203.0.113.0/24"], BlockedWords = ["Casino"], BlockedDomains = ["a.test"], ContentRules = ContentRuleLevel.Off },
            new ProtectionSettings { Level = ProtectionLevel.Captcha, CaptchaProvider = "hcaptcha", MaxLinks = 1, ProofBits = 10, BlockedRanges = ["203.0.113.0/24", "2001:db8::/32"], BlockedWords = ["casino", "pills"], BlockedDomains = ["a.test", "b.test"], ContentRules = ContentRuleLevel.Strict },
            new ProtectionSettings { Level = ProtectionLevel.ProofOfWork, MaxLinks = 3, ProofBits = 20, ContentRules = ContentRuleLevel.Normal },
            new ProtectionSettings { Level = ProtectionLevel.Captcha, CaptchaProvider = "recaptchaV2" },
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(strictest.Level, Is.EqualTo(ProtectionLevel.Captcha));
            Assert.That(strictest.CaptchaProvider, Is.EqualTo("hcaptcha"), "the first that asks for one");
            Assert.That(strictest.ProofBits, Is.EqualTo(20));
            Assert.That(strictest.MaxLinks, Is.EqualTo(1));
            Assert.That(strictest.ContentRules, Is.EqualTo(ContentRuleLevel.Strict));
            Assert.That(strictest.BlockedRanges, Is.EqualTo(new[] { "203.0.113.0/24", "2001:db8::/32" }));
            Assert.That(strictest.BlockedWords, Is.EqualTo(new[] { "Casino", "pills" }), "each word once, as rules compare them");
            Assert.That(strictest.BlockedDomains, Is.EqualTo(new[] { "a.test", "b.test" }));
        });
    }

    [Test]
    public void TheCaptchasConsent_StartsExplicit_ForMarketing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ProtectionSettings().CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Explicit));
            // Cookiebot files a captcha's cookie as necessary (the owner, ADR-0199).
            Assert.That(new ProtectionSettings().CookiebotCategory, Is.EqualTo("necessary"));
        });
    }

    // A switch between the two the owner named (ADR-0199).
    [TestCase("necessary", true)]
    [TestCase("marketing", true)]
    [TestCase("preferences", false)]
    [TestCase("statistics", false)]
    [TestCase("Necessary", false)]
    [TestCase("", false)]
    public void ACookiebotCategory_IsNecessaryOrMarketing(string category, bool allowed) =>
        Assert.That(Codes(new ProtectionSettings { CookiebotCategory = category }), Is.EqualTo(allowed ? Array.Empty<string>() : new[] { SettingsErrors.ProtectionLevelCode }));

    [Test]
    public void TheStrictest_AsksForConsent_UnlessEverySiteAgrees()
    {
        var cmp = new ProtectionSettings { CaptchaConsent = CaptchaConsentMode.Cmp, CookiebotCategory = "marketing" };

        Assert.Multiple(() =>
        {
            Assert.That(ProtectionSettings.Strictest([cmp, cmp]).CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Cmp));
            Assert.That(ProtectionSettings.Strictest([cmp, cmp]).CookiebotCategory, Is.EqualTo("marketing"));
            Assert.That(ProtectionSettings.Strictest([cmp, new ProtectionSettings { CaptchaConsent = CaptchaConsentMode.Implicit }]).CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Explicit));
            Assert.That(ProtectionSettings.Strictest([cmp, cmp with { CookiebotCategory = "necessary" }]).CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Explicit), "another category is another consent");
        });
    }

    [TestCase(nameof(CaptchaConsentMode.Explicit), "\"explicit\"")]
    [TestCase(nameof(CaptchaConsentMode.Cookiebot), "\"cookiebot\"")]
    [TestCase(nameof(CaptchaConsentMode.Cmp), "\"cmp\"")]
    [TestCase(nameof(CaptchaConsentMode.Implicit), "\"implicit\"")]
    public void AConsentMode_IsStoredByItsName(string name, string stored)
    {
        var mode = Enum.Parse<CaptchaConsentMode>(name);
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Serialize(mode), Is.EqualTo(stored));
            Assert.That(JsonSerializer.Deserialize<CaptchaConsentMode>(stored), Is.EqualTo(mode));
        });
    }

    [Test]
    public void TheImplicitMode_ReadsAsExplicit_UnlessTheHostAllowsIt()
    {
        var stored = new ProtectionSettings { CaptchaConsent = CaptchaConsentMode.Implicit };
        var refusing = new ImplicitCaptchaConsentRule(Options.Create(new FormBuilderOptions()));
        var allowing = new FormBuilderOptions();
        allowing.Privacy.AllowImplicitCaptchaConsent = true;
        var locks = new SettingsLocks();

        var read = refusing.Apply(stored, locks);
        var free = new SettingsLocks();

        Assert.Multiple(() =>
        {
            Assert.That(read.CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Explicit));
            Assert.That(locks.All["captchaConsent"], Is.EqualTo(new SettingsLock(SettingsLockKind.Restricted, "JironFormBuilder:Privacy:AllowImplicitCaptchaConsent")));
            Assert.That(refusing.Apply(new ProtectionSettings { CaptchaConsent = CaptchaConsentMode.Cmp }, new SettingsLocks()).CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Cmp), "another mode stays");
            Assert.That(new ImplicitCaptchaConsentRule(Options.Create(allowing)).Apply(stored, free).CaptchaConsent, Is.EqualTo(CaptchaConsentMode.Implicit));
            Assert.That(free.All, Is.Empty);
        });
    }

    [Test]
    public void ASaveOfTheImplicitMode_IsRefused_WhereTheHostDoesNotAllowIt()
    {
        var rules = new[] { new ImplicitCaptchaConsentRule(Options.Create(new FormBuilderOptions())) };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => SettingsHostRules.CheckSave(new ProtectionSettings { CaptchaConsent = CaptchaConsentMode.Implicit }, new HashSet<string> { "captchaConsent" }, new FormBuilderOptions(), rules, new SettingsErrors("protection")),
                Throws.TypeOf<FormBuilderSettingLockedException>());
            Assert.That(
                () => SettingsHostRules.CheckSave(new ProtectionSettings { CaptchaConsent = CaptchaConsentMode.Cookiebot }, new HashSet<string> { "captchaConsent" }, new FormBuilderOptions(), rules, new SettingsErrors("protection")),
                Throws.Nothing);
        });
    }

    [Test]
    public void TheStrictest_BelowTheCaptchaLevel_NamesNoProvider()
    {
        var strictest = ProtectionSettings.Strictest([new ProtectionSettings(), new ProtectionSettings { Level = ProtectionLevel.ProofOfWork }]);

        Assert.Multiple(() =>
        {
            Assert.That(strictest.Level, Is.EqualTo(ProtectionLevel.ProofOfWork));
            Assert.That(strictest.CaptchaProvider, Is.Null);
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
    public void TheContentRules_StartNormal_WithNothingBlocked_AndThreeLinksAllowed()
    {
        var settings = new ProtectionSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.ContentRules, Is.EqualTo(ContentRuleLevel.Normal));
            Assert.That(settings.BlockedWords, Is.Empty);
            Assert.That(settings.BlockedDomains, Is.Empty);
            Assert.That(settings.MaxLinks, Is.EqualTo(3));
        });
    }

    [Test]
    public void AWord_LosesItsSurroundingSpace_AndStandsOnceHoweverItIsWritten()
    {
        var normal = _validator.Normalize(new ProtectionSettings { BlockedWords = [" Casino ", "casino", "CASINO", "cheap pills"] });

        Assert.That(normal.BlockedWords, Is.EqualTo(new[] { "Casino", "cheap pills" }));
    }

    [Test]
    public void ADomain_IsWrittenAsItIsCompared_Once()
    {
        var normal = _validator.Normalize(new ProtectionSettings { BlockedDomains = [" Spam.Example ", "@spam.example", "spam.example.", "b\u00fccher.example", "not a domain"] });

        Assert.That(normal.BlockedDomains, Is.EqualTo(new[] { "spam.example", "xn--bcher-kva.example", "not a domain" }));
    }

    [TestCase("", TestName = "No word")]
    [TestCase("two\nlines", TestName = "A word on two lines")]
    public void WhatIsNoWord_IsRefused(string word) =>
        Assert.That(Codes(new ProtectionSettings { BlockedWords = [word] }), Is.EqualTo(new[] { SettingsErrors.WordCode }));

    [Test]
    public void AWord_MayBeAsLongAsTheLimit_AndNoLonger()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Codes(new ProtectionSettings { BlockedWords = [new string('a', ProtectionSettings.MaxBlockedWordLength)] }), Is.Empty);
            Assert.That(Codes(new ProtectionSettings { BlockedWords = [new string('a', ProtectionSettings.MaxBlockedWordLength + 1)] }), Is.EqualTo(new[] { SettingsErrors.WordCode }));
        });
    }

    [TestCase("", TestName = "No domain")]
    [TestCase("not a domain", TestName = "Words")]
    [TestCase("Spam.Example", TestName = "A domain not as it is compared")]
    public void WhatIsNoDomain_IsRefused(string domain) =>
        Assert.That(Codes(new ProtectionSettings { BlockedDomains = [domain] }), Is.EqualTo(new[] { SettingsErrors.DomainCode }));

    [Test]
    public void TooManyWordsOrDomains_AreRefused()
    {
        string[] Items(int count) => [.. Enumerable.Range(0, count).Select(at => $"item{at}.example")];

        Assert.Multiple(() =>
        {
            Assert.That(Codes(new ProtectionSettings { BlockedWords = Items(ProtectionSettings.MaxBlockedItems) }), Is.Empty);
            Assert.That(Codes(new ProtectionSettings { BlockedWords = Items(ProtectionSettings.MaxBlockedItems + 1) }), Is.EqualTo(new[] { SettingsErrors.Range }));
            Assert.That(Codes(new ProtectionSettings { BlockedDomains = Items(ProtectionSettings.MaxBlockedItems) }), Is.Empty);
            Assert.That(Codes(new ProtectionSettings { BlockedDomains = Items(ProtectionSettings.MaxBlockedItems + 1) }), Is.EqualTo(new[] { SettingsErrors.Range }));
        });
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(ProtectionSettings.MostMaxLinks, true)]
    [TestCase(ProtectionSettings.MostMaxLinks + 1, false)]
    public void TheMostLinks_AreHeldToTheirRange(int links, bool saved) =>
        Assert.That(Codes(new ProtectionSettings { MaxLinks = links }), saved ? Is.Empty : Is.EqualTo(new[] { SettingsErrors.Range }));

    [Test]
    public void ASitesContentRules_AreNeverInherited() =>
        Assert.That(Codes(new ProtectionSettings { ContentRules = ContentRuleLevel.Inherit }), Is.EqualTo(new[] { SettingsErrors.ContentRulesCode }));

    [TestCase(ProtectionLevel.Standard, true)]
    [TestCase(ProtectionLevel.ProofOfWork, true)]
    [TestCase(ProtectionLevel.Inherit, false)]
    public void ASitesLevel_IsNeverInherited(ProtectionLevel level, bool saved) =>
        Assert.That(Codes(new ProtectionSettings { Level = level }), saved ? Is.Empty : Is.EqualTo(new[] { SettingsErrors.ProtectionLevelCode }));

    [TestCase(ProtectionLevel.Captcha, "hcaptcha", true, TestName = "The captcha level with its provider")]
    [TestCase(ProtectionLevel.Captcha, null, false, TestName = "The captcha level without a provider")]
    [TestCase(ProtectionLevel.Captcha, " ", false, TestName = "The captcha level with a blank provider")]
    [TestCase(ProtectionLevel.Standard, "hcaptcha", false, TestName = "A provider at the standard level")]
    [TestCase(ProtectionLevel.ProofOfWork, "hcaptcha", false, TestName = "A provider at the proof of work level")]
    public void ASitesCaptchaProvider_GoesWithItsLevel(ProtectionLevel level, string? provider, bool saved) =>
        Assert.That(Codes(new ProtectionSettings { Level = level, CaptchaProvider = provider }), saved ? Is.Empty : Is.EqualTo(new[] { SettingsErrors.ProtectionLevelCode }));

    [Test]
    public void TheCaptchaOfAForm_IsItsOwn_OrItsSitesWhenItInherits_AndNoneOtherwise()
    {
        var own = new CaptchaChoice { Provider = "own" };
        var site = new ProtectionSettings { Level = ProtectionLevel.Captcha, CaptchaProvider = "sites" };
        static FormDefinition Form(ProtectionLevel level, CaptchaChoice? choice = null) => new()
        {
            Key = Guid.Empty,
            Settings = new FormSettings { Protection = new FormProtectionSettings { Level = level, Captcha = choice } },
        };

        Assert.Multiple(() =>
        {
            Assert.That(site.CaptchaOf(Form(ProtectionLevel.Captcha, own)), Is.SameAs(own));
            Assert.That(new ProtectionSettings().CaptchaOf(Form(ProtectionLevel.Captcha, own)), Is.SameAs(own));
            Assert.That(site.CaptchaOf(Form(ProtectionLevel.Inherit))?.Provider, Is.EqualTo("sites"));
            Assert.That(site.CaptchaOf(Form(ProtectionLevel.Standard)), Is.Null);
            Assert.That(site.CaptchaOf(Form(ProtectionLevel.ProofOfWork)), Is.Null);
            Assert.That(new ProtectionSettings().CaptchaOf(Form(ProtectionLevel.Inherit)), Is.Null);
            Assert.That(new ProtectionSettings { Level = ProtectionLevel.ProofOfWork, CaptchaProvider = "stale" }.CaptchaOf(Form(ProtectionLevel.Inherit)), Is.Null);
        });
    }

    [TestCase(7, false)]
    [TestCase(8, true)]
    [TestCase(24, true)]
    [TestCase(25, false)]
    public void APuzzlesDifficulty_IsHeldToItsRange(int bits, bool saved) =>
        Assert.That(Codes(new ProtectionSettings { ProofBits = bits }), saved ? Is.Empty : Is.EqualTo(new[] { SettingsErrors.Range }));

    [Test]
    public void TheLevel_StartsStandard_WithAPuzzleOfSixteenBits()
    {
        var settings = new ProtectionSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.Level, Is.EqualTo(ProtectionLevel.Standard));
            Assert.That(settings.ProofBits, Is.EqualTo(16));
        });
    }

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

    private List<string> Codes(ProtectionSettings settings)
    {
        var errors = new SettingsErrors(ProtectionSettings.Alias);
        _validator.Validate(settings, errors);
        return [.. errors.Errors.Select(error => error.Code)];
    }

    private IReadOnlyList<SettingsError> ErrorsOf(string range)
    {
        var errors = new SettingsErrors(ProtectionSettings.Alias);
        _validator.Validate(new ProtectionSettings { BlockedRanges = [range] }, errors);
        return errors.Errors;
    }
}
