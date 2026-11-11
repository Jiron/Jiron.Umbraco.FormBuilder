using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.AI.Chat;
using Jiron.Umbraco.FormBuilder.AI.Translating;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.AI.Suggesting;

/// <summary>What an editor asks suggestions for: one field of a form's saved draft.</summary>
/// <param name="FormKey">The form.</param>
/// <param name="FieldKey">The field.</param>
internal sealed record SuggestionRequest(Guid FormKey, Guid FieldKey);

/// <summary>One suggested text for a field.</summary>
/// <param name="Setting">Which of the field's texts: <c>label</c>, <c>help</c> or <c>placeholder</c>.</param>
/// <param name="Path">The text's path in the definition, which the designer changes.</param>
/// <param name="Text">The suggested default text.</param>
/// <param name="Reason">Why, for an improvement; null for the help suggestion itself.</param>
internal sealed record SuggestedText(string Setting, string Path, string Text, string? Reason);

/// <summary>Suggestions for one field.</summary>
/// <param name="DraftVersion">The draft version they were made for.</param>
/// <param name="Help">A help text, or null when the field needs none or has it.</param>
/// <param name="Improvements">At most three changes to the field's own texts, each with its reason.</param>
internal sealed record Suggestions(int DraftVersion, SuggestedText? Help, IReadOnlyList<SuggestedText> Improvements);

/// <summary>
/// Suggest (ADR-0018): one field of the saved draft, with its default texts, its options' labels and the labels of the
/// fields around it, goes to the AI, which answers a help text and up to three improvements to the field's own label, help
/// or placeholder, each with its reason. A suggestion is kept only when it is a text a save takes in that place, differs
/// from what is there, and keeps that text's merge tags. Nothing of any entry is read, and nothing is stored here.
/// </summary>
internal sealed class FieldSuggester(AIChat chat, IFormBuilderFormService forms)
{
    /// <summary>The feature's alias in Umbraco.AI's audit log and usage.</summary>
    public const string Alias = "jfb-suggest-field";

    /// <summary>The most improvements kept.</summary>
    public const int MaxImprovements = 3;

    /// <summary>The longest reason kept.</summary>
    public const int ReasonLength = 300;

    /// <summary>The question about a field, given as the JSON object the block holds.</summary>
    public static AIQuestion QuestionOf(JsonObject field) => new(
        Alias,
        "Jiron Form Builder: suggest for a field",
        AIPrompts.SuggestionInstructions(),
        AIPrompts.Content("Suggest a help text and improvements for this field:", AIPrompts.FieldTag, field.ToJsonString()),
        AIPrompts.SuggestionsSchema(),
        1500);

    /// <summary>Suggestions for the field; null for a form without a readable draft.</summary>
    /// <exception cref="AIUnavailableException">The field is not in the draft, no profile, a provider's failure, or an unusable reply.</exception>
    public async Task<Suggestions?> SuggestAsync(SuggestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetDraftAsync(request.FormKey, cancellationToken) is not { Definition: { } definition } draft)
        {
            return null;
        }

        var found = Locate(definition, request.FieldKey) ?? throw new AIUnavailableException(AIProblems.FieldMissing, "The field is not in the saved draft.");
        var reply = AIReplies.ObjectOf(await chat.AskAsync(QuestionOf(Describe(found)), cancellationToken));
        var current = new Dictionary<string, LocalizedText?>(StringComparer.Ordinal)
        {
            ["label"] = found.Field.Label,
            ["help"] = found.Field.Help,
            ["placeholder"] = found.Field.Placeholder,
        };

        SuggestedText? help = null;
        if (found.Field.Help is null && AIReplies.TextOf(reply, "help", 2000) is { } helpText && Kept("help", helpText, current) is { } keptHelp)
        {
            help = new SuggestedText("help", $"{found.Path}.help", keptHelp, null);
        }

