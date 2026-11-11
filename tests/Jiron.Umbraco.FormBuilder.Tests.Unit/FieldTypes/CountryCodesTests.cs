using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The codes a country field reads (ADR-0058) are one list in both runtimes (ROADMAP 6.4a, ADR-0109): the shared logic's
/// table in <c>packages/logic/src/countries.ts</c>, written as groups of a first letter and the second letters it takes
/// (6.8b and 14.2, to keep the runtime small), holds Core's <see cref="BuiltInChecks.CountryCodes"/>, no more and no fewer, so the browser refuses a code as
/// the server does.
/// </summary>
public sealed partial class CountryCodesTests
{
    [Test]
    public void TheBrowsersTable_HoldsCoresCodes()
    {
        var path = RepositoryRoot.Path(typeof(CountryCodesTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "logic", "src", "countries.ts");
        var table = File.ReadAllText(path);
        var groups = string.Concat(Letters().Matches(table[table.IndexOf("new Set(", StringComparison.Ordinal)..]).Select(match => match.Groups[1].Value))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var codes = groups.SelectMany(group => group[1..].Select(second => $"{group[0]}{second}")).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(groups.Select(group => group[0]), Is.Unique, "a group per first letter");
            Assert.That(groups, Has.All.Length.GreaterThan(1), "each group with a second letter");
            Assert.That(codes, Is.Unique, "each code once");
            Assert.That(codes.Order(StringComparer.Ordinal), Is.EqualTo(BuiltInChecks.CountryCodes.Order(StringComparer.Ordinal)));
        });
    }

    [GeneratedRegex("'([A-Z ]+)'", RegexOptions.CultureInvariant)]
    private static partial Regex Letters();
}
