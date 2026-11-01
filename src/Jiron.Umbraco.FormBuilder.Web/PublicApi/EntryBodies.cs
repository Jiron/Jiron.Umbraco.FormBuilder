using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// Reads the JSON body of a submission (ADR-0088), of a page to check and of a receipt (ADR-0089) strictly: UTF-8
/// throughout, at most <see cref="MaxDepth"/> levels, an object of the known members each once, a value of the shape its
/// member takes, and no lone surrogate, escaped or not. Anything else is named by where it is, as
/// <c>400 invalid_request</c> names it.
/// </summary>
internal static class EntryBodies
{
    /// <summary>The deepest a body nests.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most places a body's list of errors names.</summary>
    public const int MaxInvalidPaths = 100;

    /// <summary>The longest challenge, in UTF-16 code units, as an upload's challenge part.</summary>
    public const int MaxChallengeLength = 4_096;

    /// <summary>The longest captcha token, in UTF-16 code units.</summary>
    public const int MaxCaptchaTokenLength = 8_192;

    /// <summary>The longest answer to a puzzle a body may post (ADR-0141); a longer one is no number.</summary>
    public const int MaxProofLength = 64;

    /// <summary>Where a body that is not JSON, or not an object, goes wrong.</summary>
    public const string Body = "body";

    /// <summary>
    /// The submission a body holds for a form, or null with where it cannot be read; the transport's upload tokens are
    /// the caller's to add.
    /// </summary>
    /// <param name="body">The body's bytes.</param>
    /// <param name="form">The form's key, from the route.</param>
    /// <param name="invalid">Receives where the body cannot be read.</param>
    public static SubmissionRequest? Read(byte[] body, Guid form, List<string> invalid) =>
        Submission(body, form, page: false, invalid) is ({ } request, _) ? request : null;

    /// <summary>
    /// The answers a page's body holds for a form, and the page's key (ADR-0089): a submission's members with
    /// <c>pageKey</c> and without <c>submissionId</c> and <c>captchaToken</c>; null with where it cannot be read. The
    /// transport's upload tokens are the caller's to add.
    /// </summary>
    /// <param name="body">The body's bytes.</param>
    /// <param name="form">The form's key, from the route.</param>
    /// <param name="invalid">Receives where the body cannot be read.</param>
    public static (SubmissionRequest Request, Guid Page)? ReadPage(byte[] body, Guid form, List<string> invalid) =>
        Submission(body, form, page: true, invalid) is ({ } request, { } page) ? (request, page) : null;

    /// <summary>The submission id a receipt's body names (ADR-0089), or null with where the body cannot be read.</summary>
    /// <param name="body">The body's bytes: <c>submissionId</c>, and the optional <c>challenge</c>.</param>
    /// <param name="invalid">Receives where the body cannot be read.</param>
    public static (Guid? SubmissionId, string? Challenge) ReadReceipt(byte[] body, List<string> invalid)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(invalid);
        using var document = Parse(body, invalid);
        if (document is null)
        {
            return default;
        }

        Guid? submissionId = null;
        string? challenge = null;
        foreach (var (name, value) in Members(document.RootElement, string.Empty, invalid))
        {
            switch (name)
            {
                case "submissionId":
                    submissionId = Key(value, name, invalid);
                    break;
                case "challenge":
                    challenge = Text(value, name, invalid, MaxChallengeLength);
                    break;
                default:
                    Add(invalid, name);
                    break;
            }
        }

        if (submissionId is null)
        {
            Add(invalid, "submissionId");
        }

