using System.Globalization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>
/// The forms of an email address, a phone number and a web address (ADR-0069), read code unit by code unit as
/// <c>forms.ts</c> reads them in the browser, so both runtimes take the same answers. A character outside ASCII is a
/// code unit from U+00A0 on that cleaning does not trim: letters of every script, and no space or control character.
/// </summary>
internal static class BuiltInForms
{
    // The ASCII symbols an atom of an email address's local part may hold besides letters and digits (RFC 5322).
    private const string AtomSymbols = "!#$%&'*+-/=?^_`{|}~";

    /// <summary>
    /// Whether the text is an email address: a local part of 1 to 64 code units, atoms apart by single dots; one
    /// <c>@</c>; and a host name.
    /// </summary>
    public static bool IsEmailAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at is >= 1 and <= 64
            && value[..at].Split('.').All(atom => atom.Length > 0 && atom.All(IsAtomUnit))
            && IsHostName(value[(at + 1)..]);
    }

    /// <summary>
    /// Whether the text is a host name: at most 253 code units in at least two labels apart by dots, each of 1 to 63
    /// ASCII letters, digits, hyphens and characters outside ASCII, starting and ending with no hyphen, and the last not
    /// only digits.
    /// </summary>
    public static bool IsHostName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var labels = value.Split('.');
        return value.Length <= 253
            && labels.Length >= 2
            && labels.All(label => label.Length is >= 1 and <= 63 && label[0] != '-' && label[^1] != '-' && label.All(IsLabelUnit))
            && !labels[^1].All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Whether the text is a phone number in a lenient international form: digits, spaces, <c>-</c>, <c>.</c> and
    /// <c>/</c>, an optional <c>+</c> first and at most one pair of brackets holding digits and spaces, at least one
    /// digit; starting with <c>+</c>, <c>(</c> or a digit and ending with a digit, with 4 to 17 digits; then an optional
    /// extension.
    /// </summary>
    public static bool IsPhoneNumber(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var number = WithoutExtension(value);
        if (number.Length == 0 || !(number[0] is '+' or '(' || char.IsAsciiDigit(number[0])) || !char.IsAsciiDigit(number[^1]))
        {
            return false;
        }

        var digits = 0;
        var bracketed = 0;
        var open = -1;
        var close = -1;
        for (var i = 0; i < number.Length; i++)
        {
            var unit = number[i];
            var inBrackets = open >= 0 && close < 0;
            switch (unit)
            {
                case >= '0' and <= '9':
                    digits++;
                    bracketed += inBrackets ? 1 : 0;
                    break;
                case '(' when open < 0:
                    open = i;
                    break;
                case ')' when inBrackets:
                    close = i;
                    break;
                case '+' when i == 0:
                    break;
                default:
                    // Inside the brackets only spaces part the digits; outside them any separator does.
                    if (inBrackets ? !TextSemantics.IsTrimmed(unit) : !IsSeparator(unit))
                    {
                        return false;
                    }

                    break;
            }
        }

        return digits is >= 4 and <= 17 && (open < 0) == (close < 0) && (open < 0 || bracketed > 0);
    }

    /// <summary>
    /// Whether the text is a web address after its scheme: <c>//</c>, a host name with an optional port from 1 to 65535
    /// and no user name or password, then a path, query or fragment without control characters or characters cleaning
    /// trims. The scheme itself, before the first colon, is the field's to check; a text without one is read from its
    /// start, and the field refuses it for its scheme.
    /// </summary>
    public static bool IsWebAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (!value.AsSpan(colon + 1).StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var start = colon + 3;
        var end = value.IndexOfAny(['/', '?', '#'], start);
        end = end < 0 ? value.Length : end;

        // A host name holds no colon, so the last one starts the port; a user name or password puts an @ in the host.
        var authority = value[start..end];
        var portAt = authority.LastIndexOf(':');
        var host = portAt < 0 ? authority : authority[..portAt];
        var port = portAt < 0 ? null : authority[(portAt + 1)..];
        return IsHostName(host)
            && (port is null || (port.Length is >= 1 and <= 5 && port.All(char.IsAsciiDigit) && int.Parse(port, CultureInfo.InvariantCulture) is >= 1 and <= 65535))
            && !value[end..].Any(unit => unit < 0x20 || unit is >= (char)0x7F and < (char)0xA0 || TextSemantics.IsTrimmed(unit));
    }

    // A code unit outside ASCII: from U+00A0 on, and not one cleaning trims.
    private static bool IsOutsideAscii(char unit) => unit >= 0xA0 && !TextSemantics.IsTrimmed(unit);

    private static bool IsAtomUnit(char unit) => char.IsAsciiLetterOrDigit(unit) || AtomSymbols.Contains(unit, StringComparison.Ordinal) || IsOutsideAscii(unit);

    private static bool IsLabelUnit(char unit) => char.IsAsciiLetterOrDigit(unit) || unit == '-' || IsOutsideAscii(unit);

    // A separator of a phone number's digits outside its brackets.
    private static bool IsSeparator(char unit) => unit is '-' or '.' or '/' || TextSemantics.IsTrimmed(unit);

    // The number before an extension (optional spaces, "ext.", "ext", "x" or "#" in any case, optional spaces and 1 to
    // 6 digits at the end), or the whole text when it has none.
    private static string WithoutExtension(string value)
    {
        var digitsAt = value.Length;
        while (digitsAt > 0 && char.IsAsciiDigit(value[digitsAt - 1]))
        {
            digitsAt--;
        }

        if (value.Length - digitsAt is 0 or > 6)
        {
            return value;
        }

        var markerEnd = SpacesBefore(value, digitsAt);
        foreach (var marker in (ReadOnlySpan<string>)["ext.", "ext", "x", "#"])
        {
            if (markerEnd >= marker.Length && BuiltInValidation.AsciiLower(value.Substring(markerEnd - marker.Length, marker.Length)) == marker)
            {
                return value[..SpacesBefore(value, markerEnd - marker.Length)];
            }
        }

        return value;
    }

    // Where the run of spaces that ends at the index starts.
    private static int SpacesBefore(string value, int index)
    {
        while (index > 0 && TextSemantics.IsTrimmed(value[index - 1]))
        {
            index--;
        }

        return index;
    }
}
