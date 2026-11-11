using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.AI.Drafting;

/// <summary>
/// Turns a draft into a definition (ADR-0018): its pages, groups and fields as a definition's JSON with fresh keys and
/// aliases made from the labels, read and checked by the definition reader every save uses, with the new-form defaults
/// of the site's general settings (its consent field, its email to the creator). A draft a save would refuse is
/// refused here, before any form exists.
/// </summary>
internal sealed class DraftDefinitions(FormCreation creation, FormDefinitionSerializer serializer)
{
    /// <summary>The definition of a draft; its texts are the default texts.</summary>
    /// <exception cref="AIUnavailableException">The draft is not a definition a save takes.</exception>
    public async Task<FormDefinition> BuildAsync(DraftReply draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var read = serializer.Read(Json(draft).ToJsonString());
        if (read.Definition is not { } definition)
        {
            throw Invalid(read.Problems);
        }

        var withDefaults = await creation.WithNewFormDefaultsAsync(definition, cancellationToken);
        var problems = serializer.CheckSave(withDefaults);
        return problems.Count == 0 ? withDefaults : throw Invalid(problems);
    }

    /// <summary>The definition's JSON: schema version 1, the draft's pages and one outcome with the built-in thank-you message.</summary>
    public static JsonObject Json(DraftReply draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var aliases = new DraftAliases();
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["key"] = Key(),
            ["pages"] = new JsonArray([.. draft.Pages.Select(page => (JsonNode)Page(page, aliases))]),
            ["outcomes"] = new JsonArray(new JsonObject { ["key"] = Key(), ["kind"] = "message" }),
        };
    }

    private static JsonObject Page(DraftPage page, DraftAliases aliases)
    {
        var json = new JsonObject { ["key"] = Key() };
        if (page.Title is { } title)
        {
            json["title"] = Text(title);
        }

        json["groups"] = new JsonArray([.. page.Groups.Select(group => (JsonNode)Group(group, aliases))]);
        return json;
    }

    private static JsonObject Group(DraftGroup group, DraftAliases aliases)
    {
        var json = new JsonObject { ["key"] = Key() };
        if (group.Title is { } title)
        {
            json["title"] = Text(title);
        }

        json["fields"] = new JsonArray([.. group.Fields.Select(field => (JsonNode)Field(field, aliases))]);
        return json;
    }

    private static JsonObject Field(DraftField field, DraftAliases aliases)
    {
        var json = new JsonObject { ["key"] = Key(), ["type"] = field.Type };
        switch (field.Type)
        {
            case "heading":
                json["settings"] = new JsonObject { ["text"] = Text(field.Label) };
                return json;
            case "paragraph":
                json["settings"] = new JsonObject { ["html"] = Text(Paragraphs(field.Help ?? field.Label)) };
                return json;
        }

        json["alias"] = aliases.Next(field.Label);
        json["label"] = Text(field.Label);
        if (field.Help is { } help)
        {
            json["help"] = Text(help);
        }

        if (field.Placeholder is { } placeholder)
        {
            json["placeholder"] = Text(placeholder);
        }

        if (field.Required)
        {
            json["required"] = true;
        }

        if (field.Options.Count > 0)
        {
            var values = new DraftAliases(reserved: false);
            json["options"] = new JsonObject
            {
                ["items"] = new JsonArray([.. field.Options.Select(option => (JsonNode)new JsonObject { ["value"] = values.Next(option, "option"), ["label"] = Text(option) })]),
            };
        }

        return json;
    }

    // A paragraph's text as HTML: each line a paragraph, the text encoded, never markup of the AI's own.
    private static string Paragraphs(string text)
    {
        var html = new StringBuilder();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
        }

        return html.ToString();
    }

    private static JsonObject Text(string text) => new() { [string.Empty] = text };

    private static string Key() => Guid.NewGuid().ToString("D");

    private static AIUnavailableException Invalid(IReadOnlyList<DefinitionProblem> problems) =>
        new(AIProblems.DraftInvalid, $"The draft is not a form a save takes ({string.Join(", ", problems.Select(problem => problem.Code).Distinct().Take(5))}).");
}

/// <summary>
/// Aliases made from labels, as an editor would type them: the label's letters and digits in camel case, starting with a
/// lowercase letter, at most 40 characters, never a reserved name, and each one once in any letter case.
/// </summary>
/// <param name="reserved">Whether the reserved names of field aliases count (option values have none).</param>
internal sealed class DraftAliases(bool reserved = true)
{
    /// <summary>The longest alias made.</summary>
    public const int MaxLength = 40;

    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The next free alias for a label; <paramref name="fallback"/> numbered when the label has no letter or digit to use.</summary>
    public string Next(string label, string fallback = "field")
    {
        ArgumentNullException.ThrowIfNull(label);
        var stem = Stem(label);
        if (stem.Length == 0 || !char.IsAsciiLetterLower(stem[0]))
        {
            stem = fallback + stem;
        }

        if (stem.Length > MaxLength)
        {
            stem = stem[..MaxLength];
        }

        var alias = stem;
        for (var n = 2; _taken.Contains(alias) || (reserved && DefinitionStructureChecks.ReservedAliases.Contains(alias)); n++)
        {
            var suffix = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            alias = stem[..Math.Min(stem.Length, MaxLength - suffix.Length)] + suffix;
        }

        _taken.Add(alias);
        return alias;
    }

    // The ASCII letters and digits of the label's words, the first word lowercase, each other capitalised.
    private static string Stem(string label)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in label.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        var stem = new StringBuilder();
        foreach (var part in words.Take(6))
        {
            stem.Append(stem.Length == 0 ? part.ToLowerInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant());
        }

        return stem.ToString();
    }
}
