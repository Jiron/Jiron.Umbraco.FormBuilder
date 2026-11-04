using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Personal data on a form without a consent field (ROADMAP 9.5e, ADR-0180): a hint, not a warning, since consent is
/// one lawful basis of several and a contact form may rely on another. A field asks for personal data when its type's
/// answers are about a person by their nature (<see cref="FieldTypeDeclaration.PersonalData"/>) or its autocomplete
/// token names, reaches or locates a person; the hint is said at the first such field.
/// </summary>
/// <param name="types">The field types.</param>
internal sealed class ConsentFormCheck(FieldTypeRegistry types) : IFormCheck
{
    /// <summary>Personal data on a form without a consent field (a hint).</summary>
    public const string ConsentMissing = "consent_missing";

    // The autocomplete fields of the HTML standard that name, reach or locate a person, whatever section, address or
    // contact kind comes before them.
    private static readonly FrozenSet<string> PersonalTokens = new[]
    {
        "name", "honorific-prefix", "given-name", "additional-name", "family-name", "honorific-suffix", "nickname", "username",
        "email", "impp", "tel", "tel-national", "tel-area-code", "tel-local", "tel-local-prefix", "tel-local-suffix",
        "street-address", "address-line1", "address-line2", "address-line3", "postal-code", "bday", "bday-day", "bday-month",
        "bday-year", "sex", "photo",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var fields = DefinitionFields.Of(definition).ToList();
        IReadOnlyList<FormCheckIssue> issues = fields.Any(found => types.FindType(found.Field.Type) is ConsentFieldType)
                || fields.Find(found => IsPersonal(found.Field)) is not (not null, { } path)
            ? []
            : [new FormCheckIssue(FormCheckSeverity.Hint, ConsentMissing, path, "The form asks for personal data, such as this field's answer, but has no consent field. Add one if consent is the basis on which the entries are kept and used.")];
        return ValueTask.FromResult(issues);
    }

    /// <summary>A field's autocomplete token: the last word, after any section, address or contact kind; null for none.</summary>
    internal static string? TokenOf(string? autocomplete)
    {
        var words = autocomplete?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var last = words.LastOrDefault(word => !string.Equals(word, "webauthn", StringComparison.OrdinalIgnoreCase));
        return last?.ToLowerInvariant();
    }

    private bool IsPersonal(FormField field) =>
        types.Find(field.Type) is { HoldsValue: true } traits
        && (traits.PersonalData || (TokenOf(field.Autocomplete) is { } token && PersonalTokens.Contains(token)));
}

/// <summary>
/// A field that asks for a card number or a password (ROADMAP 9.5e, ADR-0180, section 1.4): a warning, since Jiron Form
/// Builder is no place for either. Payments belong to a payment provider, and a password typed into a form ends up in
/// its entries, its mail and its exports. A field of a type a visitor types into asks for one when its autocomplete token
/// is a card's or a password's, when its label or placeholder names one in a language visitors read, or, for a card
/// number, when its pattern takes a card's sixteen digits and refuses a twelve-digit number. A card's number named
/// without the card ("Card number") counts only as the whole text, since a membership or a library card has a number too.
/// </summary>
/// <param name="types">The field types.</param>
internal sealed class CardAndPasswordFormCheck(FieldTypeRegistry types) : IFormCheck
{
    /// <summary>A field that asks for a card number (a warning).</summary>
    public const string CardNumberAsked = "card_number_asked";

    /// <summary>A field that asks for a password (a warning).</summary>
    public const string PasswordAsked = "password_asked";

    // Words and phrases that name a payment card or its code, folded to lowercase ASCII, in the visitor languages; each
    // matches whole words.
    private static readonly ImmutableArray<string> CardWords =
    [
        "credit card", "debit card", "cvv", "cvv2", "cvc", "cvc2",
        "carte bancaire", "carte de credit", "carte de debit", "carte bleue", "cryptogramme",
        "carta di credito", "carta di debito",
        "pinpas",
        "tarjeta de credito", "tarjeta de debito",
        "betalingskort",
    ];

