using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The choice types' controls on real pages (ROADMAP 6.2d, ADR-0015, ADR-0096): every choice type empty, answered, with
/// an error and hidden by a rule, each with the markup contract's hooks; and options from a library list, from a source
/// and below an answered parent, which are the ones the options endpoint answers for the same answers.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingChoicesTests : RenderingTestBase
{
    /// <summary>The country of the options form, which the view <c>options</c> answers.</summary>
    internal static readonly Guid Country = K(0x40);

    private static readonly Guid Trigger = K(0x01);
    private static readonly Guid RowA = K(0x30);
    private static readonly Guid RowB = K(0x31);
    private static readonly Guid Region = K(0x41);
    private static readonly Guid Plug = K(0x42);
    private static readonly Guid Sizes = K(0x43);
    private static readonly Guid Broken = K(0x44);
    private static readonly Guid City = K(0x45);
    private static readonly Guid Home = K(0x46);
    private static readonly Guid Newsletter = K(0x47);
    private static readonly Guid SizesList = K(0x50);
    private static readonly Guid Way = K(0x60);
    private static readonly Guid HideParent = K(0x61);
    private static readonly Guid Parent = K(0x62);
    private static readonly Guid Child = K(0x63);
    private static readonly Guid ScaleGrid = K(0x64);
    private static readonly Guid Agree = K(0x65);
    private static readonly Guid Toggle = K(0x66);
    private static readonly Guid RowC = K(0x32);
    private static readonly Guid When = K(0x67);
    private static readonly Guid HideGrid = K(0x68);
    private static readonly Guid Rate = K(0x69);
    private static readonly Guid Extra = K(0x6a);
    private static readonly Guid Room = K(0x6b);

    // Each choice field: its key, alias and type, and whether it is a group of choices in a fieldset.
    private static readonly (Guid Key, string Alias, string Type, bool Grouped)[] Choices =
    [
        (K(0x10), "radioList", "radio", true),
        (K(0x11), "radioRow", "radio", true),
        (K(0x12), "radioCards", "radio", true),
        (K(0x13), "checks", "checkboxes", true),
        (K(0x14), "pick", "dropdown", false),
        (K(0x15), "picks", "dropdown", false),
        (K(0x16), "yesButtons", "yesno", true),
        (K(0x17), "yesSwitch", "yesno", false),
        (K(0x18), "nps", "scale", true),
        (K(0x19), "stars", "rating", true),
        (K(0x1a), "grid", "grid", true),
        (K(0x1b), "rank", "ranking", true),
        (K(0x1c), "box", "checkbox", false),
        (K(0x1d), "consent", "consent", false),
        (K(0x1e), "country", "country", false),
    ];

    // Each choice field's default message for no answer (ADR-0099): by what its control asks, a yes or no in its own words.
    private static readonly Dictionary<string, string> Required = new(StringComparer.Ordinal)
    {
        ["radioList"] = "Choose an answer",
        ["radioRow"] = "Choose an answer",
        ["radioCards"] = "Choose an answer",
        ["checks"] = "Choose at least one answer",
        ["pick"] = "Choose an answer",
        ["picks"] = "Choose at least one answer",
        ["yesButtons"] = "Choose Sure or No",
        ["yesSwitch"] = "Choose Yes or No",
        ["nps"] = "Choose a point on the scale",
        ["stars"] = "Choose a rating",
        ["grid"] = "Choose an answer in each row",
        ["rank"] = "Put the options in order",
        ["box"] = "Tick the box",
        ["consent"] = "Tick the box to agree",
        ["country"] = "Choose a country",
    };

    /// <summary>The state the view <c>choices</c> leaves for the form: none, or a post's answers, errors or the rules' trigger.</summary>
    /// <param name="state">The state's name: answered, errors or hidden.</param>
    internal static FormPostState? State(string state) => state switch
    {
        "answered" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [Of("radioList")] = new FieldAnswers([], Parts(("other", "By post"))),
                [Of("radioRow")] = new FieldAnswers(["b"]),
                [Of("radioCards")] = new FieldAnswers(["a"]),
                [Of("checks")] = new FieldAnswers(["a", "c"], Parts(("other", "Extra"))),
                [Of("pick")] = new FieldAnswers(["b"]),
                [Of("picks")] = new FieldAnswers(["a", "c"]),
                [Of("yesButtons")] = new FieldAnswers(["no"]),
                [Of("yesSwitch")] = new FieldAnswers(["yes"]),
                [Of("nps")] = new FieldAnswers(["8"]),
                [Of("stars")] = new FieldAnswers(["4"]),
                [Of("grid")] = new FieldAnswers([], Parts((RowA.ToString("D"), "2"), (RowB.ToString("D"), "1"))),
                [Of("rank")] = new FieldAnswers([], Parts(("z", "1"), ("x", "2"))),
                [Of("box")] = new FieldAnswers(["true"]),
                [Of("consent")] = new FieldAnswers(["true"]),
                [Of("country")] = new FieldAnswers(["CH"]),
            },
        },
        "errors" => new FormPostState { Errors = Choices.ToDictionary(choice => choice.Key, _ => new FormPostError("required")) },
        "hidden" => new FormPostState { Answers = new Dictionary<Guid, FieldAnswers> { [Trigger] = new FieldAnswers(["yes"]) } },
        _ => null,
    };

    /// <summary>The state the view <c>rare</c> leaves for its form: none, or a post that chose an option and gave the "Other" text.</summary>
    /// <param name="state">The state's name: posted, or anything else for none.</param>
    internal static FormPostState? RareState(string state) => state == "posted"
        ? new FormPostState { Answers = new Dictionary<Guid, FieldAnswers> { [Way] = new FieldAnswers(["b"], Parts(("other", "By boat"))) } }
        : null;

    /// <summary>The state the view <c>hooks</c> leaves for its form: none, or a post whose answers show the grid and require it and the extra field.</summary>
    /// <param name="state">The state's name: posted, or anything else for none.</param>
    internal static FormPostState? HooksState(string state) => state == "posted"
        ? new FormPostState { Answers = new Dictionary<Guid, FieldAnswers> { [When] = new FieldAnswers(["no"]), [HideGrid] = new FieldAnswers(["no"]) } }
        : null;

    [Test]
    public async Task AFormWithRules_WritesTheHooksTheRuntimeShowsThemBy_InTheStateItsAnswersGive()
    {
        _ = await PublishFormAsync("renderingHooks", HooksForm);
        _ = await PagesAsync("hooks");
        var first = (await DocumentAsync("/hooks/")).QuerySelector("form.jfb-form")!;
        var posted = (await DocumentAsync("/hooks/?state=posted")).QuerySelector("form.jfb-form")!;
        var food = "rate." + RowA.ToString("D");

        Assert.Multiple(() =>
        {
            Assert.That(first.QuerySelector("p.jfb-help")!.OuterHtml, Does.StartWith("""<p class="jfb-help" hidden="hidden" data-jfb-required-note="">"""), "no asterisk shows yet, so the note waits hidden");
            Assert.That(Field(first, "extra").QuerySelector("label")!.InnerHtml, Is.EqualTo("""Extra <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required" hidden="">*</span>"""), "the asterisk it shows while a rule requires it");
            Assert.That(ShownText(Field(first, "extra").QuerySelector("label")!), Is.EqualTo("Extra"));
            Assert.That(first.QuerySelector("#jfb-1-extra")!.OuterHtml, Is.EqualTo("""<input class="jfb-control" type="text" id="jfb-1-extra" name="extra" data-jfb-requires="">"""), "required once a rule requires it");
            var rate = Field(first, "rate");
            Assert.That((rate.HasAttribute("hidden"), rate.HasAttribute("data-jfb-hidden")), Is.EqualTo((true, true)), "a rule hides the grid");
            Assert.That(rate.QuerySelector("legend.jfb-label")!.InnerHtml, Is.EqualTo("""Rate <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required" hidden="">*</span>"""));
            Assert.That(rate.QuerySelectorAll(".jfb-matrix > fieldset > legend").Select(legend => legend.InnerHtml), Is.EqualTo(new[] { """Food <span class="jfb-mark" aria-hidden="true" data-jfb-mark="optional">*</span>""", "Music" }), "a row required by itself shows its asterisk while the grid is not required; a hidden grid's asks for no note");
            Assert.That(rate.QuerySelectorAll("input[type=radio]").Select(input => (input.GetAttribute("name") == food, input.HasAttribute("required"), input.HasAttribute("data-jfb-requires"))).Distinct(), Is.EqualTo(new[] { (true, true, false), (false, false, true) }), "a row required by itself is required whatever the grid is");
            Assert.That(Field(first, "room").QuerySelector("select")!.OuterHtml, Does.Contain(""" disabled="disabled" data-jfb-disabled="">"""), "a list without options stays disabled when its field shows");

            Assert.That((posted.QuerySelector("p.jfb-help")!.HasAttribute("hidden"), posted.QuerySelector("p.jfb-help")!.HasAttribute("data-jfb-required-note")), Is.EqualTo((false, true)), "an asterisk shows, so does the note");
            Assert.That(Field(posted, "extra").QuerySelector("label")!.InnerHtml, Is.EqualTo("""Extra <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required">*</span>"""));
            Assert.That(posted.QuerySelector("#jfb-1-extra")!.HasAttribute("required"), Is.True);
            Assert.That(Field(posted, "rate").HasAttribute("hidden"), Is.False);
            Assert.That(ShownText(Field(posted, "rate").QuerySelector("legend.jfb-label")!), Is.EqualTo("Rate *"));
            Assert.That(Field(posted, "rate").QuerySelectorAll(".jfb-matrix > fieldset > legend").Select(ShownText), Is.EqualTo(new[] { "Food", "Music" }), "the grid is required, so its rows show no asterisk of their own");
            Assert.That(Field(posted, "rate").QuerySelectorAll("input[type=radio]").All(input => input.HasAttribute("required")), Is.True);
        });
    }

    [Test]
    public async Task EveryChoiceType_EmptyAnsweredWithAnErrorAndHidden_CarriesTheMarkupContractsHooks()
    {
        var card = Image("Reading room", "/media/jfb/card.png", 600, 400);
        var key = await PublishFormAsync("renderingChoices", definition => ChoicesForm(definition, card));
        _ = await PagesAsync("choices");
        using var endpoint = await GetAsync($"{Root}/forms/{key}");
        var hash = (await JsonAsync(endpoint))["consents"]![Of("consent").ToString("D")]!.GetValue<string>();

        foreach (var state in new[] { "empty", "answered", "errors", "hidden" })
        {
            var form = (await ChoicesAsync(state)).QuerySelector("form.jfb-form") ?? throw new AssertionException($"{state}: no form");
            Hooks(form, state, hash);
        }

        var empty = (await ChoicesAsync("empty")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(empty.QuerySelectorAll("input:checked, option:checked").Where(element => element is not IHtmlOptionElement { Value: "" }), Is.Empty, "nothing is chosen before an answer");
            Assert.That(empty.QuerySelector("button")!.OuterHtml, Is.EqualTo("""<button type="submit" class="jfb-visually-hidden" name="jfb-nav" value="submit" tabindex="-1" aria-hidden="true">Send</button>"""), "the ranking's moves come before Send, so a copy of Send comes first");

            var list = Field(empty, "radioList");
            Assert.That(Text(list, "legend.jfb-label"), Is.EqualTo("Radio list"), "a required field has no mark");
            Assert.That(list.QuerySelector("fieldset")!.GetAttribute("aria-describedby"), Is.EqualTo("jfb-1-radioList-help"));
            Assert.That(list.QuerySelector("fieldset > legend + p.jfb-help")!.TextContent, Is.EqualTo("Pick one"), "the help follows the legend");
            Assert.That(Inputs(list).Select(input => (input.Id, input.Value, input.IsRequired)), Is.EqualTo(new[]
            {
                ("jfb-1-radioList", "a", true), ("jfb-1-radioList-2", "b", true), ("jfb-1-radioList-3", "c", true), ("jfb-1-radioList-4", string.Empty, true),
            }), "the Other choice posts no value of its own");
            Assert.That(list.QuerySelectorAll(".jfb-choice label").Select(label => label.TextContent), Is.EqualTo(new[] { "A", "B", "C", "Some other way" }));
            Assert.That(list.QuerySelector(".jfb-choices")!.GetAttribute("class"), Is.EqualTo("jfb-choices"));
            Assert.That(Compact(list.QuerySelector(".jfb-other")!.InnerHtml), Is.EqualTo("""<label class="jfb-visually-hidden" for="jfb-1-radioList-other">Your own answer</label><input class="jfb-control" id="jfb-1-radioList-other" name="radioList.other">"""));
            Assert.That(Text(Field(empty, "radioRow"), "legend"), Is.EqualTo("Radio row (optional)"), "the site's style marks the fields that need no answer");
            Assert.That(Field(empty, "radioRow").QuerySelector(".jfb-choices")!.GetAttribute("class"), Is.EqualTo("jfb-choices jfb-choices-horizontal"));
            Assert.That(Field(empty, "checks").QuerySelector(".jfb-choices")!.GetAttribute("class"), Is.EqualTo("jfb-choices jfb-choices-cards"), "inherit takes the form's layout");

            var cards = Field(empty, "radioCards");
            Assert.That(cards.QuerySelector(".jfb-choices")!.GetAttribute("class"), Is.EqualTo("jfb-choices jfb-choices-cards"));
            Assert.That(Compact(cards.QuerySelector(".jfb-choice")!.InnerHtml), Is.EqualTo(
                """<img src="/media/jfb/card.png" alt="" width="600" height="400"><input type="radio" id="jfb-1-radioCards" name="radioCards" value="a" data-jfb-requires="" aria-describedby="jfb-1-radioCards-text"><label for="jfb-1-radioCards">A</label><p class="jfb-help" id="jfb-1-radioCards-text">The first card</p>"""),
                "a card shows its image, which adds nothing to the label, and its description");
            Assert.That(cards.QuerySelectorAll(".jfb-choice img"), Has.Length.EqualTo(1), "an option whose media item is gone has no image");

            var pick = Field(empty, "pick");
            Assert.That(pick.QuerySelector("label.jfb-label")!.GetAttribute("for"), Is.EqualTo("jfb-1-pick"));
            Assert.That(pick.QuerySelector(".jfb-select > select.jfb-control")!.GetAttribute("multiple"), Is.Null);
            Assert.That(Options(pick), Is.EqualTo(new[] { ": Choose one", "a: A", "b: B", "c: C" }));
            var picks = Field(empty, "picks");
            Assert.That(picks.QuerySelector(".jfb-select"), Is.Null, "a list box has no chevron");
            Assert.That(Attributes(picks.QuerySelector("select")!), Is.EquivalentTo(new Dictionary<string, string>
            {
                ["class"] = "jfb-control",
                ["id"] = "jfb-1-picks",
                ["name"] = "picks",
                ["multiple"] = "multiple",
                ["size"] = "3",
                ["data-jfb-requires"] = string.Empty,
                ["aria-describedby"] = "jfb-1-picks-hint",
            }));
            Assert.That(Text(picks, "#jfb-1-picks-hint"), Is.EqualTo("To choose more than one, hold Ctrl (Cmd on a Mac) while you click."));
            Assert.That(Options(picks), Is.EqualTo(new[] { "a: A", "b: B", "c: C" }));

            var buttons = Field(empty, "yesButtons");
            Assert.That(buttons.QuerySelector(".jfb-choices")!.GetAttribute("class"), Is.EqualTo("jfb-choices jfb-choices-buttons"));
            Assert.That(Inputs(buttons).Select(input => (input.Value, Label(buttons, input))), Is.EqualTo(new[] { ("yes", "Sure"), ("no", "No") }));
            var toggle = Field(empty, "yesSwitch");
            Assert.That(toggle.QuerySelector("fieldset"), Is.Null, "a switch is one box with its own label");
            Assert.That(toggle.QuerySelector(".jfb-choice > input")!.OuterHtml, Is.EqualTo("""<input type="checkbox" role="switch" class="jfb-switch" id="jfb-1-yesSwitch" name="yesSwitch" value="yes" aria-describedby="jfb-1-yesSwitch-help">"""));
            Assert.That(toggle.QuerySelector(".jfb-choice > label")!.TextContent, Is.EqualTo("Yes switch"), "a switch is never marked: off is an answer");
            Assert.That(toggle.QuerySelector(".jfb-choice + p.jfb-help")!.TextContent, Is.EqualTo("Listed in the guide"), "the help follows the choice");

            var nps = Field(empty, "nps");
            Assert.That(nps.QuerySelectorAll(".jfb-scale > .jfb-choices.jfb-choices-scale input").Select(input => input.GetAttribute("value")), Is.EqualTo(Enumerable.Range(0, 11).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            Assert.That(nps.QuerySelector("label[for=jfb-1-nps]")!.InnerHtml, Is.EqualTo("""<span aria-hidden="true">0</span><span class="jfb-visually-hidden">0, Not likely</span>"""), "the whole name in one span, so no browser puts a space before its comma");
            Assert.That(nps.QuerySelector("label[for=jfb-1-nps-11]")!.InnerHtml, Is.EqualTo("""<span aria-hidden="true">10</span><span class="jfb-visually-hidden">10, Very likely</span>"""));
            Assert.That(nps.QuerySelector("label[for=jfb-1-nps-2]")!.InnerHtml, Is.EqualTo("1"), "a point between the ends is its number");
            Assert.That(nps.QuerySelector(".jfb-ends")!.OuterHtml, Is.EqualTo("""<div class="jfb-ends" aria-hidden="true"><span>Not likely</span> <span>Very likely</span></div>"""), "the words at the ends apart, also without a stylesheet");
            var stars = Field(empty, "stars");
            Assert.That(stars.QuerySelector(".jfb-scale"), Is.Null, "a rating without words at its ends needs no frame");
            Assert.That(stars.QuerySelector(".jfb-choices")!.GetAttribute("class"), Is.EqualTo("jfb-choices jfb-choices-rating jfb-choices-heart"));
            Assert.That(stars.QuerySelectorAll("label").Select(label => label.InnerHtml), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }.Select(number => $"""<span class="jfb-rating-value">{number}</span>""")), "each point's number shows without a theme; a theme that draws the symbols hides it");

            var grid = Field(empty, "grid");
            Assert.That(grid.QuerySelector(".jfb-matrix-head")!.GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(grid.QuerySelectorAll(".jfb-matrix-head > div > span").Select(span => span.TextContent), Is.EqualTo(new[] { "Poor", "Fair", "Good" }));
            Assert.That(grid.QuerySelectorAll(".jfb-matrix > fieldset > legend").Select(legend => legend.TextContent), Is.EqualTo(new[] { "Booking", "The room" }));
            Assert.That(Inputs(grid).Select(input => (input.Id, input.Name, input.IsRequired)), Is.EqualTo(new[]
            {
                ("jfb-1-grid", $"grid.{RowA:D}", false), ("jfb-1-grid-1-2", $"grid.{RowA:D}", false), ("jfb-1-grid-1-3", $"grid.{RowA:D}", false),
                ("jfb-1-grid-2-1", $"grid.{RowB:D}", true), ("jfb-1-grid-2-2", $"grid.{RowB:D}", true), ("jfb-1-grid-2-3", $"grid.{RowB:D}", true),
            }), "a required row's radios are required; the first input carries the field's id");
            Assert.That(Text(grid, "legend.jfb-label"), Is.EqualTo("Grid"), "a grid with a required row is not optional");
            Assert.That(grid.QuerySelector("label[for=jfb-1-grid]")!.InnerHtml, Is.EqualTo("<span>Poor</span>"));

            var rank = Field(empty, "rank");
            Assert.That(rank.QuerySelectorAll("ol.jfb-ranking > li").Select(item => (item.Children[0].TextContent, item.Children[1].TextContent)), Is.EqualTo(new[] { ("1", "X"), ("2", "Y"), (string.Empty, "Z") }), "the top two are ranked");
            Assert.That(Hidden(rank), Is.EqualTo(new[] { "jfb-order-rank=x", "rank.x=1", "jfb-order-rank=y", "rank.y=2", "jfb-order-rank=z" }), "each option's place in the order shown, and the top two's positions");
            Assert.That(rank.QuerySelectorAll("button").Select(button => (button.GetAttribute("id"), button.GetAttribute("value"), button.GetAttribute("aria-disabled"), button.TextContent)), Is.EqualTo(new (string?, string?, string?, string)[]
            {
                ("jfb-1-rank", "up:rank:1", "true", "Move X up"), (null, "down:rank:1", null, "Move X down"),
                (null, "up:rank:2", null, "Move Y up"), (null, "down:rank:2", null, "Move Y down"),
                (null, "up:rank:3", null, "Move Z up"), (null, "down:rank:3", "true", "Move Z down"),
            }), "the moves are submit buttons; one at an end is aria-disabled and stays in the tab order");
            Assert.That(rank.QuerySelectorAll("button").All(button => button.GetAttribute("type") == "submit" && button.GetAttribute("name") == "jfb-nav"), Is.True);
            Assert.That(rank.QuerySelector("ol.jfb-ranking")!.GetAttribute("role"), Is.EqualTo("list"), "a list the themes draw without markers says it is one");
            Assert.That(rank.QuerySelectorAll("ol.jfb-ranking > li").Select(item => item.Id), Is.EqualTo(new[] { "jfb-1-rank-place-1", "jfb-1-rank-place-2", "jfb-1-rank-place-3" }));
            Assert.That(rank.QuerySelectorAll("button").Select(button => button.GetAttribute("formaction")), Is.EqualTo(new[]
            {
                "/choices/#jfb-1-rank-place-1", "/choices/#jfb-1-rank-place-2",
                "/choices/#jfb-1-rank-place-1", "/choices/#jfb-1-rank-place-3",
                "/choices/#jfb-1-rank-place-2", "/choices/#jfb-1-rank-place-3",
            }), "each move opens the page at the option's new place, one at an end at its own");

            var box = Field(empty, "box");
            Assert.That(Compact(box.InnerHtml), Is.EqualTo("""<div class="jfb-choice"><input type="checkbox" id="jfb-1-box" name="box" value="true" data-jfb-requires=""><label for="jfb-1-box">Send me <a href="/news">the newsletter</a></label></div>"""), "one box takes its text as its label, unmarked");
            var consent = Field(empty, "consent");
            Assert.That(consent.QuerySelector("input[type=checkbox]")!.OuterHtml, Is.EqualTo("""<input type="checkbox" id="jfb-1-consent" name="consent" value="true" required="required" data-jfb-requires="">"""));
            Assert.That(consent.QuerySelector("label")!.InnerHtml, Is.EqualTo("""I agree to the <a href="/privacy">privacy notice</a>."""));

            var country = Field(empty, "country");
            Assert.That(country.QuerySelector("select")!.GetAttribute("autocomplete"), Is.EqualTo("country"));
            Assert.That(Options(country), Is.EqualTo(new[] { ": Choose a country", "CH: Switzerland", "AT: Austria", "FR: France", "DE: Germany" }), "the one shown first, then the others by their names");
        });

        var answered = (await ChoicesAsync("answered")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(Checked(Field(answered, "radioList")), Is.EqualTo(new[] { string.Empty }), "the Other choice, for a text without an option");
            Assert.That(Field(answered, "radioList").QuerySelector("#jfb-1-radioList-other")!.GetAttribute("value"), Is.EqualTo("By post"));
            Assert.That(Checked(Field(answered, "radioRow")), Is.EqualTo(new[] { "b" }));
            Assert.That(Checked(Field(answered, "radioCards")), Is.EqualTo(new[] { "a" }));
            Assert.That(Checked(Field(answered, "checks")), Is.EqualTo(new[] { "a", "c", string.Empty }), "the options and the Other choice");
            Assert.That(Field(answered, "checks").QuerySelector("#jfb-1-checks-other")!.GetAttribute("value"), Is.EqualTo("Extra"));
            Assert.That(Selected(Field(answered, "pick")), Is.EqualTo(new[] { "b" }));
            Assert.That(Selected(Field(answered, "picks")), Is.EqualTo(new[] { "a", "c" }));
            Assert.That(Checked(Field(answered, "yesButtons")), Is.EqualTo(new[] { "no" }));
            Assert.That(Checked(Field(answered, "yesSwitch")), Is.EqualTo(new[] { "yes" }));
            Assert.That(Checked(Field(answered, "nps")), Is.EqualTo(new[] { "8" }));
            Assert.That(Checked(Field(answered, "stars")), Is.EqualTo(new[] { "4" }));
            Assert.That(Inputs(Field(answered, "grid")).Where(input => input.IsChecked).Select(input => (input.Name, input.Value)), Is.EqualTo(new[] { ($"grid.{RowA:D}", "2"), ($"grid.{RowB:D}", "1") }));
            Assert.That(Field(answered, "rank").QuerySelectorAll("li").Select(item => item.Children[1].TextContent), Is.EqualTo(new[] { "Z", "X", "Y" }), "the posted positions, then the rest in their order");
            Assert.That(Hidden(Field(answered, "rank")), Is.EqualTo(new[] { "jfb-order-rank=z", "rank.z=1", "jfb-order-rank=x", "rank.x=2", "jfb-order-rank=y" }));
            Assert.That(Checked(Field(answered, "box")), Is.EqualTo(new[] { "true" }));
            Assert.That(Checked(Field(answered, "consent")), Is.EqualTo(new[] { "true" }));
            Assert.That(Selected(Field(answered, "country")), Is.EqualTo(new[] { "CH" }));
        });

        var errors = (await ChoicesAsync("errors")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(errors.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(Choices.Select(choice => "#jfb-1-" + choice.Alias)));
            foreach (var (_, alias, _, grouped) in Choices)
            {
                var field = Field(errors, alias);
                var error = field.QuerySelector($"p.jfb-error#jfb-1-{alias}-error[data-jfb-error]");
                Assert.That(error?.TextContent, Is.EqualTo(Required[alias]), alias);
                var described = grouped ? field.QuerySelector(":scope > fieldset")! : Control(errors, alias);
                Assert.That(described.GetAttribute("aria-invalid"), Is.EqualTo("true"), alias);
                Assert.That(described.GetAttribute("aria-describedby")?.Split(' '), Does.Contain($"jfb-1-{alias}-error"), alias);
                if (grouped)
                {
                    Assert.That(error?.ParentElement, Is.SameAs(described), $"{alias}: the error is in the fieldset");
                }
            }
        });

        var hidden = (await ChoicesAsync("hidden")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            foreach (var (_, alias, _, _) in Choices)
            {
                var field = Field(hidden, alias);
                Assert.That((field.HasAttribute("hidden"), field.HasAttribute("data-jfb-hidden")), Is.EqualTo((true, true)), alias);
                Assert.That(field.QuerySelectorAll("input, select, button").Where(control => !control.HasAttribute("disabled")).Select(control => control.OuterHtml), Is.Empty, $"{alias}: nothing of a hidden field is posted");
            }
        });
    }

    [Test]
    public async Task OptionsFromAListASourceOrBelowAnAnsweredParent_AreTheOnesTheOptionsEndpointAnswers()
    {
        await GetRequiredService<OptionListService>().CreateAsync(SizesList, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","label":{"":"Small"}},{"value":"m","label":{"":"Medium"},"description":{"":"Most of us"}}]"""));
        var key = await PublishFormAsync("renderingOptions", OptionsForm);
        var page = (await PagesAsync("options"))["options"];

        var first = (await OptionsPageAsync(string.Empty)).QuerySelector("form.jfb-form")!;
        var posted = (await OptionsPageAsync("de")).QuerySelector("form.jfb-form")!;
        var regionForCh = await EndpointAsync(key, Region, page, "ch");
        var regionForDe = await EndpointAsync(key, Region, page, "de");
        var plugForCh = await EndpointAsync(key, Plug, page, "ch");
        var plugForDe = await EndpointAsync(key, Plug, page, "de");
        var sizes = await EndpointAsync(key, Sizes, page, null);
        var broken = await EndpointAsync(key, Broken, page, null);
        var cities = await EndpointAsync(key, City, page, null);

        Assert.Multiple(() =>
        {
            Assert.That(Options(Field(first, "region")).Skip(1), Is.EqualTo(Answered(regionForCh)), "from a source, for the parent's default");
            Assert.That(Answered(regionForCh), Is.EqualTo(new[] { "zh: Zurich", "be: Bern", "other: Elsewhere" }));
            Assert.That(Options(Field(posted, "region")).Skip(1), Is.EqualTo(Answered(regionForDe)), "from a source, for the parent's posted answer");
            Assert.That(Answered(regionForDe), Is.EqualTo(new[] { "by: Bavaria", "other: Elsewhere" }));
            Assert.That(Labelled(Field(first, "plug")), Is.EqualTo(Answered(plugForCh)), "the field's own options a parent filters");
            Assert.That(Labelled(Field(posted, "plug")), Is.EqualTo(Answered(plugForDe)));
            Assert.That(Answered(plugForDe), Is.EqualTo(new[] { "c: Type C" }));
            Assert.That(Labelled(Field(first, "sizes")), Is.EqualTo(Answered(sizes)), "from a library list");
            Assert.That(Answered(sizes), Is.EqualTo(new[] { "s: Small", "m: Medium" }));
            Assert.That(Text(Field(first, "sizes"), "#jfb-1-sizes-2-text"), Is.EqualTo("Most of us"), "a list's description");

            Assert.That(broken["available"]!.GetValue<bool>(), Is.False);
            Assert.That(Field(first, "broken").QuerySelector("fieldset > p.jfb-help#jfb-1-broken")?.TextContent, Is.EqualTo("The choices could not be loaded. Please try again later."), "a source that failed, which the error summary can still link to");
            Assert.That(Answered(cities), Is.Empty);
            Assert.That(Compact(Field(first, "city").QuerySelector("select")!.OuterHtml), Is.EqualTo(
                """<select class="jfb-control" id="jfb-1-city" name="city" disabled="disabled"><option value="">There is nothing to choose from yet.</option></select>"""),
                "a list below a parent not answered yet offers nothing, and posts nothing");
            Assert.That(first.QuerySelectorAll("[data-jfb-requires], [data-jfb-mark], [data-jfb-disabled], [data-jfb-required-note]").Select(element => element.OuterHtml), Is.Empty, "a form without rules carries no hooks for them");

            Assert.That(Selected(Field(first, "home")), Is.EqualTo(new[] { "DE" }), "the field's default country on a first render");
            Assert.That(Selected(Field(posted, "home")), Is.Empty, "after a post, only the visitor's answer");

            Assert.That(Field(first, "newsletter").QuerySelector("[data-test-mark]")?.GetAttribute("data-test-mark"), Is.EqualTo(nameof(FieldMark.None)), "one box that needs no answer is not optional, as it may stay unticked by nature; the form's own view sees no mark");
        });
    }

    [Test]
    public async Task UnderTheAsteriskStyle_MarksAScaleGridAHiddenParentABlankLabelAndOtherBesideAnOption_RenderAsDecided()
    {
        _ = await PublishFormAsync("renderingRare", RareForm);
        _ = await PagesAsync("rare");

        var first = (await DocumentAsync("/rare/")).QuerySelector("form.jfb-form")!;
        var posted = (await DocumentAsync("/rare/?state=posted")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            var grid = Field(first, "scaleGrid");
            Assert.That(grid.QuerySelectorAll(".jfb-matrix-head > div > span").Select(span => span.TextContent), Is.EqualTo(new[] { "0", "1", "2" }), "a grid on a scale takes its numbers as columns");
            Assert.That(grid.QuerySelectorAll(".jfb-matrix > fieldset > legend").Select(legend => legend.InnerHtml), Is.EqualTo(new[] { "Booking", """The room <span class="jfb-mark" aria-hidden="true" data-jfb-mark="optional">*</span>""", "Row 3" }), "a required row of a grid that needs no answer carries the asterisk; a row without a label is numbered");
            Assert.That(Text(grid, "legend.jfb-label"), Is.EqualTo("Scale grid"));
            Assert.That(Field(first, "agree").QuerySelector("label")!.InnerHtml, Is.EqualTo("""Agree <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required">*</span>"""), "a required box carries the asterisk");
            Assert.That(Field(first, "toggle").QuerySelector("[data-test-mark]")?.GetAttribute("data-test-mark"), Is.EqualTo(nameof(FieldMark.None)), "a required switch is never marked, as off is an answer; the form's own view sees no mark");
            Assert.That(Labelled(Field(first, "child")), Is.EqualTo(new[] { "z: Z" }), "a parent a rule hides answers nothing, so its default filters nothing");
            Assert.That(Labelled(Field(first, "way")), Is.EqualTo(new[] { "a: A", "b: b", ": Other" }), "an option without a label shows its value");
            Assert.That(Checked(Field(posted, "way")), Is.EqualTo(new[] { "b" }), "a radio's Other text is its answer only when no option is chosen");
            Assert.That(Field(posted, "way").QuerySelector("#jfb-1-way-other")?.GetAttribute("value"), Is.EqualTo("By boat"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<SourceCalls>();
        builder.JironFormBuilder().OptionSources().Append<RegionsOptionSource>().Append<CitiesOptionSource>().Append<BrokenOptionSource>();
    }

    // The markup contract's hooks on every choice field (ADR-0015): the field's attributes, a unique id per element and
    // per option with its label, the control's id the error summary links to, a fieldset with its legend for a group of
    // choices, the name repeated for several answers, the Other input, a grid row's names and the consent's hash.
    private static void Hooks(IElement form, string state, string hash)
    {
        Assert.Multiple(() =>
        {
            Assert.That(form.QuerySelectorAll("[id]").Select(element => element.Id), Is.Unique, state);
            foreach (var (key, alias, type, grouped) in Choices)
            {
                var field = Field(form, alias);
                Assert.That((field.GetAttribute("data-jfb-field"), field.GetAttribute("data-jfb-type"), string.Join(' ', field.ClassList.Take(2))), Is.EqualTo((key.ToString("D"), type, "jfb-field jfb-field-" + type)), $"{state}: {alias}");
                Assert.That(field.QuerySelector($"#jfb-1-{alias}"), Is.Not.Null, $"{state}: {alias} has the control's id");
                Assert.That(field.QuerySelector(":scope > fieldset > legend.jfb-label") is not null, Is.EqualTo(grouped), $"{state}: {alias}'s fieldset and legend");
                foreach (var input in Inputs(field))
                {
                    Assert.That(form.QuerySelectorAll($"label[for='{input.Id}']"), Has.Length.EqualTo(1), $"{state}: {alias}'s choice {input.Id} has its label");
                }
            }

            Assert.That(Inputs(Field(form, "checks")).Select(input => input.Name).Distinct(), Is.EqualTo(new[] { "checks" }), $"{state}: the boxes repeat the name");
            Assert.That(Field(form, "picks").QuerySelector("select[name=picks][multiple]"), Is.Not.Null, state);
            Assert.That(Field(form, "radioList").QuerySelector(".jfb-choices > .jfb-other > input[name='radioList.other']"), Is.Not.Null, state);
            Assert.That(Inputs(Field(form, "grid")).Select(input => input.Name).Distinct(), Is.EqualTo(new[] { $"grid.{RowA:D}", $"grid.{RowB:D}" }), $"{state}: a row's names");
            Assert.That(Field(form, "consent").QuerySelector("input[type=hidden]")?.OuterHtml.Replace(" disabled=\"disabled\"", string.Empty, StringComparison.Ordinal), Is.EqualTo($"""<input type="hidden" name="jfb-consent-consent" value="{hash}">"""), $"{state}: the form endpoint's hash");
        });
    }

    // Every choice type in one group with a field that hides them; the form lays choices out as cards.
    private static FormDefinition ChoicesForm(FormDefinition definition, Guid card) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { ChoiceLayout = ChoiceLayout.Cards } },
        Pages =
        [
            new FormPage
            {
                Key = K(0x02),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x03),
                        Fields =
                        [
                            Field(Trigger, "hide", "text", "{}", rules: false),
                            Field(Of("radioList"), "radioList", "radio", """{"layout":"vertical","other":true,"otherLabel":{"":"Some other way"}}""", Items("a", "b", "c")) with { Required = true, Help = new LocalizedText("Pick one") },
                            Field(Of("radioRow"), "radioRow", "radio", """{"layout":"horizontal"}""", Items("a", "b")),
                            Field(Of("radioCards"), "radioCards", "radio", """{"layout":"cards"}""", new FieldOptions
                            {
                                Items =
                                [
                                    new FieldOption { Value = "a", Label = new LocalizedText("A"), Description = new LocalizedText("The first card"), Image = card },
                                    new FieldOption { Value = "b", Label = new LocalizedText("B"), Image = Guid.Parse("40000000-0000-4000-8000-000000000001") },
                                ],
                            }),
                            Field(Of("checks"), "checks", "checkboxes", """{"other":true}""", Items("a", "b", "c")),
                            Field(Of("pick"), "pick", "dropdown", "{}", Items("a", "b", "c")),
                            Field(Of("picks"), "picks", "dropdown", """{"multiple":true}""", Items("a", "b", "c")),
                            Field(Of("yesButtons"), "yesButtons", "yesno", """{"yesLabel":{"":"Sure"}}"""),
                            Field(Of("yesSwitch"), "yesSwitch", "yesno", """{"style":"switch"}""") with { Help = new LocalizedText("Listed in the guide") },
                            Field(Of("nps"), "nps", "scale", """{"from":0,"to":10,"lowLabel":{"":"Not likely"},"highLabel":{"":"Very likely"}}"""),
                            Field(Of("stars"), "stars", "rating", """{"count":5,"symbol":"heart"}"""),
                            Field(Of("grid"), "grid", "grid", "{}", new FieldOptions
                            {
                                Items = [new FieldOption { Value = "1", Label = new LocalizedText("Poor") }, new FieldOption { Value = "2", Label = new LocalizedText("Fair") }, new FieldOption { Value = "3", Label = new LocalizedText("Good") }],
                            }) with
                            {
                                Rows = [new GridRow { Key = RowA, Label = new LocalizedText("Booking") }, new GridRow { Key = RowB, Label = new LocalizedText("The room"), Required = true }],
                            },
                            Field(Of("rank"), "rank", "ranking", """{"top":2}""", Items("x", "y", "z")),
                            Field(Of("box"), "box", "checkbox", """{"text":{"":"Send me <a href=\"/news\">the newsletter</a>"}}"""),
                            Field(Of("consent"), "consent", "consent", """{"text":{"":"I agree to the <a href=\"/privacy\">privacy notice</a>."}}""") with { Required = true },
                            Field(Of("country"), "country", "country", """{"allowed":["DE","CH","AT","FR"],"top":["CH"]}"""),
                        ],
                    },
                ],
            },
        ],
    };

    // A country with its default, the regions of a source and the plugs of its own options below it, the cities below
    // the regions, sizes from a library list, a source that fails, and a country field with a default of its own.
    private static FormDefinition OptionsForm(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage
            {
                Key = K(0x04),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x05),
                        Fields =
                        [
                            Field(Country, "countryOf", "dropdown", "{}", new FieldOptions
                            {
                                Items = [new FieldOption { Value = "ch", Label = new LocalizedText("Switzerland") }, new FieldOption { Value = "de", Label = new LocalizedText("Germany") }],
                            }, rules: false) with { DefaultValue = FieldDefaultValue.Of("ch") },
                            Field(Region, "region", "dropdown", "{}", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"regions"}"""), Parent = Country }, rules: false),
                            Field(Plug, "plug", "radio", "{}", new FieldOptions
                            {
                                Items = [new FieldOption { Value = "j", Label = new LocalizedText("Type J"), ParentValues = ["ch"] }, new FieldOption { Value = "c", Label = new LocalizedText("Type C") }],
                                Parent = Country,
                            }, rules: false),
                            Field(City, "city", "dropdown", "{}", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"cities"}"""), Parent = Region }, rules: false),
                            Field(Sizes, "sizes", "radio", "{}", new FieldOptions { Source = OptionSource.List, OptionList = SizesList }, rules: false),
                            Field(Broken, "broken", "checkboxes", "{}", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"broken"}""") }, rules: false),
                            Field(Home, "home", "country", """{"default":"DE"}""", rules: false),
                            Field(Newsletter, "newsletter", "checkbox", "{}", rules: false),
                        ],
                    },
                ],
            },
        ],
    };

    // Under the asterisk style, with rules: a choice that requires a grid and a text, a text that hides the grid, the
    // grid with a row required by itself, and a list from a source that offers nothing below its parent.
    private static FormDefinition HooksForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Required } },
        Pages =
        [
            new FormPage
            {
                Key = K(0x08),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x09),
                        Fields =
                        [
                            Field(When, "when", "radio", "{}", Items("yes", "no"), rules: false),
                            Field(HideGrid, "hideGrid", "text", "{}", rules: false) with { DefaultValue = FieldDefaultValue.Of("yes") },
                            Field(Rate, "rate", "grid", "{}", Items("good", "poor"), rules: false) with
                            {
                                Rows = [new GridRow { Key = RowA, Label = new LocalizedText("Food"), Required = true }, new GridRow { Key = RowB, Label = new LocalizedText("Music") }],
                                Rules = [RuleOf(K(0x171), RuleEffect.Hide, HideGrid, "yes"), RuleOf(K(0x172), RuleEffect.Require, When, "no")],
                            },
                            Field(Extra, "extra", "text", "{}", rules: false) with { Rules = [RuleOf(K(0x173), RuleEffect.Require, When, "no")] },
                            Field(Room, "room", "dropdown", "{}", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"cities"}"""), Parent = When }, rules: false),
                        ],
                    },
                ],
            },
        ],
    };

    private static FormRule RuleOf(Guid key, RuleEffect effect, Guid field, string value) =>
        new() { Key = key, Effect = effect, Conditions = [new RuleCondition { Field = field, Operator = "equals", Value = new ConditionOperand.Text(value) }] };

    // Under the asterisk style: a grid on a scale with a required row and a row without a label, a required box and a
    // required switch, a parent a rule hides on the first render with the field below it, a radio with an option
    // without a label and the Other choice.
    private static FormDefinition RareForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Required } },
        Pages =
        [
            new FormPage
            {
                Key = K(0x06),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x07),
                        Fields =
                        [
                            Field(ScaleGrid, "scaleGrid", "grid", """{"scale":{"from":0,"to":2}}""", rules: false) with
                            {
                                Rows =
                                [
                                    new GridRow { Key = RowA, Label = new LocalizedText("Booking") },
                                    new GridRow { Key = RowB, Label = new LocalizedText("The room"), Required = true },
                                    new GridRow { Key = RowC },
                                ],
                            },
                            Field(Agree, "agree", "checkbox", "{}", rules: false) with { Required = true },
                            Field(Toggle, "toggle", "yesno", """{"style":"switch"}""", rules: false) with { Required = true },
                            Field(HideParent, "hideParent", "text", "{}", rules: false) with { DefaultValue = FieldDefaultValue.Of("yes") },
                            Field(Parent, "parent", "dropdown", "{}", Items("a", "b"), rules: false) with
                            {
                                DefaultValue = FieldDefaultValue.Of("a"),
                                Rules = [new FormRule { Key = K(0x170), Effect = RuleEffect.Hide, Conditions = [new RuleCondition { Field = HideParent, Operator = "equals", Value = new ConditionOperand.Text("yes") }] }],
                            },
                            Field(Child, "child", "radio", "{}", new FieldOptions
                            {
                                Items = [new FieldOption { Value = "x", Label = new LocalizedText("X"), ParentValues = ["a"] }, new FieldOption { Value = "z", Label = new LocalizedText("Z") }],
                                Parent = Parent,
                            }, rules: false),
                            Field(Way, "way", "radio", """{"other":true}""", new FieldOptions
                            {
                                Items = [new FieldOption { Value = "a", Label = new LocalizedText("A") }, new FieldOption { Value = "b" }],
                            }, rules: false),
                        ],
                    },
                ],
            },
        ],
    };

    private static FormField Field(Guid key, string alias, string type, string settings, FieldOptions? options = null, bool rules = true) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(Words(alias)),
        Settings = settings == "{}" ? null : SettingsObject.Parse(settings),
        Options = options,
        Rules = rules
            ? [new FormRule { Key = K(0x100 + (int)(key.ToByteArray()[15])), Effect = RuleEffect.Hide, Conditions = [new RuleCondition { Field = Trigger, Operator = "equals", Value = new ConditionOperand.Text("yes") }] }]
            : [],
    };

    // Options of these values, each labelled in capitals.
    private static FieldOptions Items(params string[] values) => new() { Items = [.. values.Select(value => new FieldOption { Value = value, Label = new LocalizedText(value.ToUpperInvariant()) })] };

    // "radioList" as "Radio list".
    private static string Words(string alias) =>
        string.Concat(alias.Select((letter, at) => at == 0 ? char.ToUpperInvariant(letter).ToString() : char.IsUpper(letter) ? " " + char.ToLowerInvariant(letter) : letter.ToString()));

    private static Guid Of(string alias) => Choices.Single(choice => choice.Alias == alias).Key;

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000062d{number:x3}");

    private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> Parts(params (string Name, string Text)[] parts) =>
        parts.Select(part => new KeyValuePair<string, IEnumerable<string>>(part.Name, [part.Text]));

    private async Task<IHtmlDocument> ChoicesAsync(string state) => await DocumentAsync("/choices/" + (state == "empty" ? string.Empty : "?state=" + state));

    private async Task<IHtmlDocument> OptionsPageAsync(string country) => await DocumentAsync("/options/" + (country.Length == 0 ? string.Empty : "?country=" + country));

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{path} answers {(int)response.StatusCode}: {html}");
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    // The options endpoint's answer for a field, for the parent's answer when given, as the page that shows it asks.
    private async Task<JsonNode> EndpointAsync(Guid form, Guid field, Guid page, string? parent)
    {
        using var response = await GetAsync($"{Root}/forms/{form}/options/{field}?contentKey={page}" + (parent is null ? string.Empty : "&parent=" + parent));
        Assert.That(Succeeded(response), Is.True, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    // The options endpoint's options as "value: label".
    private static string[] Answered(JsonNode answer) =>
        [.. answer["options"]!.AsArray().Select(option => $"{option!["value"]!.GetValue<string>()}: {option["label"]!.GetValue<string>()}")];

    // A select's options as "value: label".
    private static string[] Options(IElement field) => [.. field.QuerySelectorAll("option").Cast<IHtmlOptionElement>().Select(option => $"{option.Value}: {option.Text}")];

    // A group of choices' options as "value: label".
    private static string[] Labelled(IElement field) => [.. Inputs(field).Select(input => $"{input.Value}: {Label(field, input)}")];

    private static string Label(IElement field, IHtmlInputElement input) => field.QuerySelector($"label[for='{input.Id}']")!.TextContent;

    private static IHtmlInputElement[] Inputs(IElement field) => [.. field.QuerySelectorAll("input[type=radio], input[type=checkbox]").Cast<IHtmlInputElement>()];

    private static string[] Checked(IElement field) => [.. Inputs(field).Where(input => input.IsChecked).Select(input => input.Value)];

    private static string[] Selected(IElement field) => [.. field.QuerySelectorAll("option").Cast<IHtmlOptionElement>().Where(option => option.IsSelected && option.Value.Length > 0).Select(option => option.Value)];

    private static string[] Hidden(IElement field) => [.. field.QuerySelectorAll("input[type=hidden]").Cast<IHtmlInputElement>().Select(input => input.Name + "=" + input.Value)];

    // An image in the media library with its file and size.
    private Guid Image(string name, string file, int width, int height)
    {
        var media = GetRequiredService<IMediaService>();
        var image = media.CreateMedia(name, Constants.System.Root, Constants.Conventions.MediaTypes.Image);
        image.SetValue(Constants.Conventions.Media.File, $$"""{"src":"{{file}}"}""");
        image.SetValue(Constants.Conventions.Media.Width, width);
        image.SetValue(Constants.Conventions.Media.Height, height);
        Assert.That(media.Save(image).Success, Is.True);
        return image.Key;
    }

    private static IElement Field(IElement form, string alias) => form.QuerySelector($"[data-jfb-field][data-jfb-alias={alias}]") ?? throw new AssertionException($"no field {alias}");

    private static IElement Control(IElement form, string alias) => form.QuerySelector($"#jfb-1-{alias}") ?? throw new AssertionException($"no control {alias}");

    private static string Text(IElement element, string selector) => ShownText(element.QuerySelector(selector) ?? throw new AssertionException($"no {selector}"));

    private static Dictionary<string, string> Attributes(IElement element) => element.Attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value);

    // Markup without the line breaks and indentation the views write between elements.
    private static string Compact(string html) => string.Concat(html.Split('\n').Select(line => line.Trim()));
}
