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
/// The server's own calculations and sliders, which the runtime's are held to (ROADMAP 6.4f, ADR-0114): a form with a
/// calculation of every format (a prefix and its places, a suffix and no places, the default places, one that reads
/// another, the days between two dates, a choice's number times a slider's, one a rule shows, and one per row of a
/// repeatable group) and two sliders, one with its unit and its value shown, one without either, is posted without
/// JavaScript, as a browser posts it, with its required name left empty so the page comes back with the answers, in
/// English and in German. Each page first rendered and rendered again is saved, the form element alone (its route
/// token and honeypot left out, its key fixed), with the clock it used, in
/// <c>tests/fixtures/rendering/calculations-&lt;language&gt;.json</c>. <c>packages/runtime</c>'s
/// <c>outputs.fixture.test.ts</c> loads each page, blanks every result and slider text, shows them again as the
/// runtime does, and expects the server's page. After a deliberate change, run the fixture with
/// <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the files and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class CalculationFixtureTests : RenderingTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid FormKey = Guid.Parse("30000000-0000-4000-8000-0000000000f1");
    private static readonly Guid Insured = K(0x18);
    private static readonly Guid Cost = K(0x31);

    [TestCase("en")]
    [TestCase("de")]
    public async Task TheServersCalculationsAndSliders_AreTheFixtureTheRuntimeIsHeldTo(string language)
    {
        GetRequiredService<TestClock>().SetNow(Now);
        var key = await PublishFormAsync("renderingCalculations", Calculations);
        _ = language == "de" ? await CulturePagesAsync("de-DE", "German", "calculations") : await PagesAsync("calculations");

        var cases = new JsonArray();
        foreach (var (name, answers) in Cases())
        {
            var form = await FormAtAsync("/calculations/");
            var first = Normalised(form, (key, FormKey));
            foreach (var (field, values) in answers)
            {
                Fill(form, field, values);
            }

            using var response = await PostAsync(form, FormPostFields.Submit, "/calculations/");
            var html = await response.Content.ReadAsStringAsync();
            Assert.That(Succeeded(response), Is.True, $"{name}: {(int)response.StatusCode} {html}");
            var shown = (await new HtmlParser().ParseDocumentAsync(html)).QuerySelector("form.jfb-form") ?? throw new AssertionException($"{name}: no form in {html}");
            Assert.That(shown.QuerySelector("#" + shown.Id + "-name-error"), Is.Not.Null, $"{name}: the post came back for the name it lacks");
            cases.Add(new JsonObject
            {
                ["name"] = name,
                ["now"] = Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["first"] = first,
                ["html"] = Normalised(shown, (key, FormKey)),
            });
        }

        await MatchFixtureAsync($"calculations-{language}.json", cases, "the server's calculations changed");
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

    // The posts, each without the required name: every calculation answered, with the insurance a rule shows; an
    // unreadable price, an empty date and a row's unreadable cost, each leaving what reads it without a value; and a
    // price of nothing, which a division fails on while the others read it as 0.
    private static IEnumerable<(string Name, (string Field, string[] Values)[] Answers)> Cases()
    {
        yield return ("answered", [
            ("price", ["1234.5"]),
            ("quantity", ["3"]),
            ("from", ["2026-11-02"]),
            ("to", ["2026-11-05"]),
            ("plan", ["25"]),
            ("hours", ["2.5"]),
            ("seats", ["1500"]),
            ("insured", ["true"]),
            ("items[0].cost", ["10"]),
            ("items[1].cost", ["7.5"]),
        ]);
        yield return ("unreadable", [
            ("price", ["12x"]),
            ("quantity", ["2"]),
            ("from", ["2026-11-02"]),
            ("plan", ["10"]),
            ("hours", ["8"]),
            ("seats", ["0"]),
            ("insured", ["true"]),
            ("items[0].cost", ["ten"]),
            ("items[1].cost", ["5"]),
        ]);
        yield return ("nothing", [
            ("price", ["0"]),
            ("quantity", ["4"]),
            ("hours", ["1"]),
        ]);
    }

    // The fields: a name the posts leave empty; a price with places, a text input, so the page holds what a visitor
    // types as a browser holds it; a quantity, two dates, a plan whose options are numbers, two sliders and an insurance;
    // their calculations; and items in rows, each with a cost with places and its tax.
    private static FormDefinition Calculations(FormDefinition definition) => definition with
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
                            Field(K(0x10), "name", "text", "Name") with { Required = true },
                            Field(K(0x11), "price", "number", "Price", """{"decimals":2}"""),
                            Field(K(0x12), "quantity", "number", "Quantity"),
                            Field(K(0x13), "from", "date", "From"),
                            Field(K(0x14), "to", "date", "To"),
                            Field(K(0x15), "plan", "radio", "Plan") with { Options = Options(("10", "Small room"), ("25", "Large room")) },
                            Field(K(0x16), "hours", "slider", "Hours", """{"min":"1","max":"8","step":"0.5","unit":{"":"hours","de":"Stunden"}}"""),
                            Field(K(0x17), "seats", "slider", "Seats", """{"min":"0","max":"2000","step":"100","showValue":false}"""),
                            Field(Insured, "insured", "checkbox", "Insured"),
                            Field(K(0x20), "total", "calculation", "Total", """{"formula":"price * quantity","format":{"decimals":2,"prefix":{"":"EUR"}}}"""),
                            Field(K(0x21), "percent", "calculation", "Share", """{"formula":"quantity / price * 100","format":{"decimals":0,"suffix":{"":"%"}}}"""),
                            Field(K(0x22), "third", "calculation", "A third", """{"formula":"price / 3"}"""),
                            Field(K(0x23), "gross", "calculation", "Gross", """{"formula":"total * 1.2","format":{"decimals":2,"prefix":{"":"EUR"}}}"""),
                            Field(K(0x24), "days", "calculation", "Days", """{"formula":"daysBetween(from, to)","format":{"decimals":0,"suffix":{"":"days","de":"Tage"}}}"""),
                            Field(K(0x25), "planned", "calculation", "Planned", """{"formula":"plan * hours","format":{"decimals":1}}"""),
                            Field(K(0x26), "cover", "calculation", "Cover", """{"formula":"total * 0.1","format":{"prefix":{"":"EUR"}}}""") with { Rules = [Rule(K(0x40), RuleEffect.Show, Insured, "isChecked")] },
                            Field(K(0x30), "items", "repeater", "Items", """{"minRows":2,"maxRows":3,"rowLabel":{"":"Item","de":"Posten"}}""") with
                            {
                                Fields =
                                [
                                    Field(Cost, "cost", "number", "Cost", """{"decimals":2}"""),
                                    Field(K(0x32), "vat", "calculation", "Tax", """{"formula":"cost * 0.2","format":{"suffix":{"":"VAT","de":"MwSt."}}}"""),
                                ],
                            },
                        ],
                    },
                ],
            },
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

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000064f{number:x3}");
}
