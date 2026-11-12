using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Renders a published form on a page (ADR-0004, ADR-0093):
/// <c>@await Component.InvokeAsync("FormBuilderForm", new { form = "contact" })</c>, the one path
/// <c>&lt;jfb-render&gt;</c> takes too. The form's instance ids, its markup and its model data are the same for every
/// visitor of the page. A reference that names no published form renders nothing, and a form that fails while it renders
/// renders nothing for a visitor and a short note for a signed-in backoffice user (ADR-0237), so a page and its other forms
/// never break for one form: the form is written to the page only once its view has rendered in full.
/// </summary>
[ViewComponent(Name = Name)]
public sealed class FormBuilderFormViewComponent : ViewComponent
{
    /// <summary>The name pages invoke the component by.</summary>
    public const string Name = "FormBuilderForm";

    /// <summary>Renders the form.</summary>
    /// <param name="form">
    /// The form: its key (a <see cref="Guid"/>, or its text with hyphens), its alias in any letter case, or the
    /// <see cref="FormBuilderFormReference"/> the form picker gives.
    /// </param>
    /// <param name="assets">
    /// Whether the form includes the tags that load the website runtime, the base rules and its theme's stylesheet, or
    /// none for the host's <see cref="FormBuilderRenderingOptions.IncludeAssetsAutomatically"/>, which is true unless the
    /// site turns it off. Each is written once per page, and a <c>&lt;jfb-assets&gt;</c> anywhere on the page writes them
    /// in its place, for a form without its own tags too (ADR-0093, ADR-0105).
    /// </param>
    /// <param name="context">
    /// Context values for the entry, by name: those the form lists (its developer settings) are carried in the form, the
    /// same for every visitor of the page; any other name, and a value longer than 2,000 characters, is left out and logged.
    /// </param>
    /// <param name="theme">
    /// The alias of the theme the form renders with here, in place of the form's own and the site's default (ADR-0105);
    /// a theme the site lacks renders as <c>default</c>.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(object? form, bool? assets = null, IReadOnlyDictionary<string, string?>? context = null, string? theme = null)
    {
        if (FormRenderTargets.Get(HttpContext) is not null)
        {
            // The render endpoint renders its one form, and answers a failure as its own (ADR-0237).
            return await ResultAsync(form, assets, context, theme);
        }

        try
        {
            return new HtmlContentViewComponentResult(await HtmlAsync(form, assets, context, theme));
        }
        catch (Exception exception) when (FormRenderFailures.Caught(exception, HttpContext.RequestAborted))
        {
            var failures = HttpContext.RequestServices.GetRequiredService<FormRenderFailures>();
            return new HtmlContentViewComponentResult(failures.InPlace(HttpContext, form, exception) ?? HtmlString.Empty);
        }
    }

    // The form as the render endpoint answers it: its view runs as the response is written.
    private async Task<IViewComponentResult> ResultAsync(object? form, bool? assets, IReadOnlyDictionary<string, string?>? context, string? theme)
    {
        var rendered = await HttpContext.RequestServices.GetRequiredService<FormRendering>().RenderAsync(HttpContext, form, context, theme, HttpContext.RequestAborted);
        if (rendered is not { } found)
        {
            return Content(string.Empty);
        }

        var view = View(found.View, found.Model);
        if (found.Theme is not { } formTheme)
        {
            return view;
        }

        PageAssets.Use(HttpContext, formTheme);
        return Included(assets) ? new AssetsFirst(HttpContext.RequestServices.GetRequiredService<PageAssets>().Automatic(HttpContext, formTheme), view) : view;
    }

    // The form on a page: its view rendered in full, then its assets, which decide when the page is written whether they are
    // still to write, before it.
    private async Task<IHtmlContent> HtmlAsync(object? form, bool? assets, IReadOnlyDictionary<string, string?>? context, string? theme)
    {
        var rendered = await HttpContext.RequestServices.GetRequiredService<FormRendering>().RenderAsync(HttpContext, form, context, theme, HttpContext.RequestAborted);
        if (rendered is not { } found)
        {
            return HtmlString.Empty;
        }

        if (found.Theme is { } formTheme)
        {
            PageAssets.Use(HttpContext, formTheme);
        }

        var markup = await WriteAsync(found.View, found.Model);
        if (found.Theme is not { } used || !Included(assets))
        {
            return new HtmlString(markup);
        }

        return new HtmlContentBuilder()
            .AppendHtml(HttpContext.RequestServices.GetRequiredService<PageAssets>().Automatic(HttpContext, used))
            .AppendHtml(markup);
    }

    // The view as a view component's result renders it, into text of its own, so nothing of a view that fails reaches the page.
    private async Task<string> WriteAsync(string path, object model)
    {
        var found = ViewEngine.GetView(ViewContext.ExecutingFilePath, path, isMainPage: false);
        if (!found.Success)
        {
            found = ViewEngine.FindView(ViewContext, path, isMainPage: false);
        }

        if (!found.Success)
        {
            throw new InvalidOperationException($"The view '{path}' cannot be found.");
        }

        await using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var data = new ViewDataDictionary<object>(ViewData, model);
        await found.View.RenderAsync(new ViewContext(ViewContext, found.View, data, writer));
        return writer.ToString();
    }

    private bool Included(bool? assets) =>
        assets ?? HttpContext.RequestServices.GetRequiredService<IOptions<FormBuilderOptions>>().Value.Rendering.IncludeAssetsAutomatically;

    /// <summary>
    /// The form's assets, which decide when the page is written whether they are still to write, then the form.
    /// </summary>
    /// <param name="assets">The automatic assets of the request.</param>
    /// <param name="form">The form's view.</param>
    internal sealed class AssetsFirst(IHtmlContent assets, IViewComponentResult form) : IViewComponentResult
    {
        /// <inheritdoc />
        public void Execute(ViewComponentContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Writer.Write(assets);
            form.Execute(context);
        }

        /// <inheritdoc />
        public Task ExecuteAsync(ViewComponentContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Writer.Write(assets);
            return form.ExecuteAsync(context);
        }
    }
}
