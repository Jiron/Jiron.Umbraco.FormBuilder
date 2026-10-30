using System.Collections.Immutable;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// The step between a post's answers and an entry's answers (ADR-0072), which the submission pipeline runs (6.1): every
/// answer cleaned, held to its type's limits and read by its type before the rules; a repeatable group's rows counted
/// and held to its limits; the rules over what was read, with every calculation worked out by the server; a hidden
/// field's server-only value resolved; and each field the rules leave shown checked, a repeatable group's children row
/// by row, a choice whose options come from a list or another source against the options it resolves (ADR-0077). A
/// field that keeps its value on the server is checked as its value is resolved, and a value its type does not take is
/// not taken, so it fails a submission only for being required without one (ADR-0080). What the rules hide is neither
/// checked nor stored.
/// </summary>
internal sealed partial class SubmissionReader(FieldTypeRegistry types, IFormBuilderMergeTagService mergeTags, IFormBuilderOptionService options, ILogger<SubmissionReader> logger)
{
    // In the key of what was resolved, the parent answers of a field whose parent's options are unknown: every list of
    // values is written with each value's length first, so no list writes this.
    private const string UnknownParents = "?";

    /// <summary>Reads a submission's answers over its form.</summary>
    /// <param name="post">The submission.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    /// <exception cref="ArgumentException">The time zone is not an IANA name this server knows.</exception>
    /// <exception cref="LogicException">The version fails the rules' check, which publishing refuses before a form goes live (ADR-0061).</exception>
    public async Task<SubmissionAnswers> ReadAsync(PostedSubmission post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (CalendarValues.FindTimeZone(post.TimeZone) is null)
        {
            throw new ArgumentException($"'{post.TimeZone}' is no IANA time zone this server knows.", nameof(post));
        }

        var definition = post.Scope.Form;
        var now = post.Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDayNumber(CalendarValues.Today(now, post.TimeZone));
        var slots = Take(definition, post);
        var logic = DefinitionLogic.From(definition, types);
        var allowed = definition.Settings.Developer.QueryValues;
        var query = post.Scope.Query.Where(pair => allowed.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var context = new EvaluationContext(now, post.TimeZone, post.Scope.Culture, new Member(post.Scope.Member is not null, post.MemberGroups), query, []);
        var state = FormRules.Evaluate(logic, Answers(slots), context);

        // Only the rules of actions and outcomes may look at a value the server resolves (ADR-0035), so evaluating again
        // with the resolved values leaves what is shown as it was and decides the actions and the outcome.
        var resolving = new Resolving(post, [], slots.SelectMany(slot => slot.Field.Fields.Prepend(slot.Field)).ToDictionary(field => field.Key), ImmutableArray.CreateBuilder<SubmissionPlace>(), ImmutableArray.CreateBuilder<SubmissionUnique>());
        if (await ResolveAsync(slots, state, post, resolving, today, cancellationToken))
        {
            state = FormRules.Evaluate(logic, Answers(slots), context);
        }

        var stored = Stored(slots, state, post);
        return new SubmissionAnswers(stored, await ErrorsAsync(slots, state, resolving, today, cancellationToken), state, ForMergeTags(stored, state))
        {
            Places = resolving.Places.ToImmutable(),
            Unique = resolving.Unique.ToImmutable(),
        };
    }

    // Every field of the version whose type the site has, outside repeatable groups and in the form's order, with its
    // answer read; a repeatable group with its rows, read only when their number keeps the group's limits, so a post with
    // too many rows costs no more than its count.
    private List<Slot> Take(FormDefinition definition, PostedSubmission post)
    {
        var slots = new List<Slot>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (types.FindType(field.Type) is not { } type)
            {
                continue;
            }

            var slot = new Slot(field, type);
            if (slot.IsGroup)
            {
                var rows = post.Rows.GetValueOrDefault(field.Key) ?? [];
                slot.RowErrors = FieldValues.CheckRows(type, field, rows.Count);
                if (slot.RowErrors.IsEmpty)
                {
                    foreach (var row in rows)
                    {
                        slot.Rows.Add([.. field.Fields
                            .Select(child => (Child: child, Type: types.FindType(child.Type)))
                            .Where(child => child.Type is not null)
                            .Select(child => Read(new Slot(child.Child, child.Type!), row?.GetValueOrDefault(child.Child.Key)))]);
                    }
                }
            }
            else
            {
                Read(slot, post.Fields.GetValueOrDefault(field.Key));
            }

            slots.Add(slot);
        }

        return slots;
    }

