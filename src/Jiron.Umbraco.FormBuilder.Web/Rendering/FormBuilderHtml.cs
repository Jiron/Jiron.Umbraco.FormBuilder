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

    /// <summary>
    /// The hook of a page's visitor text that holds merge tags (ADR-0115): <c>data-jfb-text</c> with the page's key
    /// and the text's name, such as <c>title</c>, so the runtime renders it again as the visitor answers; nothing for a
    /// text without tags. The element it marks holds the text and nothing else, and is written <c>hidden</c> while the
    /// text is empty.
    /// </summary>
    /// <param name="page">The page whose text it is.</param>
    /// <param name="name">The text's name: <c>title</c> or <c>description</c>.</param>
    public static IHtmlContent Text(PageViewModel page, string name)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Hook(page.Key, name, page.Recalling);
    }

    /// <summary>
    /// The hook of a group's visitor text that holds merge tags, as <see cref="Text(PageViewModel, string)"/> writes a
    /// page's.
    /// </summary>
    /// <param name="group">The group whose text it is.</param>
    /// <param name="name">The text's name: <c>title</c> or <c>description</c>.</param>
    public static IHtmlContent Text(GroupViewModel group, string name)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Hook(group.Key, name, group.Recalling);
    }

    /// <summary>
    /// The hook of a field's visitor text that holds merge tags, as <see cref="Text(PageViewModel, string)"/> writes a
    /// page's; a placeholder's goes on its control, whose attribute the runtime writes.
    /// </summary>
    /// <param name="field">The field whose text it is.</param>
    /// <param name="name">
    /// The text's name: <c>help</c>, <c>placeholder</c>, or <c>settings.</c> and a text setting's path, such as
    /// <c>settings.text</c>; a label takes <see cref="Label(FieldViewModel)"/>.
    /// </param>
    public static IHtmlContent Text(FieldViewModel field, string name)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Hook(field.Key, name, field.Recalling);
    }

    /// <summary>
    /// The hook of a progress step whose page's title holds merge tags, with <c>data-jfb-fallback</c>, what the step
    /// shows while the title renders empty; nothing for a title without tags.
    /// </summary>
    /// <param name="step">The step.</param>
    public static IHtmlContent Text(ProgressStepModel step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.RecallsTitle
            ? new HtmlString(HookOf(step.Page, "title") + " data-jfb-fallback=\"" + WebUtility.HtmlEncode(step.Fallback) + "\"")
            : HtmlString.Empty;
    }

    /// <summary>
    /// A field's label, encoded: for a label that holds merge tags in a <c>&lt;span&gt;</c> of its own with its hook,
    /// <c>hidden</c> while empty, so the mark after it stays as the runtime renders the label again (ADR-0115).
    /// </summary>
    /// <param name="field">The field whose label it is.</param>
    public static IHtmlContent Label(FieldViewModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return LabelText(field.Key, field.Label, field.Recalling.Contains("label"));
    }

    // A field's label, encoded, in its own span with its hook when it holds merge tags; a box's label is one too.
    internal static IHtmlContent LabelText(Guid owner, string? text, bool recalls)
    {
        var label = new HtmlContentBuilder();
        if (!recalls)
        {
            return label.Append(text ?? string.Empty);
        }

        return label.AppendHtml("<span " + HookOf(owner, "label") + (string.IsNullOrEmpty(text) ? " hidden" : string.Empty) + ">")
            .Append(text ?? string.Empty)
            .AppendHtml("</span>");
    }

    private static HtmlString Hook(Guid owner, string name, IReadOnlySet<string> recalling)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(recalling);
        return recalling.Contains(name) ? new HtmlString(HookOf(owner, name)) : HtmlString.Empty;
    }

    private static string HookOf(Guid owner, string name) =>
        "data-jfb-text=\"" + WebUtility.HtmlEncode(owner.ToString("D") + " " + name) + "\"";

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
