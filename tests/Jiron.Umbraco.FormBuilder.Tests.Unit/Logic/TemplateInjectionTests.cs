using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Hostile values through every filter, with each culture of merge-tags.json (ROADMAP 5.3b, ADR-0064): wherever a tag may
/// stand, they add no element, attribute, header line, address part or JSON token; wherever a tag may not stand, the
/// template renders as it is written. The same in TypeScript (<c>packages/logic/test/templates.test.ts</c>).
/// </summary>
public sealed class TemplateInjectionTests
{
    private const string Secret = "SECRET-7f3a";

    private static readonly string Bs = new((char)92, 1);
    private static readonly string Quote = new((char)34, 1);
    private static readonly string Cr = new((char)13, 1);
    private static readonly string Lf = new((char)10, 1);
    private static readonly string Tab = new((char)9, 1);

    /// <summary>Values meant to break out of their place: markup, quotes, addresses, line breaks, JSON, odd code units and tags.</summary>
    private static readonly MergeTagValue[] Hostile =
    [
        V("<script>alert(1)</script>"),
        V(Quote + "><img src=x onerror=alert(1)>"),
        V("' onmouseover='alert(1)"),
        V("</p><p>"),
        V("&lt;b&gt; & &amp;"),
        V("<![CDATA[x]]>"),
        V("<!-- x -->"),
        V("]]>"),
        V("javascript:alert(1)"),
        V("//evil.test/x"),
        V("https://evil.test"),
        V("../../admin"),
        V("?a=1&b=2#f"),
        V("%2F%2e%00"),
        V(Bs + Bs + "evil.test"),
        V("x" + Cr + Lf + "Set-Cookie: a=b"),
        V("x" + Lf + "Bcc: a@evil.test"),
        V("x" + Cr + "y"),
        V("x" + (char)0x2028 + "y" + (char)0x2029),
        V("x" + (char)0x85 + "y"),
        V(Tab + "x" + (char)0x7F),
        V(Quote + "}, " + Q("admin") + ": true, " + Q("x") + ": {" + Quote),
        V(Bs + Quote),
        V(string.Empty + (char)0 + (char)0x1F),
        V("</script><script>alert(1)</script>"),
        V((char)0xD800 + "x" + (char)0xDC00),
        V(string.Empty + (char)0xFFFE + (char)0xFFFF),
        V("{{ field.secret }}"),
        V("}}{{"),
        V(string.Empty),
        MergeTagValue.ListOf([]),
        MergeTagValue.ListOf([new MergeTagItem("a" + Quote + "b"), new MergeTagItem("<c>"), new MergeTagItem("d" + Cr + Lf + "e")]),
        MergeTagValue.ListOf([new MergeTagItem("1</p>", Number: "12"), new MergeTagItem("<x>", Number: "1e5")]),
        MergeTagValue.Of(new MergeTagItem("<b>", Date: "2026-01-01")),
        MergeTagValue.Of(new MergeTagItem(Quote, Number: "-0.5")),
    ];

    /// <summary>Every filter, alone and in the chains an editor might write, with arguments that are hostile too.</summary>
    private static readonly string[] Chains =
    [
        string.Empty,
        " | upper",
        " | lower",
        " | truncate: 3",
        " | truncate: 2, " + Q("<" + Bs + Quote + "&"),
        " | default: " + Q(Bs + Quote + "><x>"),
        " | join",
        " | join: " + Q("</p>"),
        " | date",
        " | date: " + Q("long"),
        " | number: 2",
        " | currency: " + Q("EUR"),
        " | html",
        " | url",
        " | json",
        " | upper | html",
        " | join: " + Q("&") + " | url",
        " | truncate: 4 | json",
        " | default: " + Q(Bs + Quote + "}") + " | json",
    ];

    /// <summary>The characters that start or end markup or an attribute value.</summary>
    private static readonly char[] Markup = ['<', '>', '"', (char)39, '&'];

