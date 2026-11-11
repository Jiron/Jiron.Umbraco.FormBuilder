using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The settings sections through the management API (ROADMAP 12.4a, ADR-0218, after 10.3b's ADR-0198): one pair of
/// endpoints for every section, its values in a scope with the fields its screen draws and the host's locks; a save of
/// some fields keeps the rest, refuses a field the section lacks, a value out of range and a save made meanwhile, and
/// needs <c>manageSettings</c>; a site is a content root, and a key that names none is refused with nothing written; a
/// site saves the fields it names as its own; the time zone saves as the list spells it; each section's facts; Files
/// keeps its size under the host's cap; Integrations says which references are found; and the draft cleanup keeps draft
/// saves as the saved values say.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class SettingsApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    /// <summary>Holds the one secret a reference may find.</summary>
    public SettingsApiTests() => InMemoryConfiguration[FormBuilderOptions.SectionName + ":Secrets:Signing"] = "a-test-signing-value";

    private const string General = "settings/general";

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    [Test]
    public async Task ASection_AnswersItsValues_TheFieldsItsScreenDraws_AndNoLocks()
    {
        var answer = await Api.CallAsync(Admin, HttpMethod.Get, General);
        var version = await VersionAsync<GeneralSettings>();

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["version"]!.GetValue<int>(), Is.EqualTo(version), "the stored row's, which a save sends back");
            Assert.That(JsonNode.DeepEquals(answer.Body["values"]!["history"], JsonNode.Parse("""{"draftDays":90,"draftCount":50}""")), Is.True, answer.Text);
            var fields = answer.Body["fields"]!.AsArray();
            Assert.That(fields.Select(field => field!["group"]!.GetValue<string>()).Distinct(), Is.EqualTo(new[] { "forms", "site", "newForms", "history" }));
            var days = fields.Single(field => field!["name"]!.GetValue<string>() == "history")!["settings"]![0]!;
            Assert.That((days["label"]!.GetValue<string>(), days["rules"]!["minimum"]!.GetValue<string>(), days["rules"]!["maximum"]!.GetValue<string>(), days["unit"]!.GetValue<string>()), Is.EqualTo(("#jfbGlobalSettings_general_history_draftDays", "1", "3650", "#jfbGlobalSettings_general_history_draftDaysUnit")));
            Assert.That(answer.Body["siteFields"]!.AsArray().Select(field => field!.GetValue<string>()), Does.Contain("timeZone").And.Not.Contain("history"), "every site shares History");
            Assert.That(answer.Body["locks"]!.AsArray(), Is.Empty);
            Assert.That(answer.Body["facts"]!["general"], Is.Not.Null, "the base URL a scope gets without one of its own");
        });
    }

    [Test]
    public async Task ASave_ChangesTheFieldsItSends_AndKeepsTheRestOfTheSection()
    {
        var before = await Settings.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, await VersionAsync<GeneralSettings>(), null);

        var saved = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = before.Version, values = new { history = new { draftDays = 30, draftCount = 5 } } });
        var read = await Api.CallAsync(Admin, HttpMethod.Get, General);

        var stored = await Settings.GetAsync<GeneralSettings>();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["version"]!.GetValue<int>(), Is.EqualTo(before.Version + 1));
            Assert.That(JsonNode.DeepEquals(read.Body!["values"]!["history"], JsonNode.Parse("""{"draftDays":30,"draftCount":5}""")), Is.True, read.Text);
            Assert.That((stored.Value.TimeZone, stored.Value.History.DraftDays, stored.Value.History.DraftCount), Is.EqualTo(("Europe/Berlin", 30, 5)));
        });
    }

    [Test]
    public async Task ASaveOfNoFields_LeavesTheSectionAsItWas()
    {
        var before = await Settings.SaveAsync(new GeneralSettings { History = new HistorySettings { DraftDays = 7, DraftCount = 3 } }, await VersionAsync<GeneralSettings>(), null);

        var saved = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = before.Version, values = new { } });

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(JsonNode.DeepEquals(saved.Body!["values"]!["history"], JsonNode.Parse("""{"draftDays":7,"draftCount":3}""")), Is.True, saved.Text);
        });
    }

    [Test]
    public async Task AValueOutOfRange_AFieldTheSectionLacks_OrAValueOfTheWrongKind_IsRefusedAtItsField_AndNothingIsSaved()
    {
        var version = await VersionAsync<GeneralSettings>();
        var days = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version, values = new { history = new { draftDays = 0, draftCount = 5 } } });
        var count = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version, values = new { history = new { draftDays = 30, draftCount = HistorySettings.MaxDraftCount + 1 } } });
        var unknown = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version, values = new { colour = "red" } });
        var wrong = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version, values = new { requiredMarker = 3 } });

        Assert.Multiple(async () =>
        {
            Assert.That(days.Outcome, Is.EqualTo("400 validation_failed"), days.ToString());
            Assert.That(Fields(days), Is.EqualTo(new[] { ("general.history.draftDays", "range") }));
            Assert.That(count.Outcome, Is.EqualTo("400 validation_failed"), count.ToString());
            Assert.That(Fields(count), Is.EqualTo(new[] { ("general.history.draftCount", "range") }));
            Assert.That(Fields(unknown), Is.EqualTo(new[] { ("general.colour", "range") }));
            Assert.That(Fields(wrong), Is.EqualTo(new[] { ("general.requiredMarker", "range") }));
            Assert.That(await VersionAsync<GeneralSettings>(), Is.EqualTo(version), "nothing saved");
        });
    }

    [Test]
    public async Task ASaveMadeMeanwhile_IsNotOverwritten()
    {
        var read = await VersionAsync<GeneralSettings>();
        await Settings.SaveAsync(new GeneralSettings { History = new HistorySettings { DraftDays = 7, DraftCount = 3 } }, read, null);

        var answer = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = read, values = new { history = new { draftDays = 30, draftCount = 5 } } });

        Assert.Multiple(async () =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("409 concurrency_conflict"), answer.ToString());
            Assert.That((await Settings.GetAsync<GeneralSettings>()).Value.History.DraftDays, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task TheSections_NeedTheRightToManageSettings_AndASectionThatIsNoneIsNotFound()
    {
        var version = await VersionAsync<GeneralSettings>();
        var read = await Api.CallAsync("designer", HttpMethod.Get, General);
        var save = await Api.CallAsync("designer", HttpMethod.Put, General, new { version, values = new { history = new { draftDays = 30, draftCount = 5 } } });
        var none = await Api.CallAsync(Admin, HttpMethod.Get, "settings/colours");

        Assert.Multiple(async () =>
        {
            Assert.That(read.Outcome, Is.EqualTo("403 forbidden"), read.ToString());
            Assert.That(save.Outcome, Is.EqualTo("403 forbidden"), save.ToString());
            Assert.That(none.Outcome, Is.EqualTo("404 settings_section_not_found"), none.ToString());
            Assert.That(await VersionAsync<GeneralSettings>(), Is.EqualTo(version));
        });
    }

    [Test]
    public async Task ASiteSave_KeepsTheFieldsItNamesAsItsOwn_AndAKeyThatNamesNoContentRoot_IsRefusedWithNothingWritten()
    {
        var site = await RootAsync("Lindenhall");
        var page = Guid.NewGuid();
        await Settings.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, await VersionAsync<GeneralSettings>(), null);

        var listed = await Api.CallAsync(Admin, HttpMethod.Get, General);
        var own = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = 0, site, values = new { timeZone = "Europe/Zurich" }, overrides = new[] { "timeZone" } });
        var read = await Api.CallAsync(Admin, HttpMethod.Get, $"{General}?site={site}");
        var stranger = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = 0, site = page, values = new { timeZone = "Europe/Zurich" }, overrides = new[] { "timeZone" } });
        var strangerRead = await Api.CallAsync(Admin, HttpMethod.Get, $"{General}?site={page}");
        var shared = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = own.Body?["version"]?.GetValue<int>() ?? 0, site, values = new { history = new { draftDays = 5, draftCount = 1 } }, overrides = new[] { "timeZone", "history" } });

        Assert.Multiple(async () =>
        {
            Assert.That(listed.Body!["sites"]!.AsArray().Select(each => (each!["key"]!.GetValue<Guid>(), each["name"]!.GetValue<string>(), each["hasOwnValues"]!.GetValue<bool>())), Is.EqualTo(new[] { (site, "Lindenhall", false) }));
            Assert.That(own.Outcome, Is.EqualTo("200"), own.ToString());
            Assert.That((read.Body!["values"]!["timeZone"]!.GetValue<string>(), read.Body["globalValues"]!["timeZone"]!.GetValue<string>()), Is.EqualTo(("Europe/Zurich", "Europe/Berlin")));
            Assert.That(read.Body["overrides"]!.AsArray().Select(field => field!.GetValue<string>()), Is.EqualTo(new[] { "timeZone" }));
            Assert.That(stranger.Outcome, Is.EqualTo("400 site_unknown"), stranger.ToString());
            Assert.That(strangerRead.Outcome, Is.EqualTo("400 site_unknown"), strangerRead.ToString());
            Assert.That(await Settings.SitesOfAsync<GeneralSettings>(), Is.EqualTo(new[] { site }), "nothing written for the key that names no root");
            Assert.That(shared.Outcome, Is.EqualTo("400 validation_failed"), shared.ToString());
            Assert.That(Fields(shared), Is.EqualTo(new[] { ("general.history", "scope") }), "every site shares History");
            Assert.That((await Settings.GetAsync<GeneralSettings>()).Value.TimeZone, Is.EqualTo("Europe/Berlin"), "all sites keep theirs");
        });
    }

    [Test]
    public async Task ATimeZoneFromTheList_Saves_AndOneTheServerCannotResolve_IsRefusedAtItsBox()
    {
        var version = await VersionAsync<GeneralSettings>();
        var picked = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version, values = new { timeZone = "America/Argentina/Buenos_Aires" } });
        var typed = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = version + 1, values = new { timeZone = "europe/berlin" } });

        // Browsers list some zones by their older names, which the server takes as spelled.
        var listed = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = version + 1, values = new { timeZone = "America/Buenos_Aires" } });

        Assert.Multiple(async () =>
        {
            Assert.That(picked.Outcome, Is.EqualTo("200"), picked.ToString());
            Assert.That(picked.Body!["values"]!["timeZone"]!.GetValue<string>(), Is.EqualTo("America/Argentina/Buenos_Aires"));
            Assert.That(typed.Outcome, Is.EqualTo("400 validation_failed"), typed.ToString());
            Assert.That(Fields(typed), Is.EqualTo(new[] { ("general.timeZone", "timeZone") }));
            Assert.That(listed.Outcome, Is.EqualTo("200"), listed.ToString());
            Assert.That((await Settings.GetAsync<GeneralSettings>()).Value.TimeZone, Is.EqualTo("America/Buenos_Aires"));
        });
    }

    /// <summary>
    /// The backoffice's keys the six sections' screens are served with (labels, help, units, choices, what the
    /// screens' facts name, and the boxes the fields lie in), as the committed snapshot
    /// <c>docs/api/settings-keys.json</c> holds them, which the backoffice's dictionaries' check reads (ROADMAP 14.1).
    /// After a deliberate change, run with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails.
    /// </summary>
    // The screens' editors that draw a line under each choice (settings-editors.ts).
    private static readonly HashSet<string> LinedEditors = new(["choiceLines", "ipHandling", "captchaConsent", "protectionLevel"], StringComparer.Ordinal);

    [Test]
    public async Task TheSectionsKeys_AreTheReviewedSnapshot()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var section in new[] { "general", "email", "protection", "files", "integrations", "privacy" })
        {
            var answer = await Api.CallAsync(Admin, HttpMethod.Get, $"settings/{section}");
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Collect(answer.Body!["fields"], keys);
            Collect(answer.Body!["facts"], keys);
            // Each box the fields lie in, whose heading the screen names by the section and the box, and the line under each
            // choice of a field whose editor draws one.
            foreach (var field in answer.Body!["fields"]!.AsArray())
            {
                if (field!["group"]?.GetValue<string>() is { Length: > 0 } box)
                {
                    keys.Add($"jfbGlobalSettings_{section}Box_{box}");
                }

                if (field["editor"]?.GetValue<string>() is { } editor && LinedEditors.Contains(editor))
                {
                    foreach (var choice in field["choices"]!.AsArray())
                    {
                        if (choice!["label"]?.GetValue<string>() is { } label && label.StartsWith('#'))
                        {
                            keys.Add(label[1..] + "Line");
                        }
                    }
                }
            }
        }

        var path = RepositoryRoot.Path(typeof(SettingsApiTests).Assembly, "docs", "api", "settings-keys.json");
        var served = new JsonArray([.. keys.Select(key => JsonValue.Create(key))]).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await System.IO.File.WriteAllTextAsync(path, served, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {path}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(keys, Has.Count.GreaterThan(100));
        Assert.That(System.IO.File.Exists(path), Is.True, $"the reviewed snapshot {path}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await System.IO.File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the sections' keys changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");

        static void Collect(JsonNode? node, SortedSet<string> into)
        {
            switch (node)
            {
                case JsonValue value when value.TryGetValue<string>(out var text) && text.StartsWith('#'):
                    into.Add(text[1..]);
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Collect(item, into);
                    }

                    break;
                case JsonObject item:
                    foreach (var (_, inner) in item)
                    {
                        Collect(inner, into);
                    }

                    break;
            }
        }
    }

    [Test]
    public async Task EachSection_SaysWhatItsScreenNeedsBesidesItsFields()
    {
        var general = await Api.CallAsync(Admin, HttpMethod.Get, General);
        var email = await Api.CallAsync(Admin, HttpMethod.Get, "settings/email");
        var protection = await Api.CallAsync(Admin, HttpMethod.Get, "settings/protection");
        var files = await Api.CallAsync(Admin, HttpMethod.Get, "settings/files");
        var integrations = await Api.CallAsync(Admin, HttpMethod.Get, "settings/integrations");
        var privacy = await Api.CallAsync(Admin, HttpMethod.Get, "settings/privacy");

        Assert.Multiple(() =>
        {
            var site = general.Body!["facts"]!["general"]!;
            Assert.That(site["defaultTheme"]!.GetValue<string>(), Is.EqualTo(BuiltInThemes.DefaultAlias));
            Assert.That(site["themes"]!.AsArray().Select(theme => theme!["value"]!.GetValue<string>()), Does.Contain(BuiltInThemes.DefaultAlias));
            Assert.That(JsonNode.DeepEquals(site["formBlock"], JsonNode.Parse("""{"exists":false,"createdAt":null,"canCreate":true}""")), Is.True, site.ToJsonString());
            Assert.That(email.Body!["facts"]!["email"]!["sendingKey"]!.GetValue<string>(), Is.EqualTo("Umbraco:CMS:Global:Smtp"));
            Assert.That(email.Body["facts"]!["email"]!["layouts"]!.AsArray().Take(2).Select(layout => layout!["value"]!.GetValue<string>()), Is.EqualTo(new[] { EmailLayouts.CardKey.ToString(), EmailLayouts.LetterKey.ToString() }));
            var held = files.Body!["facts"]!["files"]!;
            Assert.That((held["hostMaxFileBytes"]!.GetValue<long>(), held["maxFileBytesKey"]!.GetValue<string>(), held["scanners"]!.GetValue<int>(), held["folder"]!.GetValue<bool>()), Is.EqualTo((FormBuilderFilesOptions.DefaultMaxFileBytes, "JironFormBuilder:Files:MaxFileBytes", 0, true)));
            Assert.That(held["groups"]!.AsArray().Select(group => group!["value"]!.GetValue<string>()), Is.EqualTo(new[] { "images", "pdf", "office", "text", "archives" }));
            Assert.That(held["blockedExtensions"]!.AsArray().Select(extension => extension!.GetValue<string>()), Does.Contain("exe"));
            var captchas = protection.Body!["facts"]!["protection"]!["captchas"]!.AsArray();
            Assert.That(captchas.Select(captcha => (captcha!["alias"]!.GetValue<string>(), captcha["hasKeys"]!.GetValue<bool>(), captcha["keysAt"]!.GetValue<string>())), Is.EqualTo(new[]
            {
                ("recaptchaV2", false, "JironFormBuilder:Captcha:RecaptchaV2"),
                ("recaptchaV3", false, "JironFormBuilder:Captcha:RecaptchaV3"),
                ("hcaptcha", false, "JironFormBuilder:Captcha:Hcaptcha"),
            }));
            var consent = protection.Body["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "captchaConsent")!;
            Assert.That((consent["lock"], protection.Body["facts"]!["protection"]!["implicitConsent"]!.GetValue<bool>()), Is.EqualTo(((JsonNode?)null, false)), "a restriction leaves the field editable");
            Assert.That(JsonNode.DeepEquals(protection.Body["locks"], JsonNode.Parse("""[{"field":"captchaConsent","kind":"restricted","configurationKey":"JironFormBuilder:Privacy:AllowImplicitCaptchaConsent"}]""")), Is.True, "the screen greys out the implicit mode with its key");
            Assert.That(JsonNode.DeepEquals(integrations.Body!["facts"]!["integrations"], JsonNode.Parse("""{"allowedHosts":[],"allowHttp":false,"found":[]}""")), Is.True, integrations.Text);
            Assert.That((privacy.Outcome, privacy.Body!["sites"]!.AsArray().Count), Is.EqualTo(("200", 0)), "every site shares the privacy settings");
        });
    }

    [Test]
    public async Task Files_SavesTheTypesAndTheLargestFile_AndRefusesASizeAboveTheHostsCap_OrNoType()
    {
        var version = await VersionAsync<FilesSettings>();
        var saved = await Api.CallAsync(Admin, HttpMethod.Put, "settings/files", new { version, values = new { allowedTypes = new[] { "images", "pdf" }, maxFileBytes = 1_000_000 } });
        var above = await Api.CallAsync(Admin, HttpMethod.Put, "settings/files", new { version = version + 1, values = new { maxFileBytes = FormBuilderFilesOptions.DefaultMaxFileBytes + 1 } });
        var none = await Api.CallAsync(Admin, HttpMethod.Put, "settings/files", new { version = version + 1, values = new { allowedTypes = Array.Empty<string>() } });
        var stored = (await Settings.GetAsync<FilesSettings>()).Value;

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(JsonNode.DeepEquals(saved.Body!["values"], JsonNode.Parse("""{"allowedTypes":["images","pdf"],"maxFileBytes":1000000}""")), Is.True, saved.Text);
            Assert.That(JsonNode.DeepEquals(saved.Body["locks"], JsonNode.Parse("""[{"field":"maxFileBytes","kind":"clamped","configurationKey":"JironFormBuilder:Files:MaxFileBytes"}]""")), Is.True, "the cap is named, and the field stays editable");
            Assert.That(saved.Body["fields"]!.AsArray().All(field => field!["lock"] is null), Is.True);
            Assert.That(Fields(above), Is.EqualTo(new[] { ("files.maxFileBytes", "range") }), above.ToString());
            Assert.That(Fields(none), Is.EqualTo(new[] { ("files.allowedTypes", "range") }), none.ToString());
            Assert.That((stored.AllowedTypes, stored.MaxFileBytes), Is.EqualTo((new[] { FileTypeGroup.Images, FileTypeGroup.Pdf }, (long?)1_000_000)), "only the first save is kept");
        });
    }

    [Test]
    public async Task Integrations_SaysWhichSavedReferences_TheHostConfigurationHoldsASecretFor()
    {
        var version = await VersionAsync<IntegrationSettings>();
        var saved = await Api.CallAsync(Admin, HttpMethod.Put, "settings/integrations", new { version, values = new { signingSecret = "$JironFormBuilder:Secrets:Signing", slackWebhook = "$JironFormBuilder:Secrets:Missing" } });
        var plain = await Api.CallAsync(Admin, HttpMethod.Put, "settings/integrations", new { version = version + 1, values = new { teamsWebhook = "https://example.test/hook" } });

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["facts"]!["integrations"]!["found"]!.AsArray().Select(name => name!.GetValue<string>()), Is.EqualTo(new[] { "signingSecret" }));
            Assert.That(Fields(plain), Is.EqualTo(new[] { ("integrations.teamsWebhook", "secretReference") }), "the address itself is never saved");
        });
    }

    [Test]
    public async Task TheDraftCleanup_KeepsDraftSaves_AsTheSavedSettingsSay()
    {
        var form = await FormAsync("History kept");
        await SaveDraftsAsync(form, 5);

        // Every draft save is older than a day once the cleanup runs two days on; the newest are kept by number.
        var version = await VersionAsync<GeneralSettings>();
        var four = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version, values = new { history = new { draftDays = 1, draftCount = 4 } } });
        await CleanUpAsync();
        var afterFour = await NumbersAsync(form);
        var two = await Api.CallAsync(Admin, HttpMethod.Put, General, new { version = version + 1, values = new { history = new { draftDays = 1, draftCount = 2 } } });
        await CleanUpAsync();
        var afterTwo = await NumbersAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That((four.Outcome, two.Outcome), Is.EqualTo(("200", "200")), $"{four} / {two}");
            Assert.That(afterFour, Is.EqualTo(new[] { 6, 5, 4, 3 }), "the four newest of six");
            Assert.That(afterTwo, Is.EqualTo(new[] { 6, 5 }), "the two newest, as the changed setting says");
        });
    }

    // The version of the stored global values of a section, which a save sends back.
    private async Task<int> VersionAsync<T>()
        where T : class, ISettingsSection, new() => (await Settings.GetAsync<T>()).Version;

    // The fields and codes of a refusal's errors.
    private static (string Field, string Code)[] Fields(ApiAnswer answer) =>
        [.. answer.Body!["errors"]!.AsArray().Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()))];

    // A page at the root of Content, which is a site.
    private async Task<Guid> RootAsync(string name)
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbHome", Name = "Home", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        var contents = GetRequiredService<IContentService>();
        var page = new Content(name, -1, type);
        contents.Save(page);
        return page.Key;
    }

    // Saves the form's draft this many times, each with another label, so each is a draft save of its own.
    private async Task SaveDraftsAsync(Guid formKey, int saves)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        for (var save = 1; save <= saves; save++)
        {
            var draft = (await forms.GetDraftAsync(formKey))!;
            var field = NameField with { Label = new LocalizedText([new(string.Empty, $"Name {save}")]) };
            var definition = draft.Definition! with { Pages = [draft.Definition!.Pages[0] with { Groups = [draft.Definition.Pages[0].Groups[0] with { Fields = [field] }] }] };
            _ = (await forms.SaveDraftAsync(formKey, draft.Form.ConcurrencyVersion, definition, null, null))!;
        }
    }

    // Runs the draft cleanup as the retention job would, two days on.
    private async Task CleanUpAsync()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow.AddDays(2));
        await using var scope = Services.CreateAsyncScope();
        var cleanup = ActivatorUtilities.CreateInstance<DraftVersionCleanup>(scope.ServiceProvider, (TimeProvider)clock);
        await cleanup.RunAsync(clock.GetUtcNow() + FormBuilderRetentionJob.Budget, CancellationToken.None);
    }

    // The form's versions, newest first, as the History lists them with the draft saves.
    private async Task<int[]> NumbersAsync(Guid formKey)
    {
        var answer = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{formKey}/versions?includeDrafts=true&take=50");
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        return [.. answer.Body!["items"]!.AsArray().Select(item => item!["version"]!["number"]!.GetValue<int>())];
    }
}

