using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// Every word a settings screen is served has its words (ROADMAP 12.4a, ADR-0218): each field's label, help, unit and
/// choices, at any depth, and each box's headline are keys of the global settings' dictionaries, in English and in
/// German, so no screen ever shows a key.
/// </summary>
[TestFixture]
public sealed class SettingsWordsTests
{
    private const string Area = "#jfbGlobalSettings_";

    private static readonly string Lang = Path.Combine(
        RepositoryRoot.Path(typeof(SettingsWordsTests).Assembly, "src"),
        "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "backoffice", "src", "lang");

    [TestCase("en")]
    [TestCase("de")]
    public void EveryServedKey_HasItsWords(string language)
    {
        var dictionary = File.ReadAllText(Path.Combine(Lang, $"global-settings.{language}.ts"));
        var keys = new HashSet<string>(Regex.Matches(dictionary, @"^\s{4}([A-Za-z_]+):", RegexOptions.Multiline).Select(match => match.Groups[1].Value), StringComparer.Ordinal);
        var served = Served().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(served, Has.Count.GreaterThan(100), "the keys the sections serve");
            Assert.That(served.Where(key => !keys.Contains(key)).Distinct(), Is.Empty, $"keys without words in {language}");
        });
    }

    // Every key the sections serve, without the area: the fields' labels, helps, units and choices, and the boxes.
    private static IEnumerable<string> Served()
    {
        var sections = typeof(ISettingsSection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(ISettingsSection)));
        foreach (var section in sections)
        {
            var alias = (string)section.GetProperty(nameof(ISettingsSection.Alias))!.GetValue(null)!;
            var fields = SettingsSchema.Read(section, alias);
            foreach (var box in fields.Select(field => field.Box).Distinct(StringComparer.Ordinal))
            {
                yield return $"{alias}Box_{box}";
            }

            foreach (var key in fields.SelectMany(Keys))
            {
                yield return key[Area.Length..];
            }
        }

        // The layouts Email offers besides the library's, as its facts name them.
        yield return "email_defaultLayout_card";
        yield return "email_defaultLayout_letter";
    }

    private static IEnumerable<string> Keys(SettingsFieldDescriptor field) =>
    [
        field.Label,
        .. field.Description is null ? [] : new[] { field.Description },
        .. field.Unit is null ? [] : new[] { field.Unit },
        .. field.Choices.Select(choice => $"{field.Label}_{choice}"),
        .. field.Fields.SelectMany(Keys),
    ];
}