    // The starts of words that name a payment card or its code, for the compounds of German, Dutch and Danish.
    private static readonly ImmutableArray<string> CardStarts = ["creditcard", "debitcard", "betaalkaart", "kreditkart", "debitkart", "kartenprufnummer", "kreditkort", "debetkort"];

    // A card's number named without the card, folded as above, which counts only as the whole text.
    private static readonly ImmutableArray<string> CardNumberTexts =
    [
        "card number", "card no", "kartennummer", "numero de carte", "numero di carta", "numero della carta", "kaartnummer",
        "numero de tarjeta", "kortnummer",
    ];

    // Words and phrases that name a password, folded as above.
    private static readonly ImmutableArray<string> PasswordWords = ["mot de passe"];

    // The starts of words that name a password.
    private static readonly ImmutableArray<string> PasswordStarts = ["password", "passwort", "kennwort", "wachtwoord", "contrasena", "adgangskode", "passcode"];

    // A card's sixteen digits, as visitors type them, and a shorter number no card has.
    private static readonly ImmutableArray<string> CardNumbers = ["4111111111111111", "4111 1111 1111 1111", "4111-1111-1111-1111"];
    private const string ShortNumber = "411111111111";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            if (types.Find(field.Type) is not { HoldsValue: true } traits
                || (traits.Features & (FieldFeatures.Placeholder | FieldFeatures.Autocomplete | FieldFeatures.PatternCheck)) == 0)
            {
                continue;
            }

            if (CardAt(field, path) is { } card)
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, CardNumberAsked, card, "This field asks for a card number. Take payments through a payment provider: card data never belongs in a form's entries, mail or exports."));
            }

            if (PasswordAt(field, path) is { } password)
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, PasswordAsked, password, "This field asks for a password, which would end up in the form's entries, mail and exports. Let members sign in instead."));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
    }

    /// <summary>Whether a pattern takes a card's sixteen digits and refuses a twelve-digit number; false for a pattern that does not compile.</summary>
    internal static bool TakesCardNumbers(string pattern)
    {
        try
        {
            var compiled = SafePattern.Compile(pattern);
            return CardNumbers.Any(compiled.IsMatch) && !compiled.IsMatch(ShortNumber);
        }
        catch (LogicException)
        {
            return false;
        }
    }

    /// <summary>
    /// A text folded for the word lists: lowercase, without accents, every run of other characters one space, with a space
    /// at each end. Lone surrogates and noncharacters become U+FFFD first, since .NET refuses to normalise U+FFFE and a
    /// stored text may hold it.
    /// </summary>
    internal static string Folded(string text)
    {
        var folded = new StringBuilder(text.Length + 2).Append(' ');
        foreach (var c in TextSemantics.Normalise(text).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                folded.Append(char.ToLowerInvariant(c));
            }
            else if (folded[^1] != ' ')
            {
                folded.Append(' ');
            }
        }

        return folded[^1] == ' ' ? folded.ToString() : folded.Append(' ').ToString();
    }

    // Whether a folded text names one of the words, or a word that starts with one of the starts.
    private static bool Names(string folded, ImmutableArray<string> words, ImmutableArray<string> starts) =>
        words.Any(word => folded.Contains($" {word} ", StringComparison.Ordinal)) || starts.Any(start => folded.Contains($" {start}", StringComparison.Ordinal));

    // Where a field asks for a card number: its autocomplete token, its label or placeholder in a culture, or a pattern.
    private static string? CardAt(FormField field, string path)
    {
        if (ConsentFormCheck.TokenOf(field.Autocomplete) is { } token && token.StartsWith("cc-", StringComparison.Ordinal))
        {
            return JsonPaths.Property(path, "autocomplete");
        }

        if (TextAt(field, path, folded => Names(folded, CardWords, CardStarts) || CardNumberTexts.Any(text => folded == $" {text} ")) is { } text)
        {
            return text;
        }

        var checks = JsonPaths.Property(path, "validation");
        for (var i = 0; i < field.Validation.Count; i++)
        {
            if (field.Validation[i] is PatternCheck pattern && TakesCardNumbers(pattern.Pattern))
            {
                return JsonPaths.Property(JsonPaths.Index(checks, i), "pattern");
            }
        }

        return null;
    }

    private static string? PasswordAt(FormField field, string path) =>
        ConsentFormCheck.TokenOf(field.Autocomplete) is "current-password" or "new-password"
            ? JsonPaths.Property(path, "autocomplete")
            : TextAt(field, path, folded => Names(folded, PasswordWords, PasswordStarts));

    // The first culture's label, then placeholder, whose folded text asks for it.
    private static string? TextAt(FormField field, string path, Func<string, bool> asks)
    {
        foreach (var (name, text) in new[] { ("label", field.Label), ("placeholder", field.Placeholder) })
        {
            foreach (var (culture, value) in text?.Texts ?? [])
            {
                if (asks(Folded(value)))
                {
                    return JsonPaths.Property(JsonPaths.Property(path, name), culture);
                }
            }
        }

        return null;
    }
}

