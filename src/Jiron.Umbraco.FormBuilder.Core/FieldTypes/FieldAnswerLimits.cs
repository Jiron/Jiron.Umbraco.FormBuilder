using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The longest answer a field takes (ADR-0030, ADR-0058): the most UTF-16 code units in any one text, and the most texts
/// in all, the parts' included. The pipeline holds every answer to them before the rules run, so no rule, pattern or
/// comparison reads more than a field takes; an answer over them fails at its field. A type's limits are its own
/// ceiling, the most any field of it can take, never the field's own checks such as its <c>maxLength</c>.
/// </summary>
public sealed record FieldAnswerLimits
{
    /// <summary>The longest text a type takes by default, as long as a plain text of a definition may be.</summary>
    public const int DefaultTextLength = DefinitionSaveChecks.PlainLength;

    /// <summary>
    /// The most texts a type takes by default when it takes several: as many options as a field may have where the host
    /// raises the limit furthest (ADR-0042).
    /// </summary>
    public static int DefaultManyTexts { get; } = DefinitionLimits.Ceiling.MaxOptionsPerField;

    /// <summary>Limits of a field that takes answers.</summary>
    /// <param name="longestText">The most code units in any one text, 1 or more.</param>
    /// <param name="mostTexts">The most texts in all, 1 or more.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either is below 1.</exception>
    public FieldAnswerLimits(int longestText, int mostTexts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(longestText, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(mostTexts, 1);
        LongestText = longestText;
        MostTexts = mostTexts;
    }

    private FieldAnswerLimits()
    {
    }

    /// <summary>
    /// The field takes no answer: the pipeline ignores what is posted for it, as for a calculation, which the server
    /// recomputes, or a repeatable group, whose child fields take the answers.
    /// </summary>
    public static FieldAnswerLimits None { get; } = new();

    /// <summary>The most UTF-16 code units in any one text; 0 for a field that takes no answer.</summary>
    public int LongestText { get; }

    /// <summary>The most texts in all, the parts' included; 0 for a field that takes no answer.</summary>
    public int MostTexts { get; }

    /// <summary>Whether the field takes answers at all.</summary>
    public bool TakesAnswers => MostTexts > 0;

    /// <summary>Whether the answers keep the limits; answers to a field that takes none never do, unless there are none.</summary>
    /// <param name="answers">What the visitor gave.</param>
    public bool Allows(FieldAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return answers.Count <= MostTexts && answers.LongestText <= LongestText;
    }
}
