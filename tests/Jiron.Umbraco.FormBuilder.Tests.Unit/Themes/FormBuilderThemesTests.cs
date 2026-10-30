using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Themes;

/// <summary>
/// The site's themes (ADR-0015, ADR-0105): the collection holds the built-in themes from its start; the registry finds a
/// theme by its exact alias and gives a form the first theme named, the placement's, the form's or the site's, with
/// <c>default</c> for none and for one the site lacks, logged once per name; and start-up refuses every theme with a
/// problem, naming each problem.
/// </summary>
[TestFixture]
public sealed partial class FormBuilderThemesTests
{
    private static readonly IFormBuilderTheme[] BuiltIn = [new DefaultTheme(), new UnstyledTheme(), new Bootstrap5Theme()];

    // The public tokens of docs/ux/tokens.md that a theme built on default may set: all but the site's font and size.
    private static readonly string[] DefaultTokens =
    [
        "--jfb-color-text", "--jfb-color-text-muted", "--jfb-color-surface", "--jfb-color-border", "--jfb-color-rule", "--jfb-color-accent",
        "--jfb-color-on-accent", "--jfb-color-focus", "--jfb-color-error", "--jfb-shade", "--jfb-radius", "--jfb-radius-panel", "--jfb-radius-card",
        "--jfb-radius-control", "--jfb-radius-tag", "--jfb-space-1", "--jfb-space-2", "--jfb-space-3", "--jfb-space-4", "--jfb-space-5", "--jfb-space-6",
        "--jfb-space-7", "--jfb-line-height", "--jfb-font-size-small", "--jfb-font-size-large", "--jfb-font-size-title", "--jfb-font-weight-strong",
        "--jfb-size-control", "--jfb-size-form", "--jfb-focus-ring", "--jfb-motion-fast", "--jfb-motion-base", "--jfb-motion-ease",
    ];

    [Test]
    public void TheCollectionBuilder_HoldsTheBuiltInThemes_FromItsStart()
    {
        var builder = new FormBuilderThemeCollectionBuilder();
        var changed = new FormBuilderThemeCollectionBuilder().Remove<Bootstrap5Theme>().Append<AcmeTheme>();

        Assert.Multiple(() =>
        {
            Assert.That(builder.GetTypes(), Is.EqualTo(new[] { typeof(DefaultTheme), typeof(UnstyledTheme), typeof(Bootstrap5Theme) }));
            Assert.That(BuiltInThemes.All, Is.EqualTo(builder.GetTypes()));
            Assert.That(changed.GetTypes(), Is.EqualTo(new[] { typeof(DefaultTheme), typeof(UnstyledTheme), typeof(AcmeTheme) }), "a composer removes one and adds its own");
        });
    }

