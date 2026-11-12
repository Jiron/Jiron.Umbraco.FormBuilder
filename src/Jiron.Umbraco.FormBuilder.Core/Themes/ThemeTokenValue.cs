using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>
/// The grammar a theme token's value keeps to (16.1's review): what a colour, a size, a shadow, an easing or a font stack
/// needs, and nothing that could load anything. A token feeds properties such as <c>background</c>, where an
/// <c>image-set()</c>, an escaped <c>url(</c> or a <c>-webkit-image-set()</c> would fetch from another host on a visitor's
/// page, so the value is read as an allowlist rather than searched for what to refuse.
/// </summary>
internal static class ThemeTokenValue
{
    // The functions a value may call: colours, arithmetic, variables, gradients and easings. None of them takes an address.
    private static readonly FrozenSet<string> Functions = new[]
    {
        "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix", "light-dark",
        "calc", "min", "max", "clamp", "round", "mod", "rem", "abs", "sign",
        "var",
        "linear-gradient", "radial-gradient", "conic-gradient",
        "repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient",
        "cubic-bezier", "steps", "linear",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the text is a token's value: ASCII letters, digits, spaces and <c># % . , + - * / ( )</c> with balanced
    /// parentheses, quoted names of letters, digits, spaces, hyphens and underscores, and calls only to the functions above.
    /// No backslash (an escape could spell any function), no comment, no colon, no declaration's or block's end.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("/*", StringComparison.Ordinal))
        {
            return false;
        }

        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"' or '\'':
                    var end = value.IndexOf(character, index + 1);
                    if (end < 0 || !IsName(value.AsSpan(index + 1, end - index - 1)))
                    {
                        return false;
                    }

                    index = end;
                    break;
                case '(':
                    if (NameBefore(value, index) is { Length: > 0 } name && !Functions.Contains(name))
                    {
                        return false;
                    }

                    depth++;
                    break;
                case ')':
                    if (--depth < 0)
                    {
                        return false;
                    }

                    break;
                default:
                    if (!char.IsAsciiLetterOrDigit(character) && character is not (' ' or '#' or '%' or '.' or ',' or '+' or '-' or '*' or '/' or '_'))
                    {
                        return false;
                    }

                    break;
            }
        }

        return depth == 0;
    }

    // The identifier that ends where a parenthesis opens: empty for a bare parenthesis, as in calc((a + b) * 2).
    private static string NameBefore(string value, int open)
    {
        var start = open;
        while (start > 0 && (char.IsAsciiLetterOrDigit(value[start - 1]) || value[start - 1] is '-' or '_'))
        {
            start--;
        }

        return value[start..open];
    }

    // A quoted font or family name: letters of any script, digits, spaces, hyphens and underscores.
    private static bool IsName(ReadOnlySpan<char> name)
    {
        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character is not (' ' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
