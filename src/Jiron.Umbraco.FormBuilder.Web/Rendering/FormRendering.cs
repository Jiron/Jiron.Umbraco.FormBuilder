using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
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
/// <param name="IsForm">Whether it is the form, which brings the form's assets; the note brings none.</param>
internal sealed record RenderedForm(string View, object Model, bool IsForm);

/// <summary>
/// What a page renders for a form it names (ADR-0004, ADR-0093, ADR-0095): the form's view and its model, with its
/// notice for a visitor a form for members does not admit, or nothing for a reference that names no published form
/// (missing, unpublished or trashed, logged once per reference and process) and for a value of another type (logged once
/// per type), so a page never breaks for its form. After a post without JavaScript sent the form, the page the post
/// redirects to shows the outcome its <c>jfb-sent</c> token names, for that form and instance only (ADR-0100), as the
/// receipt endpoint answers it, and is <c>no-store</c>. While the migrations are pending (ADR-0033) it renders nothing for
/// visitors and a short note for a signed-in backoffice user. The page and the culture come from Umbraco's published
/// request, held to the site's languages as the public API holds them (ADR-0084); outside an Umbraco page there is no
/// page, and the culture is the request's.
/// </summary>
/// <param name="forms">The published forms.</param>
/// <param name="requests">What the form endpoint answers, which the model data is.</param>
/// <param name="pages">The culture and the page.</param>
/// <param name="migrations">Whether the migrations are pending.</param>
/// <param name="umbraco">Umbraco's request, with its published content and culture.</param>
/// <param name="models">The view models.</param>
/// <param name="views">The views, of which the note while the migrations are pending takes the default notice.</param>
/// <param name="backOffice">Whether the request comes from a signed-in backoffice user, who sees that note.</param>
/// <param name="submissions">The submission pipeline, whose receipt the page after a sent post shows.</param>
/// <param name="sent">The tokens of the redirect after a sent post.</param>
/// <param name="logger">Where a reference that names no form is logged.</param>
internal sealed partial class FormRendering(
    PublishedFormCache forms,
    FormRequests requests,
    FormPages pages,
    IFormBuilderMigrationState migrations,
    IUmbracoContextAccessor umbraco,
    FormModels models,
    FormViews views,
    BackOfficeVisitors backOffice,
    FormSubmissions submissions,
    SentTokens sent,
    ILogger<FormRendering> logger)
{
    // The references that named no form in this process, so each is logged once.
    private readonly ConcurrentDictionary<string, bool> _missing = new(StringComparer.Ordinal);

    /// <summary>The form's view and its model, the note while the migrations are pending, or null when nothing renders.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="form">A key (a <see cref="Guid"/> or its text with hyphens), an alias, or a <see cref="FormBuilderFormReference"/>.</param>
    /// <param name="context">The context values the page gives, by name, or null.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<RenderedForm?> RenderAsync(HttpContext httpContext, object? form, IReadOnlyDictionary<string, string?>? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var reference = form switch
        {
            null => null,
            Guid key => key.ToString("D"),
            FormBuilderFormReference picked => picked.Key.ToString("D"),
            string text => text.Trim(),
            _ => Unsupported(form.GetType()),
        };
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        if (await migrations.IsPendingAsync(cancellationToken))
        {
            return await PendingAsync(httpContext, cancellationToken);
        }

        if (await requests.KeyOfAsync(reference, cancellationToken) is not { } formKey || await forms.GetAsync(formKey, cancellationToken) is not { } cached)
        {
            if (_missing.TryAdd(reference, true))
            {
                LogNoForm(logger, reference);
            }

            return null;
        }

        var (content, requested) = Request();
        var culture = await CultureAsync(requested);
        var shown = await requests.ShowAsync(cached, culture, await pages.PageAsync(content), cancellationToken);
        var availability = cached.Published.Definition.Settings.Availability;
        var gate = availability.Access == MemberAccess.Anyone ? null : await httpContext.RequestServices.GetRequiredService<FormMembers>().GateAsync(availability);
        var instance = FormInstances.Next(httpContext);
        await SentAsync(httpContext, formKey, instance, culture, cancellationToken);
        var (view, model) = await models.BuildAsync(new FormRender(httpContext, cached, shown, culture, instance, context ?? new Dictionary<string, string?>(), gate), cancellationToken);
        return new RenderedForm(view, model, IsForm: true);
    }

    // The outcome of the entry a post without JavaScript sent, on the page it redirected to (ADR-0100): the token in
    // jfb-sent names the form, the instance and the submission id, and the outcome is the receipt's; a token that names
    // nothing for this form and instance leaves the form as it is. A page that shows an outcome is no-store.
    private async Task SentAsync(HttpContext httpContext, Guid form, string instance, string culture, CancellationToken cancellationToken)
    {
        if (FormPosts.Get(httpContext, form, instance) is not null
            || !httpContext.Request.Query.TryGetValue(SentTokens.QueryName, out var values)
            || values.Count != 1
            || sent.Read(values[0], form, instance) is not { } submissionId
            || await submissions.ReceiptAsync(form, submissionId, cancellationToken) is not { Receipt: { } receipt, Outcome: { } outcome })
        {
            return;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        var words = RenderingWords.Of(culture);
        FormPosts.Set(httpContext, form, instance, new FormPostState
        {
            Outcome = new FormPostOutcome(
                outcome.Html ?? "<p>" + WebUtility.HtmlEncode(words.ThankYou) + "</p>",
                receipt.Reference,
                IsPending: false,
                outcome is { Kind: OutcomeKind.Message, Url: { } url } ? url : null),
        });
    }

    // The note in a form's place while the migrations are pending, for a signed-in backoffice user only: the page's
    // visitors see nothing there (ADR-0033). It is the default notice, in the page's culture.
    private async Task<RenderedForm?> PendingAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!backOffice.IsSignedIn(httpContext))
        {
            return null;
        }

        var words = RenderingWords.Of(await CultureAsync(Request().Culture));
        var note = new NoticeViewModel
        {
            Kind = NoticeKind.MigrationsPending,
            Heading = words.PendingHeading,
            Text = new HtmlString("<p>" + WebUtility.HtmlEncode(words.PendingText) + "</p>"),
        };
        return new RenderedForm(await views.DefaultAsync("Notice", cancellationToken), note, IsForm: false);
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