    // A field's answer held to its type's limits and read. A value the server resolves is never taken from the post,
    // unless the field is prefilled from the query, whose value only the visitor's page has (ADR-0002); a prefilled value
    // the field cannot take (too long, more texts or parts than it takes, or one its type cannot read) counts as none,
    // so its default stands in, as the visitor can neither see nor change it (ADR-0080). Its type's check waits for the
    // rules (ResolveAsync).
    private Slot Read(Slot slot, PostedAnswer? posted)
    {
        var prefilled = slot.Field.Prefill?.Query is not null;
        var serverOnly = slot.Type.Declaration.ServerOnlyDefault;
        var answers = serverOnly && !prefilled ? FieldAnswers.None : Clean(posted);
        slot.OverLimit = FieldValues.CheckLimits(slot.Type, slot.Field, answers);
        slot.Read = slot.OverLimit is null ? FieldValues.Read(slot.Type, slot.Field, answers) : Slot.Nothing;
        if (serverOnly && prefilled && (slot.OverLimit is not null || slot.Read.IsUnreadable))
        {
            LogPrefillNotTaken(logger, NameOf(slot));
            slot.OverLimit = null;
            slot.Read = FieldValues.Read(slot.Type, slot.Field, FieldAnswers.None);
        }

        return slot;
    }

