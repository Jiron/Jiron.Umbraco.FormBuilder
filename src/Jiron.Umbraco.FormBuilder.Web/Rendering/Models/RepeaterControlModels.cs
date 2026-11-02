namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>
/// The control of a repeatable group (<c>repeater</c>, <c>jfb-repeater</c>, ADR-0098): its rows, each a fieldset with
/// its legend and its fields, and the submit buttons that add and remove a row, which a page without JavaScript posts.
/// </summary>
public sealed class RepeaterControlModel
{
    /// <summary>The rows, in order.</summary>
    public required IReadOnlyList<RepeaterRowModel> Rows { get; init; }

    /// <summary>A row as a first render starts it, after the others, which the runtime adds in place (ADR-0154).</summary>
    public required RepeaterRowModel Template { get; init; }

    /// <summary>The least rows the group keeps.</summary>
    public required int Least { get; init; }

    /// <summary>The most rows the group takes.</summary>
    public required int Most { get; init; }

    /// <summary>A row's legend, {0} its number from 1.</summary>
    public required string Legend { get; init; }

    /// <summary>What the runtime says of a row removed, {0} its legend.</summary>
    public required string Removed { get; init; }

    /// <summary>The button that adds a row; null once the group has its most rows.</summary>
    public required RowButtonModel Add { get; init; }

    /// <summary>
    /// The hidden input that says how many rows the page shows (<c>jfb-rows-&lt;alias&gt;</c>), so a post without
    /// JavaScript keeps a row whose controls post nothing (ADR-0100); disabled with the group.
    /// </summary>
    public required HiddenInputModel RowCount { get; init; }

    /// <summary>The classes of each row's fields: <c>jfb-fields</c> with the columns of the group the field is in.</summary>
    public required string FieldsCssClass { get; init; }

    /// <summary>The view that writes each of a row's fields, as it writes a group's.</summary>
    public required string FieldView { get; init; }
}

/// <summary>One row of a repeatable group (<c>jfb-row</c>): its number, its id, its legend, its Remove button and its fields.</summary>
public sealed class RepeaterRowModel
{
    /// <summary>The row's number from 0 (<c>data-jfb-row</c>), as its fields' names hold it.</summary>
    public required int Index { get; init; }

    /// <summary>The row's id, which its fields' ids start with.</summary>
    public required string Id { get; init; }

    /// <summary>The row's legend: the row label and the row's number from 1, such as Guest 1.</summary>
    public required string Legend { get; init; }

    /// <summary>The button that removes the row; null while the group has no more than its least rows.</summary>
    public required RowButtonModel Remove { get; init; }

    /// <summary>The row's fields, each named by its path, such as <c>guests[0].name</c>.</summary>
    public required IReadOnlyList<FieldViewModel> Fields { get; init; }
}

/// <summary>A submit button that adds or removes a row of a repeatable group, named <c>jfb-nav</c> as navigation's are.</summary>
public sealed class RowButtonModel
{
    /// <summary>What it posts as <c>jfb-nav</c>: <c>add:&lt;alias&gt;</c>, or <c>remove:&lt;alias&gt;:&lt;row&gt;</c> with the row from 0.</summary>
    public required string Value { get; init; }

    /// <summary>Its visible words.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The address it posts to (<c>formaction</c>): the form's, with the row added or the group as the fragment, so the
    /// page opens where the visitor pressed (ADR-0101).
    /// </summary>
    public required string Action { get; init; }

    /// <summary>Whether the group's limits hide it for now: at its most rows, or at its least (ADR-0154).</summary>
    public bool IsHidden { get; init; }

    /// <summary>What its accessible name adds after its words, visually hidden: the row's legend for Remove; null for none.</summary>
    public string? Context { get; init; }
}
