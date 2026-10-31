using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A repeatable group's rows on real pages (ROADMAP 6.2f, ADR-0015, ADR-0098): a first render's least rows with their
/// defaults, a post's rows with their answers, their rows' rules, calculations and options and their errors, a group
/// with more rows than its most and a post with more than any group may have, a hidden group, and a group without
/// labels on a German page under the optional style.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingRowsTests : RenderingTestBase
{
    private static readonly Guid Trigger = K(0x01);
    private static readonly Guid Plan = K(0x05);
    private static readonly Guid Guests = K(0x10);
    private static readonly Guid GuestName = K(0x11);
    private static readonly Guid Age = K(0x12);
    private static readonly Guid Nights = K(0x13);
    private static readonly Guid Cost = K(0x14);
    private static readonly Guid Diet = K(0x15);
    private static readonly Guid Meal = K(0x16);
    private static readonly Guid Room = K(0x17);
    private static readonly Guid Notes = K(0x20);
    private static readonly Guid Party = K(0x30);
    private static readonly Guid Who = K(0x31);

    /// <summary>The state the view <c>rows</c> leaves for its form: none, or a post's rows, too many rows, far too many, the rules' trigger with rows or too many, or no rows.</summary>
    /// <param name="state">The state's name: posted, over, flood, hidden, hiddenover or bare.</param>
    internal static FormPostState? State(string state) => state switch
    {
        "posted" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers> { [Plan] = new FieldAnswers(["stay"]) },
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>>
            {
                [Guests] =
                [
                    Row((GuestName, "Ada"), (Age, "adult"), (Nights, "2"), (Diet, "Vegan"), (Plan, "day")),
                    Row((GuestName, "Sam"), (Age, "child"), (Nights, "1"), (Diet, "None")),
                    new FormPostRow { Answers = new Dictionary<Guid, FieldAnswers> { [Age] = new FieldAnswers(["adult"]) }, Errors = new Dictionary<Guid, FormPostError> { [GuestName] = new("required") } },
                ],
                [Party] = [Row((Who, "Lin"))],
            },
        },
        "over" => new FormPostState
        {
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>>
            {
                [Guests] =
                [
                    .. Enumerable.Range(1, 4).Select(n => Row((GuestName, "Guest " + n), (Age, "child"), (Nights, "1")) with
                    {
                        Errors = n == 1 ? new Dictionary<Guid, FormPostError> { [Nights] = new("required") } : new Dictionary<Guid, FormPostError>(),
                    }),
                ],
            },
            Errors = new Dictionary<Guid, FormPostError> { [Guests] = new("rowCount") },
        },
        "flood" => new FormPostState
        {
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>> { [Guests] = [.. Enumerable.Range(1, RepeaterFieldType.MostRows + 1).Select(n => Row((GuestName, "Guest " + n)))] },
        },
        "hidden" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers> { [Trigger] = new FieldAnswers(["yes"]) },
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>> { [Guests] = [Row((GuestName, "Ada")), Row((GuestName, "Sam"))] },
        },
        "hiddenover" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers> { [Trigger] = new FieldAnswers(["yes"]) },
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>> { [Guests] = [.. Enumerable.Range(1, 4).Select(n => Row((GuestName, "Guest " + n)))] },
        },
        "bare" => new FormPostState { Answers = new Dictionary<Guid, FieldAnswers> { [Trigger] = new FieldAnswers(["no"]) } },
        _ => null,
    };

    /// <summary>The state the view <c>rowsde</c> leaves for its form: none, or a post with one row.</summary>
    /// <param name="state">The state's name: posted, or anything else for none.</param>
    internal static FormPostState? GermanState(string state) => state == "posted"
        ? new FormPostState { Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>> { [Party] = [Row((Who, "Ada"))] } }
        : null;

    [Test]
    public async Task ARepeatableGroup_OnAFirstRender_ShowsItsLeastRows_NumberedAfterItsRowLabel_WithTheirFieldsNamedByTheirPath()
    {
        _ = await PublishFormAsync("renderingRows", RowsForm);
        _ = await PagesAsync("rows");
        var form = (await DocumentAsync("/rows/")).QuerySelector("form.jfb-form")!;

        var guests = Field(form, "guests");
        var rows = guests.QuerySelectorAll(".jfb-row");
        var row = rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(guests.GetAttribute("class"), Is.EqualTo("jfb-field jfb-field-repeater"));
            Assert.That(guests.GetAttribute("data-jfb-type"), Is.EqualTo("repeater"));
            Assert.That(guests.QuerySelector(":scope > fieldset")!.GetAttribute("aria-describedby"), Is.EqualTo("jfb-1-guests-help"));
            Assert.That(guests.QuerySelector(":scope > fieldset > legend")!.OuterHtml, Is.EqualTo("""<legend class="jfb-label">Guests</legend>"""), "its least rows are there, and their fields say what each needs");
            Assert.That(Text(guests, "#jfb-1-guests-help"), Is.EqualTo("Up to three guests"));
            Assert.That(guests.QuerySelector(".jfb-repeater")!.GetAttribute("id"), Is.EqualTo("jfb-1-guests"));
            Assert.That(guests.QuerySelector(".jfb-repeater")!.GetAttribute("tabindex"), Is.EqualTo("-1"), "the error summary's link moves the focus to the rows");
            Assert.That(Attributes(row), Is.EqualTo(new Dictionary<string, string> { ["class"] = "jfb-row", ["id"] = "jfb-1-guests-0", ["data-jfb-row"] = "0" }));
            Assert.That(row.QuerySelector(":scope > legend")!.TextContent, Is.EqualTo("Guest 1"), "the row label and the row's number");
            Assert.That(row.QuerySelector(":scope > button"), Is.Null, "no Remove while the group has only its least rows");
            Assert.That(row.QuerySelector(":scope > div")!.GetAttribute("class"), Is.EqualTo("jfb-fields jfb-columns-2"), "the columns of the group the field is in");
            Assert.That(row.QuerySelectorAll("[data-jfb-field]").Select(child => (child.GetAttribute("data-jfb-field"), child.GetAttribute("data-jfb-alias"))), Is.EqualTo(new[]
            {
                (GuestName.ToString("D"), "guests[0].name"),
                (Age.ToString("D"), "guests[0].age"),
                (Nights.ToString("D"), "guests[0].nights"),
                (Cost.ToString("D"), "guests[0].cost"),
                (Diet.ToString("D"), "guests[0].diet"),
                (Meal.ToString("D"), "guests[0].meal"),
                (Room.ToString("D"), "guests[0].room"),
            }), "each row's field carries the child's key and its path");
            Assert.That(row.QuerySelector("[data-jfb-alias='guests[0].name'] label")!.OuterHtml, Is.EqualTo("""<label class="jfb-label" for="jfb-1-guests-0-name">Name <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required">*</span></label>"""));
            Assert.That(form.QuerySelector("#jfb-1-guests-0-name")!.OuterHtml, Is.EqualTo("""<input class="jfb-control" type="text" id="jfb-1-guests-0-name" name="guests[0].name" required="required" data-jfb-requires="">"""));
            Assert.That(form.QuerySelector("#jfb-1-guests-0-age")!.GetAttribute("name"), Is.EqualTo("guests[0].age"));
            Assert.That(form.QuerySelector("#jfb-1-guests-0-cost")!.TextContent, Is.EqualTo("EUR 0.00"), "a row's calculation over its row's answers, an empty number counting as zero");
            Assert.That(form.QuerySelector("#jfb-1-guests-0-diet")!.GetAttribute("value"), Is.EqualTo("Any"), "a least row's field starts with its default");
            Assert.That(guests.QuerySelector(".jfb-repeater > button")!.OuterHtml, Is.EqualTo("""<button type="submit" class="jfb-button" name="jfb-nav" value="add:guests" formaction="/rows/#jfb-1-guests-1"><span class="jfb-icon jfb-icon-add" aria-hidden="true"></span>Add a guest</button>"""), "Add opens the page at the row it adds");
            Assert.That(Field(form, "party").QuerySelectorAll(".jfb-row"), Is.Empty, "a group without a least starts with its Add button alone");
            Assert.That(Text(Field(form, "party"), ".jfb-repeater > button"), Is.EqualTo("Add a row"), "the package's words without an add label");
            Assert.That(form.QuerySelector("button[tabindex='-1'][aria-hidden=true]")!.GetAttribute("value"), Is.EqualTo("submit"), "Enter in a field sends the form, not the first Add or Remove");
            Assert.That(form.QuerySelector("button[tabindex='-1']"), Is.EqualTo(form.QuerySelector("button")), "the default button comes first");
        });
    }

    [Test]
    public async Task APostsRows_ShowTheirAnswers_FollowTheirRowsRulesCalculationsAndOptions_AndCarryTheirErrors()
    {
        _ = await PublishFormAsync("renderingRows", RowsForm);
        _ = await PagesAsync("rows");
        var form = (await DocumentAsync("/rows/?state=posted")).QuerySelector("form.jfb-form")!;

        var rows = Field(form, "guests").QuerySelectorAll(".jfb-row");
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.QuerySelector(":scope > legend")!.TextContent), Is.EqualTo(new[] { "Guest 1", "Guest 2", "Guest 3" }));
            Assert.That(rows.Select(row => row.QuerySelector(":scope > button")!.OuterHtml), Is.EqualTo(new[] { 0, 1, 2 }.Select(row =>
                $"""<button type="submit" class="jfb-button jfb-button-link" name="jfb-nav" value="remove:guests:{row}" formaction="/rows/#jfb-1-guests">Leave out<span class="jfb-visually-hidden"> Guest {row + 1}</span></button>""")), "Remove names its row, and opens the page at the group");
            Assert.That(Field(form, "guests").QuerySelector(".jfb-repeater > button"), Is.Null, "no Add once the group has its most rows");
            Assert.That(form.QuerySelector("#jfb-1-guests-0-name")!.GetAttribute("value"), Is.EqualTo("Ada"));
            Assert.That(form.QuerySelector("#jfb-1-guests-1-name")!.GetAttribute("value"), Is.EqualTo("Sam"));
            Assert.That(form.QuerySelector("#jfb-1-guests-1-age option[selected]")!.GetAttribute("value"), Is.EqualTo("child"));
            Assert.That(rows.Select(row => row.QuerySelector("output")!.TextContent), Is.EqualTo(new[] { "EUR 50.00", "EUR 25.00", "EUR 0.00" }), "each row's calculation over its own row");
            Assert.That(Attributes(rows[1].QuerySelector("[data-jfb-alias='guests[1].diet']")!), Does.ContainKey("hidden").And.ContainKey("data-jfb-hidden"), "a rule over its row's answer hides it in that row");
            Assert.That(form.QuerySelector("#jfb-1-guests-1-diet")!.HasAttribute("disabled"), Is.True, "so it is never posted");
            Assert.That(rows[0].QuerySelector("[data-jfb-alias='guests[0].diet']")!.HasAttribute("hidden"), Is.False, "and only there");
            Assert.That(rows.Select(row => row.QuerySelector("input[name$='.nights']")!.HasAttribute("required")), Is.EqualTo(new[] { true, false, true }), "a rule over its row's answer requires a field in that row");
            Assert.That(rows.Select(row => string.Join(",", Offered(row, "meal"))), Is.EqualTo(new[] { "full,none", "kids,none", "full,none" }), "a parent in the row filters its row's options");
            Assert.That(rows.Select(row => string.Join(",", Offered(row, "room"))), Is.EqualTo(new[] { "bunk", "bunk", "bunk" }), "a parent outside the group filters every row's options, an answer for it in a row changing nothing");
            Assert.That(form.QuerySelector("#jfb-1-guests-2-name")!.GetAttribute("aria-invalid"), Is.EqualTo("true"));
            Assert.That(form.QuerySelector("#jfb-1-guests-2-name")!.GetAttribute("aria-describedby"), Is.EqualTo("jfb-1-guests-2-name-error"));
            Assert.That(form.QuerySelector("#jfb-1-guests-2-name-error")!.OuterHtml, Is.EqualTo("""<p class="jfb-error" id="jfb-1-guests-2-name-error" data-jfb-error="">Enter an answer</p>"""));
            Assert.That(form.QuerySelectorAll("[data-jfb-error]"), Has.Length.EqualTo(1), "the error is its row's");
            Assert.That(Field(form, "party").QuerySelector(".jfb-row > div")!.GetAttribute("class"), Is.EqualTo("jfb-fields jfb-columns-3"), "a group's rows take the columns of its own group");
            Assert.That(form.QuerySelectorAll(".jfb-error-summary a").Select(link => (link.GetAttribute("href"), link.TextContent)), Is.EqualTo(new[] { ("#jfb-1-guests-2-name", "Name (Guest 3): Enter an answer") }), "the default after its label and its row's legend");
            Assert.That(Text(Field(form, "notes"), "#jfb-1-notes-help"), Is.EqualTo("Coming: Ada, Sam; diets: Vegan"), "a recalled child lists its rows' answers, but not where its row's rule hides it");
        });
    }

    [Test]
    public async Task AGroupWithMoreRowsThanItsMost_ShowsItsOwnErrorBelowItsRows_AndItsRulesSeeNoRows_AndNoPostShowsMoreThanAnyGroupMayHave()
    {
        _ = await PublishFormAsync("renderingRows", RowsForm);
        _ = await PagesAsync("rows");
        var form = (await DocumentAsync("/rows/?state=over")).QuerySelector("form.jfb-form")!;
        var flood = (await DocumentAsync("/rows/?state=flood")).QuerySelector("form.jfb-form")!;

        var guests = Field(form, "guests");
        var fieldset = guests.QuerySelector(":scope > fieldset")!;
        Assert.Multiple(() =>
        {
            Assert.That(guests.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(4), "the rows the visitor posted, to remove one from");
            Assert.That(fieldset.HasAttribute("aria-invalid"), Is.False, "a fieldset's role takes no invalid state (6.8c); it names its error");
            Assert.That(fieldset.GetAttribute("aria-describedby"), Is.EqualTo("jfb-1-guests-help jfb-1-guests-error"));
            Assert.That(fieldset.Children[fieldset.Children.Length - 1].OuterHtml, Is.EqualTo("""<p class="jfb-error" id="jfb-1-guests-error" data-jfb-error="">Remove rows to keep at most 3</p>"""), "below the rows");
            Assert.That(form.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-guests-0-nights", "#jfb-1-guests" }), "a row's error first, then the group's own to the rows' container, in the page's order");
            Assert.That(form.QuerySelectorAll("output").Select(output => output.TextContent), Is.All.Empty, "the pipeline reads no rows past the group's most, so the rules work out nothing");
            Assert.That(form.QuerySelectorAll("[data-jfb-alias$='.diet'][hidden]"), Is.Empty, "nor hide a child in them");
            Assert.That(Field(flood, "guests").QuerySelectorAll(".jfb-row").Select(row => row.Id).LastOrDefault(), Is.EqualTo("jfb-1-guests-" + (RepeaterFieldType.MostRows - 1)), "at most as many rows as any group may have");
            Assert.That(Field(flood, "guests").QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(RepeaterFieldType.MostRows));
        });
    }

    [Test]
    public async Task AHiddenGroup_DisablesEveryRowsControlsAndItsButtons_ItsRowsPastItsMostToo()
    {
        _ = await PublishFormAsync("renderingRows", RowsForm);
        _ = await PagesAsync("rows");
        var form = (await DocumentAsync("/rows/?state=hidden")).QuerySelector("form.jfb-form")!;
        var over = Field((await DocumentAsync("/rows/?state=hiddenover")).QuerySelector("form.jfb-form")!, "guests");

        var guests = Field(form, "guests");
        Assert.Multiple(() =>
        {
            Assert.That(Attributes(guests), Does.ContainKey("hidden").And.ContainKey("data-jfb-hidden"));
            Assert.That(guests.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(2));
            Assert.That(guests.QuerySelectorAll("input, select, button").Select(control => control.GetAttribute("name")), Is.EqualTo(new[]
            {
                "jfb-rows-guests",
                "jfb-nav", "guests[0].name", "guests[0].age", "guests[0].nights", "guests[0].diet", "guests[0].meal", "guests[0].room",
                "jfb-nav", "guests[1].name", "guests[1].age", "guests[1].nights", "guests[1].diet", "guests[1].meal", "guests[1].room",
                "jfb-nav",
            }), "the row count, each row's Remove and fields, and Add");
            Assert.That(guests.QuerySelectorAll("input, select, button").Select(control => control.HasAttribute("disabled")), Is.All.True, "so none is posted or pressed");
            Assert.That(guests.QuerySelectorAll(".jfb-row [data-jfb-field]").Select(child => child.HasAttribute("hidden")), Is.All.True, "the rules hide what a hidden group holds");
            Assert.That(over.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(4));
            Assert.That(over.QuerySelectorAll("input, select, button").Select(control => control.HasAttribute("disabled")), Is.All.True, "rows past the most have no row state, and the group hides them all the same");
            Assert.That(over.QuerySelectorAll(".jfb-row [data-jfb-field]").Select(child => child.HasAttribute("hidden")), Is.All.True);
        });
    }

    [Test]
    public async Task APostWithoutAGroupsRows_AsFromAGroupThatWasHidden_ShowsItsLeastRows_AsAFirstRenderDoes()
    {
        _ = await PublishFormAsync("renderingRows", RowsForm);
        _ = await PagesAsync("rows");
        var form = (await DocumentAsync("/rows/?state=bare")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(Field(form, "guests").QuerySelectorAll(".jfb-row").Select(row => row.Id), Is.EqualTo(new[] { "jfb-1-guests-0" }), "its least, though the post held none");
            Assert.That(form.QuerySelector("#jfb-1-guests-0-name")!.HasAttribute("value"), Is.False, "each starting as a first render does");
            Assert.That(form.QuerySelector("#jfb-1-guests-0-diet")!.GetAttribute("value"), Is.EqualTo("Any"));
            Assert.That(form.QuerySelector("#jfb-1-guests-0-cost")!.TextContent, Is.EqualTo("EUR 0.00"), "and its rules over it");
            Assert.That(Field(form, "party").QuerySelectorAll(".jfb-row"), Is.Empty, "a group without a least shows none");
        });
    }

    [Test]
    public async Task AGroupWithoutLabels_OnAGermanPage_SaysThePackagesWords_AndIsOptionalWhileItNeedsNoRow()
    {
        _ = await PublishFormAsync("renderingRowsDe", definition => definition with
        {
            Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Optional } },
        }, Repeater(Party, "party", """{"maxRows":2}""", Field(Who, "who", "text")), Repeater(Guests, "guests", """{"minRows":1}""", Field(GuestName, "name", "text")));
        _ = await CulturePagesAsync("de-DE", "German", "rowsde");
        var first = (await DocumentAsync("/rowsde/")).QuerySelector("form.jfb-form")!;
        var posted = (await DocumentAsync("/rowsde/?state=posted")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(Field(first, "party").QuerySelector(":scope > fieldset > legend")!.TextContent, Is.EqualTo("Party (optional)"), "it needs no row");
            Assert.That(Field(first, "guests").QuerySelector(":scope > fieldset > legend")!.TextContent, Is.EqualTo("Guests"), "it needs one, which is there");
            Assert.That(Text(Field(first, "party"), ".jfb-repeater > button"), Is.EqualTo("Zeile hinzuf" + (char)0xFC + "gen"));
            Assert.That(Field(posted, "party").QuerySelector(".jfb-row > legend")!.TextContent, Is.EqualTo("Zeile 1"));
            Assert.That(Field(posted, "party").QuerySelector(".jfb-row > button")!.TextContent, Is.EqualTo("Entfernen Zeile 1"), "Remove and, unseen, its row");
            Assert.That(Field(posted, "party").QuerySelector(".jfb-repeater > button")!.TextContent, Is.EqualTo("Zeile hinzuf" + (char)0xFC + "gen"), "below its most");
            Assert.That(posted.QuerySelector("#jfb-1-party-0-who")!.GetAttribute("value"), Is.EqualTo("Ada"));
        });
    }

    // A trigger, a plan, guests in rows (a required name, an age, nights required for an adult and their cost, a diet
    // hidden for a child, a meal by age and a room by plan), notes that recall the guests' names and diets, and a party
    // without labels in a group of three columns; the asterisk style.
    private static FormDefinition RowsForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Required } },
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
                        Columns = 2,
                        Fields =
                        [
                            Field(Trigger, "hide", "text"),
                            Field(Plan, "plan", "dropdown") with { Options = new FieldOptions { Items = [Option("day", "Day"), Option("stay", "Stay")] } },
                            Repeater(
                                Guests,
                                "guests",
                                """{"minRows":1,"maxRows":3,"rowLabel":{"":"Guest"},"addLabel":{"":"Add a guest"},"removeLabel":{"":"Leave out"}}""",
                                Field(GuestName, "name", "text") with { Required = true },
                                Field(Age, "age", "dropdown") with { Options = new FieldOptions { Items = [Option("child", "Child"), Option("adult", "Adult")] } },
                                Field(Nights, "nights", "number") with { Rules = [When(K(0x113), RuleEffect.Require, Age, "adult")] },
                                Field(Cost, "cost", "calculation", """{"formula":"nights * 25","format":{"decimals":2,"prefix":{"":"EUR"}}}"""),
                                Field(Diet, "diet", "text") with { DefaultValue = FieldDefaultValue.Of("Any"), Rules = [When(K(0x115), RuleEffect.Hide, Age, "child")] },
                                Field(Meal, "meal", "dropdown") with
                                {
                                    Options = new FieldOptions { Parent = Age, Items = [Option("kids", "Kids' plate", "child"), Option("full", "Full plate", "adult"), Option("none", "Nothing")] },
                                },
                                Field(Room, "room", "dropdown") with
                                {
                                    Options = new FieldOptions { Parent = Plan, Items = [Option("bunk", "Bunk", "stay"), Option("tent", "Tent", "day")] },
                                }) with
                            {
                                Help = new LocalizedText("Up to three guests"),
                                Rules = [When(K(0x110), RuleEffect.Hide, Trigger, "yes")],
                            },
                            Field(Notes, "notes", "text") with { Help = new LocalizedText("Coming: {{ field.guests.name }}; diets: {{ field.guests.diet }}") },
                        ],
                    },
                    new FormGroup
                    {
                        Key = K(0x04),
                        Columns = 3,
                        Fields = [Repeater(Party, "party", """{"maxRows":2}""", Field(Who, "who", "text"))],
                    },
                ],
            },
        ],
    };

    private static FormField Repeater(Guid key, string alias, string settings, params FormField[] children) =>
        Field(key, alias, "repeater", settings) with { Fields = children };

    private static FormField Field(Guid key, string alias, string type, string settings = "{}") => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(char.ToUpperInvariant(alias[0]) + alias[1..]),
        Settings = settings == "{}" ? null : SettingsObject.Parse(settings),
    };

    // An option, below the parent's answers it shows for.
    private static FieldOption Option(string value, string label, params string[] parents) =>
        new() { Value = value, Label = new LocalizedText(label), ParentValues = parents };

    // A rule with its effect while a field answers a text.
    private static FormRule When(Guid key, RuleEffect effect, Guid field, string text) => new()
    {
        Key = key,
        Effect = effect,
        Conditions = [new RuleCondition { Field = field, Operator = "equals", Value = new ConditionOperand.Text(text) }],
    };

    private static FormPostRow Row(params (Guid Field, string Text)[] answers) =>
        new() { Answers = answers.ToDictionary(answer => answer.Field, answer => new FieldAnswers([answer.Text])) };

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{path} answers {(int)response.StatusCode}: {html}");
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000062f{number:x3}");

    private static IElement Field(IElement form, string alias) => form.QuerySelector($"[data-jfb-field][data-jfb-alias='{alias}']") ?? throw new AssertionException($"no field {alias}");

    // The options a row's dropdown offers, without its empty choice.
    private static IEnumerable<string> Offered(IElement row, string alias) =>
        row.QuerySelectorAll($"select[name$='.{alias}'] option").Select(option => option.GetAttribute("value")!).Where(value => value.Length > 0);

    private static string Text(IElement element, string selector) => (element.QuerySelector(selector) ?? throw new AssertionException($"no {selector}")).TextContent;

    private static Dictionary<string, string> Attributes(IElement element) => element.Attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value);
}
