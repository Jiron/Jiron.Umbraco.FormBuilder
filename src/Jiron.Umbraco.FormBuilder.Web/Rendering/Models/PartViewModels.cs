using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>The model of <c>Navigation.cshtml</c>: the buttons below the current page (ADR-0015).</summary>
public sealed class NavigationViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

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
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>How the progress shows.</summary>
    public required ProgressStyle Style { get; init; }

    /// <summary>Whether the rules show one page only, so that it does not show until they show another.</summary>
    public bool IsHidden { get; init; }

    /// <summary>The number of the current page among the shown pages, from 1.</summary>
    public required int Current { get; init; }

    /// <summary>How many pages the rules show.</summary>
    public required int Total { get; init; }

    /// <summary>The progress in words, such as "Page 2 of 3".</summary>
    public required string Text { get; init; }

    /// <summary>Those words with their numbers to fill in (<c>data-jfb-words</c>), which the runtime writes as it moves through the pages (ADR-0155).</summary>
    public string? Words { get; init; }

    /// <summary>The words for a step whose page has no title, its number to fill in (<c>data-jfb-untitled</c>).</summary>
    public string? UntitledWords { get; init; }

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
public sealed record ProgressStepModel(string Title, bool IsCurrent, bool IsDone)
{
    /// <summary>The page's key.</summary>
    public Guid Page { get; init; }

    /// <summary>Whether the rules hide the page, so that its step waits in a template (<c>data-jfb-steps</c>) for them to show it.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Whether the page has no title, so that the step's words hold its number.</summary>
    public bool IsUntitled { get; init; }

    /// <summary>
    /// Whether the page's title holds merge tags, so the step carries its hook and the runtime renders it again as the
    /// visitor answers (ADR-0115).
    /// </summary>
    public bool RecallsTitle { get; init; }

    /// <summary>What the step shows while the page has no title, such as "Page 2".</summary>
    public string Fallback { get; init; } = string.Empty;
}

/// <summary>The model of <c>ErrorSummary.cshtml</c>: the errors after a post, each with a link to its field (ADR-0015).</summary>
public sealed class ErrorSummaryViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

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

    /// <summary>The form is for members, and the visitor is not signed in.</summary>
    SignIn,

    /// <summary>The form is for members of groups the signed-in member is in none of.</summary>
    MemberGroups,

    /// <summary>
    /// The note a signed-in backoffice user sees in a form's place while the migrations are pending (ADR-0033): it stands
    /// alone, without the form's element around it.
    /// </summary>
    MigrationsPending,
}

/// <summary>The model of <c>Notice.cshtml</c>: a notice in place of the form or above it (ADR-0015).</summary>
public sealed class NoticeViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>Why the notice shows.</summary>
    public required NoticeKind Kind { get; init; }

    /// <summary>The notice's heading, or null when its text has its own.</summary>
    public string? Heading { get; init; }

    /// <summary>The notice's text as HTML: the package's sentence in a paragraph, or the form's own message, sanitised.</summary>
    public required IHtmlContent Text { get; init; }

    /// <summary>
    /// For a notice above the form after a post, what happened (<c>data-jfb-notice</c>): <c>form_changed</c> or
    /// <c>not_sent</c> (ADR-0100); such a notice is an alert, as the error summary is. Null for any other notice.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>The id of the heading of a notice after a post, which names the alert; null for any other notice.</summary>
    public string? HeadingId { get; init; }

    /// <summary>The notice's classes: <c>jfb-notice</c>, with <c>jfb-notice-error</c> for a send that did not go through.</summary>
    public string CssClass { get; init; } = "jfb-notice";
}

/// <summary>The model of <c>Outcome.cshtml</c>: the message after the visitor sent the form (ADR-0015).</summary>
public sealed class OutcomeViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>The views the outcome writes its parts with: the copy of the answers takes <c>Summary.cshtml</c>.</summary>
    public required FormViewPaths Views { get; init; }

    /// <summary>The message, sanitised.</summary>
    public required IHtmlContent Message { get; init; }

    /// <summary>The line with the entry's reference, the reference in <c>strong</c>; null for an entry without one.</summary>
    public IHtmlContent? Reference { get; init; }

    /// <summary>Whether the visitor must still confirm their email address (<c>data-jfb-pending</c>).</summary>
    public bool IsPending { get; init; }

    /// <summary>
    /// The link to the outcome's redirect after its message, which a page without JavaScript follows when the visitor
    /// chooses (ADR-0100); null for a message without a redirect.
    /// </summary>
    public OutcomeLinkModel? Continue { get; init; }

    /// <summary>
    /// What the runtime counts down to the redirect with, in the outcome it shows after a send (ADR-0120); null in the
    /// outcome after a post, which waits for the visitor.
    /// </summary>
    public OutcomeCountdownModel? Countdown { get; init; }

    /// <summary>The copy of the answers below the message, for a message that offers it (ADR-0123); null otherwise.</summary>
    public OutcomeCopyModel? Copy { get; init; }
}

/// <summary>The line that says when the page moves on, and the button that stops it (ADR-0022, WCAG 2.2.1).</summary>
/// <param name="One">The line with one second left, {0} the second (<c>data-jfb-one</c>).</param>
/// <param name="Other">The line with more seconds left, {0} the seconds (<c>data-jfb-other</c>).</param>
/// <param name="Stay">The button's text, "Stay here".</param>
public sealed record OutcomeCountdownModel(string One, string Other, string Stay);

/// <summary>What the runtime shows a send with (ADR-0120): the form view writes it into its templates, which stay inert.</summary>
public sealed class FormSendingModel
{
    /// <summary>Every button of a form of several pages, of which the runtime shows those of the page it moves to (ADR-0155); null for a form of one.</summary>
    public NavigationViewModel? Moves { get; init; }

    /// <summary>What the live region says while a send is on its way (<c>data-jfb-sending</c>).</summary>
    public required string SendingText { get; init; }

    /// <summary>The message of an entry whose form has none of its own (<c>data-jfb-thanks</c>).</summary>
    public required string ThanksText { get; init; }

    /// <summary>The notices just above the buttons, one per code of a send that did not go through (ADR-0022).</summary>
    public required IReadOnlyList<NoticeViewModel> Notices { get; init; }

    /// <summary>The outcome without its message, which the runtime fills in and puts in the form's place.</summary>
    public required OutcomeViewModel Outcome { get; init; }
}

/// <summary>
/// The copy of the answers below an outcome (ADR-0123): the entry's answers, shown once, as the summary page writes them
/// without "Change", or on a later view a line in their place; then the print button and the link to the form again.
/// </summary>
public sealed class OutcomeCopyModel
{
    /// <summary>The answers, a section per page; null on a later view, which writes <see cref="ShownText"/> instead.</summary>
    public SummaryViewModel? Answers { get; init; }

    /// <summary>The print button's text, "Print your answers"; the runtime reveals the button.</summary>
    public required string PrintText { get; init; }

    /// <summary>The line in the answers' place on a later view: they show only once.</summary>
    public required string ShownText { get; init; }

    /// <summary>The link to the form again: the page without the sent token, at the form.</summary>
    public required OutcomeLinkModel Again { get; init; }
}

/// <summary>A link of the outcome: where it goes and its text.</summary>
/// <param name="Url">The address, checked by the publish rule.</param>
/// <param name="Text">The link's text.</param>
public sealed record OutcomeLinkModel(string Url, string Text);
