using System.Collections.Immutable;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a visitor gave one field (ADR-0058): its texts in order, one per chosen option, file token or ranked option, and
/// its named parts, such as a name's <c>first</c> or an email's <c>confirm</c>, each with its texts. The pipeline cleans
/// every text and every part's name as rules clean them (ADR-0005, ADR-0080) and leaves out empty ones before a type
/// reads them, so none is empty here.
/// In a repeatable group, the answers are those of one row.
/// </summary>
public sealed class FieldAnswers
{
    /// <summary>No answer at all.</summary>
    public static FieldAnswers None { get; } = new([]);

    /// <summary>Answers without parts.</summary>
    /// <param name="values">The texts, in order.</param>
    /// <exception cref="ArgumentException">A text is null or empty.</exception>
    public FieldAnswers(IEnumerable<string> values)
        : this(values, null)
    {
    }

    /// <summary>Answers with parts.</summary>
    /// <param name="values">The field's own texts, in order.</param>
    /// <param name="parts">Each part's name and texts; a part without texts is left out.</param>
    /// <exception cref="ArgumentException">A text is null or empty, a part's name is blank, or a part is given twice.</exception>
    public FieldAnswers(IEnumerable<string> values, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? parts)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = Texts(values, nameof(values));
        var named = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, texts) in parts ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A part has a name.", nameof(parts));
            }

            ArgumentNullException.ThrowIfNull(texts, nameof(parts));
            var read = Texts(texts, nameof(parts));
            if (!seen.Add(name))
            {
                throw new ArgumentException($"The part '{name}' is given twice.", nameof(parts));
            }

            if (!read.IsEmpty)
            {
                named.Add(name, read);
            }
        }

        Parts = named.ToImmutable();
        Count = Values.Length + Parts.Values.Sum(texts => texts.Length);
        LongestText = Values.Concat(Parts.Keys).Concat(Parts.Values.SelectMany(texts => texts)).Select(text => text.Length).DefaultIfEmpty(0).Max();
    }

    /// <summary>The field's own texts, in order.</summary>
    public ImmutableArray<string> Values { get; }

    /// <summary>The parts that have texts, by name.</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<string>> Parts { get; }

    /// <summary>How many texts there are in all, the parts' included.</summary>
    public int Count { get; }

    /// <summary>
    /// The length of the longest text or name of a part with texts, in UTF-16 code units; 0 without any. A name counts,
    /// as a ranking posted without JavaScript names its options in its parts (ADR-0080).
    /// </summary>
    public int LongestText { get; }

    /// <summary>Whether there is no text at all.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>A part's texts; none when it was not given.</summary>
    /// <param name="name">The part's name.</param>
    public ImmutableArray<string> Part(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Parts.TryGetValue(name, out var texts) ? texts : [];
    }

    private static ImmutableArray<string> Texts(IEnumerable<string> texts, string parameter)
    {
        var read = texts.ToImmutableArray();
        if (read.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("An answer's text is never null or empty: the pipeline leaves out empty ones.", parameter);
        }

        return read;
    }
}
