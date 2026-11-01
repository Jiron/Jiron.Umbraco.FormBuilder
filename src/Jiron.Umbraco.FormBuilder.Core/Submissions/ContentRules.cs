using System.Collections.Immutable;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The content rules of a submission (ADR-0012, ADR-0140): a blocked email domain in an email field, each blocked word in
/// the texts, and more links than the site allows. They read only what a visitor typed: the answers of text fields, the
/// parts of a name or an address and an "Other" text, so never an option's value, a file or a value the server resolves.
/// <c>strict</c> doubles the weights, and where no link is allowed, it also counts the answer of a web address field.
/// </summary>
internal static class ContentRules
{
    private const int BlockedDomainScore = 100;
    private const int BlockedWordScore = 40;
    private const int TooManyLinksScore = 40;
    private const string EmailType = "email";
    private const string AddressType = "url";

    // What starts a link. A bare domain is not one, since people write them in plain sentences.
    private static readonly ImmutableArray<string> LinkStarts = ["http://", "https://", "ftp://", "www."];

    /// <summary>The signals of a submission's answers.</summary>
    /// <param name="settings">The site's rules.</param>
    /// <param name="level">How strictly to judge: <c>off</c> finds nothing.</param>
    /// <param name="definition">The form.</param>
    /// <param name="answers">The answers as they were read.</param>
    /// <param name="types">The field types, which say whose answers are text.</param>
    public static IEnumerable<(int Score, string Reason)> Check(ProtectionSettings settings, ContentRuleLevel level, FormDefinition definition, IEnumerable<EntryFieldAnswer> answers, FieldTypeRegistry types)
    {
        if (level == ContentRuleLevel.Off)
        {
            return [];
        }

        var weight = level == ContentRuleLevel.Strict ? 2 : 1;
        var fields = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).ToDictionary(field => field.Key);
        var texts = new List<string>();
        var emails = new List<string>();
        var addresses = new List<string>();
        foreach (var answer in answers)
        {
            if (!fields.TryGetValue(answer.Field, out var field) || types.FindType(field.Type) is not { Declaration: { ServerOnlyDefault: false } declaration })
            {
                continue;
            }

            if (declaration.RuleKind == FieldKind.Text)
            {
                (field.Type switch { EmailType => emails, AddressType => addresses, _ => texts }).AddRange(answer.Values.Select(value => value.Text));
                continue;
            }

            foreach (var value in answer.Values)
            {
                if (value.IsOwnText)
                {
                    texts.Add(value.Text);
                }
                else if (declaration.Storage == FieldStorageKind.Json && FieldPartsJson.Read(value.Text) is { } parts)
                {
                    texts.AddRange(parts.Select(part => part.Value));
                }
            }
        }

        var signals = new List<(int Score, string Reason)>();
        if (emails.Any(email => HasBlockedDomain(email, settings.BlockedDomains)))
        {
            signals.Add((BlockedDomainScore * weight, SpamReasons.BlockedDomain));
        }

        var folded = TextSemantics.Fold(string.Join('\n', texts.Concat(emails).Concat(addresses)));
        signals.AddRange(settings.BlockedWords.Where(word => HasWord(folded, TextSemantics.Fold(word))).Select(_ => (BlockedWordScore * weight, SpamReasons.BlockedWord)));

        var counted = level == ContentRuleLevel.Strict && settings.MaxLinks == 0 ? texts.Concat(addresses) : texts;
        if (counted.Sum(CountLinks) > settings.MaxLinks)
        {
            signals.Add((TooManyLinksScore * weight, SpamReasons.TooManyLinks));
        }

        return signals;
    }

    /// <summary>A domain as it is compared: lower case, in its ASCII form, without a dot at its end; null for what cannot be one.</summary>
    /// <param name="domain">The domain as written.</param>
    public static string? Domain(string domain)
    {
        try
        {
            return new IdnMapping { UseStd3AsciiRules = true }.GetAscii(domain.Trim().TrimStart('@').TrimEnd('.')).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Whether the address's domain is a blocked one or lies under one.
    private static bool HasBlockedDomain(string email, IReadOnlyList<string> blocked)
    {
        if (email.LastIndexOf('@') is not (>= 0 and var at) || Domain(email[(at + 1)..]) is not { } domain)
        {
            return false;
        }

        return blocked.Any(other => domain == other || domain.EndsWith("." + other, StringComparison.Ordinal));
    }

    // Whether the word stands in the text as a word of its own, so "class" does not hold "lass".
    private static bool HasWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var starts = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var ends = at + word.Length == text.Length || !char.IsLetterOrDigit(text[at + word.Length]);
            if (starts && ends)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountLinks(string text)
    {
        var links = 0;
        foreach (var start in LinkStarts)
        {
            for (var at = text.IndexOf(start, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(start, at + start.Length, StringComparison.OrdinalIgnoreCase))
            {
                // Not the www of https://www., nor one within a word or an address.
                if (at == 0 || !(char.IsLetterOrDigit(text[at - 1]) || text[at - 1] is '/' or '@' or '.' or '-'))
                {
                    links++;
                }
            }
        }

        return links;
    }
}
