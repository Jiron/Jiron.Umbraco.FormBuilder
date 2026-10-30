using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Themes;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>
/// The classes a form's views write, slot by slot (ADR-0015, ADR-0105): Jiron Form Builder's own, then the form's
/// theme's, which join them and never replace them. A slot with no class of Jiron Form Builder's own and none of the
/// theme's is null, so its attribute is left out. A view writes them as they are, such as
/// <c>class="@Model.Classes.Control"</c>.
/// </summary>
public sealed class ThemeClassesModel
{
    private readonly IReadOnlyDictionary<string, string> _theme;

    /// <summary>Jiron Form Builder's own classes, with none of a theme's.</summary>
    public static ThemeClassesModel None { get; } = new(FrozenDictionary<string, string>.Empty);

    /// <summary>The classes of a theme's class map, by slot.</summary>
    /// <param name="theme">The theme's classes by slot.</param>
    internal ThemeClassesModel(IReadOnlyDictionary<string, string> theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        Page = Join("jfb-page", FormBuilderThemeSlots.Page);
        Label = Join("jfb-label", FormBuilderThemeSlots.Label);
        PartLabel = Theme(FormBuilderThemeSlots.Label);
        Help = Join("jfb-help", FormBuilderThemeSlots.Help);
        Error = Join("jfb-error", FormBuilderThemeSlots.Error);
        Control = Join("jfb-control", FormBuilderThemeSlots.Control);
        Select = Join("jfb-control", FormBuilderThemeSlots.Select);
        Choices = Join("jfb-choices", FormBuilderThemeSlots.Choices);
        Choice = Join("jfb-choice", FormBuilderThemeSlots.Choice);
        ChoiceInput = Theme(FormBuilderThemeSlots.ChoiceInput);
        ChoiceLabel = Theme(FormBuilderThemeSlots.ChoiceLabel);
        SwitchChoice = Join(Choice, FormBuilderThemeSlots.Switch);
        SwitchInput = Join("jfb-switch", FormBuilderThemeSlots.ChoiceInput);
        Range = Theme(FormBuilderThemeSlots.Range);
        Affix = Join("jfb-affix", FormBuilderThemeSlots.Affix);
        AffixText = Theme(FormBuilderThemeSlots.AffixText);
        Counter = Join("jfb-counter", FormBuilderThemeSlots.Counter);
        Navigation = Join("jfb-navigation", FormBuilderThemeSlots.Navigation);
        Button = Join("jfb-button", FormBuilderThemeSlots.Button);
        ButtonPrimary = Join("jfb-button jfb-button-primary", FormBuilderThemeSlots.ButtonPrimary);
        ButtonLink = Join("jfb-button jfb-button-link", FormBuilderThemeSlots.ButtonLink);
        ButtonIcon = Join("jfb-button jfb-button-icon", FormBuilderThemeSlots.Button);
        ProgressBar = Join("jfb-progress jfb-progress-bar", FormBuilderThemeSlots.Progress);
        ProgressSteps = Join("jfb-progress jfb-progress-steps", FormBuilderThemeSlots.Progress);
        ErrorSummary = Join("jfb-error-summary", FormBuilderThemeSlots.ErrorSummary);
        Outcome = Join("jfb-outcome", FormBuilderThemeSlots.Outcome);
    }

    /// <summary>A page (<c>jfb-page</c>).</summary>
    public string Page { get; }

    /// <summary>A label or a legend (<c>jfb-label</c>).</summary>
    public string Label { get; }

    /// <summary>The label of a part of a name or an address, which has no class of Jiron Form Builder's own: the theme's classes for a label, or null.</summary>
    public string? PartLabel { get; }

    /// <summary>A help text (<c>jfb-help</c>).</summary>
    public string Help { get; }

    /// <summary>A field's error (<c>jfb-error</c>).</summary>
    public string Error { get; }

    /// <summary>A text input or a text area (<c>jfb-control</c>).</summary>
    public string Control { get; }

    /// <summary>A dropdown (<c>jfb-control</c>).</summary>
    public string Select { get; }

    /// <summary>The options of a choice field without a layout of their own (<c>jfb-choices</c>).</summary>
    public string Choices { get; }

    /// <summary>One option (<c>jfb-choice</c>).</summary>
    public string Choice { get; }

    /// <summary>An option's box or circle: the theme's classes only, or null.</summary>
    public string? ChoiceInput { get; }

    /// <summary>An option's label: the theme's classes only, or null.</summary>
    public string? ChoiceLabel { get; }

    /// <summary>The option of a yes or no question shown as a switch: an option's classes and the theme's for a switch.</summary>
    public string SwitchChoice { get; }

    /// <summary>A switch's box (<c>jfb-switch</c>), with the theme's classes for an option's box.</summary>
    public string SwitchInput { get; }

    /// <summary>A slider's input: the theme's classes only, or null.</summary>
    public string? Range { get; }

    /// <summary>A control with a prefix or a suffix (<c>jfb-affix</c>).</summary>
    public string Affix { get; }

    /// <summary>The text of a prefix or a suffix: the theme's classes only, or null.</summary>
    public string? AffixText { get; }

    /// <summary>A character counter (<c>jfb-counter</c>).</summary>
    public string Counter { get; }

    /// <summary>The buttons below a page (<c>jfb-navigation</c>).</summary>
    public string Navigation { get; }

    /// <summary>A button (<c>jfb-button</c>).</summary>
    public string Button { get; }

    /// <summary>The page's main button (<c>jfb-button jfb-button-primary</c>).</summary>
    public string ButtonPrimary { get; }

    /// <summary>A button that reads as a link (<c>jfb-button jfb-button-link</c>).</summary>
    public string ButtonLink { get; }

    /// <summary>A button that shows an icon, such as a ranking's move (<c>jfb-button jfb-button-icon</c>), with the theme's classes for a button.</summary>
    public string ButtonIcon { get; }

    /// <summary>The progress as a bar (<c>jfb-progress jfb-progress-bar</c>).</summary>
    public string ProgressBar { get; }

    /// <summary>The progress as steps (<c>jfb-progress jfb-progress-steps</c>).</summary>
    public string ProgressSteps { get; }

    /// <summary>The error summary (<c>jfb-error-summary</c>).</summary>
    public string ErrorSummary { get; }

    /// <summary>The outcome after sending (<c>jfb-outcome</c>).</summary>
    public string Outcome { get; }

    /// <summary>Our classes of a slot whose classes the models compose, with the theme's for that slot after them.</summary>
    /// <param name="ours">Jiron Form Builder's own classes.</param>
    /// <param name="slot">The slot, one of <see cref="FormBuilderThemeSlots"/>.</param>
    internal string Join(string ours, string slot) => _theme.TryGetValue(slot, out var theirs) && theirs.Length > 0 ? ours + " " + theirs : ours;

    // The theme's classes of a slot that has none of ours, or null.
    private string? Theme(string slot) => _theme.TryGetValue(slot, out var theirs) && theirs.Length > 0 ? theirs : null;
}
