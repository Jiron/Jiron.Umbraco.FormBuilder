using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

/// <summary>What a form shows where it stands on a page (ADR-0093).</summary>
public enum FormDisplay
{
    /// <summary>The form element with its pages.</summary>
    Form,

    /// <summary>A notice in place of the form, such as "This form is closed".</summary>
    Notice,

    /// <summary>The outcome after the visitor sent the form.</summary>
    Outcome,
}

/// <summary>
/// The model of <c>Form.cshtml</c>, the view a form starts from (ADR-0015, ADR-0093): everything the markup contract
/// needs, resolved, so the view only writes it. Its parts carry their own models, and <see cref="Views"/> says which
/// view writes each part.
/// </summary>
public sealed class FormViewModel
{
    /// <summary>The classes the views write, Jiron Form Builder's own with the form's theme's (ADR-0105).</summary>
    public ThemeClassesModel Classes { get; init; } = ThemeClassesModel.None;

    /// <summary>The version of the markup contract the built-in views write.</summary>
    public const int ContractVersion = 1;

    /// <summary>The form's key (<c>data-jfb-form</c>).</summary>
    public required Guid Key { get; init; }

    /// <summary>The form's alias.</summary>
    public required string Alias { get; init; }

    /// <summary>The form's public revision (<c>data-jfb-revision</c>).</summary>
    public required int Revision { get; init; }

    /// <summary>The instance id, <c>jfb-1</c> for the first form a request renders, <c>jfb-2</c> for the next.</summary>
    public required string Id { get; init; }

    /// <summary>The culture the form is shown in (<c>lang</c>).</summary>
    public required string Culture { get; init; }

    /// <summary>The writing direction of the culture (<c>dir</c>): <c>ltr</c> or <c>rtl</c>.</summary>
    public required string Direction { get; init; }

    /// <summary>The classes of the form's root: <c>jfb-form</c>, the theme's and the form's own.</summary>
    public required string CssClass { get; init; }

    /// <summary>What the form shows.</summary>
    public required FormDisplay Display { get; init; }

    /// <summary>The address the form posts to: the page's path below the path base, then the instance as the fragment.</summary>
    public required string Action { get; init; }

    /// <summary>The form's <c>enctype</c>: <c>multipart/form-data</c> for a form with a file field, otherwise null.</summary>
    public string? EncodingType { get; init; }

    /// <summary>The public API's base below the path base (<c>data-jfb-api</c>).</summary>
    public required string ApiBase { get; init; }

    /// <summary>The model data: the form endpoint's answer as JSON, written so no text in it can end its element.</summary>
    public required IHtmlContent ModelData { get; init; }

    /// <summary>The pages, in order; empty unless the form shows itself.</summary>
    public IReadOnlyList<PageViewModel> Pages { get; init; } = [];

    /// <summary>The progress through the pages; null for a form with one page shown, or one that shows no progress.</summary>
    public ProgressViewModel? Progress { get; init; }

    /// <summary>The errors after a post, with a link to each field; null for none, or when the form shows no summary.</summary>
    public ErrorSummaryViewModel? ErrorSummary { get; init; }

    /// <summary>
    /// The error summary the runtime fills (ADR-0113): one link with no text and no target and an empty heading, which
    /// the form view renders through the summary's own view into its templates; null unless the form shows itself.
    /// </summary>
    public ErrorSummaryViewModel? ErrorSummaryTemplate { get; init; }

    /// <summary>
    /// A copy of the current page's Next or Send button at the form's start, hidden from sight and from screen readers,
    /// for a form with submit buttons before its navigation (a ranking's moves), so Enter in a field means Next or Send
    /// (ADR-0096); null for any other form.
    /// </summary>
    public NavigationButtonModel? DefaultButton { get; init; }

    /// <summary>The buttons below the current page; null unless the form shows itself.</summary>
    public NavigationViewModel? Navigation { get; init; }

    /// <summary>
    /// The sentence above the first page that says what the asterisk means; null unless required fields are marked, or
    /// on a form with rules unless a label carries an asterisk the rules may show.
    /// </summary>
    public IHtmlContent? RequiredNote { get; init; }

    /// <summary>Whether the note is written <c>hidden</c>: a form with rules carries it while no asterisk shows now.</summary>
    public bool IsRequiredNoteHidden { get; init; }

    /// <summary>
    /// Whether the form has rules, so the views write the hooks the runtime applies them by (ADR-0111), the note's
    /// <c>data-jfb-required-note</c> among them.
    /// </summary>
    public bool Hooks { get; init; }

    /// <summary>A notice in place of the form, or above it (such as "the form has changed"); null for none.</summary>
    public NoticeViewModel? Notice { get; init; }

    /// <summary>The outcome after the visitor sent the form; null before.</summary>
    public OutcomeViewModel? Outcome { get; init; }

    /// <summary>The context values the page gives that the form lists, as the hidden inputs that carry them.</summary>
    public IReadOnlyList<HiddenInputModel> ContextValues { get; init; } = [];

    /// <summary>
    /// The hidden inputs a post without JavaScript carries (ADR-0100): Umbraco's route token (<c>ufprt</c>), the form's
    /// key, its instance, its revision and the page shown, and after a post the query values it carried; none is per
    /// visitor on a first render.
    /// </summary>
    public IReadOnlyList<HiddenInputModel> PostValues { get; init; } = [];

    /// <summary>
    /// The honeypot (ADR-0012), in a <c>jfb-hp</c> container that the functional base rules keep out of view and
    /// <c>aria-hidden</c> keeps out of the accessibility tree (ADR-0015, ADR-0105); null where there is no form to fill in.
    /// </summary>
    public HoneypotModel? Honeypot { get; init; }

    /// <summary>The views that write the form's parts.</summary>
    public required FormViewPaths Views { get; init; }
}

/// <summary>
/// The view each part of a form is written by (ADR-0015): the form's own override when it has one, otherwise the
/// built-in view. A view writes a part through <c>Html.PartialAsync</c> with the path given here.
/// </summary>
public sealed class FormViewPaths
{
    /// <summary><c>Page.cshtml</c>.</summary>
    public required string Page { get; init; }

    /// <summary><c>Group.cshtml</c>.</summary>
    public required string Group { get; init; }

    /// <summary><c>Field.cshtml</c>.</summary>
    public required string Field { get; init; }

    /// <summary><c>Navigation.cshtml</c>.</summary>
    public required string Navigation { get; init; }

    /// <summary><c>Progress.cshtml</c>.</summary>
    public required string Progress { get; init; }

    /// <summary><c>ErrorSummary.cshtml</c>.</summary>
    public required string ErrorSummary { get; init; }

    /// <summary><c>Outcome.cshtml</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary><c>Notice.cshtml</c>.</summary>
    public required string Notice { get; init; }

    /// <summary><c>Summary.cshtml</c>, the answers the summary page shows (ADR-0122).</summary>
    public required string Summary { get; init; }
}

/// <summary>The honeypot of a form (ADR-0012): a text input no visitor sees, with its label.</summary>
/// <param name="Id">Its id, the instance's followed by its name.</param>
/// <param name="Name">Its name, which the form's key and public revision give.</param>
/// <param name="Label">Its label, which asks to leave it empty.</param>
public sealed record HoneypotModel(string Id, string Name, string Label);

/// <summary>A hidden input the form carries.</summary>
/// <param name="Name">The input's name.</param>
/// <param name="Value">The input's value.</param>
public sealed record HiddenInputModel(string Name, string Value);
