using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// The upgrade fixtures of the released schemas (ROADMAP 16.5 and 18.4, ADR-0240): one
/// <c>tests/fixtures/upgrade/schema-v&lt;N&gt;.json</c> per tag <c>baseline/schema-v&lt;N&gt;</c>, written by
/// <c>scripts/install-test.ps1 -Mode upgrade -WriteFixture</c>.
/// </summary>
public static partial class UpgradeFixtures
{
    /// <summary>Every fixture of the repository a test assembly was built from, oldest schema first.</summary>
    /// <exception cref="InvalidOperationException">The repository holds no fixture.</exception>
    public static IReadOnlyList<string> Paths(Assembly testAssembly)
    {
        var folder = RepositoryRoot.Path(testAssembly, "tests", "fixtures", "upgrade");
        var paths = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "schema-v*.json")
                .Select(path => (Path: path, Match: Name().Match(Path.GetFileName(path))))
                .Where(file => file.Match.Success)
                .OrderBy(file => int.Parse(file.Match.Groups[1].Value, CultureInfo.InvariantCulture))
                .Select(file => file.Path)
                .ToList()
            : [];
        return paths.Count > 0 ? paths : throw new InvalidOperationException($"{folder} holds no schema-v<N>.json.");
    }

    [GeneratedRegex(@"^schema-v(\d+)\.json$")]
    private static partial Regex Name();
}
