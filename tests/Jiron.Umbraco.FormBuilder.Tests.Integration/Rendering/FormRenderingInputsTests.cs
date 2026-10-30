using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The controls of the types with parts, dates and outputs on real pages (ROADMAP 6.2e, ADR-0015, ADR-0097): each such
/// type empty, answered, with an error and hidden by a rule, with the markup contract's hooks and a date's limits for
/// the site's today; a calculation showing the value the submission pipeline works out for the same answers; the rarer
/// cases under the asterisk style; and a site's own type's control from its own view.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingInputsTests : RenderingTestBase
{
    private static readonly Guid Trigger = K(0x01);
    private static readonly Guid Captcha = K(0x19);
    private static readonly Guid Price = K(0x40);
    private static readonly Guid Size = K(0x41);
    private static readonly Guid Nights = K(0x42);
    private static readonly Guid Arrive = K(0x43);
    private static readonly Guid Leave = K(0x44);
    private static readonly Guid Total = K(0x45);
    private static readonly Guid Stay = K(0x46);
    private static readonly Guid Person = K(0x50);
    private static readonly Guid Home = K(0x51);
    private static readonly Guid Arrival = K(0x52);
    private static readonly Guid Late = K(0x53);
    private static readonly Guid Born = K(0x54);
    private static readonly Guid Far = K(0x55);
    private static readonly Guid Volume = K(0x56);
    private static readonly Guid Photos = K(0x57);
    private static readonly Guid Drawing = K(0x58);
    private static readonly Guid Unseen = K(0x59);
    private static readonly Guid Placement = K(0x5a);
    private static readonly Guid Autograph = K(0x5b);
    private static readonly Guid Guest = K(0x5c);
    private static readonly Guid Host = K(0x5d);
    private static readonly Guid Mood = K(0x60);
    private static readonly Guid Name = K(0x61);
    private static readonly Guid Rating = K(0x70);
    private static readonly Guid RowA = K(0x71);
    private static readonly Guid RowB = K(0x72);
    private static readonly Guid Tier = K(0x73);
    private static readonly Guid Source = K(0x74);

    // Each field of the inputs form: its key, alias and type, and whether it is a fieldset with its legend.
    private static readonly (Guid Key, string Alias, string Type, bool Grouped)[] Inputs =
    [
        (K(0x10), "visit", "date", false),
        (K(0x11), "start", "time", false),
        (K(0x12), "pickup", "datetime", false),
        (K(0x13), "hours", "slider", false),
        (K(0x14), "cost", "calculation", false),
        (K(0x15), "contact", "name", true),
        (K(0x16), "billing", "address", true),
        (K(0x17), "plan", "file", false),
        (K(0x18), "signature", "signature", true),
    ];

    // Each input's default message for no answer (ADR-0099), by what its control asks.
    private static readonly Dictionary<string, string> Required = new(StringComparer.Ordinal)
    {
        ["visit"] = "Enter a date",
        ["start"] = "Enter a time",
        ["pickup"] = "Enter a date and a time",
        ["hours"] = "Choose a value",
        ["contact"] = "Enter the name in full",
        ["billing"] = "Enter the address in full",
        ["plan"] = "Choose a file",
        ["signature"] = "Add your signature, or type your name instead",
    };

    // Half past midnight on 1 November in Auckland, still 31 October in UTC: the site's today is the first.
    private static readonly DateTimeOffset AucklandMidnight = new(2026, 10, 31, 11, 30, 0, TimeSpan.Zero);

    /// <summary>The state the view <c>inputs</c> leaves for the form: none, or a post's answers, errors or the rules' trigger.</summary>
    /// <param name="state">The state's name: answered, errors or hidden.</param>
    internal static FormPostState? State(string state) => state switch
    {
        "answered" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [Of("visit")] = new FieldAnswers(["2026-11-05"]),
                [Of("start")] = new FieldAnswers(["10:30"]),
                [Of("pickup")] = new FieldAnswers(["2026-11-20T09:15"]),
                [Of("hours")] = new FieldAnswers(["3"]),
                [Of("contact")] = new FieldAnswers([], Parts(("first", "Ada"), ("last", "Lovelace"))),
                [Of("billing")] = new FieldAnswers([], Parts(("street", "Market Street 12"), ("street2", "Second floor"), ("postcode", "8001"), ("city", "Zurich"), ("country", "CH"))),
                [Of("signature")] = new FieldAnswers([], Parts(("typed", "Ada Lovelace"))),
            },
        },
        "errors" => new FormPostState
        {
            Errors = Inputs.Where(input => input.Type != "calculation").ToDictionary(input => input.Key, _ => new FormPostError("required")).Append(KeyValuePair.Create(Captcha, new FormPostError("invalid"))).ToDictionary(),
        },
        "hidden" => new FormPostState { Answers = new Dictionary<Guid, FieldAnswers> { [Trigger] = new FieldAnswers(["yes"]) } },
        _ => null,
    };

    /// <summary>The state the view <c>sums</c> leaves for its form: none, or a post's answers.</summary>
    /// <param name="state">The state's name: posted, or anything else for none.</param>
    internal static FormPostState? SumsState(string state) => state == "posted" ? new FormPostState { Answers = Sums } : null;

    /// <summary>The state the view <c>parts</c> leaves for its form: none, or a post with an address in part, a full name, and errors.</summary>
    /// <param name="state">The state's name: posted, or anything else for none.</param>
    internal static FormPostState? PartsState(string state) => state == "posted"
        ? new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [Person] = new FieldAnswers([], Parts(("first", "Ada"), ("last", "Lovelace"))),
                [Home] = new FieldAnswers([], Parts(("street", "Quay Street 1"))),
                [Volume] = new FieldAnswers(["loud"]),
            },
            Errors = new Dictionary<Guid, FormPostError> { [Person] = new("invalid"), [Home] = new("invalid") },
        }
        : null;

    /// <summary>The state the view <c>rules</c> leaves for its form: none, or a post that answers a grid row and two hidden fields.</summary>
    /// <param name="state">The state's name: posted, or anything else for none.</param>
    internal static FormPostState? RulesState(string state) => state == "posted"
        ? new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [Rating] = new FieldAnswers([], Parts((RowA.ToString("D"), "3"))),
                [Tier] = new FieldAnswers(["gold"]),
                [Source] = new FieldAnswers(["mail"]),
            },
        }
        : null;

    /// <summary>The state the view <c>custom</c> leaves for its form: none, or a post's answer with a part.</summary>
    /// <param name="state">The state's name: answered, or anything else for none.</param>
    internal static FormPostState? CustomState(string state) => state == "answered"
        ? new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [Name] = new FieldAnswers(["Grace"]),
                [Mood] = new FieldAnswers(["bright"], Parts(("note", "After the talk"))),
            },
        }
        : null;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    // The answers of the sums form's post: a price with spaces around it, which the pipeline cleans away.
    private static Dictionary<Guid, FieldAnswers> Sums => new()
    {
        [Price] = new FieldAnswers([" 12.50 "]),
        [Size] = new FieldAnswers(["10"]),
        [Nights] = new FieldAnswers(["3"]),
        [Arrive] = new FieldAnswers(["2026-11-02"]),
        [Leave] = new FieldAnswers(["2026-11-05"]),
    };

    [TearDown]
    public void ResetTheClock() => GetRequiredService<TestClock>().Reset();

    [Test]
    public async Task EveryPartDateAndOutputType_EmptyAnsweredWithAnErrorAndHidden_CarriesTheMarkupContractsHooks()
    {
        await AucklandAsync(AucklandMidnight);
        _ = await PublishFormAsync("renderingInputs", InputsForm);
        _ = await PagesAsync("inputs");

        foreach (var state in new[] { "empty", "answered", "errors", "hidden" })
        {
            var form = (await DocumentAsync("/inputs/" + (state == "empty" ? string.Empty : "?state=" + state))).QuerySelector("form.jfb-form") ?? throw new AssertionException($"{state}: no form");
            Hooks(form, state);
        }

        var empty = (await DocumentAsync("/inputs/")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(Control(empty, "visit").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="date" id="jfb-1-visit" name="visit" value="2026-11-01" min="2026-11-01" max="2026-11-15" required="required" data-jfb-requires="" aria-describedby="jfb-1-visit-help">"""), "the site's today, not UTC's, starts the date and its window");
            Assert.That(Control(empty, "start").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="time" id="jfb-1-start" name="start" min="09:00" max="17:30" step="1800" data-jfb-requires="">"""));
            Assert.That(Control(empty, "pickup").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="datetime-local" id="jfb-1-pickup" name="pickup" min="2026-11-02T00:00" max="2026-12-31T23:59" step="900" data-jfb-requires="">"""), "a date and time's window is that of its dates");
            Assert.That(Text(Field(empty, "start"), "label.jfb-label"), Is.EqualTo("Start (optional)"));

            var hours = Field(empty, "hours");
            Assert.That(Text(hours, "label.jfb-label"), Is.EqualTo("Hours"), "a slider always holds an answer, so it is never marked");
            Assert.That(Compact(hours.QuerySelector(".jfb-range")!.InnerHtml), Is.EqualTo(
                """<input type="range" id="jfb-1-hours" name="hours" min="1" max="8" step="1" value="5" aria-valuetext="5 hours"><output for="jfb-1-hours" aria-hidden="true">5 hours</output>"""),
                "the middle of its range on its step, said with its unit, and shown beside it for sight");
            var cost = Field(empty, "cost");
            Assert.That(Text(cost, "label.jfb-label"), Is.EqualTo("Cost"), "a calculation is never optional: the visitor gives it no answer");
            Assert.That(cost.QuerySelector("label")!.GetAttribute("for"), Is.EqualTo("jfb-1-cost"));
            Assert.That(cost.QuerySelector("output")!.OuterHtml, Is.EqualTo("""<output class="jfb-output" id="jfb-1-cost" name="cost" aria-describedby="jfb-1-cost-help">EUR 75.00</output>"""), "worked out for the slider's start");

            var contact = Field(empty, "contact");
            Assert.That(Text(contact, "legend.jfb-label"), Is.EqualTo("Contact"));
            Assert.That(contact.QuerySelector("fieldset")!.GetAttribute("aria-describedby"), Is.Null);
            Assert.That(Compact(contact.QuerySelector(".jfb-parts")!.InnerHtml), Is.EqualTo(
                """<div class="jfb-part"><label for="jfb-1-contact">First name</label><input class="jfb-control" id="jfb-1-contact" name="contact.first" autocomplete="given-name" autocapitalize="words" spellcheck="false" required="required" data-jfb-requires=""></div>"""
                + """<div class="jfb-part"><label for="jfb-1-contact-last">Last name</label><input class="jfb-control" id="jfb-1-contact-last" name="contact.last" autocomplete="family-name" autocapitalize="words" spellcheck="false" required="required" data-jfb-requires=""></div>"""),
                "a required name needs every part it asks for, and the legend says so once");

            var billing = Field(empty, "billing");
            Assert.That(billing.QuerySelectorAll(".jfb-part").Select(part => (part.ClassName, part.QuerySelector("label")!.InnerHtml, (part.QuerySelector("input, select") as IElement)!.GetAttribute("name"), part.QuerySelector("input, select")!.GetAttribute("autocomplete"), part.QuerySelector("input, select")!.HasAttribute("required"))), Is.EqualTo(new[]
            {
                ("jfb-part jfb-part-wide", "Street and number", "billing.street", "address-line1", true),
                ("jfb-part jfb-part-wide", """Address line 2 <span class="jfb-mark" data-jfb-mark="required">(optional)</span>""", "billing.street2", "address-line2", false),
                ("jfb-part", """Postcode <span class="jfb-mark" data-jfb-mark="required">(optional)</span>""", "billing.postcode", "postal-code", false),
                ("jfb-part", "Town", "billing.city", "address-level2", true),
                ("jfb-part jfb-part-wide", """Country <span class="jfb-mark" data-jfb-mark="required">(optional)</span>""", "billing.country", "country", false),
            }), "the parts it asks for in its type's order; with some parts required, the others are marked in the form's style");
            Assert.That(Text(billing, "legend.jfb-label"), Is.EqualTo("Billing"), "a required address is not optional");
            Assert.That(billing.QuerySelector(".jfb-part-wide > .jfb-select > select")!.GetAttribute("id"), Is.EqualTo("jfb-1-billing-country"));
            Assert.That(Options(billing).Take(4), Is.EqualTo(new[] { ": Choose a country", "AF: Afghanistan", "AX: " + (char)0xC5 + "land Islands", "AL: Albania" }), "every country, by its name in the page's culture");
            Assert.That(billing.QuerySelectorAll("option").Length, Is.EqualTo(BuiltInChecks.CountryCodes.Count + 1));

            var plan = Field(empty, "plan");
            Assert.That(Compact(plan.InnerHtml), Does.EndWith(
                """<div class="jfb-upload" data-jfb-dropzone=""><span class="jfb-icon jfb-icon-upload" aria-hidden="true"></span><div class="jfb-upload-actions"><input type="file" id="jfb-1-plan" name="plan" accept=".pdf,.jpg,.jpeg,.png,.gif,.webp,.avif,.heic,.heif" data-jfb-requires=""></div></div>"""),
                "the native input with the extensions it takes, in the order of its groups");
            Assert.That(plan.QuerySelector("label.jfb-label")!.GetAttribute("for"), Is.EqualTo("jfb-1-plan"));

            var signature = Field(empty, "signature");
            Assert.That(Text(signature, "legend.jfb-label"), Is.EqualTo("Signature"));
            Assert.That(Compact(signature.QuerySelector(".jfb-signature")!.InnerHtml), Is.EqualTo(
                """<p class="jfb-help" id="jfb-1-signature-typed-help">Type your full name. It counts as your signature.</p><label class="jfb-visually-hidden" for="jfb-1-signature">Your full name</label><input class="jfb-control" id="jfb-1-signature" name="signature.typed" maxlength="100" autocomplete="name" autocapitalize="words" spellcheck="false" required="required" data-jfb-requires="" aria-describedby="jfb-1-signature-typed-help">"""),
                "without JavaScript, the name typed instead; the legend is the only label that shows");

            Assert.That(Captchas(empty).OuterHtml, Is.EqualTo($"""<div class="jfb-captcha" id="jfb-1-{Captcha:N}" data-jfb-captcha="hcaptcha"></div>"""), "the form's captcha's place, which its provider fills");
        });

        var answered = (await DocumentAsync("/inputs/?state=answered")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(Control(answered, "visit").GetAttribute("value"), Is.EqualTo("2026-11-05"));
            Assert.That(Control(answered, "start").GetAttribute("value"), Is.EqualTo("10:30"));
            Assert.That(Control(answered, "pickup").GetAttribute("value"), Is.EqualTo("2026-11-20T09:15"));
            Assert.That((Control(answered, "hours").GetAttribute("value"), Control(answered, "hours").GetAttribute("aria-valuetext")), Is.EqualTo(("3", "3 hours")));
            Assert.That(Text(Field(answered, "hours"), "output"), Is.EqualTo("3 hours"));
            Assert.That(Control(answered, "cost").TextContent, Is.EqualTo("EUR 45.00"), "worked out for the posted hours");
            Assert.That(Field(answered, "contact").QuerySelectorAll("input").Select(input => input.GetAttribute("value")), Is.EqualTo(new[] { "Ada", "Lovelace" }));
            Assert.That(Field(answered, "billing").QuerySelectorAll("input").Select(input => input.GetAttribute("value")), Is.EqualTo(new[] { "Market Street 12", "Second floor", "8001", "Zurich" }));
            Assert.That(Field(answered, "billing").QuerySelectorAll("option").Cast<IHtmlOptionElement>().Where(option => option.IsSelected).Select(option => option.Value), Is.EqualTo(new[] { "CH" }));
            Assert.That(Control(answered, "plan").HasAttribute("value"), Is.False, "a file input holds no file after a post");
            Assert.That(Control(answered, "signature").GetAttribute("value"), Is.EqualTo("Ada Lovelace"));
        });

        var errors = (await DocumentAsync("/inputs/?state=errors")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            var answerable = Inputs.Where(input => input.Type != "calculation").ToList();
            Assert.That(errors.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(answerable.Select(input => "#jfb-1-" + input.Alias).Append($"#jfb-1-{Captcha:N}")), "each links to its control, a field in parts to its first part");
            foreach (var (_, alias, _, grouped) in answerable)
            {
                var field = Field(errors, alias);
                var error = field.QuerySelector($"p.jfb-error#jfb-1-{alias}-error[data-jfb-error]");
                Assert.That(error?.TextContent, Is.EqualTo(Required[alias]), alias);
                var described = grouped ? field.QuerySelector("fieldset")! : Control(errors, alias);
                Assert.That(described.GetAttribute("aria-invalid"), Is.EqualTo("true"), alias);
                Assert.That(described.GetAttribute("aria-describedby")?.Split(' '), Does.Contain($"jfb-1-{alias}-error"), alias);
            }

            Assert.That(Field(errors, "contact").QuerySelectorAll("input[aria-invalid=true]"), Has.Length.EqualTo(2), "the parts it needs that are empty carry the error");
            Assert.That(Field(errors, "billing").QuerySelectorAll(".jfb-part [aria-invalid=true]").Select(part => part.GetAttribute("name")), Is.EqualTo(new[] { "billing.street", "billing.city" }), "only the parts it needs");
            Assert.That(Control(errors, "signature").GetAttribute("aria-invalid"), Is.EqualTo("true"), "the typed name too");
            Assert.That(Control(errors, "cost").TextContent, Is.EqualTo("EUR 0.00"), "a post without hours counts them as zero");
            Assert.That(Text(errors, $"#jfb-1-{Captcha:N}-error"), Is.EqualTo("Check this answer"), "the general words until the captcha has its own (12.x)");
        });

        var hidden = (await DocumentAsync("/inputs/?state=hidden")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            foreach (var alias in Inputs.Select(input => input.Alias))
            {
                var field = Field(hidden, alias);
                Assert.That((field.HasAttribute("hidden"), field.HasAttribute("data-jfb-hidden")), Is.EqualTo((true, true)), alias);
                Assert.That(field.QuerySelectorAll("input, select").Where(control => !control.HasAttribute("disabled")).Select(control => control.OuterHtml), Is.Empty, $"{alias}: nothing of a hidden field is posted");
            }

            var group = Captchas(hidden).Closest("[data-jfb-group]")!;
            Assert.That((group.HasAttribute("hidden"), group.HasAttribute("data-jfb-hidden")), Is.EqualTo((true, true)), "the captcha's group");

            Assert.That(Control(hidden, "cost").TextContent, Is.Empty, "a hidden calculation has no value");
        });
    }

    [Test]
    public async Task ACalculation_ShowsTheValueThePipelineWorksOut_ForTheSameAnswers()
    {
        var key = await PublishFormAsync("renderingSums", null, SumsFields());
        _ = await PagesAsync("sums");

        var first = (await DocumentAsync("/sums/")).QuerySelector("form.jfb-form")!;
        var posted = (await DocumentAsync("/sums/?state=posted")).QuerySelector("form.jfb-form")!;
        var json = new StringBuilder("""{"revision":1,"values":{""");
        json.Append(string.Join(',', new[] { ("price", " 12.50 "), ("size", "10"), ("nights", "3"), ("arrive", "2026-11-02"), ("leave", "2026-11-05") }.Select(pair => $"\"{pair.Item1}\":\"{pair.Item2}\"")));
        json.Append("}}");
        using var response = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var form = (await FormRows.GetAsync(key))!;
        var catalogue = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var stored = (await Entries.ListValuesAsync(await Entries.ListIdsAsync(form.Id, 10))).ToDictionary(value => value.FieldId);

        Assert.Multiple(() =>
        {
            Assert.That(stored[catalogue[Total]].Number, Is.EqualTo(47.5), "the pipeline's total: 12.50 for each of three nights, and 10 for the size");
            Assert.That(Control(posted, "total").TextContent, Is.EqualTo("EUR 47.50"), "the page's, read as the pipeline reads the answers");
            Assert.That(stored[catalogue[Stay]].Number, Is.EqualTo(3));
            Assert.That(Control(posted, "stay").TextContent, Is.EqualTo("3 days"));
            Assert.That(Control(first, "total").TextContent, Is.EqualTo("EUR 0.00"), "an empty number counts as zero, as the pipeline counts it");
            Assert.That(Control(first, "stay").TextContent, Is.Empty, "an empty date leaves the days between without a value");
            Assert.That(Control(first, "nights").GetAttribute("value"), Is.EqualTo("8"), "the middle of 1 to 14, the higher of two steps as near");
        });
    }

    [Test]
    public async Task UnderTheAsteriskStyle_RequiredPartsADateAndTimeStartingNowFarLimitsAQuietSliderFilesAndUnseenFields_RenderAsDecided()
    {
        // 14:47 on 1 November in Auckland.
        await AucklandAsync(new DateTimeOffset(2026, 11, 1, 1, 47, 0, TimeSpan.Zero));
        _ = await PublishFormAsync("renderingParts", PartsForm);
        _ = await PagesAsync("parts");

        var first = (await DocumentAsync("/parts/")).QuerySelector("form.jfb-form")!;
        var posted = (await DocumentAsync("/parts/?state=posted")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            var person = Field(first, "person");
            Assert.That(Text(person, "legend.jfb-label"), Is.EqualTo("Person *"));
            Assert.That(person.QuerySelectorAll(".jfb-part").Select(part => (part.QuerySelector("label")!.InnerHtml, part.QuerySelector("input")!.Id, part.QuerySelector("input")!.GetAttribute("autocomplete"), part.QuerySelector("input")!.HasAttribute("required"))), Is.EqualTo(new[]
            {
                ("Title", "jfb-1-person", "honorific-prefix", false),
                ("""First name <span class="jfb-mark" aria-hidden="true">*</span>""", "jfb-1-person-first", "given-name", true),
                ("Middle name", "jfb-1-person-middle", "additional-name", false),
                ("""Last name <span class="jfb-mark" aria-hidden="true">*</span>""", "jfb-1-person-last", "family-name", true),
            }), "the parts its settings require carry the asterisk, in the type's order whatever the settings' order; the first part carries the field's id");
            var guest = Field(first, "guest");
            Assert.That(guest.QuerySelectorAll(".jfb-part").Select(part => (part.QuerySelector("label")!.InnerHtml, part.QuerySelector("input")!.HasAttribute("required"))), Is.EqualTo(new[] { ("First name", false), ("Last name", false) }), "a name that needs no answer requires none of the parts it would need");
            var host = Field(first, "host");
            Assert.That(Text(host, "legend"), Is.EqualTo("Host *"));
            Assert.That(host.QuerySelectorAll(".jfb-part label").Select(label => label.InnerHtml), Is.EqualTo(new[] { "First name", "Last name" }), "a required name that needs every part it asks for says so once, on its legend");
            Assert.That(host.QuerySelectorAll("input[required]"), Has.Length.EqualTo(2));

            var home = Field(first, "home");
            Assert.That(home.QuerySelectorAll(".jfb-part").Select(part => (part.ClassName, part.QuerySelector("label")!.TextContent)), Is.EqualTo(new[]
            {
                ("jfb-part jfb-part-wide", "Street and number *"), ("jfb-part", "Postcode *"), ("jfb-part", "Town *"), ("jfb-part", "State or region"), ("jfb-part", "Country"),
            }), "a required address without parts listed needs the street, the postcode and the town; the region and the country share a line");
            Assert.That(home.QuerySelector("[name='home.postcode']")!.HasAttribute("autocapitalize"), Is.False, "a postcode keeps its letters as typed");

            Assert.That(Control(first, "arrival").GetAttribute("value"), Is.EqualTo("2026-11-01T14:30"), "now in the site's time zone, back to its step");
            Assert.That(Control(first, "late").HasAttribute("step"), Is.False, "a step that does not divide the day would drift from the server's");
            Assert.That((Control(first, "born").GetAttribute("min"), Control(first, "born").GetAttribute("max")), Is.EqualTo(("1900-01-01", "2026-12-31")));
            Assert.That(Control(first, "far").HasAttribute("max"), Is.False, "a limit that falls outside the calendar sets none");

            Assert.That(Field(first, "volume").QuerySelector(".jfb-range")!.InnerHtml.Trim(), Is.EqualTo("""<input type="range" id="jfb-1-volume" name="volume" min="0" max="100" step="1" value="50">"""), "a slider that does not show its value; its number is what a screen reader says");
            Assert.That(Control(first, "photos").OuterHtml, Is.EqualTo("""<input type="file" id="jfb-1-photos" name="photos" accept=".jpg,.jpeg,.png,.gif,.webp,.avif,.heic,.heif,.tif" multiple="multiple">"""), "its groups' extensions, then its own");
            Assert.That(Field(first, "photos").QuerySelector(".jfb-upload")!.HasAttribute("data-jfb-dropzone"), Is.False);
            Assert.That(Control(first, "drawing").GetAttribute("accept"), Is.EqualTo(".pdf,.dwg"), "each extension once");
            Assert.That(Text(Field(first, "autograph"), "legend"), Is.EqualTo("Autograph"), "a signature that needs no answer, unmarked in this style");
            Assert.That(first.QuerySelectorAll("[data-jfb-field]").Select(field => field.GetAttribute("data-jfb-field")), Does.Not.Contain(Unseen.ToString("D")).And.Not.Contain(Placement.ToString("D")), "a calculation visitors do not see and a captcha's place on a form without a captcha render nothing");

            Assert.That(Field(posted, "person").QuerySelectorAll("input[aria-invalid=true]"), Has.Length.EqualTo(4), "an error with every needed part given is about all of them");
            var address = Field(posted, "home");
            Assert.That(address.QuerySelectorAll(".jfb-part [aria-invalid=true]").Select(part => part.GetAttribute("name")), Is.EqualTo(new[] { "home.postcode", "home.city" }), "the error is on the parts it needs that are empty");
            Assert.That(Control(posted, "volume").GetAttribute("value"), Is.EqualTo("loud"), "a posted value that is no number shows as it is");
            Assert.That(Control(posted, "volume").HasAttribute("aria-valuetext"), Is.False);
        });
    }

    [Test]
    public async Task ThePagesAnswers_GiveARuleAGridRowsAnswer_AndShowAHiddenFieldsPostedAnswerNowhere()
    {
        _ = await PublishFormAsync("renderingRules", definition => definition with { Settings = definition.Settings with { Developer = definition.Settings.Developer with { QueryValues = ["source"] } } }, RulesFields());
        _ = await PagesAsync("rules");

        var first = (await DocumentAsync("/rules/")).QuerySelector("form.jfb-form")!;
        var posted = (await DocumentAsync("/rules/?state=posted")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(first.QuerySelectorAll("[data-jfb-field][hidden]").Select(field => field.GetAttribute("data-jfb-alias")), Is.Empty);
            Assert.That(Text(first, "#jfb-1-note-help"), Is.EqualTo("Tier: ; source: "));
            Assert.That(Field(posted, "praise").HasAttribute("hidden"), Is.True, "a rule reads a grid row's answer by the row");
            Assert.That(Text(posted, "#jfb-1-note-help"), Is.EqualTo("Tier: ; source: "), "a hidden field's posted answer shows nowhere, prefilled from the query or not");
        });
    }

    [Test]
    public async Task ASitesOwnType_RendersItsControlFromItsOwnView_WithItsSettingsAndItsAnswersParts()
    {
        _ = await PublishFormAsync("renderingCustom", null, new FormField
        {
            Key = Name,
            Type = "text",
            Alias = "name",
            Label = new LocalizedText("Name"),
            DefaultValue = FieldDefaultValue.Of("Ada"),
        }, new FormField
        {
            Key = Mood,
            Type = MoodFieldType.TypeAlias,
            Alias = "mood",
            Label = new LocalizedText("Mood"),
            Help = new LocalizedText("Pick the closest"),
            Settings = SettingsObject.Parse("""{"moods":["calm","bright"],"prompt":{"":"How do you feel, {{ field.name }}?"}}"""),
        }, new FormField
        {
            Key = K(0x62),
            Type = MoodFieldType.TypeAlias,
            Alias = "later",
            Label = new LocalizedText("Later"),
            Settings = SettingsObject.Parse("""{"moods":["tired"],"prompt":{"":"And after the talk?"}}"""),
        });
        _ = await PagesAsync("custom");

        var first = (await DocumentAsync("/custom/")).QuerySelector("form.jfb-form")!;
        var answered = (await DocumentAsync("/custom/?state=answered")).QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            var mood = Field(first, "mood");
            Assert.That((string.Join(' ', mood.ClassList.Take(2)), mood.GetAttribute("data-jfb-type")), Is.EqualTo(("jfb-field jfb-field-acme.mood", "acme.mood")));
            Assert.That(mood.QuerySelector("label.jfb-label")!.GetAttribute("for"), Is.EqualTo("jfb-1-mood"), "the package's field around the site's control");
            Assert.That(Compact(mood.QuerySelector("[data-test-custom]")!.OuterHtml), Is.EqualTo(
                """<select id="jfb-1-mood" name="mood" aria-describedby="jfb-1-mood-help" data-test-custom="mood"><option value="calm">calm</option><option value="bright">bright</option></select>"""),
                "its own view, found as a built-in type's is, with its settings");
            Assert.That(Text(mood, ".acme-prompt"), Is.EqualTo("How do you feel, Ada?"), "a text with tags, rendered for the page's answers");
            Assert.That(mood.QuerySelector(".acme-note"), Is.Null);
            Assert.That(Text(Field(first, "later"), ".acme-prompt"), Is.EqualTo("And after the talk?"), "settings without tags as they are written");

            var posted = Field(answered, "mood");
            Assert.That(posted.QuerySelectorAll("option").Cast<IHtmlOptionElement>().Where(option => option.IsSelected).Select(option => option.Value), Is.EqualTo(new[] { "bright" }));
            Assert.That(Text(posted, ".acme-note"), Is.EqualTo("After the talk"), "its answer's parts");
            Assert.That(Text(posted, ".acme-prompt"), Is.EqualTo("How do you feel, Grace?"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().FieldTypes().Append<MoodFieldType>();
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    // The markup contract's hooks on every field of the inputs form (ADR-0015): the field's attributes, a unique id per
    // element, the control's id the error summary links to, a fieldset with its legend for a field with parts, a part's
    // name, and a label for every input and select.
    private static void Hooks(IElement form, string state)
    {
        Assert.Multiple(() =>
        {
            Assert.That(form.QuerySelectorAll("[id]").Select(element => element.Id), Is.Unique, state);
            foreach (var (key, alias, type, grouped) in Inputs)
            {
                var field = Field(form, alias);
                Assert.That((field.GetAttribute("data-jfb-field"), field.GetAttribute("data-jfb-type"), string.Join(' ', field.ClassList.Take(2))), Is.EqualTo((key.ToString("D"), type, "jfb-field jfb-field-" + type)), $"{state}: {alias}");
                Assert.That(field.QuerySelector($"#jfb-1-{alias}"), Is.Not.Null, $"{state}: {alias} has the control's id");
                Assert.That(field.QuerySelector("fieldset > legend.jfb-label") is not null, Is.EqualTo(grouped), $"{state}: {alias}'s fieldset and legend");
                foreach (var control in field.QuerySelectorAll("input, select, output"))
                {
                    Assert.That(form.QuerySelectorAll($"label[for='{control.Id}']"), Has.Length.EqualTo(control.LocalName == "output" && alias == "hours" ? 0 : 1), $"{state}: {alias}'s {control.Id} has its label");
                }
            }

            Assert.That(Field(form, "contact").QuerySelectorAll("input").Select(input => input.GetAttribute("name")), Is.EqualTo(new[] { "contact.first", "contact.last" }), state);
            Assert.That(Field(form, "billing").QuerySelectorAll("input, select").Select(input => input.GetAttribute("name")), Is.EqualTo(new[] { "billing.street", "billing.street2", "billing.postcode", "billing.city", "billing.country" }), state);
            Assert.That(Control(form, "visit").GetAttribute("min"), Is.EqualTo("2026-11-01"), $"{state}: the site's today");
            Assert.That(Captchas(form).GetAttribute("data-jfb-captcha"), Is.EqualTo("hcaptcha"), state);
        });
    }

    // A field of each type with parts, dates and outputs, each with a rule that hides it, and the captcha's place in a
    // group that hides with them; the form uses a captcha.
    private static FormDefinition InputsForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Protection = new FormProtectionSettings { Level = ProtectionLevel.Captcha, Captcha = new CaptchaChoice { Provider = "hcaptcha" } } },
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
                            Field(Of("visit"), "visit", "date", """{"min":{"today":""},"max":{"today":"+14d"},"defaultToday":true}""") with { Required = true, Help = new LocalizedText("Any day in the next two weeks") },
                            Field(Of("start"), "start", "time", """{"min":"09:00","max":"17:30","stepMinutes":30}"""),
                            Field(Of("pickup"), "pickup", "datetime", """{"min":{"today":"+1d"},"max":"2026-12-31","stepMinutes":15}"""),
                            Field(Of("hours"), "hours", "slider", """{"min":"1","max":"8","step":"1","unit":{"":"hours"}}"""),
                            Field(Of("cost"), "cost", "calculation", """{"formula":"hours * 15","format":{"decimals":2,"prefix":{"":"EUR"}}}""") with { Help = new LocalizedText("15 euros an hour") },
                            Field(Of("contact"), "contact", "name", "{}") with { Required = true },
                            Field(Of("billing"), "billing", "address", """{"parts":["street2","country"],"requiredParts":["street","city"]}""") with { Required = true },
                            Field(Of("plan"), "plan", "file", """{"types":["pdf","images"]}"""),
                            Field(Of("signature"), "signature", "signature", "{}") with { Required = true },
                        ],
                    },
                    new FormGroup
                    {
                        Key = K(0x04),
                        Rules = [HideRule(K(0x1ff))],
                        Fields = [new FormField { Key = Captcha, Type = "captcha" }],
                    },
                ],
            },
        ],
    };

    // A price, a size of numbered options, the nights of a slider and two dates, a total and the days between the dates.
    private static FormField[] SumsFields() =>
    [
        Field(Price, "price", "number", """{"decimals":2}""", rules: false),
        Field(Size, "size", "radio", "{}", rules: false) with
        {
            Options = new FieldOptions { Items = [new FieldOption { Value = "0", Label = new LocalizedText("Small") }, new FieldOption { Value = "10", Label = new LocalizedText("Large") }] },
        },
        Field(Nights, "nights", "slider", """{"min":"1","max":"14","step":"1"}""", rules: false),
        Field(Arrive, "arrive", "date", "{}", rules: false),
        Field(Leave, "leave", "date", "{}", rules: false),
        Field(Total, "total", "calculation", """{"formula":"price * nights + size","format":{"decimals":2,"prefix":{"":"EUR"}}}""", rules: false),
        Field(Stay, "stay", "calculation", """{"formula":"daysBetween(arrive, leave)","format":{"decimals":0,"suffix":{"":"days"}}}""", rules: false),
    ];

    // A grid with two rows and a field a rule on a row hides, a hidden field and one prefilled from the query, and a
    // help text that recalls both.
    private static FormField[] RulesFields() =>
    [
        Field(Rating, "rating", "grid", "{}", rules: false) with
        {
            Options = new FieldOptions { Items = [.. new[] { "1", "2", "3" }.Select(value => new FieldOption { Value = value, Label = new LocalizedText(value) })] },
            Rows = [new GridRow { Key = RowA, Label = new LocalizedText("The room") }, new GridRow { Key = RowB, Label = new LocalizedText("The staff") }],
        },
        Field(Tier, "tier", "hidden", "{}", rules: false),
        Field(Source, "source", "hidden", "{}", rules: false) with { Prefill = new FieldPrefill { Query = "source" } },
        Field(K(0x75), "praise", "text", "{}", rules: false) with { Rules = [HiddenWhen(K(0x175), RowA, "3")] },
        Field(K(0x76), "note", "text", "{}", rules: false) with { Help = new LocalizedText("Tier: {{ field.tier }}; source: {{ field.source }}") },
    ];

    // Under the asterisk style: a name of four parts listed out of order with two required, a name that needs no answer
    // with two parts it would need, a required name that needs both its parts, a required address with a region and a country,
    // a date and time that starts now on a step and one whose step does not divide the day, a date between two dates and
    // one with a limit beyond the calendar, a slider that does not show its value, files of several kinds, a signature
    // that needs no answer, a calculation visitors do not see and a captcha's place without a captcha.
    private static FormDefinition PartsForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Required } },
        Pages =
        [
            new FormPage
            {
                Key = K(0x05),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x06),
                        Fields =
                        [
                            Field(Person, "person", "name", """{"parts":["middle","last","title","first"],"requiredParts":["first","last"]}""", rules: false) with { Required = true },
                            Field(Guest, "guest", "name", """{"requiredParts":["first","last"]}""", rules: false),
                            Field(Host, "host", "name", "{}", rules: false) with { Required = true },
                            Field(Home, "home", "address", """{"parts":["region","country"]}""", rules: false) with { Required = true },
                            Field(Arrival, "arrival", "datetime", """{"defaultToday":true,"stepMinutes":30}""", rules: false),
                            Field(Late, "late", "datetime", """{"stepMinutes":7}""", rules: false),
                            Field(Born, "born", "date", """{"min":"1900-01-01","max":"2026-12-31"}""", rules: false),
                            Field(Far, "far", "date", """{"max":{"today":"+9000y"}}""", rules: false),
                            Field(Volume, "volume", "slider", """{"showValue":false}""", rules: false),
                            Field(Photos, "photos", "file", """{"types":["images"],"extensions":["tif"],"maxFiles":5,"dropZone":false}""", rules: false),
                            Field(Drawing, "drawing", "file", """{"types":["pdf"],"extensions":["dwg","pdf"]}""", rules: false),
                            Field(Autograph, "autograph", "signature", "{}", rules: false),
                            Field(Unseen, "unseen", "calculation", """{"formula":"volume * 2","visible":false}""", rules: false),
                            new FormField { Key = Placement, Type = "captcha" },
                        ],
                    },
                ],
            },
        ],
    };

    private static FormField Field(Guid key, string alias, string type, string settings, bool rules = true) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(char.ToUpperInvariant(alias[0]) + alias[1..]),
        Settings = settings == "{}" ? null : SettingsObject.Parse(settings),
        Rules = rules ? [HideRule(K(0x100 + key.ToByteArray()[15]))] : [],
    };

    // A rule that hides its element while the trigger says yes.
    private static FormRule HideRule(Guid key) => HiddenWhen(key, Trigger, "yes");

    // A rule that hides its element while a field, or a grid's row, answers a text.
    private static FormRule HiddenWhen(Guid key, Guid field, string text) => new()
    {
        Key = key,
        Effect = RuleEffect.Hide,
        Conditions = [new RuleCondition { Field = field, Operator = "equals", Value = new ConditionOperand.Text(text) }],
    };

    // The site's time zone, Auckland, and the site's clock at an instant.
    private async Task AucklandAsync(DateTimeOffset now)
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(general.Value with { TimeZone = "Pacific/Auckland" }, general.Version, Editor);
        GetRequiredService<TestClock>().SetNow(now);
    }

    private static Guid Of(string alias) => Inputs.Single(input => input.Alias == alias).Key;

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000062e{number:x3}");

    private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> Parts(params (string Name, string Text)[] parts) =>
        parts.Select(part => new KeyValuePair<string, IEnumerable<string>>(part.Name, [part.Text]));

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{path} answers {(int)response.StatusCode}: {html}");
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    // A select's options as "value: label".
    private static string[] Options(IElement field) => [.. field.QuerySelectorAll("option").Cast<IHtmlOptionElement>().Select(option => $"{option.Value}: {option.Text}")];

    private static IElement Captchas(IElement form) => form.QuerySelector("[data-jfb-type=captcha] > .jfb-captcha") ?? throw new AssertionException("no captcha's place");

    private static IElement Field(IElement form, string alias) => form.QuerySelector($"[data-jfb-field][data-jfb-alias='{alias}']") ?? throw new AssertionException($"no field {alias}");

    private static IElement Control(IElement form, string alias) => form.QuerySelector($"#jfb-1-{alias}") ?? throw new AssertionException($"no control {alias}");

    private static string Text(IElement element, string selector) => ShownText(element.QuerySelector(selector) ?? throw new AssertionException($"no {selector}"));

    // Markup without the line breaks and indentation the views write between elements.
    private static string Compact(string html) => string.Concat(html.Split('\n').Select(line => line.Trim()));
}

/// <summary>
/// A site's own field type for the rendering tests: a mood from the moods in its settings, with a prompt that may recall
/// an answer; its control is the site's view <c>fields/acme.mood.cshtml</c>.
/// </summary>
public sealed class MoodFieldType() : FormBuilderFieldTypeBase<MoodSettings>(new FieldTypeDeclaration
{
    Alias = TypeAlias,
    Name = "Mood",
    Icon = "icon-smiley",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    Features = FieldFeatures.Label | FieldFeatures.Help | FieldFeatures.Required | FieldFeatures.VisibilityRules,
    Messages = FieldMessageKinds.Required,
})
{
    /// <summary>The type's alias.</summary>
    public const string TypeAlias = "acme.mood";
}

/// <summary>The settings of a mood field.</summary>
public sealed class MoodSettings
{
    /// <summary>The moods to choose from.</summary>
    [FormBuilderSetting("Moods", Public = true)]
    public IReadOnlyList<string> Moods { get; init; } = [];

    /// <summary>The prompt above them, which may recall an answer.</summary>
    [FormBuilderSetting("Prompt", SupportsMergeTags = true, Public = true)]
    public LocalizedText? Prompt { get; init; }
}
