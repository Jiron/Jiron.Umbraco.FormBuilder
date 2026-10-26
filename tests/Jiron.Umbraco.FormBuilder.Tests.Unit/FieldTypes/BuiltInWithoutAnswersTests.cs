using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The built-in types that take no answer of their own (ADR-0071): the calculation, whose value the server works out,
/// the repeatable group, whose child fields take the answers, the content types and the captcha placement. Whatever is
/// posted for them, however long or many, is neither limited, nor read, nor checked.
/// </summary>
public sealed class BuiltInWithoutAnswersTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    private static readonly string[] WithoutAnswers = ["calculation", "repeater", "heading", "paragraph", "divider", "image", "captcha"];

    public static IEnumerable<TestCaseData> Postings()
    {
        static KeyValuePair<string, IEnumerable<string>> Part(string name, params string[] texts) => new(name, texts);
        (string Name, FieldAnswers Answers)[] postings =
        [
            ("a number", new FieldAnswers(["42"])),
            ("a text far past any limit", new FieldAnswers([new string('x', 100_000)])),
            ("a thousand texts", new FieldAnswers([.. Enumerable.Range(0, 1000).Select(i => $"v{i}")])),
            ("the same text twice", new FieldAnswers(["5", "5"])),
            ("parts", new FieldAnswers(["1"], [Part("first", "Ada"), Part("wording", new string('a', 64))])),
            ("nothing", new FieldAnswers([])),
        ];
        foreach (var type in WithoutAnswers)
        {
            foreach (var (name, answers) in postings)
            {
                yield return new TestCaseData(type, answers).SetName($"Ignores({type}: {name})");
            }
        }
    }

    [TestCaseSource(nameof(Postings))]
    public void ATypeWithoutAnswers_IgnoresWhatIsPostedForIt(string alias, FieldAnswers answers)
    {
        var type = TestFieldTypes.Registry.FindType(alias)!;
        var field = new FormField { Key = Guid.NewGuid(), Type = alias, Alias = alias };
        var read = FieldValues.Read(type, field, answers);

        Assert.Multiple(() =>
        {
            Assert.That(FieldValues.CheckLimits(type, field, answers), Is.Null, "no limit refuses it");
            Assert.That(read.Values, Is.Empty, "nothing is read");
            Assert.That(read.IsUnreadable, Is.False, "nor found unreadable");
            var errors = FieldValues.Check(type, field, read, new FieldCheckInput(false, Today));
            Assert.That(errors.Errors, Is.Empty, "nor checked");
            Assert.That(errors.Checks, Is.Empty, "nor checked by its own checks");
        });
    }

    [Test]
    public void TheTypesWithoutAnswers_AreEveryBuiltInTypeWithoutAnswerLimits()
    {
        // A grid takes its answers through its rows, so the field has one.
        var registry = TestFieldTypes.Registry;
        var none = registry.All
            .Where(traits => registry.FindType(traits.Alias)!.AnswerLimits(new FormField { Key = Guid.NewGuid(), Type = traits.Alias, Rows = [new GridRow { Key = Guid.NewGuid() }] }) == FieldAnswerLimits.None)
            .Select(traits => traits.Alias);

        Assert.That(none, Is.EquivalentTo(WithoutAnswers));
    }
}
