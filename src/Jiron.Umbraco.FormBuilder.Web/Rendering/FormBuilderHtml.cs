using System.Net;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Attributes the form views write inside a start tag, such as <c>&lt;div @FormBuilderHtml.Flag("data-jfb-hidden",
/// Model.IsHidden)&gt;</c> (ADR-0093). Razor leaves out an attribute whose value is null or false, except a
/// <c>data-*</c> attribute, which it always writes; the markup contract's <c>data-jfb-*</c> flags are there or not.
/// </summary>
public static class FormBuilderHtml
{
    /// <summary>The attribute without a value when <paramref name="on"/>, otherwise nothing.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="on">Whether the attribute is there.</param>
    public static IHtmlContent Flag(string name, bool on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return on ? new HtmlString(name) : HtmlString.Empty;
    }

    /// <summary>The attribute with its value, encoded, when there is a value, otherwise nothing.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">Its value, or null for none.</param>
    public static IHtmlContent Attribute(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return value is null ? HtmlString.Empty : new HtmlString(name + "=\"" + WebUtility.HtmlEncode(value) + "\"");
    }
}
