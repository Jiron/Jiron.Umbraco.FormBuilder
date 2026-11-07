namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The backoffice addresses the server links to (ADR-0067), such as the entry's link in a team mail. The section's
/// manifests answer them: the section's pathname comes with the section (9.1), the entry's workspace with the entry
/// panel (11.2), and a test there holds each manifest to these values.
/// </summary>
internal static class BackofficeLinks
{
    /// <summary>The pathname of the Forms section, which Umbraco's backoffice routes as <c>/umbraco/section/jfb-forms</c>.</summary>
    public const string SectionPathname = "jfb-forms";

    /// <summary>The entity type of an entry, whose workspace opens the entry panel on its own.</summary>
    public const string EntryEntityType = "jfb-entry";

    /// <summary>The path of the Inbox, after the site's base URL (ADR-0209).</summary>
    public const string Inbox = "/umbraco/section/" + SectionPathname + "/workspace/jfb-inbox";

    /// <summary>The path of an entry's workspace, after the site's base URL.</summary>
    /// <param name="entry">The entry's key.</param>
    public static string Entry(Guid entry) =>
        string.Concat("/umbraco/section/", SectionPathname, "/workspace/", EntryEntityType, "/edit/", entry.ToString("D"));

    /// <summary>The path of a form's Entries view, after the site's base URL (ADR-0209).</summary>
    /// <param name="form">The form's key.</param>
    public static string FormEntries(Guid form) =>
        string.Concat("/umbraco/section/", SectionPathname, "/workspace/jfb-form/edit/", form.ToString("D"), "/view/entries");
}
