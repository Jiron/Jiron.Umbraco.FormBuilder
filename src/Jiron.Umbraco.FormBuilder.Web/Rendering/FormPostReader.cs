using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The answers of a form's post without JavaScript (ADR-0100), read by the names its controls carry (ADR-0058):
/// <c>&lt;alias&gt;</c> and <c>&lt;alias&gt;.&lt;part&gt;</c>, a repeatable group's
/// <c>&lt;group&gt;[&lt;row&gt;].&lt;child&gt;</c> as many rows as the page showed, a consent's wording, the context and
/// query values, and a yes or no switch that posts nothing as <c>no</c>, every line break as the runtime sends it. A
/// name is a field's when the form names it so now or did in a published version, as the pipeline reads a page of an
/// older revision (ADR-0002); any other name, and a row past the count, is left out, as a site's markup may add inputs
/// of its own.
/// </summary>
internal sealed class FormPostReader
{
    // Line breaks as a browser posts them, and as the runtime sends them.
    private static readonly string PostedBreak = new([(char)13, (char)10]);
    private static readonly string Break = ((char)10).ToString();

    private readonly Dictionary<string, FormField> _top = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Dictionary<string, FormField>> _children = [];
    private readonly Dictionary<Guid, string> _aliases = [];
    private readonly Dictionary<Guid, Answer> _fields = [];
    private readonly Dictionary<Guid, SortedDictionary<int, Dictionary<Guid, Answer>>> _rows = [];
    private readonly Dictionary<Guid, int> _counts = [];
    private readonly Dictionary<string, string> _context = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _query = new(StringComparer.Ordinal);
    private readonly List<FormField> _order;
    private readonly FieldTypeRegistry _types;

    private FormPostReader(FormDefinition definition, FieldTypeRegistry types)
    {
        _types = types;
        _order = [.. definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Where(field => field.Alias is not null)];
        Names(_top, _order);
        foreach (var group in _order.Where(IsGroup))
        {
            Names(_children[group.Key] = new Dictionary<string, FormField>(StringComparer.Ordinal), group.Fields.Where(child => child.Alias is not null));
        }
    }

    /// <summary>The context values the post carried, by name.</summary>
    public IReadOnlyDictionary<string, string> Context => _context;

    /// <summary>The query values the post carried on (<c>jfb-query-&lt;name&gt;</c>), by name; empty on a first post.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Query => _query;

    /// <summary>Reads a post for a form.</summary>
    /// <param name="posted">The post's names and values.</param>
    /// <param name="definition">The form's live version, whose fields' aliases, now and before, name the answers.</param>
    /// <param name="types">The field types, which say which fields are repeatable groups, consents and switches.</param>
    public static FormPostReader Read(IFormCollection posted, FormDefinition definition, FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(posted);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(types);
        var reader = new FormPostReader(definition, types);
        foreach (var (name, values) in posted)
        {
            reader.Take(name, values);
        }

        reader.Switches();
        return reader;
    }

    /// <summary>
    /// The answers as the pipeline takes them (ADR-0088), by the names the post gave them: each field's outside
    /// repeatable groups, and each group's rows of its children's.
    /// </summary>
    public (IReadOnlyDictionary<string, PostedAnswer> Fields, IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>> Rows) Posted()
    {
        var fields = new Dictionary<string, PostedAnswer>(StringComparer.Ordinal);
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>>(StringComparer.Ordinal);
        foreach (var field in _order)
        {
            if (IsGroup(field))
            {
                if (RowsOf(field.Key) is { Count: > 0 } posted)
                {
                    rows[_aliases[field.Key]] = [.. posted.Select(row => (IReadOnlyDictionary<string, PostedAnswer>)row.ToDictionary(pair => _aliases[pair.Key], pair => pair.Value.Posted(), StringComparer.Ordinal))];
                }
            }
            else if (_fields.TryGetValue(field.Key, out var answer))
            {
                fields[_aliases[field.Key]] = answer.Posted();
            }
        }

        return (fields, rows);
    }

    /// <summary>
    /// The answers as the page renders them again (ADR-0093): each field's by its key, and each repeatable group's rows
    /// of its children's, the texts a browser posts empty left out.
    /// </summary>
    public (Dictionary<Guid, FieldAnswers> Fields, Dictionary<Guid, IReadOnlyList<Dictionary<Guid, FieldAnswers>>> Rows) Shown()
    {
        var fields = new Dictionary<Guid, FieldAnswers>();
        var rows = new Dictionary<Guid, IReadOnlyList<Dictionary<Guid, FieldAnswers>>>();
        foreach (var field in _order)
        {
            if (IsGroup(field))
            {
                rows[field.Key] = [.. RowsOf(field.Key).Select(row => row
                    .Select(pair => (pair.Key, Shown: pair.Value.Shown()))
                    .Where(pair => pair.Shown is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Shown!))];
            }
            else if (_fields.TryGetValue(field.Key, out var answer) && answer.Shown() is { } shown)
            {
                fields[field.Key] = shown;
            }
        }

        return (fields, rows);
    }

