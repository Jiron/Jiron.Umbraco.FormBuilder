using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>The model of <c>Navigation.cshtml</c>: the buttons below the current page (ADR-0015).</summary>
public sealed class NavigationViewModel
{
    /// <summary>The button back to the previous page, or null on the first page.</summary>
    public NavigationButtonModel? Back { get; init; }

    /// <summary>The button to the next page, or null on the last page.</summary>
    public NavigationButtonModel? Next { get; init; }

    /// <summary>The button that sends the form, or null before the last page.</summary>
    public NavigationButtonModel? Submit { get; init; }
}

/// <summary>A button of the navigation: a submit button named <c>jfb-nav</c>.</summary>
/// <param name="Label">The button's label.</param>
/// <param name="Value">Its value and <c>data-jfb-nav</c>: <c>back</c>, <c>next</c> or <c>submit</c>.</param>
public sealed record NavigationButtonModel(string Label, string Value);

/// <summary>How a form shows the visitor's progress through its pages.</summary>
public enum ProgressStyle
{
    /// <summary>The pages as numbered steps.</summary>
    Steps,

    /// <summary>A progress bar.</summary>
    Bar,
}

/// <summary>The model of <c>Progress.cshtml</c>: where the visitor is among the pages the rules show (ADR-0015).</summary>
public sealed class ProgressViewModel
{
    /// <summary>How the progress shows.</summary>
    public required ProgressStyle Style { get; init; }

    /// <summary>The number of the current page among the shown pages, from 1.</summary>
    public required int Current { get; init; }

    /// <summary>How many pages the rules show.</summary>
    public required int Total { get; init; }

    /// <summary>The progress in words, such as "Step 2 of 3".</summary>
    public required string Text { get; init; }

    /// <summary>The id of the text, which the bar names as its label.</summary>
    public required string TextId { get; init; }

    /// <summary>The accessible name of the list of steps, such as "Progress".</summary>
    public required string Label { get; init; }

    /// <summary>The shown pages as steps, in order.</summary>
    public required IReadOnlyList<ProgressStepModel> Steps { get; init; }
}

/// <summary>A step of the progress.</summary>
/// <param name="Title">The page's title, or "Page 2" for a page without one.</param>
/// <param name="IsCurrent">Whether it is the current page (<c>aria-current="step"</c>).</param>
/// <param name="IsDone">Whether it comes before the current page (<c>data-jfb-done</c>).</param>
public sealed record ProgressStepModel(string Title, bool IsCurrent, bool IsDone);

/// <summary>The model of <c>ErrorSummary.cshtml</c>: the errors after a post, each with a link to its field (ADR-0015).</summary>
public sealed class ErrorSummaryViewModel
{
    /// <summary>The summary's heading.</summary>
    public required string Heading { get; init; }

    /// <summary>The id of the heading, which labels the summary.</summary>
    public required string HeadingId { get; init; }

    /// <summary>The errors, in the form's order.</summary>
    public required IReadOnlyList<ErrorLinkModel> Errors { get; init; }
}

/// <summary>An error of the summary.</summary>
/// <param name="Message">The error.</param>
/// <param name="Target">The id of the control it links to.</param>
public sealed record ErrorLinkModel(string Message, string Target);

/// <summary>Why a notice shows.</summary>
public enum NoticeKind
{
    /// <summary>The form opens later.</summary>
    NotOpenYet,

    /// <summary>The form is closed.</summary>
    Closed,

    /// <summary>The form holds as many entries as it takes.</summary>
    Full,

    /// <summary>A notice above the form after a post, such as "the form has changed".</summary>
    Message,
}

/// <summary>The model of <c>Notice.cshtml</c>: a notice in place of the form or above it (ADR-0015).</summary>
public sealed class NoticeViewModel
{
    /// <summary>Why the notice shows.</summary>
    public required NoticeKind Kind { get; init; }

    /// <summary>The notice's heading, or null when its text has its own.</summary>
    public string? Heading { get; init; }

    /// <summary>The notice's text as HTML: the package's sentence in a paragraph, or the form's own message, sanitised.</summary>
    public required IHtmlContent Text { get; init; }
}

/// <summary>The model of <c>Outcome.cshtml</c>: the message after the visitor sent the form (ADR-0015).</summary>
public sealed class OutcomeViewModel
{
    /// <summary>The message, sanitised.</summary>
    public required IHtmlContent Message { get; init; }

    /// <summary>The line with the entry's reference, the reference in <c>strong</c>; null for an entry without one.</summary>
    public IHtmlContent? Reference { get; init; }

    /// <summary>Whether the visitor must still confirm their email address (<c>data-jfb-pending</c>).</summary>
    public bool IsPending { get; init; }
}
