using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// A repeatable group's Add and Remove and a ranking's moves without JavaScript (ROADMAP 6.2i, ADR-0096, ADR-0098,
// ADR-0100): each the page again with the change and every answer, without a check, a row added starting as a first
// render; an Add past the group's most, a Remove below its least, a move past an end and any other button change nothing.
public sealed partial class FormPostTests
{
    private const string RowsPage = "/post-rows/";

    private static readonly FormField Club = Field(K(0x70), "club", "text");
    private static readonly FormField Captain = Field(K(0x71), "captain", "text") with { Required = true };
    private static readonly FormField Player = Field(K(0x73), "player", "text") with { DefaultValue = FieldDefaultValue.Of("Guest") };
    private static readonly FormField Pick = Field(K(0x74), "pick", "ranking", """{"top":1}""") with { Options = Letters("x", "y", "z") };
    private static readonly FormField Team = Field(K(0x72), "team", "repeater", """{"minRows":1,"maxRows":2}""") with { Fields = [Player, Pick] };
    private static readonly FormField Order = Field(K(0x75), "order", "ranking", """{"top":2}""") with { Options = Letters("a", "b", "c", "d") };
    private static readonly FormField Why = Field(K(0x76), "why", "text") with
    {
        Rules = [new FormRule { Key = K(0x77), Effect = RuleEffect.Show, Conditions = [new RuleCondition { Field = Order.Key, Operator = "countAtLeast", Value = new ConditionOperand.Text("3") }] }],
    };

    [Test]
    public async Task ARowsAddAndRemove_ShowThePageAgainWithTheChange_EveryAnswerKept_AndAnAddPastTheMostOrARemoveBelowTheLeastChangesNothing()
    {
        var key = await PublishFormAsync("postRows", RowsForm);
        _ = await PagesAsync("post-rows");
        var first = FormOf(await DocumentAsync(RowsPage), "jfb-1");
        Fill(first, "club", "Chess");
        Fill(first, "team[0].player", "Ada");

        var added = await MoveAsync(first, "add:team", "jfb-1", RowsPage);
        Fill(added, "team[1].player", "Lin");
        var most = await PressAsync(added, "add:team");
        var removed = await MoveAsync(most, "remove:team:0", "jfb-1", RowsPage);
        var least = await PressAsync(removed, "remove:team:0");
        Fill(least, "captain", "Ada");
        using var sent = await SubmitAsync(least, FormPostFields.Submit, RowsPage);
        var catalogue = await CatalogueAsync(key);
        var stored = (await StoredAsync(key)).Single();

        Assert.Multiple(() =>
        {
            Assert.That((RowTexts(first), RowButtons(first)), Is.EqualTo((new[] { "Guest" }, new[] { "add:team" })), "the least, one row, as a first render starts it");
            Assert.That(RowTexts(added), Is.EqualTo(new[] { "Ada", "Guest" }), "the row added starts as a first render does, its default and all");
            Assert.That(Ranked(added, "team[1].pick"), Is.EqualTo(new[] { "1 X", " Y", " Z" }), "and its ranking in the options' own order");
            Assert.That(RowButtons(added), Is.EqualTo(new[] { "remove:team:0", "remove:team:1" }), "no Add at the most");
            Assert.That(added.QuerySelector("[data-jfb-summary]"), Is.Null, "nothing is checked: the required captain left empty shows no error");
            Assert.That(Value(added, "club"), Is.EqualTo("Chess"));
            Assert.That(RowTexts(most), Is.EqualTo(new[] { "Ada", "Lin" }), "an Add past the most changes nothing");
            Assert.That(Value(most, "club"), Is.EqualTo("Chess"));
            Assert.That((RowTexts(removed), RowButtons(removed)), Is.EqualTo((new[] { "Lin" }, new[] { "add:team" })), "the first row removed, the second moved up; no Remove at the least");
            Assert.That(RowTexts(least), Is.EqualTo(new[] { "Lin" }), "a Remove below the least changes nothing");
            Assert.That(Value(least, "club"), Is.EqualTo("Chess"));
            Assert.That(least.QuerySelector("[data-jfb-summary]"), Is.Null);
            Assert.That(stored.Where(value => value.Field == catalogue[Player.Key]).Select(value => value.Text), Is.EqualTo(new[] { "Lin" }), "the buttons stored nothing; Send stores the row left");
        });
    }

