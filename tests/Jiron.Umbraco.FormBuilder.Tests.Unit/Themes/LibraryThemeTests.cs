using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Themes;

/// <summary>
/// A library theme (ADR-0215): what it changes of its base theme, read from its row whatever the row holds and written
/// the same for the same tokens; the problems of tokens its base theme does not declare, of dark colours without a dark
/// scheme, and of values that break out; its dark scheme, which keeps a changed light colour out of the dark one; the
/// layer the website loads after the base theme's stylesheet; and the theme as the website renders it, its base theme's
/// stylesheet, views and classes with both themes' classes on the form.
/// </summary>
[TestFixture]
public sealed class LibraryThemeTests
{
    private static readonly DefaultTheme Default = new();

    private static StoredTheme Stored(string tokens, string? css = null, string baseTheme = "default") =>
        new(1, Guid.NewGuid(), "autumn", "Autumn campaign", baseTheme, tokens, css, 4, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[1, 2]")]
    [TestCase("{\"light\": 3, \"dark\": \"x\"}")]
    public void TokensOfAnotherShape_ReadAsNone(string? json) =>
        Assert.That(ThemeTokens.Read(json), Is.EqualTo(ThemeTokens.None).Using<ThemeTokens>((a, b) => a.Light.Count == b.Light.Count && a.Dark.Count == b.Dark.Count));

    [Test]
    public void Tokens_AreWrittenWithSortedNames_AndReadBack()
    {
        var tokens = new ThemeTokens
        {
            Light = new Dictionary<string, string> { ["--jfb-radius"] = "6px", ["--jfb-color-accent"] = "#8a1538" },
            Dark = new Dictionary<string, string> { ["--jfb-color-accent"] = "#f0a5b8" },
        };

        var json = tokens.Write();
        var read = ThemeTokens.Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("{\"light\":{\"--jfb-color-accent\":\"#8a1538\",\"--jfb-radius\":\"6px\"},\"dark\":{\"--jfb-color-accent\":\"#f0a5b8\"}}"));
            Assert.That(read.Light, Is.EquivalentTo(tokens.Light));
            Assert.That(read.Dark, Is.EquivalentTo(tokens.Dark));
            Assert.That(ThemeTokens.Read("{\"light\": {\"--jfb-radius\": 6, \"--jfb-space-1\": \"2px\"}}").Light.Keys, Is.EqualTo(new[] { "--jfb-space-1" }), "a value that is no text is left out");
        });
    }

    [Test]
    public void TokensTheBaseThemeDeclares_WithDarkColours_Pass()
    {
        var problems = new DefinitionProblems();
        new ThemeTokens
        {
            Light = new Dictionary<string, string> { ["--jfb-color-accent"] = "#8a1538", ["--jfb-font-family"] = "Georgia, serif" },
            Dark = new Dictionary<string, string> { ["--jfb-color-accent"] = "#f0a5b8" },
        }.Check(Default, problems, "$.tokens");

        Assert.That(problems.ToReadOnlyList(), Is.Empty);
    }

    [Test]
    public void TokensTheBaseThemeDoesNotDeclare_DarkNonColours_AndValuesThatBreakOut_AreRefused_EachAtItsPath()
    {
        var problems = new DefinitionProblems();
        new ThemeTokens
        {
            Light = new Dictionary<string, string>
            {
                ["--jfb-made-up"] = "1px",
                ["--jfb-color-text"] = "red; } body { color: red",
                ["--jfb-color-surface"] = "url(https://example.com/x.png)",
                ["--jfb-radius"] = new string('1', 201),
            },
            Dark = new Dictionary<string, string> { ["--jfb-radius"] = "2px" },
        }.Check(Default, problems, "$.tokens");

        Assert.That(problems.ToReadOnlyList().Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[]
        {
            (DefinitionErrors.InvalidValue, "$.tokens.light['--jfb-color-surface']"),
            (DefinitionErrors.InvalidValue, "$.tokens.light['--jfb-color-text']"),
            (DefinitionErrors.UnknownProperty, "$.tokens.light['--jfb-made-up']"),
            (DefinitionErrors.InvalidValue, "$.tokens.light['--jfb-radius']"),
            (DefinitionErrors.PropertyNotSupported, "$.tokens.dark['--jfb-radius']"),
        }));
    }

    [Test]
    public void DarkColours_OfABaseThemeWithoutADarkScheme_AreRefused()
    {
        var problems = new DefinitionProblems();
        new ThemeTokens { Dark = new Dictionary<string, string> { ["--jfb-color-accent"] = "#fff" } }.Check(new UnstyledThemeWithAccent(), problems, "$.tokens");

        Assert.That(problems.ToReadOnlyList().Single().Message, Does.Contain("has no dark scheme"));
    }

    [Test]
    public void TheDarkScheme_SetsEveryChangedColour_ADarkValueOrTheBaseThemesDarkDefault()
    {
        var tokens = new ThemeTokens
        {
            Light = new Dictionary<string, string> { ["--jfb-color-text"] = "#222", ["--jfb-color-accent"] = "#8a1538", ["--jfb-radius"] = "6px" },
            Dark = new Dictionary<string, string> { ["--jfb-color-accent"] = "#f0a5b8" },
        };

        Assert.Multiple(() =>
        {
            Assert.That(tokens.DarkScheme(Default), Is.EquivalentTo(new Dictionary<string, string>
            {
                ["--jfb-color-accent"] = "#f0a5b8",
                ["--jfb-color-text"] = "#e8eaee",
            }), "the light text the theme changed is the base theme's dark text in the dark scheme, and the radius is no colour");
            Assert.That(tokens.DarkScheme(new UnstyledThemeWithAccent()), Is.Empty, "a base theme without a dark scheme has none");
        });
    }

    [Test]
    public void TheLayer_SetsTheTokensOnTheThemesForm_TheDarkSchemeUnderItsSelectors_ThenTheSanitisedCustomCss()
    {
        var layer = LibraryThemes.LayerOf(
            Stored(
                "{\"light\":{\"--jfb-color-accent\":\"#8a1538\",\"--jfb-made-up\":\"1px\",\"--jfb-radius\":\"6px; color: red\",\"--jfb-shade\":\"image-set(\\\"https://evil.example/x.png\\\" 1x)\"},\"dark\":{\"--jfb-color-accent\":\"#f0a5b8\"}}",
                ".jfb-label { letter-spacing: .02em; } @import url(https://example.com/x.css);"),
            Default);

        Assert.That(layer, Is.EqualTo(
            "/* Jiron Form Builder: the library theme autumn, on default (ADR-0215) */\n" +
            ".jfb-form.jfb-theme-autumn {\n  --jfb-color-accent: #8a1538;\n}\n" +
            "@media screen {\n  .jfb-form.jfb-theme-autumn.jfb-scheme-dark,\n  .jfb-scheme-dark .jfb-form.jfb-theme-autumn {\n    --jfb-color-accent: #f0a5b8;\n  }\n}\n" +
            "@media screen and (prefers-color-scheme: dark) {\n  .jfb-form.jfb-theme-autumn.jfb-scheme-auto,\n  .jfb-scheme-auto .jfb-form.jfb-theme-autumn {\n    --jfb-color-accent: #f0a5b8;\n  }\n}\n" +
            ".jfb-form.jfb-theme-autumn .jfb-label { letter-spacing: .02em; }\n"));
    }

    [Test]
    public void TheLayer_OfAThemeThatChangesNothing_IsItsHeadAlone()
    {
        Assert.That(LibraryThemes.LayerOf(Stored("{}"), Default), Is.EqualTo("/* Jiron Form Builder: the library theme autumn, on default (ADR-0215) */\n"));
    }

    [Test]
    public void TheRenderedTheme_IsItsBaseThemeWithBothClassesOnTheForm_AndItsOwnNameAndVersion()
    {
        var bootstrap = new Bootstrap5Theme();
        var theme = new LibraryTheme(Stored("{}", baseTheme: "bootstrap5"), bootstrap);

        Assert.Multiple(() =>
        {
            Assert.That((theme.Name, theme.Label, theme.Version), Is.EqualTo(("autumn", "Autumn campaign", 4)));
            Assert.That(theme.Stylesheet, Is.EqualTo(bootstrap.Stylesheet));
            Assert.That(theme.ViewFolder, Is.EqualTo("bootstrap5"));
            Assert.That(theme.Classes[FormBuilderThemeSlots.Form], Is.EqualTo("jfb-theme-bootstrap5"));
            Assert.That(theme.Classes[FormBuilderThemeSlots.Control], Is.EqualTo(bootstrap.Classes[FormBuilderThemeSlots.Control]));
            Assert.That(theme.LayerTag, Has.Length.EqualTo(32));
        });
    }

    [Test]
    public void TheLayersAddress_CarriesTheVersion_AndOnlyAnAliasPathIsALayer()
    {
        var theme = new LibraryTheme(Stored("{}"), Default);

        Assert.Multiple(() =>
        {
            Assert.That(LibraryThemeLayers.Url(new PathString("/site"), theme), Is.EqualTo("/site/jfb/assets/themes/library/autumn.css?v=4"));
            Assert.That(LibraryThemeLayers.AliasOf("/themes/library/autumn.css"), Is.EqualTo("autumn"));
            Assert.That(LibraryThemeLayers.AliasOf("/THEMES/LIBRARY/autumn.CSS"), Is.EqualTo("autumn"));
            Assert.That(new[] { "/themes/default.css", "/themes/library/Autumn.css", "/themes/library/../x.css", "/themes/library/a.js", null }.Select(LibraryThemeLayers.AliasOf), Is.All.Null);
        });
    }

    // A theme without a dark scheme that declares an accent.
    private sealed class UnstyledThemeWithAccent : IFormBuilderTheme
    {
        public string Name => "plain";

        public string Label => "Plain";

        public string? Stylesheet => null;

        public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string> { ["--jfb-color-accent"] = "#000" };

        public bool DarkScheme => false;

        public string ViewFolder => "plain";
    }
}
