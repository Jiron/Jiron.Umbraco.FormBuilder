using System.Globalization;
using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The server's own errors, which the runtime's checks, messages and their display are held to (ROADMAP 6.4d and 6.4e,
/// ADR-0112, ADR-0113): a form with a field of every kind the browser checks, in the default theme and, marked with
/// asterisks, in <c>bootstrap5</c>, is posted without JavaScript, as a browser posts it, in English and in German, and
/// each page the server renders again with its errors is saved with the page as it was first rendered, the form element
/// alone (its route token and honeypot left out, its key fixed), with the clock it used, in
/// <c>tests/fixtures/rendering/errors-&lt;language&gt;.json</c>. <c>packages/runtime</c>'s
/// <c>checks.fixture.test.ts</c> loads each page, checks it as the runtime does and expects the server's errors and
/// summary links, and shows them on the page as the server wrote them. After a deliberate change, run the fixture with
/// <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the files and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ErrorFixtureTests : RenderingTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid ChecksKey = Guid.Parse("30000000-0000-4000-8000-0000000000e1");
    private static readonly Guid PagedKey = Guid.Parse("30000000-0000-4000-8000-0000000000e2");
    private static readonly Guid BootstrapKey = Guid.Parse("30000000-0000-4000-8000-0000000000e3");

    private static readonly Guid Name = K(0x10);
    private static readonly Guid Email = K(0x12);
    private static readonly Guid Topic = K(0x1a);
    private static readonly Guid Company = K(0x22);
    private static readonly Guid Second = K(0x31);

    [TestCase("en")]
    [TestCase("de")]
    public async Task TheServersErrorsAfterPostsWithoutJavaScript_AreTheFixtureTheRuntimeIsHeldTo(string language)
    {
        GetRequiredService<TestClock>().SetNow(Now);
        var checks = await PublishFormAsync("renderingErrors", Checks);
        var paged = await PublishFormAsync("renderingErrorsPaged", Paged);
        var bootstrap = await PublishFormAsync("renderingErrorsBootstrap", definition => Checks(definition) with { Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { Theme = "bootstrap5", RequiredMarker = RequiredMarker.Required } } });
        string[] pages = ["errors", "errors-paged", "errors-bootstrap"];
        _ = language == "de" ? await CulturePagesAsync("de-DE", "German", pages) : await PagesAsync(pages);

        var cases = new JsonArray();
        foreach (var (name, page, nav, answers) in Cases())
        {
            var form = await FormAtAsync(page);
            var first = Normalised(form, (checks, ChecksKey), (paged, PagedKey), (bootstrap, BootstrapKey));
            foreach (var (field, values) in answers)
            {
                Fill(form, field, values);
            }

            using var response = await PostAsync(form, nav, page);
            var html = await response.Content.ReadAsStringAsync();
            Assert.That(Succeeded(response), Is.True, $"{name}: {(int)response.StatusCode} {html}");
            var shown = (await new HtmlParser().ParseDocumentAsync(html)).QuerySelector("form.jfb-form") ?? throw new AssertionException($"{name}: no form in {html}");
            Assert.That(shown.QuerySelectorAll("[data-jfb-summary]"), Is.Not.Empty, $"{name}: the post was refused with its errors");
            cases.Add(new JsonObject
            {
                ["name"] = name,
                ["scope"] = nav == FormPostFields.Next ? "page" : "send",
                ["now"] = Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["first"] = first,
                ["html"] = Normalised(shown, (checks, ChecksKey), (paged, PagedKey), (bootstrap, BootstrapKey)),
            });
        }

        await MatchFixtureAsync($"errors-{language}.json", cases, "the server's errors changed");
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    // The posts: every required field left empty with one answer given, so the post is not empty; answers each check
    // refuses; nothing at all; Next on the first page of two with its errors; and the first two in bootstrap5.
    private static IEnumerable<(string Name, string Path, string Nav, (string Field, string[] Values)[] Answers)> Cases()
    {
        (string Field, string[] Values)[] required = [("promo", ["1234"])];
        (string Field, string[] Values)[] refused =
        [
            ("name", ["Ada"]),
            ("nickname", ["Ad"]),
            ("email", ["not an address"]),
            ("email.confirm", ["not an address"]),
            ("code", ["abc"]),
            ("promo", ["12"]),
            ("again", ["Bob"]),
            ("count", ["5000"]),
            ("visit", ["2026-06-01"]),
            ("phone", ["call me"]),
            ("website", ["not a site"]),
            ("story", ["Too long"]),
            ("topic", ["b"]),
            ("way", ["a"]),
            ($"rate.{K(0x30):D}", ["good"]),
            ($"rate.{Second:D}", ["poor"]),
            ("full.first", ["Ada"]),
            ("company", ["Acme"]),
            ("guests[0].guest", ["Bob"]),
            ("guests[0].confirm", ["Rob"]),
            ("guests[0].diet", ["Nothing with nuts"]),
        ];
        yield return ("required", "/errors/", FormPostFields.Submit, required);
        yield return ("refused", "/errors/", FormPostFields.Submit, refused);
        yield return ("empty", "/errors/", FormPostFields.Submit, []);
        yield return ("next", "/errors-paged/", FormPostFields.Next, [("pagedEmail", ["nope"])]);
        yield return ("bootstrap5 required", "/errors-bootstrap/", FormPostFields.Submit, required);
        yield return ("bootstrap5 refused", "/errors-bootstrap/", FormPostFields.Submit, refused);
    }

    // A field of every kind the browser checks: required texts, one with its label hidden, an email address asked twice
    // with its own message;
    // checks with and without their message, one that names another field; a number's range and a date's window; a
    // phone and a web address; a text's longest; choices a rule requires and a radio; a grid with a row required by
    // itself and a row a validation rule without a message refuses; a consent; a name in parts; a field a rule shows;
    // a company a validation rule refuses; guests in rows, each with a name, the name again and a diet.
    private static FormDefinition Checks(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage
            {
                Key = K(0x01),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x02),
                        Fields =
                        [
                            Field(Name, "name", "text", "Name") with { Required = true },
                            Field(K(0x11), "nickname", "text", "Nickname") with { Required = true, HideLabel = true, Settings = SettingsObject.Parse("""{"minLength":3}""") },
                            Field(Email, "email", "email", "Email address", """{"confirm":true}""") with { Required = true, Messages = new FieldMessages { Required = new LocalizedText("Tell us your email address") } },
                            Field(K(0x13), "code", "text", "Code") with { Validation = [new PatternCheck { Pattern = "[A-Z]{3}", Message = new LocalizedText("Use three capital letters") }] },
                            Field(K(0x14), "promo", "text", "Promo code") with { Validation = [new PatternCheck { Pattern = "[0-9]{4}" }] },
                            Field(K(0x15), "again", "text", "Name again") with { Validation = [new MatchesFieldCheck { Field = Name }] },
                            Field(K(0x16), "count", "number", "Count", """{"min":"1","max":"1000"}"""),
                            Field(K(0x17), "visit", "date", "When will you visit?", """{"min":{"today":"+1d"}}"""),
                            Field(K(0x18), "phone", "phone", "Phone"),
                            Field(K(0x19), "website", "url", "Website"),
                            Field(K(0x1b), "story", "text", "Story", """{"maxLength":5}"""),
                            Field(Topic, "topic", "dropdown", "Topic") with { Required = true, Options = Options(("a", "A"), ("b", "B")) },
                            Field(K(0x1c), "way", "radio", "Way") with { Required = true, Options = Options(("a", "By post"), ("b", "By email")) },
                            Field(K(0x1d), "rate", "grid", "Rate") with
                            {
                                Options = Options(("good", "Good"), ("poor", "Poor")),
                                Rows = [new GridRow { Key = K(0x30), Label = new LocalizedText("The food") }, new GridRow { Key = Second, Label = new LocalizedText("The room"), Required = true }],
                            },
                            Field(K(0x1e), "agree", "consent", "Terms", """{"text":{"":"<p>I agree to the terms.</p>"}}""") with { Required = true },
                            Field(K(0x1f), "full", "name", "Your name", """{"parts":["first","last"]}""") with { Required = true },
                            Field(K(0x20), "secret", "text", "Secret") with { Required = true, Rules = [Rule(K(0x40), RuleEffect.Show, Name, "isNotEmpty")] },
                            Field(K(0x21), "detail", "text", "Detail") with { Rules = [Rule(K(0x41), RuleEffect.Require, Topic, "equals", "b")] },
                            Field(Company, "company", "text", "Company"),
                            Field(K(0x23), "guests", "repeater", "Guests", """{"minRows":1,"rowLabel":{"":"Guest"}}""") with
                            {
                                Fields =
                                [
                                    Field(K(0x24), "guest", "text", "Guest name") with { Required = true },
                                    Field(K(0x26), "confirm", "text", "Guest name again") with { Validation = [new MatchesFieldCheck { Field = K(0x24) }] },
                                    Field(K(0x25), "diet", "text", "Diet", """{"maxLength":10}"""),
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
        ValidationRules =
        [
            new FormValidationRule
            {
                Key = K(0x42),
                Field = Company,
                Message = new LocalizedText("We cannot take bookings for this company"),
                Conditions = [new RuleCondition { Field = Company, Operator = "equals", Value = new ConditionOperand.Text("Acme") }],
            },
            new FormValidationRule
            {
                Key = K(0x43),
                Field = K(0x30),
                Conditions = [new RuleCondition { Field = K(0x30), Operator = "equals", Value = new ConditionOperand.Text("good") }],
            },
        ],
    };

    // Two pages: a name and an email address, then a text; Next checks the first alone.
    private static FormDefinition Paged(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage { Key = K(0x50), Groups = [new FormGroup { Key = K(0x51), Fields = [Field(K(0x52), "pagedName", "text", "Name") with { Required = true }, Field(K(0x53), "pagedEmail", "email", "Email address")] }] },
            new FormPage { Key = K(0x54), Groups = [new FormGroup { Key = K(0x55), Fields = [Field(K(0x56), "later", "text", "Later") with { Required = true }] }] },
        ],
    };

    private static FormField Field(Guid key, string alias, string type, string label, string settings = "{}") => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(label),
        Settings = settings == "{}" ? null : SettingsObject.Parse(settings),
    };

    private static FieldOptions Options(params (string Value, string Label)[] options) =>
        new() { Items = [.. options.Select(option => new FieldOption { Value = option.Value, Label = new LocalizedText(option.Label) })] };

    private static FormRule Rule(Guid key, RuleEffect effect, Guid field, string op, string? value = null) => new()
    {
        Key = key,
        Effect = effect,
        Conditions = [new RuleCondition { Field = field, Operator = op, Value = value is null ? null : new ConditionOperand.Text(value) }],
    };

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000064d{number:x3}");
}
