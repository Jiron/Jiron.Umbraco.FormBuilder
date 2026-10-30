using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>The themes Jiron Form Builder ships (ADR-0015, ADR-0105), in the order the backoffice lists them.</summary>
public static class BuiltInThemes
{
    /// <summary>The alias of the theme every other resolution falls back to.</summary>
    public const string DefaultAlias = "default";

    /// <summary>The built-in themes' types.</summary>
    public static IReadOnlyList<Type> All { get; } = [typeof(DefaultTheme), typeof(UnstyledTheme), typeof(Bootstrap5Theme)];
}

/// <summary>
/// The default theme (ADR-0022, ADR-0106): the design of ROADMAP 2.3, with an opt-in dark scheme, its stylesheet styling
/// only the forms that carry <c>jfb-theme-default</c>. A site's theme that builds on its stylesheet adds that class to
/// its form slot.
/// </summary>
public sealed class DefaultTheme : IFormBuilderTheme
{
    /// <summary>The address of the default theme's stylesheet, one of the package's website assets.</summary>
    public const string StylesheetPath = "/jfb/assets/themes/default.css";

    /// <inheritdoc />
    public string Name => BuiltInThemes.DefaultAlias;

    /// <inheritdoc />
    public string Label => "Default";

    /// <inheritdoc />
    public string? Stylesheet => StylesheetPath;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Classes { get; } = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// The public tokens and their light defaults (<c>docs/ux/tokens.md</c>), the dark scheme's own aside;
    /// <c>--jfb-font-family</c> and <c>--jfb-font-size</c> have none, since the form takes the site's.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["--jfb-color-text"] = "#1d2433",
        ["--jfb-color-text-muted"] = "#5b6372",
        ["--jfb-color-surface"] = "#fff",
        ["--jfb-color-border"] = "#767c88",
        ["--jfb-color-rule"] = "#d9dde3",
        ["--jfb-color-accent"] = "#0f6e5c",
        ["--jfb-color-on-accent"] = "#fff",
        ["--jfb-color-focus"] = "#1a5fd1",
        ["--jfb-color-error"] = "#b3261e",
        ["--jfb-shade"] = "0 1px 2px rgb(29 36 51 / .08), 0 4px 16px -2px rgb(29 36 51 / .12)",
        ["--jfb-radius"] = "12px",
        ["--jfb-radius-panel"] = "var(--jfb-radius, 12px)",
        ["--jfb-radius-card"] = "calc(var(--jfb-radius, 12px) * .75)",
        ["--jfb-radius-control"] = "calc(var(--jfb-radius, 12px) / 2)",
        ["--jfb-radius-tag"] = "calc(var(--jfb-radius, 12px) / 4)",
        ["--jfb-space-1"] = "4px",
        ["--jfb-space-2"] = "8px",
        ["--jfb-space-3"] = "12px",
        ["--jfb-space-4"] = "16px",
        ["--jfb-space-5"] = "24px",
        ["--jfb-space-6"] = "32px",
        ["--jfb-space-7"] = "48px",
        ["--jfb-line-height"] = "1.5",
        ["--jfb-font-size-small"] = ".875em",
        ["--jfb-font-size-large"] = "1.25em",
        ["--jfb-font-size-title"] = "1.5em",
        ["--jfb-font-weight-strong"] = "600",
        ["--jfb-size-control"] = "48px",
        ["--jfb-size-form"] = "960px",
        ["--jfb-focus-ring"] = "2px solid var(--jfb-color-focus, #1a5fd1)",
        ["--jfb-motion-fast"] = "120ms",
        ["--jfb-motion-base"] = "200ms",
        ["--jfb-motion-ease"] = "cubic-bezier(.23, 1, .32, 1)",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool DarkScheme => true;

    /// <inheritdoc />
    public string ViewFolder => BuiltInThemes.DefaultAlias;
}

/// <summary>Semantic markup and Jiron Form Builder's classes only, with the base rules every theme ships (ADR-0015).</summary>
public sealed class UnstyledTheme : IFormBuilderTheme
{
    /// <inheritdoc />
    public string Name => "unstyled";

    /// <inheritdoc />
    public string Label => "Unstyled";

    /// <inheritdoc />
    public string? Stylesheet => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Classes { get; } = FrozenDictionary<string, string>.Empty;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tokens { get; } = FrozenDictionary<string, string>.Empty;

    /// <inheritdoc />
    public bool DarkScheme => false;

    /// <inheritdoc />
    public string ViewFolder => Name;
}

/// <summary>
/// Bootstrap 5's own classes on the default markup, for a site that loads Bootstrap itself (ADR-0015, ADR-0108); Jiron
/// Form Builder never ships Bootstrap. Its stylesheet draws only what Bootstrap has no class for, from Bootstrap's own
/// variables, so it follows the site's Bootstrap and its colour modes; it styles only the forms that carry
/// <c>jfb-theme-bootstrap5</c>.
/// </summary>
public sealed class Bootstrap5Theme : IFormBuilderTheme
{
    /// <summary>The address of the stylesheet for what Bootstrap lacks, one of the package's website assets.</summary>
    public const string StylesheetPath = "/jfb/assets/themes/bootstrap5.css";

    /// <inheritdoc />
    public string Name => "bootstrap5";

    /// <inheritdoc />
    public string Label => "Bootstrap 5";

    /// <inheritdoc />
    public string? Stylesheet => StylesheetPath;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
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
        [FormBuilderThemeSlots.Button] = "btn btn-secondary",
        [FormBuilderThemeSlots.ButtonPrimary] = "btn btn-primary",
        [FormBuilderThemeSlots.ButtonLink] = "btn btn-link",
        [FormBuilderThemeSlots.ErrorSummary] = "alert alert-danger",
        [FormBuilderThemeSlots.Notice] = "alert alert-secondary",
        [FormBuilderThemeSlots.Outcome] = "alert alert-success",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tokens { get; } = FrozenDictionary<string, string>.Empty;

    /// <inheritdoc />
    public bool DarkScheme => false;

    /// <inheritdoc />
    public string ViewFolder => Name;
}
