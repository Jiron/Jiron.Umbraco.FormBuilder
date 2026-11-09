using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// The codes in the links of the mails Jiron Form Builder writes itself, the confirmation and the resume link (ADR-0226).
/// Once a mail is handed to the mail server its stored copy keeps them no longer: otherwise anyone who reads the database,
/// or a backup of it, could confirm an address or open a draft for as long as the copy is kept.
/// </summary>
internal static partial class LinkSecrets
{
    /// <summary>What a link holds in place of its code once its mail has left.</summary>
    public const string Removed = "removed";

    /// <summary>Whether mail of this kind carries a link with a code.</summary>
    /// <param name="kind">The mail's kind.</param>
    public static bool Holds(EmailKind kind) => kind is EmailKind.Verification or EmailKind.Resume;

    /// <summary>A body with the code of every such link replaced by <see cref="Removed"/>.</summary>
    /// <param name="body">The body, HTML or text.</param>
    public static string Mask(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return ResumeCode().Replace(VerifyCode().Replace(body, "${link}" + Removed), "${link}" + Removed);
    }

    [GeneratedRegex("(?<link>" + EntryVerification.LinkPath + "\\?t=)[A-Za-z0-9_%.~+-]+", RegexOptions.CultureInvariant)]
    private static partial Regex VerifyCode();

    [GeneratedRegex("(?<link>#" + EntryDrafts.FragmentName + "=)[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ResumeCode();
}