        return invalid.Count > 0 ? default : (submissionId, challenge);
    }

    /// <summary>The earlier challenge a request for a new one names and the page it is for, each or null; any other member is invalid.</summary>
    /// <param name="body">The body's bytes.</param>
    /// <param name="invalid">Receives where the body cannot be read.</param>
    public static (string? Previous, Guid? ContentKey) ReadChallengeRequest(byte[] body, List<string> invalid)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(invalid);
        using var document = Parse(body, invalid);
        if (document is null)
        {
            return default;
        }

        string? previous = null;
        Guid? contentKey = null;
        foreach (var (name, value) in Members(document.RootElement, string.Empty, invalid))
        {
            switch (name)
            {
                case "previous":
                    previous = Text(value, name, invalid, MaxChallengeLength);
                    break;
                case "contentKey":
                    contentKey = Key(value, name, invalid);
                    break;
                default:
                    Add(invalid, name);
                    break;
            }
        }

        return (previous, contentKey);
    }

    // A submission's body, or a page's with the page's key.
    private static (SubmissionRequest? Request, Guid? Page) Submission(byte[] body, Guid form, bool page, List<string> invalid)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(invalid);
        using var document = Parse(body, invalid);
        return document is null ? default : Read(document.RootElement, form, page, invalid);
    }

    // The body as a JSON object, or null with where it goes wrong: bytes that are not UTF-8, text that is not JSON or
    // nests deeper than MaxDepth, or a value that is not an object.
    private static JsonDocument? Parse(byte[] body, List<string> invalid)
    {
        if (!System.Text.Unicode.Utf8.IsValid(body))
        {
            Add(invalid, Body);
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            Add(invalid, Body);
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();
        Add(invalid, Body);
        return null;
    }

    private static (SubmissionRequest? Request, Guid? Page) Read(JsonElement root, Guid form, bool page, List<string> invalid)
    {
        int? revision = null;
        Guid? submissionId = null;
        Guid? pageKey = null;
        Guid? contentKey = null;
        string? culture = null;
        var answers = new Answers();
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        string? challenge = null;
        string? honeypot = null;
        string? proof = null;
        string? captchaToken = null;
        foreach (var (name, value) in Members(root, string.Empty, invalid))
        {
            switch (name)
            {
                case "values" or "consents" or "query" or "context" when value.ValueKind == JsonValueKind.Null:
                    break;
                case "revision":
                    revision = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 1 ? number : Invalid(invalid, name, (int?)null);
                    break;
                case "submissionId" when !page:
                    submissionId = Key(value, name, invalid);
                    break;
                case "pageKey" when page:
                    pageKey = Key(value, name, invalid);
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
                    challenge = Text(value, name, invalid, MaxChallengeLength);
                    break;
                case "captchaToken" when !page:
                    captchaToken = Text(value, name, invalid, MaxCaptchaTokenLength);
                    break;
                case "honeypot":
                    honeypot = Text(value, name, invalid, int.MaxValue);
                    break;
                case "proofOfWork" when !page:
                    proof = Text(value, name, invalid, MaxProofLength);
                    break;
                default:
                    Add(invalid, name);
                    break;
            }
        }

        if (revision is null)
        {
            Add(invalid, "revision");
        }

        if (page && pageKey is null)
        {
            Add(invalid, "pageKey");
        }

        return invalid.Count > 0 ? default : (new SubmissionRequest
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
            Challenge = challenge,
            Honeypot = honeypot,
            ProofOfWork = proof,
            CaptchaToken = captchaToken,
        }, pageKey);
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
            Add(invalid, "values");
            return;
        }

        foreach (var (name, value) in Members(values, "values", invalid))
        {
            var path = Join("values", name);
            if (Split(name) is not (string alias, var part))
            {
                Add(invalid, path);
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
                Add(invalid, path);
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
                    Add(invalid, Join(rowPath, name));
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
            Add(invalid, "consents");
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
                Add(invalid, path);
            }
        }
    }

    // The page's query values: a text or a list of texts by name.
    private static void Query(JsonElement values, Dictionary<string, IReadOnlyList<string>> query, List<string> invalid)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            Add(invalid, "query");
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
                Add(invalid, Join("query", name));
            }
        }
    }

    // The context values: a text by name.
    private static void Context(JsonElement values, Dictionary<string, string> context, List<string> invalid)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            Add(invalid, "context");
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
                Add(invalid, Join("context", name));
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

    // A UUID in its usual form, or null for null; a value of another shape, or a string with a lone surrogate, is named.
    private static Guid? Key(JsonElement value, string name, List<string> invalid) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when Guid.TryParseExact(JsonPaths.TryGetString(value), "D", out var key) => key,
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

    // Each once, and at most a hundred: a body's list of errors costs what a few of them cost, however many it could name.
    private static void Add(List<string> invalid, string path)
    {
        if (invalid.Count < MaxInvalidPaths && !invalid.Contains(path))
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
