using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The form check's checks of the form's rules (ROADMAP 9.5d, ADR-0179): a require rule that holds while a rule hides
/// its field (its own, its repeatable group's, its group's, its page's), the fields the form hides counting as no
/// answer, and a page no visitor reaches going forward, each found by the rule search over the answers a visitor can
/// give on a site of two languages; none where the answers tried don't stand for all, nor for a field required while
/// shown. What the search tries the answers with: each choice's own options only, the site's languages and day, and a
/// repeatable group's children as several answers.
/// </summary>
[TestFixture]
public sealed class LogicFormCheckTests
{
    private const string Topic = "30000000-0000-4000-8000-000000000001";
    private const string Notes = "30000000-0000-4000-8000-000000000002";
    private const string Guests = "30000000-0000-4000-8000-000000000003";
    private const string Later = "30000000-0000-4000-8000-000000000004";
    private const string Listed = "30000000-0000-4000-8000-000000000005";
    private const string Cascading = "30000000-0000-4000-8000-000000000006";
    private const string Others = "30000000-0000-4000-8000-000000000007";
    private const string Plan = "30000000-0000-4000-8000-000000000008";
    private const string Meals = "30000000-0000-4000-8000-000000000009";
    private const string Party = "30000000-0000-4000-8000-00000000000b";
    private const string Child = "30000000-0000-4000-8000-00000000000c";
    private const string Joined = "30000000-0000-4000-8000-00000000000d";

    private static readonly FieldTypeRegistry Types = TestFieldTypes.Build();

