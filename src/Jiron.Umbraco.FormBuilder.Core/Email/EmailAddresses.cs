using System.Globalization;
using System.Net.Mail;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>What header values may be (section 4.10).</summary>
internal static class EmailHeaders
{
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// A header value on one line: every line break and other control character becomes a space, because MimeKit alone would
    /// drop them and glue the words together (ADR-0007), and a mail's headers are never a visitor's to add to.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var cleaned = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            cleaned.Append(char.IsControl(c) || c is LineSeparator or ParagraphSeparator ? ' ' : c);
        }

        return cleaned.ToString().Trim();
    }

    /// <summary>A name and an address as a mail's sender: the name quoted, so that no character in it can start another address.</summary>
    public static string Sender(string? name, string address)
    {
        var line = Clean(name);
        return string.IsNullOrEmpty(line)
            ? address
            : $"\"{line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\" <{address}>";
    }
}

/// <summary>Addresses as mail takes them, and as the limits for visitor mail count them (ADR-0012).</summary>
internal static class EmailAddresses
{
    /// <summary>The longest address (RFC 5321).</summary>
    public const int MaxLength = 254;

    /// <summary>
    /// Whether a text is one address, without a display name, a comment or a second address. A quoted local part is refused: it
    /// can hold commas, and stored recipients are a list split on them, so one such "address" would reach everyone it names.
    /// </summary>
    public static bool IsOne(string? address) =>
        !string.IsNullOrEmpty(address)
        && address.Length <= MaxLength
        && !address.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '"' or '\\')
        && !HasLoneSurrogate(address)
        && MailAddress.TryCreate(address, out var parsed)
        && string.Equals(parsed.Address.Normalize(NormalizationForm.FormC), address.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);

    /// <summary>
    /// The key the limits count a mailbox by (section 4.5): trimmed, normalised with compatibility forms, folded here because
    /// SQLite's <c>LOWER</c> folds ASCII only, without a <c>+tag</c> in its local part, and with its domain as the mail server will
    /// look it up, so every spelling of one mailbox counts as one. Null for a text that is not an address, or whose key is too
    /// long to store.
    /// </summary>
    public static string? KeyOf(string? address)
    {
        var trimmed = address?.Trim();
        if (!IsOne(trimmed))
        {
            return null;
        }

        var at = trimmed!.LastIndexOf('@');
        var local = trimmed[..at].Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var plus = local.IndexOf('+', StringComparison.Ordinal);
        var key = $"{(plus > 0 ? local[..plus] : local)}@{DomainOf(trimmed[(at + 1)..])}";
        return key.Length <= EmailRepository.MaxRecipientKeyLength ? key : null;
    }

    // The domain in the one form every spelling of it comes to: full-width letters, other dots and the punycode of a name alike.
    private static string DomainOf(string domain)
    {
        var folded = domain.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        try
        {
            return new IdnMapping().GetAscii(folded).ToLowerInvariant().TrimEnd('.');
        }
        catch (ArgumentException)
        {
            return folded.TrimEnd('.');
        }
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }
}
