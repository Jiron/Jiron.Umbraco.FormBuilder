using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>What a page renders for a form: a view and its model.</summary>
/// <param name="View">The view's path.</param>
/// <param name="Model">Its model: a <see cref="FormViewModel"/>, or for the note while the migrations are pending a <see cref="NoticeViewModel"/>.</param>
/// <param name="Theme">The form's theme, whose stylesheet the page loads with the form's assets; null for the note, which brings none.</param>
internal sealed record RenderedForm(string View, object Model, IFormBuilderTheme? Theme);

/// <summary>
/// What a page renders for a form it names (ADR-0004, ADR-0093, ADR-0095): the form's view and its model, with its
/// notice for a visitor a form for members does not admit, or nothing for a reference that names no published form
/// (missing, unpublished or trashed, logged once per reference and process) and for a value of another type (logged once
/// per type), so a page never breaks for its form. After a post without JavaScript sent the form, the page the post
/// redirects to shows the outcome its <c>jfb-sent</c> token names, for that form and instance only (ADR-0100), as the
/// receipt endpoint answers it. While the migrations are pending (ADR-0033) it renders nothing for visitors and a short
/// note for a signed-in backoffice user. The page and the culture come from Umbraco's published request, held to the
/// site's languages as the public API holds them (ADR-0084); outside an Umbraco page there is no page, and the culture is
/// the request's. The render endpoint gives the page, the culture and the instance id in their place (ADR-0104). A render
/// that is per visitor keeps its page out of every cache (ADR-0004, ADR-0104): one on a request other than <c>GET</c> or
/// <c>HEAD</c> or that carries <c>jfb-sent</c>, a form for members for anyone, and while the migrations are pending any
/// form for anyone. The form's theme is the placement's, else the form's own, else the site's default, else
/// <c>default</c> (ADR-0105). The preview renders the version its target names, as though it were published, for a test
/// member it admits, with no member and no outcome of a send (ADR-0203).
/// </summary>
/// <param name="forms">The published forms.</param>
/// <param name="previews">The versions the preview renders.</param>
/// <param name="requests">What the form endpoint answers, which the model data is.</param>
/// <param name="pages">The culture and the page.</param>
/// <param name="migrations">Whether the migrations are pending.</param>
/// <param name="umbraco">Umbraco's request, with its published content and culture.</param>
/// <param name="models">The view models.</param>
/// <param name="views">The views, of which the note while the migrations are pending takes the default notice.</param>
/// <param name="backOffice">Whether the request comes from a signed-in backoffice user, who sees that note.</param>
/// <param name="submissions">The submission pipeline, whose receipt the page after a sent post shows.</param>
/// <param name="sent">The tokens of the redirect after a sent post.</param>
/// <param name="copies">The marks that let the copy of the answers show once (ADR-0123).</param>
/// <param name="themes">The themes, which give the form's.</param>
/// <param name="logger">Where a reference that names no form is logged.</param>
internal sealed partial class FormRendering(
    PublishedFormCache forms,
    PreviewForms previews,
    FormRequests requests,
    FormPages pages,
    IFormBuilderMigrationState migrations,
    IUmbracoContextAccessor umbraco,
    FormModels models,
    FormViews views,
    BackOfficeVisitors backOffice,
    FormSubmissions submissions,
    SentTokens sent,
    SentCopies copies,
    FormThemes themes,
    ILogger<FormRendering> logger)
{
    // The references that named no form in this process, so each is logged once.
    private readonly ConcurrentDictionary<string, bool> _missing = new(StringComparer.Ordinal);

    /// <summary>The form's view and its model, the note while the migrations are pending, or null when nothing renders.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="form">A key (a <see cref="Guid"/> or its text with hyphens), an alias, or a <see cref="FormBuilderFormReference"/>.</param>
    /// <param name="context">The context values the page gives, by name, or null.</param>
    /// <param name="theme">The alias of the theme the placement names, or null for the form's own.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<RenderedForm?> RenderAsync(HttpContext httpContext, object? form, IReadOnlyDictionary<string, string?>? context, string? theme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var picked = form as FormBuilderFormReference;
        var reference = form switch
        {
            null => null,
            Guid key => key.ToString("D"),
            FormBuilderFormReference placed => placed.Key.ToString("D"),
            string text => text.Trim(),
            _ => Unsupported(form.GetType()),
        };
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        // A post to the page, or the page after a sent post, is the visitor's own.
        var request = httpContext.Request;
        if ((!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) || request.Query.ContainsKey(SentTokens.QueryName))
        {
            PageCaching.NoStore(httpContext);
        }

        var target = FormRenderTargets.Get(httpContext);
        var (content, requested) = target is null ? Request() : (target.Content, target.Culture);
        if (await migrations.IsPendingAsync(cancellationToken))
        {
            // A visitor sees nothing and a backoffice user the note, so neither page may answer for the other.
            PageCaching.NoStore(httpContext);
            return await PendingAsync(httpContext, requested, cancellationToken);
        }

        var preview = target?.Preview;
        if (await requests.KeyOfAsync(reference, cancellationToken) is not { } formKey
            || await (preview is { } version ? previews.GetAsync(formKey, version, cancellationToken) : forms.GetAsync(formKey, cancellationToken)) is not { } cached)
        {
            if (_missing.TryAdd(reference, true))
            {
                LogNoForm(logger, reference);
            }

            return null;
        }

        var culture = await CultureAsync(requested);
        var shown = await requests.ShowAsync(cached, culture, await pages.PageAsync(content), cancellationToken);
        var availability = cached.Published.Definition.Settings.Availability;
        MemberGate? gate = null;
        string? challenge = null;
        var alreadySent = false;
        if (availability.Access != MemberAccess.Anyone && preview is null)
        {
            // Who is signed in decides what shows (ADR-0095), and no other origin may frame the member's page (ADR-0231).
            PageCaching.NoStore(httpContext);
            PageFraming.OwnOriginOnly(httpContext);
            var members = httpContext.RequestServices.GetRequiredService<FormMembers>();
            gate = await members.GateAsync(availability);

            // A member who has sent a form that takes one entry per member sees that in its place; any other member's page
            // carries a challenge issued to them, which a post without JavaScript sends (ADR-0012, ADR-0231).
            if (gate is null && await members.KeyAsync() is { } admitted)
            {
                alreadySent = availability.OnePerMember && await submissions.HasSentAsync(formKey, admitted, cancellationToken);
                challenge = alreadySent ? null : httpContext.RequestServices.GetRequiredService<ChallengeTokens>().Issue(formKey, admitted);
            }
        }

        var instance = target?.Instance ?? FormInstances.Next(httpContext);
        if (preview is null)
        {
            await SentAsync(httpContext, formKey, instance, cancellationToken);
        }

        // A form that takes values from the member renders them for the member signed in, so their page is theirs (ADR-0124).
        Guid? member = null;
        if (preview is null
            && FormModels.TakesFromMember(cached.Published.Definition)
            && await httpContext.RequestServices.GetRequiredService<FormMembers>().KeyAsync() is { } signedIn)
        {
            PageCaching.NoStore(httpContext);
            PageFraming.OwnOriginOnly(httpContext);
            member = signedIn;
        }

        // The picker's theme and page after a send are the placement's (ADR-0129); the render endpoint names its own theme.
        var formTheme = await themes.ResolveAsync(target is null ? theme ?? picked?.Theme : target.Theme, shown.Form.Settings.Theme, cancellationToken);
        var (view, model) = await models.BuildAsync(new FormRender(httpContext, cached, shown, culture, instance, context ?? new Dictionary<string, string?>(), formTheme, gate, target?.Page, member, target is null ? picked?.Redirect : target.Redirect, content, Preview: preview is not null) { Challenge = challenge, AlreadySent = alreadySent }, cancellationToken);
        return new RenderedForm(view, model, formTheme);
    }

    // The outcome of the entry a post without JavaScript sent, on the page it redirected to (ADR-0100), or that a send on
    // the page asks the render endpoint for, for the copy of its answers (ADR-0155): the token in jfb-sent names the form, the instance and the submission id, and the outcome is the receipt's; a token that names
    // nothing for this form and instance leaves the form as it is. The request carries jfb-sent, so the page is no-store.
    // A message that offers the copy of the answers shows them to the one GET that takes the entry's mark, and a line
    // in their place to any later view (ADR-0123).
    private async Task SentAsync(HttpContext httpContext, Guid form, string instance, CancellationToken cancellationToken)
    {
        if (FormPosts.Get(httpContext, form, instance) is not null
            || !httpContext.Request.Query.TryGetValue(SentTokens.QueryName, out var values)
            || values.Count != 1
            || sent.Read(values[0], form, instance) is not { } submissionId
            || await submissions.ReceiptAsync(form, submissionId, await MemberAsync(httpContext), cancellationToken) is not { Receipt: { } receipt, Outcome: { } outcome })
        {
            return;
        }

        FormPostCopy? copy = null;
        if (outcome is { Kind: OutcomeKind.Message, ShowAnswers: true })
        {
            var entry = HttpMethods.IsGet(httpContext.Request.Method) ? await submissions.CopyAsync(form, submissionId, await MemberAsync(httpContext), cancellationToken) : null;
            copy = new FormPostCopy(entry is not null && await copies.TakeAsync(form, submissionId, entry.Submitted, cancellationToken) ? entry : null);
        }

        FormPosts.Set(httpContext, form, instance, new FormPostState
        {
            Outcome = new FormPostOutcome(
                outcome.Html,
                receipt.Reference,
                IsPending: false,
                outcome is { Kind: OutcomeKind.Message, Url: { } url } ? url : null,
                copy),
        });
    }

    // The member signed in on the request, whose own entry alone a form for members shows the outcome of (ADR-0231).
    private static Task<Guid?> MemberAsync(HttpContext httpContext) => httpContext.RequestServices.GetRequiredService<FormMembers>().KeyAsync();

    // The note in a form's place while the migrations are pending, for a signed-in backoffice user only: the page's
    // visitors see nothing there (ADR-0033). It is the default notice, in the page's culture.
    private async Task<RenderedForm?> PendingAsync(HttpContext httpContext, string? requested, CancellationToken cancellationToken)
    {
        if (!backOffice.IsSignedIn(httpContext))
        {
            return null;
        }

        var words = RenderingWords.Of(await CultureAsync(requested));
        var note = new NoticeViewModel
        {
            Kind = NoticeKind.MigrationsPending,
            Heading = words.PendingHeading,
            Text = new HtmlString("<p>" + WebUtility.HtmlEncode(words.PendingText) + "</p>"),
        };
        return new RenderedForm(await views.DefaultAsync("Notice", cancellationToken), note, Theme: null);
    }

    // The page's content and culture from Umbraco's published request; outside an Umbraco page, none and the request's.
    private (Guid? Content, string? Culture) Request() =>
        umbraco.TryGetUmbracoContext(out var current) && current.PublishedRequest is { } published
            ? (published.PublishedContent?.Key, published.Culture)
            : (null, CultureInfo.CurrentUICulture.Name);

    private Task<string> CultureAsync(string? requested) => pages.CultureAsync(FormPages.IsCulture(requested) ? requested : null);

    // A value of another type names no form; each type is logged once.
    private string? Unsupported(Type type)
    {
        var name = type.FullName ?? type.Name;
        if (_missing.TryAdd("type:" + name, true))
        {
            LogUnsupported(logger, name);
        }

        return null;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "A page names the form '{Reference}', which is no published form; the page renders without it.")]
    private static partial void LogNoForm(ILogger logger, string reference);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "A page names a form by a value of type {Type}, which names no form; give the form's key, its alias or a FormBuilderFormReference.")]
    private static partial void LogUnsupported(ILogger logger, string type);
}
