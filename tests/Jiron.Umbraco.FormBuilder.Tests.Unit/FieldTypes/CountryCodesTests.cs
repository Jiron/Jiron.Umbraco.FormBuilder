using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The codes a country field reads (ADR-0058) are one list in both runtimes (ROADMAP 6.4a, ADR-0109): the shared logic's
/// table in <c>packages/logic/src/countries.ts</c> holds Core's <see cref="BuiltInChecks.CountryCodes"/>, no more and
/// no fewer, so the browser refuses a code as the server does.
/// </summary>
public sealed partial class CountryCodesTests
{
    [Test]
    public void TheBrowsersTable_HoldsCoresCodes()
    {
        var path = RepositoryRoot.Path(typeof(CountryCodesTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "logic", "src", "countries.ts");
        var table = File.ReadAllText(path);
        var codes = Code().Matches(table[table.IndexOf("new Set([", StringComparison.Ordinal)..]).Select(match => match.Groups[1].Value).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(codes, Is.Unique, "each code once");
            Assert.That(codes.Order(StringComparer.Ordinal), Is.EqualTo(BuiltInChecks.CountryCodes.Order(StringComparer.Ordinal)));
        });
    }

    [GeneratedRegex("'([A-Z]{2})'", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
