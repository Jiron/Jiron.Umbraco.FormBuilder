using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The messages of a post's errors on real pages (ROADMAP 6.2g, ADR-0099): a field's own message, a check's and a
/// validation rule's shown instead of the defaults, the package's defaults under the field and, after the field's label
/// and a row's legend, in the summary (with no colon after a label that ends in its own mark, ADR-0106), whose heading counts, with the values they name (a full option's label, a grid's
/// row, a date window from the site's today, a number in the culture's format); the empty submission's message, linked to the page's first
/// field, also on a form without the summary; and the defaults and the heading on a German page.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingMessagesTests : RenderingTestBase
{
    private static readonly Guid Name = K(0x10);
    private static readonly Guid Nickname = K(0x11);
    private static readonly Guid Email = K(0x12);
    private static readonly Guid Code = K(0x13);
    private static readonly Guid Promo = K(0x14);
    private static readonly Guid Repeat = K(0x15);
    private static readonly Guid Company = K(0x16);
    private static readonly Guid Guests = K(0x17);
    private static readonly Guid GuestName = K(0x18);
    private static readonly Guid Workshop = K(0x19);
    private static readonly Guid Times = K(0x1a);
    private static readonly Guid Visit = K(0x1b);
    private static readonly Guid Count = K(0x1c);
    private static readonly Guid Member = K(0x1d);
    private static readonly Guid Taken = K(0x20);
    private static readonly Guid Morning = K(0x30);

    // The site's today in its time zone, UTC: 15 June 2026.
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The state the view <c>messages</c> leaves for its form: none, or a post's errors, one error or no answer.</summary>
    /// <param name="state">The state's name: errors, one or empty.</param>
    internal static FormPostState? State(string state) => state switch
    {
        "errors" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers> { [Company] = new FieldAnswers(["Acme"]), [Workshop] = new FieldAnswers(["repair"]) },
            Errors = new Dictionary<Guid, FormPostError>
            {
                [Name] = new("required"),
                [Nickname] = new("required"),
                [Email] = new("required"),
                [Code] = new("check", Check: 0),
                [Promo] = new("check", Check: 0),
                [Repeat] = new("check", Check: 0),
                [Company] = new("rule", Rule: Taken),
                [Member] = new("check", Check: 0),
                [Workshop] = new("full"),
                [Times] = new("required", GridRow: Morning),
                [Visit] = new("dateWindow"),
                [Count] = new("max"),
                [Guests] = new("rowCount"),
            },
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>>
            {
                [Guests] = [new FormPostRow { Errors = new Dictionary<Guid, FormPostError> { [GuestName] = new("required") } }],
            },
        },
        "one" => new FormPostState { Errors = new Dictionary<Guid, FormPostError> { [Name] = new("required") } },
        // The pipeline names an empty send's fields' errors when it has any (ADR-0266), so its own problem comes alone.
        "empty" => new FormPostState { IsEmpty = true },
        _ => null,
    };

    [Test]
    public async Task APostsErrors_ShowTheEditorsMessages_AndOtherwiseTheDefaults_NamedByTheirFieldsInTheSummary()
    {
        GetRequiredService<TestClock>().SetNow(Now);
        _ = await PublishFormAsync("renderingMessages", MessagesForm);
        _ = await PagesAsync("messages");
        var form = (await DocumentAsync("/messages/?state=errors")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(Text(form, ".jfb-error-summary h2"), Is.EqualTo("These answers need a change"), "several");
            Assert.That(form.QuerySelectorAll(".jfb-error-summary a").Select(link => (link.GetAttribute("href"), link.TextContent)), Is.EqualTo(new[]
            {
                ("#jfb-1-name", "Name: Enter an answer"),
                ("#jfb-1-nickname", "Enter an answer"),
                ("#jfb-1-email", "Tell us your email address"),
                ("#jfb-1-code", "Use three capital letters"),
                ("#jfb-1-promo", "Promo code: Enter the answer in the format asked for"),
                ("#jfb-1-repeat", "Repeat email: Enter the same as for Email address"),
                ("#jfb-1-company", "We cannot take bookings for this company"),
                ("#jfb-1-membership", "Member number: This answer has already been given. Enter another one."),
                ("#jfb-1-workshop", "Workshop: Bike repair basics is now full. Choose another option."),
                ("#jfb-1-times", "Times (Morning): Choose an answer in this row"),
                ("#jfb-1-visit", "When will you visit? Choose a date on or after Jun 16, 2026"),
                ("#jfb-1-count", "Count: Enter a number from 1 to 1,000"),
                ("#jfb-1-guests-0-guestName", "Guest name (Guest 1): Enter an answer"),
                ("#jfb-1-guests", "Guests: Add at least 2 rows"),
            }), "a default after its field's label (with no colon after a question) and its row's legend, a field whose label is hidden and an editor's own message as written");
            Assert.That(Errors(form), Is.EqualTo(new[]
            {
                ("jfb-1-name-error", "Enter an answer"),
                ("jfb-1-nickname-error", "Enter an answer"),
                ("jfb-1-email-error", "Tell us your email address"),
                ("jfb-1-code-error", "Use three capital letters"),
                ("jfb-1-promo-error", "Enter the answer in the format asked for"),
                ("jfb-1-repeat-error", "Enter the same as for Email address"),
                ("jfb-1-company-error", "We cannot take bookings for this company"),
                ("jfb-1-membership-error", "This answer has already been given. Enter another one."),
                ("jfb-1-workshop-error", "Bike repair basics is now full. Choose another option."),
                ("jfb-1-times-error", "Morning: Choose an answer in this row"),
                ("jfb-1-visit-error", "Choose a date on or after Jun 16, 2026"),
                ("jfb-1-count-error", "Enter a number from 1 to 1,000"),
                ("jfb-1-guests-0-guestName-error", "Enter an answer"),
                ("jfb-1-guests-error", "Add at least 2 rows"),
            }), "under each field the message alone, a grid's row named");
        });
    }

    [Test]
    public async Task OneError_HeadsTheSummaryInTheSingular()
    {
        _ = await PublishFormAsync("renderingMessages", MessagesForm);
        _ = await PagesAsync("messages");
        var form = (await DocumentAsync("/messages/?state=one")).QuerySelector("form.jfb-form")!;

        Assert.That(Compact(form.QuerySelector(".jfb-error-summary")!.OuterHtml), Is.EqualTo(
            """<div class="jfb-error-summary" data-jfb-summary="" role="alert" tabindex="-1" aria-labelledby="jfb-1-summary"><h2 id="jfb-1-summary">This answer needs a change</h2><ul role="list"><li><a href="#jfb-1-name">Name: Enter an answer</a></li></ul></div>"""));
    }

    [Test]
    public async Task AnEmptySubmission_SaysSoInTheSummary_LinkedToThePagesFirstField_AlsoWithoutTheSummary()
    {
        _ = await PublishFormAsync("renderingMessages", MessagesForm);
        _ = await PublishFormAsync("renderingMessagesQuiet", definition => MessagesForm(definition) with
        {
            Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { ValidationSummary = false } },
        });
        _ = await PagesAsync("messages");
        var form = (await DocumentAsync("/messages/?state=empty")).QuerySelector("form.jfb-form")!;
        var quiet = (await DocumentAsync("/messages/?state=empty&form=quiet")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(Links(form), Is.EqualTo(new[] { ("#jfb-1-name", "Answer at least one question before you send the form") }), "to the first field past the heading");
            Assert.That(Text(form, ".jfb-error-summary h2"), Is.EqualTo("This answer needs a change"));
            Assert.That(Errors(form), Is.Empty, "no field's error");
            Assert.That(Links(quiet), Is.EqualTo(new[] { ("#jfb-1-name", "Answer at least one question before you send the form") }), "a form without the summary shows the message that belongs to no field");
            Assert.That(Text(quiet, ".jfb-error-summary h2"), Is.EqualTo("This answer needs a change"));
        });
    }

    [Test]
    public async Task OnAGermanPage_TheDefaultsAndTheHeading_AreGerman()
    {
        GetRequiredService<TestClock>().SetNow(Now);
        _ = await PublishFormAsync("renderingMessages", MessagesForm);
        _ = await CulturePagesAsync("de-DE", "German", "messages");
        var form = (await DocumentAsync("/messages/?state=errors")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(Text(form, ".jfb-error-summary h2"), Is.EqualTo("Diese Antworten m" + (char)0xFC + "ssen noch ge" + (char)0xE4 + "ndert werden"));
            Assert.That(Links(form)[0], Is.EqualTo(("#jfb-1-name", "Name: Geben Sie eine Antwort ein")));
            Assert.That(Text(form, "#jfb-1-guests-error"), Is.EqualTo("F" + (char)0xFC + "gen Sie mindestens 2 Zeilen hinzu"));
            Assert.That(Text(form, "#jfb-1-email-error"), Is.EqualTo("Tell us your email address"), "an editor's message in the only culture it has");
            Assert.That(Text(form, "#jfb-1-count-error"), Is.EqualTo("Geben Sie eine Zahl von 1 bis 1.000 ein"), "in the culture's format");
            Assert.That(Text(form, "#jfb-1-visit-error"), Is.EqualTo("W" + (char)0xE4 + "hlen Sie ein Datum ab dem 16. Juni 2026"), "the date in the culture's medium pattern");
        });
    }

    // A heading, then: a name; a nickname whose label is hidden; an email address with its own message; a code whose
    // check has its message; a promo code whose check has none; the email address repeated; a company a validation rule
    // refuses; a member number given already; a workshop whose option filled up; times in a grid; a visit from tomorrow;
    // a count up to 1,000; and guests in rows, at least two, each with a name.
    private static FormDefinition MessagesForm(FormDefinition definition) => definition with
    {
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
                            new FormField { Key = K(0x04), Type = "heading", Settings = SettingsObject.Parse("""{"text":{"":"About you"}}""") },
                            Field(Name, "name", "text", "Name") with { Required = true },
                            Field(Nickname, "nickname", "text", "Nickname") with { Required = true, HideLabel = true },
                            Field(Email, "email", "email", "Email address") with { Required = true, Messages = new FieldMessages { Required = new LocalizedText("Tell us your email address") } },
                            Field(Code, "code", "text", "Code") with { Validation = [new PatternCheck { Pattern = "[A-Z]{3}", Message = new LocalizedText("Use three capital letters") }] },
                            Field(Promo, "promo", "text", "Promo code") with { Validation = [new PatternCheck { Pattern = "[0-9]{4}" }] },
                            Field(Repeat, "repeat", "email", "Repeat email") with { Validation = [new MatchesFieldCheck { Field = Email }] },
                            Field(Company, "company", "text", "Company"),
                            Field(Member, "membership", "text", "Member number") with { Validation = [new UniquePerFormCheck()] },
                            Field(Workshop, "workshop", "dropdown", "Workshop") with { Options = Options(("repair", "Bike repair basics"), ("sewing", "Sewing")) },
                            Field(Times, "times", "grid", "Times") with
                            {
                                Options = Options(("yes", "Yes"), ("no", "No")),
                                Rows = [new GridRow { Key = Morning, Label = new LocalizedText("Morning") }, new GridRow { Key = K(0x31), Label = new LocalizedText("Evening") }],
                            },
                            Field(Visit, "visit", "date", "When will you visit?", """{"min":{"today":"+1d"}}"""),
                            Field(Count, "count", "number", "Count", """{"min":"1","max":"1000"}"""),
                            Field(Guests, "guests", "repeater", "Guests", """{"minRows":2,"rowLabel":{"":"Guest"}}""") with
                            {
                                Fields = [Field(GuestName, "guestName", "text", "Guest name") with { Required = true }],
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
                Key = Taken,
                Field = Company,
                Message = new LocalizedText("We cannot take bookings for this company"),
                Conditions = [new RuleCondition { Field = Company, Operator = "equals", Value = new ConditionOperand.Text("Acme") }],
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

    // The summary's links as their targets and texts.
    private static (string?, string)[] Links(IElement form) =>
        [.. form.QuerySelectorAll(".jfb-error-summary a").Select(link => (link.GetAttribute("href"), link.TextContent))];

    // The errors under the fields as their ids and texts, in the page's order.
    private static (string, string)[] Errors(IElement form) =>
        [.. form.QuerySelectorAll("p.jfb-error[data-jfb-error]").Select(error => (error.Id ?? string.Empty, error.TextContent))];

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-000000629{number:x3}");

    private static string Compact(string html) => string.Concat(html.Split((char)10).Select(line => line.Trim()));

    private static string Text(IElement element, string selector) => (element.QuerySelector(selector) ?? throw new AssertionException($"no {selector}")).TextContent;

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{path} answers {(int)response.StatusCode}: {html}");
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
