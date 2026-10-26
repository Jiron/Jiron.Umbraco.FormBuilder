using System.Globalization;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>What the fuzz tests share (ROADMAP 5.3a, 5.3b): how a report shows an input, and the cultures of the merge-tag vectors.</summary>
internal static class Fuzzing
{
    private static readonly Lazy<IReadOnlyList<CultureFormats>> Cultures = new(LoadCultures);

    /// <summary>The cultures of merge-tags.json, so templates print with the formats the vectors use, the odd ones included.</summary>
    public static IReadOnlyList<CultureFormats> TemplateCultures => Cultures.Value;

    /// <summary>An input as a report shows it: short, and ASCII, with each other code unit as its code.</summary>
    public static string Show(string input)
    {
        var text = new StringBuilder();
        foreach (var c in input.Length > 200 ? input[..200] + "..." : input)
        {
            if (c is >= ' ' and <= '~')
            {
                text.Append(c);
            }
            else
            {
                text.Append((char)92).Append('u').Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    private static List<CultureFormats> LoadCultures()
    {
        var path = RepositoryRoot.Path(typeof(Fuzzing).Assembly, "tests", "vectors", "merge-tags.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("defaults").GetProperty("cultures").EnumerateObject().Select(culture => VectorReader.Formats(culture.Value))];
    }
}
