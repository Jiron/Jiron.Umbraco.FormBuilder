using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>
/// A text before or after an input, such as a currency or a unit (<c>jfb-affix</c>), with the id the input names in
/// <c>aria-describedby</c>.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="Id">Its id.</param>
public sealed record AffixModel(string Text, string Id);

/// <summary>The line that tells how many characters a text takes (<c>jfb-counter</c>), which the runtime keeps current.</summary>
/// <param name="Text">The line, such as "Up to 255 characters".</param>
/// <param name="Id">Its id, which the input names in <c>aria-describedby</c>.</param>
/// <param name="Limit">The most characters the text takes.</param>
/// <param name="Left">The line with characters left, {0} how many.</param>
/// <param name="LeftOne">The line with one left.</param>
/// <param name="Over">The line past the limit, {0} by how many.</param>
/// <param name="OverOne">The line one past it.</param>
public sealed record CounterModel(string Text, string Id, int Limit, string Left, string LeftOne, string Over, string OverOne);

/// <summary>The control of a one-line input: <c>text</c>, <c>email</c>, <c>phone</c> and <c>url</c>.</summary>
public sealed class TextControlModel
{
    /// <summary>The input's <c>type</c>: <c>text</c>, <c>email</c>, <c>tel</c> or <c>url</c>.</summary>
    public required string InputType { get; init; }

    /// <summary>Whether the browser checks the spelling (<c>spellcheck</c>); null for the browser's default.</summary>
    public bool? Spellcheck { get; init; }

    /// <summary>The text before the input, or null.</summary>
    public AffixModel? Prefix { get; init; }

    /// <summary>The text after the input, or null.</summary>
    public AffixModel? Suffix { get; init; }

    /// <summary>The character counter, or null.</summary>
    public CounterModel? Counter { get; init; }

    /// <summary>For an email field that asks for the address twice, the second input; otherwise null.</summary>
    public ConfirmInputModel? Confirm { get; init; }
}

/// <summary>The second input of an email field that asks for the address twice (the part <c>confirm</c>).</summary>
/// <param name="Label">Its label.</param>
/// <param name="Id">Its id, the field's id and <c>-confirm</c>.</param>
/// <param name="Name">Its name, the alias and <c>.confirm</c>.</param>
/// <param name="Value">The address typed again after a post, or empty.</param>
public sealed record ConfirmInputModel(string Label, string Id, string Name, string Value);

/// <summary>The control of a long text (<c>textarea</c>).</summary>
public sealed class TextAreaControlModel
{
    /// <summary>How many lines the input shows at first.</summary>
    public required int Rows { get; init; }

    /// <summary>Whether the input grows with its text, which the runtime does.</summary>
    public bool AutoGrow { get; init; }

    /// <summary>The character counter, or null.</summary>
    public CounterModel? Counter { get; init; }
}

/// <summary>The control of a number (<c>number</c>).</summary>
public sealed class NumberControlModel
{
    /// <summary>The input's <c>type</c>: <c>number</c> for whole numbers, <c>text</c> for numbers with places, which every keyboard can write.</summary>
    public required string InputType { get; init; }

    /// <summary>
    /// The keyboard a phone shows (<c>inputmode</c>): <c>numeric</c> or <c>decimal</c>; null for a number that may be
    /// below zero, as those keyboards lack the minus sign.
    /// </summary>
    public string? InputMode { get; init; }

    /// <summary>The lowest number (<c>min</c>), for a number input; otherwise null.</summary>
    public string? Min { get; init; }

    /// <summary>The highest number (<c>max</c>), for a number input; otherwise null.</summary>
    public string? Max { get; init; }

    /// <summary>The step (<c>step</c>), for a number input; otherwise null.</summary>
    public string? Step { get; init; }

    /// <summary>The text before the input, or null.</summary>
    public AffixModel? Prefix { get; init; }

    /// <summary>The text after the input, or null.</summary>
    public AffixModel? Suffix { get; init; }
}

/// <summary>The control of a heading (<c>heading</c>).</summary>
/// <param name="Text">The heading's text.</param>
/// <param name="Level">Its level, 2 to 4 (<c>h2</c> to <c>h4</c>).</param>
public sealed record HeadingControlModel(string Text, int Level);

/// <summary>The control of a text block (<c>paragraph</c>).</summary>
/// <param name="Html">The text, sanitised.</param>
public sealed record TextBlockControlModel(IHtmlContent Html)
{
    /// <summary>Whether the text is empty, as one that recalls answers may render; the block is then hidden.</summary>
    public bool IsEmpty { get; init; }
}

/// <summary>The control of an image (<c>image</c>).</summary>
public sealed class ImageControlModel
{
    /// <summary>The image's address, or null when its media item is gone; the image then renders nothing.</summary>
    public string? Url { get; init; }

    /// <summary>The alternative text.</summary>
    public required string Alt { get; init; }

    /// <summary>How large the image shows: <c>small</c>, <c>medium</c>, <c>large</c> or <c>full</c> (<c>jfb-image-&lt;size&gt;</c>).</summary>
    public required string Size { get; init; }

    /// <summary>The image's width in pixels, when the media item gives it, so the page keeps its place while it loads.</summary>
    public int? Width { get; init; }

    /// <summary>The image's height in pixels, when the media item gives it.</summary>
    public int? Height { get; init; }
}
