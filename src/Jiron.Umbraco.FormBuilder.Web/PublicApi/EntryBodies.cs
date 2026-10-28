using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// Reads the JSON body of a submission (ADR-0088) strictly: UTF-8 throughout, at most <see cref="MaxDepth"/> levels,
/// an object of the known members each once, a value of the shape its member takes, and no lone surrogate, escaped or
/// not. Anything else is named by where it is, as <c>400 invalid_request</c> names it.
/// </summary>
internal static class EntryBodies
{
    /// <summary>The deepest a body nests.</summary>
    public const int MaxDepth = 64;

    /// <summary>The longest challenge, in UTF-16 code units, as an upload's challenge part.</summary>
    public const int MaxChallengeLength = 4_096;

    /// <summary>The longest captcha token, in UTF-16 code units.</summary>
    public const int MaxCaptchaTokenLength = 8_192;

    /// <summary>Where a body that is not JSON, or not an object, goes wrong.</summary>
    public const string Body = "body";

    /// <summary>
    /// The submission a body holds for a form, or null with where it cannot be read; the transport's upload tokens are
    /// the caller's to add.
    /// </summary>
    /// <param name="body">The body's bytes.</param>
    /// <param name="form">The form's key, from the route.</param>
    /// <param name="invalid">Receives where the body cannot be read.</param>
    public static SubmissionRequest? Read(byte[] body, Guid form, List<string> invalid)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(invalid);
        if (!System.Text.Unicode.Utf8.IsValid(body))
        {
            invalid.Add(Body);
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            invalid.Add(Body);
            return null;
        }

