using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// <c>&lt;jfb-assets /&gt;</c> writes the tags that load Jiron Form Builder's website runtime (and, with the themes, their
/// stylesheets) once per page, wherever the layout puts it, usually in <c>&lt;head&gt;</c> (ADR-0004, ADR-0092). A page
/// that names it twice gets the tags once, and <c>&lt;jfb-render&gt;</c> includes them by itself when no
/// <c>&lt;jfb-assets&gt;</c> came before it. The element itself is not written. Add
/// <c>@addTagHelper *, Jiron.Umbraco.FormBuilder.Web</c> to the site's <c>_ViewImports.cshtml</c> to use it.
/// </summary>
[HtmlTargetElement("jfb-assets")]
public sealed class FormBuilderAssetsTagHelper : TagHelper
{
    /// <summary>The view the tag is in, which Razor sets.</summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// The request's Content Security Policy nonce, put on every tag written; without it, the site's
    /// <see cref="IFormBuilderCspNonceProvider"/> gives it, if it registers one. Only for pages that are never cached
    /// (ADR-0004).
    /// </summary>
    [HtmlAttributeName("nonce")]
    public string? Nonce { get; set; }

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var httpContext = ViewContext.HttpContext;
        output.TagName = null;
        if (httpContext.RequestServices.GetRequiredService<PageAssets>().TagsOnce(httpContext, Nonce) is { } tags)
        {
            output.Content.SetHtmlContent(tags);
        }
        else
        {
            output.SuppressOutput();
        }
    }
}