    /// <summary>
    /// The consents the post ticked, each with the wording hash it posted: the field's key, and its row (null outside a
    /// repeatable group).
    /// </summary>
    public IEnumerable<(Guid Field, int? Row, string? Hash)> Ticked()
    {
        foreach (var field in _order)
        {
            if (IsConsent(field) && _fields.TryGetValue(field.Key, out var answer) && answer.IsTicked)
            {
                yield return (field.Key, null, answer.Hash);
            }

            if (!IsGroup(field))
            {
                continue;
            }

            var rows = RowsOf(field.Key);
            for (var row = 0; row < rows.Count; row++)
            {
                foreach (var child in field.Fields.Where(IsConsent))
                {
                    if (rows[row].TryGetValue(child.Key, out var ticked) && ticked.IsTicked)
                    {
                        yield return (child.Key, row, ticked.Hash);
                    }
                }
            }
        }
    }

    /// <summary>Unticks a consent whose wording changed since the page showed it (ADR-0100).</summary>
    /// <param name="field">The consent's key.</param>
    /// <param name="row">Its row, or null outside a repeatable group.</param>
    public void Untick(Guid field, int? row)
    {
        if (row is null)
        {
            _fields.GetValueOrDefault(field)?.Untick();
            return;
        }

        foreach (var group in _order.Where(group => group.Fields.Any(child => child.Key == field)))
        {
            if (RowsOf(group.Key).ElementAtOrDefault(row.Value) is { } posted)
            {
                posted.GetValueOrDefault(field)?.Untick();
            }
        }
    }

    // The names of fields: each one's alias now, then the aliases it had, unless another field has one of them now.
    private static void Names(Dictionary<string, FormField> names, IEnumerable<FormField> fields)
    {
        var list = fields.ToList();
        foreach (var field in list)
        {
            names[field.Alias!] = field;
        }

        foreach (var field in list)
        {
            foreach (var former in field.FormerAliases)
            {
                names.TryAdd(former, field);
            }
        }
    }

    // A post's name and values: the package's own names, then a field's or a row's.
    private void Take(string name, StringValues values)
    {
        if (name.StartsWith(FormPostFields.Prefix, StringComparison.Ordinal))
        {
            Own(name, values);
        }
        else if (Path(name) is { } path && AnswerOf(path) is { } answer)
        {
            answer.Take(path.Part, Texts(values));
        }
    }

