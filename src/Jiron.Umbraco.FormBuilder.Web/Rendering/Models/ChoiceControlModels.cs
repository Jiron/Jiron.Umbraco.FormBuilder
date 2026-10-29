using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>A line a control names in <c>aria-describedby</c>, such as how to choose several options.</summary>
/// <param name="Text">The line.</param>
/// <param name="Id">Its id.</param>
public sealed record HintModel(string Text, string Id);

/// <summary>The image on an option's card (<c>jfb-choices-cards</c>), which adds nothing to the option's label.</summary>
/// <param name="Url">The image's address.</param>
/// <param name="Width">Its width in pixels, when the media item gives it.</param>
/// <param name="Height">Its height in pixels, when the media item gives it.</param>
public sealed record ChoiceImageModel(string Url, int? Width, int? Height);

/// <summary>One choice of a group of choices: its input and its label (<c>jfb-choice</c>, ADR-0096).</summary>
public sealed class ChoiceModel
{
    /// <summary>The input's id: the field's id for the first choice, then the field's id and <c>-2</c>, <c>-3</c> and on.</summary>
    public required string Id { get; init; }

    /// <summary>The value the input posts; empty for the "Other" choice, whose text is the answer.</summary>
    public required string Value { get; init; }

    /// <summary>The label: the option's label, or its value when it has none.</summary>
    public required string Label { get; init; }

    /// <summary>Words after the label for screen readers alone, such as the word at a scale's end; null for none.</summary>
    public string? HiddenText { get; init; }

    /// <summary>The text below the label, which the input names in <c>aria-describedby</c>; null for none.</summary>
    public string? Description { get; init; }

    /// <summary>The id of the description: the input's id and <c>-text</c>.</summary>
    public string DescriptionId => Id + "-text";

    /// <summary>The image on the option's card; null for none, and for every layout but cards.</summary>
    public ChoiceImageModel? Image { get; init; }

    /// <summary>Whether the choice is chosen.</summary>
    public bool IsChecked { get; init; }
}

/// <summary>The "Other" choice of radio buttons or checkboxes, and the input for its text (<c>jfb-other</c>).</summary>
/// <param name="Choice">The choice, which posts no value of its own.</param>
/// <param name="TextLabel">The label of the text input, for screen readers.</param>
/// <param name="TextId">The text input's id: the field's id and <c>-other</c>.</param>
/// <param name="TextName">The text input's name: the alias and <c>.other</c> (ADR-0058).</param>
/// <param name="Text">The text after a post, or empty.</param>
public sealed record OtherChoiceModel(ChoiceModel Choice, string TextLabel, string TextId, string TextName, string Text);

/// <summary>
/// The words at the two ends of a scale or a rating (<c>jfb-ends</c>), shown below it and hidden from screen readers,
/// which hear them with the first and the last choice; an end without words is empty.
/// </summary>
/// <param name="Low">The word at the low end.</param>
/// <param name="High">The word at the high end.</param>
public sealed record ScaleEndsModel(string Low, string High);

/// <summary>
/// The control of a group of choices (<c>jfb-choices</c>, ADR-0096): radio buttons or checkboxes in a list, side by side
/// or as cards (<c>radio</c>, <c>checkboxes</c>), yes or no as two buttons (<c>yesno</c>), a scale and a rating.
/// </summary>
public sealed class ChoicesControlModel
{
    /// <summary>The inputs' <c>type</c>: <c>radio</c> or <c>checkbox</c>.</summary>
    public required string InputType { get; init; }

    /// <summary>
    /// The classes of the choices: <c>jfb-choices</c> and its layout's (<c>jfb-choices-horizontal</c>,
    /// <c>-cards</c>, <c>-buttons</c>, <c>-scale</c>, or <c>-rating</c> with its symbol's, such as
    /// <c>jfb-choices-star</c>).
    /// </summary>
    public required string CssClass { get; init; }

    /// <summary>The choices, in order; empty when the field has no options to offer.</summary>
    public required IReadOnlyList<ChoiceModel> Choices { get; init; }

    /// <summary>The "Other" choice with its text, or null.</summary>
    public OtherChoiceModel? Other { get; init; }

    /// <summary>The words at a scale's or a rating's ends, or null for none.</summary>
    public ScaleEndsModel? Ends { get; init; }

    /// <summary>
    /// What shows in place of the choices when there are none, such as a list whose source failed; it carries the
    /// field's id. Null when there are choices.
    /// </summary>
    public string? Empty { get; init; }
}

/// <summary>One option of a select (<c>option</c>).</summary>
/// <param name="Value">The value it posts.</param>
/// <param name="Label">The label: the option's label, or its value when it has none.</param>
/// <param name="IsSelected">Whether it is chosen.</param>
public sealed record SelectOptionModel(string Value, string Label, bool IsSelected);

/// <summary>
/// The control of a dropdown (<c>dropdown</c>), a list box that takes several choices, or a country (<c>country</c>):
/// a native <c>select</c> (ADR-0096).
/// </summary>
public sealed class SelectControlModel
{
    /// <summary>Whether several options may be chosen, in a list box (<c>multiple</c>).</summary>
    public bool Multiple { get; init; }

    /// <summary>How many lines a list box shows (<c>size</c>); null for a dropdown.</summary>
    public int? Size { get; init; }

    /// <summary>The dropdown's first line, which posts nothing; null for a list box.</summary>
    public string? Prompt { get; init; }

    /// <summary>The options, in order.</summary>
    public required IReadOnlyList<SelectOptionModel> Options { get; init; }

