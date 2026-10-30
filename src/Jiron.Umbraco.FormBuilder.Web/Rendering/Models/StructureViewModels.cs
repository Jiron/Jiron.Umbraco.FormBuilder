using System.Collections.Frozen;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>The model of <c>Page.cshtml</c>: one page of a form (ADR-0015).</summary>
public sealed class PageViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>The page's key (<c>data-jfb-page</c>).</summary>
    public required Guid Key { get; init; }

    /// <summary>The page's title, or null.</summary>
    public string? Title { get; init; }

    /// <summary>The text above the page's groups, sanitised, or null.</summary>
    public IHtmlContent? Description { get; init; }

    /// <summary>
    /// The names of the page's texts that hold merge tags (ADR-0095), such as <c>title</c>: the views mark each with
    /// <c>data-jfb-text</c> and write it however it renders, so the runtime renders it again as the visitor answers
    /// (ADR-0115).
    /// </summary>
    public IReadOnlySet<string> Recalling { get; init; } = FrozenSet<string>.Empty;

    /// <summary>Whether this is the page the visitor is on; the other pages carry <c>hidden</c>.</summary>
    public required bool IsCurrent { get; init; }

    /// <summary>Whether a rule hides the page (<c>hidden</c> and <c>data-jfb-hidden</c>).</summary>
    public required bool IsHidden { get; init; }

    /// <summary>The page's groups.</summary>
    public required IReadOnlyList<GroupViewModel> Groups { get; init; }

    /// <summary>The views that write the form's parts.</summary>
    public required FormViewPaths Views { get; init; }
}

/// <summary>The model of <c>Group.cshtml</c>: a fieldset when the group has a title, otherwise a plain block (ADR-0015).</summary>
public sealed class GroupViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>The group's key (<c>data-jfb-group</c>).</summary>
    public required Guid Key { get; init; }

    /// <summary>The group's title, the fieldset's legend; null for a group without one.</summary>
    public string? Title { get; init; }

    /// <summary>The text above the group's fields, sanitised, or null.</summary>
    public IHtmlContent? Description { get; init; }

    /// <summary>The id of the description, which the fieldset names in <c>aria-describedby</c>.</summary>
    public required string DescriptionId { get; init; }

    /// <summary>
    /// The names of the group's texts that hold merge tags (ADR-0095), such as <c>title</c>: the views mark each with
    /// <c>data-jfb-text</c> and write it however it renders, so the runtime renders it again as the visitor answers
    /// (ADR-0115).
    /// </summary>
    public IReadOnlySet<string> Recalling { get; init; } = FrozenSet<string>.Empty;

    /// <summary>The group's classes: <c>jfb-group</c>, its columns and its style.</summary>
    public required string CssClass { get; init; }

    /// <summary>Whether a rule hides the group (<c>hidden</c> and <c>data-jfb-hidden</c>).</summary>
    public required bool IsHidden { get; init; }

    /// <summary>The group's fields that render, in order; a hidden field renders nothing.</summary>
    public required IReadOnlyList<FieldViewModel> Fields { get; init; }

    /// <summary>The views that write the form's parts.</summary>
    public required FormViewPaths Views { get; init; }
}

/// <summary>What labels a field (ADR-0015).</summary>
public enum FieldLabelElement
{
    /// <summary>Nothing: a field without a value, such as a heading.</summary>
    None,

    /// <summary>A <c>label</c> for the control.</summary>
    Label,

    /// <summary>
    /// A <c>fieldset</c> around the field's help, control and error, with the label as its <c>legend</c>: a group of
    /// choices, such as radio buttons, a grid or a ranking (ADR-0096).
    /// </summary>
    Legend,

    /// <summary>
    /// The control's own label beside its box, with the help after it: a single checkbox, a consent, or yes or no as a
    /// switch (ADR-0096).
    /// </summary>
    Choice,
}

/// <summary>
/// The states of a field in which a label's mark shows, as <c>data-jfb-mark</c> names them on a form with rules
/// (ADR-0111): a part's mark and a required field's asterisk show while the field is required, "(optional)" and a grid's
/// own required row's asterisk while it is not.
/// </summary>
public static class MarkWhile
{
    /// <summary>The mark shows while the field is required.</summary>
    public const string Required = "required";

    /// <summary>The mark shows while the field is not required.</summary>
    public const string Optional = "optional";
}

/// <summary>How a field's label is marked (the form's required-marker style, ADR-0093).</summary>
public enum FieldMark
{
    /// <summary>No mark.</summary>
    None,

    /// <summary>"(optional)" after the label of a field that is not required.</summary>
    Optional,

