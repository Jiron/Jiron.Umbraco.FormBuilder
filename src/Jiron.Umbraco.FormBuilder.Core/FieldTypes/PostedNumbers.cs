using System.Text;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Numbers as visitors post them (ADR-0058), converted exactly to the wire form of ADR-0005: the wire form itself, or what
/// a browser's number input posts, an optional minus, digits (leading zeros too), a point with digits, or both, and an
/// optional exponent (<c>1.5e3</c>, <c>.5</c>, <c>007</c>). The value is kept only when a calculation can read it: at most
/// <see cref="ExactDecimal.MaxDigits"/> digits in all and as many after the point (ADR-0069); whether a stored number
/// keeps it exactly is the field's check. Nothing is built from the digits before the length of the result is known, so
/// an exponent of any size costs what reading it costs.
/// </summary>
internal static class PostedNumbers
{
    /// <summary>The value in the wire form, without trailing zeros or "-0"; null when it is no number or too long to read.</summary>
    public static string? ToWire(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var i = 0;
        var negative = i < text.Length && text[i] == '-';
        if (negative)
        {
            i++;
        }

        var integerStart = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        var integer = text.AsSpan(integerStart, i - integerStart);
        var fraction = ReadOnlySpan<char>.Empty;
        if (i < text.Length && text[i] == '.')
        {
            var fractionStart = ++i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            fraction = text.AsSpan(fractionStart, i - fractionStart);
            if (fraction.IsEmpty)
            {
                return null;
            }
        }
        else if (integer.IsEmpty)
        {
            return null;
        }

        long exponent = 0;
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            i++;
            var exponentNegative = i < text.Length && text[i] == '-';
            if (i < text.Length && text[i] is '-' or '+')
            {
                i++;
            }

            var exponentStart = i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                // Past this the value is 0 or far too long to store either way; the count only has to stay that large.
                exponent = Math.Min((exponent * 10) + (text[i] - '0'), 1_000_000);
                i++;
            }

            if (i == exponentStart)
            {
                return null;
            }

            exponent = exponentNegative ? -exponent : exponent;
        }

        if (i != text.Length)
        {
            return null;
        }

        // The digits without leading and trailing zeros, and the power of ten they are multiplied by.
        var digits = string.Concat(integer, fraction).TrimStart('0');
        var trimmed = digits.TrimEnd('0');
        if (trimmed.Length == 0)
        {
            return "0";
        }

        var power = exponent - fraction.Length + (digits.Length - trimmed.Length);
        var places = power < 0 ? -power : 0;
        var length = trimmed.Length + (power > 0 ? power : 0);
        if (places > ExactDecimal.MaxDigits || (places > 0 ? trimmed.Length : length) > ExactDecimal.MaxDigits)
        {
            return null;
        }

        var wire = new StringBuilder(negative ? "-" : string.Empty);
        if (power >= 0)
        {
            wire.Append(trimmed).Append('0', (int)power);
        }
        else if (trimmed.Length > places)
        {
            wire.Append(trimmed, 0, trimmed.Length - (int)places).Append('.').Append(trimmed, trimmed.Length - (int)places, (int)places);
        }
        else
        {
            wire.Append("0.").Append('0', (int)places - trimmed.Length).Append(trimmed);
        }

        return wire.ToString();
    }
}
