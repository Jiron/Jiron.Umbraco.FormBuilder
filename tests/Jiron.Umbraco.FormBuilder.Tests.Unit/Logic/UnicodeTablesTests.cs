using System.Globalization;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// The tables check (ADR-0005, ADR-0064): compares Core's fold, clean, upper, lower and NFC recomposition for every code point with what the
/// TypeScript side wrote to tests/vectors/tables/typescript.json (<c>npm run tables</c> in packages/logic, whose own test
/// keeps the file current), so a difference between the runtimes' Unicode data shows up here.
/// </summary>
[TestFixture]
public sealed class UnicodeTablesTests
{
    /// <summary>
    /// Compositions in scripts added in Unicode 16 (Todhri, Tulu-Tigalari, Gurung Khema, Kirat Rai): Node's ICU knows
    /// them, the ICU .NET uses on Windows does not yet. The list is exact, so the check fails both when a gap appears and
    /// when one closes.
    /// </summary>
    private static readonly int[] KnownRecompositionGaps =
    [
        0x105C9, 0x105E4, 0x11383, 0x11385, 0x1138E, 0x11391, 0x113C5, 0x113C7, 0x113C8,
        0x16121, 0x16122, 0x16123, 0x16124, 0x16125, 0x16126, 0x16127, 0x16128, 0x16D68, 0x16D69, 0x16D6A,
    ];

    private static readonly Lazy<JsonDocument> Tables = new(() => JsonDocument.Parse(File.ReadAllText(
        RepositoryRoot.Path(typeof(UnicodeTablesTests).Assembly, "tests", "vectors", "tables", "typescript.json"))));

    private static JsonElement Root => Tables.Value.RootElement;

    [Test]
    public void TablesFile_WrittenByTheTypeScriptSide_IsJfbUnicodeTables1()
    {
        Assert.That(Root.GetProperty("format").GetString(), Is.EqualTo("jfb-unicode-tables/1"));
    }

    [Test]
    public void Fold_EveryCodePoint_AgreesWithTypeScript()
    {
        var expected = Expected("fold", e => char.ConvertFromUtf32(e.GetInt32()));

        var differences = Compare("fold", expected, Scalars(), TextSemantics.Fold);

        Assert.That(differences, Is.Empty, Summary(differences));
    }

    [Test]
    public void Clean_EveryCodePointAndLoneSurrogate_AgreesWithTypeScript()
    {
        var expected = Expected("clean", e => Text(e.EnumerateArray().Select(c => c.GetInt32())));
        var units = Enumerable.Range(0, 0x110000).Select(cp => (cp, Text([cp])));

        var differences = Compare("clean", expected, units, TextSemantics.Clean);

        Assert.That(differences, Is.Empty, Summary(differences));
    }

    [Test]
    public void Upper_EveryCodePoint_AgreesWithTypeScript()
    {
        var expected = Expected("upper", e => Text(e.EnumerateArray().Select(c => c.GetInt32())));

        var differences = Compare("upper", expected, Scalars(), TextSemantics.Upper);

        Assert.That(differences, Is.Empty, Summary(differences));
    }

    [Test]
    public void Lower_EveryCodePoint_AgreesWithTypeScript()
    {
        var expected = Expected("lower", e => Text(e.EnumerateArray().Select(c => c.GetInt32())));

        var differences = Compare("lower", expected, Scalars(), TextSemantics.Lower);

        Assert.That(differences, Is.Empty, Summary(differences));
    }

    [Test]
    public void Recompose_EveryDecomposition_AgreesWithTypeScriptApartFromTheKnownGaps()
    {
        var expected = Expected("recompose", e => Text(e.EnumerateArray().Select(c => c.GetInt32())));
        var differences = new List<string>();
        var decomposed = 0;
        foreach (var (code, ch) in Scalars().Where(s => s.Code is not 0xFFFE)) // .NET refuses U+FFFE; clean replaces it
        {
            var nfd = ch.Normalize(NormalizationForm.FormD);
            if (nfd == ch)
            {
                if (expected.ContainsKey(code))
                {
                    differences.Add(Gap(code));
                }

                continue;
            }

            decomposed++;
            var mine = nfd.Normalize(NormalizationForm.FormC);
            var theirs = expected.TryGetValue(code, out var value) ? value : "(no decomposition)";
            if (!string.Equals(mine, theirs, StringComparison.Ordinal))
            {
                differences.Add($"U+{code:X4}: C# {Describe(mine)}, TypeScript {Describe(theirs)}");
            }
        }

        var known = KnownRecompositionGaps.Select(Gap).ToList();
        var unexpected = differences.Except(known).Concat(known.Except(differences).Select(gap => "no longer differs: " + gap)).ToList();
        TestContext.Out.WriteLine($"recompose: {known.Count} known gaps in Unicode 16 scripts");
        Report("recompose", decomposed, expected.Count, unexpected);

        Assert.That(unexpected, Is.Empty, Summary(unexpected));
    }

    private static string Gap(int code) => $"U+{code:X4}: C# does not decompose it, TypeScript does";

    private static IEnumerable<(int Code, string Text)> Scalars() =>
        Enumerable.Range(0, 0x110000).Where(cp => cp is < 0xD800 or > 0xDFFF).Select(cp => (cp, char.ConvertFromUtf32(cp)));

    private static Dictionary<int, string> Expected(string table, Func<JsonElement, string> read) =>
        Root.GetProperty(table).EnumerateArray().ToDictionary(e => e[0].GetInt32(), e => read(e[1]));

    private static List<string> Compare(
        string what,
        Dictionary<int, string> expected,
        IEnumerable<(int Code, string Text)> inputs,
        Func<string, string> mine)
    {
        var differences = new List<string>();
        var changed = 0;
        foreach (var (code, text) in inputs)
        {
            string result;
            try
            {
                result = mine(text);
            }
            catch (ArgumentException exception)
            {
                differences.Add($"U+{code:X4}: C# throws {exception.GetType().Name}");
                continue;
            }

            if (!string.Equals(result, text, StringComparison.Ordinal))
            {
                changed++;
            }

            var theirs = expected.TryGetValue(code, out var value) ? value : text;
            if (!string.Equals(result, theirs, StringComparison.Ordinal))
            {
                differences.Add($"U+{code:X4}: C# {Describe(result)}, TypeScript {Describe(theirs)}");
            }
        }

        Report(what, changed, expected.Count, differences);
        return differences;
    }

    /// <summary>Code points as text; a surrogate code point becomes that lone code unit.</summary>
    private static string Text(IEnumerable<int> codePoints) =>
        string.Concat(codePoints.Select(cp => cp is >= 0xD800 and <= 0xDFFF ? ((char)cp).ToString() : char.ConvertFromUtf32(cp)));

    private static string Describe(string text) =>
        text.Length == 0
            ? "(empty)"
            : string.Join(' ', text.EnumerateRunes().Select(r => "U+" + r.Value.ToString("X4", CultureInfo.InvariantCulture)));

    private static string Summary(List<string> differences) =>
        $"{differences.Count} code points differ (first: {differences.FirstOrDefault()})";

    private static void Report(string what, int changed, int changedThere, List<string> differences)
    {
        TestContext.Out.WriteLine(
            $"{what}: .NET {Environment.Version} changes {changed}; Node {Root.GetProperty("node").GetString()} " +
            $"(ICU {Root.GetProperty("icu").GetString()}, Unicode {Root.GetProperty("unicode").GetString()}) changes {changedThere}; " +
            $"{differences.Count} differ");
        foreach (var line in differences.Take(40))
        {
            TestContext.Out.WriteLine(line);
        }
    }
}