    /// <summary>How to choose several, for a list box; null otherwise.</summary>
    public HintModel? Hint { get; init; }

    /// <summary>The only line of a select without options, which is then disabled; null when there are options.</summary>
    public string? Empty { get; init; }
}

/// <summary>One statement of a grid: a fieldset with the statement as its legend and a choice per column.</summary>
public sealed class GridRowModel
{
    /// <summary>The row's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The statement.</summary>
    public required string Label { get; init; }

    /// <summary>The inputs' name: the alias, a dot and the row's key (ADR-0058).</summary>
    public required string Name { get; init; }

    /// <summary>Whether the row needs an answer, by the grid or by the row itself.</summary>
    public bool IsRequired { get; init; }

    /// <summary>How the statement is marked: the asterisk on a required row of a grid that is not itself required.</summary>
    public FieldMark Mark { get; init; }

    /// <summary>The words of the mark, or empty.</summary>
    public string MarkText { get; init; } = string.Empty;

    /// <summary>A choice per column, in order.</summary>
    public required IReadOnlyList<ChoiceModel> Choices { get; init; }
}

/// <summary>The control of a grid (<c>grid</c>, <c>jfb-matrix</c>): statements rated on shared columns (ADR-0096).</summary>
public sealed class GridControlModel
{
    /// <summary>The inputs' <c>type</c>: <c>radio</c>, or <c>checkbox</c> for a grid whose rows take several choices.</summary>
    public required string InputType { get; init; }

    /// <summary>The columns' labels, shown once above the rows (<c>jfb-matrix-head</c>) and hidden from screen readers.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>The rows, in order; none when the grid has no columns to offer.</summary>
    public required IReadOnlyList<GridRowModel> Rows { get; init; }

    /// <summary>What shows in place of the rows when the grid has no columns, carrying the field's id; null otherwise.</summary>
    public string? Empty { get; init; }
}

/// <summary>A move button of a ranking: a submit button that renders the page again with the option moved (ADR-0096).</summary>
/// <param name="Label">The button's words for screen readers, such as "Move Daylight up".</param>
/// <param name="Value">
/// The value it posts as <c>jfb-nav</c>: <c>up:&lt;alias&gt;:&lt;n&gt;</c> or <c>down:&lt;alias&gt;:&lt;n&gt;</c>, where
/// n counts the options from 1 in the order shown.
/// </param>
/// <param name="IsDisabled">Whether the option cannot move this way, at an end of the list (<c>aria-disabled</c>, so it keeps its place in the tab order).</param>
/// <param name="Id">The field's id on the first button, which the error summary links to; otherwise null.</param>
/// <param name="Action">
/// The address it posts to (<c>formaction</c>): the form's, with the option's place after the move as the fragment, so
/// the page opens where the visitor pressed (ADR-0101).
/// </param>
public sealed record RankingMoveModel(string Label, string Value, bool IsDisabled, string? Id, string Action);

/// <summary>One option of a ranking, in the order the visitor gave or the options' own.</summary>
public sealed class RankingItemModel
{
    /// <summary>The id of the option's place, <c>&lt;field id&gt;-place-&lt;n&gt;</c>, at which a move opens the page (ADR-0101).</summary>
    public required string Id { get; init; }

    /// <summary>The option's label.</summary>
    public required string Label { get; init; }

    /// <summary>The option's position from 1; null for an option below the ones a ranking of the top few asks for.</summary>
    public int? Position { get; init; }

    /// <summary>The hidden input that posts the position: the alias, a dot and the option's value (ADR-0058); null without a position.</summary>
    public HiddenInputModel? Input { get; init; }

    /// <summary>
    /// The hidden input that posts the option's value in the order shown, <c>jfb-order-&lt;alias&gt;</c>, which a move
    /// without JavaScript acts on (ADR-0100).
    /// </summary>
    public required HiddenInputModel Order { get; init; }

    /// <summary>The button that moves the option up.</summary>
    public required RankingMoveModel Up { get; init; }

    /// <summary>The button that moves the option down.</summary>
    public required RankingMoveModel Down { get; init; }
}

/// <summary>The control of a ranking (<c>ranking</c>, <c>jfb-ranking</c>): options put in order with move buttons (ADR-0096).</summary>
public sealed class RankingControlModel
{
    /// <summary>The options in their order.</summary>
    public required IReadOnlyList<RankingItemModel> Items { get; init; }

    /// <summary>What shows in place of the list when there are no options, carrying the field's id; null otherwise.</summary>
    public string? Empty { get; init; }
}

/// <summary>
/// The control of one box (ADR-0096): a single checkbox (<c>checkbox</c>), a consent (<c>consent</c>), or yes or no as a
/// switch (<c>yesno</c>, <c>jfb-switch</c>). The box's label is the type's text when it has one, otherwise the field's
/// label.
/// </summary>
public sealed class SingleChoiceControlModel
{
    /// <summary>The value the box posts when ticked.</summary>
    public required string Value { get; init; }

    /// <summary>The label beside the box: a text the type holds, sanitised, or the field's label, encoded.</summary>
    public required IHtmlContent Label { get; init; }

    /// <summary>Whether the box is ticked.</summary>
    public bool IsChecked { get; init; }

    /// <summary>
    /// A consent's hidden input with the hash of the wording it shows, <c>jfb-consent-&lt;alias&gt;</c> (ADR-0015); null for
    /// any other box, and for a consent whose wording shows nothing.
    /// </summary>
    public HiddenInputModel? Wording { get; init; }
}
