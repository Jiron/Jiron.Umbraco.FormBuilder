using System.Globalization;
using System.Net;
using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// <c>GET forms/{key}/render</c> behind Umbraco's website output cache (ROADMAP 6.2l, ADR-0004, ADR-0104): the published
/// form's markup as the page shows it at the place the request names, with the given instance id, in the page's
/// culture, for anyone or for the member signed in, in the theme it names, else the form's (ADR-0105); never an outcome;
/// <c>no-store</c>; and its refusals. The site reads no
/// culture from the query string, as a site may choose, so the culture render renders in is its own doing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RenderEndpointTests : OutputCacheTestBase
{
    [Test]
    public async Task Render_ReturnsThePublishedForm_WithTheGivenInstanceId_AsThePageShowsIt()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName with { Label = new LocalizedText("Name on {{ page.name }}") });
        var content = (await PagesAsync(CachePage))[CachePage];
        var culture = await GetRequiredService<ILanguageService>().GetDefaultIsoCodeAsync();
        var page = await ServedAsync(Client, "/cache/");

        var same = await RenderAsync(key, "jfb-1", "/cache/", content, culture);
        var seventh = await RenderAsync(key, "jfb-7", "/cache/", content, culture);
        await SaveAndPublishAsync(key, definition => WithFields(definition, RequiredName with { Label = new LocalizedText("Your full name") }));
        var changed = await RenderAsync(key, "jfb-1", "/cache/", content, culture);
        var stale = await ServedAsync(Visitor(), "/cache/");

        Assert.Multiple(() =>
        {
            Assert.That((same.Status, same.ContentType, same.Served.CacheControl, same.Served.SetsCookie), Is.EqualTo((HttpStatusCode.OK, "text/html; charset=utf-8", "no-store", false)));
            Assert.That(same.Served.Document.Body!.Children.Select(element => element.LocalName + "#" + element.Id), Is.EqualTo(new[] { "form#jfb-1" }), "the form alone");
            Assert.That(same.Served.Document.QuerySelectorAll("link, script[src]"), Is.Empty, "without the asset tags, which the page has");
            Assert.That(Markup(same.Served.Document.QuerySelector("form#jfb-1")!), Is.EqualTo(Markup(page.Document.QuerySelector("form#jfb-1")!)), "the page's markup, but for Umbraco's route token, which is new on every render");
            Assert.That(same.Served.Document.QuerySelector("label[for='jfb-1-name']")!.TextContent, Does.Contain("Name on cache"), "a visitor text that recalls the page, for the page the request names");
            Assert.That(seventh.Served.Document.QuerySelector("form#jfb-7")?.GetAttribute("action"), Is.EqualTo("/cache/#jfb-7"));
            Assert.That(seventh.Served.Document.QuerySelector("input#jfb-7-name"), Is.Not.Null, "its ids below the instance");
            Assert.That(changed.Served.Document.QuerySelector("form#jfb-1")!.GetAttribute("data-jfb-revision"), Is.EqualTo("2"), "the form as published now");
            Assert.That(changed.Served.Document.QuerySelector("label[for='jfb-1-name']")!.TextContent, Does.Contain("Your full name"));
            Assert.That((stale.FromCache, stale.Document.QuerySelector("form#jfb-1")!.GetAttribute("data-jfb-revision")), Is.EqualTo((true, "1")), "the page the runtime swaps it into came from the cache");
        });
    }

    [Test]
    public async Task Render_OfALargeForm_WritesItsWholeMarkup()
    {
        var help = string.Concat(Enumerable.Repeat("A help text long enough to fill the response's writer. ", 12));
        FormField[] fields = [.. Enumerable.Range(0, 60).Select(at => new FormField
        {
            Key = Guid.Parse($"30000000-0000-4000-8000-00000070{at:x4}"),
            Type = "text",
            Alias = "field" + at.ToString(CultureInfo.InvariantCulture),
            Label = new LocalizedText("Field " + at.ToString(CultureInfo.InvariantCulture)),
            Help = new LocalizedText(help),
        })];
        var key = await PublishFormAsync("renderingLarge", null, fields);
        var content = (await PagesAsync(CachePage))[CachePage];

        var rendered = await RenderAsync(key, "jfb-1", "/cache/", content, null);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Served.Html.Length, Is.GreaterThan(64 * 1024), "past the response writer's buffer");
            Assert.That(rendered.Served.Document.QuerySelectorAll("form#jfb-1 [data-jfb-field]"), Has.Length.EqualTo(60), "every field, written without a synchronous write the server refuses");
        });
    }

    [Test]
    public async Task Render_InThePagesCulture_IsTheGermanPagesMarkup()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName);
        var content = (await CulturePagesAsync("de-DE", "German", CachePage))[CachePage];
        var page = await ServedAsync(Client, "/cache/");

        var german = await RenderAsync(key, "jfb-1", "/cache/", content, "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(german.Served.Document.QuerySelector("form#jfb-1")!.GetAttribute("lang"), Is.EqualTo("de-DE"));
            Assert.That(german.Served.Document.QuerySelector("[data-test-culture]")!.GetAttribute("data-test-culture"), Is.EqualTo("de-DE de-DE"), "a site's own view renders in the page's culture, as on the page");
            Assert.That(Markup(german.Served.Document.QuerySelector("form#jfb-1")!), Is.EqualTo(Markup(page.Document.QuerySelector("form#jfb-1")!)));
        });
    }

    [Test]
    public async Task Render_OfAFormForMembers_ShowsAVisitorItsNotice_AndAMemberTheForm()
    {
        var key = await PublishFormAsync(CacheForm, definition => definition with { Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } } }, RequiredName);
        var content = (await PagesAsync(CachePage))[CachePage];

        var visitor = await RenderAsync(key, "jfb-1", "/cache/", content, null);
        await SignInMemberAsync("render-member@example.com");
        var member = await RenderAsync(key, "jfb-1", "/cache/", content, null);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.Served.Document.QuerySelector("#jfb-1 .jfb-notice"), Is.Not.Null);
            Assert.That(visitor.Served.Document.QuerySelector("form"), Is.Null);
            Assert.That(member.Served.Document.QuerySelector("form#jfb-1"), Is.Not.Null);
            Assert.That((visitor.Served.CacheControl, member.Served.CacheControl), Is.EqualTo(("no-store", "no-store")));
        });
    }

    [Test]
    public async Task Render_ShowsTheOutcome_ForTheTokenOfASend_AndOnlyInItsOwnInstance()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName);
        var content = (await PagesAsync(CachePage))[CachePage];
        var page = await ServedAsync(Client, "/cache/");
        var sent = await PostAsync(Client, "/cache/", Sent(page.Document.QuerySelector("form#jfb-1")!, ("name", "Ada")));
        var token = sent.Location!.Split('?')[1].Split('#')[0];

        var rendered = await RenderAsync(key, "jfb-1", "/cache/", content, null, "&" + token);
        var elsewhere = await RenderAsync(key, "jfb-2", "/cache/", content, null, "&" + token);
        var forged = await RenderAsync(key, "jfb-1", "/cache/", content, null, "&jfb-sent=jfb-1.forged");

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Served.Document.QuerySelector("#jfb-1 [data-jfb-outcome]"), Is.Not.Null, "the page's way to the outcome after a send (ADR-0155)");
            Assert.That(rendered.Served.Document.QuerySelector("form#jfb-1"), Is.Null);
            Assert.That(elsewhere.Served.Document.QuerySelector("[data-jfb-outcome]"), Is.Null, "the token names its instance");
            Assert.That(elsewhere.Served.Document.QuerySelector("form#jfb-2"), Is.Not.Null);
            Assert.That(forged.Served.Document.QuerySelector("[data-jfb-outcome]"), Is.Null);
            Assert.That(forged.Served.Document.QuerySelector("form#jfb-1"), Is.Not.Null);
        });
    }

    [Test]
    public async Task AForm_AsksAtOnce_ForThePlacesOfItsOptions_OrARuleOnTheMember_AndOtherwiseNot()
    {
        var content = (await PagesAsync(CachePage))[CachePage];
        var seats = Guid.Parse("71c00000-0000-4000-8000-000000000001");
        await GetRequiredService<OptionListService>().CreateAsync(seats, new OptionListInput("seats", "Seats", OptionListKind.Manual, Items: """[{"value":"s","places":1},{"value":"m"}]"""));
        var plainList = Guid.Parse("71c00000-0000-4000-8000-000000000002");
        await GetRequiredService<OptionListService>().CreateAsync(plainList, new OptionListInput("rooms", "Rooms", OptionListKind.Manual, Items: """[{"value":"s"},{"value":"m"}]"""));
        var fits = Guid.Parse("71c00000-0000-4000-8000-000000000003");
        await GetRequiredService<OptionListService>().CreateAsync(fits, new OptionListInput("fits", "Fits", OptionListKind.Manual, Items: """[{"value":"slim","places":1,"parentValues":["s"]}]"""));
        var slot = new FormField
        {
            Key = Guid.Parse("71c00000-0000-4000-8000-000000000011"),
            Type = "radio",
            Alias = "slot",
            Label = new LocalizedText("Slot"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "a", Label = new LocalizedText("A") }] },
        };
        var limited = slot with { Options = new FieldOptions { Items = [new FieldOption { Value = "a", Label = new LocalizedText("A"), Places = 3 }] } };
        var fromList = slot with { Type = "dropdown", Options = new FieldOptions { Source = OptionSource.List, OptionList = seats } };
        var fromPlainList = slot with { Type = "dropdown", Options = new FieldOptions { Source = OptionSource.List, OptionList = plainList } };
        var filtered = slot with { Key = Guid.Parse("71c00000-0000-4000-8000-000000000012"), Type = "dropdown", Alias = "fit", Options = new FieldOptions { Source = OptionSource.List, OptionList = fits, Parent = slot.Key } };
        var ranked = slot with { Type = "ranking", Options = new FieldOptions { Source = OptionSource.List, OptionList = seats } };
        var forMembers = RequiredName with { Rules = [new FormRule { Key = Guid.Parse("71c00000-0000-4000-8000-000000000021"), Effect = RuleEffect.Hide, Conditions = [new RuleCondition { Context = "memberSignedIn", Operator = "isChecked" }] }] };

        var ownFiltered = filtered with { Options = new FieldOptions { Items = [new FieldOption { Value = "slim", Label = new LocalizedText("Slim"), ParentValues = ["a"], Places = 2 }], Parent = slot.Key } };
        var inRows = new FormField { Key = Guid.Parse("71c00000-0000-4000-8000-000000000013"), Type = "repeater", Alias = "rows", Label = new LocalizedText("Rows"), Fields = [limited] };
        var checkedForMembers = new FormValidationRule { Key = Guid.Parse("71c00000-0000-4000-8000-000000000022"), Field = RequiredName.Key, Conditions = [new RuleCondition { Context = "memberSignedIn", Operator = "isChecked" }] };

        async Task<bool> AsksAsync(string alias, params FormField[] fields)
        {
            var rendered = await RenderAsync(await PublishFormAsync(alias, null, fields), "jfb-1", "/cache/", content, null);
            return rendered.Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks");
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await AsksAsync("asksNothing", RequiredName, slot), Is.False, "nothing the challenge tells");
            Assert.That(await AsksAsync("asksPlaces", limited), Is.True, "its own option has places");
            Assert.That(await AsksAsync("asksList", fromList), Is.True, "an option of its list has places");
            Assert.That(await AsksAsync("asksPlainList", fromPlainList), Is.False, "no option of its list has");
            Assert.That(await AsksAsync("asksFiltered", slot, filtered), Is.False, "the endpoint tells no option that hangs on a parent's answer");
            Assert.That(await AsksAsync("asksRanked", ranked), Is.False, "a ranking's options take no places");
            Assert.That(await AsksAsync("asksOwnFiltered", slot, ownFiltered), Is.True, "the endpoint tells its own options, whatever hangs on a parent");
            Assert.That(await AsksAsync("asksInRows", inRows), Is.True, "an option in a row has places");
            Assert.That(await AsksAsync("asksMember", forMembers), Is.True, "a rule looks at the member");
            var captcha = await PublishFormAsync("asksCaptcha", definition => definition with { Settings = definition.Settings with { Protection = new FormProtectionSettings { Level = ProtectionLevel.Captcha, Captcha = new CaptchaChoice { Provider = "hcaptcha" } } } }, RequiredName);
            Assert.That((await RenderAsync(captcha, "jfb-1", "/cache/", content, null)).Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks"), Is.True, "its own captcha");
            var keyless = await PublishFormAsync("asksKeyless", definition => definition with { Settings = definition.Settings with { Protection = new FormProtectionSettings { Level = ProtectionLevel.Captcha, Captcha = new CaptchaChoice { Provider = "recaptchaV2" } } } }, RequiredName);
            Assert.That((await RenderAsync(keyless, "jfb-1", "/cache/", content, null)).Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks"), Is.False, "a provider without its keys shows nothing");
            var puzzle = await PublishFormAsync("asksPuzzle", definition => definition with { Settings = definition.Settings with { Protection = new FormProtectionSettings { Level = ProtectionLevel.ProofOfWork } } }, RequiredName);
            Assert.That((await RenderAsync(puzzle, "jfb-1", "/cache/", content, null)).Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks"), Is.False, "a puzzle waits for a send");
            var validated = await PublishFormAsync("asksValidated", definition => definition with { ValidationRules = [checkedForMembers] }, RequiredName);
            Assert.That((await RenderAsync(validated, "jfb-1", "/cache/", content, null)).Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks"), Is.True, "a validation rule looks at the member");
        });
    }

    [Test]
    public async Task AForm_AsksAtOnce_ForACaptchaItInheritsFromItsSite_AndNotAboveALevelItSetsItself()
    {
        var content = (await PagesAsync(CachePage))[CachePage];
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>();
        _ = await settings.SaveAsync(new ProtectionSettings { Level = ProtectionLevel.Captcha, CaptchaProvider = "hcaptcha" }, current.Version, null);

        var inherits = await PublishFormAsync("asksInherited", null, RequiredName);
        var standard = await PublishFormAsync("asksOwnLevel", definition => definition with { Settings = definition.Settings with { Protection = new FormProtectionSettings { Level = ProtectionLevel.Standard } } }, RequiredName);

        Assert.Multiple(async () =>
        {
            Assert.That((await RenderAsync(inherits, "jfb-1", "/cache/", content, null)).Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks"), Is.True);
            Assert.That((await RenderAsync(standard, "jfb-1", "/cache/", content, null)).Served.Document.QuerySelector("form#jfb-1")!.HasAttribute("data-jfb-asks"), Is.False);
        });
    }

    [Test]
    public async Task Render_InTheThemeItNames_ElseTheFormsOwn_AndAThemeTheSiteLacksAsDefault()
    {
        var key = await PublishFormAsync(CacheForm, definition => definition with { Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { Theme = "unstyled" } } }, RequiredName);
        var content = (await PagesAsync(CachePage))[CachePage];

        var named = await RenderAsync(key, "jfb-1", "/cache/", content, null, "&theme=bootstrap5");
        var own = await RenderAsync(key, "jfb-1", "/cache/", content, null);
        var lacking = await RenderAsync(key, "jfb-1", "/cache/", content, null, "&theme=a-theme-the-site-lacks");
        var blank = await RenderAsync(key, "jfb-1", "/cache/", content, null, "&theme=%20");

        Assert.Multiple(() =>
        {
            Assert.That(named.Served.Document.QuerySelector("form#jfb-1")!.ClassList, Does.Contain("jfb-theme-bootstrap5"));
            Assert.That(named.Served.Document.QuerySelector("input#jfb-1-name")!.GetAttribute("class"), Is.EqualTo("jfb-control form-control"), "the theme's classes beside ours");
            Assert.That(own.Served.Document.QuerySelector("form#jfb-1")!.ClassList, Does.Contain("jfb-theme-unstyled"), "the form's own theme");
            Assert.That(lacking.Served.Document.QuerySelector("form#jfb-1")!.ClassList, Does.Contain("jfb-theme-default"));
            Assert.That(blank.Served.Document.QuerySelector("form#jfb-1")!.ClassList, Does.Contain("jfb-theme-unstyled"), "a blank theme is none");
            Assert.That(named.Served.Document.QuerySelectorAll("link, script[src]"), Is.Empty, "still without the asset tags");
            Assert.That(GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(FormBuilderThemes).FullName), Is.Empty, "a name from a request is neither logged nor kept");
        });
    }

    [Test]
    public async Task Render_RefusesWhatNamesNoPlaceOnAPage_AndAKeyThatNamesNoPublishedForm()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName);
        var gone = await PublishFormAsync("cacheGone", null, RequiredName);
        _ = await Forms.UnpublishAsync(gone, (await Forms.GetDraftAsync(gone))!.Form.ConcurrencyVersion, Editor);
        var content = (await PagesAsync(CachePage))[CachePage];
        var place = $"page=%2Fcache%2F&contentKey={content}";

        var refusals = new Dictionary<string, string>
        {
            ["no instance"] = $"{Root}/forms/{key}/render?{place}",
            ["instance jfb-0"] = $"{Root}/forms/{key}/render?instance=jfb-0&{place}",
            ["instance one"] = $"{Root}/forms/{key}/render?instance=one&{place}",
            ["no page"] = $"{Root}/forms/{key}/render?instance=jfb-1&contentKey={content}",
            ["another site"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=https%3A%2F%2Fevil.example%2F",
            ["another host"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2F%2Fevil.example%2F",
            ["a query"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2Fcache%2F%3Fx%3D1",
            ["a content key that is no key"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2Fcache%2F&contentKey=nope",
            ["a culture that is none"] = $"{Root}/forms/{key}/render?instance=jfb-1&{place}&culture=not%20one",
            ["a theme that is no alias"] = $"{Root}/forms/{key}/render?instance=jfb-1&{place}&theme=Bootstrap5",
            ["a theme over two lines"] = $"{Root}/forms/{key}/render?instance=jfb-1&{place}&theme=bootstrap5%0A",
            ["the alias"] = $"{Root}/forms/{CacheForm}/render?instance=jfb-1&{place}",
            ["a key without hyphens"] = $"{Root}/forms/{key:N}/render?instance=jfb-1&{place}",
            ["no form"] = $"{Root}/forms/{Guid.NewGuid()}/render?instance=jfb-1&{place}",
            ["an unpublished form"] = $"{Root}/forms/{gone}/render?instance=jfb-1&{place}",
        };
        var answers = new Dictionary<string, string>();
        foreach (var (name, path) in refusals)
        {
            using var response = await GetAsync(path);
            answers[name] = await RefusalAsync(response) + ", " + Header(response, "Cache-Control");
        }

        Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
        {
            ["no instance"] = "400 invalid_request, no-store",
            ["instance jfb-0"] = "400 invalid_request, no-store",
            ["instance one"] = "400 invalid_request, no-store",
            ["no page"] = "400 invalid_request, no-store",
            ["another site"] = "400 invalid_request, no-store",
            ["another host"] = "400 invalid_request, no-store",
            ["a query"] = "400 invalid_request, no-store",
            ["a content key that is no key"] = "400 invalid_request, no-store",
            ["a culture that is none"] = "400 invalid_request, no-store",
            ["a theme that is no alias"] = "400 invalid_request, no-store",
            ["a theme over two lines"] = "400 invalid_request, no-store",
            ["the alias"] = "404 form_not_found, no-store",
            ["a key without hyphens"] = "404 form_not_found, no-store",
            ["no form"] = "404 form_not_found, no-store",
            ["an unpublished form"] = "404 form_not_found, no-store",
        }));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.Services.Configure<FormBuilderOptions>(options =>
        {
            options.Captcha.Hcaptcha.SiteKey = "JFB_FAKE_site_key";
            options.Captcha.Hcaptcha.SecretKey = "JFB_FAKE_secret_key";
        });
        builder.Services.PostConfigure<RequestLocalizationOptions>(options =>
            options.RequestCultureProviders = [.. options.RequestCultureProviders.Where(provider => provider is not QueryStringRequestCultureProvider)]);
    }

    // The render endpoint's answer for a place on a page.
    private async Task<Rendered> RenderAsync(Guid key, string instance, string page, Guid content, string? culture, string extra = "")
    {
        var path = $"{Root}/forms/{key}/render?instance={instance}&page={Uri.EscapeDataString(page)}&contentKey={content}" + (culture is null ? string.Empty : "&culture=" + culture) + extra;
        using var response = await Client.GetAsync(path);
        var served = await Served.ReadAsync(response);
        Assert.That(served.Status, Is.EqualTo(HttpStatusCode.OK), served.Html);
        return new Rendered(served.Status, response.Content.Headers.ContentType?.ToString(), served);
    }

    // A form's markup with Umbraco's route token blanked, which Data Protection makes new on every render.
    private static string Markup(IElement form)
    {
        var copy = (IElement)form.Clone();
        copy.QuerySelector("input[name='" + FormPostFields.RouteToken + "']")?.SetAttribute("value", string.Empty);
        return copy.OuterHtml;
    }

    private sealed record Rendered(HttpStatusCode Status, string? ContentType, Served Served);
}