    /// <summary>Where a tag may stand, in every context whose output has a structure.</summary>
    private static readonly Place[] Allowed =
    [
        Between(MergeTagContext.Html, "<p>", "</p>", IsHtmlText),
        Between(MergeTagContext.Html, "<div class=" + Q("a") + ">Hi ", "!</div>", IsHtmlText),
        Between(MergeTagContext.Html, "<script>x</script>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<!-- c -->", "<br>", IsHtmlText),
        Between(MergeTagContext.Html, string.Empty, string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<a" + Quote + "b>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<p a=x" + Quote + "y>", "</p>", IsHtmlText),
        Between(MergeTagContext.Html, "<p a=" + Q("x>y") + ">", "</p>", IsHtmlText),
        Between(MergeTagContext.Html, "<script" + Quote + "x>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<!-->", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<!-- a --!>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "</ a=" + Quote + "b>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<svg><title>Logo</title></svg>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<noscript>x</noscript>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Html, "<script></script<</script>", string.Empty, IsHtmlText),
        Between(MergeTagContext.Xml, "<a>", "</a>", IsXmlText),
        Between(MergeTagContext.Xml, "<![CDATA[x]]>", string.Empty, IsXmlText),
        Between(MergeTagContext.Xml, "<!DOCTYPE a SYSTEM " + Q("b>c") + ">", string.Empty, IsXmlText),
        Between(MergeTagContext.Header, "Re: ", string.Empty, IsHeaderLine),
        Between(MergeTagContext.Header, string.Empty, " (form)", IsHeaderLine),
        Between(MergeTagContext.Url, "https://example.test/a?q=", "&x=1", IsUrlComponent),
        Between(MergeTagContext.Url, "/path/", string.Empty, IsUrlComponent),
        Between(MergeTagContext.Url, "?q=", string.Empty, IsUrlComponent),
        Between(MergeTagContext.Url, "#", string.Empty, IsUrlComponent),
        Between(MergeTagContext.Url, "https://example.test/", "/", IsUrlComponent),
        Between(MergeTagContext.Form, "a=1&b=", string.Empty, IsFormValue),
        Between(MergeTagContext.JsonString, "Hello ", "!", IsJsonStringContent),
        Around(MergeTagContext.Json, "{" + Q("a") + ": " + Quote, Quote + "}", IsJsonObject(["a"], inString: true)),
        Around(MergeTagContext.Json, "{" + Q("b") + ": ", "}", IsJsonObject(["b"], inString: false)),
        Around(MergeTagContext.Json, "{" + Q("a") + ": " + Q("x" + Bs + Quote + "y") + ", " + Q("b") + ": ", "}", IsJsonObject(["a", "b"], inString: false)),
        Around(MergeTagContext.Json, "{" + Q("a") + ": " + Q("x") + ", " + Q("b") + ": " + Quote, Bs + "n" + Quote + "}", IsJsonObject(["a", "b"], inString: true)),
    ];

    /// <summary>Where a tag may not stand: in markup, before an address's path, and after a backslash in a JSON string.</summary>
    private static readonly (MergeTagContext Context, string Before, string After, string Code)[] Refused =
    [
        (MergeTagContext.Html, "<a href=" + Quote, Quote + ">x</a>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<a href=", ">x</a>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<img ", ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<p", ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "</", ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<p title='", "'>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<script>", "</script>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<script>if (a</b) ", "</script>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<STYLE>", "</style>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<textarea>", "</textarea>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<title>", "</title>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<!-- ", " -->", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<!DOCTYPE ", ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<?php ", " ?>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<plaintext></plaintext>", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<a x" + Quote + " onclick=" + Quote + "if(a>b)", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<a b" + Quote + "c=" + Quote + "d>", Quote + " f>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<p a='x>", "'>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<a title = " + Quote + "x>", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<script/>", "</script>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "{{ field.v | truncate: " + Q("<script>") + " }}", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<p>{{ field.v | upper | html | x: 1 }}<style>", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<!--><a onclick=" + Quote + "-->", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<!-- --!><a onclick=" + Quote + "-->", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "</ a=" + Quote + "x><a onclick=" + Quote + "//>", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<script><!--<script></script>", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<svg><style><a onclick=" + Quote + "</style>", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<svg><foreignObject><script></svg>", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<svg><![CDATA[ <a b=" + Quote + " ]]><a onclick=" + Quote + "//>", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<noscript><a href=" + Quote + "</noscript>", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Html, "<select><style><option onclick=" + Quote + "</style>", Quote + ">", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<a b=" + Quote, Quote + "/>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<", "/>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<![CDATA[", "]]>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<?pi ", "?>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<!-- ", " -->", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<_a b=" + Quote, Quote + "/>", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<!DOCTYPE a [<!ENTITY b " + Q("c") + ">]>", string.Empty, MergeTagProblems.TagInMarkup),
        (MergeTagContext.Xml, "<!-->", "-->", MergeTagProblems.TagInMarkup),
        (MergeTagContext.Url, string.Empty, string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, " ", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "https://", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "https://", "/x", MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "https://user@", "/", MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "http", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "java", ":alert(1)", MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "javascript:", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "javascript:/", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "mailto:", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "data:", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "//", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, Bs + Bs, string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "HTTPS://", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "https:" + Tab + "//", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Url, "https:" + Lf + "//", string.Empty, MergeTagProblems.TagBeforePath),
        (MergeTagContext.Json, "{" + Q("a") + ": " + Quote + Bs, Quote + "}", MergeTagProblems.TagInEscape),
        (MergeTagContext.Json, "[" + Quote + Bs, Quote + "]", MergeTagProblems.TagInEscape),
        (MergeTagContext.JsonString, "x" + Bs, string.Empty, MergeTagProblems.TagInEscape),
    ];

    [Test]
    public void HostileValues_WhereATagMayStand_AddNoElementAttributeHeaderLineAddressPartOrJsonToken()
    {
        var failures = new List<string>();
        var checkedValues = 0;
        var renders = 0;
        foreach (var culture in Fuzzing.TemplateCultures)
        {
            foreach (var place in Allowed)
            {
                foreach (var chain in Chains)
                {
                    var text = place.Template("{{ field.v" + chain + " }}");
                    var parsed = MergeTagEngine.Parse(text, place.Context);
                    foreach (var value in Hostile)
                    {
                        var rendered = MergeTagEngine.Render(parsed, Sources(value), culture);
                        renders++;

                        // A tag with an error renders as it is written, which is the editor's own text.
                        var asWritten = rendered.Problems.Any(problem => !MergeTagProblems.IsWarning(problem.Code));
                        var safe = asWritten ? rendered.Text == text : place.Safe(rendered.Text);
                        if (!asWritten)
                        {
                            checkedValues++;
                        }

                        if (!safe || rendered.Text.Contains(Secret, StringComparison.Ordinal))
                        {
                            failures.Add($"{place.Context}: {Fuzzing.Show(text)} with {Fuzzing.Show(Describe(value))} gave {Fuzzing.Show(rendered.Text)}");
                        }
                    }
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(failures.Take(20), Is.Empty);

            // Most values reach the output, so the checks are not empty.
            Assert.That(checkedValues, Is.GreaterThan(renders / 2));
        });
    }

    [Test]
    public void HostileValues_WhereATagMayNotStand_LeaveTheTemplateAsItIsWritten()
    {
        var failures = new List<string>();
        foreach (var (context, before, after, code) in Refused)
        {
            foreach (var chain in new[] { string.Empty, " | url", " | json", " | html" })
            {
                var text = before + "{{ field.v" + chain + " }}" + after;
                var parsed = MergeTagEngine.Parse(text, context);
                foreach (var value in Hostile)
                {
                    foreach (var culture in Fuzzing.TemplateCultures)
                    {
                        var rendered = MergeTagEngine.Render(parsed, Sources(value), culture);
                        if (rendered.Text != text || !rendered.Problems.Any(problem => problem.Code == code))
                        {
                            failures.Add($"{context}: {Fuzzing.Show(text)} with {Fuzzing.Show(Describe(value))} gave {Fuzzing.Show(rendered.Text)}");
                        }
                    }
                }
            }
        }

        Assert.That(failures.Take(20), Is.Empty);
    }

    /// <summary>
    /// No vector can hold a lone surrogate, since .NET's JSON reader refuses one, so this test and its TypeScript twin
    /// cover them: wherever the output must be well formed, each becomes U+FFFD.
    /// </summary>
    [Test]
    public void LoneSurrogates_WhereTheOutputMustBeWellFormed_BecomeTheReplacementCharacter()
    {
        var lone = "a" + (char)0xD800 + "b" + (char)0xDC00;
        var r = new string((char)0xFFFD, 1);
        const string Bytes = "%EF%BF%BD";
        (MergeTagContext Context, string Template, string Expected)[] cases =
        [
            (MergeTagContext.Xml, "{{ field.v }}", "a" + r + "b" + r),
            (MergeTagContext.Url, "/a/{{ field.v }}", "/a/a" + Bytes + "b" + Bytes),
            (MergeTagContext.Form, "{{ field.v }}", "a" + Bytes + "b" + Bytes),
            (MergeTagContext.JsonString, "{{ field.v }}", "a" + r + "b" + r),
            (MergeTagContext.Json, "{{ field.v }}", Q("a" + r + "b" + r)),
            (MergeTagContext.Text, "{{ field.v | url }}", "a" + Bytes + "b" + Bytes),
            (MergeTagContext.Text, "{{ field.v | json }}", Q("a" + r + "b" + r)),
        ];
        var culture = Fuzzing.TemplateCultures[0];
        Assert.Multiple(() =>
        {
            foreach (var (context, template, expected) in cases)
            {
                var rendered = MergeTagEngine.Render(MergeTagEngine.Parse(template, context), Sources(V(lone)), culture);
                Assert.That(rendered.Text, Is.EqualTo(expected), $"{context}: {template}");
            }
        });
    }

    private static string Q(string inner) => Quote + inner + Quote;

    private static MergeTagValue V(string text) => MergeTagValue.Of(text);

    private static Func<string, MergeTagValue?> Sources(MergeTagValue value) =>
        path => path switch
        {
            "field.v" => value,
            "field.secret" => MergeTagValue.Of(Secret),
            _ => null,
        };

    private static string Describe(MergeTagValue value) =>
        (value.IsList ? "list " : string.Empty) + string.Join(" | ", value.Items.Select(item => $"{item.Text} ({item.Number}, {item.Date})"));

    /// <summary>One tag between two texts, whose piece of the output must pass the check.</summary>
    private static Place Between(MergeTagContext context, string before, string after, Func<string, bool> check) => new(
        context,
        tag => before + tag + after,
        output => output.Length >= before.Length + after.Length
            && output.StartsWith(before, StringComparison.Ordinal)
            && output.EndsWith(after, StringComparison.Ordinal)
            && check(output[before.Length..^after.Length]));

    /// <summary>One tag between two texts, whose whole output must pass the check.</summary>
    private static Place Around(MergeTagContext context, string before, string after, Func<string, bool> check) =>
        new(context, tag => before + tag + after, check);

    private static bool IsControl(char unit) => unit < 0x20 || unit is >= (char)0x7F and <= (char)0x9F;

    /// <summary>Text content with no markup but the line breaks the <c>html</c> filter writes, and no references but these.</summary>
    private static bool IsMarkupText(string piece, string[] references, bool breaks)
    {
        for (var i = 0; i < piece.Length;)
        {
            var reference = references.FirstOrDefault(name => string.CompareOrdinal(piece, i, name, 0, name.Length) == 0);
            if (reference is not null)
            {
                i += reference.Length;
            }
            else if (breaks && string.CompareOrdinal(piece, i, "<br>", 0, 4) == 0)
            {
                i += 4;
            }
            else if (Markup.Contains(piece[i]))
            {
                return false;
            }
            else
            {
                i++;
            }
        }

        return true;
    }

    private static bool IsHtmlText(string piece) => IsMarkupText(piece, ["&amp;", "&lt;", "&gt;", "&quot;", "&#39;"], breaks: true);

    /// <summary>XML text content, with every code unit one XML 1.0 can carry.</summary>
    private static bool IsXmlText(string piece)
    {
        for (var i = 0; i < piece.Length; i++)
        {
            var unit = piece[i];
            if (char.IsHighSurrogate(unit) && i + 1 < piece.Length && char.IsLowSurrogate(piece[i + 1]))
            {
                i++;
            }
            else if (!(unit is (char)9 or (char)10 or (char)13 || (unit is >= (char)0x20 and <= (char)0xFFFD && !char.IsSurrogate(unit))))
            {
                return false;
            }
        }

        return IsMarkupText(piece, ["&amp;", "&lt;", "&gt;", "&quot;", "&apos;"], breaks: false);
    }

    /// <summary>One header line: no line break and no control character.</summary>
    private static bool IsHeaderLine(string output) => !output.Any(unit => IsControl(unit) || unit is (char)0x2028 or (char)0x2029);

    /// <summary>Percent-encoded: only the characters kept, and <c>%</c> with two capital hex digits.</summary>
    private static bool IsPercentEncoded(string piece, string kept)
    {
        const string Hex = "0123456789ABCDEF";
        for (var i = 0; i < piece.Length; i++)
        {
            var c = piece[i];
            if (c == '%')
            {
                if (i + 2 >= piece.Length || !Hex.Contains(piece[i + 1], StringComparison.Ordinal) || !Hex.Contains(piece[i + 2], StringComparison.Ordinal))
                {
                    return false;
                }

                i += 2;
            }
            else if (!char.IsAsciiLetterOrDigit(c) && !kept.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUrlComponent(string piece) => IsPercentEncoded(piece, "-._~");

    private static bool IsFormValue(string piece) => IsPercentEncoded(piece, "*-._+");

    /// <summary>No character a script or markup around the JSON could read: <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, U+2028 and U+2029 are escaped.</summary>
    private static bool IsScriptSafe(string output) => output.IndexOfAny(['<', '>', '&', (char)0x2028, (char)0x2029]) < 0;

    private static bool IsJsonStringContent(string piece)
    {
        try
        {
            using var document = JsonDocument.Parse(Quote + piece + Quote);
            return document.RootElement.ValueKind == JsonValueKind.String && IsScriptSafe(piece);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsScalar(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number;

    /// <summary>
    /// A JSON object with exactly these keys, the last holding what a tag writes: a string inside a string literal,
    /// outside one a string, a number or an array of them.
    /// </summary>
    private static Func<string, bool> IsJsonObject(string[] keys, bool inString) => output =>
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(property => property.Name).SequenceEqual(keys))
            {
                return false;
            }

            var value = root.GetProperty(keys[^1]);
            var written = inString
                ? value.ValueKind == JsonValueKind.String
                : IsScalar(value) || (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(IsScalar));
            return written && IsScriptSafe(output);
        }
        catch (JsonException)
        {
            return false;
        }
    };

    private sealed record Place(MergeTagContext Context, Func<string, string> Template, Func<string, bool> Safe);
}
