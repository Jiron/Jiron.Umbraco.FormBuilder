using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Form aliases (ADR-0002, ADR-0044): they follow the field alias pattern, are unique among all forms in any letter
/// case, and come from the name when the editor gives none.
/// </summary>
internal static class FormAliases
{
    /// <summary>The alias of a name with no letter or digit.</summary>
    public const string Fallback = "form";

    /// <summary>The longest alias taken from a name, leaving room for a number up to 9999.</summary>
    public const int MaxBaseLength = DefinitionStructureChecks.MaxAliasLength - 4;

    // Letters that have no accent to remove but a usual spelling in ASCII: German sharp s, the ligatures ae and oe, the
    // slashed o, the stroked l and d, eth, thorn and the dotless i.
    private static readonly FrozenDictionary<char, string> Spelled = new Dictionary<char, string>
    {
        [(char)0xDF] = "ss",
        [(char)0xE6] = "ae",
        [(char)0xC6] = "ae",
        [(char)0x153] = "oe",
        [(char)0x152] = "oe",
        [(char)0xF8] = "o",
        [(char)0xD8] = "o",
        [(char)0x142] = "l",
        [(char)0x141] = "l",
        [(char)0x111] = "d",
        [(char)0x110] = "d",
        [(char)0xF0] = "d",
        [(char)0xD0] = "d",
        [(char)0xFE] = "th",
        [(char)0xDE] = "th",
        [(char)0x131] = "i",
    }.ToFrozenDictionary();

    /// <summary>Whether <paramref name="alias"/> follows the pattern <c>^[a-z][a-zA-Z0-9]{0,63}$</c>.</summary>
    public static bool IsValid(string? alias) => alias is not null && DefinitionStructureChecks.IsAlias(alias);

    /// <summary>
    /// The alias a name gives: its letters and digits without their accents, as camelCase words ("Contact us" gives
    /// <c>contactUs</c>), <see cref="Fallback"/> before a leading digit, and <see cref="Fallback"/> alone for a name with
    /// neither; at most <see cref="MaxBaseLength"/> characters.
    /// </summary>
    public static string FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = Words(name);
        var alias = new StringBuilder();
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            alias.Append(alias.Length == 0 ? lower : char.ToUpperInvariant(lower[0]) + lower[1..]);
        }

        if (alias.Length == 0)
        {
            return Fallback;
        }

        if (char.IsAsciiDigit(alias[0]))
        {
            alias.Insert(0, Fallback);
        }

        return alias.Length <= MaxBaseLength ? alias.ToString() : alias.ToString(0, MaxBaseLength);
    }

    /// <summary>
    /// The start that every alias <see cref="FirstFree"/> gives for <paramref name="alias"/> shares, up to the number
    /// 9999: the aliases to ask the database about.
    /// </summary>
    public static string Stem(string alias)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        return alias.Length <= MaxBaseLength ? alias : alias[..MaxBaseLength];
    }

    /// <summary>
    /// <paramref name="alias"/> when no form has it, otherwise the alias with the lowest number from 2 that no form has
    /// (<c>contact2</c>), compared in any letter case as the database compares aliases (ADR-0033). An alias too long to
    /// take its number is cut, so the alias with its number has at most 64 characters (<c>...ab2</c> for a 64-character
    /// alias that ends in <c>abc</c>).
    /// </summary>
    public static string FirstFree(string alias, IEnumerable<string> taken)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        ArgumentNullException.ThrowIfNull(taken);
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(alias))
        {
            return alias;
        }

        for (var number = 2; ; number++)
        {
            var suffix = number.ToString(CultureInfo.InvariantCulture);
            var candidate = alias[..Math.Min(alias.Length, DefinitionStructureChecks.MaxAliasLength - suffix.Length)] + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // The runs of ASCII letters and digits once accents are removed and the letters above spelled out.
    private static List<string> Words(string name)
    {
        var plain = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(Spelled.TryGetValue(c, out var spelled) ? spelled : c);
        }

        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in plain.ToString())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }
}