/// <summary>
/// A file field without a size of its own (ROADMAP 9.5e, ADR-0180): a hint, since such a field takes every file up to
/// the site's limit (<c>Files:MaxFileBytes</c>), which is set for the largest file any form takes, and each entry may
/// hold as many such files as the field allows. The types and the number of files always have limits of their own. A
/// size the host locks is none an editor can give, so it gets no hint.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="host">The host's limits.</param>
/// <param name="locks">The host's locks of field settings.</param>
internal sealed class FileLimitFormCheck(FieldTypeRegistry types, FieldHostLimits host, FieldSettingLocks locks) : IFormCheck
{
    /// <summary>A file field without a size of its own (a hint).</summary>
    public const string FileSizeUnset = "file_size_unset";

    private const string MaxBytes = "maxBytes";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var limit = string.Create(CultureInfo.InvariantCulture, $"{host.MaxFileBytes:N0}");
        IReadOnlyList<FormCheckIssue> issues =
        [
            .. DefinitionFields.Of(definition)
                .Where(found => types.FindType(found.Field.Type) is FileFieldType
                    && !locks.Of(found.Field.Type).Any(settingLock => settingLock.Setting.Name == MaxBytes)
                    && !(found.Field.Settings is { } settings && settings.TryGetProperty(MaxBytes, out var size) && size.ValueKind == JsonValueKind.Number))
                .Select(found => new FormCheckIssue(
                    FormCheckSeverity.Hint,
                    FileSizeUnset,
                    JsonPaths.Property(JsonPaths.Property(found.Path, "settings"), MaxBytes),
                    $"This field takes files up to the site's limit of {limit} bytes each (Files:MaxFileBytes): give it the largest size its answers need.")),
        ];
        return ValueTask.FromResult(issues);
    }
}

/// <summary>
/// A required choice whose every option has a number of places (ROADMAP 9.5e, ADR-0180): a warning, since once every
/// place is taken nobody can send the form. A choice is required always or under a rule; its options are its own, or a
/// library list's that holds them. A choice with "Other" takes any text, and the options a source resolves have no places
/// the form can know.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="lists">The library's option lists.</param>
internal sealed class FullOptionsFormCheck(FieldTypeRegistry types, OptionListService lists) : IFormCheck
{
    /// <summary>A required choice whose every option has a number of places (a warning).</summary>
    public const string OptionsCanFill = "options_can_fill";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            if (types.Find(field.Type) is { } traits
                && traits.Supports(FieldFeatures.Places)
                && (field.Required || field.Rules.Any(rule => rule is { Effect: RuleEffect.Require, Enabled: true }))
                && !TakesOther(field)
                && await OptionsOfAsync(field, lists, cancellationToken) is { Count: > 0 } options
                && options.All(option => option.Places is not null))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, OptionsCanFill, JsonPaths.Property(path, "options"), "Every option of this required field has a number of places, so once all are taken nobody can send the form: add an option without a limit, or let the field be left empty."));
            }
        }

        return issues;
    }

    /// <summary>The options a field holds itself or takes from a library list that holds them; empty for options a source resolves.</summary>
    internal static async Task<IReadOnlyList<FieldOption>> OptionsOfAsync(FormField field, OptionListService lists, CancellationToken cancellationToken)
    {
        if (field.Options is not { } options)
        {
            return [];
        }

        if (options.Source == OptionSource.Items)
        {
            return options.Items;
        }

        return options is { Source: OptionSource.List, OptionList: { } key }
            && await lists.GetAsync(key, cancellationToken) is { IsReadable: true, Kind.HasItems: true } list
            ? list.Items
            : [];
    }

    // Whether the choice takes any text besides its options ("Other").
    private static bool TakesOther(FormField field) =>
        field.Settings is { } settings && settings.TryGetProperty("other", out var other) && other.ValueKind == JsonValueKind.True;
}

