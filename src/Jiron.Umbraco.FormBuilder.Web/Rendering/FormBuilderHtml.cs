using System.Net;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
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

    /// <summary>
    /// A field's label's mark after a space (ADR-0093): "(optional)", or the asterisk hidden from assistive technology;
    /// on a form with rules also a mark that does not show now, <c>hidden</c>, with <c>data-jfb-mark</c> naming the
    /// field's state it shows in (ADR-0111); nothing for none.
    /// </summary>
    /// <param name="field">The field whose label it marks.</param>
    public static IHtmlContent Mark(FieldViewModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Mark(field.Mark, field.MarkText, field.MarkShownWhile, field.IsMarkShown);
    }

    /// <summary>A part's label's mark after a space, as <see cref="Mark(FieldViewModel)"/> writes a field's.</summary>
    /// <param name="part">The part of a name or an address whose label it marks.</param>
    public static IHtmlContent Mark(PartModel part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return Mark(part.Mark, part.MarkText, part.MarkShownWhile, part.IsMarkShown);
    }

    /// <summary>A grid's row's statement's mark after a space, as <see cref="Mark(FieldViewModel)"/> writes a field's.</summary>
    /// <param name="row">The row whose statement it marks.</param>
    public static IHtmlContent Mark(GridRowModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Mark(row.Mark, row.MarkText, row.MarkShownWhile, row.IsMarkShown);
    }

    private static IHtmlContent Mark(FieldMark mark, string text, string? shownWhile, bool shown)
    {
        if (mark == FieldMark.None)
        {
            return HtmlString.Empty;
        }

        var start = " <span class=\"jfb-mark\""
            + (mark == FieldMark.Required ? " aria-hidden=\"true\"" : string.Empty)
            + (shownWhile is null ? string.Empty : " data-jfb-mark=\"" + WebUtility.HtmlEncode(shownWhile) + "\"")
            + (shown ? string.Empty : " hidden")
            + ">";
        return new HtmlContentBuilder().AppendHtml(start).Append(text).AppendHtml("</span>");
    }
}