    [Test]
    public void TheBuiltInThemes_PassTheChecks_EachRenderingTheDefaultViews()
    {
        var registry = Registry(BuiltIn);

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderThemes.Problems(BuiltIn), Is.Empty);
            Assert.That(registry.All.Select(theme => (theme.Name, theme.Label)), Is.EqualTo(new[] { ("default", "Default"), ("unstyled", "Unstyled"), ("bootstrap5", "Bootstrap 5") }));
            Assert.That(registry.All.Select(theme => theme.ViewFolder), Is.EqualTo(new[] { "default", "unstyled", "bootstrap5" }), "each theme's folder is its alias");
            Assert.That(registry.All.Select(theme => theme.DarkScheme), Is.EqualTo(new[] { true, false, false }), "only default offers the dark scheme");
            Assert.That(registry.All.Select(theme => theme.Stylesheet), Is.EqualTo(new[] { "/jfb/assets/themes/default.css", null, null }), "default's is the package's; bootstrap5's comes in 6.3d");
            Assert.That(registry.Find("default")!.Tokens.Keys, Is.EquivalentTo(DefaultTokens), "default declares every public token but the site's font and size");
            Assert.That(registry.All.Skip(1).Select(theme => theme.Tokens.Count), Is.All.Zero);
            Assert.That(registry.Default, Is.InstanceOf<DefaultTheme>());
            Assert.That(registry.Find("unstyled")!.Classes, Is.Empty, "unstyled adds no class to ours");
            Assert.That(registry.Find("default")!.Classes, Is.Empty);
        });
    }

    [Test]
    public void DefaultsTokens_AreTheLightFallbacksItsStylesheetReadsThemWith()
    {
        var css = File.ReadAllText(RepositoryRoot.Path(typeof(FormBuilderThemesTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "themes", "src", "default.css"));

        // Each public token the stylesheet reads, with the fallback of its first reading, the light scheme's.
        var read = TokenRead().Matches(css).GroupBy(match => match.Groups["token"].Value).ToDictionary(each => each.Key, each => each.First().Groups["fallback"].Value.Trim());
        var tokens = new DefaultTheme().Tokens;

        Assert.Multiple(() =>
        {
            Assert.That(tokens.Keys, Is.SubsetOf(read.Keys), "every default names a token the stylesheet reads");
            Assert.That(read.Where(each => each.Value.Length == 0).Select(each => each.Key), Is.EquivalentTo(new[] { "--jfb-font-family", "--jfb-font-size" }), "only the site's font and size have none");
            foreach (var (token, fallback) in read.Where(each => each.Value.Length > 0 && !each.Value.Contains("var(--_", StringComparison.Ordinal)))
            {
                Assert.That(Plain(tokens.GetValueOrDefault(token) ?? "none"), Is.EqualTo(Plain(fallback)), token);
            }

            Assert.That(read.Count(each => each.Value.Contains("var(--_", StringComparison.Ordinal)), Is.EqualTo(5), "the radius steps and the focus ring derive from other tokens");
        });
    }

    [Test]
    public void Bootstrap5_GivesBootstrapsOwnClasses_ToTheSlotsItStyles()
    {
        var classes = new Bootstrap5Theme().Classes;

        Assert.That(classes, Is.EquivalentTo(new Dictionary<string, string>
        {
            [FormBuilderThemeSlots.Field] = "mb-3",
            [FormBuilderThemeSlots.Label] = "form-label",
            [FormBuilderThemeSlots.Help] = "form-text",
            [FormBuilderThemeSlots.Error] = "invalid-feedback d-block",
            [FormBuilderThemeSlots.Control] = "form-control",
            [FormBuilderThemeSlots.Select] = "form-select",
            [FormBuilderThemeSlots.Choice] = "form-check",
            [FormBuilderThemeSlots.ChoiceInput] = "form-check-input",
            [FormBuilderThemeSlots.ChoiceLabel] = "form-check-label",
            [FormBuilderThemeSlots.Switch] = "form-switch",
            [FormBuilderThemeSlots.Range] = "form-range",
            [FormBuilderThemeSlots.Affix] = "input-group",
            [FormBuilderThemeSlots.AffixText] = "input-group-text",
            [FormBuilderThemeSlots.Counter] = "form-text",
            [FormBuilderThemeSlots.Navigation] = "d-flex flex-wrap gap-2",
            [FormBuilderThemeSlots.Button] = "btn btn-outline-secondary",
            [FormBuilderThemeSlots.ButtonPrimary] = "btn btn-primary",
            [FormBuilderThemeSlots.ButtonLink] = "btn btn-link",
            [FormBuilderThemeSlots.ErrorSummary] = "alert alert-danger",
            [FormBuilderThemeSlots.Notice] = "alert alert-secondary",
        }));
    }

    [Test]
    public void TheSlots_AreTheContractsList_EachOnce()
    {
        Assert.That(FormBuilderThemeSlots.All, Is.EqualTo(new[]
        {
            "form", "page", "group", "field", "label", "help", "error", "control", "select", "choices", "choice", "choice-input", "choice-label",
            "switch", "range", "affix", "affix-text", "counter", "navigation", "button", "button-primary", "button-link", "progress",
            "error-summary", "notice", "outcome",
        }));
    }

    [Test]
    public void TheRegistry_FindsAThemeByItsExactAlias()
    {
        var acme = new AcmeTheme();
        var registry = Registry([.. BuiltIn, acme]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("acme"), Is.SameAs(acme));
            Assert.That(registry.Find("Acme"), Is.Null, "an alias in another letter case is none");
            Assert.That(registry.Find(" acme"), Is.Null);
            Assert.That(registry.Find(null), Is.Null);
            Assert.That(registry.All.Last(), Is.SameAs(acme), "in the order the collection holds them");
        });
    }

    [Test]
    public void AFormsTheme_IsTheFirstNamed_ThePlacementsBeforeTheFormsBeforeTheSites()
    {
        var registry = Registry([.. BuiltIn, new AcmeTheme()]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Resolve("acme", "unstyled", "bootstrap5").Name, Is.EqualTo("acme"), "the placement's");
            Assert.That(registry.Resolve(null, "unstyled", "bootstrap5").Name, Is.EqualTo("unstyled"), "the form's");
            Assert.That(registry.Resolve(null, "  ", "bootstrap5").Name, Is.EqualTo("bootstrap5"), "the site's, past a blank one");
            Assert.That(registry.Resolve(string.Empty, null, null).Name, Is.EqualTo("default"), "none named");
            Assert.That(registry.Resolve().Name, Is.EqualTo("default"));
        });
    }

    [Test]
    public void AThemeTheSiteLacks_RendersAsDefault_LoggedOncePerName()
    {
        var logger = new RecordingLogger<FormBuilderThemes>();
        var registry = new FormBuilderThemes(BuiltIn, logger);

        var gone = registry.Resolve("gone", "unstyled");
        _ = registry.Resolve(null, "gone");
        _ = registry.Resolve("Unstyled");

        Assert.Multiple(() =>
        {
            Assert.That(gone, Is.SameAs(registry.Default), "a name that is gone is default, not the next one named");
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Warning, "A form or the settings name the theme 'gone', which the site does not have; the form renders with the default theme."),
                (LogLevel.Warning, "A form or the settings name the theme 'Unstyled', which the site does not have; the form renders with the default theme."),
            }));
        });
    }

    [TestCase("a")]
    [TestCase("default")]
    [TestCase("bootstrap5")]
    [TestCase("my-theme-2")]
    public void AnAlias_IsLowerCaseLettersDigitsAndHyphens_StartingWithALetter(string alias) =>
        Assert.That(FormBuilderThemes.IsAlias(alias), Is.True);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Default")]
    [TestCase("2col")]
    [TestCase("-dark")]
    [TestCase("my_theme")]
    [TestCase("my theme")]
    [TestCase("theme\n")]
    [TestCase("th\xE9me")]
    public void AnythingElse_IsNoAlias(string? alias) =>
        Assert.That(FormBuilderThemes.IsAlias(alias), Is.False);

    [Test]
    public void AnAlias_HoldsAtMostSixtyFourCharacters() =>
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderThemes.IsAlias(new string('a', 64)), Is.True);
            Assert.That(FormBuilderThemes.IsAlias(new string('a', 65)), Is.False);
        });

    [Test]
    public void ASitesOwnTheme_WithALocalStylesheet_ClassesAndTokens_Passes() =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new AcmeTheme()]), Is.Empty);

    [TestCase("/css/forms.css")]
    [TestCase("/assets/acme%20forms/forms.min.css")]
    [TestCase("/jfb/assets/themes/acme.css")]
    [TestCase("/a")]
    public void ALocalPath_IsAStylesheet(string stylesheet) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Stylesheet = stylesheet }]), Is.Empty);

    [TestCase("https://cdn.example.com/forms.css")]
    [TestCase("//cdn.example.com/forms.css")]
    [TestCase("css/forms.css")]
    [TestCase("/")]
    [TestCase("")]
    [TestCase("/forms.css?v=1")]
    [TestCase("/forms.css#top")]
    [TestCase("/forms css.css")]
    [TestCase("/forms\".css")]
    [TestCase("/forms<x>.css")]
    [TestCase("\\\\host\\forms.css")]
    public void AnythingElse_IsRefused_SinceNothingLoadsFromAnotherHost(string stylesheet) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Stylesheet = stylesheet }]), Is.EqualTo(new[]
        {
            $"The theme 'acme' has the stylesheet '{stylesheet}': a stylesheet is a local path on the site, such as /css/forms.css, since nothing loads from another host on a visitor's page.",
        }));

    [TestCase("a")]
    [TestCase("btn btn-primary")]
    [TestCase("md:w-1/2 hover:underline [mask-type:luminance]")]
    public void ClassNames_SeparatedBySingleSpaces_Pass(string classes) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Classes = new Dictionary<string, string> { ["control"] = classes } }]), Is.Empty);

    [TestCase("")]
    [TestCase(" a")]
    [TestCase("a ")]
    [TestCase("a  b")]
    [TestCase("a\tb")]
    [TestCase("a\"b")]
    [TestCase("a'b")]
    [TestCase("a<b")]
    [TestCase("a>b")]
    [TestCase("a&b")]
    [TestCase("caf\xE9")]
    public void OtherClasses_AreRefused(string classes) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Classes = new Dictionary<string, string> { ["control"] = classes } }]), Is.EqualTo(new[]
        {
            $"The theme 'acme' gives the slot 'control' the classes '{classes}': classes are names without quotes, angle brackets or ampersands, separated by single spaces.",
        }));

    [TestCase("card")]
    [TestCase("Control")]
    [TestCase("")]
    public void ASlotThatIsNone_IsRefused(string slot) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Classes = new Dictionary<string, string> { [slot] = "x" } }]), Is.EqualTo(new[]
        {
            $"The theme 'acme' gives classes to the slot '{slot}', which is none of FormBuilderThemeSlots.",
        }));

    [TestCase("--jfb-accent", "#0a7")]
    [TestCase("--jfb-radius-lg", "calc(0.5rem + 2px)")]
    [TestCase("--jfb-font", "\"Acme Sans\", system-ui, sans-serif")]
    public void AToken_OfOneDeclaration_Passes(string token, string value) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Tokens = new Dictionary<string, string> { [token] = value } }]), Is.Empty);

    [TestCase("--accent", "#0a7")]
    [TestCase("--jfb-Accent", "#0a7")]
    [TestCase("--jfb-", "#0a7")]
    [TestCase("--jfb--accent", "#0a7")]
    [TestCase("--jfb-accent\n", "#0a7")]
    [TestCase("--jfb-accent", "")]
    [TestCase("--jfb-accent", " ")]
    [TestCase("--jfb-accent", "red; color: blue")]
    [TestCase("--jfb-accent", "red } body { color: blue")]
    [TestCase("--jfb-accent", "red { x")]
    [TestCase("--jfb-accent", "</style>")]
    [TestCase("--jfb-accent", "URL(https://example.com/a.png)")]
    [TestCase("--jfb-accent", "red\nblue")]
    public void AnyOtherToken_IsRefused(string token, string value) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { Tokens = new Dictionary<string, string> { [token] = value } }]), Is.EqualTo(new[]
        {
            $"The theme 'acme' has the token '{token}': a token is --jfb- and lower-case letters, digits and hyphens, with a value of one declaration and no address.",
        }));

    [TestCase("acme")]
    [TestCase("2024")]
    [TestCase("acme-v2")]
    public void AFolderName_IsAViewFolder(string folder) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { ViewFolder = folder }]), Is.Empty);

    [TestCase("")]
    [TestCase("Acme")]
    [TestCase("../default")]
    [TestCase("acme/forms")]
    [TestCase("-acme")]
    [TestCase("acme\n")]
    [TestCase(null)]
    public void AnyOtherFolder_IsRefused(string? folder) =>
        Assert.That(FormBuilderThemes.Problems([.. BuiltIn, new Custom("acme") { ViewFolder = folder! }]), Is.EqualTo(new[]
        {
            $"The theme 'acme' has the view folder '{folder}': a folder name of lower-case letters, digits and hyphens below Views/JironFormBuilder/.",
        }));

    [Test]
    public void AnAliasOutsideTheGrammar_ALabelMissing_ATakenAlias_AndNoDefault_AreEachRefused()
    {
        var problems = FormBuilderThemes.Problems([new UnstyledTheme(), new Custom("Acme Forms"), new Custom("plain") { Label = " " }, new Custom("unstyled"), new Custom(null!)]);
        var custom = typeof(Custom).FullName;

        Assert.That(problems, Is.EqualTo(new[]
        {
            $"The theme {custom} has the alias 'Acme Forms': an alias is lower-case letters, digits and hyphens, starting with a letter, at most 64 characters.",
            $"The theme 'plain' ({custom}) has no label.",
            $"The theme {custom} has the alias '': an alias is lower-case letters, digits and hyphens, starting with a letter, at most 64 characters.",
            $"The alias 'unstyled' belongs to 2 themes: {typeof(UnstyledTheme).FullName}, {custom}.",
            "No theme has the alias 'default', which a form falls back to: replace DefaultTheme with a theme of that alias, never remove it.",
        }));
    }

    [Test]
    public void ASitesThemeOfTheAliasDefault_ReplacesTheBuiltInOne()
    {
        var own = new Custom("default");
        var registry = Registry([own, new UnstyledTheme()]);

        Assert.That(registry.Default, Is.SameAs(own));
    }

    [Test]
    public void TheRegistry_RefusesThemesWithProblems_NamingEveryOne()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => _ = Registry([new Custom("Bad"), new Custom("acme") { Stylesheet = "https://cdn.example.com/a.css" }]));

        Assert.That(failure!.Message.Split(Environment.NewLine), Has.Length.EqualTo(3).And.Some.Contains("'Bad'").And.Some.Contains("https://cdn.example.com/a.css").And.Some.Contains("No theme has the alias 'default'"));
    }

    [Test]
    public void TheStartupCheck_PassesTheBuiltInThemesAndASitesOwn_AndRefusesASiteWithAProblem_NamingIt()
    {
        var valid = new ThemeStartupCheck(Collection([.. BuiltIn, new AcmeTheme()]));
        var refused = new ThemeStartupCheck(Collection([.. BuiltIn, new Custom("acme") { ViewFolder = "../x" }])).Validate(Options.DefaultName, new FormBuilderStartupChecks());

        Assert.Multiple(() =>
        {
            Assert.That(valid.Validate(Options.DefaultName, new FormBuilderStartupChecks()).Succeeded, Is.True);
            Assert.That(refused.Failed, Is.True);
            Assert.That(refused.Failures, Is.EqualTo(new[] { "The theme 'acme' has the view folder '../x': a folder name of lower-case letters, digits and hyphens below Views/JironFormBuilder/." }));
        });
    }

    private static FormBuilderThemes Registry(IEnumerable<IFormBuilderTheme> themes) => new(themes, new RecordingLogger<FormBuilderThemes>());

    private static FormBuilderThemeCollection Collection(params IFormBuilderTheme[] themes) => new(() => themes);

    /// <summary>A site's own theme, as a composer adds it.</summary>
    public sealed class AcmeTheme : IFormBuilderTheme
    {
        /// <inheritdoc />
        public string Name => "acme";

        /// <inheritdoc />
        public string Label => "Acme";

        /// <inheritdoc />
        public string? Stylesheet => "/css/acme-forms.css";

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string> { [FormBuilderThemeSlots.Control] = "acme-input", [FormBuilderThemeSlots.ButtonPrimary] = "acme-button acme-button--main" };

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string> { ["--jfb-accent"] = "#0a7d5c" };

        /// <inheritdoc />
        public bool DarkScheme => false;

        /// <inheritdoc />
        public string ViewFolder => "acme";
    }

    // A theme whose every member a test sets, valid unless it sets something wrong.
    private sealed class Custom(string name) : IFormBuilderTheme
    {
        public string Name => name;

        public string Label { get; init; } = "Custom";

        public string? Stylesheet { get; init; }

        public IReadOnlyDictionary<string, string> Classes { get; init; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Tokens { get; init; } = new Dictionary<string, string>();

        public bool DarkScheme => false;

        public string ViewFolder { get; init; } = "custom";
    }

    // A value without its spaces and with no zero before a decimal point, as formatters write either.
    private static string Plain(string value) => LeadingZero().Replace(value.Replace(" ", string.Empty, StringComparison.Ordinal), ".");

    // var(--jfb-..., fallback), the fallback held to balanced parentheses two deep.
    [GeneratedRegex(@"var\((?<token>--jfb-[a-z0-9-]+)\s*(?:,(?<fallback>(?:[^()]|\((?:[^()]|\([^()]*\))*\))*))?\)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRead();

    [GeneratedRegex(@"(?<![0-9])0\.", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingZero();
}
