using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>A visitor text a site or a form may reword (ADR-0230).</summary>
/// <param name="Key">Its key: <c>message.</c>, <c>form.</c> or <c>answer.</c> and its name, such as <c>message.minLength.one</c>.</param>
/// <param name="Group">The group the editor lists it in, one of <see cref="VisitorTextKeys.Groups"/>.</param>
/// <param name="Values">The values in braces the package's text names, which a wording may name too, such as <c>{min}</c>.</param>
internal sealed record VisitorTextKey(string Key, string Group, ImmutableArray<string> Values);

/// <summary>
/// The package's visitor texts a site or a form may reword, by key (ADR-0230): every message (<c>message.</c>, a counting
/// one as <c>.one</c> and <c>.other</c>, a cookie category's name as <c>message.captchaCategories.marketing</c>), every
/// word a rendered form brings (<c>form.</c>, Web's <c>RenderingWords</c> by name) but the three buttons, which have
/// fields of their own, and the note only backoffice users see, and the words for yes and no (<c>answer.yes</c>,
/// <c>answer.no</c>). Each sits in one group the editor lists it under; a test holds the list to the tables.
/// </summary>
internal static partial class VisitorTextKeys
{
    /// <summary>The prefix of a message's key.</summary>
    public const string MessagePrefix = "message.";

    /// <summary>The prefix of a rendered form's word's key.</summary>
    public const string FormPrefix = "form.";

    /// <summary>The prefix of the words for yes and no.</summary>
    public const string AnswerPrefix = "answer.";

    /// <summary>The longest wording, in characters.</summary>
    public const int MaxLength = 500;

    /// <summary>The longest key, in characters.</summary>
    public const int MaxKeyLength = 100;

    /// <summary>
    /// The groups, in the order the editor lists them: what a form shows around its questions, the file control, the
    /// signature, the notices, what follows a send, the error messages and the words for yes and no.
    /// </summary>
    public static ImmutableArray<string> Groups { get; } = ["form", "files", "signature", "notices", "after", "errors", "answers"];

    // The words of a rendered form that may be reworded, by group, with the values each names, in RenderingWords' order.
    private static (string Group, string Name, string[] Values)[] FormWords =>
    [
        ("form", "optional", []),
        ("form", "requiredNote", ["{0}"]),
        ("form", "asterisk", []),
        ("form", "required", []),
        ("form", "step", ["{0}", "{1}"]),
        ("form", "progress", []),
        ("form", "page", ["{0}"]),
        ("form", "confirmEmail", []),
        ("form", "counter", ["{0}"]),
        ("form", "counterLeft", ["{0}"]),
        ("form", "counterLeftOne", ["{0}"]),
        ("form", "counterOver", ["{0}"]),
        ("form", "counterOverOne", ["{0}"]),
        ("form", "rankingMoved", ["{0}", "{1}"]),
        ("form", "rowRemoved", ["{0}"]),
        ("after", "reference", ["{0}"]),
        ("notices", "opensHeading", ["{0}"]),
        ("notices", "opensText", []),
        ("notices", "closedHeading", []),
        ("notices", "closedText", []),
        ("notices", "fullHeading", []),
        ("notices", "fullText", []),
        ("notices", "signInHeading", []),
        ("notices", "signInText", []),
        ("notices", "groupsHeading", []),
        ("notices", "groupsText", []),
        ("form", "other", []),
        ("form", "otherText", []),
        ("form", "choose", []),
        ("form", "chooseCountry", []),
        ("form", "severalHint", []),
        ("form", "noChoices", []),
        ("form", "choicesUnavailable", []),
        ("form", "moveUp", ["{0}"]),
        ("form", "moveDown", ["{0}"]),
        ("form", "row", ["{0}"]),
        ("form", "repeaterRow", ["{0}"]),
        ("form", "addRow", []),
        ("form", "removeRow", []),
        ("form", "nameTitle", []),
        ("form", "nameFirst", []),
        ("form", "nameMiddle", []),
        ("form", "nameLast", []),
        ("form", "street", []),
        ("form", "street2", []),
        ("form", "postcode", []),
        ("form", "city", []),
        ("form", "region", []),
        ("form", "country", []),
        ("signature", "signatureName", []),
        ("signature", "signatureTyped", []),
        ("signature", "signatureHand", []),
        ("signature", "signatureDrawn", []),
        ("signature", "signatureClear", []),
        ("signature", "signatureUndoClear", []),
        ("signature", "signatureTypeInstead", []),
        ("signature", "signatureByHand", []),
        ("after", "thankYou", []),
        ("after", "continue", []),
        ("after", "goNow", []),
        ("after", "stayHere", []),
        ("after", "countdownOne", ["{0}"]),
        ("after", "countdownOther", ["{0}"]),
        ("notices", "sending", []),
        ("notices", "connectionText", []),
        ("notices", "serverErrorText", []),
        ("notices", "changedHeading", []),
        ("notices", "changedText", []),
        ("notices", "changedConsent", []),
        ("notices", "changedConsents", []),
        ("notices", "notSentHeading", []),
        ("notices", "notSentText", []),
        ("notices", "tooLargeText", []),
        ("notices", "waitSecondsText", ["{0}"]),
        ("notices", "waitMinutesText", ["{0}"]),
        ("files", "chooseFilesAgain", []),
        ("files", "noRoomText", []),
        ("files", "keepFile", ["{0}", "{1}"]),
        ("files", "chooseFile", []),
        ("files", "chooseFiles", []),
        ("files", "takePhoto", []),
        ("files", "dragFile", []),
        ("files", "dragFiles", []),
        ("files", "yourFiles", []),
        ("files", "fileUploading", ["{0}"]),
        ("files", "fileUploaded", ["{0}"]),
        ("files", "removeFile", ["{0}"]),
        ("files", "stopUploading", ["{0}"]),
        ("files", "tryAgain", []),
        ("files", "tryAgainFor", ["{0}"]),
        ("files", "uploadStopped", []),
        ("files", "uploadLater", []),
        ("files", "kilobytes", ["{0}"]),
        ("files", "megabytes", ["{0}"]),
        ("form", "honeypot", []),
        ("after", "change", []),
        ("after", "notAnswered", []),
        ("after", "hiddenAnswer", []),
        ("after", "yourAnswers", []),
        ("after", "printAnswers", []),
        ("after", "fillInAgain", []),
        ("after", "copyShown", []),
    ];

