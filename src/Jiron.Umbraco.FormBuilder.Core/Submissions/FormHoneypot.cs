using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// A form's honeypot (ADR-0012): a text input no visitor sees, whose name comes from the form's key and its public
/// revision, so it is the same in every cached copy of one revision and different in the next. The name starts with a
/// digit and holds a hyphen, which no field alias can (ADR-0002), so it is no field's name and, after the instance id,
/// no field's id; it never starts with <c>jfb-</c>, the prefix of Jiron Form Builder's own inputs; and it reads as no
/// word a browser fills in by itself. Pages render it from 6.3a on (ADR-0105); the submission pipeline scores it from
/// 7.1.
/// </summary>
internal static class FormHoneypot
{
    /// <summary>The honeypot's name for a form's public revision, such as <c>7f3a-9c1d</c>.</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="revision">The form's public revision.</param>
    public static string NameOf(Guid form, int revision)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"jfb-honeypot:{form:D}:{revision}")));
        var hex = Convert.ToHexStringLower(hash.AsSpan(0, 4));
        return string.Create(CultureInfo.InvariantCulture, $"{hash[4] % 10}{hex[..3]}-{hex[3..7]}");
    }
}
