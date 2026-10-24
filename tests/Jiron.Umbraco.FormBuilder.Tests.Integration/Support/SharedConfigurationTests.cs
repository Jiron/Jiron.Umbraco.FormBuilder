using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// No fixture writes into the configuration the test base shares. Umbraco's test host chains one configuration into
/// every fixture's site, and setting a key on <c>builder.Config</c> writes through into it, so the value reaches every
/// fixture that runs later. A fixture sets its site's configuration in its constructor, through
/// <c>InMemoryConfiguration</c>, which the test base reads each time it builds a test's site. The test reads the
/// fixtures' source, as the order the fixtures run in hides a leak from the tests themselves.
/// </summary>
[TestFixture]
public sealed partial class SharedConfigurationTests
{
    [Test]
    public void NoFixture_SetsAKeyOnTheSharedConfiguration()
    {
        var root = RepositoryRoot.Path(typeof(SharedConfigurationTests).Assembly, "tests", "Jiron.Umbraco.FormBuilder.Tests.Integration");
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        var writes = sources
            .Where(path => Path.GetFileName(path) != nameof(SharedConfigurationTests) + ".cs") // its own examples
            .SelectMany(path => Write().Matches(File.ReadAllText(path)).Select(match => $"{Path.GetFileName(path)}: {match.Value}"))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sources.Select(Path.GetFileName), Has.Member("HostOptionsBootTests.cs"), "the scan read the fixtures");
            Assert.That(writes, Is.Empty, "set the key in the fixture's constructor, through InMemoryConfiguration");
        });
    }

    [TestCase("builder.Config[key] = \"1\";", true)]
    [TestCase("builder.Config [ Name + \":A\" ]=value;", true)]
    [TestCase("Configuration[\"A:B\"] = \"x\";", true)]
    [TestCase("InMemoryConfiguration[\"A:B\"] = \"x\";", false)]
    [TestCase("var value = builder.Config[key];", false)]
    [TestCase("Assert.That(builder.Config[key] == \"1\");", false)]
    public void TheScan_FindsAWrite_AndNoRead(string code, bool write)
    {
        Assert.That(Write().IsMatch(code), Is.EqualTo(write));
    }

    // An assignment to an indexer of a configuration: Config[...] = or Configuration[...] =, but not ==.
    [GeneratedRegex(@"\b(?:Config|Configuration)\s*\[[^\]]+\]\s*=(?!=)")]
    private static partial Regex Write();
}