    [Test]
    public async Task ARequireRule_HoldingWhileItsOwnRuleHidesTheField_IsWarnedAtTheRule()
    {
        var definition = Read(Page(1, Group(1, null, TopicField, Field(Notes, "textarea", "notes", Rule("hide", Topic, "equals", "press"), Rule("require", Topic, "isOneOf", new[] { "shipping", "press" })))));

        var issues = await RequiredHidden().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, RequiredHiddenFormCheck.RequiredCanHide, "$.pages[0].groups[0].fields[1].rules[1]"),
        }));
        Assert.That(issues[0].Message, Does.Contain("notes").And.Contain("its own rule"));
    }

    [Test]
    public async Task ARequireRule_HoldingWhileItsGroupsOrPagesRuleHidesTheField_SaysWhose()
    {
        var group = Read(Page(1, Group(1, Rule("hide", Topic, "equals", "press"), Field(Notes, "textarea", "notes", Rule("require", Topic, "equals", "press")))), Page(2, Group(2, null, TopicField)));
        var page = Read(PageWith(1, Rule("show", null, "isChecked", null, context: "memberSignedIn"), Group(1, null, Field(Notes, "textarea", "notes", Rule("require", Topic, "equals", "press")))), Page(2, Group(2, null, TopicField)));

        var byGroup = await RequiredHidden().CheckAsync(group, CancellationToken.None);
        var byPage = await RequiredHidden().CheckAsync(page, CancellationToken.None);

        Assert.That(byGroup.Single().Message, Does.Contain("its group's rule"));
        Assert.That(byPage.Single().Message, Does.Contain("its page's rule"));
    }

    [Test]
    public async Task ARepeatableGroupsRule_HidesItsFields()
    {
        var repeater = Repeater("30000000-0000-4000-8000-00000000000a", "repeater", "people", [Rule("show", Topic, "equals", "shipping")], Field(Notes, "text", "name", Rule("require", Topic, "isNotEmpty")));
        var definition = Read(Page(1, Group(1, null, TopicField, repeater)));

        var issues = await RequiredHidden().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => issue.Path), Is.EqualTo(new[] { "$.pages[0].groups[0].fields[1].fields[0].rules[0]" }));
        Assert.That(issues[0].Message, Does.Contain("its repeatable group's rule"));
    }

    [Test]
    public async Task AFieldRequiredWhileShown_OrRequiredForOtherAnswers_IsNoIssue()
    {
        var shown = Read(Page(1, Group(1, null, TopicField, Field(Notes, "textarea", "notes", required: true, Rule("show", Topic, "equals", "press")))));
        var apart = Read(Page(1, Group(1, null, TopicField, Field(Notes, "textarea", "notes", Rule("hide", Topic, "equals", "press"), Rule("require", Topic, "equals", "shipping")))));

        Assert.That(await RequiredHidden().CheckAsync(shown, CancellationToken.None), Is.Empty);
        Assert.That(await RequiredHidden().CheckAsync(apart, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task APageShownOnlyByAnAnswerGivenAfterIt_IsUnreachable()
    {
        var definition = Read(
            Page(1, Group(1, null, TopicField)),
            PageWith(2, Rule("show", Later, "isNotEmpty"), Group(2, null, Field(Notes, "textarea", "notes"))),
            Page(3, Group(3, null, Field(Later, "text", "later"))));

        var issues = await Unreachable().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, UnreachablePageFormCheck.PageUnreachable, "$.pages[1].rules[0]"),
        }));
        Assert.That(issues[0].Message, Does.Contain("page 2"));
    }

    [Test]
    public async Task APageShownInALanguageTheSiteLacks_IsUnreachable_AndOneAnEarlierAnswerShowsIsNot()
    {
        var french = Read(Page(1, Group(1, null, TopicField)), PageWith(2, Rule("show", null, "equals", "fr-FR", context: "culture"), Group(2, null, Field(Notes, "textarea", "notes"))));
        var reached = Read(Page(1, Group(1, null, TopicField)), PageWith(2, Rule("show", Topic, "equals", "press"), Group(2, null, Field(Notes, "textarea", "notes"))));

        Assert.That((await Unreachable().CheckAsync(french, CancellationToken.None)).Select(issue => issue.Path), Is.EqualTo(new[] { "$.pages[1].rules[0]" }));
        Assert.That(await Unreachable().CheckAsync(reached, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task APage_TheAnswersTriedCannotSpeakFor_IsNotSaidToBeUnreachable()
    {
        // Between 4 and 5 lies 4.5, which no answer tried is.
        var between = new[] { Condition(Guests, "greaterThan", "4"), Condition(Guests, "lessThan", "5") };
        var definition = Read(
            Page(1, Group(1, null, Field(Guests, "number", "guests"))),
            PageWith(2, RuleOf("show", between), Group(2, null, Field(Notes, "textarea", "notes"))));

        Assert.That(await Unreachable().CheckAsync(definition, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task ARequireRule_ConflictingOnlyForAnAnswerTheFormHides_IsNoIssue_AndOneItShowsIs()
    {
        // The guests are asked only about the press, and more than four hide the notes.
        var asked = Field(Guests, "number", "guests", Rule("show", Topic, "equals", "press"));
        var delivery = Read(Page(1, Group(1, null, TopicField, asked, Field(Notes, "textarea", "notes", Rule("hide", Guests, "greaterThan", "4"), Rule("require", Topic, "equals", "delivery")))));
        var press = Read(Page(1, Group(1, null, TopicField, asked, Field(Notes, "textarea", "notes", Rule("hide", Guests, "greaterThan", "4"), Rule("require", Topic, "isOneOf", new[] { "delivery", "press" })))));

        Assert.That(await RequiredHidden().CheckAsync(delivery, CancellationToken.None), Is.Empty, "for delivery the guests are hidden, so they are no answer");
        Assert.That((await RequiredHidden().CheckAsync(press, CancellationToken.None)).Select(issue => issue.Path), Is.EqualTo(new[] { "$.pages[0].groups[0].fields[2].rules[1]" }));
    }

    [Test]
    public async Task TheScope_TriesAChoiceWithItsOwnOptionsOnly_TheSitesLanguages_AndTheSitesDay()
    {
        var definition = Read(Page(1, Group(
            1,
            null,
            TopicField,
            Choice(Listed, "dropdown", "listed", """{"source":"list","list":"50000000-0000-4000-8000-000000000001","items":[{"value":"stale"}]}"""),
            Choice(Cascading, "dropdown", "cascading", $$"""{"items":[{"value":"near"}],"parent":"{{Topic}}"}"""),
            OtherTopic(Others),
            PlanGrid,
            Repeater(Party, "repeater", "people", [], Field(Child, "text", "name")))));

        var (_, scope) = await Scopes("Pacific/Auckland", new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero)).OfAsync(definition, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Options(Topic), Is.EqualTo(new[] { "delivery", "shipping", "press" }));
            Assert.That(scope.Options(Listed), Is.Empty, "a library list's options are not resolved");
            Assert.That(scope.Options(Cascading), Is.Empty, "a cascading list's options hang on its parent's answer");
            Assert.That(scope.Options(Others), Is.Empty, "\"Other\" takes any text");
            Assert.That(scope.Options(Meals), Is.EqualTo(new[] { "yes", "no" }), "a grid's rows take its options");
            Assert.That(scope.Fields[Meals], Is.EqualTo(FieldKind.Choice));
            Assert.That(scope.Cultures, Is.EqualTo(new[] { "en-US", "de-DE" }));
            Assert.That(scope.Today, Is.EqualTo("2026-11-05"), "it is the 5th in Auckland already");
            Assert.That(scope.Several!(Child), Is.True, "a child read from outside its group has every row's answer");
            Assert.That(scope.Several(Topic), Is.False);
            Assert.That(scope.Hidden, Is.Not.Null);
        });
    }

    [Test]
    public async Task TheScope_CountsTheDayInUtc_WhereTheSitesZoneIsUnknown()
    {
        var (_, scope) = await Scopes("Nowhere/Atlantis", new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero)).OfAsync(Read(Page(1, Group(1, null, TopicField))), CancellationToken.None);

        Assert.That(scope.Today, Is.EqualTo("2026-11-04"));
    }

    [Test]
    public async Task APageShownByALaterAnswer_IsReachable_WhereADefaultValueAPrefillOrACalculationGivesIt()
    {
        FormDefinition Form(string later) => Read(
            Page(1, Group(1, null, TopicField)),
            PageWith(2, Rule("show", Later, "isNotEmpty"), Group(2, null, Field(Notes, "textarea", "notes"))),
            Page(3, Group(3, null, later)));
        string Given(string type, string extra) => $$"""{"key":"{{Later}}","type":"{{type}}","alias":"later","label":{"":"later"},{{extra}}}""";

        var blank = await Unreachable().CheckAsync(Form(Field(Later, "text", "later")), CancellationToken.None);

        Assert.That(blank.Single().Message, Does.Contain("page 2").And.Contain("a visitor going forward never reaches it"));
        foreach (var later in new[] { Given("text", "\"defaultValue\":\"Hello\""), Given("text", "\"prefill\":{\"query\":\"ref\"}"), Given("calculation", "\"settings\":{\"formula\":\"1\"}") })
        {
            Assert.That(await Unreachable().CheckAsync(Form(later), CancellationToken.None), Is.Empty, later);
        }
    }

    [Test]
    public async Task APageShownByALaterGridRowOrRepeatableGroupsChild_IsUnreachable()
    {
        var byRow = Read(
            Page(1, Group(1, null, TopicField)),
            PageWith(2, Rule("show", Meals, "isNotEmpty"), Group(2, null, Field(Notes, "textarea", "notes"))),
            Page(3, Group(3, null, PlanGrid)));
        var byChild = Read(
            Page(1, Group(1, null, TopicField)),
            PageWith(2, Rule("show", Child, "isNotEmpty"), Group(2, null, Field(Notes, "textarea", "notes"))),
            Page(3, Group(3, null, Repeater(Party, "repeater", "people", [], Field(Child, "text", "name")))));

        Assert.That((await Unreachable().CheckAsync(byRow, CancellationToken.None)).Select(issue => issue.Path), Is.EqualTo(new[] { "$.pages[1].rules[0]" }));
        Assert.That((await Unreachable().CheckAsync(byChild, CancellationToken.None)).Select(issue => issue.Path), Is.EqualTo(new[] { "$.pages[1].rules[0]" }));
    }

    [Test]
    public async Task APageShownForTwoOptionsOfOneChoice_IsUnreachable_ButNotForARepeatableGroupsChild()
    {
        // Two rows of the group may answer both.
        var both = new[] { Condition(Child, "equals", "delivery"), Condition(Child, "equals", "shipping") };
        var child = Choice(Child, "dropdown", "topic", """{"items":[{"value":"delivery"},{"value":"shipping"}]}""");
        var once = Read(Page(1, Group(1, null, child)), PageWith(2, RuleOf("show", both), Group(2, null, Field(Notes, "textarea", "notes"))));
        var rows = Read(Page(1, Group(1, null, Repeater(Party, "repeater", "people", [], child))), PageWith(2, RuleOf("show", both), Group(2, null, Field(Notes, "textarea", "notes"))));

        Assert.That(await Unreachable().CheckAsync(once, CancellationToken.None), Has.Count.EqualTo(1));
        Assert.That(await Unreachable().CheckAsync(rows, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task APageShownForAnAnswerNoOptionIs_IsUnreachable_ButNotForAChoiceTakingAnyText()
    {
        FormDefinition Form(string topic) => Read(Page(1, Group(1, null, topic)), PageWith(2, Rule("show", Topic, "equals", "elsewhere"), Group(2, null, Field(Notes, "textarea", "notes"))));

        Assert.That(await Unreachable().CheckAsync(Form(TopicField), CancellationToken.None), Has.Count.EqualTo(1));
        Assert.That(await Unreachable().CheckAsync(Form(OtherTopic(Topic)), CancellationToken.None), Is.Empty, "\"Other\" takes any text");
    }

    [Test]
    public async Task ARulesValueNoneOfItsChoicesOwnOptions_IsWarnedAtTheValue_AYesOrNosAnswersAmongThem()
    {
        // 16.6's review: a typed "Yes" for a yes or no question, whose answer is yes, never held.
        var joined = Field(Joined, "yesno", "joined");
        var definition = Read(
            PageWith(1, null, Group(1, null, joined, TopicField, Field(Notes, "textarea", "notes", Rule("show", Joined, "equals", "Yes"), Rule("require", Joined, "notEquals", "no")))),
            PageWith(2, Rule("show", Topic, "isOneOf", new[] { "press", "Press" }), Group(2, Rule("hide", Topic, "isNoneOf", new[] { "delivery" }), Field(Later, "text", "later", Rule("show", Topic, "equals", "shipping")))));

        var issues = await new RuleValueOptionFormCheck().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, RuleValueOptionFormCheck.RuleValueNotOption, "$.pages[0].groups[0].fields[2].rules[0].conditions[0].value"),
            (FormCheckSeverity.Warning, RuleValueOptionFormCheck.RuleValueNotOption, "$.pages[1].rules[0].conditions[0].value[1]"),
        }));
        Assert.That(issues[0].Message, Does.Contain("'Yes'"));
    }

    [Test]
    public async Task ARulesValue_IsHeldOnlyToOptionsTheFormOwns_AndOnlyWhereItIsCompared()
    {
        var definition = DefinitionFixtures.Read($$"""
            {"schemaVersion":1,"key":"f0000000-0000-4000-8000-000000000001",
             "pages":[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[
               {{OtherTopic(Others)}},
               {{Choice(Listed, "dropdown", "listed", """{"source":"list","list":"50000000-0000-4000-8000-000000000001"}""")}},
               {{Field(Joined, "yesno", "joined")}},
               {{Field(Notes, "textarea", "notes", Rule("show", Others, "equals", "elsewhere"), Rule("hide", Listed, "equals", "anything"), Rule("require", Joined, "isNotEmpty"))}}]}]}],
             "actions":[{"key":"60000000-0000-4000-8000-000000000001","type":"email","name":"Tell","rules":[{{Rule("run", Joined, "equals", "Ja")}}]}],
             "outcomes":[{"key":"70000000-0000-4000-8000-000000000001","kind":"message","rules":[{{Rule("use", Joined, "equals", "yes")}}]}]}
            """);

        var issues = await new RuleValueOptionFormCheck().CheckAsync(definition, CancellationToken.None);

        Assert.That(issues.Select(issue => issue.Path), Is.EqualTo(new[] { "$.actions[0].rules[0].conditions[0].value" }), "an action's rule; not a choice with Other, a library list, an operator without a value, or yes");
    }

    [Test]
    public async Task APageShownForAYesOrNoAnswerItDoesNotHave_IsUnreachable_AndForYes_IsNot()
    {
        FormDefinition Form(string value) => Read(Page(1, Group(1, null, Field(Joined, "yesno", "joined"))), PageWith(2, Rule("show", Joined, "equals", value), Group(2, null, Field(Notes, "textarea", "notes"))));

        Assert.That(await Unreachable().CheckAsync(Form("Yes"), CancellationToken.None), Has.Count.EqualTo(1), "the search tries a yes or no's two answers");
        Assert.That(await Unreachable().CheckAsync(Form("yes"), CancellationToken.None), Is.Empty);
    }

    private static string TopicField => Choice(Topic, "dropdown", "topic", """{"items":[{"value":"delivery"},{"value":"shipping"},{"value":"press"}]}""");

    // A choice whose "Other" takes any text.
    private static string OtherTopic(string key) =>
        $$$"""{"key":"{{{key}}}","type":"dropdown","alias":"other{{{key[^1]}}}","label":{"":"topic"},"options":{"items":[{"value":"delivery"},{"value":"shipping"},{"value":"press"}]},"settings":{"other":true}}""";

    private static string PlanGrid =>
        $$$"""{"key":"{{{Plan}}}","type":"grid","alias":"plan","label":{"":"plan"},"options":{"items":[{"value":"yes"},{"value":"no"}]},"rows":[{"key":"{{{Meals}}}","label":{"":"meals"}}]}""";

    private static RequiredHiddenFormCheck RequiredHidden() => new(Scopes());

    private static UnreachablePageFormCheck Unreachable() => new(Scopes());

    // A site of English and German, by default in UTC on 2026-11-04.
    private static RuleSearchScopes Scopes(string zone = GeneralSettings.DefaultTimeZone, DateTimeOffset? now = null)
    {
        IEnumerable<ILanguage> site = [Language("en-US"), Language("de-DE")];
        var languages = Substitute.For<ILanguageService>();
        languages.GetAllAsync().Returns(Task.FromResult(site));
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<GeneralSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SettingsSnapshot<GeneralSettings>(new GeneralSettings { TimeZone = zone }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        return new RuleSearchScopes(Types, languages, settings, new TestTimeProvider(now ?? new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)));
    }

    private static ILanguage Language(string culture)
    {
        var language = Substitute.For<ILanguage>();
        language.IsoCode.Returns(culture);
        return language;
    }

    private static FormDefinition Read(params string[] pages) =>
        DefinitionFixtures.Read("{\"schemaVersion\":1,\"key\":\"f0000000-0000-4000-8000-000000000001\",\"pages\":[" + string.Join(',', pages) + "]}");

    private static string Page(int n, params string[] groups) => PageWith(n, null, groups);

    private static string PageWith(int n, string? rule, params string[] groups) =>
        $"{{\"key\":\"10000000-0000-4000-8000-00000000000{n}\"{Rules(rule)},\"groups\":[{string.Join(',', groups)}]}}";

    private static string Group(int n, string? rule, params string[] fields) =>
        $"{{\"key\":\"20000000-0000-4000-8000-00000000000{n}\"{Rules(rule)},\"fields\":[{string.Join(',', fields)}]}}";

    private static string Field(string key, string type, string alias, params string[] rules) => Field(key, type, alias, false, rules);

    private static string Field(string key, string type, string alias, bool required, params string[] rules) =>
        $"{{\"key\":\"{key}\",\"type\":\"{type}\",\"alias\":\"{alias}\",\"label\":{{\"\":\"{alias}\"}}{(required ? ",\"required\":true" : string.Empty)}{(rules.Length > 0 ? ",\"rules\":[" + string.Join(',', rules) + "]" : string.Empty)}}}";

    private static string Choice(string key, string type, string alias, string options) =>
        $"{{\"key\":\"{key}\",\"type\":\"{type}\",\"alias\":\"{alias}\",\"label\":{{\"\":\"{alias}\"}},\"options\":{options}}}";

    private static string Repeater(string key, string type, string alias, string[] rules, params string[] children) =>
        $"{{\"key\":\"{key}\",\"type\":\"{type}\",\"alias\":\"{alias}\",\"label\":{{\"\":\"{alias}\"}},\"rules\":[{string.Join(',', rules)}],\"fields\":[{string.Join(',', children)}]}}";

    private static string Rules(string? rule) => rule is null ? string.Empty : $",\"rules\":[{rule}]";

    private static string Rule(string effect, string? field, string op, object? value = null, string? context = null) =>
        RuleOf(effect, [Condition(field, op, value, context)]);

    private static string RuleOf(string effect, IEnumerable<string> conditions) =>
        $"{{\"key\":\"40000000-0000-4000-8000-{Key()}\",\"effect\":\"{effect}\",\"conditions\":[{string.Join(',', conditions)}]}}";

    // A rule's own key, which no check reads.
    private static string Key() => Guid.NewGuid().ToString("N")[..12];

    private static string Condition(string? field, string op, object? value = null, string? context = null)
    {
        var subject = field is not null ? $"\"field\":\"{field}\"" : $"\"context\":\"{context}\"";
        var operand = value switch
        {
            null => string.Empty,
            string text => $",\"value\":\"{text}\"",
            string[] list => $",\"value\":[{string.Join(',', list.Select(item => $"\"{item}\""))}]",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        return $"{{{subject},\"op\":\"{op}\"{operand}}}";
    }
}
