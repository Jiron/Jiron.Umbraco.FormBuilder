using System.Reflection;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// Every code a form check raises has the designer's own words, or is one of the few whose words only the server has
/// (ROADMAP 14.1 and 14.3): the designer says the rest in the backoffice's language, so a code it does not list would show
/// the server's English sentence to a German editor.
/// </summary>
[TestFixture]
public sealed partial class FormCheckWordsTests
{
    [Test]
    public void EveryFormChecksCode_HasTheDesignersWords_OrIsOneOnlyTheServerSays()
    {
        var file = File.ReadAllText(Path.Combine(
            RepositoryRoot.Path(typeof(FormCheckWordsTests).Assembly, "src"),
            "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "backoffice", "src", "designer", "check", "check-words.ts"));
        var listed = Listed().Matches(file).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var codes = typeof(IFormCheck).Assembly.GetTypes()
            .Where(type => typeof(IFormCheck).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral && field.FieldType == typeof(string)))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(code => Code().IsMatch(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(codes, Has.Count.GreaterThan(20), "the codes the form checks raise");
            Assert.That(codes, Does.Contain(AnonymousFormCheck.AnonymousForMembers).And.Contain("password_asked"));
            Assert.That(codes.Where(code => !listed.Contains(code)), Is.Empty, "codes the designer neither words nor leaves to the server");
        });
    }

    // A code as check-words.ts lists it, one per line in quotes.
    [GeneratedRegex(@"^\s+'([a-z][a-z_]*)',", RegexOptions.Multiline)]
    private static partial Regex Listed();

    // A check code: lowercase words joined by underscores.
    [GeneratedRegex("^[a-z]+(_[a-z]+)+$")]
    private static partial Regex Code();
}
