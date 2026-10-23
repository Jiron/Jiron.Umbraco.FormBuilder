using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>The stored definitions of tests/fixtures/definitions and the serializer the tests read them with.</summary>
internal static class DefinitionFixtures
{
    /// <summary>A fully populated v1 document, hand-written in canonical property order and indented for review.</summary>
    public static string Full => File.ReadAllText(Path("v1-full.json"));

    /// <summary>The reviewed public projection of <see cref="Full"/>, indented in canonical property order.</summary>
    public static string FullProjection => File.ReadAllText(Path("v1-full.public.json"));

    /// <summary>The production serializer: no upgraders (v1 is the only schema), the default limits.</summary>
    public static FormDefinitionSerializer Serializer(DefinitionLimits? limits = null) =>
        new(new FormDefinitionUpgrades([]), limits ?? new DefinitionLimits());

    /// <summary>Reads a definition that must be readable.</summary>
    public static FormDefinition Read(string json)
    {
        var result = Serializer().Read(json);
        Assert.That(result.Problems, Is.Empty, "the definition reads");
        return result.Definition!;
    }

    /// <summary>
    /// Removes the whitespace between JSON tokens and keeps the strings exactly as written: an indented document in
    /// canonical form becomes its canonical bytes without the writer under test.
    /// </summary>
    public static string Minify(string json)
    {
        var text = new StringBuilder(json.Length);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                text.Append(c);
                if (c == '\\')
                {
                    text.Append(json[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
                text.Append(c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static string Path(string name) =>
        RepositoryRoot.Path(typeof(DefinitionFixtures).Assembly, "tests", "fixtures", "definitions", name);
}
