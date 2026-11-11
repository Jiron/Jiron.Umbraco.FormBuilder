using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;

/// <summary>
/// A scripted, strict chat model (ADR-0018), ported from Tandem's. It is as strict as a real API: after hoisting system
/// messages out it refuses an empty conversation, one that does not end with the user, a blank user message, and tools
/// with invalid or duplicate names. It answers by the schema the request names in its <c>title</c>: a draft made from the
/// editor's text, a translation that marks each text with the language, or suggestions for the field. A directive in the
/// request's block scripts a test's own answer instead: <c>say:</c> a reply as it is, <c>draft:</c> a draft's JSON,
/// <c>error:</c> a provider's failure (<c>provider</c>, <c>timeout</c>, <c>http429</c>, <c>http500</c>), <c>slow:</c> a
/// delay in milliseconds. Every message's whole text goes to the journal.
/// </summary>
public sealed partial class ScriptedChatClient(FakeChatJournal journal, ILogger<ScriptedChatClient> logger) : IChatClient
{
    /// <summary>The prefix of every exception the fake throws itself, so tests can tell them apart.</summary>
    public const string ErrorPrefix = "JFB_FAKE";

    [GeneratedRegex("^[a-zA-Z0-9_-]{1,64}$")]
    private static partial Regex ValidToolName();

    [GeneratedRegex(@"(?m)^\s*say:(?<text>.*)$")]
    private static partial Regex SayDirective();

    [GeneratedRegex(@"draft:(?<json>\{[\s\S]*\})")]
    private static partial Regex DraftDirective();

    [GeneratedRegex(@"error:(?<kind>[a-z0-9]+)")]
    private static partial Regex ErrorDirective();

    [GeneratedRegex(@"slow:(?<ms>\d{1,5})")]
    private static partial Regex SlowDirective();

    [GeneratedRegex(@"\((?<culture>[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\):")]
    private static partial Regex TargetCulture();

    [GeneratedRegex(@"^(?<question>[^(]*?)\s*\((?<options>[^)]*)\)\s*\??\s*$")]
    private static partial Regex QuestionWithOptions();

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var all = messages.ToList();
        ValidateTools(options);
        var list = Validate(all);
        var schema = SchemaOf(options);
        Record($"request: {all.Count} messages, roles [{string.Join(",", all.Select(message => message.Role.Value))}], schema {schema ?? "none"}");
        foreach (var message in all)
        {
            Record($"message {message.Role.Value}: {message.Text}");
        }

        var last = list[^1].Text;
        if (SlowDirective().Match(last) is { Success: true } slow)
        {
            await Task.Delay(int.Parse(slow.Groups["ms"].Value, CultureInfo.InvariantCulture), cancellationToken);
        }

        if (ErrorDirective().Match(last) is { Success: true } error)
        {
            Record($"error directive: {error.Groups["kind"].Value}");
            throw ErrorFor(error.Groups["kind"].Value);
        }

        var answer = SayDirective().Match(last) is { Success: true } say
            ? say.Groups["text"].Value.Trim()
            : schema switch
            {
                AIPrompts.DraftSchemaName => DraftDirective().Match(last) is { Success: true } draft ? draft.Groups["json"].Value : Draft(AIPrompts.BlockOf(last, AIPrompts.EditorTextTag) ?? string.Empty),
                AIPrompts.TranslationSchemaName => Translation(last),
                AIPrompts.SuggestionsSchemaName => Suggestions(AIPrompts.BlockOf(last, AIPrompts.FieldTag) ?? "{}"),
                _ => $"Fake answer to: {last}",
            };
        Record($"answered {schema ?? "text"}");
        var input = Math.Max(1, all.Sum(message => message.Text.Length) / 4);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
        {
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = Math.Max(1, answer.Length / 4), TotalTokenCount = input + Math.Max(1, answer.Length / 4) },
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// A draft from the editor's text, as a person would sketch it: the first line names the form, every line with a
    /// question mark is a question (with options in brackets, split by slashes, a choice of them), and a name and an
    /// email address come first.
    /// </summary>
    public static string Draft(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = lines.FirstOrDefault(line => !line.Contains('?', StringComparison.Ordinal))?.TrimEnd('.');
        var fields = new JsonArray(Field("text", "Name", required: true), Field("email", "Email address", required: true));
        foreach (var line in lines.Where(line => line.Contains('?', StringComparison.Ordinal)))
        {
            var match = QuestionWithOptions().Match(line);
            var options = match.Success ? match.Groups["options"].Value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
            fields.Add(options.Length >= 2
                ? Field("radio", match.Groups["question"].Value.Trim().TrimEnd('?') + "?", required: true, options: options)
                : Field("textarea", line, required: false));
        }

        if (fields.Count == 2)
        {
            fields.Add(Field("textarea", "Message", required: false));
        }

        return new JsonObject
        {
            ["name"] = string.IsNullOrEmpty(first) ? "Drafted form" : first[..Math.Min(first.Length, 60)],
            ["pages"] = new JsonArray(new JsonObject
            {
                ["title"] = null,
                ["groups"] = new JsonArray(new JsonObject { ["title"] = null, ["fields"] = fields }),
            }),
        }.ToJsonString();
    }

