using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// How the shared logic reads text (ADR-0005), the same in TypeScript. Both runtimes spell out every step, because their
/// built-in trims, case mappings and whitespace classes differ.
/// </summary>
internal static class TextSemantics
{
    private const char Replacement = (char)0xFFFD;
    private const int DottedCapitalI = 0x130;
    private const int DotlessSmallI = 0x131;

    /// <summary>Code units trimmed from both ends of every value and operand: the same explicit set in both runtimes.</summary>
    private static readonly FrozenSet<char> Trimmed = new[]
    {
        (char)9, (char)10, (char)11, (char)12, (char)13, (char)32, (char)0x85, (char)0xA0, (char)0x1680,
        (char)0x2000, (char)0x2001, (char)0x2002, (char)0x2003, (char)0x2004, (char)0x2005, (char)0x2006,
        (char)0x2007, (char)0x2008, (char)0x2009, (char)0x200A,
        (char)0x2028, (char)0x2029, (char)0x202F, (char)0x205F, (char)0x3000, (char)0xFEFF,
    }.ToFrozenSet();

    /// <summary>
    /// The blocks whose letters fold: Latin, Greek, Cyrillic, Armenian and the full-width Latin letters. Their case
    /// mappings have been stable in every runtime for years; other scripts gained mappings recently, so runtimes with
    /// older Unicode data (a server's ICU, an older browser) would fold them differently. Their letters compare exactly.
    /// </summary>
    private static readonly ImmutableArray<(int Low, int High)> Folded =
    [
        (0x0000, 0x024F), (0x0370, 0x03FF), (0x0400, 0x052F), (0x0530, 0x058F),
        (0x1E00, 0x1EFF), (0x1F00, 0x1FFF), (0xFF21, 0xFF3A), (0xFF41, 0xFF5A),
    ];

    /// <summary>Lone surrogates and noncharacters become U+FFFD, and the text is normalised to NFC. Patterns get this too.</summary>
    public static string Normalise(string value) => ReplaceInvalid(value).Normalize(NormalizationForm.FormC);

    /// <summary>
    /// Lone surrogates and noncharacters become U+FFFD, the text is normalised to NFC, and the explicit set is trimmed
    /// from both ends.
    /// </summary>
    public static string Clean(string value)
    {
        var text = Normalise(value);
        var start = 0;
        var end = text.Length;
        while (start < end && Trimmed.Contains(text[start]))
        {
            start++;
        }

        while (end > start && Trimmed.Contains(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    /// <summary>
    /// Folds case one character at a time, in the folded blocks only: the simple uppercase mapping, then the simple
    /// lowercase mapping. So final and medial sigma fold alike, sharp s never becomes "ss", and both Turkish i's fold
    /// to "i".
    /// </summary>
    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Folds(rune.Value))
            {
                builder.Append(buffer[..rune.EncodeToUtf16(buffer)]);
                continue;
            }

            if (rune.Value is DottedCapitalI or DotlessSmallI)
            {
                builder.Append('i');
                continue;
            }

            var folded = Rune.ToLowerInvariant(Rune.ToUpperInvariant(rune));
            builder.Append(buffer[..folded.EncodeToUtf16(buffer)]);
        }

        return builder.ToString();
    }

    /// <summary>Whether the code point lies in one of the folded blocks.</summary>
    public static bool Folds(int codePoint)
    {
        foreach (var (low, high) in Folded)
        {
            if (codePoint >= low && codePoint <= high)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Noncharacters: U+FDD0 to U+FDEF, and the last two code points of every plane.</summary>
    private static bool IsNoncharacter(int codePoint) =>
        codePoint is >= 0xFDD0 and <= 0xFDEF || (codePoint & 0xFFFE) == 0xFFFE;

    /// <summary>
    /// Lone surrogates (as JavaScript's <c>toWellFormed</c> does) and noncharacters become U+FFFD. .NET refuses to
    /// normalise U+FFFE, and its JSON reader refuses an escaped lone surrogate, so neither reaches the comparison.
    /// </summary>
    private static string ReplaceInvalid(string value)
    {
        // Made at the first replacement, so a text with nothing to replace is returned as it is, without a copy.
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var pair = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]);
            var width = pair ? 2 : 1;
            if (pair ? IsNoncharacter(char.ConvertToUtf32(c, value[i + 1])) : char.IsSurrogate(c) || IsNoncharacter(c))
            {
                builder ??= new StringBuilder(value.Length).Append(value, 0, i);
                builder.Append(Replacement);
            }
            else
            {
                builder?.Append(value, i, width);
            }

            i += width - 1;
        }

        return builder?.ToString() ?? value;
    }
}