/// <summary>
/// A limit that counts entries the form does not keep (ROADMAP 9.5e, ADR-0158, ADR-0180): a warning. A form that keeps no
/// entries forgets each once its actions have run, so its entry limit, a member's one entry, its options' places and its
/// unique answers count only the entries whose actions still run; a field whose answers are kept only until the actions
/// have run gives its places and its unique answers back then.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="lists">The library's option lists.</param>
internal sealed class KeptLimitsFormCheck(FieldTypeRegistry types, OptionListService lists) : IFormCheck
{
    /// <summary>A limit that counts entries the form does not keep (a warning).</summary>
    public const string LimitWithoutEntries = "limit_without_entries";

    private const string FormKeepsNone = "The form keeps no entries, so this limit forgets each entry once its actions have run.";

    private const string FieldKeepsNone = "This field's answers are kept only until the actions have run, so this limit gives them back then.";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        var keepsNone = definition.Settings.Submission.Store == EntryStorage.Never;
        var availability = JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "settings"), "availability");
        if (keepsNone && definition.Settings.Availability.MaxEntries is not null)
        {
            issues.Add(Issue(JsonPaths.Property(availability, "maxEntries"), FormKeepsNone));
        }

        if (keepsNone && definition.Settings.Availability.OnePerMember)
        {
            issues.Add(Issue(JsonPaths.Property(availability, "onePerMember"), FormKeepsNone));
        }

        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            if (!keepsNone && field.Keep != FieldKeep.UntilActionsRun)
            {
                continue;
            }

            var why = keepsNone ? FormKeepsNone : FieldKeepsNone;
            if (types.Find(field.Type) is { } traits && traits.Supports(FieldFeatures.Places)
                && (await FullOptionsFormCheck.OptionsOfAsync(field, lists, cancellationToken)).Any(option => option.Places is not null))
            {
                issues.Add(Issue(JsonPaths.Property(path, "options"), why));
            }

            var checks = JsonPaths.Property(path, "validation");
            for (var i = 0; i < field.Validation.Count; i++)
            {
                if (field.Validation[i] is UniquePerFormCheck)
                {
                    issues.Add(Issue(JsonPaths.Index(checks, i), why));
                }
            }
        }

        return issues;
    }

    private static FormCheckIssue Issue(string path, string message) => new(FormCheckSeverity.Warning, LimitWithoutEntries, path, message);
}

/// <summary>
/// A last outcome with a rule (ROADMAP 9.5e, ADR-0002, ADR-0180): a warning, since visitors whose answers no outcome's
/// rule matches get the built-in thank-you message. A rule that is switched off is none, and so is one that matches
/// every answer: all of no conditions.
/// </summary>
internal sealed class LastOutcomeFormCheck : IFormCheck
{
    /// <summary>A last outcome with a rule (a warning).</summary>
    public const string LastOutcomeHasRule = "last_outcome_has_rule";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var last = definition.Outcomes.Count - 1;
        var rule = last < 0 ? -1 : definition.Outcomes[last].Rules.ToList().FindIndex(rule => rule.Enabled && !(rule.Match == RuleMatch.All && rule.Conditions.Count == 0));
        IReadOnlyList<FormCheckIssue> issues = rule < 0
            ? []
            : [new FormCheckIssue(FormCheckSeverity.Warning, LastOutcomeHasRule, JsonPaths.Index(JsonPaths.Property(JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "outcomes"), last), "rules"), rule), "Visitors whose answers no outcome's rule matches get the built-in thank-you message: leave the last outcome without a rule, so it is there for every other answer.")];
        return ValueTask.FromResult(issues);
    }
}
