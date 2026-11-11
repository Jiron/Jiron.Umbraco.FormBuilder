using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Renders a published form as HTML from a site's own code (section 4.9, ADR-0233), where no view invokes
/// <c>&lt;jfb-render&gt;</c>: a controller that answers a fragment, a block's own renderer, a page built in code. It is
/// the same rendering, with the same markup, assets and post back to the page, as the tag helper's.
/// </summary>
public interface IFormBuilderRenderer
{
    /// <summary>The form's HTML for the request; null for a reference that names no published form, as a page renders nothing then.</summary>
    /// <param name="httpContext">The request the form renders for, whose page it posts back to.</param>
    /// <param name="request">The form and how it renders.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    Task<IHtmlContent?> RenderAsync(HttpContext httpContext, FormBuilderRenderRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A form to render, as <c>&lt;jfb-render&gt;</c> names it.</summary>
/// <param name="Form">The form: its key, its alias in any letter case, or the <see cref="FormBuilderFormReference"/> the form picker gives.</param>
public sealed record FormBuilderRenderRequest(object Form)
{
    /// <summary>
    /// Whether the HTML starts with the tags that load the runtime and the theme's stylesheet, once per request; null for the
    /// host's <see cref="FormBuilderRenderingOptions.IncludeAssetsAutomatically"/>. A fragment for a page that has them
    /// already passes false.
    /// </summary>
    public bool? Assets { get; init; }

    /// <summary>Context values for the entry, by name, as <c>context-*</c> attributes give them.</summary>
    public IReadOnlyDictionary<string, string?>? Context { get; init; }

    /// <summary>The alias of the theme the form renders with here, in place of the form's own and the site's default.</summary>
    public string? Theme { get; init; }
}

/// <summary>The renderer: the view component's path, written to a string with the request's culture.</summary>
/// <param name="rendering">What a page renders for a form.</param>
/// <param name="engine">Finds the form's view.</param>
/// <param name="tempData">The views' temp data.</param>
/// <param name="assets">The page's assets.</param>
/// <param name="options">Whether assets come along by default.</param>
internal sealed class FormBuilderRenderer(
    FormRendering rendering,
    IRazorViewEngine engine,
    ITempDataProvider tempData,
    PageAssets assets,
    IOptions<FormBuilderOptions> options) : IFormBuilderRenderer
{
    /// <inheritdoc />
    public async Task<IHtmlContent?> RenderAsync(HttpContext httpContext, FormBuilderRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);
        if (await rendering.RenderAsync(httpContext, request.Form, request.Context, request.Theme, cancellationToken) is not { } rendered)
        {
            return null;
        }

        var found = engine.GetView(null, rendered.View, isMainPage: false);
        if (!found.Success)
        {
            throw new InvalidOperationException($"The view '{rendered.View}' cannot be found.");
        }

        var action = new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor());
        var markup = await RazorViewText.WriteAsync(found.View, action, rendered.Model, tempData);
        if (rendered.Theme is not { } theme)
        {
            return new HtmlString(markup);
        }

        PageAssets.Use(httpContext, theme);
        var builder = new HtmlContentBuilder();
        if (request.Assets ?? options.Value.Rendering.IncludeAssetsAutomatically)
        {
            builder.AppendHtml(assets.Automatic(httpContext, theme));
        }

        return builder.AppendHtml(markup);
    }
}
