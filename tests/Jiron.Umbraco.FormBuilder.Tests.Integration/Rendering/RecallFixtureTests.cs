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
/// The server's own visitor texts that recall answers, which the runtime's are held to (ROADMAP 6.4g, ADR-0115): a form
/// with such a text in every place the views mark (a page's title and a progress step's name, a group's title and
/// description, a field's label, help and placeholder, a legend, a box's label, a heading and a text block) that recall
/// a text, a choice, a number, a date, a sensitive answer, a field a rule shows and a repeatable group's children, one
/// of them shown by a rule in its row, is posted without JavaScript as a browser posts its first page, with Next and
/// its required email left empty, so the page comes back with the answers; a second post answers what the rules showed
/// once the first came back, as a visitor without JavaScript can only then. In English and in German, each page first
/// rendered and rendered again after the second post is saved, the form element alone (its route token and honeypot
/// left out, its key fixed), with the clock it used, in <c>tests/fixtures/rendering/recall-&lt;language&gt;.json</c>.
/// <c>packages/runtime</c>'s <c>recall.fixture.test.ts</c> loads each page, makes every marked text stale, renders them
/// again as the runtime does, and expects the server's page. After a deliberate change, run the fixture with
/// <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the files and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RecallFixtureTests : RenderingTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid FormKey = Guid.Parse("30000000-0000-4000-8000-0000000000f2");
    private static readonly Guid Plan = K(0x11);
    private static readonly Guid Guest = K(0x15);

    [TestCase("en")]
    [TestCase("de")]
    public async Task TheServersRecalledTexts_AreTheFixtureTheRuntimeIsHeldTo(string language)
    {
        GetRequiredService<TestClock>().SetNow(Now);
        var key = await PublishFormAsync("renderingRecallFixture", Recall);
        _ = language == "de" ? await CulturePagesAsync("de-DE", "German", "recall-fixture") : await PagesAsync("recall-fixture");

        var cases = new JsonArray();
        foreach (var (name, posts) in Cases())
        {
            var shown = await FormAtAsync("/recall-fixture/");
            var first = Normalised(shown, (key, FormKey));
            foreach (var answers in posts)
            {
                foreach (var (field, values) in answers)
                {
                    Fill(shown, field, values);
                }

                using var response = await PostAsync(shown, FormPostFields.Next, "/recall-fixture/");
                var html = await response.Content.ReadAsStringAsync();
                Assert.That(Succeeded(response), Is.True, $"{name}: {(int)response.StatusCode} {html}");
                shown = (await new HtmlParser().ParseDocumentAsync(html)).QuerySelector("form.jfb-form") ?? throw new AssertionException($"{name}: no form in {html}");
                Assert.That(shown.QuerySelector("#" + shown.Id + "-email-error"), Is.Not.Null, $"{name}: the post came back for the email it lacks");
            }

            Assert.That(shown.QuerySelectorAll("[data-jfb-text]"), Has.Length.EqualTo(Marked), $"{name}: every text that holds tags carries its hook");
            cases.Add(new JsonObject
            {
                ["name"] = name,
                ["now"] = Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["first"] = first,
                ["html"] = Normalised(shown, (key, FormKey)),
            });
        }

        await MatchFixtureAsync($"recall-{language}.json", cases, "the server's recalled texts changed");
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

    // The hooks the page carries: the second page's title and its progress step, the second group's title and
    // description, the room's legend, the notes' label, help and placeholder, the heading, the text block, the box's
    // and the switch's labels, the extra field's label, and the guest's label in each of the two rows.
    private const int Marked = 15;

    // The posts, each without the required email. Everything answered: the large room, so a second post answers the
    // note a rule shows for it, a sensitive code, posted again as the page never shows it, and two guests whose ages
    // the rule shows once they have names. The large room, then the small one, so the note posted with it is hidden and
    // recalls nothing, and a second guest without a name, whose age the rule hides in that row. Nothing but the extra
    // field, so a text that is only a tag renders empty and the step of a page without a title shows its number.
    private static IEnumerable<(string Name, (string Field, string[] Values)[][] Posts)> Cases()
    {
        yield return ("answered", [
            [
                ("name", ["Ada <Lovelace>"]),
                ("plan", ["l"]),
                ("count", ["1234.5"]),
                ("day", ["2026-11-02"]),
                ("guests[0].guest", ["Ben"]),
                ("guests[1].guest", ["Cleo"]),
                ("agree", ["true"]),
                ("news", ["yes"]),
                ("extra", ["x"]),
            ],
            [
                ("note", ["by the window"]),
                ("code", ["1234"]),
                ("guests[0].age", ["adult"]),
                ("guests[1].age", ["under16"]),
            ],
        ]);
        yield return ("hidden", [
            [
                ("name", ["Grace"]),
                ("plan", ["l"]),
                ("count", ["7"]),
                ("guests[0].guest", ["Ben"]),
            ],
            [
                ("plan", ["s"]),
                ("note", ["a note the rules hide"]),
                ("guests[0].age", ["adult"]),
            ],
        ]);
        yield return ("nothing", [
            [("extra", ["x"])],
        ]);
    }

    // The form: on its first page a name, a room whose legend recalls it, a note a rule shows for the large room, a
    // sensitive code, a number, a date and guests in rows; notes whose label, help and placeholder recall them, a
    // heading that is only the name, a text block of the guests, a box and a switch labelled by the name, and the
    // required email; a second group titled and described by the name; and a second page titled by the name alone.
    private static FormDefinition Recall(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage
            {
                Key = K(0x01),
                Title = new LocalizedText("Your booking"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x02),
                        Fields =
                        [
                            Field(K(0x10), "name", "text", "Your name"),
                            Field(Plan, "plan", "radio", "Which room, {{ field.name }}?") with { Options = Options(("s", "Small room"), ("l", "Large room")) },
                            Field(K(0x12), "note", "text", "Note") with { Rules = [Rule(K(0x40), RuleEffect.Show, Plan, "equals", "l")] },
                            Field(K(0x13), "code", "text", "Code") with { Sensitive = true },
                            Field(K(0x14), "count", "number", "Seats", """{"decimals":1}"""),
                            Field(K(0x1a), "day", "date", "Day"),
                            Field(K(0x16), "guests", "repeater", "Guests", """{"minRows":2,"maxRows":3}""") with
                            {
                                Fields =
                                [
                                    Field(Guest, "guest", "text", "Guest of {{ field.name }}"),
                                    Field(K(0x17), "age", "radio", "Age") with
                                    {
                                        Options = Options(("under16", "Under 16"), ("adult", "16 or over")),
                                        Rules = [Rule(K(0x41), RuleEffect.Show, Guest, "isNotEmpty")],
                                    },
                                ],
                            },
                            Field(K(0x18), "notes", "textarea", "Anything else, {{ field.name }}?") with
                            {
                                Help = new LocalizedText("For the {{ field.plan | default: \"room\" }}: {{ field.note | default: \"no note\" }}; code {{ field.code }}; {{ field.count }} seats on {{ field.day }}"),
                                Placeholder = new LocalizedText("{{ field.name | default: \"Your notes\" }}"),
                            },
                            Field(K(0x19), "heading", "heading", null, """{"level":3,"text":{"":"{{ field.name }}"}}"""),
                            Field(K(0x1b), "block", "paragraph", null, """{"html":{"":"<p>Guests: <b>{{ field.guests.guest }}</b>, ages {{ field.guests.age }}</p>"}}"""),
                            Field(K(0x1c), "agree", "checkbox", "Keep {{ field.name }} posted"),
                            Field(K(0x1d), "news", "yesno", "{{ field.name }} reads the newsletter", """{"style":"switch"}"""),
                            Field(K(0x1e), "email", "email", "Email") with { Required = true },
                        ],
                    },
                    new FormGroup
                    {
                        Key = K(0x03),
                        Title = new LocalizedText("For {{ field.name }}"),
                        Description = new LocalizedHtml("<p>Booked by <b>{{ field.name | default: \"you\" }}</b></p>"),
                        Fields = [Field(K(0x1f), "extra", "text", "Extra for {{ field.name }}")],
                    },
                ],
            },
            new FormPage
            {
                Key = K(0x04),
                Title = new LocalizedText("{{ field.name }}"),
                Groups = [new FormGroup { Key = K(0x05), Fields = [Field(K(0x20), "last", "text", "Last")] }],
            },
        ],
    };

    private static FormField Field(Guid key, string alias, string type, string? label, string settings = "{}") => new()
    {
        Key = key,
        Type = type,
        Alias = type is "heading" or "paragraph" ? null : alias,
        Label = label is null ? null : new LocalizedText(label),
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

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000064a{number:x3}");
}
