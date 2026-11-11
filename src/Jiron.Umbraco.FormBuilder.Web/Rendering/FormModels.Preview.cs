using System.Globalization;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>What an outcome's preview is made of (ADR-0187).</summary>
/// <param name="Outcome">The outcome as the server renders it for the sample entry.</param>
/// <param name="Shown">
/// The visitor's form the copy of the answers is listed by, given only for a message that shows the copy; null for an
/// outcome without one.
/// </param>
/// <param name="Entry">The sample entry's scope.</param>
/// <param name="Reference">The sample entry's reference in the form's format.</param>
/// <param name="Alias">The form's alias, whose own views come first.</param>
/// <param name="Culture">The culture the visitor's words are in.</param>
/// <param name="Texts">The visitor texts the form or every site rewords in the culture, by key (ADR-0230).</param>
internal sealed record OutcomePreviewSource(SubmissionOutcome Outcome, CopyShown? Shown, MergeTagScope Entry, string Reference, string Alias, string Culture, IReadOnlyDictionary<string, string> Texts);

// An outcome's preview in the backoffice (ADR-0187): the model the site's Outcome view writes it with, as the runtime
// shows it after a send, in the default theme the designer's canvas draws in.
internal sealed partial class FormModels
{
    /// <summary>The id the preview's copy of the answers names its headings below.</summary>
    public const string PreviewId = "jfb-preview";

    /// <summary>
    /// The model of an outcome's preview: the message (the site's thank-you where it renders as nothing), the reference
    /// line, "Go now" to the redirect's address, the countdown and "Stay here" when it waits, and the copy of the answers
    /// where the outcome offers it, listed as a real entry's are.
    /// </summary>
    /// <param name="source">The outcome, the form and the sample entry.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="InvalidOperationException">A part of the default theme has no view.</exception>
    public async Task<OutcomeViewModel> PreviewAsync(OutcomePreviewSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var words = RenderingWords.Of(source.Culture).With(source.Texts);
        var classes = new ThemeClassesModel(new DefaultTheme().Classes);
        var (_, paths) = await views.PartsAsync(FormViews.DefaultFolder, source.Alias, cancellationToken);
        var outcome = source.Outcome;
        OutcomeCopyModel? copy = null;
        if (source.Shown is { } shown && source.Entry.Entry is { } entry)
        {
            copy = new OutcomeCopyModel
            {
                // Nothing hidden, as the rules are not applied: every field the copy lists has its row, "Not answered" where the
                // sample has no answer, as a real entry's copy has.
                Answers = await CopiedAsync(shown, PreviewId, classes, new EntryCopy(source.Entry, Hidden: new HashSet<Guid>(), entry.Submitted), words, CultureInfo.GetCultureInfo(source.Culture), cancellationToken),
                PrintText = words.PrintAnswers,
                ShownText = words.CopyShown,
                Again = new OutcomeLinkModel("#" + PreviewId, words.FillInAgain),
            };
        }

        return new OutcomeViewModel
        {
            Classes = classes,
            Views = paths,
            Copy = copy,
            Message = new HtmlString(outcome.Html ?? "<p>" + WebUtility.HtmlEncode(words.ThankYou) + "</p>"),
            Reference = WithMarkup(words.Reference, "<strong>" + WebUtility.HtmlEncode(source.Reference) + "</strong>"),
            Continue = outcome.Url is { } url ? new OutcomeLinkModel(url, words.GoNow) : null,
            Countdown = outcome is { Url: not null, DelaySeconds: > 0 } ? new OutcomeCountdownModel(words.CountdownOne, words.CountdownOther, words.StayHere) : null,
        };
    }
}
