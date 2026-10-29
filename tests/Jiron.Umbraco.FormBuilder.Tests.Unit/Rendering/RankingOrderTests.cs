using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// A ranking's order as the page shows it (ADR-0096, ADR-0100): the visitor's order, given as positions or as texts in
/// order, then the options the page showed in that order, then the rest in their own; a move by one place, none past an
/// end; and the positions the page posts for the top few.
/// </summary>
public sealed class RankingOrderTests
{
    private static readonly string[] Options = ["a", "b", "c", "d"];

    [Test]
    public void TheOrder_IsTheVisitorsThenTheShownThenTheOptionsOwn_OfTheOptionsOfferedOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RankingOrder.Of(Options, FieldAnswers.None), Is.EqualTo(Options), "the options' own order");
            Assert.That(RankingOrder.Of(Options, new FieldAnswers(["c", "a", "x", "c"])), Is.EqualTo(new[] { "c", "a", "b", "d" }), "texts in order, each once, of the options offered");
            Assert.That(RankingOrder.Of(Options, Positions(("b", ["2"]), ("d", ["1"]), ("c", ["x"]), ("a", ["1", "2"]))), Is.EqualTo(new[] { "d", "b", "a", "c" }), "by position; one that is no number, or two, last, in the order of their values");
            Assert.That(RankingOrder.Of(Options, Positions(("b", ["1"])), ["d", "c", "b", "a"]), Is.EqualTo(new[] { "b", "d", "c", "a" }), "the options not ranked in the order shown");
            Assert.That(RankingOrder.Of(Options, FieldAnswers.None, ["z", "c", "c"]), Is.EqualTo(new[] { "c", "a", "b", "d" }), "a shown value no option has is left out, and one not shown comes in its own order");
            Assert.That(RankingOrder.Of([], Positions(("a", ["1"])), ["a"]), Is.Empty, "no options, no order");
        });
    }

    [Test]
    public void AMove_SwapsTheOptionWithItsNeighbour_AndOnePastAnEndChangesNothing()
    {
        string[] order = ["a", "b", "c"];

        Assert.Multiple(() =>
        {
            Assert.That(RankingOrder.Moved(order, 2, up: true), Is.EqualTo(new[] { "b", "a", "c" }));
            Assert.That(RankingOrder.Moved(order, 2, up: false), Is.EqualTo(new[] { "a", "c", "b" }));
            Assert.That(RankingOrder.Moved(order, 1, up: true), Is.EqualTo(order), "the first up");
            Assert.That(RankingOrder.Moved(order, 3, up: false), Is.EqualTo(order), "the last down");
            Assert.That(RankingOrder.Moved(order, 0, up: false), Is.EqualTo(order), "no place 0");
            Assert.That(RankingOrder.Moved(order, 4, up: true), Is.EqualTo(order), "nor one past the list");
            Assert.That(RankingOrder.Moved([], 1, up: false), Is.Empty);
            Assert.That(order, Is.EqualTo(new[] { "a", "b", "c" }), "the order given stays as it was");
        });
    }

    [Test]
    public void ThePositions_AreTheTopFewsFromOne_OrEveryOptionsWithoutATop()
    {
        string[] order = ["c", "a", "b"];

        Assert.Multiple(() =>
        {
            Assert.That(RankingOrder.Positions(order, null), Is.EqualTo(new Dictionary<string, string> { ["c"] = "1", ["a"] = "2", ["b"] = "3" }));
            Assert.That(RankingOrder.Positions(order, 2), Is.EqualTo(new Dictionary<string, string> { ["c"] = "1", ["a"] = "2" }));
            Assert.That(RankingOrder.Positions(order, 0), Has.Count.EqualTo(3), "a top of 0 asks for all");
            Assert.That(RankingOrder.Positions(order, 5), Has.Count.EqualTo(3), "a top past the options asks for all there are");
            Assert.That(RankingOrder.Positions([], 2), Is.Empty);
        });
    }

    [Test]
    public void WithoutAnOrderOptionsOrAnswers_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => RankingOrder.Of(null!, FieldAnswers.None), Throws.ArgumentNullException);
            Assert.That(() => RankingOrder.Of(Options, null!), Throws.ArgumentNullException);
            Assert.That(() => RankingOrder.Moved(null!, 1, up: true), Throws.ArgumentNullException);
            Assert.That(() => RankingOrder.Positions(null!, null), Throws.ArgumentNullException);
        });
    }

    private static FieldAnswers Positions(params (string Value, string[] Texts)[] parts) =>
        new([], parts.Select(part => new KeyValuePair<string, IEnumerable<string>>(part.Value, part.Texts)));
}