        using (document)
        {
            return document.RootElement.ValueKind == JsonValueKind.Object ? Read(document.RootElement, form, invalid) : Fail(invalid, Body);
        }
    }

    private static SubmissionRequest? Read(JsonElement root, Guid form, List<string> invalid)
    {
        int? revision = null;
        Guid? submissionId = null;
        Guid? contentKey = null;
        string? culture = null;
        var answers = new Answers();
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in Members(root, string.Empty, invalid))
        {
            switch (name)
            {
                case "values" or "consents" or "query" or "context" when value.ValueKind == JsonValueKind.Null:
                    break;
                case "revision":
                    revision = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 1 ? number : Invalid(invalid, name, (int?)null);
                    break;
                case "submissionId":
                    submissionId = Key(value, name, invalid);
                    break;
                case "contentKey":
                    contentKey = Key(value, name, invalid);
                    break;
                case "culture":
                    culture = Text(value, name, invalid, FormPages.MaxCultureLength) is { } text && FormPages.IsCulture(text) ? text : Invalid(invalid, name, (string?)null, value);
                    break;
                case "values":
                    Values(value, answers, invalid);
                    break;
                case "consents":
                    Consents(value, answers, invalid);
                    break;
                case "query":
                    Query(value, query, invalid);
                    break;
                case "context":
                    Context(value, context, invalid);
                    break;
                case "challenge":
                    _ = Text(value, name, invalid, MaxChallengeLength);
                    break;
                case "captchaToken":
                    _ = Text(value, name, invalid, MaxCaptchaTokenLength);
                    break;
                default:
                    invalid.Add(name);
                    break;
            }
        }

        if (revision is null && !invalid.Contains("revision"))
        {
            invalid.Add("revision");
        }

        return invalid.Count > 0 ? null : new SubmissionRequest
        {
            Form = form,
            Revision = revision!.Value,
            SubmissionId = submissionId,
            Culture = culture,
            ContentKey = contentKey,
            Fields = answers.Fields(),
            Rows = answers.Rows(),
            Query = query,
            Context = context,
        };
    }

    // The members of an object, each name once; a name with a lone surrogate, or one given twice, is named once.
    private static IEnumerable<(string Name, JsonElement Value)> Members(JsonElement value, string path, List<string> invalid)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var name = JsonPaths.TryGetName(property);
            if (name is null || !seen.Add(name))
            {
                Add(invalid, name is null ? (path.Length == 0 ? Body : path) : Join(path, name));
                continue;
            }

            yield return (name, property.Value);
        }
    }

    // The answers by alias: "alias" and "alias.part" hold texts, and a repeatable group's alias an array of rows.
    private static void Values(JsonElement values, Answers answers, List<string> invalid)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            Fail(invalid, "values");
            return;
        }

        foreach (var (name, value) in Members(values, "values", invalid))
        {
            var path = Join("values", name);
            if (Split(name) is not (string alias, var part))
            {
                invalid.Add(path);
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object))
            {
                if (part is not null || !answers.TakeRows(alias, Rows(value, path, invalid)))
                {
                    Add(invalid, path);
                }

                continue;
            }

            if (Texts(value) is not { } texts || !answers.Take(alias, part, texts))
            {
                invalid.Add(path);
            }
        }
    }

    // A repeatable group's rows, each an object of its children's "alias" and "alias.part".
    private static List<Dictionary<string, Answer>> Rows(JsonElement rows, string path, List<string> invalid)
    {
        var list = new List<Dictionary<string, Answer>>();
        var index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var rowPath = string.Create(CultureInfo.InvariantCulture, $"{path}[{index++}]");
            var answers = new Dictionary<string, Answer>(StringComparer.Ordinal);
            foreach (var (name, value) in Members(row, rowPath, invalid))
            {
                if (Split(name) is not (string alias, var part) || Texts(value) is not { } texts || !Answer.Of(answers, alias).Take(part, texts))
                {
                    invalid.Add(Join(rowPath, name));
                }
            }

            list.Add(answers);
        }

        return list;
    }

    // Each ticked consent's wording hash by its alias, the part "wording" of its answer (ADR-0058).
    private static void Consents(JsonElement consents, Answers answers, List<string> invalid)
    {
        if (consents.ValueKind != JsonValueKind.Object)
        {
            Fail(invalid, "consents");
            return;
        }

        foreach (var (alias, value) in Members(consents, "consents", invalid))
        {
            var path = Join("consents", alias);
            if (value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (Split(alias) is not (_, null) || JsonPaths.TryGetString(value) is not { } hash || !answers.Take(alias, ConsentFieldType.WordingPart, [hash]))
            {
                invalid.Add(path);
            }
        }
    }

    // The page's query values: a text or a list of texts by name.
    private static void Query(JsonElement values, Dictionary<string, IReadOnlyList<string>> query, List<string> invalid)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            Fail(invalid, "query");
            return;
        }

        foreach (var (name, value) in Members(values, "query", invalid))
        {
            if (value.ValueKind != JsonValueKind.Null && Texts(value) is { } texts)
            {
                query[name] = [.. texts.OfType<string>()];
            }
            else if (value.ValueKind != JsonValueKind.Null)
            {
                invalid.Add(Join("query", name));
            }
        }
    }

    // The context values: a text by name.
    private static void Context(JsonElement values, Dictionary<string, string> context, List<string> invalid)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            Fail(invalid, "context");
            return;
        }

        foreach (var (name, value) in Members(values, "context", invalid))
        {
            if (value.ValueKind == JsonValueKind.String && JsonPaths.TryGetString(value) is { } text)
            {
                context[name] = text;
            }
            else if (value.ValueKind != JsonValueKind.Null)
            {
                invalid.Add(Join("context", name));
            }
        }
    }

    // An answer's texts: a string, a number as it is written, true or false, nothing for null, or a list of strings;
    // null for any other shape or a string with a lone surrogate.
    private static List<string?>? Texts(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return JsonPaths.TryGetString(value) is { } text ? [text] : null;
            case JsonValueKind.Number:
                return [value.GetRawText()];
            case JsonValueKind.True:
                return ["true"];
            case JsonValueKind.False:
                return ["false"];
            case JsonValueKind.Null:
                return [];
            case JsonValueKind.Array:
                var texts = new List<string?>();
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || JsonPaths.TryGetString(item) is not { } itemText)
                    {
                        return null;
                    }

                    texts.Add(itemText);
                }

                return texts;
            default:
                return null;
        }
    }

    // An alias and, after its first dot, a part; null when either is empty.
    private static (string Alias, string? Part)? Split(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return name.Length > 0 ? (name, null) : null;
        }

        return dot > 0 && dot < name.Length - 1 ? (name[..dot], name[(dot + 1)..]) : null;
    }

    private static Guid? Key(JsonElement value, string name, List<string> invalid) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when Guid.TryParseExact(value.GetString(), "D", out var key) => key,
        _ => Invalid(invalid, name, (Guid?)null),
    };

    // A text of at most this length, or null for null; a text of another shape or length is named.
    private static string? Text(JsonElement value, string name, List<string> invalid, int maxLength)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && JsonPaths.TryGetString(value) is { } text && text.Length <= maxLength
            ? text
            : Invalid(invalid, name, (string?)null);
    }

    // A value that cannot be read, named once; a null culture is none.
    private static T Invalid<T>(List<string> invalid, string name, T none, JsonElement? value = null)
    {
        if (value is not { ValueKind: JsonValueKind.Null })
        {
            Add(invalid, name);
        }

        return none;
    }

    private static SubmissionRequest? Fail(List<string> invalid, string path)
    {
        Add(invalid, path);
        return null;
    }

    private static void Add(List<string> invalid, string path)
    {
        if (!invalid.Contains(path))
        {
            invalid.Add(path);
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    // One answer as it is read: its texts and its parts' texts, each given once.
    private sealed class Answer
    {
        private List<string?>? _values;
        private Dictionary<string, IReadOnlyList<string?>>? _parts;

        public static Answer Of(Dictionary<string, Answer> answers, string alias)
        {
            if (!answers.TryGetValue(alias, out var answer))
            {
                answers[alias] = answer = new Answer();
            }

            return answer;
        }

        public bool Take(string? part, List<string?> texts)
        {
            if (part is null)
            {
                // Members gives each name once, so an answer's own texts come once.
                _values = texts;
                return true;
            }

            _parts ??= new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);
            return _parts.TryAdd(part, texts);
        }

        public PostedAnswer Posted() => new(_values ?? [], _parts);
    }

    // The answers of the fields outside repeatable groups and the groups' rows, an alias in one of them only.
    private sealed class Answers
    {
        private readonly Dictionary<string, Answer> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Dictionary<string, Answer>>> _rows = new(StringComparer.Ordinal);

        public bool Take(string alias, string? part, List<string?> texts) => !_rows.ContainsKey(alias) && Answer.Of(_fields, alias).Take(part, texts);

        public bool TakeRows(string alias, List<Dictionary<string, Answer>> rows) => !_fields.ContainsKey(alias) && _rows.TryAdd(alias, rows);

        public Dictionary<string, PostedAnswer> Fields() => _fields.ToDictionary(pair => pair.Key, pair => pair.Value.Posted(), StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>> Rows() =>
            _rows.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>)[.. pair.Value.Select(row => (IReadOnlyDictionary<string, PostedAnswer>)row.ToDictionary(item => item.Key, item => item.Value.Posted(), StringComparer.Ordinal))],
                StringComparer.Ordinal);
    }
}
