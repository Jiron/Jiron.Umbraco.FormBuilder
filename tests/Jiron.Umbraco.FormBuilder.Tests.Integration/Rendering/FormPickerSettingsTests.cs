using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The form picker's settings (ROADMAP 6.7a, ADR-0129) on real pages: a single picker renders its form in the theme its
/// settings name and sends the visitor to its page after a send without JavaScript, where the same form placed by its
/// alias shows its outcome; a multiple picker renders each form in the editor's order; and a page's value that the
/// settings do not allow (a form outside the allowed folders, not among the allowed forms, several in a single picker, a
/// form the library lacks or holds in its trash, a value that is not keys) is reported when the page is saved and never
/// published, as Umbraco does for its own properties; a theme the site lacks, and a folder, form or page that is not a
/// key, are refused with the data type. A send with JavaScript from a picked form goes to the same page, by a token only
/// the server can write and read, and a token a visitor changed is refused (6.4i, ADR-0120).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPickerSettingsTests : RenderingTestBase
{
    [Test]
    public async Task ASinglePicker_RendersItsFormInItsTheme_AndAMultipleOneEachFormInOrder()
    {
        var first = await PublishFormAsync("pickerForm");
        var second = await PublishFormAsync("otherForm");
        var pages = await PagesAsync("picker-single", "picker-many", "picker-thanks");
        await PickersAsync(Settings(theme: "bootstrap5", redirect: pages["picker-thanks"]), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", first.ToString("D"));
        await SetAsync(pages["picker-many"], "forms", $"{second:D},{first:D}");

        var (_, single) = await PageAsync("picker-single");
        var (_, many) = await PageAsync("picker-many");

        Assert.Multiple(() =>
        {
            var shown = single.QuerySelectorAll("form.jfb-form");
            Assert.That(shown.Select(form => form.GetAttribute("data-jfb-form")), Is.EqualTo(new[] { first.ToString("D") }));
            Assert.That(shown.Single().ClassList, Does.Contain("jfb-theme-bootstrap5"), "the picker's theme");
            var placed = many.QuerySelectorAll("form.jfb-form");
            Assert.That(placed.Select(form => form.GetAttribute("data-jfb-form")), Is.EqualTo(new[] { second.ToString("D"), first.ToString("D") }), "each form in the editor's order");
            Assert.That(placed.Select(form => form.ClassList.Contains("jfb-theme-default")), Is.All.True, "each form's own theme");
            Assert.That(placed.Select(form => form.Id), Is.EqualTo(new[] { "jfb-1", "jfb-2" }));
        });
    }

    [Test]
    public async Task ThePickersPage_TakesTheVisitorThereAfterASendWithoutJavaScript_WhereTheFormByItsAliasShowsItsOutcome()
    {
        var form = await PublishFormAsync("pickerForm");
        var pages = await PagesAsync("picker-single", "picker-direct", "picker-thanks");
        await PickersAsync(Settings(redirect: pages["picker-thanks"]), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", form.ToString("D"));

        var picked = await FormAtAsync("/picker-single/");
        Fill(picked, "name", "Ada");
        using var sent = await PostAsync(picked, "submit", "/picker-single/");
        var direct = await FormAtAsync("/picker-direct/");
        Fill(direct, "name", "Ada");
        using var outcome = await PostAsync(direct, "submit", "/picker-direct/");

        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(sent.Headers.Location?.ToString(), Does.EndWith("/picker-thanks/#"), "the picker's page, opened at its top, not at the post's #jfb-1");
            Assert.That(outcome.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(outcome.Headers.Location?.ToString(), Does.Contain("/picker-direct/").And.Contain("jfb-sent="), "the form's own outcome");
            Assert.That(picked.QuerySelector("input[name='ufprt']")?.GetAttribute("value"), Is.Not.EqualTo(direct.QuerySelector("input[name='ufprt']")?.GetAttribute("value")), "the page rides in the token");
            Assert.That(picked.OuterHtml, Does.Not.Contain(pages["picker-thanks"].ToString("D")), "and nowhere a visitor can read or change it");
        });
    }

    [Test]
    public async Task ASendWithJavaScript_FromAPickedForm_GoesToThePickersPage_WhereTheFormByItsAliasShowsItsOutcome()
    {
        var form = await PublishFormAsync("pickerForm");
        var pages = await PagesAsync("picker-single", "picker-direct", "picker-thanks");
        await PickersAsync(Settings(redirect: pages["picker-thanks"]), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", form.ToString("D"));
        var picked = await FormAtAsync("/picker-single/");
        var direct = await FormAtAsync("/picker-direct/");
        var placement = picked.GetAttribute("data-jfb-placement")!;

        using var sent = await SendEntryAsync(form, $$"""{"revision":1,"values":{"name":"Ada"},"contentKey":"{{picked.GetAttribute("data-jfb-content")}}","placement":{{JsonSerializer.Serialize(placement)}}}""");
        var sentAnswer = await JsonAsync(sent);
        using var outcome = await SendEntryAsync(form, """{"revision":1,"values":{"name":"Ada"}}""");
        var outcomeAnswer = await JsonAsync(outcome);

        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sentAnswer["outcome"]!["kind"]!.GetValue<string>(), Is.EqualTo("redirect"));
            Assert.That(sentAnswer["outcome"]!["url"]!.GetValue<string>(), Does.EndWith("/picker-thanks/"), "the picker's page");
            Assert.That(picked.GetAttribute("data-jfb-content"), Is.EqualTo(pages["picker-single"].ToString("D")), "the page the form is on");
            Assert.That(placement, Does.Not.Contain(pages["picker-thanks"].ToString("D")).And.Not.Contain(pages["picker-thanks"].ToString("N")), "the visitor cannot read it");
            Assert.That(direct.HasAttribute("data-jfb-placement"), Is.False, "the form by its alias has no placement");
            Assert.That(outcome.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(outcomeAnswer["outcome"]!["kind"]!.GetValue<string>(), Is.EqualTo("message"), "the form's own outcome");
        });
    }

    [Test]
    public async Task ARetryAndAReceipt_KeepThePlacement_WhichNoEntryKeeps()
    {
        var form = await PublishFormAsync("pickerForm");
        var pages = await PagesAsync("picker-single", "picker-thanks");
        await PickersAsync(Settings(redirect: pages["picker-thanks"]), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", form.ToString("D"));
        var placement = JsonSerializer.Serialize((await FormAtAsync("/picker-single/")).GetAttribute("data-jfb-placement")!);
        var id = Guid.NewGuid();
        var challenge = await ChallengeAsync(form);
        var body = $$"""{"revision":1,"submissionId":"{{id}}","challenge":{{JsonSerializer.Serialize(challenge)}},"values":{"name":"Ada"},"placement":{{placement}}}""";

        using var sent = await SendEntryAsync(form, body);
        using var retry = await SendEntryAsync(form, body);
        using var plainRetry = await SendEntryAsync(form, body.Replace(",\"placement\":" + placement, string.Empty, StringComparison.Ordinal));
        using var receipt = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/receipt", request => request.Content = new StringContent($$"""{"submissionId":"{{id}}","challenge":{{JsonSerializer.Serialize(challenge)}},"placement":{{placement}}}""", Encoding.UTF8, "application/json"));
        using var changed = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/receipt", request => request.Content = new StringContent($$"""{"submissionId":"{{id}}","challenge":{{JsonSerializer.Serialize(challenge)}},"placement":"changed"}""", Encoding.UTF8, "application/json"));

        var kinds = new[] { await JsonAsync(sent), await JsonAsync(retry), await JsonAsync(receipt) }.Select(answer => answer["outcome"]!["url"]?.GetValue<string>());
        Assert.Multiple(async () =>
        {
            Assert.That(kinds, Has.All.EndsWith("/picker-thanks/"), "the entry, its retry and its receipt");
            Assert.That((await JsonAsync(plainRetry))["outcome"]!["kind"]!.GetValue<string>(), Is.EqualTo("message"), "a retry without it has the form's own outcome");
            Assert.That(await RefusalAsync(changed), Is.EqualTo("400 invalid_request"), "a receipt's token is held as a send's");
        });
    }

    [Test]
    public async Task TheFormAsItIsNow_RendersWithThePlacement_WhoseTokenThePageGives()
    {
        var form = await PublishFormAsync("pickerForm");
        var pages = await PagesAsync("picker-single", "picker-thanks");
        await PickersAsync(Settings(redirect: pages["picker-thanks"]), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", form.ToString("D"));
        var shown = await FormAtAsync("/picker-single/");
        var token = Uri.EscapeDataString(shown.GetAttribute("data-jfb-placement")!);
        var place = $"instance=jfb-1&page=/picker-single/&contentKey={pages["picker-single"]:D}";

        using var placed = await GetAsync($"{Root}/forms/{form}/render?{place}&placement={token}");
        using var without = await GetAsync($"{Root}/forms/{form}/render?{place}");
        using var changed = await GetAsync($"{Root}/forms/{form}/render?{place}&placement=changed");
        var rendered = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await placed.Content.ReadAsStringAsync()).QuerySelector("form.jfb-form")!;
        var plain = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await without.Content.ReadAsStringAsync()).QuerySelector("form.jfb-form")!;
        Fill(rendered, "name", "Ada");
        rendered.SetAttribute("action", "/picker-single/#jfb-1");
        using var sent = await PostAsync(rendered, "submit", "/picker-single/");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(changed), Is.EqualTo("400 invalid_request"));
            Assert.That(rendered.GetAttribute("data-jfb-content"), Is.EqualTo(pages["picker-single"].ToString("D")));
            Assert.That(rendered.GetAttribute("data-jfb-placement"), Is.Not.Null, "the send that follows carries it too");
            Assert.That(plain.HasAttribute("data-jfb-placement"), Is.False);
            Assert.That(sent.Headers.Location?.ToString(), Does.EndWith("/picker-thanks/#"), "and so does its route token");
        });
    }

    [Test]
    public async Task APlacementAVisitorChanged_IsRefused_AndNothingIsStored()
    {
        var form = await PublishFormAsync("pickerForm");
        var other = await PublishFormAsync("otherForm");
        var pages = await PagesAsync("picker-single", "picker-thanks", "picker-many");
        await PickersAsync(Settings(redirect: pages["picker-thanks"]), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", form.ToString("D"));
        var token = (await FormAtAsync("/picker-single/")).GetAttribute("data-jfb-placement")!;
        var another = GetRequiredService<PlacementTokens>().Issue(other, pages["picker-thanks"]);
        var entries = GetRequiredService<IEntryRepository>();
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var flipped = token[^1] == 'A' ? token[..^1] + "B" : token[..^1] + "A";
        var values = new[]
        {
            JsonSerializer.Serialize(flipped),
            JsonSerializer.Serialize(token[..^4]),
            JsonSerializer.Serialize(pages["picker-many"].ToString("D")),
            JsonSerializer.Serialize("https://evil.example/"),
            JsonSerializer.Serialize(another),
            JsonSerializer.Serialize(new string('A', 1_025)),
            JsonSerializer.Serialize(string.Empty),
            "12",
            "true",
            "[]",
        };

        foreach (var value in values)
        {
            using var refused = await SendEntryAsync(form, $$"""{"revision":1,"values":{"name":"Ada"},"placement":{{value}}}""");
            var problem = await JsonAsync(refused);
            Assert.Multiple(() =>
            {
                Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), value);
                Assert.That(problem["code"]!.GetValue<string>(), Is.EqualTo("invalid_request"), value);
                Assert.That(problem["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>()), Is.EqualTo(new[] { "placement" }), value);
            });
        }

        using var check = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/validate", request => request.Content = new StringContent($$"""{"revision":1,"pageKey":"{{Guid.NewGuid()}}","values":{"name":"Ada"},"placement":{{JsonSerializer.Serialize(token)}}}""", Encoding.UTF8, "application/json"));
        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(check), Is.EqualTo("400 invalid_request"), "a page's check takes no placement");
            Assert.That(await entries.ListIdsAsync(row.Id, 50), Is.Empty, "nothing was stored");
        });

        using var without = await SendEntryAsync(form, """{"revision":1,"values":{"name":"Ada"},"placement":null}""");
        Assert.That(without.StatusCode, Is.EqualTo(HttpStatusCode.OK), "no placement is the form's own outcome");
    }

    [Test]
    public async Task AFormsTemplates_HoldWhatTheRuntimeShowsASendWith_InTheThemeAndTheLanguage()
    {
        var form = await PublishFormAsync("pickerForm");
        var pages = await PagesAsync("picker-single");
        await PickersAsync(Settings(theme: "bootstrap5"), Settings(multiple: true));
        await SetAsync(pages["picker-single"], "form", form.ToString("D"));

        var shown = await FormAtAsync("/picker-single/");
        var templates = shown.QuerySelector(":scope > template[data-jfb-templates]")!;
        var parts = ((AngleSharp.Html.Dom.IHtmlTemplateElement)templates).Content;
        var notices = parts.QuerySelectorAll("[data-jfb-notice]");
        var outcome = parts.QuerySelector("[data-jfb-outcome]")!;
        var countdown = outcome.QuerySelector("[data-jfb-countdown]")!;

        Assert.Multiple(() =>
        {
            Assert.That(templates.GetAttribute("data-jfb-sending"), Is.EqualTo("Sending your answers"));
            Assert.That(templates.GetAttribute("data-jfb-thanks"), Is.EqualTo("Thank you. We have received your message."));
            Assert.That(notices.Select(notice => notice.GetAttribute("data-jfb-notice")), Is.EqualTo(new[] { "rate_limited", "server_error", "timeout", "form_changed" }));
            foreach (var notice in notices)
            {
                Assert.That(notice.GetAttribute("role"), Is.EqualTo("alert"));
                Assert.That(notice.GetAttribute("tabindex"), Is.EqualTo("-1"));
                Assert.That(notice.QuerySelector("h2")!.Id, Is.EqualTo(notice.GetAttribute("aria-labelledby")));
            }

            foreach (var notice in notices.SkipLast(1))
            {
                Assert.That(notice.ClassList, Does.Contain("jfb-notice-error"));
                Assert.That(notice.QuerySelector("h2")!.TextContent, Is.EqualTo("Your answers weren't sent"));
            }

            Assert.That(notices[3].ClassList, Does.Not.Contain("jfb-notice-error"), "nothing went wrong: the form changed");
            Assert.That(notices[3].QuerySelector("h2")!.TextContent, Is.EqualTo("This form was updated while you filled it in"));
            Assert.That(notices[3].QuerySelector("p")!.TextContent, Is.EqualTo("Your answers are kept."));
            Assert.That(notices[3].QuerySelector("p")!.GetAttribute("data-jfb-one"), Does.StartWith("A consent has new wording"));
            Assert.That(notices[3].QuerySelector("p")!.GetAttribute("data-jfb-other"), Does.StartWith("Some consents have new wording"));

            Assert.That(notices[0].QuerySelector("p")!.GetAttribute("data-jfb-seconds"), Does.Contain("{0} seconds"));
            Assert.That(notices[0].QuerySelector("p")!.GetAttribute("data-jfb-minutes"), Does.Contain("{0} minutes"));
            Assert.That(notices[1].TextContent, Does.Contain("Something went wrong"));
            Assert.That(notices[2].TextContent, Does.Contain("Check your connection"));
            Assert.That(outcome.GetAttribute("tabindex"), Is.EqualTo("-1"));
            Assert.That(outcome.QuerySelector("[data-jfb-reference] strong")!.TextContent, Is.Empty);
            Assert.That(countdown.GetAttribute("data-jfb-one"), Does.Contain("{0} second."));
            Assert.That(countdown.GetAttribute("data-jfb-other"), Does.Contain("{0} seconds."));
            Assert.That(outcome.QuerySelector("a[data-jfb-continue]")!.TextContent, Is.EqualTo("Go now"));
            Assert.That(outcome.QuerySelector("button[type=button][data-jfb-stay]")!.TextContent, Is.EqualTo("Stay here"));
            Assert.That(outcome.ClassName, Does.Contain("jfb-outcome"));
        });
    }

    private Task<HttpResponseMessage> SendEntryAsync(Guid form, string body) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent(body, Encoding.UTF8, "application/json"));

    [Test]
    public async Task APageValueTheSettingsDoNotAllow_IsReportedOnSave_AndNeverPublished()
    {
        var folders = GetRequiredService<IFormBuilderFolderService>();
        var allowed = await folders.CreateAsync(new NewFolderRequest("Allowed", UserKey: Editor));
        var below = await folders.CreateAsync(new NewFolderRequest("Below", allowed.Key, Editor));
        var other = await folders.CreateAsync(new NewFolderRequest("Other", UserKey: Editor));
        var inside = await FormInAsync("insideForm", below.Key);
        var outside = await FormInAsync("outsideForm", other.Key);
        var listed = await FormInAsync("listedForm", null);
        var trashed = await FormInAsync("trashedForm", null);
        var stored = (await Forms.GetAsync(trashed))!;
        Assert.That(await Forms.TrashAsync(trashed, stored.ConcurrencyVersion, Editor), Is.Not.Null);
        var pages = await PagesAsync("picker-single");
        var single = await DataTypeAsync("Folder picker", Settings(folders: [allowed.Key]));
        var forms = await DataTypeAsync("Form list picker", Settings(forms: [listed]));
        var many = await DataTypeAsync("Many picker", Settings(multiple: true));
        await PropertiesAsync(("inFolder", single), ("listed", forms), ("many", many));
        var page = pages["picker-single"];

        Assert.Multiple(async () =>
        {
            Assert.That(await ProblemsAsync(page, ("inFolder", inside.ToString("D"))), Is.Empty, "a form below an allowed folder");
            Assert.That(await ProblemsAsync(page, ("inFolder", outside.ToString("D"))), Is.EqualTo(new[] { "inFolder #jfbPicker_notAllowed" }), "a form outside");
            Assert.That(await ProblemsAsync(page, ("listed", listed.ToString("D"))), Is.Empty, "an allowed form");
            Assert.That(await ProblemsAsync(page, ("listed", inside.ToString("D"))), Is.EqualTo(new[] { "listed #jfbPicker_notAllowed" }), "a form not among the allowed ones");
            Assert.That(await ProblemsAsync(page, ("inFolder", $"{inside:D},{inside:D}")), Is.Empty, "the same form twice is one");
            Assert.That(await ProblemsAsync(page, ("listed", $"{listed:D},{inside:D}")), Is.EqualTo(new[] { "listed #jfbPicker_oneOnly" }), "several in a single picker");
            Assert.That(await ProblemsAsync(page, ("many", $"{outside:D},{listed:D}")), Is.Empty, "several in a multiple one");
            Assert.That(await ProblemsAsync(page, ("many", $"{Guid.NewGuid():D}")), Is.EqualTo(new[] { "many #jfbPicker_noSuchForm" }), "a form the library lacks");
            Assert.That(await ProblemsAsync(page, ("many", $"{trashed:D}")), Is.EqualTo(new[] { "many #jfbPicker_noSuchForm" }), "a form in the trash");
            Assert.That(await ProblemsAsync(page, ("many", "the contact form")), Is.EqualTo(new[] { "many #jfbPicker_notAKey" }), "a value that is not keys");
            Assert.That(await ProblemsAsync(page, ("many", string.Empty)), Is.Empty, "nothing picked");

            var published = await GetRequiredService<IContentEditingService>().UpdateAndPublishAsync(page, Update(page, ("inFolder", outside.ToString("D"))), [], Constants.Security.SuperUserKey);
            Assert.That(published.Status, Is.Not.EqualTo(ContentEditingOperationStatus.Success), "refused");
            Assert.That(Contents.GetById(page)!.PublishedState == PublishedState.Published && Contents.GetById(page)!.GetValue<string>("inFolder", published: true) is { Length: > 0 }, Is.False);
        });
    }

    [Test]
    public async Task APickerWhoseThemeTheSiteLacks_OrWhoseKeysAreNotKeys_IsRefused()
    {
        var types = GetRequiredService<IDataTypeService>();
        var refused = await types.CreateAsync(NewDataType("Odd picker", Settings(theme: "a-theme-the-site-lacks")), Constants.Security.SuperUserKey);
        var notKeys = await types.CreateAsync(NewDataType("Folder text picker", new Dictionary<string, object> { ["allowedFolders"] = new[] { "Contact forms" } }), Constants.Security.SuperUserKey);
        var notAPage = await types.CreateAsync(NewDataType("Page text picker", new Dictionary<string, object> { ["redirect"] = "/thanks/" }), Constants.Security.SuperUserKey);
        var kept = await types.CreateAsync(NewDataType("Good picker", Settings(theme: "unstyled")), Constants.Security.SuperUserKey);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Success, Is.False);
            Assert.That(refused.Status, Is.EqualTo(DataTypeOperationStatus.InvalidConfiguration));
            Assert.That((notKeys.Status, notAPage.Status), Is.EqualTo((DataTypeOperationStatus.InvalidConfiguration, DataTypeOperationStatus.InvalidConfiguration)));
            Assert.That(kept.Success, Is.True);
        });
    }

    [TestCase("allowedForms")]
    [TestCase("allowedFolders")]
    public async Task APickerAllowingMoreThanItsQueriesCarry_IsRefused(string setting)
    {
        var types = GetRequiredService<IDataTypeService>();
        string[] Keys(int count) => [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString("D"))];

        var most = await types.CreateAsync(NewDataType("As many as it carries", new Dictionary<string, object> { [setting] = Keys(SqlArgs.MaxListLength) }), Constants.Security.SuperUserKey);
        var tooMany = await types.CreateAsync(NewDataType("One too many", new Dictionary<string, object> { [setting] = Keys(SqlArgs.MaxListLength + 1) }), Constants.Security.SuperUserKey);

        Assert.Multiple(() =>
        {
            Assert.That(SqlArgs.MaxListLength, Is.EqualTo(1000));
            Assert.That(most.Success, Is.True, most.Status.ToString());
            Assert.That((tooMany.Success, tooMany.Status), Is.EqualTo((false, DataTypeOperationStatus.InvalidConfiguration)));
        });
    }

    // A picker's settings, as the backoffice writes them.
    private static Dictionary<string, object> Settings(bool multiple = false, Guid[]? folders = null, Guid[]? forms = null, string? theme = null, Guid? redirect = null)
    {
        var settings = new Dictionary<string, object> { ["multiple"] = multiple };
        if (folders is not null)
        {
            settings["allowedFolders"] = folders.Select(key => key.ToString("D")).ToArray();
        }

        if (forms is not null)
        {
            settings["allowedForms"] = forms.Select(key => key.ToString("D")).ToArray();
        }

        if (theme is not null)
        {
            settings["theme"] = theme;
        }

        if (redirect is { } page)
        {
            settings["redirect"] = page.ToString("D");
        }

        return settings;
    }

    private DataType NewDataType(string name, Dictionary<string, object> settings) =>
        new(GetRequiredService<PropertyEditorCollection>()[FormPickerPropertyEditor.EditorAlias]!, GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Name = name,
            DatabaseType = ValueStorageType.Nvarchar,
            ConfigurationData = settings,
        };

    private async Task<IDataType> DataTypeAsync(string name, Dictionary<string, object> settings)
    {
        var created = await GetRequiredService<IDataTypeService>().CreateAsync(NewDataType(name, settings), Constants.Security.SuperUserKey);
        Assert.That(created.Success, Is.True, $"{name}: {created.Status}");
        return created.Result!;
    }

    // The page type's two pickers the views read: "form", a single one, and "forms", a multiple one.
    private async Task PickersAsync(Dictionary<string, object> single, Dictionary<string, object> many) =>
        await PropertiesAsync(("form", await DataTypeAsync("Single picker", single)), ("forms", await DataTypeAsync("Multiple picker", many)));

    private async Task PropertiesAsync(params (string Alias, IDataType Type)[] properties)
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = types.Get(PageType)!;
        foreach (var (alias, dataType) in properties)
        {
            type.AddPropertyType(new PropertyType(GetRequiredService<global::Umbraco.Cms.Core.Strings.IShortStringHelper>(), dataType, alias) { Name = alias });
        }

        Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
    }

    // A form created in a folder (or at the root) and published.
    private async Task<Guid> FormInAsync(string alias, Guid? folder)
    {
        var created = await Forms.CreateAsync(new NewFormRequest(alias, alias, folder, Editor));
        await SaveAndPublishAsync(created.Key, definition => WithFields(definition, NameField));
        return created.Key;
    }

    // Sets a page's property and publishes it.
    private async Task SetAsync(Guid key, string alias, string value)
    {
        var page = Contents.GetById(key)!;
        page.SetValue(alias, value);
        Contents.Save(page);
        Contents.Publish(page, ["*"]);
        await Task.CompletedTask;
    }

    private ValidateContentUpdateModel Update(Guid key, params (string Alias, string Value)[] values) => new()
    {
        Variants = [new VariantModel { Name = Contents.GetById(key)!.Name! }],
        Properties = [.. values.Select(value => new PropertyValueModel { Alias = value.Alias, Value = value.Value })],
    };

    // What saving the page with these values reports, as "<alias> <message>".
    private async Task<IReadOnlyList<string>> ProblemsAsync(Guid key, params (string Alias, string Value)[] values)
    {
        var checkedUpdate = await GetRequiredService<IContentEditingService>().ValidateUpdateAsync(key, Update(key, values), Constants.Security.SuperUserKey);
        return [.. checkedUpdate.Result.ValidationErrors.SelectMany(error => error.ErrorMessages.Select(message => $"{error.Alias} {message}"))];
    }
}
