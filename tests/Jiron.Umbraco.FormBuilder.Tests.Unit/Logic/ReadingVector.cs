using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a vector's <c>read</c> through Core's reading stage (ROADMAP 6.4a, ADR-0058, ADR-0109): the answer as posted,
/// cleaned as the pipeline cleans it, held to its built-in type's limits and read by the type; and a <c>rowCount</c>
/// through a repeatable group's check of its rows. The field gives every public setting its type has a default for, as
/// the projection writes them, as <see cref="FieldVector"/> asks.
/// </summary>
internal static class ReadingVector
{
    private static readonly Guid Key = new("7c2e9a41-5b3d-4f60-8e1a-2d9c4b6f0a35");

    /// <summary>The reading as both runners write it: <c>{ "limit", "values": [{ "text", "row"?, "stored"? }], "unreadable" }</c>.</summary>
    public static JsonObject Read(string id, JsonElement read)
    {
        var (type, field) = Field(id, read.GetProperty("field"));
        var answer = read.GetProperty("answer");
        var parts = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal);
        if (answer.TryGetProperty("parts", out var given))
        {
            foreach (var pair in given.EnumerateArray())
            {
                var name = pair[0].GetString()!;
                if (!parts.TryAdd(name, [.. VectorReader.Strings(pair[1])]))
                {
                    throw new AssertionException($"{id}: the part '{name}' is posted twice; a post names a part once.");
                }
            }
        }

        var answers = SubmissionReader.Clean(new PostedAnswer([.. VectorReader.Strings(answer.GetProperty("values"))], parts));
        var limit = FieldValues.CheckLimits(type, field, answers);
        var reading = limit is null ? FieldValues.Read(type, field, answers) : null;
        return new JsonObject
        {
            ["limit"] = limit is { } kind ? FieldMessageProperties.All.Single(property => property.Kind == kind).Name : null,
            ["values"] = new JsonArray([.. (reading?.IsUnreadable == false ? reading.Values : []).Select(Value)]),
            ["unreadable"] = reading?.IsUnreadable == true,
        };
    }

    /// <summary>A repeatable group's errors for its number of rows, as both runners write them.</summary>
    public static JsonArray RowCount(string id, JsonElement rows)
    {
        var (type, field) = Field(id, rows.GetProperty("field"));
        var errors = FieldValues.CheckRows(type, field, rows.GetProperty("count").GetInt32());
        return new JsonArray([.. errors.Select(error => (JsonNode)FieldMessageProperties.All.Single(property => property.Kind == error).Name)]);
    }

    private static JsonNode Value(FieldValue value)
    {
        var json = new JsonObject { ["text"] = value.Text };
        if (value.Row is { } row)
        {
            json["row"] = row.ToString("D");
        }

        if (value.StoredText is { } stored)
        {
            json["stored"] = stored;
        }

        return json;
    }

    // The field of a built-in type, with the public settings its type has defaults for and a grid's rows.
    private static (IFormBuilderFieldType Type, FormField Field) Field(string id, JsonElement field)
    {
        var alias = field.GetProperty("type").GetString()!;
        var type = TestFieldTypes.Registry.FindType(alias) ?? throw new AssertionException($"{id}: the site has no type '{alias}'.");
        var settings = field.TryGetProperty("settings", out var given) ? given : default;
        var leaned = FieldVector.Leaned(type.SettingsSchema.Settings.Where(setting => setting.IsPublic), settings, "").ToList();
        if (leaned.Count > 0)
        {
            throw new AssertionException($"{id}: the field leans on the defaults of {string.Join(", ", leaned)}, which the projection writes; give them.");
        }

        return (type, new FormField
        {
            Key = Key,
            Type = alias,
            Alias = "field",
            // Settings with nothing in them are no settings, as the definition writes them.
            Settings = settings.ValueKind == JsonValueKind.Object && settings.EnumerateObject().Any() ? SettingsObject.Parse(settings.GetRawText()) : null,
            Rows = field.TryGetProperty("rows", out var rows)
                ? [.. rows.EnumerateArray().Select(row => new GridRow { Key = Guid.Parse(row.GetProperty("id").GetString()!, System.Globalization.CultureInfo.InvariantCulture) })]
                : [],
        });
    }
}
