namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>
/// The model of <c>Summary.cshtml</c>: the answers of the pages before the last one, which the last page shows above its
/// own fields with the summary page on (ADR-0122), a section per page.
/// </summary>
public sealed class SummaryViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>A section for each page the rules show before the summary's own, in order.</summary>
    public required IReadOnlyList<SummarySectionModel> Sections { get; init; }
}

/// <summary>One page's answers on the summary page.</summary>
public sealed class SummarySectionModel
{
    /// <summary>The page's key.</summary>
    public required Guid Page { get; init; }

    /// <summary>The page's title as the page shows it, or "Page 2" for a page without one, as the progress names it.</summary>
    public required string Title { get; init; }

    /// <summary>The id of the section's heading, which names the section.</summary>
    public required string HeadingId { get; init; }

    /// <summary>A row for each field of the page that takes an answer and that the rules show, in order.</summary>
    public required IReadOnlyList<SummaryAnswerModel> Answers { get; init; }
}

/// <summary>
/// One answer on the summary page: the field's label, its answer as the outcome's copy writes it, and the button that
/// opens its page (ADR-0122).
/// </summary>
public sealed class SummaryAnswerModel
{
    /// <summary>The field's key.</summary>
    public required Guid Field { get; init; }

    /// <summary>The field's label as the page shows it.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The answer, one text for a field, one for each answered row of a repeatable group, each split at its line breaks;
    /// empty when the field is unanswered or masked.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Texts { get; init; } = [];

    /// <summary>Whether the answer is a list of a repeatable group's rows.</summary>
    public bool IsRows { get; init; }

    /// <summary>Whether the field is sensitive, so its answer shows as <see cref="Mask"/> and <see cref="HiddenText"/>.</summary>
    public bool IsMasked { get; init; }

    /// <summary>What a masked answer shows: three bullets, as the outcome's copy masks it.</summary>
    public required string Mask { get; init; }

    /// <summary>What an unanswered field reads: "Not answered".</summary>
    public required string EmptyText { get; init; }

    /// <summary>What a screen reader hears for a masked answer: "Hidden for your privacy".</summary>
    public required string HiddenText { get; init; }

    /// <summary>The button that opens the field's page; null for a field nobody types, such as a calculation.</summary>
    public SummaryChangeModel? Change { get; init; }

    /// <summary>The field's error after a send from the summary page, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The id of the error, which describes the row's button.</summary>
    public required string ErrorId { get; init; }

    /// <summary>Whether the row writes the answer's texts: it is answered and not masked.</summary>
    public bool IsAnswered => Texts.Count > 0 && !IsMasked;

    /// <summary>Whether the row reads "Not answered".</summary>
    public bool IsEmpty => Texts.Count == 0 && !IsMasked;
}

/// <summary>
/// The "Change" of an answer on the summary page (ADR-0122): a submit button that posts <c>jfb-nav</c> with
/// <c>goto:&lt;alias&gt;</c> to the form's own action with the field's control as the fragment.
/// </summary>
/// <param name="Id">The button's id, which the error summary links to.</param>
/// <param name="Value">The button's value, <c>goto:&lt;alias&gt;</c>.</param>
/// <param name="Action">The button's <c>formaction</c>: the form's action, at the field; null in the preview, which never posts (ADR-0203).</param>
/// <param name="Text">The button's visible text, "Change".</param>
/// <param name="Label">The field's label, after the text for screen readers.</param>
public sealed record SummaryChangeModel(string Id, string Value, string? Action, string Text, string Label);
