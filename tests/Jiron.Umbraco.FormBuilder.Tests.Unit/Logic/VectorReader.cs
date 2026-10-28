using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Reads fields, answers, rules, conditions, operands, contexts, calculations, forms, template values and culture
/// formats from a vector file's JSON (ADR-0005, ADR-0061, ADR-0064); <see cref="FieldVector"/> reads a field to check
/// (ADR-0062).
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

    private static readonly Dictionary<string, MergeTagContext> TemplateContexts = new(StringComparer.Ordinal)
    {
        ["html"] = MergeTagContext.Html,
        ["text"] = MergeTagContext.Text,
        ["header"] = MergeTagContext.Header,
        ["url"] = MergeTagContext.Url,
        ["json"] = MergeTagContext.Json,
        ["jsonString"] = MergeTagContext.JsonString,
        ["form"] = MergeTagContext.Form,
        ["xml"] = MergeTagContext.Xml,
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

    /// <summary>A form in the engine's model; a list or flag it lacks reads as empty or false, as in TypeScript.</summary>
    public static LogicForm Form(JsonElement element) => new(
        List(element, "pages", page => new LogicPage(Id(page), RulesOf(page), List(page, "groups", group => new LogicFormGroup(Id(group), RulesOf(group), List(group, "fields", Field))))),
        List(element, "validationRules", rule => new LogicValidationRule(
            Id(rule),
            rule.GetProperty("field").GetString()!,
            rule.GetProperty("enabled").GetBoolean(),
            rule.GetProperty("match").GetString()!,
            Items(rule.GetProperty("conditions")))),
        List(element, "actions", action => new LogicAction(Id(action), RulesOf(action))),
        List(element, "outcomes", outcome => new LogicOutcome(Id(outcome), RulesOf(outcome))));

    /// <summary>Each repeatable group's rows, each holding its child fields' answers.</summary>
    public static Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>> Rows(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            p => p.Name,
            p => (IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>)[.. p.Value.EnumerateArray().Select(row => Values(row))],
            StringComparer.Ordinal);

    /// <summary>A calculation; a <c>scale</c> that is not a whole number reads as none, which round refuses.</summary>
    public static Calculation Calculation(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new Constant(element.GetString()!);
        }

        if (element.TryGetProperty("field", out var field))
        {
            return new FieldReference(field.GetString()!);
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

    /// <summary>A template's context by its name in the vectors.</summary>
    public static MergeTagContext TemplateContextOf(JsonElement element) =>
        TemplateContexts.TryGetValue(element.GetString() ?? string.Empty, out var context)
            ? context
            : throw new FormatException($"Unknown template context {element}.");

    /// <summary>A source's value: a string, an object with its text and the wire number or date behind it, or a list of these.</summary>
    public static MergeTagValue ValueOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? MergeTagValue.ListOf([.. element.EnumerateArray().Select(ItemOf)])
            : MergeTagValue.Of(ItemOf(element));

    /// <summary>A culture's formats as the server writes them (ADR-0064).</summary>
    public static CultureFormats Formats(JsonElement element)
    {
        var dates = element.GetProperty("dates");
        var number = element.GetProperty("number");
        var currency = element.GetProperty("currency");
        string Text(JsonElement parent, string name) => parent.GetProperty(name).GetString()!;
        List<int> Sizes(JsonElement parent) => [.. parent.GetProperty("sizes").EnumerateArray().Select(size => size.GetInt32())];
        return new CultureFormats(
            new DatePatterns(Text(dates, "short"), Text(dates, "medium"), Text(dates, "long"), Text(dates, "time"), Text(dates, "dateTime")),
            Strings(element.GetProperty("months")),
            Strings(element.GetProperty("monthsShort")),
            Strings(element.GetProperty("monthsGenitive")),
            Strings(element.GetProperty("monthsShortGenitive")),
            Strings(element.GetProperty("days")),
            Strings(element.GetProperty("daysShort")),
            Text(element, "am"),
            Text(element, "pm"),
            Text(element, "minus"),
            new NumberFormat(Text(number, "decimal"), Text(number, "group"), Sizes(number), Text(number, "negative")),
            new CurrencyFormat(
                Text(currency, "code"),
                Text(currency, "symbol"),
                Text(currency, "decimal"),
                Text(currency, "group"),
                Sizes(currency),
                Text(currency, "positive"),
                Text(currency, "negative")));
    }

    private static MergeTagItem ItemOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? new MergeTagItem(element.GetString()!)
            : new MergeTagItem(element.GetProperty("text").GetString()!, OptionalString(element, "number"), OptionalString(element, "date"));

    private static LogicField Field(JsonElement element) => new(
        Id(element),
        element.TryGetProperty("kind", out var kind) && kind.ValueKind != JsonValueKind.Null ? Kind(kind) : null,
        element.TryGetProperty("required", out var required) && required.GetBoolean(),
        RulesOf(element),
        List(element, "fields", Field),
        List(element, "rows", row => new LogicGridRow(Id(row), Kind(row.GetProperty("kind")))),
        OptionalString(element, "alias"),
        element.TryGetProperty("calculation", out var calculation) && calculation.ValueKind != JsonValueKind.Null
            ? new LogicCalculation(
                calculation.GetProperty("formula").GetString()!,
                calculation.TryGetProperty("decimals", out var decimals) && decimals.ValueKind == JsonValueKind.Number ? (decimals.TryGetInt32(out var places) ? places : -1) : null)
            : null,
        element.TryGetProperty("canBeRequired", out var canBeRequired) && canBeRequired.GetBoolean());

    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    private static List<LogicRule> RulesOf(JsonElement element) => List(element, "rules", Rule);

    private static List<T> List<T>(JsonElement element, string name, Func<JsonElement, T> read) =>
        element.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(read)] : [];

    public static FieldKind Kind(JsonElement element) =>
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

    public static List<string> Strings(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? [element.GetString()!]
            : [.. element.EnumerateArray().Select(item => item.GetString()!)];
}