    /// <summary>
    /// Every text and part name cleaned as rules clean them (ADR-0005), the empty texts and the parts without a name left
    /// out (ADR-0058), and the texts of names that clean to one name kept under it, in their order (ADR-0080). The page
    /// reads its answers the same way before its rules (ADR-0097).
    /// </summary>
    /// <param name="posted">The answer as posted, or null for none.</param>
    public static FieldAnswers Clean(PostedAnswer? posted)
    {
        if (posted is null)
        {
            return FieldAnswers.None;
        }

        static IEnumerable<string> Texts(IEnumerable<string?>? texts) =>
            (texts ?? []).Select(text => text is null ? string.Empty : TextSemantics.Clean(text)).Where(text => text.Length > 0);
        var parts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, texts) in posted.Parts ?? new Dictionary<string, IReadOnlyList<string?>>())
        {
            if (TextSemantics.Clean(name ?? string.Empty) is var clean && string.IsNullOrWhiteSpace(clean))
            {
                continue;
            }

            if (!parts.TryGetValue(clean, out var kept))
            {
                parts[clean] = kept = [];
            }

            kept.AddRange(Texts(texts));
        }

        return new FieldAnswers([.. Texts(posted.Values)], parts.Select(part => KeyValuePair.Create<string, IEnumerable<string>>(part.Key, part.Value)));
    }

    // The answers as rules see them: each field's texts by its id, a grid's by its rows' ids, and a repeatable group's
    // rows; a calculation has none of its own, as the form engine works it out.
    // The answers as the rules see them, and the fields whose answers their types could not read (ADR-0114).
    private static FormAnswers Answers(List<Slot> slots)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(StringComparer.Ordinal);
        var unreadable = new HashSet<string>(StringComparer.Ordinal);
        var unreadableRows = new Dictionary<string, IReadOnlyList<IReadOnlySet<string>>>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (!slot.IsGroup)
            {
                Put(values, slot);
                if (slot.IsUnreadable)
                {
                    unreadable.Add(DefinitionLogic.Id(slot.Field.Key));
                }

                continue;
            }

            rows[DefinitionLogic.Id(slot.Field.Key)] = [.. slot.Rows.Select(row =>
            {
                var texts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var child in row)
                {
                    Put(texts, child);
                }

                return (IReadOnlyDictionary<string, IReadOnlyList<string>>)texts;
            })];
            unreadableRows[DefinitionLogic.Id(slot.Field.Key)] = [.. slot.Rows.Select(row =>
                (IReadOnlySet<string>)row.Where(child => child.IsUnreadable).Select(child => DefinitionLogic.Id(child.Field.Key)).ToHashSet(StringComparer.Ordinal))];
        }

        return new FormAnswers(values, rows) { Unreadable = unreadable, UnreadableRows = unreadableRows };
    }

    private static void Put(Dictionary<string, IReadOnlyList<string>> texts, Slot slot)
    {
        foreach (var values in slot.Values.GroupBy(value => value.Row))
        {
            texts[DefinitionLogic.Id(values.Key ?? slot.Field.Key)] = [.. values.Select(value => value.Text)];
        }
    }

    // Each shown field whose type keeps its value on the server, in the form's order, a repeatable group's children row
    // by row. A value the query gave it that its type does not take counts as none (TakesAsync). A field without a value
    // then gets its default value rendered where hidden values render, over the answers so far (the earlier resolved
    // values among them), cleaned, held to its type's limits and read by its type; a value its type does not take is not
    // stored. The visitor can neither see nor change such a field, so no error could tell them anything (ADR-0080); each
    // value not taken is logged, naming the field, never the value. All the defaults render over one scope, given the
    // answers again after each change, so what its tags read once, such as an option's label, is read once (ADR-0083).
    // Whether any value changed.
    private async Task<bool> ResolveAsync(List<Slot> slots, FormState state, PostedSubmission post, Resolving resolving, DateOnly today, CancellationToken cancellationToken)
    {
        var changed = false;
        MergeTagScope? scope = null;
        foreach (var (slot, row) in Shown(slots, state))
        {
            if (!slot.Type.Declaration.ServerOnlyDefault)
            {
                continue;
            }

            if (!slot.Values.IsEmpty)
            {
                if (await TakesAsync(slot, row, slots, state, resolving, today, cancellationToken))
                {
                    continue;
                }

                LogPrefillNotTaken(logger, NameOf(slot));
                slot.Read = FieldValues.Read(slot.Type, slot.Field, FieldAnswers.None);
                changed = true;
                scope?.Answers.Replace(ForMergeTags(Stored(slots, state, post)));
            }

            if (slot.Field.DefaultValue is not { } template)
            {
                continue;
            }

            scope ??= post.Scope with { Answers = MergeTagAnswers.Replaceable(ForMergeTags(Stored(slots, state, post)), HiddenKeys(state)), TextOwner = null };
            var texts = new List<string>();
            foreach (var text in template.Values)
            {
                var rendering = await mergeTags.RenderAsync(text, MergeTagPlace.HiddenValue, MergeTagContext.Text, scope, cancellationToken);
                if (TextSemantics.Clean(rendering.Text) is { Length: > 0 } clean)
                {
                    texts.Add(clean);
                }
            }

            var answers = new FieldAnswers(texts);
            var none = slot.Read;
            if (FieldValues.CheckLimits(slot.Type, slot.Field, answers) is null)
            {
                slot.Read = FieldValues.Read(slot.Type, slot.Field, answers);
                if (slot.Read is { IsUnreadable: false, Values.IsEmpty: true })
                {
                    continue;
                }

                if (await TakesAsync(slot, row, slots, state, resolving, today, cancellationToken))
                {
                    changed = true;
                    scope.Answers.Replace(ForMergeTags(Stored(slots, state, post)));
                    continue;
                }
            }

            LogDefaultNotTaken(logger, NameOf(slot));
            slot.Read = none;
        }

        return changed;
    }

    // Whether a field's value, as its type read it, is one its type takes: its type's check, against the options its
    // source resolves, and its own checks pass over the answers so far (FieldValues.Check), in a repeatable group's row
    // over that row's.
    private async Task<bool> TakesAsync(Slot slot, ShownRow? row, List<Slot> slots, FormState state, Resolving resolving, DateOnly today, CancellationToken cancellationToken)
    {
        var others = Others(slots, state, state.Hidden.ToHashSet(StringComparer.Ordinal));
        if (row is { } at)
        {
            others = RowOthers(others, at.Fields, at.State);
        }

        var offered = await OfferedAsync(slot, others, resolving, cancellationToken);
        var found = FieldValues.Check(slot.Type, slot.Field, slot.Read, new FieldCheckInput(IsRequired: false, today) { Values = others, Options = offered });
        return found.Errors.IsEmpty && found.Checks.IsEmpty;
    }

    // The shown fields outside repeatable groups and, for a shown group, the children shown in each of its rows, each with
    // its row's fields and state.
    private static IEnumerable<(Slot Slot, ShownRow? Row)> Shown(List<Slot> slots, FormState state)
    {
        var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            var id = DefinitionLogic.Id(slot.Field.Key);
            if (hidden.Contains(id))
            {
                continue;
            }

            if (!slot.IsGroup)
            {
                yield return (slot, null);
                continue;
            }

            var rows = RowsOf(state, id);
            for (var row = 0; row < slot.Rows.Count && row < rows.Count; row++)
            {
                foreach (var child in slot.Rows[row].Where(child => !rows[row].Hidden.Contains(DefinitionLogic.Id(child.Field.Key))))
                {
                    yield return (child, new ShownRow(slot.Rows[row], rows[row]));
                }
            }
        }
    }

    // What the entry stores of the shown fields, in the form's order; a group's children by row, numbered from 0 among the
    // rows that hold a value. A file field whose tokens do not all stand for its uploads stores nothing (its error says so).
    private static ImmutableArray<EntryFieldAnswer> Stored(List<Slot> slots, FormState state, PostedSubmission post)
    {
        var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
        var claimed = new HashSet<int>();
        var answers = ImmutableArray.CreateBuilder<EntryFieldAnswer>();
        foreach (var slot in slots)
        {
            var id = DefinitionLogic.Id(slot.Field.Key);
            if (hidden.Contains(id))
            {
                continue;
            }

            if (!slot.IsGroup)
            {
                Add(answers, slot, ValuesOf(slot, state.Calculations), 0, post, claimed);
                continue;
            }

            var rows = RowsOf(state, id);
            var stored = 0;
            for (var row = 0; row < slot.Rows.Count && row < rows.Count; row++)
            {
                var before = answers.Count;
                foreach (var child in slot.Rows[row].Where(child => !rows[row].Hidden.Contains(DefinitionLogic.Id(child.Field.Key))))
                {
                    Add(answers, child, ValuesOf(child, rows[row].Calculations), stored, post, claimed);
                }

                if (answers.Count > before)
                {
                    stored++;
                }
            }
        }

        return answers.ToImmutable();
    }

    private static void Add(ImmutableArray<EntryFieldAnswer>.Builder answers, Slot slot, ImmutableArray<FieldValue> values, int row, PostedSubmission post, HashSet<int> claimed)
    {
        if (values.IsEmpty)
        {
            return;
        }

        if (slot.Type.Declaration.Storage != FieldStorageKind.File)
        {
            answers.Add(new EntryFieldAnswer(slot.Field.Key, values, row));
        }
        else if (UploadsOf(slot, values, post, claimed) is { } uploads)
        {
            answers.Add(new EntryFieldAnswer(slot.Field.Key, values, row) { Files = uploads });
        }
    }

    // A calculation's value is the server's, rounded to its places (ADR-0063), whatever was posted; any other field's is
    // what its type read.
    private static ImmutableArray<FieldValue> ValuesOf(Slot slot, IReadOnlyDictionary<string, string?> calculations) =>
        !slot.Calculates ? slot.Values
            : calculations.GetValueOrDefault(DefinitionLogic.Id(slot.Field.Key)) is { } result ? [new FieldValue(result)] : [];

    // The upload each token stands for, when every one stands for an upload of the field that no entry holds and that no
    // other value took, by any token: neither an earlier value of the submission (in the form's order, a repeatable
    // group's rows among them) nor one of the same answer; otherwise null (ADR-0080). The uploads are claimed only when
    // the whole answer stands, so a value that fails takes nothing from a later one.
    private static ImmutableArray<StoredEntryFile>? UploadsOf(Slot slot, ImmutableArray<FieldValue> values, PostedSubmission post, HashSet<int> claimed)
    {
        var uploads = ImmutableArray.CreateBuilder<StoredEntryFile>(values.Length);
        foreach (var value in values)
        {
            if (!post.Uploads.TryGetValue(value.Text, out var upload) || upload.FieldKey != slot.Field.Key || upload.EntryId is not null
                || claimed.Contains(upload.Id) || uploads.Any(earlier => earlier.Id == upload.Id))
            {
                return null;
            }

            uploads.Add(upload);
        }

        claimed.UnionWith(uploads.Select(upload => upload.Id));
        return uploads.MoveToImmutable();
    }

    // The errors of the shown fields in the form's order: a group's own, then its children's row by row; for each field
    // its type's error kinds, its own checks and the form-level validation rules whose message shows at it.
    private async Task<ImmutableArray<SubmissionError>> ErrorsAsync(List<Slot> slots, FormState state, Resolving resolving, DateOnly today, CancellationToken cancellationToken)
    {
        var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
        var required = state.Required.ToHashSet(StringComparer.Ordinal);
        var others = Others(slots, state, hidden);
        var claimed = new HashSet<int>();
        var errors = ImmutableArray.CreateBuilder<SubmissionError>();
        foreach (var slot in slots)
        {
            var id = DefinitionLogic.Id(slot.Field.Key);
            if (hidden.Contains(id))
            {
                continue;
            }

            if (!slot.IsGroup)
            {
                await CheckAsync(errors, slot, null, required.Contains(id), others, resolving, claimed, today, cancellationToken);
                Messages(errors, state.Messages, slot.Field, null);
                continue;
            }

            errors.AddRange(slot.RowErrors.Select(kind => new SubmissionError(slot.Field.Key, null, kind)));
            Messages(errors, state.Messages, slot.Field, null);
            var rows = RowsOf(state, id);
            for (var row = 0; row < slot.Rows.Count && row < rows.Count; row++)
            {
                var rowOthers = RowOthers(others, slot.Rows[row], rows[row]);
                foreach (var child in slot.Rows[row])
                {
                    var childId = DefinitionLogic.Id(child.Field.Key);
                    if (!rows[row].Hidden.Contains(childId))
                    {
                        await CheckAsync(errors, child, row, rows[row].Required.Contains(childId), rowOthers, resolving, claimed, today, cancellationToken);
                        Messages(errors, rows[row].Messages, child.Field, row);
                    }
                }
            }
        }

        return errors.ToImmutable();
    }

    // The options a list or another source resolves for a field with an answer to check (ADR-0077); null for a field
    // whose options are its own, and for one whose source cannot give them.
    private async Task<IReadOnlyList<FormBuilderOption>?> OfferedAsync(Slot slot, Dictionary<Guid, ImmutableArray<string>> others, Resolving resolving, CancellationToken cancellationToken) =>
        slot.Field.Options is { Source: not OptionSource.Items } && slot.OverLimit is null && !slot.Values.IsEmpty
            ? await ResolvedAsync(slot.Field, others, resolving, [], cancellationToken)
            : null;

    // A field's options from its list or source, in the submission's culture and page and for its parent's answers held
    // to the options the parent offers (ADR-0081), asked for once per field and parent answers; null when the source
    // cannot give them or the parent's options are unknown, either logged once per field.
    private async Task<IReadOnlyList<FormBuilderOption>?> ResolvedAsync(
        FormField field,
        Dictionary<Guid, ImmutableArray<string>> others,
        Resolving resolving,
        ImmutableHashSet<Guid> asking,
        CancellationToken cancellationToken)
    {
        ImmutableArray<string> parentValues = [];
        if (field.Options?.Parent is { } parent)
        {
            if (await HeldParentAnswersAsync(parent, others, resolving, asking.Add(field.Key), cancellationToken) is not { } held)
            {
                if (resolving.Resolved.TryAdd((field.Key, UnknownParents), FormBuilderResolvedOptions.Unavailable))
                {
                    LogOptionsUnavailable(logger, field.Alias ?? DefinitionLogic.Id(field.Key));
                }

                return null;
            }

            parentValues = held;
        }

        // Each value after its length, so no two lists of values make one key.
        var key = (field.Key, string.Concat(parentValues.Select(value => $"{value.Length}:{value}")));
        if (!resolving.Resolved.TryGetValue(key, out var found))
        {
            found = await options.GetAsync(field, new FormBuilderOptionContext(resolving.Post.Scope.Culture) { ParentValues = parentValues, Page = resolving.Post.Scope.Page }, cancellationToken);
            resolving.Resolved[key] = found;
            if (!found.IsAvailable)
            {
                LogOptionsUnavailable(logger, field.Alias ?? DefinitionLogic.Id(field.Key));
            }
        }

        return found.IsAvailable ? found.Options : null;
    }

    // The parent's answers as the rules see them that are options the parent offers, its own or those its list or source
    // resolves, each once and in their order, so that no posted text reaches a source or becomes a cache key (ADR-0081);
    // null when the parent's options are unknown. A parent outside the form's fields, or one that a chain of parents leads
    // back to, offers none. A parent in a repeatable group answers in the same row, as the row's answers say.
    private async Task<ImmutableArray<string>?> HeldParentAnswersAsync(
        Guid parent,
        Dictionary<Guid, ImmutableArray<string>> others,
        Resolving resolving,
        ImmutableHashSet<Guid> asking,
        CancellationToken cancellationToken)
    {
        if (!others.TryGetValue(parent, out var answers) || answers.IsEmpty
            || !resolving.Fields.TryGetValue(parent, out var parentField) || parentField.Options is not { } parentOptions || asking.Contains(parent))
        {
            return [];
        }

        var offered = parentOptions.Source == OptionSource.Items
            ? parentOptions.Items.Select(item => item.Value)
            : (await ResolvedAsync(parentField, others, resolving, asking, cancellationToken))?.Select(option => option.Value);
        if (offered is null)
        {
            return null;
        }

        var values = offered.ToHashSet(StringComparer.Ordinal);
        return [.. answers.Where(values.Contains).Distinct(StringComparer.Ordinal)];
    }

    // One shown field's errors: over its type's limits alone; a file token that stands for no upload of the field, for
    // one an entry holds, or for one an earlier value or another token of the answer named, as invalid; otherwise its
    // type's check, against the options its source resolves, and its own checks (FieldValues.Check). A calculation's
    // value is the server's; a value a field that keeps its value on the server took was checked as it was resolved, so
    // such a field fails only for being required without one (ADR-0080), and its options are not asked for again.
    private async Task CheckAsync(
        ImmutableArray<SubmissionError>.Builder errors,
        Slot slot,
        int? row,
        bool isRequired,
        Dictionary<Guid, ImmutableArray<string>> others,
        Resolving resolving,
        HashSet<int> claimed,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var key = slot.Field.Key;
        if (slot.OverLimit is { } kind)
        {
            errors.Add(new SubmissionError(key, row, kind));
            return;
        }

        if (slot.Calculates)
        {
            return;
        }

        if (slot.Type.Declaration.ServerOnlyDefault)
        {
            if (slot.Values.IsEmpty)
            {
                errors.AddRange(FieldValues.Check(slot.Type, slot.Field, Slot.Nothing, new FieldCheckInput(isRequired, today)).Errors.Select(error => new SubmissionError(key, row, error)));
            }

            return;
        }

        ImmutableArray<FieldFile> files = [];
        if (slot.Type.Declaration.Storage == FieldStorageKind.File && !slot.Values.IsEmpty)
        {
            if (UploadsOf(slot, slot.Values, resolving.Post, claimed) is not { } uploads)
            {
                errors.Add(new SubmissionError(key, row, FieldMessageKinds.Invalid));
                return;
            }

            files = [.. uploads.Select(upload => new FieldFile(upload.OriginalName, upload.SizeBytes))];
        }

        var offered = await OfferedAsync(slot, others, resolving, cancellationToken);
        var found = FieldValues.Check(slot.Type, slot.Field, slot.Read, new FieldCheckInput(isRequired, today) { Files = files, Values = others, Options = offered });
        errors.AddRange(found.Errors.Select(error => new SubmissionError(key, row, error)));
        errors.AddRange(found.Checks.Select(check => new SubmissionError(key, row, FieldMessageKinds.None, Check: check)));
        if (!found.Errors.IsEmpty || !found.Checks.IsEmpty)
        {
            return;
        }

        if (slot.Type.Declaration.Features.HasFlag(FieldFeatures.Places))
        {
            resolving.Places.AddRange(Places(slot, row, offered));
        }

        if (Unique(slot, row) is { } unique)
        {
            resolving.Unique.Add(unique);
        }
    }

    // The answer that stands of a field with a uniquePerForm check (ADR-0091): its values as entries are compared by them,
    // with the indexes of its unique checks; none without such a check or a value, and none for a type that does not
    // support the check: publishing refuses one, but a site may change its types since, and such a type's values may not
    // compare. The registry holds a type that supports it to a storage kind that compares.
    private static SubmissionUnique? Unique(Slot slot, int? row)
    {
        ImmutableArray<int> checks = [.. slot.Field.Validation.Select((check, index) => (check, index)).Where(pair => pair.check is UniquePerFormCheck).Select(pair => pair.index)];
        if (checks.IsEmpty || slot.Values.IsEmpty || !slot.Type.Declaration.Features.HasFlag(FieldFeatures.UniquePerFormCheck))
        {
            return null;
        }

        var storage = slot.Type.Declaration.Storage;
        return new SubmissionUnique(slot.Field.Key, row, checks, [.. slot.Values.Select(value => EntryValueColumns.ComparedBy(storage, slot.Field.Key, value))]);
    }

    // The options with places an answer that stands chose (ADR-0090): of the field's own options, or of those its list or
    // source resolved for it. A negative number of places, which only code can give, leaves none.
    private static IEnumerable<SubmissionPlace> Places(Slot slot, int? row, IReadOnlyList<FormBuilderOption>? offered)
    {
        var limits = slot.Field.Options is { Source: OptionSource.Items } own
            ? own.Items.Where(item => item.Places is not null).Select(item => (item.Value, Places: item.Places!.Value))
            : (offered ?? []).Where(option => option.Places is not null).Select(option => (option.Value, Places: option.Places!.Value));
        var places = limits.ToDictionary(limit => limit.Value, limit => Math.Max(0, limit.Places), StringComparer.Ordinal);
        return Texts(slot.Values).Where(places.ContainsKey).Select(value => new SubmissionPlace(slot.Field.Key, row, value, places[value]));
    }

    // The form-level validation rules whose message shows at the field or one of its grid rows.
    private static void Messages(ImmutableArray<SubmissionError>.Builder errors, IReadOnlyDictionary<string, IReadOnlyList<string>> messages, FormField field, int? row)
    {
        foreach (var key in field.Rows.Select(gridRow => gridRow.Key).Prepend(field.Key))
        {
            if (messages.TryGetValue(DefinitionLogic.Id(key), out var rules))
            {
                errors.AddRange(rules.Select(rule => new SubmissionError(key, row, FieldMessageKinds.None, Rule: Guid.Parse(rule))));
            }
        }
    }

    // The values a matchesField check compares with, as the rules see them: a shown field's own, and a repeatable group's
    // child's across the rows it is shown in; a hidden field has none.
    private static Dictionary<Guid, ImmutableArray<string>> Others(List<Slot> slots, FormState state, HashSet<string> hidden)
    {
        var others = new Dictionary<Guid, ImmutableArray<string>>();
        foreach (var slot in slots.Where(slot => !hidden.Contains(DefinitionLogic.Id(slot.Field.Key))))
        {
            if (!slot.IsGroup)
            {
                others[slot.Field.Key] = Texts(ValuesOf(slot, state.Calculations));
                continue;
            }

            var rows = RowsOf(state, DefinitionLogic.Id(slot.Field.Key));
            foreach (var child in slot.Field.Fields)
            {
                var texts = ImmutableArray.CreateBuilder<string>();
                for (var row = 0; row < slot.Rows.Count && row < rows.Count; row++)
                {
                    if (slot.Rows[row].Find(each => each.Field.Key == child.Key) is { } answer && !rows[row].Hidden.Contains(DefinitionLogic.Id(child.Key)))
                    {
                        texts.AddRange(Texts(ValuesOf(answer, rows[row].Calculations)));
                    }
                }

                others[child.Key] = texts.ToImmutable();
            }
        }

        return others;
    }

    // The values a repeatable group's child's checks compare with in a row: every shown field's (Others), with the row's
    // own children's values in place of every row's.
    private static Dictionary<Guid, ImmutableArray<string>> RowOthers(Dictionary<Guid, ImmutableArray<string>> others, List<Slot> fields, RowState row)
    {
        var rowOthers = new Dictionary<Guid, ImmutableArray<string>>(others);
        foreach (var child in fields)
        {
            rowOthers[child.Field.Key] = row.Hidden.Contains(DefinitionLogic.Id(child.Field.Key)) ? [] : Texts(ValuesOf(child, row.Calculations));
        }

        return rowOthers;
    }

    private static ImmutableArray<string> Texts(ImmutableArray<FieldValue> values) => [.. values.Where(value => value.Row is null).Select(value => value.Text)];

    private static string NameOf(Slot slot) => slot.Field.Alias ?? DefinitionLogic.Id(slot.Field.Key);

    private static IReadOnlyList<RowState> RowsOf(FormState state, string id) => state.Rows.GetValueOrDefault(id) ?? [];

    // The stored answers as merge tags show them, a file's with its name and size, and the fields the rules hid.
    private static MergeTagAnswers ForMergeTags(ImmutableArray<EntryFieldAnswer> answers, FormState state) => new(ForMergeTags(answers), HiddenKeys(state));

    private static IEnumerable<Guid> HiddenKeys(FormState state) => state.Hidden.Select(id => Guid.TryParse(id, out var key) ? key : (Guid?)null).OfType<Guid>();

    private static IEnumerable<MergeTagAnswer> ForMergeTags(ImmutableArray<EntryFieldAnswer> answers) =>
        answers.Select(answer => new MergeTagAnswer(
            answer.Field,
            answer.Values,
            answer.Files.IsEmpty ? null : [.. answer.Files.Select(file => new FieldFile(file.OriginalName, file.SizeBytes))],
            answer.Row));

    // What resolving a submission's options shares: the submission, what was resolved so far by field and parent answers,
    // every field of the form by key, a repeatable group's children among them, as a parent may be one, and the options
    // with places and the answers that must be unique, in the order of the form and its rows.
    private sealed record Resolving(
        PostedSubmission Post,
        Dictionary<(Guid Field, string Parents), FormBuilderResolvedOptions> Resolved,
        Dictionary<Guid, FormField> Fields,
        ImmutableArray<SubmissionPlace>.Builder Places,
        ImmutableArray<SubmissionUnique>.Builder Unique);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The default value of the field '{Field}' renders to a value its type cannot take, so the entry stores none.")]
    private static partial void LogDefaultNotTaken(ILogger logger, string field);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The options of the field '{Field}' could not be resolved, so its answers are taken without being held to them.")]
    private static partial void LogOptionsUnavailable(ILogger logger, string field);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The query gives the field '{Field}' a value its type cannot take, so its default stands in.")]
    private static partial void LogPrefillNotTaken(ILogger logger, string field);

    // A repeatable group's row a child is shown in: the row's fields and its state.
    private readonly record struct ShownRow(List<Slot> Fields, RowState State);

    // One field's answer in the submission: the field and its type, what its type read, and a repeatable group's rows.
    private sealed class Slot(FormField field, IFormBuilderFieldType type)
    {
        public static readonly FieldRead Nothing = new(FieldAnswers.None, [], IsUnreadable: false);

        public FormField Field { get; } = field;

        public IFormBuilderFieldType Type { get; } = type;

        public bool IsGroup => Type.Declaration.Features.HasFlag(FieldFeatures.ChildFields);

        public bool Calculates => Type is CalculationFieldType;

        public FieldMessageKinds? OverLimit { get; set; }

        public FieldRead Read { get; set; } = Nothing;

        public ImmutableArray<FieldValue> Values => Read.IsUnreadable ? [] : Read.Values;

        /// <summary>Whether the answer was over its type's limits, or one its type could not read.</summary>
        public bool IsUnreadable => OverLimit is not null || Read.IsUnreadable;

        public ImmutableArray<FieldMessageKinds> RowErrors { get; set; } = [];

        public List<List<Slot>> Rows { get; } = [];
    }
}
