using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// A case of the rule search against Core's <see cref="RuleSearch"/> (ROADMAP 9.5d, ADR-0179): the conflict of a require
/// rule with the rules that may hide its field, as the index of the hiding rule and the answers that matter (with a form,
/// the fields it hides counting as unanswered), or whether an element shows; the rules overview asks the same of
/// TypeScript.
/// </summary>
internal static class SearchVector
{
    public static JsonNode? Run(JsonElement root, JsonElement vector)
    {
        var search = root.GetProperty("defaults").GetProperty("search");
        var fields = VectorReader.Fields(root.GetProperty("fields"));
        if (vector.TryGetProperty("fields", out var overrides))
        {
            foreach (var (key, kind) in VectorReader.Fields(overrides))
            {
                fields[key] = kind;
            }
        }

        var options = Options(search.GetProperty("options"));
        if (vector.TryGetProperty("options", out var more))
        {
            foreach (var (key, values) in Options(more))
            {
                options[key] = values;
            }
        }

        var scope = new SearchScope(
            fields,
            key => options.TryGetValue(key, out var known) ? known : [],
            [.. search.GetProperty("cultures").EnumerateArray().Select(value => value.GetString()!)],
            search.GetProperty("today").GetString()!);

        if (vector.TryGetProperty("shows", out var shows))
        {
            var rules = shows.GetProperty("rules").EnumerateArray().Select(VectorReader.Rule).ToList();
            var unanswered = shows.GetProperty("unanswered").EnumerateArray().Select(value => value.GetString()!).ToList();
            var several = shows.TryGetProperty("several", out var children) ? VectorReader.Strings(children) : [];
            scope = scope with { Several = several.Contains };
            return JsonValue.Create(RuleSearch.ShowsAtAll(rules, scope, unanswered) switch
            {
                Showing.Shows => "shows",
                Showing.Never => "never",
                _ => "unknown",
            });
        }

        var conflict = vector.GetProperty("conflict");
        if (conflict.TryGetProperty("form", out var form))
        {
            scope = scope with { Hidden = RuleSearch.FormSight(VectorReader.Form(form)) };
        }

        var hidings = conflict.GetProperty("hidings").EnumerateArray().Select(VectorReader.Rule).ToList();
        var found = RuleSearch.ConflictOf(VectorReader.Rule(conflict.GetProperty("requiring")), hidings, scope);
        if (found is null)
        {
            return null;
        }

        var answers = new JsonArray();
        foreach (var answer in found.Answers)
        {
            var written = new JsonObject
            {
                ["subject"] = answer.Subject,
                ["values"] = new JsonArray([.. answer.Values.Select(value => (JsonNode?)value)]),
            };
            if (answer.NoneOf is { } noneOf)
            {
                written["noneOf"] = new JsonArray([.. noneOf.Select(value => (JsonNode?)value)]);
            }

            answers.Add(written);
        }

        // The hiding rule is found as it was given, by reference, as TypeScript finds it.
        return new JsonObject { ["hiding"] = hidings.FindIndex(rule => ReferenceEquals(rule, found.Hiding)), ["answers"] = answers };
    }

    private static Dictionary<string, IReadOnlyList<string>> Options(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => (IReadOnlyList<string>)[.. property.Value.EnumerateArray().Select(value => value.GetString()!)],
            StringComparer.Ordinal);
}
