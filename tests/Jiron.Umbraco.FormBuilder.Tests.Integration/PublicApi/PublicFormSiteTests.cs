using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// What the form endpoint takes from the site (ADR-0008, ADR-0084, ADR-0095): the time zone of the site the page belongs
/// to, or the global one; UTC with a warning for a stored zone this server cannot resolve; and of every settings section,
/// only what the allowlist names: the time zone, the required-marker style and the buttons' labels.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormSiteTests : PublicApiTestBase
{
    private IContentService Contents => GetRequiredService<IContentService>();

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    [Test]
    public async Task ThePage_SetsTheSite_WhoseTimeZoneTheFormRunsIn()
    {
        var key = await PublishAsync();
        var (shop, contact, other, draft) = await SitesAsync();
        await Settings.SaveSiteAsync(shop, new GeneralSettings { TimeZone = "Pacific/Auckland" }, ["timeZone"], 0, Editor);
        var global = await Settings.GetAsync<GeneralSettings>();
        await Settings.SaveAsync(global.Value with { TimeZone = "Europe/Berlin" }, global.Version, Editor);

        var answers = new Dictionary<string, string>();
        foreach (var (what, query) in new[]
        {
            ("a page of the site", $"?contentKey={contact}"),
            ("the site's root", $"?contentKey={shop:N}"),
            ("a page of a site without its own", $"?contentKey={other}"),
            ("a page not published", $"?contentKey={draft}"),
            ("a key of no page", $"?contentKey={Guid.NewGuid()}"),
            ("no page", string.Empty),
        })
        {
            using var response = await GetAsync($"{Root}/forms/{key}{query}");
            answers[what] = (await JsonAsync(response))["timeZone"]!.GetValue<string>();
        }

        using var unreadable = await GetAsync($"{Root}/forms/{key}?contentKey=page-1");
        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["a page of the site"] = "Pacific/Auckland",
                ["the site's root"] = "Pacific/Auckland",
                ["a page of a site without its own"] = "Europe/Berlin",
                ["a page not published"] = "Europe/Berlin",
                ["a key of no page"] = "Europe/Berlin",
                ["no page"] = "Europe/Berlin",
            }), string.Join(" | ", answers));
            Assert.That(await RefusalAsync(unreadable), Is.EqualTo("400 invalid_request"));
        });
    }

    [Test]
    public async Task AStoredTimeZoneThisServerCannotResolve_RunsAsUtc_WithOneWarning()
    {
        var key = await PublishAsync();
        var (shop, contact, _, _) = await SitesAsync();

        // A row from a server whose time zone data knew the zone; a save here refuses it.
        _ = await GetRequiredService<ISettingsRepository>().SaveAsync(new SettingsSave(SettingsScopes.ForSite(shop), FormBuilderSettingsSections.General, GeneralSettings.SchemaVersion, 0, """{"timeZone":"Mars/Olympus_Mons"}""", null));
        var zones = new List<string>();
        for (var time = 0; time < 3; time++)
        {
            using var response = await GetAsync($"{Root}/forms/{key}?contentKey={contact}");
            zones.Add((await JsonAsync(response))["timeZone"]!.GetValue<string>());
        }

        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(SiteTimeZones).FullName).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(zones, Is.All.EqualTo("UTC"));
            Assert.That(warnings.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }), "once, however often it is asked for");
            Assert.That(warnings[0].Message, Does.Contain("general.timeZone").And.Contain("Mars/Olympus_Mons").And.Contain(shop.ToString()));
        });
    }

    [Test]
    public async Task OfEverySettingsSection_TheAnswerCarriesOnlyWhatTheAllowlistNames()
    {
        var key = await PublishAsync();
        using var before = await GetAsync($"{Root}/forms/{key}");
        var unset = await JsonAsync(before);

        // Every field of every section differs from its default; the guards fail for a section or field this test does not fill.
        var filled = new GeneralSettings
        {
            TimeZone = "Pacific/Chatham",
            BaseUrl = "https://jfb-allowlist.example",
            RequiredMarker = RequiredMarker.None,
            SubmitLabel = new Dictionary<string, string> { [""] = "Send for the allowlist" },
            NextLabel = new Dictionary<string, string> { [""] = "Next for the allowlist" },
            BackLabel = new Dictionary<string, string> { [""] = "Back for the allowlist" },
            Theme = "bootstrap5",
            NewForms = new NewFormDefaults { Consent = new NewFormConsent { Label = new Dictionary<string, string> { [""] = "JFB_TEST consent label" }, Text = new Dictionary<string, string> { [""] = "<p>JFB_TEST consent text</p>" } } },
            History = new HistorySettings { DraftDays = 7, DraftCount = 3 },
        };
        var current = await Settings.GetAsync<GeneralSettings>();
        await Settings.SaveAsync(filled, current.Version, Editor);
        var protection = new ProtectionSettings
        {
            Level = ProtectionLevel.Captcha,
            CaptchaProvider = HcaptchaProvider.ChosenAs,
            ProofBits = 12,
            BlockedRanges = ["203.0.113.0/24"],
            ContentRules = ContentRuleLevel.Strict,
            BlockedWords = ["JFB_TEST blocked word"],
            BlockedDomains = ["jfb-allowlist-blocked.example"],
            MaxLinks = 7,
        };
        var currentProtection = await Settings.GetAsync<ProtectionSettings>();
        await Settings.SaveAsync(protection, currentProtection.Version, Editor);
        using var after = await GetAsync($"{Root}/forms/{key}");
        var set = await JsonAsync(after);
        var allowed = set.DeepClone();
        allowed["timeZone"] = unset["timeZone"]!.DeepClone();
        var settings = allowed["form"]!["settings"]!.AsObject();
        var defaults = unset["form"]!["settings"]!.AsObject();
        foreach (var name in new[] { "requiredMarker", "submitLabel", "nextLabel", "backLabel", "theme" })
        {
            settings.Remove(name);
            if (defaults[name] is { } shown)
            {
                settings[name] = shown.DeepClone();
            }
        }

        var sections = typeof(GeneralSettings).Assembly.GetTypes().Where(type => typeof(ISettingsSection).IsAssignableFrom(type) && !type.IsInterface).ToList();
        var unchanged = Leaves(filled, new GeneralSettings(), nameof(GeneralSettings)).Concat(Leaves(protection, new ProtectionSettings(), nameof(ProtectionSettings))).Where(leaf => leaf.Filled == leaf.Default).Select(leaf => leaf.Path).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(sections, Is.EquivalentTo(new[] { typeof(GeneralSettings), typeof(ProtectionSettings) }), "a new section is filled here too");
            Assert.That(unchanged, Is.Empty, "a new field is filled here too");
            Assert.That(set["timeZone"]!.GetValue<string>(), Is.EqualTo("Pacific/Chatham"), "visitors get the time zone");
            Assert.That(set["form"]!["settings"]!["requiredMarker"]!.GetValue<string>(), Is.EqualTo("none"), "the style of a form that inherits it");
            Assert.That(set["form"]!["settings"]!["submitLabel"]!.GetValue<string>(), Is.EqualTo(defaults["submitLabel"]?.GetValue<string>() ?? "Send for the allowlist"), "and a label the form has not");
            Assert.That(set["form"]!["settings"]!["theme"]!.GetValue<string>(), Is.EqualTo(defaults["theme"]?.GetValue<string>() ?? "bootstrap5"), "and the site's theme for a form without one (ADR-0105)");
            Assert.That(JsonNode.DeepEquals(allowed, unset), Is.True, $"nothing else of the settings shows:\n{unset.ToJsonString()}\n{set.ToJsonString()}");
            Assert.That(set.ToJsonString(), Does.Not.Contain("JFB_TEST").And.Not.Contain("jfb-allowlist"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    // Every value a section holds, nested records' values included, as JSON beside the default's.
    private static IEnumerable<(string Path, string Filled, string Default)> Leaves(object? filled, object? defaults, string path)
    {
        var type = filled?.GetType() ?? defaults?.GetType();
        if (type is { IsClass: true } && type.Namespace == typeof(GeneralSettings).Namespace)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var leaf in Leaves(filled is null ? null : property.GetValue(filled), defaults is null ? null : property.GetValue(defaults), $"{path}.{property.Name}"))
                {
                    yield return leaf;
                }
            }

            yield break;
        }

        yield return (path, JsonSerializer.Serialize(filled), JsonSerializer.Serialize(defaults));
    }

    // Two sites, each a published root with a published page; one page of the first is not published. In the test host,
    // content made outside a request does not reach the navigation and the publish states, so the test builds both from
    // the database, as a site does when it boots with this content.
    private async Task<(Guid Shop, Guid Contact, Guid Other, Guid Draft)> SitesAsync()
    {
        var types = GetRequiredService<IContentTypeService>();
        var page = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(page, Constants.Security.SuperUserKey)).Success, Is.True);
        page.AllowedContentTypes = [new ContentTypeSort(page.Key, 0, page.Alias)];
        Assert.That((await types.UpdateAsync(page, Constants.Security.SuperUserKey)).Success, Is.True);

        Content Published(string name, Content? parent)
        {
            var content = parent is null ? new Content(name, -1, page) : new Content(name, parent, page);
            Contents.Save(content);
            Contents.Publish(content, ["*"]);
            return content;
        }

        var shop = Published("Shop", null);
        var contact = Published("Contact", shop);
        var other = Published("Other", null);
        var otherPage = Published("About", other);
        var draft = new Content("Coming soon", shop, page);
        Contents.Save(draft);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return (shop.Key, contact.Key, otherPage.Key, draft.Key);
    }
}
