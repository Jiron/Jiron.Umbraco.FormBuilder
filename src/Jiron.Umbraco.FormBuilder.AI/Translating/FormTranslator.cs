using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.AI.Chat;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.AI.Translating;

/// <summary>What an editor asks a translation of.</summary>
/// <param name="FormKey">The form, whose saved draft is translated.</param>
/// <param name="Culture">One of the site's languages other than its default.</param>
/// <param name="ReplaceExisting">Whether texts already in the language are translated again.</param>
internal sealed record TranslationRequest(Guid FormKey, string Culture, bool ReplaceExisting);

/// <summary>One text and its translation.</summary>
/// <param name="Path">The text's path in the definition, as the texts walker gives it.</param>
/// <param name="Source">The default text.</param>
/// <param name="Text">The translation.</param>
/// <param name="Html">Whether the text is rich text.</param>
internal sealed record TranslatedText(string Path, string Source, string Text, bool Html);

/// <summary>A translation of a form's texts into one language.</summary>
/// <param name="Culture">The language.</param>
/// <param name="DraftVersion">The draft version that was translated, which the designer holds.</param>
/// <param name="Texts">The texts and their translations, in the form's order.</param>
/// <param name="Left">How many texts were sent and came back without a usable translation.</param>
internal sealed record Translation(string Culture, int DraftVersion, IReadOnlyList<TranslatedText> Texts, int Left);

/// <summary>
/// Translate with AI (ADR-0018): the saved draft's texts, as the form check walks them, that have a default text and lack
/// the language (or all of them when the editor asks), go to the AI as a list of ids and texts. A dictionary item
/// (<c>#key</c>) stays out: the dictionary translates it. A translation is kept only when it is a text a save takes in
/// that place and keeps exactly its source's merge tags; the designer applies the kept ones as one step the editor can
/// undo. Nothing is stored here, and nothing of any entry is read.
/// </summary>
internal sealed partial class FormTranslator(AIChat chat, IFormBuilderFormService forms, FieldTypeRegistry fieldTypes, ILanguageService languages)
{
    /// <summary>The most texts one translation sends.</summary>
    public const int MaxTexts = 400;

    /// <summary>The feature's alias in Umbraco.AI's audit log and usage.</summary>
    public const string Alias = "jfb-translate-form";

    [GeneratedRegex(@"\{\{.*?\}\}", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MergeTag();

    /// <summary>The question that translates <paramref name="texts"/> into <paramref name="culture"/>.</summary>
    public static AIQuestion QuestionOf(string culture, IReadOnlyList<(string Id, string Text, bool Html)> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var list = new JsonArray([.. texts.Select(text => (JsonNode)(text.Html
            ? new JsonObject { ["id"] = text.Id, ["text"] = text.Text, ["html"] = true }
            : new JsonObject { ["id"] = text.Id, ["text"] = text.Text }))]);
        return new AIQuestion(
            Alias,
            "Jiron Form Builder: translate a form",
            AIPrompts.TranslationInstructions(culture),
            AIPrompts.Content($"Translate these texts into {AIPrompts.LanguageName(culture)} ({culture}):", AIPrompts.FormTextsTag, list.ToJsonString()),
            AIPrompts.TranslationSchema(),
            16000);
    }

    /// <summary>Translates the saved draft's texts; null for a form without a readable draft.</summary>
    /// <exception cref="AIUnavailableException">The language is not one to translate into, no profile, a provider's failure, or an unusable reply.</exception>
    public async Task<Translation?> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var culture = await CultureOfAsync(request.Culture);
        if (await forms.GetDraftAsync(request.FormKey, cancellationToken) is not { Definition: { } definition } draft)
        {
            return null;
        }

        var wanted = FormTexts.Of(definition, fieldTypes)
            .Where(text => !LocalizedString.TryGetDictionaryKey(text.Text.Default, out _)
                && (request.ReplaceExisting || !text.Text.TryGetText(culture, out _) && text.Text.EntryFor(culture).Culture.Length == 0))
            .Take(MaxTexts)
            .ToList();
        if (wanted.Count == 0)
        {
            return new Translation(culture, draft.DraftVersion, [], 0);
        }

        var sent = wanted.Select((text, index) => (Id: (index + 1).ToString(CultureInfo.InvariantCulture), Text: text.Text.Default, Html: text.Text is LocalizedHtml)).ToList();
        var reply = AIReplies.ObjectOf(await chat.AskAsync(QuestionOf(culture, sent), cancellationToken));
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in AIReplies.ArrayOf(reply, "texts"))
        {
            if (AIReplies.TextOf(item, "id", 10) is { } id && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                answers.TryAdd(id, text.GetString() ?? string.Empty);
            }
        }

        var kept = new List<TranslatedText>();
        for (var i = 0; i < wanted.Count; i++)
        {
            if (answers.TryGetValue(sent[i].Id, out var answer) && Usable(wanted[i].Text, culture, answer) is { } usable)
            {
                kept.Add(new TranslatedText(wanted[i].Path, wanted[i].Text.Default, usable, sent[i].Html));
            }
        }

        if (kept.Count == 0)
        {
            throw AIReplies.Unusable();
        }

        return new Translation(culture, draft.DraftVersion, kept, wanted.Count - kept.Count);
    }

    /// <summary>
    /// A translation as it may stand beside its source, or null: not empty, on one line where the source is, with exactly
    /// the source's merge tags, and a text the save's checks take in the source's kind.
    /// </summary>
    public static string? Usable(LocalizedString source, string culture, string translation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translation);
        var text = translation.Trim();
        if (!source.Default.Contains('\n', StringComparison.Ordinal))
        {
            text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (text.Length == 0 || !SameMergeTags(source.Default, text))
        {
            return null;
        }

        var html = source is LocalizedHtml;
        LocalizedString both;
        try
        {
            var texts = new[] { KeyValuePair.Create(string.Empty, source.Default), KeyValuePair.Create(culture, text) };
            both = html ? new LocalizedHtml(texts) : new LocalizedText(texts);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var limit = html || source.Default.Contains('\n', StringComparison.Ordinal) ? int.MaxValue : DefinitionSaveChecks.LineLength;
        return text.Length <= limit && DefinitionSaveChecks.CheckText(both, html).Count == 0 ? text : null;
    }

    /// <summary>Whether two texts hold the same merge tags, each as often, white space inside a tag aside.</summary>
    public static bool SameMergeTags(string source, string translation)
    {
        static List<string> Tags(string text) =>
            [.. MergeTag().Matches(text).Select(match => string.Concat(match.Value.Where(c => !char.IsWhiteSpace(c)))).Order(StringComparer.Ordinal)];
        return Tags(source).SequenceEqual(Tags(translation), StringComparer.Ordinal);
    }

    // The site's language for the culture asked, as Umbraco names it; never the default, which the texts are in.
    private async Task<string> CultureOfAsync(string culture)
    {
        var language = (await languages.GetAllAsync()).FirstOrDefault(candidate => string.Equals(candidate.IsoCode, culture, StringComparison.OrdinalIgnoreCase));
        return language is { IsDefault: false } && LocalizedString.IsCultureName(language.IsoCode)
            ? language.IsoCode
            : throw new AIUnavailableException(AIProblems.LanguageInvalid, "The language is not one of the site's other languages.");
    }
}
