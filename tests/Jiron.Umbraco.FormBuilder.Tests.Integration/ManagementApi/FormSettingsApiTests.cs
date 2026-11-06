using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// What a form's Settings view reads of the site (ROADMAP 10.1a, ADR-0184): the captcha providers with whether each has
/// its keys and the settings it describes, never a key, as the reviewed snapshot <c>docs/api/captcha-providers.json</c>
/// holds them on a site with reCAPTCHA v2's keys; the site's defaults, time zone, themes and member groups; and the
/// bounds the save checks hold settings to; and the email layouts an action may choose, with the one a mail has by default
/// (ADR-0189).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormSettingsApiTests : ManagementApiTestBase
{
    private const string SiteKey = "JFB_FAKE-v2-site-key";
    private const string SecretKey = "JFB_FAKE-v2-secret-key";

    [Test]
    public async Task TheProviders_SayWhetherEachHasItsKeys_AndItsSettings_NeverAKey()
    {
        var providers = await CallAsync("designer", HttpMethod.Get, "captcha-provider");

        Assert.Multiple(() =>
        {
            Assert.That(providers.Status, Is.EqualTo(HttpStatusCode.OK), providers.ToString());
            var items = providers.Body!.AsArray();
            Assert.That(
                items.Select(item => (item!["name"]!.GetValue<string>(), item["label"]!.GetValue<string>(), item["isConfigured"]!.GetValue<bool>())),
                Is.EqualTo(new[] { (RecaptchaV2Provider.ChosenAs, "reCAPTCHA v2", true), (RecaptchaV3Provider.ChosenAs, "reCAPTCHA v3", false), (HcaptchaProvider.ChosenAs, "hCaptcha", false) }));
            var threshold = items[1]!["settings"]![0]!;
            Assert.That(
                (threshold["name"]!.GetValue<string>(), threshold["kind"]!.GetValue<string>(), threshold["default"]!.GetValue<string>(), threshold["rules"]!["minimum"]!.GetValue<string>(), threshold["rules"]!["maximum"]!.GetValue<string>()),
                Is.EqualTo(("threshold", "number", "0.5", "0", "1")));
            Assert.That((items[0]!["settings"]!.AsArray().Count, items[2]!["settings"]!.AsArray().Count), Is.EqualTo((0, 0)));
            Assert.That(providers.Text, Does.Not.Contain(SiteKey).And.Not.Contain(SecretKey), "never a key");
        });
    }

    [Test]
    public async Task TheBuiltInProviders_AreServedAsTheReviewedSnapshotHolds()
    {
        var answer = await CallAsync("designer", HttpMethod.Get, "captcha-provider");
        var served = answer.Body!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        var path = RepositoryRoot.Path(typeof(FormSettingsApiTests).Assembly, "docs", "api", "captcha-providers.json");
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await System.IO.File.WriteAllTextAsync(path, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {path}. Review the diff, check the backoffice's dictionaries, commit it, and run again without it.");
        }

        Assert.That((await System.IO.File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the captcha providers changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task AFormsChoices_AreTheSitesDefaults_Labels_TimeZone_Themes_MemberGroupsByName_AndTheSaveChecksBounds()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var protection = await settings.GetAsync<ProtectionSettings>();
        await settings.SaveAsync(protection.Value with { Level = ProtectionLevel.ProofOfWork, ContentRules = ContentRuleLevel.Strict }, protection.Version, null);
        var general = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(general.Value with { SubmitLabel = new Dictionary<string, string> { [""] = "Send", ["de-DE"] = "Senden" } }, general.Version, null);
        await GetRequiredService<IThemeRepository>().CreateAsync(new NewTheme(Guid.NewGuid(), "brand", "Brand", "default"));
        var groups = GetRequiredService<IMemberGroupService>();
        var volunteers = (await groups.CreateAsync(new MemberGroup { Name = "Volunteers" })).Result!;
        var alumni = (await groups.CreateAsync(new MemberGroup { Name = "Alumni" })).Result!;

        var form = await FormAsync("Contact");

        var choices = await CallAsync("designer", HttpMethod.Get, $"form/{form}/setting-choices");

        Assert.Multiple(() =>
        {
            Assert.That(choices.Status, Is.EqualTo(HttpStatusCode.OK), choices.ToString());
            var body = choices.Body!;
            Assert.That(body["timeZone"]!.GetValue<string>(), Is.EqualTo(new GeneralSettings().TimeZone));
            var defaults = body["defaults"]!;
            Assert.That(
                (defaults["protection"]!.GetValue<string>(), defaults["contentRules"]!.GetValue<string>(), defaults["requiredMarker"]!.GetValue<string>(), defaults["theme"]!.GetValue<string>()),
                Is.EqualTo(("proofOfWork", "strict", "optional", "default")),
                "what the site's settings say");
            Assert.That(defaults["submitLabel"]!.ToJsonString(), Is.EqualTo("""{"":"Send","de-DE":"Senden"}"""), "the site's own label");
            Assert.That((defaults["nextLabel"], defaults["backLabel"]), Is.EqualTo(((JsonNode?)null, (JsonNode?)null)), "none for the package's words");
            Assert.That(
                body["themes"]!.AsArray().Select(theme => theme!["alias"]!.GetValue<string>()),
                Is.EqualTo(new[] { "default", "unstyled", "bootstrap5", "brand" }),
                "the code's themes, then the library's");
            var memberGroups = body["memberGroups"]!.AsArray().Select(group => (Key: group!["key"]!.GetValue<Guid>(), Name: group["name"]!.GetValue<string>())).ToList();
            Assert.That(memberGroups, Does.Contain((alumni.Key, "Alumni")).And.Contain((volunteers.Key, "Volunteers")));
            Assert.That(memberGroups.Select(group => group.Name), Is.Ordered.Using((IComparer<string>)StringComparer.CurrentCultureIgnoreCase), "by name");
            var limits = body["limits"]!;
            Assert.That(
                (limits["lineLength"]!.GetValue<int>(), limits["htmlLength"]!.GetValue<int>(), limits["nameLength"]!.GetValue<int>(), limits["verificationDays"]!.GetValue<int>(), limits["referenceDigits"]!.GetValue<int>()),
                Is.EqualTo((DefinitionSaveChecks.LineLength, DefinitionSaveChecks.HtmlLength, DefinitionStructureChecks.MaxNameLength, DefinitionSaveChecks.MaxVerificationDays, DefinitionSaveChecks.MaxReferenceDigits)));
            Assert.That(limits["outcomes"]!.GetValue<int>(), Is.EqualTo(GetRequiredService<DefinitionLimits>().MaxOutcomes), "the host's limit of outcomes");
            Assert.That(limits["actions"]!.GetValue<int>(), Is.EqualTo(GetRequiredService<DefinitionLimits>().MaxActions), "the host's limit of actions");
        });
    }

    [Test]
    public async Task AFormsChoices_ListTheEmailLayouts_BuiltInFirst_WithTheSitesDefault_AndTheCardForADefaultThatIsGone()
    {
        var library = GetRequiredService<IEmailLayoutRepository>();
        var zebra = await library.CreateAsync(new NewEmailLayout(Guid.NewGuid(), "zebra", "Zebra"));
        var brand = await library.CreateAsync(new NewEmailLayout(Guid.NewGuid(), "brand", "Brand mail"));
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var email = await settings.GetAsync<EmailSettings>();
        await settings.SaveAsync(email.Value with { DefaultLayout = brand.Key }, email.Version, null);
        var form = await FormAsync("Contact");

        var named = (await CallAsync("designer", HttpMethod.Get, $"form/{form}/setting-choices")).Body!;
        email = await settings.GetAsync<EmailSettings>();
        await settings.SaveAsync(email.Value with { DefaultLayout = Guid.NewGuid() }, email.Version, null);
        var gone = (await CallAsync("designer", HttpMethod.Get, $"form/{form}/setting-choices")).Body!;

        Assert.Multiple(() =>
        {
            Assert.That(
                named["emailLayouts"]!.AsArray().Select(layout => (layout!["key"]!.GetValue<Guid>(), layout["name"]!.GetValue<string>())),
                Is.EqualTo(new[] { (EmailLayouts.CardKey, "#jfbEmailLayouts_card"), (EmailLayouts.LetterKey, "#jfbEmailLayouts_letter"), (brand.Key, "Brand mail"), (zebra.Key, "Zebra") }),
                "the built-in card and letter, then the library's by name");
            Assert.That(named["defaults"]!["emailLayout"]!.GetValue<Guid>(), Is.EqualTo(brand.Key), "the site's default");
            Assert.That(gone["defaults"]!["emailLayout"]!.GetValue<Guid>(), Is.EqualTo(EmailLayouts.CardKey), "a default that is gone gives way to the card, as a mail's does");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        configuration["JironFormBuilder:Captcha:RecaptchaV2:SiteKey"] = SiteKey;
        configuration["JironFormBuilder:Captcha:RecaptchaV2:SecretKey"] = SecretKey;
    }
}
