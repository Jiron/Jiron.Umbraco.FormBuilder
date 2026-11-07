using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>A backoffice user an entry may be forwarded to.</summary>
/// <param name="Key">The user.</param>
/// <param name="Name">Their name.</param>
internal sealed record EntryForwardUser(Guid Key, string Name);

/// <summary>Whom an entry may be forwarded to: the users who may view its form's entries, and the site's forward addresses.</summary>
/// <param name="Users">The users with an address who may view the form's entries, by name.</param>
/// <param name="Addresses">The forward addresses of the entry's site (Settings, Email).</param>
internal sealed record EntryForwardRecipients(IReadOnlyList<EntryForwardUser> Users, IReadOnlyList<string> Addresses);

/// <summary>How a forward came out.</summary>
internal enum EntryForwardStatus
{
    /// <summary>The mail is queued.</summary>
    Queued,

    /// <summary>The form has no such entry.</summary>
    NotFound,

    /// <summary>The recipient is no user who may view the form's entries and no forward address of the entry's site.</summary>
    NotAllowed,
}

/// <summary>
/// Forwards an entry to someone the site already trusts (ADR-0208): a backoffice user who may view the form's entries, or
/// one of the forward addresses of the entry's site. The mail holds the sender's message, the answers and the entry's
/// link; sensitive values go along only when the sender may see them and chooses to. It goes through the outbox, is
/// audited, and shows on the timeline.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="forms">The forms.</param>
/// <param name="infos">Reads a form's folder.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The timelines.</param>
/// <param name="audit">The audit log.</param>
/// <param name="outbox">The outbox.</param>
/// <param name="settings">The sites' email settings.</param>
/// <param name="pages">The entry's site.</param>
/// <param name="baseUrls">The sites' base URLs, for the entry's link.</param>
/// <param name="users">The backoffice users.</param>
/// <param name="access">Who may view the form's entries.</param>
/// <param name="reads">Reads an entry's answers.</param>
/// <param name="titles">Writes entry titles.</param>
/// <param name="answers">The answers as texts.</param>
/// <param name="types">The field types.</param>
internal sealed class EntryForwards(
    FormBuilderDatabase database,
    IFormRepository forms,
    FormInfos infos,
    IEntryRepository entries,
    IEntryEventRepository events,
    IAuditRepository audit,
    EmailOutbox outbox,
    IFormBuilderSettingsService settings,
    FormPages pages,
    SiteBaseUrl baseUrls,
    IUserService users,
    IFormBuilderAccessService access,
    SubmissionEntries reads,
    EntryTitles titles,
    MergeTagAnswerTexts answers,
    FieldTypeRegistry types)
{
    /// <summary>The kind of the event that records a forward; its details name the recipient.</summary>
    public const string ForwardedEvent = "forwarded";

    /// <summary>The audit action of a forward.</summary>
    public const string ForwardedAction = "entry.forwarded";

    /// <summary>The longest message.</summary>
    public const int MaxMessageLength = 2000;

    // The most users read for the choice, a page at a time.
    private const int MostUsers = 2000;
    private const int UsersPage = 200;

    private static readonly FrozenDictionary<string, Words> AllWords = new Dictionary<string, Words>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("Entry #{0} of {1}", "{0} forwarded this entry to you.", "Open the entry", "none"),
        ["de"] = new("Eintrag #{0} von {1}", "{0} hat Ihnen diesen Eintrag weitergeleitet.", "Eintrag " + (char)0xF6 + "ffnen", "keine"),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whom an entry of a form may be forwarded to; null when the form has no such entry.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="senderKey">Who forwards it.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryForwardRecipients?> RecipientsAsync(Guid formKey, Guid entryKey, Guid senderKey, CancellationToken cancellationToken = default)
    {
        if (await EntryAsync(formKey, entryKey, cancellationToken) is not { } pair)
        {
            return null;
        }

        var (form, entry) = pair;

        var target = FormAccessTarget.Of(await infos.InfoAsync(form, cancellationToken));
        var allowed = new List<EntryForwardUser>();
        for (var skip = 0; skip < MostUsers; skip += UsersPage)
        {
            var page = await users.GetAllAsync(senderKey, skip, UsersPage);
            var found = page.Result?.Items.ToList() ?? [];
            foreach (var user in found)
            {
                if (await MayGetAsync(user, target, cancellationToken))
                {
                    allowed.Add(new EntryForwardUser(user.Key, user.Name ?? user.Username));
                }
            }

            if (found.Count < UsersPage)
            {
                break;
            }
        }

        return new EntryForwardRecipients(
            [.. allowed.OrderBy(user => user.Name, StringComparer.CurrentCultureIgnoreCase)],
            await AddressesAsync(entry, cancellationToken));
    }

    /// <summary>Forwards an entry of a form to a user or to a forward address.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="senderKey">Who forwards it.</param>
    /// <param name="userKey">The user it goes to, or null for an address.</param>
    /// <param name="address">The forward address it goes to, when no user.</param>
    /// <param name="message">The sender's message, at most <see cref="MaxMessageLength"/> characters, or none.</param>
    /// <param name="includeSensitive">Whether sensitive values go along; the caller checked that the sender may see them.</param>
    /// <param name="cancellationToken">Cancels the reads and the unit of work.</param>
    public async Task<EntryForwardStatus> ForwardAsync(Guid formKey, Guid entryKey, Guid senderKey, Guid? userKey, string? address, string? message, bool includeSensitive, CancellationToken cancellationToken = default)
    {
        if (await EntryAsync(formKey, entryKey, cancellationToken) is not { } pair)
        {
            return EntryForwardStatus.NotFound;
        }

        var (form, entry) = pair;

        string to, recipient, culture;
        var sender = await users.GetAsync(senderKey);
        if (userKey is { } key)
        {
            if (await users.GetAsync(key) is not { } user
                || !await MayGetAsync(user, FormAccessTarget.Of(await infos.InfoAsync(form, cancellationToken)), cancellationToken))
            {
                return EntryForwardStatus.NotAllowed;
            }

            (to, recipient, culture) = (user.Email, user.Name ?? user.Username, user.Language ?? "en");
        }
        else
        {
            var addresses = await AddressesAsync(entry, cancellationToken);
            if (address?.Trim() is not { Length: > 0 } wanted || addresses.FirstOrDefault(item => string.Equals(item, wanted, StringComparison.OrdinalIgnoreCase)) is not { } allowed)
            {
                return EntryForwardStatus.NotAllowed;
            }

            (to, recipient, culture) = (allowed, allowed, sender?.Language ?? "en");
        }

        if (await reads.ReadWholeAsync(entry, form, cancellationToken) is not { } read)
        {
            return EntryForwardStatus.NotFound;
        }

        var site = (await pages.PageAsync(entry.ContentKey)).Site;
        var body = await BodyAsync(read, entry, sender?.Name ?? sender?.Username ?? string.Empty, message, includeSensitive, site, culture, cancellationToken);
        var words = WordsOf(culture);
        // "Entry #3 of Job application", after the entry's title, and a middle dot, when it has one.
        var title = await titles.RenderAsync(read.Scope, entry.Key, cancellationToken);
        var subject = string.Format(CultureInfo.InvariantCulture, words.Subject, entry.Number, form.Name);
        var prepared = await outbox.PrepareAsync(
            new OutgoingEmail
            {
                Kind = EmailKind.Forward,
                To = [to],
                Subject = string.IsNullOrWhiteSpace(title) ? subject : string.Concat(title, " " + (char)0xB7 + " ", subject),
                Body = body,
                Culture = culture,
                Site = site,
                EntryId = entry.Id,
            },
            cancellationToken);
        if (prepared is null)
        {
            return EntryForwardStatus.NotAllowed;
        }

        await database.WriteAsync(
            async _ =>
            {
                await outbox.QueueAsync(prepared, cancellationToken);
                await events.AddAsync(new NewEntryEvent(entry.Id, ForwardedEvent, senderKey, Details: JsonSerializer.Serialize(new { to = recipient, sensitive = includeSensitive })), cancellationToken);
                var details = new AuditDetails().Add("form", form.Key).Add("sensitive", includeSensitive);
                details = userKey is { } user ? details.Add("user", user) : details.Add("teamAddress", true);
                await audit.AddAsync(new NewAuditEntry(ForwardedAction, EntryPanels.EntityType, entry.Key, senderKey, details), cancellationToken);
                return true;
            },
            cancellationToken);
        return EntryForwardStatus.Queued;
    }

    // Whether a user may get the entry: not disabled, with an address, and allowed to view the form's entries.
    private async Task<bool> MayGetAsync(IUser user, FormAccessTarget target, CancellationToken cancellationToken) =>
        user.UserState is not (UserState.Disabled or UserState.LockedOut)
        && !string.IsNullOrWhiteSpace(user.Email)
        && (await access.GetForFormAsync(AccessUser.Of(user), target, cancellationToken)).HasFlag(FormBuilderPermissions.ViewEntries);

    // The forward addresses of the entry's site.
    private async Task<IReadOnlyList<string>> AddressesAsync(StoredEntry entry, CancellationToken cancellationToken) =>
        (await settings.GetAsync<EmailSettings>((await pages.PageAsync(entry.ContentKey)).Site, cancellationToken)).Value.ForwardAddresses;

    // The mail's body: who forwarded it and their message, each answer by its label, and the entry's link.
    private async Task<string> BodyAsync(EntryRead read, StoredEntry entry, string sender, string? message, bool includeSensitive, Guid? site, string culture, CancellationToken cancellationToken)
    {
        var words = WordsOf(culture);
        var html = new StringBuilder();
        html.Append("<p>").Append(Encode(string.Format(CultureInfo.InvariantCulture, words.Forwarded, sender))).Append("</p>");
        if (message?.Trim() is { Length: > 0 } said)
        {
            html.Append("<blockquote>").Append(Encode(said).ReplaceLineEndings("<br>")).Append("</blockquote>");
        }

        var shown = read.Scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
            .Where(field => !read.Scope.Answers.Hidden.Contains(field.Key) && (MergeTagAnswerTexts.IsRepeater(field) || types.Find(field.Type) is { HoldsValue: true }))
            .Select(field => includeSensitive ? field with { Sensitive = false, Fields = [.. field.Fields.Select(child => child with { Sensitive = false })] } : field)
            .ToList();
        var texts = await answers.TextsAsync(read.Scope, shown.SelectMany(field => field.Fields.Select(child => new AnswerField(child, field)).Prepend(new AnswerField(field, null))), cancellationToken);
        html.Append("<table>");
        foreach (var field in shown)
        {
            var (masked, found) = answers.Summary(field, read.Scope, texts);
            var answer = masked ? MergeTagAnswerTexts.Masked : found.Count == 0 ? words.None : string.Join("<br>", found.Select(Encode));
            html.Append("<tr><th align=\"left\">").Append(Encode(MergeTagAnswerTexts.Label(field, read.Scope.Culture, texts.Dictionary))).Append("</th><td>").Append(answer).Append("</td></tr>");
        }

        html.Append("</table>");
        if (await baseUrls.GetAsync(site, cancellationToken) is { } baseUrl)
        {
            html.Append("<p><a href=\"").Append(Encode(baseUrl + BackofficeLinks.Entry(entry.Key))).Append("\">").Append(Encode(words.Open)).Append("</a></p>");
        }

        return html.ToString();
    }

    private static Words WordsOf(string culture) => AllWords.TryGetValue(culture.Split('-')[0], out var found) ? found : AllWords["en"];

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private async Task<(StoredForm Form, StoredEntry Entry)?> EntryAsync(Guid formKey, Guid entryKey, CancellationToken cancellationToken) =>
        await forms.GetAsync(formKey, cancellationToken) is { } form
        && await entries.GetAsync(entryKey, cancellationToken) is { } entry
        && entry.FormId == form.Id
        && entry.State != EntryState.Draft
            ? (form, entry)
            : null;

    private sealed record Words(string Subject, string Forwarded, string Open, string None);
}