    /// <summary>A translation that marks every text with the language: <c>Name (de)</c>, inside the last tag of HTML.</summary>
    public static string Translation(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var culture = TargetCulture().Match(message) is { Success: true } target ? target.Groups["culture"].Value.Split('-')[0] : "xx";
        var texts = new JsonArray();
        if (JsonNode.Parse(AIPrompts.BlockOf(message, AIPrompts.FormTextsTag) ?? "[]") is JsonArray sent)
        {
            foreach (var item in sent.OfType<JsonObject>())
            {
                var text = item["text"]?.GetValue<string>() ?? string.Empty;
                var close = text.EndsWith('>') ? text.LastIndexOf("</", StringComparison.Ordinal) : -1;
                var marked = close > 0 ? $"{text[..close]} ({culture}){text[close..]}" : $"{text} ({culture})";
                texts.Add(new JsonObject { ["id"] = item["id"]?.GetValue<string>(), ["text"] = marked });
            }
        }

        return new JsonObject { ["texts"] = texts }.ToJsonString();
    }

    /// <summary>Suggestions: a help text that names the label, and for a short capitalised label, that label written out.</summary>
    public static string Suggestions(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var label = (JsonNode.Parse(field) as JsonObject)?["label"]?.GetValue<string>();
        var improvements = new JsonArray();
        if (label is { Length: > 0 and <= 4 } && label.All(char.IsUpper))
        {
            improvements.Add(new JsonObject { ["setting"] = "label", ["text"] = $"{label} written out", ["reason"] = "An abbreviation is hard to understand." });
        }

        return new JsonObject
        {
            ["help"] = label is null ? "Fake help." : $"Fake help for {label}.",
            ["improvements"] = improvements,
        }.ToJsonString();
    }

    private static JsonObject Field(string type, string label, bool required, string[]? options = null) => new()
    {
        ["type"] = type,
        ["label"] = label,
        ["help"] = null,
        ["placeholder"] = null,
        ["required"] = required,
        ["options"] = new JsonArray([.. (options ?? []).Select(option => (JsonNode)option)]),
    };

    private static string? SchemaOf(ChatOptions? options) =>
        options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } && schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
            ? title.GetString()
            : null;

    private static Exception ErrorFor(string kind) => kind switch
    {
        "timeout" => new TimeoutException($"{ErrorPrefix} provider timeout"),
        "http429" => new HttpRequestException($"{ErrorPrefix} rate limited", null, HttpStatusCode.TooManyRequests),
        "http500" => new HttpRequestException($"{ErrorPrefix} server error", null, HttpStatusCode.InternalServerError),
        _ => new InvalidOperationException($"{ErrorPrefix} provider error: {kind}"),
    };

    private void ValidateTools(ChatOptions? options)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in options?.Tools ?? [])
        {
            if (!ValidToolName().IsMatch(tool.Name))
            {
                Refuse($"the tool name '{tool.Name}' does not match ^[a-zA-Z0-9_-]{{1,64}}$");
            }

            if (!declared.Add(tool.Name))
            {
                Refuse($"the tool '{tool.Name}' is declared twice");
            }
        }
    }

    private List<ChatMessage> Validate(List<ChatMessage> all)
    {
        // System messages are hoisted out, as some APIs do; the rules apply to the rest.
        var list = all.Where(message => message.Role != ChatRole.System).ToList();
        if (list.Count == 0)
        {
            Refuse("no messages");
        }

        if (list[^1].Role != ChatRole.User)
        {
            Refuse($"the last message is {list[^1].Role}, not the user");
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Role == ChatRole.User && string.IsNullOrWhiteSpace(list[i].Text))
            {
                Refuse($"message {i} from the user has no text");
            }
        }

        return list;
    }

    private void Refuse(string reason)
    {
        Record($"refused: {reason}");
        throw new InvalidOperationException($"{ErrorPrefix} strict: {reason}");
    }

    private void Record(string entry)
    {
        LogEntry(logger, entry.Length > 200 ? entry[..200] : entry);
        journal.Record(entry);
    }

    // Debug, as a real provider keeps its traffic out of the log.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Fake chat model: {Entry}")]
    private static partial void LogEntry(ILogger logger, string entry);
}
