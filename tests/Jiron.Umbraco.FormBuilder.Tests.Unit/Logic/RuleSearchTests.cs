using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// What the rule search does beside the shared vectors (ROADMAP 9.5d, ADR-0179): sets of answers too many to count in a
/// long are trimmed to what it tries, never counted past what a long holds, so the search answers at once and can't tell.
/// </summary>
[TestFixture]
public sealed class RuleSearchTests
{
    [Test]
    [Timeout(60_000)]
    public void ChoicesOfAThousandOptionsEach_AreTriedAsFarAsTheSearchTries_AndCanNotBeTold()
    {
        // Seven choices of 1,100 options make more sets of answers than a long counts.
        var choices = Enumerable.Range(1, 7).Select(at => $"choice{at}").ToList();
        IReadOnlyList<string> options = [.. Enumerable.Range(1, 1100).Select(at => $"option{at}")];
        var scope = new SearchScope(choices.ToDictionary(key => key, _ => FieldKind.Choice), _ => options, ["en-US"], "2026-11-04");
        var rule = new LogicRule("show", true, "all", [.. choices.Select(key => (LogicItem)new LogicCondition(key, null, "equals", new TextOperand("nowhere")))]);

        Assert.That(RuleSearch.ShowsAtAll([rule], scope, []), Is.EqualTo(Showing.Unknown));
    }
}
