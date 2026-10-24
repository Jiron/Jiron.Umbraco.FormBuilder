using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// Form aliases (ADR-0044): a name without an alias gives camelCase ASCII words, and an alias another form has in any
/// letter case is numbered from 2 (ADR-0033).
/// </summary>
[TestFixture]
public sealed class FormAliasTests
{
    public static IEnumerable<TestCaseData> Names()
    {
        yield return Case("Contact us", "contactUs", "words become camelCase");
        yield return Case("  CONTACT   us  ", "contactUs", "each word is lowercased, then capitalised");
        yield return Case("contact-form_v2", "contactFormV2", "punctuation separates words");
        yield return Case("K" + (char)0xFC + "ndigung", "kundigung", "an accent is removed");
        yield return Case("Caf" + (char)0x65 + (char)0x301 + " order", "cafeOrder", "a combining accent is removed too");
        yield return Case("Stra" + (char)0xDF + "e", "strasse", "the sharp s is spelled out");
        yield return Case((char)0xC6 + "ble " + (char)0xD8 + "l", "aebleOl", "a ligature and the slashed o are spelled out");
        yield return Case(string.Empty + (char)0x141 + (char)0xF3 + "d" + (char)0x17A, "lodz", "the stroked l is spelled out");
        yield return Case((char)0xDE + "ing", "thing", "thorn is spelled out");
        yield return Case("D" + (char)0x131 + "yarbak" + (char)0x131 + "r", "diyarbakir", "the dotless i is spelled out");
        yield return Case((char)0x130 + "stanbul", "istanbul", "the dotted capital I loses its dot");
        yield return Case("Feedback " + (char)0xD83D + (char)0xDE00, "feedback", "an emoji separates words");
        yield return Case("2026 survey", "form2026Survey", "an alias starts with a letter");
        yield return Case(string.Empty + (char)0x6771 + (char)0x4EAC, FormAliases.Fallback, "a name without Latin letters or digits");
        yield return Case("!!!", FormAliases.Fallback, "a name of punctuation");
        yield return Case(string.Empty, FormAliases.Fallback, "an empty name");
        yield return Case(new string('a', 100), new string('a', FormAliases.MaxBaseLength), "a long name is cut");
        yield return Case("1" + new string('a', 100), "form1" + new string('a', FormAliases.MaxBaseLength - 5), "the prefix counts");
    }

    [TestCaseSource(nameof(Names))]
    public void AName_GivesItsAlias(string name, string expected)
    {
        var alias = FormAliases.FromName(name);

        Assert.Multiple(() =>
        {
            Assert.That(alias, Is.EqualTo(expected));
            Assert.That(FormAliases.IsValid(alias), Is.True, "every alias a name gives is one a form takes");
        });
    }

    [Test]
    public void ANameAlias_LeavesRoomForANumberUpTo9999()
    {
        Assert.That(FormAliases.MaxBaseLength + "9999".Length, Is.EqualTo(DefinitionStructureChecks.MaxAliasLength));
    }

    [TestCase("contact", ExpectedResult = "contact", Description = "no form has it")]
    [TestCase("contact", "contactUs", "contact2", ExpectedResult = "contact", Description = "only longer aliases start with it")]
    [TestCase("contact", "contact", ExpectedResult = "contact2")]
    [TestCase("contact", "CONTACT", "Contact2", ExpectedResult = "contact3", Description = "in any letter case")]
    [TestCase("contact", "contact", "contact3", ExpectedResult = "contact2", Description = "the lowest free number")]
    [TestCase("form", "form", "form2", "form3", "form4", ExpectedResult = "form5")]
    public string AnAliasThatIsTaken_GetsTheLowestFreeNumber(string alias, params string[] taken) => FormAliases.FirstFree(alias, taken);

    [Test]
    public void FirstFree_NeedsAnAliasAndTheTakenOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FormAliases.FirstFree(string.Empty, []), Throws.ArgumentException);
            Assert.That(() => FormAliases.FirstFree("contact", null!), Throws.ArgumentNullException);
            Assert.That(() => FormAliases.FromName(null!), Throws.ArgumentNullException);
        });
    }

    [TestCase("contactUs", ExpectedResult = true)]
    [TestCase("a", ExpectedResult = true)]
    [TestCase("ContactUs", ExpectedResult = false, Description = "a capital first")]
    [TestCase("1contact", ExpectedResult = false)]
    [TestCase("contact-us", ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    [TestCase(null, ExpectedResult = false)]
    public bool AnAlias_FollowsTheFieldAliasPattern(string? alias) => FormAliases.IsValid(alias);

    [Test]
    public void AnAlias_HasAtMost64Characters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormAliases.IsValid("a" + new string('b', 63)), Is.True);
            Assert.That(FormAliases.IsValid("a" + new string('b', 64)), Is.False);
            Assert.That(FormAliases.IsValid("caf" + (char)0xE9), Is.False, "ASCII only");
        });
    }

    private static TestCaseData Case(string name, string expected, string why) =>
        new TestCaseData(name, expected).SetArgDisplayNames(why);
}
