using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// What the designer's canvas draws a form with (ADR-0168): the rendering words in one of the site's languages, the
/// site's languages (ADR-0170), the default theme's classes and stylesheets, and the site's marker, for any user of the
/// Forms section. The answer in the
/// site's default language, without the stylesheets' fingerprints, matches the committed snapshot
/// <c>docs/api/canvas.json</c>, which the backoffice's preview tests draw with. After a deliberate change, run the
/// fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class CanvasApiTests : ManagementApiTestBase
{
    private const string Canvas = "canvas";

    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(CanvasApiTests).Assembly, "docs", "api", "canvas.json");

    [Test]
    public async Task TheDefaultLanguage_IsServedToAUserOfTheSectionWithoutGrants_AsTheReviewedSnapshotHolds()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, Canvas);
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var body = answer.Body!.AsObject();
        Assert.That(body["stylesheets"]!.AsArray().Select(Text), Is.EqualTo(GetRequiredService<PageAssets>().Addresses(PathString.Empty, new DefaultTheme()).Styles), "the base rules, then the default theme's, each with its fingerprint");

        body.Remove("stylesheets");
        var served = body.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await System.IO.File.WriteAllTextAsync(SnapshotPath, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(System.IO.File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await System.IO.File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the canvas's words or classes changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task ALanguageOfTheSite_GetsItsWords_AndOneTheSiteLacks_TheDefaultLanguage()
    {
        var languages = GetRequiredService<ILanguageService>();
        var created = await languages.CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        Assert.That(created.Success, Is.True, created.Status.ToString());

        var german = (await CallAsync("designer", HttpMethod.Get, $"{Canvas}?culture=de-DE")).Body!;
        var lacking = (await CallAsync("designer", HttpMethod.Get, $"{Canvas}?culture=fr-FR")).Body!;
        var nonsense = (await CallAsync("designer", HttpMethod.Get, $"{Canvas}?culture=%3Cb%3E")).Body!;

        Assert.Multiple(() =>
        {
            Assert.That(german["languages"]!.ToJsonString(), Is.EqualTo("""[{"culture":"en-US","name":"English (United States)","isDefault":true},{"culture":"de-DE","name":"German","isDefault":false}]"""), "the site's languages, the default first");
            Assert.That(lacking["languages"]!.ToJsonString(), Is.EqualTo(german["languages"]!.ToJsonString()), "the same in every language");
            Assert.That((Text(german["culture"]), Text(german["words"]!["next"]), Text(german["words"]!["optional"])), Is.EqualTo(("de-DE", "Weiter", "(optional)")));
            Assert.That((Text(lacking["culture"]), Text(lacking["words"]!["next"])), Is.EqualTo(("en-US", "Next")), "French is no language of the site");
            Assert.That(Text(nonsense["culture"]), Is.EqualTo("en-US"));
        });
    }

    [Test]
    public async Task TheMarker_IsTheSitesStyle()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        Assert.That(Text((await CallAsync("designer", HttpMethod.Get, Canvas)).Body!["requiredMarker"]), Is.EqualTo("optional"), "the default style");

        await settings.SaveAsync(general.Value with { RequiredMarker = RequiredMarker.Required }, general.Version, null);

        Assert.That(Text((await CallAsync("designer", HttpMethod.Get, Canvas)).Body!["requiredMarker"]), Is.EqualTo("required"));
    }

    private static string Text(JsonNode? node) => node?.GetValue<string>() ?? string.Empty;
}
