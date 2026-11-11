using System.Reflection;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Every kind of error a built-in type declares has a default message of its own in every language of
/// <see cref="VisitorMessageWords"/> (ADR-0099): not the general "Check this answer" or "Answer this question" a site's
/// own type gets, never empty and with every value filled in. A kind a type declares without words of its own is found,
/// and so is a word a language leaves empty; the copy guide lists every word in all seven languages.
/// </summary>
[TestFixture]
public sealed class VisitorMessageDefaultsTests
{
    private static readonly MergeTagCultures Cultures = new();
    private static readonly int Today = new DateOnly(2026, 6, 15).DayNumber;

    // Limits that give each kind its own words: one set for every type, and the dates and times their own.
    private const string Limits = """
        {"minLength":2,"maxLength":40,"minWords":2,"maxWords":40,"min":"1","max":"9","step":"0.5","decimals":1,
         "minSelections":1,"maxSelections":2,"multiple":true,"types":["images"],"maxBytes":2097152,"maxFiles":2,
         "minRows":1,"maxRows":3,"stepMinutes":15,"count":5,"from":0,"to":10}
        """;

    private static readonly Dictionary<string, string> OwnLimits = new(StringComparer.Ordinal)
    {
        ["date"] = """{"min":{"today":"+1d"},"max":{"today":"+30d"},"weekdaysOnly":true}""",
        ["datetime"] = """{"min":{"today":"+1d"},"max":{"today":"+30d"},"stepMinutes":30}""",
        ["time"] = """{"min":"08:00","max":"17:30","stepMinutes":15}""",
    };

    private static IEnumerable<FieldTypeDeclaration> BuiltIn =>
        TestFieldTypes.Registry.Types.Select(type => type.Declaration);

    [Test]
    public void EveryKindEveryBuiltInTypeDeclares_HasADefaultOfItsOwn_InEveryLanguage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisitorMessageWords.Languages, Is.EquivalentTo(new[] { "en", "de", "fr", "it", "nl", "es", "da" }), "the seven languages of the visitor texts (ADR-0230)");
            Assert.That(BuiltIn.Count(type => type.Messages != FieldMessageKinds.None), Is.GreaterThan(20), "the types with messages are all read");
            Assert.That(Missing(BuiltIn), Is.Empty);
        });
    }

    [Test]
    public void AKindLeftOut_IsFound()
    {
        var time = BuiltIn.Single(type => type.Alias == "time");
        var leftOut = time with { Messages = time.Messages | FieldMessageKinds.DateWindow | FieldMessageKinds.Selections };

        var expected = new[] { "dateWindow", "selections" }.SelectMany(kind => VisitorMessageWords.Languages.Select(language => $"time.{kind} ({language})")).ToList();

        Assert.That(expected, Has.Count.EqualTo(14).And.Contains("time.dateWindow (da)"));
        Assert.That(Missing([leftOut]), Is.EqualTo(expected));
    }

    [Test]
    public void EveryLanguage_HasEveryWord()
    {
        Assert.Multiple(() =>
        {
            foreach (var language in VisitorMessageWords.Languages)
            {
                var words = Words(VisitorMessageWords.Of(language)).ToList();
                Assert.That(words, Has.Count.GreaterThan(90), $"{language}: every word is read");
                foreach (var (name, text) in words)
                {
                    Assert.That(text, Is.Not.Empty.And.Not.Contains((char)10), $"{language}.{name}");
                }
            }
        });
    }

    [Test]
    public void TheCopyGuide_ListsEveryWord_InEveryLanguage()
    {
        var guide = File.ReadAllText(RepositoryRoot.Path(typeof(VisitorMessageDefaultsTests).Assembly, "docs", "ux", "copy.md"));
        var table = guide[guide.IndexOf("### The default error messages", StringComparison.Ordinal)..];

        Assert.Multiple(() =>
        {
            foreach (var language in VisitorMessageWords.Languages)
            {
                foreach (var (name, text) in Words(VisitorMessageWords.Of(language)))
                {
                    Assert.That(table, Does.Contain("| " + text + " |").Or.Contain("| " + text + " ").Or.Contain(" " + text + " |"), $"{language}.{name}");
                }
            }
        });
    }

    // Every word of a language, a plural's singular and plural each, by its property's name.
    private static IEnumerable<(string Name, string Text)> Words(VisitorMessageWords words) =>
        typeof(VisitorMessageWords).GetProperties(BindingFlags.Public | BindingFlags.Instance).SelectMany(property => property.GetValue(words) switch
        {
            string text => [(property.Name, text)],
            PluralWords plural => new[] { (property.Name, plural.One), (property.Name, plural.Other) },
            _ => [],
        });

    // The kinds a declaration lists whose default is a general one, empty or has a value left unfilled, as
    // "alias.kind (language)".
    private static List<string> Missing(IEnumerable<FieldTypeDeclaration> declarations)
    {
        var missing = new List<string>();
        foreach (var declaration in declarations)
        {
            using var settings = JsonDocument.Parse(OwnLimits.GetValueOrDefault(declaration.Alias, Limits));
            foreach (var property in FieldMessageProperties.All.Where(property => declaration.Messages.HasFlag(property.Kind)))
            {
                foreach (var language in VisitorMessageWords.Languages)
                {
                    var words = VisitorMessageWords.Of(language);
                    var message = VisitorMessages.Default(Error(declaration.Alias, property.Name, settings.RootElement), language, Cultures.For(language), Today);
                    if (string.IsNullOrWhiteSpace(message) || message == words.CheckThis || message == words.AnswerThis || message.Contains('{', StringComparison.Ordinal))
                    {
                        missing.Add($"{declaration.Alias}.{property.Name} ({language})");
                    }
                }
            }
        }

        return missing;
    }

    private static VisitorError Error(string type, string kind, JsonElement settings) => new()
    {
        Code = kind,
        Field = new MessageField { Type = type, Settings = settings, Options = [new MessageOption("a", "Morning")] },
        Answers = kind switch
        {
            "full" => ["a"],
            "invalid" when type == "email" => ["not an address"],
            _ => [],
        },
        Rows = kind == "rowCount" ? 5 : null,
    };
}