        var improvements = new List<SuggestedText>();
        foreach (var item in AIReplies.ArrayOf(reply, "improvements"))
        {
            var setting = AIReplies.TextOf(item, "setting", 20);
            if (improvements.Count == MaxImprovements || setting is null || !current.ContainsKey(setting)
                || AIReplies.TextOf(item, "text", 2000) is not { } text || Kept(setting, text, current) is not { } kept
                || (setting == "help" && help?.Text == kept) || improvements.Any(other => other.Setting == setting))
            {
                continue;
            }

            var reason = AIReplies.TextOf(item, "reason", ReasonLength);
            improvements.Add(new SuggestedText(setting, $"{found.Path}.{setting}", kept, reason is null ? null : OneLine(reason)));
        }

        return new Suggestions(draft.DraftVersion, help, improvements);
    }

    /// <summary>The field and where it is, by its key; a repeatable group's child is found too.</summary>
    public static LocatedField? Locate(FormDefinition definition, Guid fieldKey)
    {
        ArgumentNullException.ThrowIfNull(definition);
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = definition.Pages[p];
            for (var g = 0; g < page.Groups.Count; g++)
            {
                var group = page.Groups[g];
                var groupPath = $"$.pages[{p}].groups[{g}]";
                for (var f = 0; f < group.Fields.Count; f++)
                {
                    var field = group.Fields[f];
                    var path = $"{groupPath}.fields[{f}]";
                    if (field.Key == fieldKey)
                    {
                        return new LocatedField(field, path, page, group, group.Fields);
                    }

                    for (var c = 0; c < field.Fields.Count; c++)
                    {
                        if (field.Fields[c].Key == fieldKey)
                        {
                            return new LocatedField(field.Fields[c], $"{path}.fields[{c}]", page, group, field.Fields);
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The block's JSON of a field: its default texts and what is around it, never anything of an entry.</summary>
    public static JsonObject Describe(LocatedField found)
    {
        ArgumentNullException.ThrowIfNull(found);
        var field = found.Field;
        return new JsonObject
        {
            ["type"] = field.Type,
            ["label"] = field.Label?.Default,
            ["help"] = field.Help?.Default,
            ["placeholder"] = field.Placeholder?.Default,
            ["required"] = field.Required,
            ["options"] = new JsonArray([.. (field.Options?.Items ?? []).Take(50).Select(option => (JsonNode?)option.Label?.Default)]),
            ["page"] = found.Page.Title?.Default,
            ["group"] = found.Group.Title?.Default,
            ["nearby"] = new JsonArray([.. found.Siblings.Where(other => other.Key != field.Key && other.Label is not null).Take(20).Select(other => (JsonNode?)other.Label!.Default)]),
        };
    }

    // A suggestion as it may stand in its place, or null: one line for a label or placeholder, a text the save takes there,
    // with the merge tags of what is there, and not what is there already.
    private static string? Kept(string setting, string text, Dictionary<string, LocalizedText?> current)
    {
        var now = current[setting];
        var source = new LocalizedText(now?.Default is { Length: > 0 } existing ? existing : text);
        var line = setting == "help" ? OneLine(text) : text;
        return TranslatorUsable(source, line) is { } kept && !string.Equals(kept, now?.Default, StringComparison.Ordinal) && FormTranslator.SameMergeTags(now?.Default ?? string.Empty, kept)
            ? kept
            : null;
    }

    // The translator's checks of a text in the source's kind, applied to a default text.
    private static string? TranslatorUsable(LocalizedText source, string text) => FormTranslator.Usable(source, "en", text);

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>A field of a definition and where it is.</summary>
/// <param name="Field">The field.</param>
/// <param name="Path">Its path in the definition.</param>
/// <param name="Page">Its page.</param>
/// <param name="Group">Its group.</param>
/// <param name="Siblings">The fields beside it: its group's, or its repeatable group's.</param>
internal sealed record LocatedField(FormField Field, string Path, FormPage Page, FormGroup Group, IReadOnlyList<FormField> Siblings);
