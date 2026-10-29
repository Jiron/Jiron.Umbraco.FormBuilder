using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>
/// The control of a date, a time or a date and time (<c>date</c>, <c>time</c>, <c>datetime</c>): the browser's own
/// input, with the limits the server checks (ADR-0097).
/// </summary>
public sealed class DateControlModel
{
    /// <summary>The input's <c>type</c>: <c>date</c>, <c>time</c> or <c>datetime-local</c>.</summary>
    public required string InputType { get; init; }

    /// <summary>
    /// The earliest answer (<c>min</c>) as the input writes it, a limit relative to today worked out for the site's
    /// today; null for none.
    /// </summary>
    public string? Min { get; init; }

    /// <summary>The latest answer (<c>max</c>) as the input writes it, worked out as <see cref="Min"/> is; null for none.</summary>
    public string? Max { get; init; }

    /// <summary>The step in seconds (<c>step</c>) for a time that keeps a step of minutes; null for any minute or day.</summary>
    public string? Step { get; init; }
}

/// <summary>
/// The control of a slider (<c>slider</c>): a range input in <c>jfb-range</c> and, when the field shows it, its value
/// beside it for sight (an <c>output</c>); screen readers hear the value from the slider itself.
/// </summary>
public sealed class SliderControlModel
{
    /// <summary>The lowest number (<c>min</c>).</summary>
    public required string Min { get; init; }

    /// <summary>The highest number (<c>max</c>).</summary>
    public required string Max { get; init; }

    /// <summary>The step between two positions (<c>step</c>).</summary>
    public required string Step { get; init; }

    /// <summary>The position (<c>value</c>): the answer, or the middle of the range on its step, where a browser puts a slider without one.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// The value as a screen reader says it (<c>aria-valuetext</c>): the number in the culture's format with the field's
    /// unit; null when that is the number as the slider holds it.
    /// </summary>
    public string? ValueText { get; init; }

    /// <summary>The value as it shows beside the slider, or null when the field does not show it.</summary>
    public string? Shown { get; init; }
}

/// <summary>
/// The control of a calculation (<c>calculation</c>): its result as the submission pipeline works it out for the page's
/// answers (<c>output.jfb-output</c>), so a page without JavaScript shows it too; the runtime works it out again as the
/// visitor answers.
/// </summary>
public sealed class CalculationControlModel
{
    /// <summary>
    /// The result in the culture's format with the field's places, between its prefix and its suffix; empty while there
    /// is none, such as before a field it reads is answered.
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>A part of a name or an address: its own label and input, named <c>&lt;alias&gt;.&lt;part&gt;</c> (ADR-0058).</summary>
public sealed class PartModel
{
    /// <summary>The part's name, such as <c>first</c> or <c>postcode</c>.</summary>
    public required string Part { get; init; }

    /// <summary>The part's label.</summary>
    public required string Label { get; init; }

    /// <summary>The input's id: the field's for the first part, so the error summary links to it, otherwise the field's and the part's.</summary>
    public required string Id { get; init; }

    /// <summary>The input's name, the alias and the part.</summary>
    public required string Name { get; init; }

    /// <summary>The part's answer, or empty.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The part's <c>autocomplete</c> token, such as <c>given-name</c>.</summary>
    public required string Autocomplete { get; init; }

    /// <summary>Whether the part takes the full width (<c>jfb-part-wide</c>), as a street does.</summary>
    public bool IsWide { get; init; }

    /// <summary>Whether the part needs a text now (<c>required</c>): the field is required and the part is one it needs.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Whether the part carries the field's error (<c>aria-invalid</c>): a part it needs that is empty, or every part when none is.</summary>
    public bool IsInvalid { get; init; }

    /// <summary>
    /// How the part's label is marked: only when the field is required and needs some of its parts but not all, the
    /// parts it needs with the asterisk or the others as optional, in the form's style.
    /// </summary>
    public FieldMark Mark { get; init; }

    /// <summary>The words of the mark: "(optional)", or the asterisk; empty for none.</summary>
    public string MarkText { get; init; } = string.Empty;

    /// <summary>For an address's country, the countries named in the page's culture, the answer selected; null for a text.</summary>
    public SelectControlModel? Countries { get; init; }
}

/// <summary>
/// The control of a field in parts, a name or an address (<c>name</c>, <c>address</c>): the parts the field asks for, in
/// its type's order, in <c>jfb-parts</c>; the field's fieldset names the help and the error.
/// </summary>
public sealed class PartsControlModel
{
    /// <summary>The parts.</summary>
    public required IReadOnlyList<PartModel> Parts { get; init; }
}

/// <summary>
/// The control of a file upload (<c>file</c>): the browser's own file input in <c>jfb-upload</c>, which the runtime turns
/// into its drop zone and buttons (<c>data-jfb-enhanced</c>).
/// </summary>
public sealed class FileControlModel
{
    /// <summary>The extensions the field takes, each with its point (<c>accept</c>); the server's checks still decide.</summary>
    public required string Accept { get; init; }

    /// <summary>Whether the field takes more than one file (<c>multiple</c>).</summary>
    public bool Multiple { get; init; }

    /// <summary>Whether files can be dropped on the field once the runtime runs (<c>data-jfb-dropzone</c>).</summary>
    public bool DropZone { get; init; }
}

/// <summary>
/// The control of a signature (<c>signature</c>) without JavaScript: the name typed instead, which counts as the
/// signature; the runtime adds the pad to draw on and the way back to typing.
/// </summary>
public sealed class SignatureControlModel
{
    /// <summary>The label of the typed name, for screen readers: the field's legend is the only label that shows.</summary>
    public required string Label { get; init; }

    /// <summary>The input's name, the alias and <c>.typed</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The name typed after a post, or empty.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The line that says typing counts as signing.</summary>
    public required string Help { get; init; }

    /// <summary>The id of <see cref="Help"/>, which the input names in <c>aria-describedby</c>.</summary>
    public required string HelpId { get; init; }
}

/// <summary>The place of the form's captcha (<c>captcha</c>), which its provider fills (7.2).</summary>
/// <param name="Provider">The provider's alias (<c>data-jfb-captcha</c>).</param>
public sealed record CaptchaControlModel(string Provider);

/// <summary>
/// What the control of a site's own field type gets (ADR-0015): its settings as the form endpoint shows them in the
/// culture, a text with merge tags rendered for the page's answers, and the parts of its answer; the field's values
/// are on the field.
/// </summary>
public sealed class CustomControlModel
{
    /// <summary>The type's public settings, or null for none.</summary>
    public JsonElement? Settings { get; init; }

    /// <summary>The answer's parts by name, such as <c>street</c>; empty for none.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Parts { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
}
