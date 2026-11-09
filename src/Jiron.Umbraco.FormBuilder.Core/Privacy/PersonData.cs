using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>Whom the privacy tools look for (ADR-0223): an email address or a member, never both.</summary>
internal sealed record PersonQuery
{
    private PersonQuery(string? email, Guid? member) => (Email, Member) = (email, member);

    /// <summary>The address, trimmed; null when the query is by member.</summary>
    public string? Email { get; }

    /// <summary>The member; null when the query is by address.</summary>
    public Guid? Member { get; }

    /// <summary>What the query is by, as the audit names it: <c>email</c> or <c>member</c>.</summary>
    public string By => Email is null ? "member" : "email";

    /// <summary>A query of one address or one member.</summary>
    /// <param name="email">An address.</param>
    /// <param name="member">A member's key.</param>
    /// <exception cref="FormBuilderValidationException">Neither or both, or the address is not one.</exception>
    public static PersonQuery Of(string? email, Guid? member)
    {
        var trimmed = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if ((trimmed is null) == (member is null || member == Guid.Empty))
        {
            throw FormBuilderValidationException.For("person_invalid", "email", "Look for an email address or a member.");
        }

        if (trimmed is not null && !EmailAddresses.IsOne(trimmed))
        {
            throw FormBuilderValidationException.For("email_invalid", "email", "Enter one email address, such as name@example.com.");
        }

        return new PersonQuery(trimmed, trimmed is null ? member : null);
    }
}

/// <summary>An entry found for a person.</summary>
/// <param name="Id">The entry's row.</param>
/// <param name="Key">The entry.</param>
/// <param name="Form">Its form.</param>
/// <param name="Number">Its number.</param>
/// <param name="State">Its state; a draft is a form saved to finish later.</param>
/// <param name="IsTest">Whether it is a test entry.</param>
/// <param name="Received">When it came.</param>
/// <param name="Files">Its files.</param>
/// <param name="Emails">Its mail.</param>
/// <param name="HeldUntil">The end of its legal hold, while one lasts.</param>
internal sealed record PersonEntry(long Id, Guid Key, StoredForm Form, int Number, EntryState State, bool IsTest, DateTimeOffset Received, int Files, int Emails, DateTimeOffset? HeldUntil);

/// <summary>A person's entries, newest first.</summary>
/// <param name="Entries">At most <see cref="PersonData.MaxFound"/>.</param>
/// <param name="More">Whether there are more than that.</param>
/// <param name="Addresses">The addresses the person was found by: the one asked for, or the member's.</param>
internal sealed record PersonFound(IReadOnlyList<PersonEntry> Entries, bool More, IReadOnlyList<string> Addresses);

/// <summary>What an erase takes of one form.</summary>
/// <param name="Form">The form.</param>
/// <param name="Entries">Its entries that go or are anonymised.</param>
/// <param name="Drafts">Its drafts, which go.</param>
/// <param name="Held">Its entries a legal hold keeps.</param>
/// <param name="Files">The files of its entries that go.</param>
/// <param name="Emails">The mail of its entries that goes.</param>
internal sealed record PersonEraseForm(StoredForm Form, int Entries, int Drafts, int Held, int Files, int Emails);

/// <summary>What an erase would take (ADR-0223).</summary>
/// <param name="Forms">Per form, most entries first.</param>
/// <param name="Entries">The entries.</param>
/// <param name="Drafts">The drafts.</param>
/// <param name="Held">The entries a hold keeps, which it leaves.</param>
/// <param name="Files">The files that go.</param>
/// <param name="Emails">The mail that goes: the entries' own and any other to the person.</param>
/// <param name="More">Whether the person has more entries than one erase takes.</param>
internal sealed record PersonErasePreview(IReadOnlyList<PersonEraseForm> Forms, int Entries, int Drafts, int Held, int Files, int Emails, bool More);

