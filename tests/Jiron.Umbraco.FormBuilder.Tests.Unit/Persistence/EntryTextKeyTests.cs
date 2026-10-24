using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// <c>jfbEntryValue.textKey</c> compares as rules do (ADR-0005, ADR-0038): two text answers share a key exactly when the
/// rule vectors of 1.5 say an <c>equals</c> condition on a text field matches, so the entries screen filters as rules
/// decide. The vectors are read in place from <c>tests/vectors</c>, the files both runtimes pass.
/// </summary>
[TestFixture]
public sealed class EntryTextKeyTests
{
    // The cases ROADMAP 4.3c names: the Turkish i's, final sigma, and a script outside the folded blocks.
    private static readonly string[] Cited =
    [
        "spike/text-equals-dotted-capital-i",
        "spike/text-equals-dotless-i",
        "spike/text-equals-greek-capitals",
        "spike/text-equals-final-sigma",
        "spike/text-equals-other-scripts-compare-exactly",
    ];

    private static readonly Lazy<IReadOnlyList<TextEquals>> Vectors = new(Load);

    public static IEnumerable<TestCaseData> Cases() =>
        Vectors.Value.Select(vector => new TestCaseData(vector.Answer, vector.Operand, vector.Matches).SetName($"TextKey({vector.Id})"));

    [TestCaseSource(nameof(Cases))]
    public void TwoTexts_ShareAKey_ExactlyWhenTheRuleVectorSaysTheyAreEqual(string answer, string operand, bool matches) =>
        Assert.That(EntryTextKey.For(answer) == EntryTextKey.For(operand), Is.EqualTo(matches));

    [Test]
    public void TheVectors_IncludeTheCasesThisStepNames() =>
        Assert.That(Vectors.Value.Select(vector => vector.Id), Is.SupersetOf(Cited));

