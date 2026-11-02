using System.Globalization;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The built-in views on real pages (ROADMAP 6.2b, ADR-0015, ADR-0093): the controls of the text, number and content
/// types, pages, groups and fields in the state their rules give for the default values, the progress, the required
/// marker, the notices of a form that takes no entry now, a post's errors and outcome, and the culture of a German site.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingViewTests : RenderingTestBase
{
    private static readonly Guid FirstName = Key(0x01);
    private static readonly Guid PageTwo = Guid.Parse("30000000-0000-4000-8000-0000000062b2");

    [Test]
    public async Task TheControlsOfTheTextNumberAndContentTypes_RenderInTheStateTheRulesGiveForTheDefaults()
    {
        var image = Image("Reading room", "/media/jfb/room.png", 600, 400);
        var unsized = Image("Floor plan", "/media/jfb/plan.png", 0, null);
        var fileless = Image("Not uploaded", null, 300, 200);
        var key = await PublishFormAsync("renderingKitchen", definition => Kitchen(definition, image, unsized, fileless));
        _ = await PagesAsync("kitchen");
        var (html, document) = await PageAsync("kitchen");

        var form = document.QuerySelector("form.jfb-form")!;
        var pages = form.QuerySelectorAll("section.jfb-page");
        Assert.Multiple(() =>
        {
            Assert.That(pages.Select(page => (page.GetAttribute("data-jfb-page"), page.HasAttribute("hidden"), page.HasAttribute("data-jfb-hidden"))), Is.EqualTo(new[]
            {
                (Key(0x10).ToString("D"), false, false),
                (PageTwo.ToString("D"), true, false),
                (Key(0x30).ToString("D"), true, true),
            }), "the first page is current; a later page waits hidden; a page a rule hides says so");
            Assert.That(Text(form, ".jfb-progress-steps .jfb-progress-text"), Is.EqualTo("Step 1 of 2"), "a page the rules hide is no step");
            Assert.That(form.QuerySelectorAll(".jfb-progress-steps li").Select(step => (step.TextContent, step.GetAttribute("aria-current"))), Is.EqualTo(new[] { ("Your details", "step"), ("Page 2", (string?)null) }));
            Assert.That(((AngleSharp.Html.Dom.IHtmlTemplateElement)form.QuerySelector(".jfb-progress-steps template[data-jfb-steps]")!).Content.QuerySelectorAll("li[data-jfb-step]"), Has.Length.EqualTo(1), "its step waits for a rule to show the page");
            Assert.That(form.QuerySelector(".jfb-progress-steps .jfb-progress-text")!.GetAttribute("data-jfb-words"), Is.EqualTo("Step {0} of {1}"), "the words the runtime goes on with");
            Assert.That(form.QuerySelector(".jfb-progress-steps ol")!.GetAttribute("data-jfb-untitled"), Is.EqualTo("Page {0}"));
            Assert.That(form.QuerySelectorAll(".jfb-progress-steps [data-jfb-numbered]").Select(step => step.TextContent), Is.EqualTo(new[] { "Page 2" }), "a step without a title of its own");
            Assert.That(((AngleSharp.Html.Dom.IHtmlTemplateElement)form.QuerySelector(":scope > template[data-jfb-templates]")!).Content.QuerySelectorAll("[data-jfb-nav]").Select(button => (button.GetAttribute("value"), button.TextContent.Trim())), Is.EqualTo(new[] { ("next", "Next"), ("submit", "Send"), ("back", "Back") }), "every button a move may show, once");
            Assert.That(form.QuerySelector(".jfb-progress-steps ol")!.GetAttribute("aria-label"), Is.EqualTo("Progress"));
            Assert.That(form.QuerySelector(".jfb-progress-steps ol")!.GetAttribute("role"), Is.EqualTo("list"), "a list the themes draw without markers says it is one");
            Assert.That(form.QuerySelector("p.jfb-help")!.InnerHtml, Is.EqualTo("""Questions marked <span aria-hidden="true">*</span><span class="jfb-visually-hidden">with an asterisk</span> need an answer."""));
            Assert.That(Text(pages[0], "h2.jfb-title"), Is.EqualTo("Your details"));
            Assert.That(pages[0].QuerySelector(".jfb-intro")!.InnerHtml, Is.EqualTo("<p>Tell us who you are.</p>"));
            Assert.That(form.QuerySelector(".jfb-navigation")!.InnerHtml.Trim(), Is.EqualTo("""<button type="submit" class="jfb-button jfb-button-primary" name="jfb-nav" value="next" data-jfb-nav="next">Next</button>"""), "Next only, on the first of two pages");
            Assert.That(form.GetAttribute("enctype"), Is.Null);
        });

        var card = pages[0].QuerySelector("fieldset.jfb-group")!;
        Assert.Multiple(() =>
        {
            Assert.That(card.GetAttribute("class"), Is.EqualTo("jfb-group jfb-columns-2 jfb-group-card"));
            Assert.That(Text(card, "legend"), Is.EqualTo("How we reach you"));
            Assert.That(card.GetAttribute("aria-describedby"), Is.EqualTo($"jfb-1-{Key(0x11):N}-help"));
            Assert.That(card.QuerySelector($"#jfb-1-{Key(0x11):N}-help")!.InnerHtml, Is.EqualTo("<p>We confirm by email.</p>"));
            Assert.That(pages[0].QuerySelector("div.jfb-group")!.GetAttribute("class"), Is.EqualTo("jfb-group jfb-columns-1"), "a group without a title is no fieldset");
        });

        var first = Field(form, "firstName");
        Assert.Multiple(() =>
        {
            Assert.That(first.GetAttribute("class"), Is.EqualTo("jfb-field jfb-field-text jfb-span-2 vip"));
            Assert.That(first.QuerySelector("label")!.OuterHtml, Is.EqualTo("""<label class="jfb-label" for="jfb-1-firstName">First name <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required">*</span></label>"""));
            Assert.That(first.QuerySelector("#jfb-1-firstName-help")!.TextContent, Is.EqualTo("As on your card"));
            Assert.That(first.QuerySelector(".jfb-affix")!.InnerHtml.Replace("\n", string.Empty, StringComparison.Ordinal).Replace("  ", string.Empty, StringComparison.Ordinal), Is.EqualTo(
                """<span id="jfb-1-firstName-prefix">Dr</span><input class="jfb-control" type="text" id="jfb-1-firstName" name="firstName" value="Ada" placeholder="Your first name" autocomplete="given-name" required="required" data-jfb-requires="" aria-describedby="jfb-1-firstName-help jfb-1-firstName-prefix jfb-1-firstName-suffix jfb-1-firstName-count"><span id="jfb-1-firstName-suffix">(given)</span>"""));
            Assert.That(first.QuerySelector("p.jfb-counter")!.OuterHtml, Is.EqualTo("""<p class="jfb-counter" id="jfb-1-firstName-count" data-jfb-limit="40" data-jfb-left="{0} characters left" data-jfb-left-one="{0} character left" data-jfb-too-many="{0} characters too many" data-jfb-too-many-one="{0} character too many">Up to 40 characters</p>"""));
            Assert.That(Control(form, "nickname").HasAttribute("value"), Is.False, "a default with a merge tag is the browser's to fill");
            Assert.That(Control(form, "email").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="email" id="jfb-1-email" name="email" autocomplete="email" spellcheck="false" required="required" data-jfb-requires="">"""));
            Assert.That(Field(form, "email").QuerySelector("label[for=jfb-1-email-confirm]")!.TextContent, Is.EqualTo("Email again *"));
            Assert.That(form.QuerySelector("#jfb-1-email-confirm")!.OuterHtml, Is.EqualTo("""<input class="jfb-control" type="email" id="jfb-1-email-confirm" name="email.confirm" autocomplete="email" spellcheck="false" required="required" data-jfb-requires="">"""));
            Assert.That(Control(form, "phone").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="tel" id="jfb-1-phone" name="phone" autocomplete="tel" data-jfb-requires="">"""));
            Assert.That(ShownText(Field(form, "phone").QuerySelector("label")!), Is.EqualTo("Phone"), "the asterisk style marks only required fields");
            Assert.That(Field(form, "phone").QuerySelector("label .jfb-mark")!.OuterHtml, Is.EqualTo("""<span class="jfb-mark" aria-hidden="true" data-jfb-mark="required" hidden="">*</span>"""), "a form with rules carries it hidden for while a rule requires the field");
            Assert.That(Control(form, "website").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="url" id="jfb-1-website" name="website" autocomplete="url" spellcheck="false" data-jfb-requires="">"""));
            Assert.That(Control(form, "about").OuterHtml, Is.EqualTo("""<textarea class="jfb-control" id="jfb-1-about" name="about" rows="3" data-jfb-requires="" aria-describedby="jfb-1-about-count">First line</textarea>"""));
            Assert.That(Text(Field(form, "about"), "p.jfb-counter"), Is.EqualTo("Up to 200 characters"));
            Assert.That(Control(form, "about").HasAttribute("data-jfb-grow"), Is.False, "its rows stay with autoGrow off");
            Assert.That(Control(form, "story").HasAttribute("data-jfb-grow"), Is.True, "autoGrow is on by default");
            Assert.That(Text(Field(form, "story"), "p.jfb-counter"), Is.EqualTo("Up to 1,500 characters"), "in the page's numbers, as the runtime goes on");
            Assert.That(Control(form, "guests").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="number" id="jfb-1-guests" name="guests" inputmode="numeric" min="1" max="40" step="1" data-jfb-requires="" aria-describedby="jfb-1-guests-suffix">"""));
            Assert.That(Text(Field(form, "guests"), "#jfb-1-guests-suffix"), Is.EqualTo("people"));
            Assert.That(Control(form, "budget").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="text" id="jfb-1-budget" name="budget" inputmode="decimal" data-jfb-requires="" aria-describedby="jfb-1-budget-prefix">"""), "a number with places is typed as text");
            Assert.That(Control(form, "temperature").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="number" id="jfb-1-temperature" name="temperature" data-jfb-requires="">"""), "a number below zero needs the full keyboard");
            Assert.That(Control(form, "floor").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="number" id="jfb-1-floor" name="floor" min="-2" max="12" data-jfb-requires="">"""), "so does one whose least is below zero");
        });

        var content = pages[1];
        Assert.Multiple(() =>
        {
            Assert.That(content.QuerySelector("[data-jfb-type=heading]")!.InnerHtml.Trim(), Is.EqualTo("""<h3 class="jfb-heading">Before you arrive</h3>"""));
            Assert.That(content.QuerySelector("[data-jfb-type=heading]")!.HasAttribute("data-jfb-alias"), Is.False, "a field without an alias says none");
            Assert.That(content.QuerySelector("[data-jfb-type=paragraph] .jfb-text")!.InnerHtml, Is.EqualTo("<p>The side door opens early.</p>"), "sanitised");
            Assert.That(content.QuerySelector("[data-jfb-type=divider]")!.InnerHtml.Trim(), Is.EqualTo("""<hr class="jfb-divider">"""));
            Assert.That(content.QuerySelector("[data-jfb-type=image] figure")!.OuterHtml.Replace("\n", string.Empty, StringComparison.Ordinal).Replace("  ", string.Empty, StringComparison.Ordinal), Is.EqualTo(
                """<figure class="jfb-image jfb-image-large"><img src="/media/jfb/room.png" alt="Six tables under tall windows" width="600" height="400" loading="lazy" decoding="async"></figure>"""));
            Assert.That(content.QuerySelector("[data-jfb-type=image] label"), Is.Null, "a field without a value has no label");
            Assert.That(content.QuerySelector($"[data-jfb-field='{Key(0x15):D}']")!.InnerHtml.Trim(), Is.Empty, "an image whose media item is gone shows nothing");
            Assert.That(content.QuerySelector($"[data-jfb-field='{Key(0x18):D}']")!.InnerHtml.Trim(), Is.Empty, "nor one whose media item has no file");
            Assert.That(content.QuerySelector($"[data-jfb-field='{Key(0x16):D}'] img")!.OuterHtml, Is.EqualTo("""<img src="/media/jfb/plan.png" alt="The floor plan" loading="lazy" decoding="async">"""), "no size without one");
            Assert.That(Attributes(Field(form, "secret")), Does.ContainKey("hidden").And.ContainKey("data-jfb-hidden"), "a rule hides it for the default answers");
            Assert.That(Control(form, "secret").HasAttribute("disabled"), Is.True, "so it is never posted");
            Assert.That(Control(form, "cardNumber").HasAttribute("required"), Is.True, "a rule requires it for the default answers");
            Assert.That(Control(form, "cardNumber").HasAttribute("disabled"), Is.False);
            Assert.That(Control(form, "hiddenPage").HasAttribute("disabled"), Is.True, "a field of a page a rule hides is never posted");
            Assert.That(Attributes(Field(form, "hiddenPage")), Does.ContainKey("hidden").And.ContainKey("data-jfb-hidden"), "the rules hide what a hidden page holds");
        });

        using var answer = await GetAsync($"{Root}/forms/{key}");
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(form.QuerySelector("script[data-jfb-model]")!.TextContent), await JsonAsync(answer)), Is.True, "the model data is the form endpoint's answer");
        Assert.That(html, Does.Not.Contain("<script>alert"));
    }

    [Test]
    public async Task AFormThatTakesNoEntryNow_ShowsItsNotice_InPlaceOfTheForm()
    {
        var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2099, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        _ = await PublishFormAsync("renderingClosed", definition => Availability(definition, new FormAvailabilitySettings { ClosesAt = past }));
        _ = await PublishFormAsync("renderingMessage", definition => Availability(definition, new FormAvailabilitySettings { ClosesAt = past, ClosedMessage = new LocalizedHtml("<h2>Bookings have closed</h2><p>See you in spring.</p>") }));
        _ = await PublishFormAsync("renderingLater", definition => Availability(definition, new FormAvailabilitySettings { OpensAt = later }));
        _ = await PagesAsync("notices");
        var (_, document) = await PageAsync("notices");

        var roots = document.QuerySelectorAll("div.jfb-form");
        Assert.Multiple(() =>
        {
            Assert.That(document.QuerySelectorAll("form"), Is.Empty);
            Assert.That(roots.Select(root => root.Id), Is.EqualTo(new[] { "jfb-1", "jfb-2", "jfb-3" }));
            Assert.That(Attributes(roots[0]).Keys, Is.EqualTo(new[] { "class", "id", "data-jfb-form", "data-jfb-contract", "lang", "dir" }));
            Assert.That(Compact(roots[0].InnerHtml), Is.EqualTo("""<div class="jfb-notice"><h2>This form is closed</h2><p>It no longer takes answers.</p></div>"""));
            Assert.That(Compact(roots[1].InnerHtml), Is.EqualTo("""<div class="jfb-notice"><h2>Bookings have closed</h2><p>See you in spring.</p></div>"""), "the form's own message, in place of the package's");
            Assert.That(Text(roots[2], ".jfb-notice h2"), Is.EqualTo("This form opens on " + later.ToString("f", CultureInfo.GetCultureInfo("en-US"))), "in the site's time zone, UTC here");
            Assert.That(Text(roots[2], ".jfb-notice p"), Is.EqualTo("Come back then to fill it in."));
            Assert.That(document.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "a notice brings the assets too");
        });
    }

    [Test]
    public async Task ASitesOwnNoticeView_CanTellWhyItShows()
    {
        var later = new DateTime(2099, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        _ = await PublishFormAsync("renderingKind", definition => Availability(definition, new FormAvailabilitySettings { OpensAt = later }));
        _ = await PagesAsync("notice-kind");
        var (_, document) = await PageAsync("notice-kind");

        Assert.That(document.QuerySelector("[data-test-kind]")?.GetAttribute("data-test-kind"), Is.EqualTo("NotOpenYet"));
    }

    [Test]
    public async Task AProgressWithOnePageToShow_IsWrittenHidden_WithTheStepOfTheOtherForARuleToShow()
    {
        _ = await PublishFormAsync("renderingOnePage", definition => definition with
        {
            Pages =
            [
                new FormPage { Key = Key(0x60), Groups = [new FormGroup { Key = Key(0x61), Fields = [Text(FirstName, "first", "First") with { DefaultValue = FieldDefaultValue.Of("Ada") }] }] },
                new FormPage { Key = PageTwo, Rules = [Rule(0x62, RuleEffect.Hide)], Groups = [new FormGroup { Key = Key(0x63), Fields = [Text(Key(0x64), "city", "City")] }] },
            ],
        });
        _ = await PagesAsync("one-page");
        var (_, document) = await PageAsync("one-page");

        var form = document.QuerySelector("form.jfb-form")!;
        var steps = form.QuerySelector(".jfb-progress-steps")!;
        Assert.Multiple(() =>
        {
            Assert.That(steps.HasAttribute("hidden"), Is.True, "no steps to speak of, until a rule shows the other page");
            Assert.That(steps.QuerySelectorAll("ol > li"), Has.Length.EqualTo(1));
            Assert.That(((AngleSharp.Html.Dom.IHtmlTemplateElement)steps.QuerySelector("template[data-jfb-steps]")!).Content.QuerySelector("li")!.GetAttribute("data-jfb-step"), Is.EqualTo(PageTwo.ToString("D")));
            Assert.That(form.QuerySelector(".jfb-navigation [data-jfb-nav='submit']"), Is.Not.Null, "the one page sends");
        });
    }

    [Test]
    public async Task AFormRenderedAgainAfterAFailedPost_ShowsItsAnswersErrorsNoticeAndPage()
    {
        _ = await PublishFormAsync("renderingPosted", Posted);
        _ = await PagesAsync("post-errors");
        var (_, document) = await PageAsync("post-errors");

        var form = document.QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(form.QuerySelectorAll("section.jfb-page").Select(page => page.HasAttribute("hidden")), Is.EqualTo(new[] { true, false }), "the post's page");
            Assert.That(Compact(form.QuerySelector(".jfb-progress-bar")!.OuterHtml), Is.EqualTo("""<div class="jfb-progress jfb-progress-bar"><p class="jfb-progress-text" id="jfb-1-progress" data-jfb-words="Step {0} of {1}">Step 2 of 2</p><progress max="2" value="2" aria-labelledby="jfb-1-progress">Step 2 of 2</progress></div>"""));
            Assert.That(Compact(form.QuerySelector(".jfb-notice")!.OuterHtml), Is.EqualTo("""<div class="jfb-notice"><h2>The form has changed</h2><p>Check your answers, then send them again.</p></div>"""));
            Assert.That(Compact(form.QuerySelector(".jfb-error-summary")!.OuterHtml), Is.EqualTo("""<div class="jfb-error-summary" data-jfb-summary="" role="alert" tabindex="-1" aria-labelledby="jfb-1-summary"><h2 id="jfb-1-summary">This answer needs a change</h2><ul role="list"><li><a href="#jfb-1-name">Enter your whole name</a></li></ul></div>"""), "the field's own message for the kind, as written");
            Assert.That(Control(form, "name").OuterHtml, Is.EqualTo("""<input class="jfb-control" type="text" id="jfb-1-name" name="name" value="Bob <b>" aria-invalid="true" aria-describedby="jfb-1-name-error">"""));
            Assert.That(Field(form, "name").QuerySelector("p.jfb-error")!.OuterHtml, Is.EqualTo("""<p class="jfb-error" id="jfb-1-name-error" data-jfb-error="">Enter your whole name</p>"""));
            Assert.That(Compact(form.QuerySelector(".jfb-navigation")!.InnerHtml), Is.EqualTo(
                """<button type="submit" class="jfb-button jfb-button-primary" name="jfb-nav" value="submit" data-jfb-nav="submit">Book it</button><button type="submit" class="jfb-button" name="jfb-nav" value="back" data-jfb-nav="back"><span class="jfb-icon jfb-icon-back" aria-hidden="true"></span>Previous</button>"""),
                "the form's own labels, Send first so Enter sends");
        });
    }

    [Test]
    public async Task AFormWithoutTheValidationSummary_ShowsAPostsErrorsInline_Only()
    {
        _ = await PublishFormAsync("renderingQuiet", definition => definition with
        {
            Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { ValidationSummary = false } },
        });
        _ = await PagesAsync("post-quiet");
        var (_, document) = await PageAsync("post-quiet");

        var form = document.QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(form.QuerySelectorAll(".jfb-error-summary"), Is.Empty);
            Assert.That(form.QuerySelectorAll(".jfb-progress"), Is.Empty, "a form of one page shows no progress");
            Assert.That(Text(Field(form, "name"), "p.jfb-error"), Is.EqualTo("Enter an answer"), "the default");
            Assert.That(Control(form, "name").GetAttribute("aria-invalid"), Is.EqualTo("true"));
        });
    }

    [Test]
    public async Task AFormAfterAPostThatSentIt_ShowsTheOutcome_InPlaceOfTheForm()
    {
        _ = await PublishFormAsync("renderingPosted", Posted);
        _ = await PagesAsync("post-sent");
        var (_, document) = await PageAsync("post-sent");

        Assert.Multiple(() =>
        {
            Assert.That(document.QuerySelectorAll("form"), Is.Empty);
            Assert.That(Compact(document.QuerySelector("div.jfb-form")!.InnerHtml), Is.EqualTo(
                """<div class="jfb-outcome" data-jfb-outcome="" data-jfb-pending="" tabindex="-1"><h2>Thank you, Bob</h2><p>We write back within a day.</p><p class="jfb-reference" data-jfb-reference="">Your reference is <strong>REQ-2026-00042</strong></p></div>"""));
        });
    }

    [Test]
    public async Task OnAGermanSite_AFormRendersInGerman_AndItsModelDataIsTheEndpointsAnswerForThatCulture()
    {
        var key = await PublishFormAsync("renderingContact", null, NameField, Text(Key(0x60), "city", "City") with { Required = true });
        var page = await CultureSiteAsync("de-DE", "German");
        var (_, document) = await PageAsync("culture");
        using var answer = await GetAsync($"{Root}/forms/{key}?contentKey={page}&culture=de-DE");

        var form = document.QuerySelector("form.jfb-form")!;
        Assert.Multiple(async () =>
        {
            Assert.That(form.GetAttribute("lang"), Is.EqualTo("de-DE"));
            Assert.That(form.GetAttribute("dir"), Is.EqualTo("ltr"));
            Assert.That(Field(form, "name").QuerySelector("label")!.TextContent, Is.EqualTo("Name (de) (optional)"));
            Assert.That(Field(form, "city").QuerySelector("label")!.TextContent, Is.EqualTo("City"), "the style marks only what is not required");
            Assert.That(Text(form, ".jfb-navigation button"), Is.EqualTo("Senden"));
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(form.QuerySelector("script[data-jfb-model]")!.TextContent), await JsonAsync(answer)), Is.True);
        });
    }

    [Test]
    public async Task OnAnArabicSite_AFormIsRightToLeft_WithTheEnglishWordsForWantOfItsOwn()
    {
        _ = await PublishFormAsync("renderingContact");
        _ = await CultureSiteAsync("ar-SA", "Arabic");
        var (_, document) = await PageAsync("culture");

        var form = document.QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(form.GetAttribute("lang"), Is.EqualTo("ar-SA"));
            Assert.That(form.GetAttribute("dir"), Is.EqualTo("rtl"));
            Assert.That(Text(form, ".jfb-navigation button"), Is.EqualTo("Send"));
        });
    }

    private static Guid Key(int n) => Guid.Parse($"30000000-0000-4000-8000-0000000062{n:x2}");

    private static FormDefinition Availability(FormDefinition definition, FormAvailabilitySettings availability) =>
        definition with { Settings = definition.Settings with { Availability = availability } };

    private static FormField Text(Guid key, string alias, string label, string? settings = null) => new()
    {
        Key = key,
        Type = "text",
        Alias = alias,
        Label = new LocalizedText(label),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static FormField Typed(Guid key, string type, string? alias, string? label, string? settings = null) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = label is null ? null : new LocalizedText(label),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static FormRule Rule(int key, RuleEffect effect) => new()
    {
        Key = Key(key),
        Effect = effect,
        Conditions = [new RuleCondition { Field = FirstName, Operator = "equals", Value = new ConditionOperand.Text("Ada") }],
    };

    // Three pages: the details with a card group and a plain one, the content types with fields rules hide and
    // require, and a page a rule hides; asterisks mark the required fields.
    private static FormDefinition Kitchen(FormDefinition definition, Guid image, Guid unsized, Guid fileless) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Required } },
        Pages =
        [
            new FormPage
            {
                Key = Key(0x10),
                Title = new LocalizedText("Your details"),
                Description = new LocalizedHtml("<p>Tell us who you are.</p>"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(0x11),
                        Title = new LocalizedText("How we reach you"),
                        Description = new LocalizedHtml("<p>We confirm by email.</p>"),
                        Columns = 2,
                        Style = GroupStyle.Card,
                        Fields =
                        [
                            Text(FirstName, "firstName", "First name", """{"prefix":{"":"Dr"},"suffix":{"":"(given)"},"counter":true,"maxLength":40}""") with
                            {
                                Required = true,
                                Span = 2,
                                CssClass = "vip",
                                Help = new LocalizedText("As on your card"),
                                Placeholder = new LocalizedText("Your first name"),
                                Autocomplete = "given-name",
                                DefaultValue = FieldDefaultValue.Of("Ada"),
                            },
                            Text(Key(0x02), "nickname", "Nickname") with { DefaultValue = FieldDefaultValue.Of("{{ form.name }}") },
                            Typed(Key(0x03), "email", "email", "Email", """{"confirm":true,"confirmLabel":{"":"Email again"}}""") with { Required = true },
                            Typed(Key(0x04), "phone", "phone", "Phone"),
                            Typed(Key(0x05), "url", "website", "Website"),
                        ],
                    },
                    new FormGroup
                    {
                        Key = Key(0x12),
                        Fields =
                        [
                            Typed(Key(0x06), "textarea", "about", "About you", """{"rows":3,"counter":true,"maxLength":200,"autoGrow":false}""") with { DefaultValue = FieldDefaultValue.Of("First line") },
                            Typed(Key(0x7e), "textarea", "story", "Your story", """{"rows":2,"counter":true,"maxLength":1500}"""),
                            Typed(Key(0x07), "number", "guests", "Guests", """{"min":"1","max":"40","step":"1","suffix":{"":"people"}}"""),
                            Typed(Key(0x08), "number", "budget", "Budget", """{"decimals":2,"min":"0","prefix":{"":"EUR"}}"""),
                            Typed(Key(0x09), "number", "temperature", "Temperature"),
                            Typed(Key(0x17), "number", "floor", "Floor", """{"min":"-2","max":"12"}"""),
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = PageTwo,
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(0x21),
                        Fields =
                        [
                            Typed(Key(0x0a), "heading", null, null, """{"text":{"":"Before you arrive"},"level":3}"""),
                            Typed(Key(0x0b), "paragraph", null, null, """{"html":{"":"<p>The side door opens early.<script>alert(1)</script></p>"}}"""),
                            Typed(Key(0x0c), "divider", null, null),
                            Typed(Key(0x0d), "image", null, null, $$"""{"media":"{{image}}","alt":{"":"Six tables under tall windows"},"size":"large"}"""),
                            Typed(Key(0x15), "image", null, null, """{"media":"30000000-0000-4000-8000-0000000062ff","alt":{"":"Deleted"}}"""),
                            Typed(Key(0x16), "image", null, null, $$"""{"alt":{"":"The floor plan"},"media":"{{unsized}}"}"""),
                            Typed(Key(0x18), "image", null, null, $$"""{"alt":{"":"Coming soon"},"media":"{{fileless}}"}"""),
                            Text(Key(0x0e), "secret", "Secret") with { Rules = [Rule(0x40, RuleEffect.Hide)] },
                            Text(Key(0x0f), "cardNumber", "Card number") with { Rules = [Rule(0x41, RuleEffect.Require)] },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = Key(0x30),
                Title = new LocalizedText("Not for Ada"),
                Rules = [Rule(0x42, RuleEffect.Hide)],
                Groups = [new FormGroup { Key = Key(0x31), Fields = [Text(Key(0x32), "hiddenPage", "On a hidden page")] }],
            },
        ],
    };

    // Two pages with a bar and the form's own labels: the name, then the city.
    private static FormDefinition Posted(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with
        {
            Submission = definition.Settings.Submission with { Progress = ProgressIndicator.Bar, SubmitLabel = new LocalizedText("Book it"), BackLabel = new LocalizedText("Previous") },
            Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.None },
        },
        Pages =
        [
            new FormPage { Key = Key(0x50), Groups = [new FormGroup { Key = Key(0x51), Fields = [NameField with { Messages = new FieldMessages { MinLength = new LocalizedText("Enter your whole name") } }] }] },
            new FormPage { Key = PageTwo, Groups = [new FormGroup { Key = Key(0x52), Fields = [Text(Key(0x53), "city", "City")] }] },
        ],
    };

    // A site in one culture, whose pages are the test pages, and the key of its page "culture".
    private async Task<Guid> CultureSiteAsync(string culture, string name) => (await CulturePagesAsync(culture, name, "culture"))["culture"];

    // An image in the media library: its file, its width and its height, each if any.
    private Guid Image(string name, string? file, int width, int? height)
    {
        var media = GetRequiredService<IMediaService>();
        var image = media.CreateMedia(name, Constants.System.Root, Constants.Conventions.MediaTypes.Image);
        if (file is not null)
        {
            image.SetValue(Constants.Conventions.Media.File, $$"""{"src":"{{file}}"}""");
        }

        image.SetValue(Constants.Conventions.Media.Width, width);
        if (height is { } pixels)
        {
            image.SetValue(Constants.Conventions.Media.Height, pixels);
        }

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
