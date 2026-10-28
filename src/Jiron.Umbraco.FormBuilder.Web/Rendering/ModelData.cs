using System.Text;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The model data of a form (ADR-0093): the form endpoint's answer, inside <c>&lt;script type="application/json"
/// data-jfb-model&gt;</c>, with every <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, U+2028 and U+2029 written as a <c>\u</c>
/// escape. JSON holds these only inside strings, where the escape reads back as the same character, so no text can end
/// the element and the data parsed equals the answer.
/// </summary>
internal static class ModelData
{
    /// <summary>The answer as the text of the script element.</summary>
    /// <param name="json">The answer as UTF-8 JSON.</param>
    public static IHtmlContent Of(ReadOnlySpan<byte> json)
    {
        var text = new StringBuilder(json.Length + 32);
        foreach (var character in Encoding.UTF8.GetString(json))
        {
            _ = character switch
            {
                '<' => text.Append("\\u003c"),
                '>' => text.Append("\\u003e"),
                '&' => text.Append("\\u0026"),
                (char)0x2028 => text.Append("\\u2028"),
                (char)0x2029 => text.Append("\\u2029"),
                _ => text.Append(character),
            };
        }

        return new HtmlString(text.ToString());
    }
}