    /// <summary>Every text a site or a form may reword: the rendered form's words, the messages, then yes and no.</summary>
    public static ImmutableArray<VisitorTextKey> All { get; } = Build();

    // Declared after All, which it reads, so it starts once All has.
    private static readonly FrozenDictionary<string, VisitorTextKey> ByKey = All.ToFrozenDictionary(key => key.Key, StringComparer.Ordinal);

    /// <summary>The text of a key, or null when the package has no text of that key.</summary>
    /// <param name="key">The key, such as <c>message.email</c>.</param>
    public static VisitorTextKey? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    /// <summary>The values in braces a text names, in the order it names them, each once.</summary>
    /// <param name="text">A text, such as <c>Enter at least {min} characters</c>.</param>
    public static ImmutableArray<string> ValuesOf(string text) =>
        [.. ValuePattern().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Why a wording cannot stand for a text, or null when it can: it is not only white space, is on one line, has at most
    /// <see cref="MaxLength"/> characters, and every brace in it is part of a value the text names. A wording may name a
    /// dictionary item instead (<c>#key</c>), whose text the same rule holds where it shows.
    /// </summary>
    /// <param name="key">The text.</param>
    /// <param name="wording">The wording.</param>
    public static string? Problem(VisitorTextKey key, string? wording)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(wording))
        {
            return "A wording is more than white space; leave the text out to keep the package's.";
        }

        if (wording.Length > MaxLength)
        {
            return $"A wording has at most {MaxLength} characters.";
        }

        if (wording.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'))
        {
            return "A wording is one line, without control characters.";
        }

        var rest = ValuePattern().Replace(wording, match => key.Values.Contains(match.Value, StringComparer.Ordinal) ? string.Empty : match.Value);
        if (rest.Contains('{', StringComparison.Ordinal) || rest.Contains('}', StringComparison.Ordinal))
        {
            var allowed = key.Values.Length == 0 ? "no value in braces" : "only " + string.Join(", ", key.Values);
            return $"This text's wording may name {allowed}, and has no other brace.";
        }

        return null;
    }

    private static ImmutableArray<VisitorTextKey> Build()
    {
        var keys = new List<VisitorTextKey>();
        keys.AddRange(FormWords.Select(word => new VisitorTextKey(FormPrefix + word.Name, word.Group, [.. word.Values])));
        foreach (var (name, text) in MessageTexts(VisitorMessageWords.Of("en")))
        {
            keys.Add(new VisitorTextKey(MessagePrefix + name, "errors", ValuesOf(text)));
        }

        keys.Add(new VisitorTextKey(AnswerPrefix + "yes", "answers", []));
        keys.Add(new VisitorTextKey(AnswerPrefix + "no", "answers", []));
        return [.. keys];
    }

    /// <summary>
    /// Every text of a message table by its name after <see cref="MessagePrefix"/>: a counting word's forms as
    /// <c>name.one</c> and <c>name.other</c>, a map's entries as <c>name.entry</c>.
    /// </summary>
    /// <param name="words">The table.</param>
    public static IEnumerable<(string Name, string Text)> MessageTexts(VisitorMessageWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        foreach (var property in typeof(VisitorMessageWords).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(property => property.MetadataToken))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            switch (property.GetValue(words))
            {
                case string text:
                    yield return (name, text);
                    break;
                case PluralWords plural:
                    yield return (name + ".one", plural.One);
                    yield return (name + ".other", plural.Other);
                    break;
                case IReadOnlyDictionary<string, string> names:
                    foreach (var (entry, text) in names.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        yield return (name + "." + entry, text);
                    }

                    break;
            }
        }
    }

    [GeneratedRegex(@"\{[A-Za-z0-9]+\}")]
    private static partial Regex ValuePattern();
}
