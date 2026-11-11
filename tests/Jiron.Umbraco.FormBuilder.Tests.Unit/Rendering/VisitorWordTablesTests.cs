using System.Reflection;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// Every table of words a visitor sees comes in the seven languages of the visitor texts (ROADMAP 14.2, ADR-0230): the
/// words a rendered form brings, the save-and-resume mail, the confirmation mail and page, the words for yes and no and
/// the standard lists. In each language every word is written, on one line, with the same values in braces as
/// English's, so no language drops a value or names one the text never gets. A word a language leaves out, leaves empty
/// or writes with another value is found.
/// </summary>
[TestFixture]
public sealed partial class VisitorWordTablesTests
{
    private static readonly string[] Seven = ["en", "de", "fr", "it", "nl", "es", "da"];

    [Test]
    public void TheRenderedFormsWords_AreWrittenInEveryLanguage_WithEnglishsValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderingWords.Cultures, Is.EquivalentTo(Seven));
            Assert.That(Problems(Seven.ToDictionary(language => language, language => Texts(RenderingWords.Of(language)))), Is.Empty);
        });
    }

    [Test]
    public void TheResumeMailsWords_AreWrittenInEveryLanguage_WithEnglishsValues()
    {
        var tables = Seven.ToDictionary(language => language, language => Texts(ResumeWords.Of(language)));

        Assert.Multiple(() =>
        {
            Assert.That(tables.Values.Select(texts => texts["Subject"]).Distinct().Count(), Is.EqualTo(7), "each language its own words");
            Assert.That(Problems(tables), Is.Empty);
        });
    }

    [Test]
    public void TheConfirmationsWords_AreWrittenInEveryLanguage_WithEnglishsValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VerificationWords.Languages, Is.EquivalentTo(Seven));
            Assert.That(Problems(Seven.ToDictionary(language => language, language => Texts(VerificationWords.Of(language)))), Is.Empty);
        });
    }

    [Test]
    public void TheWordsForYesAndNo_AndTheStandardLists_ComeInEveryLanguage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldDisplayWords.Cultures, Is.EquivalentTo(Seven));
            Assert.That(StandardOptionWords.Cultures, Is.EquivalentTo(Seven));
            Assert.That(Seven.Select(FieldDisplayWords.Yes).Distinct().Count(), Is.EqualTo(5), "Ja serves German, Dutch and Danish");
            Assert.That(Seven.Select(language => StandardOptionWords.Of(language).Agreement[0]).Distinct().Count(), Is.EqualTo(7));
        });
    }

    [Test]
    public void AWordLeftOut_LeftEmpty_OrWithAnotherValue_IsFound()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal) { ["Step"] = "Step {0} of {1}", ["Back"] = "Back", ["Keep"] = "Keep {0}" };
        var french = new Dictionary<string, string>(StringComparer.Ordinal) { ["Step"] = "\u00c9tape {0}", ["Back"] = " " };

        Assert.That(
            Problems(new Dictionary<string, Dictionary<string, string>> { ["en"] = english, ["fr"] = french }),
            Is.EqualTo(new[] { "fr.Step: values {0} for {0},{1}", "fr.Back: empty or broken", "fr.Keep: missing" }));
    }

    // What each language lacks or writes wrong against English's words, as "language.word: why".
    private static List<string> Problems(Dictionary<string, Dictionary<string, string>> tables)
    {
        var english = tables["en"];
        var problems = new List<string>();
        foreach (var (language, words) in tables.Where(table => table.Key != "en"))
        {
            foreach (var (name, text) in english)
            {
                if (!words.TryGetValue(name, out var own))
                {
                    problems.Add($"{language}.{name}: missing");
                }
                else if (own.Trim().Length == 0 || own.Contains((char)10, StringComparison.Ordinal))
                {
                    problems.Add($"{language}.{name}: empty or broken");
                }
                else if (Values(own) != Values(text))
                {
                    problems.Add($"{language}.{name}: values {Values(own)} for {Values(text)}");
                }
            }
        }

        return problems;
    }

    // The values a text names in braces, in order of their names.
    private static string Values(string text) => string.Join(',', ValueName().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal));

    // Every string a table's words hold, by its property's name.
    private static Dictionary<string, string> Texts(object words) =>
        words.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => (string)property.GetValue(words)!, StringComparer.Ordinal);

    [GeneratedRegex(@"\{[A-Za-z0-9]+\}")]
    private static partial Regex ValueName();
}
