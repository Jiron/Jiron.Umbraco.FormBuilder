using System.Text.Json;
using Jiron.Umbraco.FormBuilder.AI.Chat;

namespace Jiron.Umbraco.FormBuilder.AI.Drafting;

/// <summary>A draft as the add-on read it from the AI's reply: within the limits, and only with what it can use.</summary>
/// <param name="Name">The form's name.</param>
/// <param name="Pages">Its pages, none empty.</param>
/// <param name="Left">How many of the reply's fields were left out: an unknown type, no label, or past a limit.</param>
internal sealed record DraftReply(string Name, IReadOnlyList<DraftPage> Pages, int Left);

/// <summary>A page of a draft.</summary>
/// <param name="Title">Its title, or null.</param>
/// <param name="Groups">Its groups, none empty.</param>
internal sealed record DraftPage(string? Title, IReadOnlyList<DraftGroup> Groups);

/// <summary>A group of a draft.</summary>
/// <param name="Title">Its title, or null.</param>
/// <param name="Fields">Its fields, at least one.</param>
internal sealed record DraftGroup(string? Title, IReadOnlyList<DraftField> Fields);

/// <summary>A field of a draft.</summary>
/// <param name="Type">One of <see cref="AIPrompts.DraftTypes"/>.</param>
/// <param name="Label">Its label: a heading's text, a paragraph's name.</param>
/// <param name="Help">Its help, a paragraph's text, or null.</param>
/// <param name="Placeholder">Its placeholder, or null.</param>
/// <param name="Required">Whether a visitor must answer it.</param>
/// <param name="Options">Its options' labels, for a type with options; none otherwise.</param>
internal sealed record DraftField(string Type, string Label, string? Help, string? Placeholder, bool Required, IReadOnlyList<string> Options);

/// <summary>
/// Reads a draft out of the AI's reply (ADR-0018), strictly: a field of a type a draft may not use, or without a label,
/// is left out and counted; texts are trimmed and cut to their limits; a type with options that has fewer than two
/// becomes a one-line text, so its question stays. A draft without a field is unusable.
/// </summary>
internal static class DraftReader
{
    /// <summary>The longest name.</summary>
    public const int NameLength = 120;

    /// <summary>The longest label or title, kept on one line.</summary>
    public const int LabelLength = 300;

    /// <summary>The longest help.</summary>
    public const int HelpLength = 500;

    /// <summary>The longest placeholder.</summary>
    public const int PlaceholderLength = 200;

    /// <summary>The longest paragraph.</summary>
    public const int ParagraphLength = 2000;

    /// <summary>The name of a draft whose reply named none.</summary>
    public const string Unnamed = "Draft";

    /// <summary>The draft in a reply.</summary>
    /// <exception cref="AIUnavailableException">The reply holds no field the add-on can use.</exception>
    public static DraftReply Read(JsonElement reply, DraftLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var left = 0;
        var groups = 0;
        var fields = 0;
        var pages = new List<DraftPage>();
        foreach (var page in AIReplies.ArrayOf(reply, "pages"))
        {
            var kept = new List<DraftGroup>();
            foreach (var group in AIReplies.ArrayOf(page, "groups"))
            {
                var keptFields = new List<DraftField>();
                foreach (var field in AIReplies.ArrayOf(group, "fields"))
                {
                    var read = pages.Count < limits.Pages && groups < limits.Groups && fields < limits.Fields ? Field(field, limits) : null;
                    if (read is null)
                    {
                        left++;
                        continue;
                    }

                    keptFields.Add(read);
                    fields++;
                }

                if (keptFields.Count > 0 && groups < limits.Groups)
                {
                    kept.Add(new DraftGroup(Line(AIReplies.TextOf(group, "title", LabelLength)), keptFields));
                    groups++;
                }
            }

            if (kept.Count > 0 && pages.Count < limits.Pages)
            {
                pages.Add(new DraftPage(Line(AIReplies.TextOf(page, "title", LabelLength)), kept));
            }
        }

        if (fields == 0)
        {
            throw AIReplies.Unusable();
        }

        return new DraftReply(Line(AIReplies.TextOf(reply, "name", NameLength)) ?? Unnamed, pages, left);
    }

    private static DraftField? Field(JsonElement field, DraftLimits limits)
    {
        var type = AIReplies.TextOf(field, "type", 40);
        var label = Line(AIReplies.TextOf(field, "label", LabelLength));
        if (type is null || !AIPrompts.DraftTypes.Contains(type) || label is null)
        {
            return null;
        }

        var help = AIReplies.TextOf(field, "help", type == "paragraph" ? ParagraphLength : HelpLength);
        var options = AIPrompts.OptionTypes.Contains(type)
            ? AIReplies.ArrayOf(field, "options")
                .Where(option => option.ValueKind == JsonValueKind.String)
                .Select(option => Line(AIReplies.Cut(option.GetString(), LabelLength)))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limits.Options)
                .ToList()
            : [];
        if (AIPrompts.OptionTypes.Contains(type) && options.Count < 2)
        {
            type = "text";
            options = [];
        }

        if (type == "paragraph" && help is null)
        {
            return null;
        }

        var content = type is "heading" or "paragraph";
        return new DraftField(
            type,
            label,
            type == "heading" ? null : help,
            content ? null : Line(AIReplies.TextOf(field, "placeholder", PlaceholderLength)),
            !content && AIReplies.FlagOf(field, "required"),
            options);
    }

    // A label, title or name is one line.
    private static string? Line(string? text) =>
        text is null ? null : string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