    // The package's own names: a group's row count, a consent's wording, a context or a query value; any other is left out.
    private void Own(string name, StringValues values)
    {
        if (name.StartsWith(FormPostFields.RowsPrefix, StringComparison.Ordinal))
        {
            var alias = name[FormPostFields.RowsPrefix.Length..];
            if (_top.TryGetValue(alias, out var group) && IsGroup(group) && Claim(group, alias)
                && int.TryParse(values.LastOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                _counts[group.Key] = Math.Min(count, RepeaterFieldType.MostRows);
            }
        }
        else if (name.StartsWith(FormModels.ConsentPrefix, StringComparison.Ordinal))
        {
            if (Path(name[FormModels.ConsentPrefix.Length..]) is { Part: null } path && AnswerOf(path) is { } answer)
            {
                answer.Take(ConsentFieldType.WordingPart, Texts(values));
            }
        }
        else if (name.StartsWith(FormModels.ContextPrefix, StringComparison.Ordinal))
        {
            _context[name[FormModels.ContextPrefix.Length..]] = values.LastOrDefault() ?? string.Empty;
        }
        else if (name.StartsWith(FormPostFields.QueryPrefix, StringComparison.Ordinal))
        {
            _query[name[FormPostFields.QueryPrefix.Length..]] = [.. values.OfType<string>()];
        }
    }

    // The answer a path names, made when it is the first of its field; null for a name of no field, or a field named
    // by another of its aliases before.
    private Answer? AnswerOf(PostedPath path)
    {
        if (path.Group is null)
        {
            return _top.TryGetValue(path.Alias, out var field) && !IsGroup(field) && Claim(field, path.Alias) ? Made(_fields, field.Key) : null;
        }

        if (!_top.TryGetValue(path.Group, out var group) || !IsGroup(group) || !_children[group.Key].TryGetValue(path.Alias, out var child)
            || !Claim(group, path.Group) || !Claim(child, path.Alias))
        {
            return null;
        }

        if (!_rows.TryGetValue(group.Key, out var rows))
        {
            _rows[group.Key] = rows = [];
        }

        if (!rows.TryGetValue(path.Row, out var row))
        {
            rows[path.Row] = row = [];
        }

        return Made(row, child.Key);
    }

    private static Answer Made(Dictionary<Guid, Answer> answers, Guid key)
    {
        if (!answers.TryGetValue(key, out var answer))
        {
            answers[key] = answer = new Answer();
        }

        return answer;
    }

    // Whether a field's answers are posted by this alias: the first name of the field the post holds says which.
    private bool Claim(FormField field, string alias) => _aliases.TryAdd(field.Key, alias) || _aliases[field.Key] == alias;

    // A group's rows, as many as the page showed (or, for a post that does not say, as far as it names one), each row its
    // children's answers; a row whose controls posted nothing is empty.
    private List<Dictionary<Guid, Answer>> RowsOf(Guid group)
    {
        var posted = _rows.GetValueOrDefault(group);
        var count = _counts.TryGetValue(group, out var shown) ? shown : posted is { Count: > 0 } ? posted.Keys.Max() + 1 : 0;
        return [.. Enumerable.Range(0, count).Select(row => posted?.GetValueOrDefault(row) ?? [])];
    }

    // A yes or no switch posts nothing while it is off, which the runtime sends as no (ADR-0096): outside groups, and in
    // each row the page showed. The page holds every page of the form, so every switch was on the posted page.
    private void Switches()
    {
        foreach (var field in _order)
        {
            if (IsSwitch(field) && Claim(field, _aliases.GetValueOrDefault(field.Key) ?? field.Alias!))
            {
                Made(_fields, field.Key).SwitchedOff();
            }

            if (!IsGroup(field))
            {
                continue;
            }

            var count = RowsOf(field.Key).Count;
            foreach (var child in field.Fields.Where(IsSwitch))
            {
                Claim(child, _aliases.GetValueOrDefault(child.Key) ?? child.Alias!);
                for (var row = 0; row < count; row++)
                {
                    if (!_rows.TryGetValue(field.Key, out var rows))
                    {
                        _rows[field.Key] = rows = [];
                    }

                    if (!rows.TryGetValue(row, out var answers))
                    {
                        rows[row] = answers = [];
                    }

                    Made(answers, child.Key).SwitchedOff();
                }
            }
        }
    }

    private bool IsGroup(FormField field) => _types.FindType(field.Type)?.Declaration.Features.HasFlag(FieldFeatures.ChildFields) == true;

    private bool IsConsent(FormField field) => _types.FindType(field.Type) is ConsentFieldType;

    private bool IsSwitch(FormField field) =>
        field.Alias is not null && _types.FindType(field.Type) is YesNoFieldType yesNo && yesNo.ReadSettings(field.Settings).Style == YesNoStyle.Switch;

    // A field's name: "alias", "alias.part", "group[row].child" or "group[row].child.part"; null for any other.
    private static PostedPath? Path(string name)
    {
        var open = name.IndexOf('[', StringComparison.Ordinal);
        if (open < 0)
        {
            var (alias, part) = Split(name);
            return alias.Length > 0 ? new PostedPath(null, 0, alias, part) : null;
        }

        var close = name.IndexOf(']', open);
        if (open == 0 || close < 0 || close + 1 >= name.Length || name[close + 1] != '.'
            || !int.TryParse(name.AsSpan(open + 1, close - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var row)
            || row >= RepeaterFieldType.MostRows)
        {
            return null;
        }

        var (child, childPart) = Split(name[(close + 2)..]);
        return child.Length > 0 ? new PostedPath(name[..open], row, child, childPart) : null;
    }

    // An alias and the part after its first dot, if any; a part that is empty is none.
    private static (string Alias, string? Part) Split(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? (name, null) : (name[..dot], dot < name.Length - 1 ? name[(dot + 1)..] : null);
    }

    // The posted texts, each line break as the runtime sends it.
    private static List<string?> Texts(StringValues values) => [.. values.Select(value => value?.Replace(PostedBreak, Break, StringComparison.Ordinal))];

    // Where a posted name is: a field outside groups, or a group's row's child; and the part, if any.
    private sealed record PostedPath(string? Group, int Row, string Alias, string? Part);

    // One field's posted texts and its parts' texts.
    private sealed class Answer
    {
        private readonly List<string?> _values = [];
        private readonly Dictionary<string, List<string?>> _parts = new(StringComparer.Ordinal);

        public bool IsTicked => _values.Contains(FieldAnswerDefaults.Ticked, StringComparer.Ordinal);

        public string? Hash => _parts.GetValueOrDefault(ConsentFieldType.WordingPart)?.FirstOrDefault(text => !string.IsNullOrEmpty(text));

        public void Take(string? part, List<string?> texts)
        {
            if (part is null)
            {
                _values.AddRange(texts);
            }
            else if (_parts.TryGetValue(part, out var given))
            {
                given.AddRange(texts);
            }
            else
            {
                _parts[part] = texts;
            }
        }

        public void SwitchedOff()
        {
            if (!_values.Any(value => !string.IsNullOrEmpty(value)))
            {
                _values.Clear();
                _values.Add(YesNoFieldType.No);
            }
        }

        public void Untick() => _values.Clear();

        public PostedAnswer Posted() => new(
            [.. _values],
            _parts.Count == 0 ? null : _parts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string?>)[.. pair.Value], StringComparer.Ordinal));

        public FieldAnswers? Shown()
        {
            List<string> values = [.. _values.Where(value => !string.IsNullOrEmpty(value)).Cast<string>()];
            var parts = _parts
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => new KeyValuePair<string, IEnumerable<string>>(pair.Key, [.. pair.Value.Where(text => !string.IsNullOrEmpty(text)).Cast<string>()]))
                .Where(pair => pair.Value.Any())
                .ToList();
            return values.Count == 0 && parts.Count == 0 ? null : new FieldAnswers(values, parts);
        }
    }
}
