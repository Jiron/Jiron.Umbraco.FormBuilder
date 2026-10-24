using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// <c>jfbEntryValue.textKey</c>: a text value cleaned and folded exactly as rules compare text (ADR-0005), so the entries
/// screen filters and sorts as rules decide (ADR-0038). Only the first <see cref="MaxLength"/> code units are kept, the
/// column's length; the query builder compares the full text when a key fills the column (4.3d). On SQL Server the
/// column's case-insensitive collation can treat keys as equal that rules keep apart, so the query builder compares a
/// key again in a binary collation after the seek.
/// </summary>
internal static class EntryTextKey
{
    /// <summary>The longest key the column holds, in UTF-16 code units.</summary>
    public const int MaxLength = 256;

    /// <summary>The key of a text value.</summary>
    public static string For(string text)
    {
        var key = Whole(text);
        if (key.Length <= MaxLength)
        {
            return key;
        }

        // Never half a surrogate pair: the cut moves before a high surrogate that would lose its partner.
        return key[..(char.IsHighSurrogate(key[MaxLength - 1]) ? MaxLength - 1 : MaxLength)];
    }

    /// <summary>A text cleaned and folded as rules compare it, uncut: what a key that may be cut stands for.</summary>
    public static string Whole(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TextSemantics.Fold(TextSemantics.Clean(text));
    }

    /// <summary>Whether a key may have been cut: a filter for equal text then compares the text itself too.</summary>
    public static bool MayBeCut(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Length >= MaxLength - 1;
    }
}
