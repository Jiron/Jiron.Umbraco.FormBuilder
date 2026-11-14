using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The library's themes through the management API (ROADMAP 12.1, ADR-0215): the themes in code a theme builds on, with
/// their tokens and defaults, the dark ones only for a theme with a dark scheme, a site's own base theme's included; a
/// theme created under the name-based key of its alias and changed, each problem at its path; the forms that use it,
/// counted and named, and the delete that waits until none does and refuses the site's default; a duplicate's free alias;
/// export and import; and the preview's layer with what the sanitiser took out.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ThemeLibraryApiTests : ManagementApiTestBase
{
    private static readonly object Autumn = new
    {
        theme = new
        {
            alias = "autumn",
            name = "Autumn campaign",
            baseTheme = "default",
            tokens = new
            {
                light = new Dictionary<string, string> { ["--jfb-color-accent"] = "#8a1538", ["--jfb-radius"] = "6px" },
                dark = new Dictionary<string, string> { ["--jfb-color-accent"] = "#f0a5b8" },
            },
            customCss = ".jfb-label { letter-spacing: .02em; }",
        },
    };

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().Themes().Append<AcmeTheme>().Append<PlainTheme>();
    }

    [Test]
    public async Task TheEditor_ListsEveryThemeInCode_WithItsTokensAndDefaults_DarkOnesOnlyWhereItsDarkSchemeIsTrue()
    {
        var answer = await CallAsync("designer", HttpMethod.Get, "theme/editor");

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var bases = answer.Body!["bases"]!.AsArray();
        JsonNode Base(string alias) => bases.Single(each => each!["alias"]!.GetValue<string>() == alias)!;
        JsonNode? Token(string alias, string name) => Base(alias)["tokens"]!.AsArray().SingleOrDefault(token => token!["name"]!.GetValue<string>() == name);
        Assert.Multiple(() =>
        {
            Assert.That(bases.Select(each => each!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "default", "unstyled", "bootstrap5", "acme", "plain" }));
            Assert.That(answer.Body!["defaultTheme"]!.GetValue<string>(), Is.EqualTo("default"));
            Assert.That(Token("default", "--jfb-color-accent")!["default"]!.GetValue<string>(), Is.EqualTo("#0f6e5c"));
            Assert.That(Token("default", "--jfb-color-accent")!["darkDefault"]!.GetValue<string>(), Is.EqualTo("#5fd0b8"));
            Assert.That(Token("default", "--jfb-radius")!["darkDefault"], Is.Null, "a size has no dark value");
            Assert.That(Base("acme")["darkScheme"]!.GetValue<bool>(), Is.True);
            Assert.That(Base("acme")["tokens"]!.AsArray().Select(token => (token!["name"]!.GetValue<string>(), token["default"]!.GetValue<string>(), token["darkDefault"]?.GetValue<string>())), Is.EqualTo(new[]
            {
                ("--jfb-color-ink", "#102030", "#f0f0f0"),
                ("--jfb-gutter", "18px", null),
            }), "a site's own base theme's tokens with their defaults, in its order, the dark one where it declares one");
            Assert.That(Base("plain")["darkScheme"]!.GetValue<bool>(), Is.False);
            Assert.That(Token("plain", "--jfb-color-ink")!["darkDefault"], Is.Null, "no dark colour without a dark scheme, though the theme names one");
        });
    }

    [Test]
    public async Task ATheme_IsCreatedUnderItsAliassKey_ReadAndChanged_TheAliasStaying_EachChangeAudited()
    {
        var created = await CallAsync("designer", HttpMethod.Post, "theme", Autumn);
        Assert.That(created.Outcome, Is.EqualTo("201"), created.ToString());
        var key = created.Body!["key"]!.GetValue<Guid>();
        var read = await CallAsync("designer", HttpMethod.Get, $"theme/{key}");
        var changed = await CallAsync("designer", HttpMethod.Put, $"theme/{key}", new
        {
            concurrencyVersion = read.Body!["concurrencyVersion"]!.GetValue<int>(),
            theme = new { alias = "renamed", name = "Autumn", baseTheme = "default", tokens = new { light = new Dictionary<string, string>(), dark = new Dictionary<string, string>() }, customCss = "  " },
        });
        var rows = await CallAsync("designer", HttpMethod.Get, "theme/library");
        var audit = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10));

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo(FormLibraryUses.ThemeKey("autumn")));
            Assert.That(read.Body!["tokens"]!["light"]!["--jfb-color-accent"]!.GetValue<string>(), Is.EqualTo("#8a1538"));
            Assert.That(read.Body!["tokens"]!["dark"]!["--jfb-color-accent"]!.GetValue<string>(), Is.EqualTo("#f0a5b8"));
            Assert.That(read.Body!["customCss"]!.GetValue<string>(), Is.EqualTo(".jfb-label { letter-spacing: .02em; }"));
            Assert.That(changed.Outcome, Is.EqualTo("200"), changed.ToString());
            Assert.That(changed.Body!["alias"]!.GetValue<string>(), Is.EqualTo("autumn"), "the alias forms name it by stays");
            Assert.That(changed.Body!["customCss"], Is.Null, "blank custom CSS is none");
            var row = rows.Body!["items"]![0]!;
            Assert.That((row["name"]!.GetValue<string>(), row["baseLabel"]!.GetValue<string>(), row["usedBy"]!.GetValue<int>(), row["isDefault"]!.GetValue<bool>()), Is.EqualTo(("Autumn", "Default", 0, false)));
            Assert.That(row["swatch"]!.AsArray().Select(colour => colour!.GetValue<string>()), Is.EqualTo(new[] { "#fff", "#1d2433", "#0f6e5c" }), "the base theme's colours where it changes none");
            Assert.That(audit.Items.Select(entry => entry.Action), Is.EquivalentTo(new[] { ThemeLibrary.CreatedAction, ThemeLibrary.SavedAction }));
        });
    }

    [Test]
    public async Task ProblemsOfATheme_AreEachSaidAtTheirPath()
    {
        var refused = await CallAsync("designer", HttpMethod.Post, "theme", new
        {
            theme = new
            {
                alias = "Autumn Sale",
                name = "",
                baseTheme = "default",
                tokens = new
                {
                    light = new Dictionary<string, string> { ["--jfb-made-up"] = "1px", ["--jfb-color-text"] = "red; } body { x: y" },
                    dark = new Dictionary<string, string> { ["--jfb-radius"] = "2px" },
                },
            },
        });
        var reserved = await CallAsync("designer", HttpMethod.Post, "theme", new { theme = new { alias = "bootstrap5", name = "B", baseTheme = "nowhere", tokens = new { light = new { }, dark = new { } } } });
        var noDark = await CallAsync("designer", HttpMethod.Post, "theme", new { theme = new { alias = "flat", name = "Flat", baseTheme = "plain", tokens = new { light = new { }, dark = new Dictionary<string, string> { ["--jfb-color-ink"] = "#000" } } } });

        Assert.Multiple(() =>
        {
            Assert.That(Problems(refused), Is.EqualTo("400 $.alias $.name $.tokens.light['--jfb-color-text'] $.tokens.light['--jfb-made-up'] $.tokens.dark['--jfb-radius']"), refused.ToString());
            Assert.That(Problems(reserved), Is.EqualTo("400 $.alias $.baseTheme"), reserved.ToString());
            Assert.That(Problems(noDark), Is.EqualTo("400 $.tokens.dark['--jfb-color-ink']"), noDark.ToString());
        });
    }

    [Test]
    public async Task ASitesOwnDefaultTheme_StaysToo_ThoughAllSitesNameAnother()
    {
        // General is set per site (ADR-0215): a theme that only one site starts its forms with is a default all the same.
        var created = await CallAsync("designer", HttpMethod.Post, "theme", Autumn);
        var key = created.Body!["key"]!.GetValue<Guid>();
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        await settings.SaveSiteAsync(Guid.NewGuid(), new GeneralSettings { Theme = "autumn" }, ["theme"], 0, global::Umbraco.Cms.Core.Constants.Security.SuperUserKey);

        var refused = await CallAsync("designer", HttpMethod.Delete, $"theme/{key}?concurrencyVersion=1");
        var still = await CallAsync("designer", HttpMethod.Get, $"theme/{key}");

        Assert.Multiple(() =>
        {
            Assert.That(refused.Outcome, Is.EqualTo("409 is_default"), refused.ToString());
            Assert.That(still.Status, Is.EqualTo(System.Net.HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task UsedBy_CountsAndNamesTheFormsThatNameTheTheme_AndTheDeleteWaits_AsItDoesForTheSitesDefault()
    {
        var created = await CallAsync("designer", HttpMethod.Post, "theme", Autumn);
        var key = created.Body!["key"]!.GetValue<Guid>();
        var form = await FormInThemeAsync("Contact us", "Autumn");

        var rows = await CallAsync("designer", HttpMethod.Get, "theme/library");
        var users = await CallAsync("designer", HttpMethod.Get, $"theme/{key}/used-by");
        var inUse = await CallAsync("designer", HttpMethod.Delete, $"theme/{key}?concurrencyVersion=1");
        await TrashFormAsync(form);
        await GetRequiredService<IFormBuilderFormService>().DeleteAsync(form, (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion, null);
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        _ = await settings.UpdateAsync<GeneralSettings>(current => current with { Theme = "autumn" }, general.Version, null);
        var isDefault = await CallAsync("designer", HttpMethod.Delete, $"theme/{key}?concurrencyVersion=1");
        var defaultRow = await CallAsync("designer", HttpMethod.Get, "theme/library");
        general = await settings.GetAsync<GeneralSettings>();
        _ = await settings.UpdateAsync<GeneralSettings>(current => current with { Theme = null }, general.Version, null);
        var deleted = await CallAsync("designer", HttpMethod.Delete, $"theme/{key}?concurrencyVersion=1");
        var gone = await CallAsync("designer", HttpMethod.Get, $"theme/{key}");
        var audit = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Body!["items"]![0]!["usedBy"]!.GetValue<int>(), Is.EqualTo(1), "a form naming the theme in any letter case uses it");
            Assert.That(users.Body!["forms"]!.AsArray().Select(each => each!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Contact us" }));
            Assert.That(inUse.Outcome, Is.EqualTo("409 in_use"));
            Assert.That(isDefault.Outcome, Is.EqualTo("409 is_default"));
            Assert.That(defaultRow.Body!["items"]![0]!["isDefault"]!.GetValue<bool>(), Is.True);
            Assert.That(deleted.Outcome, Is.EqualTo("204"), deleted.ToString());
            Assert.That(gone.Outcome, Is.EqualTo("404 theme_not_found"));
            Assert.That(audit.Items[0].Action, Is.EqualTo(ThemeLibrary.DeletedAction), "the delete is audited");
        });
    }

    [Test]
    public async Task ADuplicate_TakesTheFirstFreeAlias_AndAnExport_ImportsAsANewTheme()
    {
        var created = await CallAsync("designer", HttpMethod.Post, "theme", Autumn);
        var key = created.Body!["key"]!.GetValue<Guid>();

        var copy = await CallAsync("designer", HttpMethod.Post, $"theme/{key}/duplicate", new { name = "Autumn (copy)" });
        var exported = await CallAsync("designer", HttpMethod.Get, $"theme/{key}/export");
        var imported = await CallAsync("designer", HttpMethod.Post, "theme/import", new { document = exported.Body!["document"]!.GetValue<string>() });
        var foreign = await CallAsync("designer", HttpMethod.Post, "theme/import", new { document = """{"format":"something-else","version":1}""" });
        var broken = await CallAsync("designer", HttpMethod.Post, "theme/import", new { document = "not json" });

        Assert.Multiple(() =>
        {
            Assert.That(copy.Outcome, Is.EqualTo("201"), copy.ToString());
            Assert.That((copy.Body!["alias"]!.GetValue<string>(), copy.Body!["name"]!.GetValue<string>()), Is.EqualTo(("autumn-2", "Autumn (copy)")));
            Assert.That(copy.Body!["tokens"]!["light"]!["--jfb-radius"]!.GetValue<string>(), Is.EqualTo("6px"));
            Assert.That(exported.Body!["fileName"]!.GetValue<string>(), Is.EqualTo("autumn.jfb-theme.json"));
            var document = JsonNode.Parse(exported.Body!["document"]!.GetValue<string>())!;
            Assert.That((document["format"]!.GetValue<string>(), document["version"]!.GetValue<int>(), document["baseTheme"]!.GetValue<string>()), Is.EqualTo(("jfb-theme", 1, "default")));
            Assert.That(imported.Outcome, Is.EqualTo("201"), imported.ToString());
            Assert.That((imported.Body!["alias"]!.GetValue<string>(), imported.Body!["name"]!.GetValue<string>()), Is.EqualTo(("autumn-3", "Autumn campaign")), "the first alias after the theme's that no theme has");
            Assert.That(imported.Body!["customCss"]!.GetValue<string>(), Is.EqualTo(".jfb-label { letter-spacing: .02em; }"));
            Assert.That(foreign.Outcome, Is.EqualTo("400 unsupported_format"), foreign.ToString());
            Assert.That(broken.Outcome, Is.EqualTo("400 invalid_json"), broken.ToString());
        });
    }

    [Test]
    public async Task ThePreview_GivesTheLayerUnderTheAliasGiven_WithWhatTheSanitiserTookOut_AndTheProblems()
    {
        var preview = await CallAsync("designer", HttpMethod.Post, "theme/preview", new
        {
            baseTheme = "default",
            tokens = new { light = new Dictionary<string, string> { ["--jfb-color-accent"] = "#8a1538", ["--jfb-made-up"] = "1px", ["--jfb-color-surface"] = "image-set(\"https://example.com/x.png\" 1x)" }, dark = new { } },
            customCss = "@import url(https://fonts.example.com/a.css); .jfb-label { color: red; background: url(https://example.com/x.png); }",
        });

        Assert.That(preview.Outcome, Is.EqualTo("200"), preview.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(preview.Body!["alias"]!.GetValue<string>(), Is.EqualTo(ThemeLibrary.PreviewAlias), "a theme not saved yet is drawn under the preview's alias");
            var css = preview.Body!["css"]!.GetValue<string>();
            Assert.That(css, Does.Contain(".jfb-form.jfb-theme-jfb-preview {\n  --jfb-color-accent: #8a1538;\n}"));
            Assert.That(css, Does.Not.Contain("--jfb-made-up").And.Not.Contain("@import").And.Not.Contain("example.com"));
            Assert.That(css, Does.Contain(".jfb-form.jfb-theme-jfb-preview .jfb-label { color: red; }"));
            Assert.That(preview.Body!["removed"]!.AsArray().Select(each => each!["code"]!.GetValue<string>()), Is.EqualTo(new[] { "import", "address" }));
            Assert.That(preview.Body!["problems"]!.AsArray().Select(each => each!["path"]!.GetValue<string>()), Is.EqualTo(new[] { "$.tokens.light['--jfb-color-surface']", "$.tokens.light['--jfb-made-up']" }), "an image set would load from another host");
        });
    }

    [Test]
    public async Task TheSample_IsTheWebsitesRenderingOfASampleForm_InTheBaseTheme_UnderThePreviewsAlias_WithoutAScript()
    {
        await GetRequiredService<ILanguageService>().CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        var german = await CallAsync("designer", HttpMethod.Get, "theme/sample?baseTheme=bootstrap5&culture=de-DE");
        var english = await CallAsync("designer", HttpMethod.Get, "theme/sample?baseTheme=default&culture=fr-FR");
        var missing = await CallAsync("designer", HttpMethod.Get, "theme/sample?baseTheme=nowhere");

        Assert.That(german.Outcome, Is.EqualTo("200"), german.ToString());
        var page = await new HtmlParser().ParseDocumentAsync(german.Body!["html"]!.GetValue<string>());
        var form = page.QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(form.ClassList, Does.Contain("jfb-theme-jfb-preview").And.Contain("jfb-theme-bootstrap5"), "the base theme's form under the preview's alias");
            Assert.That(page.QuerySelector("input[type=email]")!.ClassList, Does.Contain("form-control"), "the base theme's classes");
            Assert.That(page.QuerySelectorAll("link[rel=stylesheet]").Select(link => link.GetAttribute("href")!.Split('?')[0]), Is.EqualTo(new[] { "/jfb/assets/themes/base.css", "/jfb/assets/themes/bootstrap5.css" }));
            Assert.That(page.GetElementById(german.Body!["layerId"]!.GetValue<string>())!.TextContent, Is.Empty, "the layer's place, after the stylesheets");
            Assert.That(page.QuerySelectorAll("script").Select(script => script.GetAttribute("type")), Is.All.EqualTo("application/json"), "no script, only the form's data, which no browser runs");
            Assert.That(form.HasAttribute("action"), Is.False, "nowhere to post to");
            Assert.That(page.QuerySelector("label[for$=-email]")!.TextContent, Does.Contain("E-Mail-Adresse"), "the sample in German");
            Assert.That(page.QuerySelector("[data-jfb-summary]"), Is.Not.Null, "an answer its check refuses");
            Assert.That(page.QuerySelectorAll("button").Length, Is.GreaterThanOrEqualTo(2), "both buttons of a second page");
            Assert.That(german.Body!["culture"]!.GetValue<string>(), Is.EqualTo("de-DE"));
            Assert.That(english.Body!["culture"]!.GetValue<string>(), Is.EqualTo("en-US"), "a culture the site lacks is its default language");
            Assert.That(english.Body!["html"]!.GetValue<string>(), Does.Contain("Email address").And.Contain("jfb-theme-default"));
            Assert.That(missing.Outcome, Is.EqualTo("404 theme_not_found"));
        });
    }

    // The status and the path of each problem the answer names, in its order, such as "400 $.alias $.name".
    private static string Problems(ApiAnswer answer) =>
        string.Join(' ', [((int)answer.Status).ToString(System.Globalization.CultureInfo.InvariantCulture), .. answer.Body?["errors"]?.AsArray().Select(error => error!["path"]!.GetValue<string>()) ?? []]);

    // A draft form whose settings name the theme.
    private async Task<Guid> FormInThemeAsync(string name, string theme)
    {
        var key = await FormAsync(name);
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(key))!;
        var definition = draft.Definition! with { Settings = draft.Definition.Settings with { Appearance = draft.Definition.Settings.Appearance with { Theme = theme } } };
        _ = await forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, definition, null, null);
        return key;
    }

    /// <summary>A site's own base theme with a dark scheme and a token that is no colour.</summary>
    public sealed class AcmeTheme : IFormBuilderTheme
    {
        public string Name => "acme";

        public string Label => "Acme";

        public string? Stylesheet => null;

        public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string> { ["--jfb-color-ink"] = "#102030", ["--jfb-gutter"] = "18px" };

        public bool DarkScheme => true;

        public IReadOnlyDictionary<string, string> DarkTokens { get; } = new Dictionary<string, string> { ["--jfb-color-ink"] = "#f0f0f0" };

        public string ViewFolder => "acme";
    }

    /// <summary>A site's own base theme without a dark scheme.</summary>
    public sealed class PlainTheme : IFormBuilderTheme
    {
        public string Name => "plain";

        public string Label => "Plain";

        public string? Stylesheet => null;

        public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string> { ["--jfb-color-ink"] = "#000" };

        public bool DarkScheme => false;

        public string ViewFolder => "plain";
    }
}