    [Test]
    public void BothTurkishIs_FoldToI()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.For((char)0x130 + "STANBUL"), Is.EqualTo("istanbul"), "the dotted capital I");
            Assert.That(EntryTextKey.For((char)0x131 + "sparta"), Is.EqualTo("isparta"), "the dotless small i");
            Assert.That(EntryTextKey.For("ISPARTA"), Is.EqualTo("isparta"));
        });
    }

    [Test]
    public void FinalSigma_FoldsLikeMedialSigma()
    {
        var capitals = Text(0x39F, 0x394, 0x39F, 0x3A3);
        var final = Text(0x3BF, 0x3B4, 0x3BF, 0x3C2);
        var medial = Text(0x3BF, 0x3B4, 0x3BF, 0x3C3);

        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.For(capitals), Is.EqualTo(medial));
            Assert.That(EntryTextKey.For(final), Is.EqualTo(medial));
            Assert.That(EntryTextKey.For(medial), Is.EqualTo(medial));
        });
    }

    [Test]
    public void TextOutsideTheFoldedBlocks_KeepsItsCase()
    {
        var deseretCapital = Text(0x10400);
        var deseretSmall = Text(0x10428);

        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.For(deseretCapital), Is.EqualTo(deseretCapital));
            Assert.That(EntryTextKey.For(deseretSmall), Is.EqualTo(deseretSmall));
            Assert.That(EntryTextKey.For("STRASSE"), Is.Not.EqualTo(EntryTextKey.For("Stra" + (char)0xDF + "e")), "simple folding: sharp s is not ss");
        });
    }

    [Test]
    public void AKey_IsTheTextCleaned_ThenFolded()
    {
        var decomposed = " Zoe" + (char)0x308 + (char)0x3000;

        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.For(decomposed), Is.EqualTo("zo" + (char)0xEB), "trimmed, composed and folded");
            Assert.That(EntryTextKey.For(decomposed), Is.EqualTo(TextSemantics.Fold(TextSemantics.Clean(decomposed))));
            Assert.That(EntryTextKey.For(string.Empty), Is.Empty);
        });
    }

    [Test]
    public void ALongKey_IsCutToTheColumn_NeverInsideASurrogatePair()
    {
        var max = EntryTextKey.MaxLength;
        var face = char.ConvertFromUtf32(0x1F600);

        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.For(new string('A', max)), Is.EqualTo(new string('a', max)), "a key that fits is whole");
            Assert.That(EntryTextKey.For(new string('A', max + 1)), Is.EqualTo(new string('a', max)));
            Assert.That(EntryTextKey.For(new string('a', max - 1) + face), Is.EqualTo(new string('a', max - 1)), "the pair would lose its second half");
            Assert.That(EntryTextKey.For(new string('a', max - 2) + face + "a"), Is.EqualTo(new string('a', max - 2) + face), "a pair that fits is kept");
        });
    }

    [Test]
    public void MayBeCut_FromOneShortOfTheColumn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.MayBeCut(new string('a', EntryTextKey.MaxLength - 2)), Is.False);
            Assert.That(EntryTextKey.MayBeCut(new string('a', EntryTextKey.MaxLength - 1)), Is.True, "a cut before a surrogate pair");
            Assert.That(EntryTextKey.MayBeCut(new string('a', EntryTextKey.MaxLength)), Is.True);
        });
    }

    [Test]
    public void TheWholeText_IsCleanedAndFolded_NeverCut()
    {
        var max = EntryTextKey.MaxLength;
        var text = "  " + new string('A', max + 44) + "B ";

        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.Whole(text), Is.EqualTo(new string('a', max + 44) + "b"));
            Assert.That(EntryTextKey.For(text), Is.EqualTo(EntryTextKey.Whole(text)[..max]), "the key is the whole text cut");
            Assert.That(EntryTextKey.Whole("Anna"), Is.EqualTo(EntryTextKey.For("Anna")), "a short text is its key");
            Assert.That(() => EntryTextKey.Whole(null!), Throws.ArgumentNullException);
        });
    }

    private static string Text(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    /// <summary>
    /// Every vector that compares one answer of a text field with <c>equals</c> and expects true or false, unless its
    /// context hides the field (then the rule sees no answer at all).
    /// </summary>
    private static List<TextEquals> Load()
    {
        var folder = RepositoryRoot.Path(typeof(EntryTextKeyTests).Assembly, "tests", "vectors");
        var cases = new List<TextEquals>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var suite = root.GetProperty("suite").GetString();
            var kinds = VectorReader.Fields(root.GetProperty("fields"));
            foreach (var vector in root.GetProperty("cases").EnumerateArray())
            {
                if (!vector.TryGetProperty("condition", out var condition)
                    || condition.GetProperty("op").GetString() != "equals"
                    || !condition.TryGetProperty("field", out var fieldElement)
                    || !condition.TryGetProperty("value", out var operand)
                    || operand.ValueKind != JsonValueKind.String
                    || vector.GetProperty("expect").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !vector.TryGetProperty("values", out var values))
                {
                    continue;
                }

                var field = fieldElement.GetString()!;
                var fieldKinds = new Dictionary<string, FieldKind>(kinds, StringComparer.Ordinal);
                if (vector.TryGetProperty("fields", out var overrides))
                {
                    foreach (var (alias, overridden) in VectorReader.Fields(overrides))
                    {
                        fieldKinds[alias] = overridden;
                    }
                }

                var hidden = vector.TryGetProperty("context", out var context) && context.TryGetProperty("hidden", out var list) && list.EnumerateArray().Any(alias => alias.GetString() == field);
                if (hidden || !fieldKinds.TryGetValue(field, out var kind) || kind != FieldKind.Text
                    || !values.TryGetProperty(field, out var answers) || answers.GetArrayLength() != 1 || answers[0].ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                cases.Add(new TextEquals($"{suite}/{vector.GetProperty("id").GetString()}", answers[0].GetString()!, operand.GetString()!, vector.GetProperty("expect").GetBoolean()));
            }
        }

        return cases;
    }

    /// <summary>One vector: an answer, the operand of <c>equals</c>, and whether the rule matches.</summary>
    private sealed record TextEquals(string Id, string Answer, string Operand, bool Matches);
}
