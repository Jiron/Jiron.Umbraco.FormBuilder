using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a <c>message</c> vector of tests/vectors/messages.json (ADR-0099) against Core: an error's default message, the
/// error summary's heading or a summary link, in the case's culture with the file's formats for it and the site's today
/// of the case's context.
/// </summary>
internal static class MessageVector
{
    public static JsonNode Run(JsonElement root, JsonElement message, EvaluationContext context)
    {
        if (message.TryGetProperty("heading", out var heading))
        {
            return VisitorMessages.SummaryHeading(heading.GetInt32(), context.Culture);
        }

        if (message.TryGetProperty("entry", out var entry))
        {
            return VisitorMessages.SummaryEntry(
                entry.GetProperty("message").GetString()!,
                entry.TryGetProperty("label", out var label) ? label.GetString() : null,
                entry.TryGetProperty("row", out var row) ? row.GetString() : null,
                context.Culture);
        }

        if (!root.GetProperty("defaults").GetProperty("cultures").TryGetProperty(context.Culture, out var formats))
        {
            throw new InvalidOperationException($"The file has no formats for {context.Culture}.");
        }

        var error = message.GetProperty("error");
        return VisitorMessages.Default(Error(error), context.Culture, VectorReader.Formats(formats), CalendarValues.Today(context.Now, context.TimeZone));
    }

    private static VisitorError Error(JsonElement error) => new()
    {
        Code = error.GetProperty("code").GetString()!,
        Field = error.TryGetProperty("field", out var field) ? Field(field) : null,
        Check = error.TryGetProperty("check", out var check) ? check.GetString() : null,
        Other = error.TryGetProperty("other", out var other) ? other.GetString() : null,
        GridRow = error.TryGetProperty("gridRow", out var gridRow) && gridRow.GetBoolean(),
        Answers = error.TryGetProperty("answers", out var answers) ? VectorReader.Strings(answers) : [],
        Rows = error.TryGetProperty("rows", out var rows) ? rows.GetInt32() : null,
    };

    private static MessageField Field(JsonElement field) => new()
    {
        Type = field.GetProperty("type").GetString()!,
        BuiltIn = !field.TryGetProperty("builtIn", out var builtIn) || builtIn.GetBoolean(),
        Settings = field.TryGetProperty("settings", out var settings) ? settings.Clone() : null,
        Options = field.TryGetProperty("options", out var options)
            ? [.. options.EnumerateArray().Select(option => new MessageOption(option.GetProperty("value").GetString()!, option.GetProperty("label").GetString()!))]
            : [],
    };
}
