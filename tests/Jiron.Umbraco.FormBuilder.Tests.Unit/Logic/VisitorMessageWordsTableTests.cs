using System.Reflection;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Core's visitor words are the browser runtime's, word for word, in all seven languages (ROADMAP 14.2, ADR-0230):
/// <c>packages/logic</c> writes every word of its tables to <c>tests/vectors/tables/message-words.json</c>, and Core has
/// exactly those words, each the same there, a counting word's two forms as <c>name.one</c> and <c>name.other</c>. A
/// word changed on one side only fails here, or in the runtime's test of the file.
/// </summary>
[TestFixture]
public sealed class VisitorMessageWordsTableTests
{
    [Test]
    public void EveryWordOfCore_IsTheRuntimes_InEveryLanguage()
    {
        var table = Table();

        Assert.Multiple(() =>
        {
            Assert.That(table.Keys, Is.EquivalentTo(VisitorMessageWords.Languages), "the same seven languages");
            foreach (var language in VisitorMessageWords.Languages)
            {
                var words = Words(VisitorMessageWords.Of(language)).ToList();
                Assert.That(words, Has.Count.GreaterThan(90), $"{language}: every word is read");
                Assert.That(Differences(words, table[language]), Is.Empty, language);
                Assert.That(words.Select(word => word.Name), Is.EquivalentTo(table[language].Keys), $"{language}: Core has every word the runtime has, and no other");
            }
        });
    }

    [Test]
    public void AWordChangedOnOneSide_IsFound()
    {
        var table = Table();
        var changed = VisitorMessageWords.Of("fr") with { Answer = "Saisissez", MinLength = new PluralWords("x", "y {min}") };

        Assert.That(
            Differences(Words(changed), table["fr"]),
            Is.EqualTo(new[] { "answer", "minLength.one", "minLength.other" }));
    }

    // The words whose text is not the file's, by their names there.
    private static List<string> Differences(IEnumerable<(string Name, string Text)> words, IReadOnlyDictionary<string, string> theirs) =>
        [.. words.Where(word => !theirs.TryGetValue(word.Name, out var text) || !string.Equals(text, word.Text, StringComparison.Ordinal)).Select(word => word.Name)];

    // Every word of a language by its name in the file: the property's name in camel case, a plural's two forms after it.
    private static IEnumerable<(string Name, string Text)> Words(VisitorMessageWords words) =>
        typeof(VisitorMessageWords).GetProperties(BindingFlags.Public | BindingFlags.Instance).SelectMany(property =>
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            return property.GetValue(words) switch
            {
                string text => [(name, text)],
                PluralWords plural => new[] { (name + ".one", plural.One), (name + ".other", plural.Other) },
                IReadOnlyDictionary<string, string> names => names.Select(pair => (name + "." + pair.Key, pair.Value)),
                _ => [],
            };
        });

    // The file's words by language.
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Table()
    {
        var path = RepositoryRoot.Path(typeof(VisitorMessageWordsTableTests).Assembly, "tests", "vectors", "tables", "message-words.json");
        using var file = JsonDocument.Parse(File.ReadAllText(path));
        Assert.That(file.RootElement.GetProperty("format").GetString(), Is.EqualTo("jfb-message-words/1"));
        return file.RootElement.GetProperty("languages").EnumerateObject().ToDictionary(
            language => language.Name,
            language => (IReadOnlyDictionary<string, string>)language.Value.EnumerateObject().ToDictionary(word => word.Name, word => word.Value.GetString() ?? string.Empty, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }
}
