using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// A path into a JSON answer for a URL list (ADR-0076): <c>$</c>, then any number of <c>.name</c> (letters, digits,
/// <c>_</c> and <c>-</c>), <c>['name']</c> (any name, <c>\'</c> and <c>\\</c> for a quote and a backslash) and
/// <c>[index]</c> (<c>0</c> to <c>9999999</c>, no leading zero), at most 256 characters. <c>$</c> is where the path
/// starts: the answer, or an element of its list.
/// </summary>
internal sealed class OptionJsonPath
{
    /// <summary>The longest path.</summary>
    public const int MaxLength = 256;

    // A part's name, or null for a list's index.
    private readonly (string? Name, int Index)[] _parts;

    private OptionJsonPath((string? Name, int Index)[] parts) => _parts = parts;

    /// <summary>The path <paramref name="text"/> writes, or null when it is no path.</summary>
    public static OptionJsonPath? Parse(string? text)
    {
        if (text is not { Length: > 0 and <= MaxLength } || text[0] != '$')
        {
            return null;
        }

        var parts = new List<(string?, int)>();
        var i = 1;
        while (i < text.Length)
        {
            if (text[i] == '.')
            {
                var start = ++i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '-'))
                {
                    i++;
                }

                if (i == start)
                {
                    return null;
                }

                parts.Add((text[start..i], -1));
            }
            else if (text[i] == '[' && i + 1 < text.Length && text[i + 1] == '\'')
            {
                if (ReadQuoted(text, i + 2) is not ({ } name, var end))
                {
                    return null;
                }

                parts.Add((name, -1));
                i = end;
            }
            else if (text[i] == '[')
            {
                var start = ++i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                var digits = text.AsSpan(start, i - start);
                if (digits.Length is 0 or > 7 || (digits.Length > 1 && digits[0] == '0') || i == text.Length || text[i] != ']')
                {
                    return null;
                }

                parts.Add((null, int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture)));
                i++;
            }
            else
            {
                return null;
            }
        }

        return new OptionJsonPath([.. parts]);
    }

    /// <summary>Whether the path leads somewhere from <paramref name="start"/>, and where.</summary>
    public bool TrySelect(JsonElement start, out JsonElement found)
    {
        found = start;
        foreach (var (name, index) in _parts)
        {
            if (name is not null)
            {
                if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(name, out found))
                {
                    return false;
                }
            }
            else if (found.ValueKind != JsonValueKind.Array || index >= found.GetArrayLength())
            {
                return false;
            }
            else
            {
                found = found[index];
            }
        }

        return true;
    }

    /// <summary>
    /// A value as an option's value or label reads it: a string as it is, a number as the answer writes it, and
    /// <c>true</c> or <c>false</c>; null for anything else.
    /// </summary>
    public static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    // A quoted name from just after its opening quote: the name and where the path goes on after its closing bracket.
    private static (string? Name, int End) ReadQuoted(string text, int i)
    {
        var name = new StringBuilder();
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '\\' when i + 1 < text.Length && text[i + 1] is '\'' or '\\':
                    name.Append(text[i + 1]);
                    i += 2;
                    break;
                case '\\':
                    return (null, i);
                case '\'' when i + 1 < text.Length && text[i + 1] == ']':
                    return (name.ToString(), i + 2);
                case '\'':
                    return (null, i);
                default:
                    name.Append(text[i]);
                    i++;
                    break;
            }
        }

        return (null, i);
    }
}
