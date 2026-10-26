using System.Collections.Frozen;
using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// A stored entry's answers, typed again (ADR-0070): one <see cref="EntryAnswer"/> for each field and row with values,
/// in the catalogue's order and then by row. Exports, the entries screen, actions and mail read an entry through them.
/// </summary>
internal sealed class EntryAnswers
{
    private readonly FrozenDictionary<(Guid Field, int Row), EntryAnswer> _byField;

    /// <summary>The answers, in their order.</summary>
    /// <exception cref="ArgumentException">An answer is null, or two answer the same field in the same row.</exception>
    public EntryAnswers(IEnumerable<EntryAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        All = [.. answers];
        var byField = new Dictionary<(Guid Field, int Row), EntryAnswer>();
        foreach (var answer in All)
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

        _byField = byField.ToFrozenDictionary();
    }

    /// <summary>The answers, in the catalogue's order and then by row.</summary>
    public ImmutableArray<EntryAnswer> All { get; }

    /// <summary>The answer of a field in a row, or null for none.</summary>
    /// <param name="field">The field's key; a grid's for its rows' values.</param>
    /// <param name="row">The row of a repeatable group's child, from 0; 0 for any other field.</param>
    public EntryAnswer? Find(Guid field, int row = 0) => _byField.GetValueOrDefault((field, row));

    /// <summary>The rows of a repeatable group that any of its children has an answer in, in their order.</summary>
    /// <param name="repeater">The repeatable group's key.</param>
    public IReadOnlyList<int> Rows(Guid repeater) =>
        [.. All.Where(answer => answer.Repeater == repeater).Select(answer => answer.Row).Distinct().Order()];

    /// <summary>
    /// The answers as merge tags show them (ADR-0066): the values as the field types read them, a file's with its name
    /// and size, so actions and mail render a stored entry as they render a submission.
    /// </summary>
    /// <param name="hidden">The fields the rules hid for the entry, which show nothing.</param>
    public MergeTagAnswers ForMergeTags(IEnumerable<Guid>? hidden = null) =>
        new(All.Select(answer => new MergeTagAnswer(answer.Field, [.. answer.Values.Select(value => value.Value)], Files(answer), answer.Row)), hidden);

    // A file whose row is gone has no name to show, so an answer without all its files shows its values alone.
    private static IReadOnlyList<FieldFile>? Files(EntryAnswer answer) =>
        answer.Storage == FieldStorageKind.File && answer.Values.All(value => value.File is not null)
            ? [.. answer.Values.Select(value => new FieldFile(value.File!.OriginalName, value.File.SizeBytes))]
            : null;
}

/// <summary>A field's stored answer in one row (ADR-0070).</summary>
internal sealed record EntryAnswer
{
    /// <summary>The field's key; a grid's for the values of its rows.</summary>
    public required Guid Field { get; init; }

    /// <summary>The row of a repeatable group's child, from 0; 0 for any other field.</summary>
    public int Row { get; init; }

    /// <summary>The repeatable group the field is a child of, or null.</summary>
    public Guid? Repeater { get; init; }

    /// <summary>The field's alias, as the catalogue keeps it.</summary>
    public required string Alias { get; init; }

    /// <summary>The field type's alias, as the catalogue keeps it.</summary>
    public required string Type { get; init; }

    /// <summary>Which column holds its values.</summary>
    public required FieldStorageKind Storage { get; init; }

    /// <summary>The values, in their order: a grid's by its rows in the catalogue's order.</summary>
    public required ImmutableArray<EntryAnswerValue> Values { get; init; }
}

/// <summary>
/// One stored value, typed again from its stored text (ADR-0070): at most one of the typed properties is set (a ticked
/// consent's wording comes with its <c>true</c>), none for text or for a text its kind cannot read, and
/// <see cref="Value"/> gives it back as rules and merge tags read it.
/// </summary>
internal sealed record EntryAnswerValue
{
    /// <summary>The value as the field type read it (ADR-0058): a grid row's with its row, a consent's <c>true</c> with its hash.</summary>
    public required FieldValue Value { get; init; }

    /// <summary>The stored text: a file's key, a consent's hash, a value of parts as its JSON object.</summary>
    public string Text => Value.StoredText ?? Value.Text;

    /// <summary>A number a stored number keeps exactly (15 digits, ADR-0003); null for a longer one, which is text only.</summary>
    public decimal? Number { get; init; }

    /// <summary>A date.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>A date with a time, as the wall clock showed it: its kind is unspecified.</summary>
    public DateTime? DateTime { get; init; }

    /// <summary>A time of day.</summary>
    public TimeOnly? Time { get; init; }

    /// <summary>A yes or no; a ticked consent's <c>true</c>.</summary>
    public bool? Bool { get; init; }

    /// <summary>The entry's file a file value stands for, or null when its row is gone.</summary>
    public StoredEntryFile? File { get; init; }

    /// <summary>The parts of a value of parts, in the type's order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Parts { get; init; }

    /// <summary>The wording a ticked consent agreed to, or null when it is not stored.</summary>
    public StoredConsentText? Consent { get; init; }
}
