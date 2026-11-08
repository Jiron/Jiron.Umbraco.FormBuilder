using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>
/// A theme of the website forms (section 4.9, ADR-0015, ADR-0105): every theme renders the same views, themed by a class
/// map, a stylesheet and <c>--jfb-*</c> tokens. A site adds one from a composer with
/// <c>builder.JironFormBuilder().Themes().Append&lt;AcmeTheme&gt;()</c>; the registry checks every theme at start-up.
/// </summary>
public interface IFormBuilderTheme
{
    /// <summary>
    /// The theme's alias, which forms, the General settings and <c>&lt;jfb-render theme&gt;</c> name it by, fixed once
    /// it is in use: lower-case letters, digits and hyphens, starting with a letter, at most 64 characters. Start-up
    /// refuses a name that two themes share or that is outside the grammar.
    /// </summary>
    string Name { get; }

    /// <summary>What the backoffice calls the theme: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    string Label { get; }

    /// <summary>
    /// The address of the theme's stylesheet on the site, a local path such as <c>/css/acme-forms.css</c>, or null for
    /// none. Nothing loads from another host on a visitor's page, so the registry refuses any other address.
    /// </summary>
    string? Stylesheet { get; }

    /// <summary>
    /// The classes the theme adds to the slots of <see cref="FormBuilderThemeSlots"/>, by slot, as class names separated
    /// by spaces; they join Jiron Form Builder's own classes and never replace them. A slot the theme leaves out gets
    /// none.
    /// </summary>
    IReadOnlyDictionary<string, string> Classes { get; }

    /// <summary>The tokens a theme based on this one may set (<c>--jfb-*</c>), with their default values.</summary>
    IReadOnlyDictionary<string, string> Tokens { get; }

    /// <summary>Whether the theme offers the dark scheme, which a site opts into with <c>jfb-scheme-dark</c> or <c>jfb-scheme-auto</c>.</summary>
    bool DarkScheme { get; }

    /// <summary>
    /// The colours the theme takes in the dark scheme, by token, each one of <see cref="Tokens"/> whose name starts with
    /// <c>--jfb-color-</c>: what the library's theme editor offers as a dark colour's default, and what a library theme
    /// based on this one keeps in the dark scheme when it changes only the light colour (ADR-0215). None by default; a
    /// theme without <see cref="DarkScheme"/> declares none.
    /// </summary>
    IReadOnlyDictionary<string, string> DarkTokens => FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// The folder below <c>Views/JironFormBuilder/</c> where a site's own views for the theme are found before the
    /// default ones; a folder name of lower-case letters, digits and hyphens.
    /// </summary>
    string ViewFolder { get; }
}

/// <summary>
/// The slots of a theme's class map (ADR-0015, ADR-0105), each where Jiron Form Builder writes its own classes, given
/// here, or none.
/// </summary>
public static class FormBuilderThemeSlots
{
    /// <summary>The form element (<c>jfb-form</c>).</summary>
    public const string Form = "form";

    /// <summary>A page (<c>jfb-page</c>).</summary>
    public const string Page = "page";

    /// <summary>A group (<c>jfb-group</c>).</summary>
    public const string Group = "group";

    /// <summary>A field's wrapper (<c>jfb-field</c>).</summary>
    public const string Field = "field";

    /// <summary>A field's label or legend (<c>jfb-label</c>).</summary>
    public const string Label = "label";

    /// <summary>A help text (<c>jfb-help</c>).</summary>
    public const string Help = "help";

    /// <summary>A field's error (<c>jfb-error</c>).</summary>
    public const string Error = "error";

    /// <summary>A text input or a text area (<c>jfb-control</c>).</summary>
    public const string Control = "control";

    /// <summary>A dropdown (<c>jfb-control</c>).</summary>
    public const string Select = "select";

    /// <summary>The options of a choice field (<c>jfb-choices</c>).</summary>
    public const string Choices = "choices";

    /// <summary>One option (<c>jfb-choice</c>).</summary>
    public const string Choice = "choice";

    /// <summary>An option's box or circle, which has no class of Jiron Form Builder's own.</summary>
    public const string ChoiceInput = "choice-input";

    /// <summary>An option's label, which has no class of Jiron Form Builder's own.</summary>
    public const string ChoiceLabel = "choice-label";

    /// <summary>A yes or no question shown as a switch (<c>jfb-choice jfb-switch</c>).</summary>
    public const string Switch = "switch";

    /// <summary>A slider's input, which has no class of Jiron Form Builder's own.</summary>
    public const string Range = "range";

    /// <summary>A control with a prefix or a suffix (<c>jfb-affix</c>).</summary>
    public const string Affix = "affix";

    /// <summary>The text of a prefix or a suffix, which has no class of Jiron Form Builder's own.</summary>
    public const string AffixText = "affix-text";

    /// <summary>A character counter (<c>jfb-counter</c>).</summary>
    public const string Counter = "counter";

    /// <summary>The buttons below a page (<c>jfb-navigation</c>).</summary>
    public const string Navigation = "navigation";

    /// <summary>A button (<c>jfb-button</c>).</summary>
    public const string Button = "button";

    /// <summary>The page's main button, Next or Send (<c>jfb-button jfb-button-primary</c>).</summary>
    public const string ButtonPrimary = "button-primary";

    /// <summary>A button that reads as a link, such as a row's Remove (<c>jfb-button jfb-button-link</c>).</summary>
    public const string ButtonLink = "button-link";

    /// <summary>The progress through the pages (<c>jfb-progress</c>).</summary>
    public const string Progress = "progress";

    /// <summary>The error summary (<c>jfb-error-summary</c>).</summary>
    public const string ErrorSummary = "error-summary";

    /// <summary>A notice (<c>jfb-notice</c>).</summary>
    public const string Notice = "notice";

    /// <summary>The outcome after sending (<c>jfb-outcome</c>).</summary>
    public const string Outcome = "outcome";

    /// <summary>Every slot, in the order of this list.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Form, Page, Group, Field, Label, Help, Error, Control, Select, Choices, Choice, ChoiceInput, ChoiceLabel, Switch, Range,
        Affix, AffixText, Counter, Navigation, Button, ButtonPrimary, ButtonLink, Progress, ErrorSummary, Notice, Outcome,
    ];
}
