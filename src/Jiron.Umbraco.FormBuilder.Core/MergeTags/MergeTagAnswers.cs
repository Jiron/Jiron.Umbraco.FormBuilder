using System.Collections.Frozen;
using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The answers merge tags show (ADR-0014, ADR-0066): each field's values as its type read them, a repeatable group's
/// child once for each row, and the fields the rules hid, which show nothing. The submission pipeline gives them once
/// the visitor has submitted; a page shown again with what the visitor typed gives them too. Answers do not change,
/// with one exception (ADR-0083): while a submission's reader renders its hidden fields' defaults over one scope, it
/// gives that scope's answers again between two of those renderings, never during one. A source keeps nothing it read
/// of a scope's answers past a rendering; the labels of a scope's options are kept past one only under the parent
/// answers they were read for, which no change makes stale.
/// </summary>
public sealed class MergeTagAnswers
{
    private readonly FrozenDictionary<(Guid Field, int Row), MergeTagAnswer> _answers;

    // The answers a submission's reader gave last, for answers it made to be replaced; null for every other, which never
    // change (ADR-0083).
    private readonly Latest? _latest;
    /// <summary>The answers, and the fields the rules hid.</summary>
    /// <param name="answers">The answers, at most one for each field and row; one without values counts as none.</param>
    /// <param name="hidden">
    /// The keys of the fields the rules hid for the entry, by their own rule or by their page's, group's or repeatable
    /// group's; a hidden repeatable group hides its children too.
    /// </param>
    /// <exception cref="ArgumentException">An answer is null, or two answer the same field in the same row.</exception>
    public MergeTagAnswers(IEnumerable<MergeTagAnswer> answers, IEnumerable<Guid>? hidden = null)
        : this(answers, hidden, replaceable: false)
    {
    }

    private MergeTagAnswers(IEnumerable<MergeTagAnswer> answers, IEnumerable<Guid>? hidden, bool replaceable)
    {
        _answers = ByField(answers);
        _latest = replaceable ? new Latest(_answers) : null;
        Hidden = (hidden ?? []).ToFrozenSet();
    }

    /// <summary>No answers: before the visitor submits, or where no entry is rendered for.</summary>
    public static MergeTagAnswers None { get; } = new([]);

    /// <summary>The keys of the fields the rules hid for the entry.</summary>
    public IReadOnlySet<Guid> Hidden { get; }

    /// <summary>The answer of a field in a row, null for none or one without values.</summary>
    /// <param name="field">The field's key.</param>
    /// <param name="row">The row of a repeatable group's child, from 0; 0 for any other field.</param>
    public MergeTagAnswer? Find(Guid field, int row = 0) =>
        Current.TryGetValue((field, row), out var answer) && answer.Values.Length > 0 ? answer : null;

    /// <summary>The rows any of the fields has an answer in, in their order.</summary>
    /// <param name="fields">The keys of a repeatable group's children.</param>
    internal IReadOnlyList<int> Rows(IEnumerable<Guid> fields)
    {
        var keys = fields.ToHashSet();
        return [.. Current.Values.Where(answer => keys.Contains(answer.Field) && answer.Values.Length > 0).Select(answer => answer.Row).Distinct().Order()];
    }

    /// <summary>
    /// Answers that <see cref="Replace"/> gives again (ADR-0083): a submission's reader renders all of its hidden fields'
    /// defaults over one scope, whose answers it replaces after each value it resolves, so that a later default shows an
    /// earlier one's value and what the scope's tags read once is read once per submission. What the scope keeps of its
    /// options is kept for the parent answers it was read for, so a value resolved later gives later tags its own. No
    /// other answers change.
    /// </summary>
    /// <param name="answers">The answers, at most one for each field and row.</param>
    /// <param name="hidden">The keys of the fields the rules hid, which stay.</param>
    internal static MergeTagAnswers Replaceable(IEnumerable<MergeTagAnswer> answers, IEnumerable<Guid> hidden) => new(answers, hidden, replaceable: true);

    /// <summary>Takes these answers in place of those before, the hidden fields staying.</summary>
    /// <param name="answers">The answers, at most one for each field and row.</param>
    /// <exception cref="InvalidOperationException">The answers were not made <see cref="Replaceable"/>.</exception>
    internal void Replace(IEnumerable<MergeTagAnswer> answers)
    {
        if (_latest is null)
        {
            throw new InvalidOperationException("Only answers made to be replaced change.");
        }

        _latest.Answers = ByField(answers);
    }

    // The answers as they are now.
    private FrozenDictionary<(Guid Field, int Row), MergeTagAnswer> Current => _latest?.Answers ?? _answers;

    private static FrozenDictionary<(Guid Field, int Row), MergeTagAnswer> ByField(IEnumerable<MergeTagAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var byField = new Dictionary<(Guid Field, int Row), MergeTagAnswer>();
        foreach (var answer in answers)
        {
            if (answer is null)
            {
                throw new ArgumentException("An answer is never null.", nameof(answers));
            }

            if (!byField.TryAdd((answer.Field, answer.Row), answer))
            {
                throw new ArgumentException($"The field {answer.Field} has two answers in the row {answer.Row}.", nameof(answers));
            }
        }

        return byField.ToFrozenDictionary();
    }

    // What replaceable answers hold now.
    private sealed class Latest(FrozenDictionary<(Guid Field, int Row), MergeTagAnswer> answers)
    {
        public FrozenDictionary<(Guid Field, int Row), MergeTagAnswer> Answers { get; set; } = answers;
    }
}

/// <summary>A field's answer as merge tags see it (ADR-0066): its values, the files they stand for, and its row.</summary>
public sealed class MergeTagAnswer
{
    /// <summary>A field's answer.</summary>
    /// <param name="field">The field's key.</param>
    /// <param name="values">The values its type read, in the wire form (ADR-0058).</param>
    /// <param name="files">For a field that stores files, the file each value stands for, one for each value; otherwise none.</param>
    /// <param name="row">The row of a repeatable group's child, from 0; 0 for any other field.</param>
    /// <exception cref="ArgumentException">A value or a file is null, the files are not one for each value, or the row is negative.</exception>
    public MergeTagAnswer(Guid field, IReadOnlyList<FieldValue> values, IReadOnlyList<FieldFile>? files = null, int row = 0)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        if (values.Any(value => value is null) || (files is not null && files.Any(file => file is null)))
        {
            throw new ArgumentException("An answer's values and files are never null.", nameof(values));
        }

        if (files is { Count: > 0 } && files.Count != values.Count)
        {
            throw new ArgumentException("An answer's files are one for each of its values, or none.", nameof(files));
        }

        Field = field;
        Values = [.. values];
        Files = files is null ? [] : [.. files];
        Row = row;
    }

    /// <summary>The field's key.</summary>
    public Guid Field { get; }

    /// <summary>The values its type read, in the wire form.</summary>
    public ImmutableArray<FieldValue> Values { get; }

    /// <summary>The file each value stands for; none for a field that stores no files.</summary>
    public ImmutableArray<FieldFile> Files { get; }

    /// <summary>The row of a repeatable group's child, from 0; 0 for any other field.</summary>
    public int Row { get; }
}
