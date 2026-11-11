using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Samples.Spam;

/// <summary>
/// The link density sample's spam check (docs/extending.md): an entry whose answers are mostly links is likely an
/// advertisement. It counts the links in every answer against the words around them: three links or more, or more than
/// one link in five words, adds to the entry's score in proportion, up to enough for Spam on its own at ten links in
/// mostly links. An entry without links has nothing against it.
/// </summary>
public sealed partial class LinkDensitySpamCheck : IFormBuilderSpamCheck
{
    /// <summary>The reason the entry stores.</summary>
    public const string Reason = "link_density";

    /// <inheritdoc />
    public Task<FormBuilderSpamSignal?> CheckAsync(FormBuilderSpamCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(SignalOf(context.Answers.Values.SelectMany(texts => texts)));
    }

    /// <summary>The signal of some answers' texts, or null when their links say nothing against them.</summary>
    /// <param name="texts">The texts.</param>
    /// <returns>The signal, or null.</returns>
    public static FormBuilderSpamSignal? SignalOf(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var links = 0;
        var words = 0;
        foreach (var text in texts)
        {
            links += Link().Count(text);
            words += Word().Count(text);
        }

        if (links == 0)
        {
            return null;
        }

        var density = (double)links / Math.Max(words, 1);
        if (links < 3 && density <= 0.2)
        {
            return null;
        }

        // Ten links that are most of the words reach Spam on their own; fewer, or among more words, add less.
        var score = (int)Math.Round(Math.Min(links, 10) * 10 * Math.Min(1, density * 2));
        return new FormBuilderSpamSignal(Math.Clamp(score, 10, 100), Reason);
    }

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Link();

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Word();
}
