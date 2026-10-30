using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// <c>&lt;jfb-render form="contact" /&gt;</c> renders a published form through <see cref="FormBuilderFormViewComponent"/>
/// (ADR-0004, ADR-0093), with the tags that load the website runtime once per page unless <c>assets="false"</c>,
/// <c>theme="..."</c> for a theme in place of the form's (ADR-0105), and <c>context-&lt;name&gt;="..."</c> for the
/// context values the form lists. The element itself is not written. Add
/// <c>@addTagHelper *, Jiron.Umbraco.FormBuilder.Web</c> to the site's <c>_ViewImports.cshtml</c> to use it. A value
/// arrives as the page's code gives it, and a value written in the page as it is written, as with MVC's tag helpers.
/// </summary>
/// <param name="components">The view components, which render the form.</param>
[HtmlTargetElement("jfb-render", TagStructure = TagStructure.NormalOrSelfClosing)]
public sealed class FormBuilderRenderTagHelper(IViewComponentHelper components) : TagHelper
{
    /// <summary>The view the tag is in, which Razor sets.</summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>The form: its key with hyphens, its alias in any letter case, or a <see cref="FormBuilderFormReference"/> written as <c>form="@..."</c>.</summary>
    [HtmlAttributeName("form")]
    public string? Form { get; set; }

    /// <summary>Whether the form includes the tags that load the website runtime (once per page); true by default.</summary>
    [HtmlAttributeName("assets")]
    public bool Assets { get; set; } = true;

    /// <summary>The alias of the theme the form renders with here, in place of its own and the site's; none for those.</summary>
    [HtmlAttributeName("theme")]
    public string? Theme { get; set; }

    /// <summary>The context values, from the attributes <c>context-&lt;name&gt;</c>.</summary>
    [HtmlAttributeName(DictionaryAttributePrefix = "context-")]
    public IDictionary<string, string?> Context { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ((IViewContextAware)components).Contextualize(ViewContext);
        output.TagName = null;
        var values = new Dictionary<string, string?>(Context, StringComparer.OrdinalIgnoreCase);
        output.Content.SetHtmlContent(await components.InvokeAsync(FormBuilderFormViewComponent.Name, new { form = Form, assets = Assets, context = (IReadOnlyDictionary<string, string?>)values, theme = Theme }));
    }
}
