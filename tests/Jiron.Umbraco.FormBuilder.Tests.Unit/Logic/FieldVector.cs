using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a vector's <c>field</c> through its built-in type as the pipeline will (ADR-0058, ADR-0062): its answers read,
/// then checked, with the form's other fields' values. First it holds the vector to what both runtimes see alike: the
/// field gives every public setting its type has a default for, an object's settings each in turn, as the public
/// projection writes them; its kind is its type's; it has options and rows only when its type has them; a field that
/// stores files has one file for each value; and its answers are readable already cleaned as the pipeline cleans them,
/// with the values posted as they read, because only Core reads answers until the browser's runtime (6.4).
/// </summary>
internal static class FieldVector
{
    private static readonly Guid Key = new("2b1f5c8e-0d3a-4e7b-9c61-5a8f3d2e4b70");

    // The library list a choice without options of its own takes them from, whose answers 5.5 checks.
    private static readonly Guid List = new("6d0e2f4a-8b1c-4c3d-9e5f-7a6b5c4d3e2f");

    /// <summary>The field's errors as both runners write them: <c>{ "errors": [...], "checks": [...] }</c>.</summary>
    public static JsonObject Run(string id, JsonElement field, JsonElement answers, JsonElement values, EvaluationContext context)
    {
        var alias = field.GetProperty("type").GetString()!;
        var type = TestFieldTypes.Registry.FindType(alias) ?? throw new AssertionException($"{id}: the site has no type '{alias}'.");
        var settings = field.TryGetProperty("settings", out var given) ? given : default;
        var leaned = Leaned(type.SettingsSchema.Settings.Where(setting => setting.IsPublic), settings, "").ToList();
        if (leaned.Count > 0)
        {
            throw new AssertionException($"{id}: the field leans on the defaults of {string.Join(", ", leaned)}, which the projection writes; give them.");
        }

        var kind = field.TryGetProperty("kind", out var named) && named.ValueKind == JsonValueKind.String ? VectorReader.Kind(named) : (FieldKind?)null;
        if (kind != type.Declaration.RuleKind)
        {
            throw new AssertionException($"{id}: a {alias} field's kind is {type.Declaration.RuleKind?.ToString() ?? "none"}, not {kind?.ToString() ?? "none"}.");
        }

        var hasOptions = field.TryGetProperty("options", out var options);
        var hasRows = field.TryGetProperty("rows", out var rows);
        if ((hasOptions && !type.Declaration.Features.HasFlag(FieldFeatures.Options)) || (hasRows && !type.Declaration.Features.HasFlag(FieldFeatures.Rows)))
        {
            throw new AssertionException($"{id}: a {alias} field has no options or rows of its own.");
        }

        var required = field.TryGetProperty("required", out var flag) && flag.GetBoolean();
        var definition = new FormField
        {
            Key = Key,
            Type = alias,
            Alias = "field",
            Required = required,
            Settings = settings.ValueKind == JsonValueKind.Object ? SettingsObject.Parse(settings.GetRawText()) : null,
            Options = hasOptions
                ? new FieldOptions { Items = [.. VectorReader.Strings(options).Select(value => new FieldOption { Value = value })] }
                : type.Declaration.Features.HasFlag(FieldFeatures.Options) ? new FieldOptions { Source = OptionSource.List, OptionList = List } : null,
            Rows = hasRows ? [.. rows.EnumerateArray().Select(Row)] : [],
            Validation = field.TryGetProperty("checks", out var checks) ? [.. checks.EnumerateArray().Select(Check)] : [],
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

        ImmutableArray<FieldFile> files = answers.TryGetProperty("files", out var chosen)
            ? [.. chosen.EnumerateArray().Select(file => new FieldFile(file.GetProperty("name").GetString()!, file.GetProperty("size").GetInt64()))]
            : [];
        if (files.Length != (type.Declaration.Storage == FieldStorageKind.File ? posted.Values.Length : 0))
        {
            throw new AssertionException($"{id}: a field that stores files has one file for each value, and no other field has files.");
        }

        // The values posted are the field's own values as they read: a choice's "Other" text, a grid's rows and the
        // parts of a name or an address come as parts.
        var read = FieldValues.Read(type, definition, posted);
        var own = read.Values.Where(value => value.Row is null).Select(value => value.Text).ToList();
        if (read.IsUnreadable || own.Count < posted.Values.Length || !own.Take(posted.Values.Length).SequenceEqual(posted.Values, StringComparer.Ordinal))
        {
            throw new AssertionException($"{id}: the answers are unreadable or not in the wire form; the browser does not read them.");
        }

        var today = DateOnly.FromDayNumber(CalendarValues.Today(context.Now, context.TimeZone));
        var others = values.ValueKind == JsonValueKind.Object
            ? VectorReader.Values(values).ToDictionary(pair => Guid.Parse(pair.Key, CultureInfo.InvariantCulture), pair => pair.Value.ToImmutableArray())
            : new Dictionary<Guid, ImmutableArray<string>>();
        var errors = FieldValues.Check(type, definition, read, new FieldCheckInput(required, today) { Files = files, Values = others });
        return new JsonObject
        {
            ["errors"] = new JsonArray([.. errors.Errors.Select(error => (JsonNode)FieldMessageProperties.All.Single(property => property.Kind == error).Name)]),
            ["checks"] = new JsonArray([.. errors.Checks.Select(index => (JsonNode)index)]),
        };
    }

    // The settings with a default that the field does not give, and those of an object it gives, by their paths.
    private static IEnumerable<string> Leaned(IEnumerable<FieldSettingDescriptor> descriptors, JsonElement settings, string prefix)
    {
        foreach (var setting in descriptors)
        {
            if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty(setting.Name, out var value))
            {
                if (setting.Default is not null)
                {
                    yield return prefix + setting.Name;
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var inner in Leaned(setting.Settings, value, prefix + setting.Name + "."))
                {
                    yield return inner;
                }
            }
        }
    }

    // A grid's row as the projection writes it: its id, and whether it is marked required (ADR-0069).
    private static GridRow Row(JsonElement row) => new()
    {
        Key = Guid.Parse(row.GetProperty("id").GetString()!, CultureInfo.InvariantCulture),
        Required = row.TryGetProperty("required", out var required) && required.GetBoolean(),
    };

    private static FieldCheck Check(JsonElement check) => check.GetProperty("kind").GetString() switch
    {
        "pattern" => new PatternCheck { Pattern = check.GetProperty("pattern").GetString()! },
        "matchesField" => new MatchesFieldCheck { Field = Guid.Parse(check.GetProperty("field").GetString()!, CultureInfo.InvariantCulture) },
        "uniquePerForm" => new UniquePerFormCheck(),
        var other => throw new AssertionException($"No check of the kind '{other}'."),
    };
}
