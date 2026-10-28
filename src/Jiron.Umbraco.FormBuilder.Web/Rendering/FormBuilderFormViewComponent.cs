using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Renders a published form on a page (ADR-0004, ADR-0093):
/// <c>@await Component.InvokeAsync("FormBuilderForm", new { form = "contact" })</c>, the one path
/// <c>&lt;jfb-render&gt;</c> takes too. The form's instance ids, its markup and its model data are the same for every
/// visitor of the page. A reference that names no published form renders nothing, so a page never breaks for its form.
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
    /// Whether the form includes the tags that load the website runtime; they are written once per page, and a
    /// <c>&lt;jfb-assets&gt;</c> anywhere on the page writes them in its place (ADR-0093).
    /// </param>
    /// <param name="context">
    /// Context values for the entry, by name: those the form lists (its developer settings) are carried in the form, the
    /// same for every visitor of the page; any other name, and a value longer than 2,000 characters, is left out and logged.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(object? form, bool assets = true, IReadOnlyDictionary<string, string?>? context = null)
    {
        var rendered = await HttpContext.RequestServices.GetRequiredService<FormRendering>().RenderAsync(HttpContext, form, context, HttpContext.RequestAborted);
        if (rendered is not { } found)
        {
            return Content(string.Empty);
        }

        var view = View(found.View, found.Model);
        return assets ? new AssetsFirst(HttpContext.RequestServices.GetRequiredService<PageAssets>().Automatic(HttpContext), view) : view;
    }

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