/// <summary>How an erase treats the entries.</summary>
internal enum PersonEraseMode
{
    /// <summary>They go, with everything of theirs.</summary>
    Delete,

    /// <summary>They stay for the counts, without anything about the person.</summary>
    Anonymise,
}

/// <summary>What an erase took.</summary>
/// <param name="Mode">How it treated the entries.</param>
/// <param name="Entries">The entries deleted or anonymised.</param>
/// <param name="Drafts">The drafts deleted.</param>
/// <param name="Files">The files deleted.</param>
/// <param name="Emails">The mails deleted.</param>
/// <param name="Held">The entries a hold kept.</param>
/// <param name="AuditId">The audit entry that records it.</param>
internal sealed record PersonErased(PersonEraseMode Mode, int Entries, int Drafts, int Files, int Emails, int Held, long AuditId);

/// <summary>
/// The privacy tools (ADR-0223): finding a person's entries in every form by an email field's value, by the members with
/// that address, by the mail sent to them as a visitor, or by member; exporting everything about them; holding their
/// entries; and erasing them, deleted or anonymised, with every mail to them and the recipient of every forward to them.
/// Each action finds the entries again, so no request reaches entries that are not the person's. The audit log records
/// what was done and how much, never whom it was about.
/// </summary>
/// <param name="people">What the tools read and write beyond the entries.</param>
/// <param name="entries">The entries.</param>
/// <param name="forms">The forms.</param>
/// <param name="members">Umbraco's members.</param>
/// <param name="deletions">Deletes entries.</param>
/// <param name="anonymiser">Anonymises entries.</param>
/// <param name="holds">Legal holds.</param>
/// <param name="jobs">Background exports.</param>
/// <param name="audit">The audit log.</param>
/// <param name="time">The clock.</param>
internal sealed class PersonData(
    IPersonDataRepository people,
    IEntryRepository entries,
    IFormRepository forms,
    IMemberService members,
    EntryDeletions deletions,
    EntryAnonymiser anonymiser,
    EntryHolds holds,
    EntryExportJobs jobs,
    IAuditRepository audit,
    TimeProvider time)
{
    /// <summary>The most entries one find lists and one erase takes.</summary>
    public const int MaxFound = 500;

    /// <summary>The entity type of the tools' audit entries, which have no key: they name no person.</summary>
    public const string EntityType = "person";

    /// <summary>The audit action of a find.</summary>
    public const string FoundAction = "people.found";

    /// <summary>The audit action of an erase.</summary>
    public const string ErasedAction = "people.erased";

    // How many mails or events one read takes while an erase looks for the address.
    private const int Page = 200;

    // The most members one address is looked up among.
    private const int MembersByAddress = 50;

    /// <summary>Finds a person's entries, and audits that someone looked.</summary>
    /// <param name="query">Whom.</param>
    /// <param name="userKey">The user who looks.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<PersonFound> FindAsync(PersonQuery query, Guid userKey, CancellationToken cancellationToken = default)
    {
        var found = await LookAsync(query, cancellationToken);
        await audit.AddAsync(new NewAuditEntry(FoundAction, EntityType, null, userKey, new AuditDetails().AddIdentifier("by", query.By).Add("entries", found.Entries.Count).Add("more", found.More)), cancellationToken);
        return found;
    }

    /// <summary>What an erase of the person would take, for the user to confirm.</summary>
    /// <param name="query">Whom.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<PersonErasePreview> PreviewAsync(PersonQuery query, CancellationToken cancellationToken = default)
    {
        var found = await LookAsync(query, cancellationToken);
        var now = time.GetUtcNow();
        var free = found.Entries.Where(entry => !EntryDeletions.IsHeld(entry.HeldUntil, now)).ToList();
        var forms = found.Entries
            .GroupBy(entry => entry.Form.Id)
            .Select(group => new PersonEraseForm(
                group.First().Form,
                group.Count(entry => entry.State != EntryState.Draft && !EntryDeletions.IsHeld(entry.HeldUntil, now)),
                group.Count(entry => entry.State == EntryState.Draft),
                group.Count(entry => EntryDeletions.IsHeld(entry.HeldUntil, now)),
                group.Where(entry => !EntryDeletions.IsHeld(entry.HeldUntil, now)).Sum(entry => entry.Files),
                group.Where(entry => !EntryDeletions.IsHeld(entry.HeldUntil, now)).Sum(entry => entry.Emails)))
            .OrderByDescending(form => form.Entries + form.Drafts)
            .ThenBy(form => form.Form.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var others = await OtherMailAsync(found, cancellationToken);
        return new PersonErasePreview(
            forms,
            forms.Sum(form => form.Entries),
            forms.Sum(form => form.Drafts),
            forms.Sum(form => form.Held),
            free.Sum(entry => entry.Files),
            free.Sum(entry => entry.Emails) + others.Count,
            found.More);
    }

    /// <summary>
    /// Erases the person: their entries deleted or anonymised, their drafts deleted, every other mail to them deleted and
    /// the recipient of every forward to them blanked. Entries a hold keeps stay as they are, with their mail.
    /// </summary>
    /// <param name="query">Whom.</param>
    /// <param name="mode">How the entries go.</param>
    /// <param name="userKey">The user who erases.</param>
    /// <param name="cancellationToken">Cancels what has not started.</param>
    public async Task<PersonErased> EraseAsync(PersonQuery query, PersonEraseMode mode, Guid userKey, CancellationToken cancellationToken = default)
    {
        var found = await LookAsync(query, cancellationToken);
        var (taken, drafts, files, mails, held) = (0, 0, 0, 0, 0);
        foreach (var group in found.Entries.GroupBy(entry => entry.Form.Id))
        {
            var form = group.First().Form;
            mails += group.Where(entry => !EntryDeletions.IsHeld(entry.HeldUntil, time.GetUtcNow())).Sum(entry => entry.Emails);
            foreach (var chunk in group.Where(entry => entry.State == EntryState.Draft).Select(entry => entry.Id).Chunk(EntryDeletions.MaxEntries))
            {
                var gone = await deletions.EraseAsync(form, chunk, userKey, cancellationToken);
                drafts += gone.Entries;
                files += gone.Files;
            }

            foreach (var chunk in group.Where(entry => entry.State != EntryState.Draft).Select(entry => entry.Id).Chunk(EntryDeletions.MaxEntries))
            {
                if (mode == PersonEraseMode.Delete)
                {
                    var gone = await deletions.EraseAsync(form, chunk, userKey, cancellationToken);
                    (taken, files, held) = (taken + gone.Entries, files + gone.Files, held + gone.Held);
                }
                else
                {
                    var done = await anonymiser.AnonymiseAsync(form, chunk, userKey, cancellationToken);
                    (taken, files, held) = (taken + done.Entries, files + done.Files, held + done.Held);
                }
            }
        }

        var others = await OtherMailAsync(found, cancellationToken);
        foreach (var chunk in others.Chunk(SqlArgs.MaxListLength))
        {
            mails += await people.DeleteMailAsync(chunk, cancellationToken);
        }

        foreach (var address in found.Addresses)
        {
            await BlankForwardsAsync(address, cancellationToken);
        }

        var entry = new NewAuditEntry(
            ErasedAction,
            EntityType,
            null,
            userKey,
            new AuditDetails().Add("mode", mode).Add("entries", taken).Add("drafts", drafts).Add("files", files).Add("emails", mails).Add("held", held));
        var recorded = await audit.AddAsync(entry, cancellationToken);
        return new PersonErased(mode, taken, drafts, files, mails, held, recorded.Id);
    }

    /// <summary>Holds the person's entries among <paramref name="entryKeys"/> until <paramref name="until"/>.</summary>
    /// <param name="query">Whom.</param>
    /// <param name="entryKeys">Which of their entries.</param>
    /// <param name="until">The end, after now and at most <see cref="EntryHolds.MaxDays"/> days ahead.</param>
    /// <param name="reason">Why, on one line.</param>
    /// <param name="userKey">The user who holds them.</param>
    /// <param name="cancellationToken">Cancels what has not started.</param>
    /// <returns>Why the hold was refused, or <see cref="EntryHoldRefusal.None"/>, and how many entries it holds.</returns>
    public async Task<(EntryHoldRefusal Refusal, int Held)> HoldAsync(PersonQuery query, IReadOnlyCollection<Guid> entryKeys, DateTimeOffset until, string reason, Guid userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        var chosen = await ChosenAsync(query, entryKeys, cancellationToken);
        var count = 0;
        foreach (var key in chosen)
        {
            var refusal = await holds.HoldAsync(key, until, reason, userKey, cancellationToken);
            if (refusal is EntryHoldRefusal.End or EntryHoldRefusal.Reason)
            {
                return (refusal, count);
            }

            count += refusal == EntryHoldRefusal.None ? 1 : 0;
        }

        return (EntryHoldRefusal.None, count);
    }

    /// <summary>Ends the holds of the person's entries among <paramref name="entryKeys"/> now.</summary>
    /// <param name="query">Whom.</param>
    /// <param name="entryKeys">Which of their entries.</param>
    /// <param name="userKey">The user who releases them.</param>
    /// <param name="cancellationToken">Cancels what has not started.</param>
    /// <returns>How many it released.</returns>
    public async Task<int> ReleaseAsync(PersonQuery query, IReadOnlyCollection<Guid> entryKeys, Guid userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        var count = 0;
        foreach (var key in await ChosenAsync(query, entryKeys, cancellationToken))
        {
            count += await holds.ReleaseAsync(key, userKey, cancellationToken) == EntryHoldRefusal.None ? 1 : 0;
        }

        return count;
    }

    /// <summary>Queues the export of everything about the person, by the keys of their entries, never the address.</summary>
    /// <param name="query">Whom.</param>
    /// <param name="exporter">Who exports.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryExportRecord> ExportAsync(PersonQuery query, EntryExporter exporter, CancellationToken cancellationToken = default)
    {
        var found = await LookAsync(query, cancellationToken);
        return await jobs.QueuePersonAsync([.. found.Entries.Select(entry => entry.Key)], exporter, cancellationToken);
    }

    // The person's entries: by an email field's value, by members with the address, by the mail sent to them as a
    // visitor, and by member; newest first.
    private async Task<PersonFound> LookAsync(PersonQuery query, CancellationToken cancellationToken)
    {
        var addresses = new List<string>();
        var memberKeys = new HashSet<Guid>();
        if (query.Email is { } email)
        {
            addresses.Add(email);
            // Every member whose address is this one in any case, whatever the database compares case by.
            foreach (var member in members.FindByEmail(email, 0, MembersByAddress, out _, StringPropertyMatchType.Contains))
            {
                if (string.Equals(member.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase))
                {
                    memberKeys.Add(member.Key);
                }
            }
        }
        else if (query.Member is { } key)
        {
            memberKeys.Add(key);
            if ((await members.GetByKeysAsync(key)).FirstOrDefault()?.Email is { } own && EmailAddresses.IsOne(own.Trim()))
            {
                addresses.Add(own.Trim());
            }
        }

        var ids = new HashSet<long>();
        foreach (var address in addresses)
        {
            ids.UnionWith(await people.FindByEmailFieldAsync(EntryTextKey.For(address), MaxFound + 1, cancellationToken));
            if (EmailAddresses.KeyOf(address) is { } mailbox)
            {
                ids.UnionWith(await people.FindByVisitorMailAsync(mailbox, MaxFound + 1, cancellationToken));
            }
        }

        ids.UnionWith(await people.FindByMembersAsync(memberKeys, MaxFound + 1, cancellationToken));
        var newest = ids.OrderDescending().ToList();
        var chosen = newest.Take(MaxFound).ToList();
        var rows = await entries.ListForRetentionAsync(chosen, cancellationToken);
        var copies = await people.CountCopiesAsync(chosen, cancellationToken);
        var byId = new Dictionary<int, StoredForm?>();
        var found = new List<PersonEntry>();
        foreach (var row in rows.OrderByDescending(row => row.Id))
        {
            if (!byId.TryGetValue(row.FormId, out var form))
            {
                byId[row.FormId] = form = await forms.GetByIdAsync(row.FormId, cancellationToken);
            }

            if (form is null)
            {
                continue;
            }

            var counted = copies.GetValueOrDefault(row.Id);
            found.Add(new PersonEntry(row.Id, row.Key, form, row.Number, row.State, row.IsTest, row.SubmittedUtc, counted.Files, counted.Emails, row.HoldUntilUtc));
        }

        return new PersonFound(found, newest.Count > MaxFound, addresses);
    }

    // The person's entries among those asked for.
    private async Task<IReadOnlyList<Guid>> ChosenAsync(PersonQuery query, IReadOnlyCollection<Guid> entryKeys, CancellationToken cancellationToken)
    {
        var asked = entryKeys.ToHashSet();
        return [.. (await LookAsync(query, cancellationToken)).Entries.Where(entry => entry.State != EntryState.Draft && asked.Contains(entry.Key)).Select(entry => entry.Key)];
    }

    // The mail to the person that is not one of their entries' own: a forward to them, or one whose entry went before.
    private async Task<IReadOnlyList<long>> OtherMailAsync(PersonFound found, CancellationToken cancellationToken)
    {
        // An entry's own mail goes with it, or stays with it while a hold keeps it.
        var own = found.Entries.Select(entry => entry.Id).ToHashSet();
        var others = new List<long>();
        foreach (var address in found.Addresses)
        {
            if (EmailAddresses.KeyOf(address) is not { } mailbox)
            {
                continue;
            }

            for (var after = 0L; ;)
            {
                var page = await people.ListMailToAsync(address, mailbox, after, Page, cancellationToken);
                foreach (var mail in page)
                {
                    var toPerson = mail.Addresses.Any(one => EmailAddresses.KeyOf(AddressOf(one)) == mailbox);
                    if (toPerson && (mail.EntryId is not { } entry || !own.Contains(entry)))
                    {
                        others.Add(mail.Id);
                    }
                }

                if (page.Count < Page)
                {
                    break;
                }

                after = page[^1].Id;
            }
        }

        return [.. others.Distinct()];
    }

    // Blanks the recipient of every forward to the address: the forward stays in its entry's timeline, to nobody named.
    private async Task BlankForwardsAsync(string address, CancellationToken cancellationToken)
    {
        var mailbox = EmailAddresses.KeyOf(address);
        for (var after = 0L; ;)
        {
            var page = await people.ListEventsMentioningAsync(EntryForwards.ForwardedEvent, address, after, Page, cancellationToken);
            var toPerson = page.Where(item => mailbox is not null && EmailAddresses.KeyOf(RecipientOf(item.Details)) == mailbox).Select(item => item.Id).ToList();
            if (toPerson.Count > 0)
            {
                await people.ReplaceEventDetailsAsync(toPerson, "{}", cancellationToken);
            }

            if (page.Count < Page)
            {
                return;
            }

            after = page[^1].Id;
        }
    }

    // The address in a stored recipient, which may carry a display name.
    private static string AddressOf(string recipient)
    {
        var open = recipient.LastIndexOf('<');
        var close = recipient.LastIndexOf('>');
        return open >= 0 && close > open ? recipient[(open + 1)..close].Trim() : recipient.Trim();
    }

    // The recipient a forward's details name.
    private static string? RecipientOf(string details)
    {
        try
        {
            using var document = JsonDocument.Parse(details);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.String ? to.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
