using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The check of the themes' stylesheets at start-up (ADR-0092, ADR-0105): a stylesheet below <c>/jfb/assets/</c> must be
/// one of the website assets the Web assembly holds, and a stylesheet the site serves itself, or none, passes.
/// </summary>
[TestFixture]
public sealed class ThemeStylesheetStartupCheckTests
{
    private static readonly WebsiteAssets Assets = new(new Dictionary<string, byte[]>
    {
        ["themes/base.css"] = Encoding.UTF8.GetBytes(".jfb-form [hidden] { display: none; }"),
        ["themes/default.css"] = Encoding.UTF8.GetBytes(".jfb-form:where(.jfb-theme-default) { color: black; }"),
        ["themes/calm.css"] = Encoding.UTF8.GetBytes(".jfb-form { color: teal; }"),
        ["themes/calm.css.br"] = [1, 2],
    });

    [Test]
    public void ThemesWithoutAStylesheet_WithTheSitesOwn_OrWithOneTheAssemblyHolds_Pass()
    {
        var check = Check(new DefaultTheme(), new UnstyledTheme(), new Theme("site", "/css/forms.css"), new Theme("calm", "/jfb/assets/themes/calm.css"), new Theme("shouting", "/JFB/Assets/themes/calm.css"));

        Assert.That(check.Validate(Options.DefaultName, new FormBuilderStartupChecks()).Succeeded, Is.True);
    }

    [Test]
    public void AStylesheetBelowTheAssetsPathThatTheAssemblyLacks_StopsTheSite_NamingEachTheme()
    {
        var result = Check(new DefaultTheme(), new Theme("gone", "/jfb/assets/themes/gone.css"), new Theme("copy", "/jfb/assets/themes/calm.css.br"), new Theme("cased", "/jfb/assets/themes/Calm.css"))
            .Validate(Options.DefaultName, new FormBuilderStartupChecks());

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Failures, Is.EqualTo(new[]
            {
                "The theme 'gone' names the stylesheet '/jfb/assets/themes/gone.css', which is none of the website assets below /jfb/assets/ that the Web assembly holds: build the client, or give the theme a stylesheet the site serves itself (ADR-0105).",
                "The theme 'copy' names the stylesheet '/jfb/assets/themes/calm.css.br', which is none of the website assets below /jfb/assets/ that the Web assembly holds: build the client, or give the theme a stylesheet the site serves itself (ADR-0105).",
                "The theme 'cased' names the stylesheet '/jfb/assets/themes/Calm.css', which is none of the website assets below /jfb/assets/ that the Web assembly holds: build the client, or give the theme a stylesheet the site serves itself (ADR-0105).",
            }));
        });
    }

    private static ThemeStylesheetStartupCheck Check(params IFormBuilderTheme[] themes) => new(new FormBuilderThemeCollection(() => themes), Assets);

    private sealed class Theme(string name, string? stylesheet) : IFormBuilderTheme
    {
        public string Name => name;

        public string Label => name;

        public string? Stylesheet => stylesheet;

        public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string>();

        public bool DarkScheme => false;

        public string ViewFolder => name;
    }
}
