using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Reads fields, answers, rules, conditions, operands, contexts and calculations from a vector file's JSON (ADR-0005).
/// An operand of a shape nothing accepts is read as <see cref="InvalidOperand"/>, so checking refuses it as TypeScript
/// does; a null name reads as no name, as in TypeScript.
/// </summary>
internal static class VectorReader
{
    private static readonly Dictionary<string, FieldKind> Kinds = new(StringComparer.Ordinal)
    {
        ["text"] = FieldKind.Text,
        ["choice"] = FieldKind.Choice,
        ["choices"] = FieldKind.Choices,
        ["checkbox"] = FieldKind.Checkbox,
        ["files"] = FieldKind.Files,
        ["number"] = FieldKind.Number,
        ["date"] = FieldKind.Date,
        ["time"] = FieldKind.Time,
        ["dateTime"] = FieldKind.DateTime,
    };

    public static Dictionary<string, FieldKind> Fields(JsonElement element) =>
        element.EnumerateObject().ToDictionary(p => p.Name, p => Kind(p.Value), StringComparer.Ordinal);

    public static Dictionary<string, IReadOnlyList<string>> Values(JsonElement element) =>
        element.EnumerateObject().ToDictionary(p => p.Name, p => (IReadOnlyList<string>)Strings(p.Value), StringComparer.Ordinal);

    public static LogicRule Rule(JsonElement element) => new(
        element.GetProperty("effect").GetString()!,
        element.GetProperty("enabled").GetBoolean(),
        element.GetProperty("match").GetString()!,
        Items(element.GetProperty("conditions")));

    public static LogicCondition Condition(JsonElement element) => new(
        OptionalString(element, "field"),
        OptionalString(element, "context"),
        element.GetProperty("op").GetString()!,
        element.TryGetProperty("value", out var value) ? Operand(value) : null);

    public static Operand Operand(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return new TextOperand(element.GetString()!);
            case JsonValueKind.Array:
                return new ListOperand([.. element.EnumerateArray().Select(Operand)]);
            case JsonValueKind.Object:
                if (element.TryGetProperty("field", out var field) && field.ValueKind == JsonValueKind.String)
                {
                    return new FieldOperand(field.GetString()!);
                }

                if (element.TryGetProperty("today", out var today) && today.ValueKind == JsonValueKind.String)
                {
                    return new TodayOperand(today.GetString()!);
                }

                return new InvalidOperand();
            default:
                return new InvalidOperand();
        }
    }

    /// <summary>Reads a context; a property it lacks comes from <paramref name="defaults"/>.</summary>
    public static EvaluationContext Context(JsonElement element, EvaluationContext? defaults = null)
    {
        string Text(string name, Func<EvaluationContext, string> fallback) =>
            element.TryGetProperty(name, out var value) ? value.GetString()! : fallback(defaults!);

        return new EvaluationContext(
            Text("now", d => d.Now),
            Text("timeZone", d => d.TimeZone),
            Text("culture", d => d.Culture),
            element.TryGetProperty("member", out var member)
                ? new Member(member.GetProperty("signedIn").GetBoolean(), Strings(member.GetProperty("groups")))
                : defaults!.Member,
            element.TryGetProperty("query", out var query) ? Values(query) : defaults!.Query,
            element.TryGetProperty("hidden", out var hidden) ? Strings(hidden) : defaults!.Hidden);
    }

    /// <summary>A calculation; a <c>scale</c> that is not a whole number reads as none, which round refuses.</summary>
    public static Calculation Calculation(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new Constant(element.GetString()!);
        }

        int? scale = element.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Number
            && s.TryGetDecimal(out var places) && places == decimal.Truncate(places) && places is >= int.MinValue and <= int.MaxValue
                ? (int)places
                : null;
        return new Operation(
            element.GetProperty("op").GetString()!,
            [.. element.GetProperty("args").EnumerateArray().Select(Calculation)],
            scale);
    }

    private static FieldKind Kind(JsonElement element) =>
        Kinds.TryGetValue(element.GetString() ?? string.Empty, out var kind)
            ? kind
            : throw new FormatException($"Unknown field kind {element}.");

    private static List<LogicItem> Items(JsonElement element) =>
        [.. element.EnumerateArray()
            .Select(item => item.TryGetProperty("match", out var match)
                ? (LogicItem)new LogicGroup(match.GetString()!, Items(item.GetProperty("conditions")))
                : Condition(item))];

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static List<string> Strings(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? [element.GetString()!]
            : [.. element.EnumerateArray().Select(item => item.GetString()!)];
}
