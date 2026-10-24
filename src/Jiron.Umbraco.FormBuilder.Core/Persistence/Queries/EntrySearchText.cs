using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>
/// <c>jfbEntry.searchText</c> and the terms looked for in it (ADR-0039): both cleaned and folded as rules compare text
/// (ADR-0005), so a search finds what a rule's <c>contains</c> would, without regard to case on both databases.
/// </summary>
internal static class EntrySearchText
{
    /// <summary>The longest term, in UTF-16 code units once cleaned and folded.</summary>
    public const int MaxTermLength = 200;

    // Between two answers. Inside an answer or a term it becomes a space, so a term never matches across two answers.
    private const char Separator = (char)10;

    /// <summary>The search text of an entry's answers: the sensitive ones are the pipeline's to leave out (6.x).</summary>
    public static string For(IEnumerable<string> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return string.Join(Separator, answers.Select(Normalize).Where(answer => answer.Length > 0));
    }

    /// <summary>A text or a term, cleaned and folded, its line feeds as spaces; empty when nothing is left to look for.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TextSemantics.Fold(TextSemantics.Clean(text)).Replace(Separator, ' ');
    }
}
