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
/// The default theme (ADR-0022): the design of ROADMAP 2.3, with an opt-in dark scheme. Its stylesheet and tokens come
/// with its look (6.3b); until then it renders with the base rules alone.
/// </summary>
public sealed class DefaultTheme : IFormBuilderTheme
{
    /// <inheritdoc />
    public string Name => BuiltInThemes.DefaultAlias;

    /// <inheritdoc />
    public string Label => "Default";

    /// <inheritdoc />
    public string? Stylesheet => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Classes { get; } = FrozenDictionary<string, string>.Empty;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tokens { get; } = FrozenDictionary<string, string>.Empty;

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
/// Bootstrap 5's own classes on the default markup, for a site that loads Bootstrap itself (ADR-0015); Jiron Form
/// Builder never ships Bootstrap. Its small stylesheet for what Bootstrap lacks comes with its look (6.3c).
/// </summary>
public sealed class Bootstrap5Theme : IFormBuilderTheme
{
    /// <inheritdoc />
    public string Name => "bootstrap5";

    /// <inheritdoc />
    public string Label => "Bootstrap 5";

    /// <inheritdoc />
    public string? Stylesheet => null;

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
        [FormBuilderThemeSlots.Button] = "btn btn-outline-secondary",
        [FormBuilderThemeSlots.ButtonPrimary] = "btn btn-primary",
        [FormBuilderThemeSlots.ButtonLink] = "btn btn-link",
        [FormBuilderThemeSlots.ErrorSummary] = "alert alert-danger",
        [FormBuilderThemeSlots.Notice] = "alert alert-secondary",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tokens { get; } = FrozenDictionary<string, string>.Empty;

    /// <inheritdoc />
    public bool DarkScheme => false;

    /// <inheritdoc />
    public string ViewFolder => Name;
}