    /// <summary>An asterisk after the label of a required field, hidden from assistive technology.</summary>
    Required,
}

/// <summary>
/// The model of <c>Field.cshtml</c>, the wrapper of every field: its label, help, control and error (ADR-0015). The
/// control is written by <see cref="ControlView"/> with this model; <see cref="Control"/> carries what the field's type
/// adds.
/// </summary>
public sealed class FieldViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>The field's key (<c>data-jfb-field</c>).</summary>
    public required Guid Key { get; init; }

    /// <summary>The field's type (<c>data-jfb-type</c>).</summary>
    public required string Type { get; init; }

    /// <summary>The field's alias (<c>data-jfb-alias</c>), or null for a field without one, such as a heading.</summary>
    public string? Alias { get; init; }

    /// <summary>The control's id: the instance and the alias, such as <c>jfb-1-email</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The control's name, the alias; empty for a field without one.</summary>
    public required string Name { get; init; }

    /// <summary>The field's classes: <c>jfb-field</c>, its type's, its span and its own.</summary>
    public required string CssClass { get; init; }

    /// <summary>What labels the field.</summary>
    public required FieldLabelElement LabelElement { get; init; }

    /// <summary>The label, or null.</summary>
    public string? Label { get; init; }

    /// <summary>Whether the label is hidden from sight; it stays for screen readers.</summary>
    public bool HideLabel { get; init; }

    /// <summary>
    /// How the label is marked: the mark it shows, or on a form with rules the mark it carries whether or not it shows
    /// now (<see cref="IsMarkShown"/>).
    /// </summary>
    public FieldMark Mark { get; init; }

    /// <summary>The words of the mark: "(optional)", or the asterisk; empty for none.</summary>
    public string MarkText { get; init; } = string.Empty;

    /// <summary>Whether the mark shows now; a mark a form with rules carries for another state is written <c>hidden</c>.</summary>
    public bool IsMarkShown { get; init; } = true;

    /// <summary>
    /// On a form with rules, the field's state in which the mark shows (<c>data-jfb-mark</c>, a <see cref="MarkWhile"/>
    /// value), so the runtime shows it as the rules change whether the field is required; null otherwise (ADR-0111).
    /// </summary>
    public string? MarkShownWhile { get; init; }

    /// <summary>
    /// Whether the form has rules, so the views write the hooks the runtime applies them by (ADR-0111): the controls whose
    /// <c>required</c> follows the field's (<c>data-jfb-requires</c>) and a control that stays disabled when the field
    /// shows (<c>data-jfb-disabled</c>).
    /// </summary>
    public bool Hooks { get; init; }

    /// <summary>The help text below the label, or null.</summary>
    public string? Help { get; init; }

    /// <summary>The id of the help text.</summary>
    public required string HelpId { get; init; }

    /// <summary>The error after a post, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The id of the error.</summary>
    public required string ErrorId { get; init; }

    /// <summary>
    /// The ids the control names in <c>aria-describedby</c>, or the fieldset of a group of choices: its help, its
    /// control's parts and its error; null for none.
    /// </summary>
    public string? DescribedBy { get; init; }

    /// <summary>Whether an answer is required now, by the field or a rule (<c>required</c>).</summary>
    public bool IsRequired { get; init; }

    /// <summary>Whether a rule hides the field (<c>hidden</c> and <c>data-jfb-hidden</c>).</summary>
    public bool IsHidden { get; init; }

    /// <summary>Whether the controls are <c>disabled</c>: the field, its group or its page is hidden by a rule, so nothing is posted.</summary>
    public bool IsDisabled { get; init; }

    /// <summary>The placeholder, or null.</summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// The names of the field's texts that hold merge tags (ADR-0095), such as <c>title</c>: the views mark each with
    /// <c>data-jfb-text</c> and write it however it renders, so the runtime renders it again as the visitor answers
    /// (ADR-0115).
    /// </summary>
    public IReadOnlySet<string> Recalling { get; init; } = FrozenSet<string>.Empty;

    /// <summary>The <c>autocomplete</c> token, or null.</summary>
    public string? Autocomplete { get; init; }

    /// <summary>The field's answers: the visitor's after a post, else its default value; empty for none.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>The first answer, or empty.</summary>
    public string Value => Values.Count > 0 ? Values[0] : string.Empty;

    /// <summary>The view that writes the control.</summary>
    public required string ControlView { get; init; }

    /// <summary>What the field's type adds to the control (a <c>TextControlModel</c>, ...), or null.</summary>
    public object? Control { get; init; }
}
