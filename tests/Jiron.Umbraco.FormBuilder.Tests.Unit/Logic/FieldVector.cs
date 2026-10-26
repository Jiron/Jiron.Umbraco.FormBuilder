using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a vector's <c>field</c> through its built-in type as the pipeline will (ADR-0058, ADR-0062): its answers read,
/// then checked. First it holds the vector to what both runtimes see alike: the field gives every public setting its
/// type has a default for, as the public projection writes them; its kind is its type's; and its answers are readable
/// already cleaned as the pipeline cleans them, and in the wire form, because only Core reads answers until the
/// browser's runtime (6.4).
/// </summary>
internal static class FieldVector
{
    private static readonly Guid Key = new("2b1f5c8e-0d3a-4e7b-9c61-5a8f3d2e4b70");

    /// <summary>The field's errors as both runners write them: <c>{ "errors": [...], "checks": [...] }</c>.</summary>
    public static JsonObject Run(string id, JsonElement field, JsonElement answers, EvaluationContext context)
    {
        var alias = field.GetProperty("type").GetString()!;
        var type = TestFieldTypes.Registry.FindType(alias) ?? throw new AssertionException($"{id}: the site has no type '{alias}'.");
        var settings = field.TryGetProperty("settings", out var given) ? given : default;
        var leaned = type.SettingsSchema.Settings
            .Where(setting => setting.IsPublic && setting.Default is not null && !(settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(setting.Name, out _)))
            .Select(setting => setting.Name)
            .ToList();
        if (leaned.Count > 0)
        {
            throw new AssertionException($"{id}: the field leans on the defaults of {string.Join(", ", leaned)}, which the projection writes; give them.");
        }

        var kind = field.TryGetProperty("kind", out var named) && named.ValueKind == JsonValueKind.String ? VectorReader.Kind(named) : (FieldKind?)null;
        if (kind != type.Declaration.RuleKind)
        {
            throw new AssertionException($"{id}: a {alias} field's kind is {type.Declaration.RuleKind?.ToString() ?? "none"}, not {kind?.ToString() ?? "none"}.");
        }

        var required = field.TryGetProperty("required", out var flag) && flag.GetBoolean();
        var definition = new FormField
        {
            Key = Key,
            Type = alias,
            Alias = "field",
            Required = required,
            Settings = settings.ValueKind == JsonValueKind.Object ? SettingsObject.Parse(settings.GetRawText()) : null,
        };
        var posted = new FieldAnswers(
            VectorReader.Strings(answers.GetProperty("values")),
            answers.TryGetProperty("parts", out var parts)
                ? parts.EnumerateObject().Select(part => KeyValuePair.Create(part.Name, (IEnumerable<string>)VectorReader.Strings(part.Value)))
                : null);
        if (posted.Values.Concat(posted.Parts.Values.SelectMany(texts => texts)).Any(text => TextSemantics.Clean(text) != text))
        {
            throw new AssertionException($"{id}: an answer is not cleaned as the pipeline cleans it.");
        }

        var read = FieldValues.Read(type, definition, posted);
        if (read.IsUnreadable || !read.Values.Select(value => value.Text).SequenceEqual(posted.Values, StringComparer.Ordinal))
        {
            throw new AssertionException($"{id}: the answers are unreadable or not in the wire form; the browser does not read them.");
        }

        var today = DateOnly.FromDayNumber(CalendarValues.Today(context.Now, context.TimeZone));
        var errors = FieldValues.Check(type, definition, read, required, today);
        return new JsonObject
        {
            ["errors"] = new JsonArray([.. errors.Select(error => (JsonNode)FieldMessageProperties.All.Single(property => property.Kind == error).Name)]),
            ["checks"] = new JsonArray(),
        };
    }
}
