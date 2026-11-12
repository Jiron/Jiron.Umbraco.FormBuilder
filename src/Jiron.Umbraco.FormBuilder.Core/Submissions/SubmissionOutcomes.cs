using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// Renders the outcome a submission's rules chose (ADR-0088) for its entry: a message as consent wordings render (the
/// text in the culture, a dictionary item through the dictionary, its merge tags in the HTML context, then the one
/// sanitiser); a URL redirect with its merge tags in the URL context, checked again by the publish rule (ADR-0035); a
/// content redirect at the content's address in the culture. A redirect that cannot be followed is left out, and the
/// site logs a warning that names the form and the outcome, never an answer.
/// </summary>
internal sealed partial class SubmissionOutcomes(
    IFormBuilderMergeTagService mergeTags,
    MergeTagDictionary dictionary,
    MergeTagUmbraco umbraco,
    SiteBaseUrl baseUrls,
    EditorHtml html,
    ILogger<SubmissionOutcomes> logger)
{
    // The origin a content's address is built on when the site has no base URL: only its path is kept then.
    private const string NoOrigin = "https://jfb.invalid";

    /// <summary>
    /// The outcome with this key of the scope's form, rendered for the scope's entry; <see cref="SubmissionOutcome.Default"/>
    /// for none or a key the form lacks, and for a redirect outcome whose redirect cannot be followed.
    /// </summary>
    /// <param name="outcome">The key of the outcome the rules chose, as <see cref="FormState.Outcome"/> gives it, or null.</param>
    /// <param name="scope">The form (the version the entry was submitted with), the culture, the site, the page, the entry and its answers.</param>
    /// <param name="placement">
    /// The page the form's placement sends a visitor to after a send (ADR-0129), which comes in place of the outcome when
    /// it has an address; null for the outcome.
    /// </param>
    /// <param name="cancellationToken">Cancels the merge-tag sources' work.</param>
    public async Task<SubmissionOutcome> RenderAsync(string? outcome, MergeTagScope scope, Guid? placement = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (placement is { } page)
        {
            if (await RedirectAsync(new OutcomeRedirect { Content = page }, scope, cancellationToken) is { } address)
            {
                return new SubmissionOutcome { Kind = OutcomeKind.Redirect, Url = address };
            }

            LogPlacementNotFollowed(logger, scope.FormAlias);
        }

        if (outcome is null || !Guid.TryParseExact(outcome, "D", out var key) || scope.Form.Outcomes.FirstOrDefault(item => item.Key == key) is not { } chosen)
        {
            return SubmissionOutcome.Default;
        }

        var url = chosen.Redirect is { } redirect ? await RedirectAsync(redirect, scope, cancellationToken) : null;
        if (chosen.Redirect is not null && url is null)
        {
            LogRedirectNotFollowed(logger, scope.FormAlias, chosen.Key);
        }

        if (chosen.Kind == OutcomeKind.Redirect)
        {
            return url is null ? SubmissionOutcome.Default : new SubmissionOutcome { Kind = OutcomeKind.Redirect, Url = url };
        }

        return new SubmissionOutcome
        {
            Kind = OutcomeKind.Message,
            Html = await MessageAsync(chosen.Message, scope, cancellationToken),
            Url = url,
            DelaySeconds = url is null ? 0 : chosen.Redirect!.DelaySeconds,
            ShowAnswers = chosen.ShowAnswers,
        };
    }

    // The message in the scope's culture, or null for one that renders as nothing.
    private async Task<string?> MessageAsync(LocalizedHtml? message, MergeTagScope scope, CancellationToken cancellationToken)
    {
        if (message is null)
        {
            return null;
        }

        var written = message.Resolve(scope.Culture);
        if (LocalizedString.TryGetDictionaryKey(written, out var item) && await dictionary.TextAsync(item, scope.Culture) is { Length: > 0 } text)
        {
            written = text;
        }

        var rendering = await mergeTags.RenderAsync(written, MergeTagPlace.Outcome, MergeTagContext.Html, scope, cancellationToken);
        var shown = StrictStringConverter.WellFormed(html.Sanitise(rendering.Text));
        return shown.Trim().Length == 0 ? null : shown;
    }

    // Where the redirect leads, or null when it cannot be followed: a template or a rendered URL that fails the publish rule,
    // or content Umbraco gives no address. The template is held to the rule again, as a version stored before it, or not
    // through publishing, could hold a merge tag in the host, which renders to an address the rule cannot tell from the
    // editor's own (16.1's review).
    private async Task<string?> RedirectAsync(OutcomeRedirect redirect, MergeTagScope scope, CancellationToken cancellationToken)
    {
        if (redirect.Content is { } content)
        {
            if (await umbraco.ContentAsync(content) is not { } page)
            {
                return null;
            }

            var baseUrl = await baseUrls.GetAsync(scope.Site, cancellationToken);
            var address = umbraco.Url(page, await umbraco.CultureAsync(scope.Culture), baseUrl ?? NoOrigin);
            return address is null || baseUrl is not null || !address.StartsWith(NoOrigin + "/", StringComparison.Ordinal) ? address : address[NoOrigin.Length..];
        }

        if (redirect.Url is not { Length: > 0 } template || RedirectUrls.Problem(template) is not null)
        {
            return null;
        }

        var rendered = (await mergeTags.RenderAsync(template, MergeTagPlace.Redirect, MergeTagContext.Url, scope, cancellationToken)).Text;
        return RedirectUrls.Problem(rendered) is null ? rendered : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The page after a send that a placement of the form '{FormAlias}' names has no address; the form's outcome shows instead.")]
    private static partial void LogPlacementNotFollowed(ILogger logger, string formAlias);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An outcome's redirect of the form '{FormAlias}' cannot be followed and was left out (outcome {OutcomeKey}).")]
    private static partial void LogRedirectNotFollowed(ILogger logger, string formAlias, Guid outcomeKey);
}
