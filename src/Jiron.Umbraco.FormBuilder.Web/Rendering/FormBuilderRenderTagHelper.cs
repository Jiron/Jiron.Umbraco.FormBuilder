using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// <c>&lt;jfb-render form="contact" /&gt;</c> renders a published form through <see cref="FormBuilderFormViewComponent"/>
/// (ADR-0004, ADR-0093), with the tags that load the website runtime once per page unless <c>assets="false"</c> (or the
/// host's <c>Rendering:IncludeAssetsAutomatically</c> is off and the tag does not say <c>assets="true"</c>),
/// <c>theme="..."</c> for a theme in place of the form's (ADR-0105), and <c>context-&lt;name&gt;="..."</c> for the
/// context values the form lists. A form a picker picks is written <c>picked="..."</c>, which keeps what the picker's
/// settings give it, its theme and its page after a send (ADR-0129). The element itself is not written. Add
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

    /// <summary>
    /// The form: its key with hyphens or its alias in any letter case. A <see cref="FormBuilderFormReference"/> written as
    /// <c>form="@..."</c> names the form by its key alone; <see cref="Picked"/> keeps the picker's settings.
    /// </summary>
    [HtmlAttributeName("form")]
    public string? Form { get; set; }

    /// <summary>
    /// The form a picker picks, with the theme and the page after a send of the picker's settings (ADR-0129), written as
    /// code: <c>picked="Model.Value&lt;FormBuilderFormReference&gt;(&quot;form&quot;)"</c>. It comes before <see cref="Form"/>.
    /// </summary>
    [HtmlAttributeName("picked")]
    public FormBuilderFormReference? Picked { get; set; }

    /// <summary>
    /// Whether the form includes the tags that load the website runtime (once per page); when the tag does not say, the
    /// host's <c>Rendering:IncludeAssetsAutomatically</c> decides, which is true unless the site turns it off.
    /// </summary>
    [HtmlAttributeName("assets")]
    public bool? Assets { get; set; }

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
        output.Content.SetHtmlContent(await components.InvokeAsync(FormBuilderFormViewComponent.Name, new { form = (object?)Picked ?? Form, assets = Assets, context = (IReadOnlyDictionary<string, string?>)values, theme = Theme }));
    }
}
