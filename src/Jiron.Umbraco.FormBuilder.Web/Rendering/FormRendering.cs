using System.Collections.Concurrent;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// What a page renders for a form it names (ADR-0004, ADR-0093): the form's view and its model, or nothing for a
/// reference that names no published form (missing, unpublished or trashed, logged once per reference and process), for
/// a value of another type (logged once per type), and while the migrations are pending (ADR-0033), so a page never
/// breaks for its form. The page and the culture come from Umbraco's published request, held to the site's languages
/// as the public API holds them (ADR-0084); outside an Umbraco page there is no page, and the culture is the request's.
/// </summary>
/// <param name="forms">The published forms.</param>
/// <param name="requests">What the form endpoint answers, which the model data is.</param>
/// <param name="pages">The culture and the page.</param>
/// <param name="migrations">Whether the migrations are pending.</param>
/// <param name="umbraco">Umbraco's request, with its published content and culture.</param>
/// <param name="models">The view models.</param>
/// <param name="logger">Where a reference that names no form is logged.</param>
internal sealed partial class FormRendering(
    PublishedFormCache forms,
    FormRequests requests,
    FormPages pages,
    IFormBuilderMigrationState migrations,
    IUmbracoContextAccessor umbraco,
    FormModels models,
    ILogger<FormRendering> logger)
{
    // The references that named no form in this process, so each is logged once.
    private readonly ConcurrentDictionary<string, bool> _missing = new(StringComparer.Ordinal);

    /// <summary>The form's view and its model, or null when nothing renders.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="form">A key (a <see cref="Guid"/> or its text with hyphens), an alias, or a <see cref="FormBuilderFormReference"/>.</param>
    /// <param name="context">The context values the page gives, by name, or null.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<(string View, FormViewModel Model)?> RenderAsync(HttpContext httpContext, object? form, IReadOnlyDictionary<string, string?>? context, CancellationToken cancellationToken)
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
        if (string.IsNullOrEmpty(reference) || await migrations.IsPendingAsync(cancellationToken))
        {
            return null;
        }

        if (await requests.KeyOfAsync(reference, cancellationToken) is not { } formKey || await forms.GetAsync(formKey, cancellationToken) is not { } cached)
        {
            if (_missing.TryAdd(reference, true))
            {
                LogNoForm(logger, reference);
            }

            return null;
        }

        var (content, requested) = umbraco.TryGetUmbracoContext(out var current) && current.PublishedRequest is { } published
            ? (published.PublishedContent?.Key, published.Culture)
            : (null, CultureInfo.CurrentUICulture.Name);
        var culture = await pages.CultureAsync(FormPages.IsCulture(requested) ? requested : null);
        var shown = await requests.ShowAsync(cached, culture, await pages.PageAsync(content), cancellationToken);
        var instance = FormInstances.Next(httpContext);
        return await models.BuildAsync(new FormRender(httpContext, cached, shown, culture, instance, context ?? new Dictionary<string, string?>()), cancellationToken);
    }

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