/// <summary>A site whose host locks the general section (ADR-0008, ADR-0198): the endpoint names the key, and refuses a save.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class LockedSettingsApiTests : ManagementApiTestBase
{
    /// <summary>Locks the general section, and allows the implicit captcha consent.</summary>
    public LockedSettingsApiTests()
    {
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:0"] = "general";
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":Privacy:AllowImplicitCaptchaConsent"] = "true";
    }

    [Test]
    public async Task ALockedSection_IsAnsweredWithItsKey_EachFieldLockedWithIt_AndASaveIsRefused()
    {
        var read = await Api.CallAsync("admin", HttpMethod.Get, "settings/general");
        var save = await Api.CallAsync("admin", HttpMethod.Put, "settings/general", new { version = 0, values = new { history = new { draftDays = 30, draftCount = 5 } } });
        var protection = await Api.CallAsync("admin", HttpMethod.Get, "settings/protection");

        Assert.Multiple(() =>
        {
            Assert.That(read.Outcome, Is.EqualTo("200"), read.ToString());
            Assert.That(JsonNode.DeepEquals(read.Body!["locks"], JsonNode.Parse("""[{"field":"*","kind":"locked","configurationKey":"JironFormBuilder:LockedSections:0"}]""")), Is.True, read.Text);
            Assert.That(read.Body["fields"]!.AsArray().Select(field => field!["lock"]!["configurationKey"]!.GetValue<string>()).Distinct(), Is.EqualTo(new[] { "JironFormBuilder:LockedSections:0" }));
            Assert.That(save.Outcome, Is.EqualTo("409 setting_locked"), save.ToString());
            Assert.That(save.Body!["configurationKey"]!.GetValue<string>(), Is.EqualTo("JironFormBuilder:LockedSections:0"));
            Assert.That((protection.Body!["facts"]!["protection"]!["implicitConsent"]!.GetValue<bool>(), protection.Body["locks"]!.AsArray().Count), Is.EqualTo((true, 0)), "the host allows the implicit mode");
        });
    }
}
