using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Emails;

/// <summary>What addresses and header values may be, and how the limits count a mailbox (ROADMAP 8.2, ADR-0012).</summary>
public sealed class EmailAddressesTests
{
    [TestCase("anna@jfb.test")]
    [TestCase("anna+news@jfb.test")]
    [TestCase("\u00c4nne@jfb.test")]
    public void OneAddress_IsOne(string address) => Assert.That(EmailAddresses.IsOne(address), Is.True);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("anna")]
    [TestCase("Anna <anna@jfb.test>")]
    [TestCase("<anna@jfb.test>")]
    [TestCase("anna@jfb.test, ben@jfb.test")]
    [TestCase("anna@jfb.test,ben@jfb.test")]
    [TestCase("anna@jfb.test;ben@jfb.test")]
    [TestCase("anna@jfb.test\r\nBcc: ben@jfb.test")]
    [TestCase(" anna@jfb.test")]
    [TestCase("\"x,victim1@a.test,victim2@b.test,y\"@jfb.test")]
    [TestCase("\"anna\"@jfb.test")]
    [TestCase("\"v\\ictim\"@jfb.test")]
    public void AnythingElse_IsNot(string? address) => Assert.That(EmailAddresses.IsOne(address), Is.False);

    [Test]
    public void AnAddressWithALoneSurrogate_IsNotOne() =>
        Assert.That(EmailAddresses.IsOne("a" + (char)0xd800 + "@jfb.test"), Is.False);

    [Test]
    public void AnAddressTooLongForMail_IsNotOne() =>
        Assert.That(EmailAddresses.IsOne(new string('a', EmailAddresses.MaxLength) + "@jfb.test"), Is.False);

    [TestCase("Anna@JFB.test", "anna@jfb.test")]
    [TestCase("  anna@jfb.test ", "anna@jfb.test")]
    [TestCase("anna+news@jfb.test", "anna@jfb.test")]
    [TestCase("ANNA+News+more@jfb.test", "anna@jfb.test")]
    [TestCase("\u00c4NNE@jfb.test", "\u00e4nne@jfb.test")]
    [TestCase("A\u0308nne@jfb.test", "\u00e4nne@jfb.test")]
    [TestCase("\u00d6ZG\u00dcR+x@JFB.test", "\u00f6zg\u00fcr@jfb.test")]
    public void TheKey_OfEachSpellingOfAMailbox_IsTheSame(string spelling, string key) =>
        Assert.That(EmailAddresses.KeyOf(spelling), Is.EqualTo(key));

    [TestCase("anna@\uff4afb.test", "anna@jfb.test")]
    [TestCase("anna@jfb\u3002test", "anna@jfb.test")]
    [TestCase("anna@jfb.test.", "anna@jfb.test")]
    [TestCase("\uff41nna@jfb.test", "anna@jfb.test")]
    [TestCase("anna@b\u00fccher.test", "anna@xn--bcher-kva.test")]
    [TestCase("anna@bu\u0308cher.test", "anna@xn--bcher-kva.test")]
    [TestCase("anna@XN--BCHER-KVA.test", "anna@xn--bcher-kva.test")]
    public void TheKey_OfADomainsSpellings_IsTheSame(string spelling, string key)
    {
        Assert.That(EmailAddresses.KeyOf(spelling), Is.EqualTo(key));
    }

    [Test]
    public void AKeyTooLongToStore_IsNoKey()
    {
        var address = new string((char)0xfdfa, 200) + "@jfb.test";

        Assert.Multiple(() =>
        {
            Assert.That(EmailAddresses.IsOne(address), Is.True, "the address fits");
            Assert.That(EmailAddresses.KeyOf(address), Is.Null);
        });
    }

    [Test]
    public void APlus_AtTheStartOrInTheDomain_IsNoTag() =>
        Assert.Multiple(() =>
        {
            Assert.That(EmailAddresses.KeyOf("+anna@jfb.test"), Is.EqualTo("+anna@jfb.test"));
            Assert.That(EmailAddresses.KeyOf("not an address"), Is.Null);
        });

    [TestCase("Line one\r\nBcc: ben@jfb.test", "Line one  Bcc: ben@jfb.test")]
    [TestCase("Line one\nLine two", "Line one Line two")]
    [TestCase("Line one\rLine two", "Line one Line two")]
    [TestCase("Tab\there", "Tab here")]
    [TestCase("  both ends  ", "both ends")]
    public void AHeaderValue_IsOneLine_WithSpacesForItsBreaks(string value, string line) =>
        Assert.That(EmailHeaders.Clean(value), Is.EqualTo(line));

    [Test]
    public void TheUnicodeLineBreaks_AreBreaksToo()
    {
        var value = "a" + (char)0x2028 + "b" + (char)0x2029 + "c" + (char)0x0085 + "d";

        Assert.That(EmailHeaders.Clean(value), Is.EqualTo("a b c d"));
    }

    [Test]
    public void NoValue_StaysNoValue() => Assert.That(EmailHeaders.Clean(null), Is.Null);

    [TestCase(null, "hello@jfb.test")]
    [TestCase("  ", "hello@jfb.test")]
    [TestCase("Jiron", "\"Jiron\" <hello@jfb.test>")]
    [TestCase("Sales, Support <x@y.test>", "\"Sales, Support <x@y.test>\" <hello@jfb.test>")]
    [TestCase("The \"best\" \\ team\r\nBcc: x@y.test", "\"The \\\"best\\\" \\\\ team  Bcc: x@y.test\" <hello@jfb.test>")]
    public void TheSender_QuotesItsName_SoItCannotStartAnotherAddress(string? name, string sender) =>
        Assert.That(EmailHeaders.Sender(name, "hello@jfb.test"), Is.EqualTo(sender));

    [Test]
    public void TheSettings_AreNormalised_BeforeTheyAreChecked()
    {
        var normal = new EmailSettingsValidator().Normalize(new EmailSettings
        {
            SenderName = " Jiron\nteam ",
            SenderAddress = " hello@jfb.test ",
            TeamRecipients = [" a@jfb.test", "A@jfb.test", "", "b@jfb.test"],
            ForwardAddresses = ["  "],
        });

        Assert.Multiple(() =>
        {
            Assert.That(normal.SenderName, Is.EqualTo("Jiron team"));
            Assert.That(normal.SenderAddress, Is.EqualTo("hello@jfb.test"));
            Assert.That(normal.TeamRecipients, Is.EqualTo(new[] { "a@jfb.test", "b@jfb.test" }));
            Assert.That(normal.ForwardAddresses, Is.Empty);
        });
    }

    [Test]
    public void ABlankName_AndABlankAddress_MeanNone()
    {
        var normal = new EmailSettingsValidator().Normalize(new EmailSettings { SenderName = "  ", SenderAddress = " " });

        Assert.That((normal.SenderName, normal.SenderAddress), Is.EqualTo(((string?)null, (string?)null)));
    }

    [Test]
    public void GoodSettings_HaveNoErrors() => Assert.That(ErrorsOf(new EmailSettings
    {
        SenderName = "Jiron",
        SenderAddress = "hello@jfb.test",
        TeamRecipients = ["a@jfb.test"],
        ForwardAddresses = ["b@jfb.test"],
    }), Is.Empty);

    [Test]
    public void EachBadValue_IsNamed()
    {
        var errors = ErrorsOf(new EmailSettings
        {
            SenderName = new string('n', EmailSettingsValidator.MaxNameLength + 1),
            SenderAddress = "Jiron <hello@jfb.test>",
            TeamRecipients = ["nope"],
            ForwardAddresses = [.. Enumerable.Range(0, EmailSettingsValidator.MaxAddresses + 1).Select(i => $"a{i}@jfb.test")],
        });

        Assert.That(errors, Is.EquivalentTo(new[]
        {
            ("email.senderName", SettingsErrors.Range),
            ("email.senderAddress", SettingsErrors.AddressCode),
            ("email.teamRecipients", SettingsErrors.AddressCode),
            ("email.forwardAddresses", SettingsErrors.Range),
        }));
    }

    [Test]
    public void AListThatWouldNotFitAMailsColumn_IsRefused()
    {
        var errors = ErrorsOf(new EmailSettings { TeamRecipients = [.. Enumerable.Range(0, 80).Select(i => new string('a', 50) + $"{i}@jfb.test")] });

        Assert.That(errors, Is.EqualTo(new[] { ("email.teamRecipients", SettingsErrors.Range) }));
    }

    [Test]
    public void TheLongestName_AndTheLongestLists_AreAllowed() => Assert.That(ErrorsOf(new EmailSettings
    {
        SenderName = new string('n', EmailSettingsValidator.MaxNameLength),
        TeamRecipients = [.. Enumerable.Range(0, EmailSettingsValidator.MaxAddresses).Select(i => $"a{i}@jfb.test")],
    }), Is.Empty);

    private static List<(string, string)> ErrorsOf(EmailSettings settings)
    {
        var errors = new SettingsErrors(EmailSettings.Alias);
        new EmailSettingsValidator().Validate(settings, errors);
        return [.. errors.Errors.Select(error => (error.Field, error.Code))];
    }
}
