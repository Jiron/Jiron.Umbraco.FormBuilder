namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// The rule for an outcome's redirect URL (ADR-0002, ADR-0035): a site-relative path or an absolute <c>https</c>
/// address, whose scheme and host are always the editor's own. Merge tags may appear only after the host, and their
/// values are encoded as URI components (ADR-0014), so no visitor can steer a redirect to another site.
/// </summary>
internal static class RedirectUrls
{
    private const string TagStart = "{{";
    private const string TagEnd = "}}";
    private const string Https = "https://";
    private const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;

    /// <summary>Why the URL cannot be a redirect, or null when it can.</summary>
    public static string? Problem(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (Literal(url) is { } literal)
        {
            return literal;
        }

        var firstTag = url.IndexOf(TagStart, StringComparison.Ordinal);
        if (url.StartsWith('/'))
        {
            // "//host" and "/\host" are addresses on another site to a browser, and "/%2F" may become one once decoded.
            if (url.Length > 1 && url[1] == '/')
            {
                return "A path that starts with two slashes leads to another site.";
            }

            // An empty value there would render "/{{ a }}/x" as "//x", which leads to the host x.
            if (firstTag == 1)
            {
                return "A path starts with text of its own after the first slash, such as /thanks; merge tags come later.";
            }

            return url.StartsWith("/%2f", StringComparison.OrdinalIgnoreCase) || url.StartsWith("/%5c", StringComparison.OrdinalIgnoreCase)
                ? "A path that starts with an encoded slash may lead to another site once decoded."
                : null;
        }

        if (!url.StartsWith(Https, StringComparison.OrdinalIgnoreCase))
        {
            return firstTag == 0
                ? "A redirect starts with its path or its address; merge tags come after the host."
                : "A redirect is a path on the site, such as /thanks, or an https address.";
        }

        var hostStart = Https.Length;
        var hostEnd = url.IndexOfAny(['/', '?', '#'], hostStart);
        if (hostEnd < 0)
        {
            hostEnd = url.Length;
        }

        if (firstTag >= 0 && firstTag < hostEnd)
        {
            return "Merge tags come after the host, so the address always leads to the site the editor chose.";
        }

        return Authority(url[hostStart..hostEnd]);
    }

    // Outside merge tags: no whitespace, no control character, no backslash, and every % starts an escape.
    private static string? Literal(string url)
    {
        for (var i = 0; i < url.Length; i++)
        {
            if (string.CompareOrdinal(url, i, TagStart, 0, TagStart.Length) == 0)
            {
                var end = url.IndexOf(TagEnd, i + TagStart.Length, StringComparison.Ordinal);
                if (end < 0)
                {
                    return "A merge tag opens with {{ and closes with }}.";
                }

                i = end + TagEnd.Length - 1;
                continue;
            }

            var c = url[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return "A redirect has no spaces or control characters outside merge tags; encode a space as %20.";
            }

            if (c == '\\')
            {
                return "A redirect has no backslash: browsers read it as a slash.";
            }

            if (c == '%' && !(i + 2 < url.Length && char.IsAsciiHexDigit(url[i + 1]) && char.IsAsciiHexDigit(url[i + 2])))
            {
                return "A % in a redirect starts an escape of two hexadecimal digits, such as %20.";
            }
        }

        return null;
    }

    // A host name or IPv4 address and an optional port; no user name, password or IPv6 literal.
    private static string? Authority(string authority)
    {
        if (authority.Contains('@', StringComparison.Ordinal))
        {
            return "A redirect address names no user or password.";
        }

        var host = authority;
        var colon = authority.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            host = authority[..colon];
            var port = authority[(colon + 1)..];
            if (port.Length is < 1 or > 5 || !port.All(char.IsAsciiDigit) || port[0] == '0' || int.Parse(port, System.Globalization.CultureInfo.InvariantCulture) > 65535)
            {
                return "A port is a number from 1 to 65535.";
            }
        }

        if (host.Length is 0 or > MaxHostLength)
        {
            return "A redirect address has a host of 1 to 253 characters.";
        }

        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-' || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return "A host is a name such as www.example.com: letters, digits and hyphens between dots.";
            }
        }

        return null;
    }
}