    [Test]
    public async Task ARankingsMoves_ReorderItAsShown_EveryAnswerKept_AndAMovePastAnEndChangesNothing()
    {
        var key = await PublishFormAsync("postRows", RowsForm);
        _ = await PagesAsync("post-rows");
        var first = FormOf(await DocumentAsync(RowsPage), "jfb-1");
        Fill(first, "club", "Chess");

        var top = await MoveAsync(first, "up:order:1", "jfb-1", RowsPage);
        var bottom = await MoveAsync(top, "down:order:4", "jfb-1", RowsPage);
        var below = await MoveAsync(bottom, "up:order:4", "jfb-1", RowsPage);
        var into = await MoveAsync(below, "up:order:3", "jfb-1", RowsPage);
        var down = await MoveAsync(into, "down:order:1", "jfb-1", RowsPage);
        Fill(down, "captain", "Ada");
        using var sent = await SubmitAsync(down, FormPostFields.Submit, RowsPage);
        var catalogue = await CatalogueAsync(key);
        var stored = (await StoredAsync(key)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(Ranked(first, "order"), Is.EqualTo(new[] { "1 A", "2 B", " C", " D" }), "the top two ranked");
            Assert.That(Ranked(top, "order"), Is.EqualTo(Ranked(first, "order")), "a move up past the first changes nothing");
            Assert.That(Ranked(bottom, "order"), Is.EqualTo(Ranked(first, "order")), "nor one down past the last");
            Assert.That(Ranked(below, "order"), Is.EqualTo(new[] { "1 A", "2 B", " D", " C" }), "below the top two too, the order kept as shown");
            Assert.That(Ranked(into, "order"), Is.EqualTo(new[] { "1 A", "2 D", " B", " C" }), "an option from below the top two moves into them");
            Assert.That(Ranked(down, "order"), Is.EqualTo(new[] { "1 D", "2 A", " B", " C" }));
            Assert.That(Hidden(down, "order"), Is.EqualTo(new[] { "jfb-order-order=d", "order.d=1", "jfb-order-order=a", "order.a=2", "jfb-order-order=b", "jfb-order-order=c" }));
            Assert.That(new[] { top, bottom, below, into, down }.Select(form => (Value(form, "club"), form.QuerySelector("[data-jfb-summary]") is null)), Is.All.EqualTo(("Chess", true)), "every answer kept, nothing checked");
            Assert.That(new[] { first, below, into, down }.Select(form => form.QuerySelector("[data-jfb-alias='why']")!.Closest("[hidden][data-jfb-hidden]") is not null), Is.All.True, "the rules read the top two alone, so a rule on three or more never shows its field");
            Assert.That(stored.Where(value => value.Field == catalogue[Order.Key]).OrderBy(value => value.Index).Select(value => value.Text), Is.EqualTo(new[] { "d", "a" }), "the ranking as the page left it");
        });
    }

    [Test]
    public async Task ARowsRanking_MovesInItsRow_AndKeepsItsOrderWhenTheRowMovesUp()
    {
        var key = await PublishFormAsync("postRows", RowsForm);
        _ = await PagesAsync("post-rows");
        var first = FormOf(await DocumentAsync(RowsPage), "jfb-1");
        Fill(first, "team[0].player", "Ada");
        var added = await MoveAsync(first, "add:team", "jfb-1", RowsPage);
        Fill(added, "team[1].player", "Lin");

        var below = await MoveAsync(added, "down:team[1].pick:2", "jfb-1", RowsPage);
        var moved = await MoveAsync(below, "down:team[1].pick:1", "jfb-1", RowsPage);
        var past = await PressAsync(moved, "up:team[2].pick:2");
        var removed = await MoveAsync(past, "remove:team:0", "jfb-1", RowsPage);
        Fill(removed, "captain", "Ada");
        using var sent = await SubmitAsync(removed, FormPostFields.Submit, RowsPage);
        var catalogue = await CatalogueAsync(key);
        var stored = (await StoredAsync(key)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(Ranked(below, "team[1].pick"), Is.EqualTo(new[] { "1 X", " Z", " Y" }), "below the top one, in its row, the order kept as shown");
            Assert.That(Ranked(moved, "team[1].pick"), Is.EqualTo(new[] { "1 Z", " X", " Y" }), "the second row's ranking moved");
            Assert.That(Ranked(moved, "team[0].pick"), Is.EqualTo(new[] { "1 X", " Y", " Z" }), "the first row's as it was");
            Assert.That(RowTexts(moved), Is.EqualTo(new[] { "Ada", "Lin" }));
            Assert.That(Ranked(past, "team[1].pick"), Is.EqualTo(new[] { "1 Z", " X", " Y" }), "a move in a row past those shown changes nothing");
            Assert.That(Ranked(removed, "team[0].pick"), Is.EqualTo(new[] { "1 Z", " X", " Y" }), "the row moved up with its order");
            Assert.That(RowTexts(removed), Is.EqualTo(new[] { "Lin" }));
            Assert.That(stored.Where(value => value.Field == catalogue[Pick.Key]).Select(value => value.Text), Is.EqualTo(new[] { "z" }));
        });
    }

    [Test]
    public async Task AButtonOfNoGroupNoRankingOrNoPlace_ShowsThePageAgainAsPosted()
    {
        var key = await PublishFormAsync("postRows", RowsForm);
        _ = await PagesAsync("post-rows");
        var first = FormOf(await DocumentAsync(RowsPage), "jfb-1");
        Fill(first, "club", "Chess");
        Fill(first, "team[0].player", "Ada");
        string[] navs =
        [
            "add:club", "add:nowhere", "add", "add:team:0", "remove:team", "remove:team:x", "remove:team:-1", "remove:team:1",
            "remove:club:0", "up:club:1", "up:order", "up:order:x", "up:order:0", "down:order:5", "up:order:1:2", "up:nowhere:1",
            "up:team[0]:1", "up:team[0].player:1", "up:order.a:1", "sideways:order:2", string.Empty,
        ];

        List<IElement> shown = [];
        foreach (var nav in navs)
        {
            shown.Add(await PressAsync(first, nav));
        }

        Assert.Multiple(async () =>
        {
            foreach (var (nav, form) in navs.Zip(shown))
            {
                Assert.That((Value(form, "club"), RowTexts(form), Ranked(form, "order"), Ranked(form, "team[0].pick")), Is.EqualTo(("Chess", new[] { "Ada" }, new[] { "1 A", "2 B", " C", " D" }, new[] { "1 X", " Y", " Z" })), nav);
                Assert.That(form.QuerySelector("[data-jfb-summary]"), Is.Null, nav);
            }

            Assert.That(await StoredAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task AMove_FollowsThePostedPositionsOverTheOrder_AndWithoutAnOrderMovesAmongTheRankedOptions()
    {
        _ = await PublishFormAsync("postRows", RowsForm);
        _ = await PagesAsync("post-rows");
        var first = FormOf(await DocumentAsync(RowsPage), "jfb-1");
        var reversed = Posted(first, null).Where(pair => pair.Key != "jfb-order-order").ToList();
        reversed.AddRange(new[] { "d", "c", "b", "a" }.Select(value => new KeyValuePair<string, string>("jfb-order-order", value)));
        reversed.Add(new(FormPostFields.Nav, "down:order:2"));
        var orderless = Posted(first, null).Where(pair => pair.Key != "jfb-order-order").ToList();
        orderless.Add(new(FormPostFields.Nav, "down:order:1"));

        using var followed = await PostPairsAsync(RowsPage, reversed, OwnOrigin, RowsPage);
        using var without = await PostPairsAsync(RowsPage, orderless, OwnOrigin, RowsPage);
        var followedForm = FormOf(await ParseAsync(followed), "jfb-1");
        var withoutForm = FormOf(await ParseAsync(without), "jfb-1");

        Assert.Multiple(() =>
        {
            Assert.That(Ranked(followedForm, "order"), Is.EqualTo(new[] { "1 A", "2 D", " B", " C" }), "the positions first, then the order posted: A B D C, its second moved down");
            Assert.That(Ranked(withoutForm, "order"), Is.EqualTo(new[] { "1 B", "2 A", " C", " D" }), "the ranked options alone, the rest in their own order");
        });
    }

    // A form with a text, a required text, a repeatable group of one or two rows, each with a text that has a default and
    // a ranking of the top one of three options, a ranking of the top two of four options, and a text a rule shows once
    // three or more of them are ranked.
    private static FormDefinition RowsForm(FormDefinition definition) => WithFields(definition, Club, Captain, Team, Order, Why);

    private static FieldOptions Letters(params string[] values) =>
        new() { Items = [.. values.Select(value => new FieldOption { Value = value, Label = new LocalizedText(value.ToUpperInvariant()) })] };

    // Posts the form with a button value no button of the page need have, as a page shown before or another site's
    // markup may post it.
    private async Task<IElement> PressAsync(IElement form, string nav)
    {
        var pairs = Posted(form, null);
        pairs.Add(new(FormPostFields.Nav, nav));
        using var response = await PostPairsAsync(RowsPage, pairs, OwnOrigin, RowsPage);
        return FormOf(await ParseAsync(response), "jfb-1");
    }

    // A text as the page shows it.
    private static string? Value(IElement form, string name) => form.QuerySelector($"input[name='{name}']")?.GetAttribute("value");

    // The players' texts as the page shows them, row by row.
    private static string[] RowTexts(IElement form) =>
        [.. form.QuerySelectorAll("input[name^='team['][name$='].player']").Select(input => input.GetAttribute("value") ?? string.Empty)];

    // The rows' Add and Remove buttons the page offers.
    private static string[] RowButtons(IElement form) =>
        [.. form.QuerySelectorAll("button[name='jfb-nav']").Select(button => button.GetAttribute("value")!).Where(value => value.StartsWith("add:", StringComparison.Ordinal) || value.StartsWith("remove:", StringComparison.Ordinal))];

    // A ranking's options as shown: each one's position, if any, and its label.
    private static string[] Ranked(IElement form, string path) =>
        [.. form.QuerySelectorAll($"[data-jfb-alias='{path}'] ol.jfb-ranking > li").Select(item => item.Children[0].TextContent + " " + item.Children[1].TextContent)];

    // A ranking's hidden inputs, as name=value.
    private static string[] Hidden(IElement form, string path) =>
        [.. form.QuerySelectorAll($"[data-jfb-alias='{path}'] input[type='hidden']").Select(input => input.GetAttribute("name") + "=" + input.GetAttribute("value"))];
}
