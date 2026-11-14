using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Signatures;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>An uploaded file of an answer, as the panel lists it.</summary>
/// <param name="Key">The file, which its authorised download names.</param>
/// <param name="Name">The name it was uploaded with.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Image">Whether it is an image the panel shows in its viewer: PNG, JPEG, GIF or WebP.</param>
/// <param name="Held">Whether it is held back from everybody, quarantined or waiting for its scan.</param>
internal sealed record EntryPanelFile(Guid Key, string Name, string ContentType, long Size, bool Image, bool Held);

/// <summary>
/// A field's answer in the panel (ADR-0208): its texts as the summary page lists them, or masked; its files, a consent's
/// exact text and a typed signature's name where it has them.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Label">Its label in the entry's culture.</param>
/// <param name="Removed">Whether the form's live version no longer has it.</param>
/// <param name="Sensitive">Whether it is sensitive, so its answer is shown only by a reveal.</param>
/// <param name="Masked">Whether its answer is held back until revealed.</param>
/// <param name="NotKept">Whether its answer was kept only until the actions ran, and is gone.</param>
/// <param name="Texts">Its answer's texts; one for each answered row of a repeatable group; none when masked or unanswered.</param>
/// <param name="Files">Its uploaded files.</param>
/// <param name="Consent">The exact text the visitor agreed to, for a consent.</param>
/// <param name="SignedName">The name a typed signature was signed with.</param>
/// <param name="Edit">How the panel edits it, for a user who may edit entries; null otherwise.</param>
internal sealed record EntryPanelAnswer(
    Guid Field,
    string Label,
    bool Removed,
    bool Sensitive,
    bool Masked,
    bool NotKept,
    IReadOnlyList<string> Texts,
    IReadOnlyList<EntryPanelFile> Files,
    string? Consent,
    string? SignedName,
    EntryPanelEdit? Edit = null);

/// <summary>How the panel edits an answer (ADR-0208).</summary>
/// <param name="Kind">The editor: <c>text</c>, <c>longText</c>, <c>number</c>, <c>date</c>, <c>choice</c>, <c>choices</c> or <c>checkbox</c>.</param>
/// <param name="Values">The answer's values now, in the wire form.</param>
/// <param name="Options">The field's own options, for a choice.</param>
/// <param name="Required">Whether the field needs an answer.</param>
internal sealed record EntryPanelEdit(string Kind, IReadOnlyList<string> Values, IReadOnlyList<EntryListOption> Options, bool Required);

/// <summary>A group of the entry's version, with the answers of its fields the rules showed.</summary>
/// <param name="Key">The group.</param>
/// <param name="Title">Its title in the entry's culture, or none.</param>
/// <param name="Answers">Its fields' answers, in the form's order.</param>
internal sealed record EntryPanelGroup(Guid Key, string? Title, IReadOnlyList<EntryPanelAnswer> Answers);

/// <summary>A page of the entry's version, with its groups the rules showed.</summary>
/// <param name="Key">The page.</param>
/// <param name="Title">Its title in the entry's culture, or none.</param>
/// <param name="Groups">Its groups.</param>
internal sealed record EntryPanelPage(Guid Key, string? Title, IReadOnlyList<EntryPanelGroup> Groups);

/// <summary>Where and how an entry was sent.</summary>
/// <param name="Page">The address of the page it was sent from, or none.</param>
/// <param name="Culture">The language it was sent in.</param>
/// <param name="Duration">How many seconds it took to fill in, when known.</param>
/// <param name="Member">The member who sent it while signed in, or none.</param>
/// <param name="MemberName">That member's name, while the member exists.</param>
/// <param name="IpAddress">The address it was sent from, as the form's privacy settings kept it, or none.</param>
/// <param name="SpamReasons">What the spam protection found, as codes.</param>
internal sealed record EntryPanelDetails(string? Page, string Culture, int? Duration, Guid? Member, string? MemberName, string? IpAddress, IReadOnlyList<string> SpamReasons);

/// <summary>One entry as the panel shows it to a user (ADR-0208).</summary>
/// <param name="Key">The entry.</param>
/// <param name="FormKey">Its form.</param>
/// <param name="FormName">Its form's name.</param>
/// <param name="Number">Its number.</param>
/// <param name="Title">Its entry title, or none.</param>
/// <param name="State">Its state.</param>
/// <param name="Received">When it was sent.</param>
/// <param name="ManualApproval">Whether its version waits for a person to approve its entries.</param>
/// <param name="IsTest">Whether it is a test entry.</param>
/// <param name="Details">Where and how it was sent.</param>
/// <param name="Pages">Its answers, by the pages and groups of its version.</param>
/// <param name="Notes">How many notes it has.</param>
/// <param name="HeldUntil">The end of its legal hold, while one keeps it (ADR-0222).</param>
internal sealed record EntryPanel(
    Guid Key,
    Guid FormKey,
    string FormName,
    int Number,
    string? Title,
    EntryState State,
    DateTimeOffset Received,
    bool ManualApproval,
    bool IsTest,
    EntryPanelDetails Details,
    IReadOnlyList<EntryPanelPage> Pages,
    int Notes,
    DateTimeOffset? HeldUntil = null);

/// <summary>
/// Reads one entry for the entry panel (ADR-0208): its header, details and answers in the pages and groups of its
/// version, each sensitive answer masked. Opening it marks it seen once, for everyone, with an <c>opened</c> event; a
/// reveal gives one answer unmasked and writes an audit entry each time.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The timelines.</param>
/// <param name="notes">The notes.</param>
/// <param name="audit">The audit log.</param>
/// <param name="reads">Reads an entry's scope, answers and timeline.</param>
/// <param name="titles">Writes entry titles.</param>
/// <param name="answers">The answers as texts.</param>
/// <param name="documents">Reads stored versions.</param>
/// <param name="types">The field types.</param>
/// <param name="downloads">Which files are held back.</param>
/// <param name="files">The files' bytes, for a typed signature's name.</param>
/// <param name="members">The members.</param>
/// <param name="edits">Which answers the panel edits.</param>
/// <param name="logger">Where a signature that cannot be read goes.</param>
/// <param name="time">The clock, by which a hold still lasts.</param>
internal sealed partial class EntryPanels(
    FormBuilderDatabase database,
    IFormRepository forms,
    IEntryRepository entries,
    IEntryEventRepository events,
    IEntryNoteRepository notes,
    IAuditRepository audit,
    SubmissionEntries reads,
    EntryTitles titles,
    MergeTagAnswerTexts answers,
    FormDocuments documents,
    FieldTypeRegistry types,
    EntryFileDownloads downloads,
    IFormBuilderFileStore files,
    IMemberService members,
    EntryEdits edits,
    ILogger<EntryPanels> logger,
    TimeProvider time)
{
    /// <summary>The kind of the event that records the first time a backoffice user opened an entry.</summary>
    public const string OpenedEvent = "opened";

    /// <summary>The audit action of a reveal; its details name the field, never the value.</summary>
    public const string RevealedAction = "entry.sensitiveRevealed";

    /// <summary>The entity type of the audit entries about one entry.</summary>
    public const string EntityType = "entry";

    // The media types the panel's viewer shows; every other file downloads (IA 6.3).
    private static readonly FrozenSet<string> ImageTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/webp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // The largest signature whose bytes are read for the name it was typed with.
    private const long MaxSignatureBytes = 1024 * 1024;

    private const string SignatureType = "signature";

    /// <summary>
    /// Opens an entry of a form for a user, marking it seen the first time anyone opens it; null when the form has no
    /// such entry, it is a draft, or its version is gone.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="userKey">The user who opens it.</param>
    /// <param name="mayEdit">Whether the user may edit entries, so the answers the panel edits say how.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryPanel?> OpenAsync(Guid formKey, Guid entryKey, Guid userKey, bool mayEdit, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(formKey, entryKey, cancellationToken) is not { } pair)
        {
            return null;
        }

        var (form, entry) = pair;
        if (entry.FirstSeenUtc is null)
        {
            await database.WriteAsync(
                async _ =>
                {
                    if (await entries.MarkSeenAsync(entry.Id, cancellationToken))
                    {
                        await events.AddAsync(new NewEntryEvent(entry.Id, OpenedEvent, userKey), cancellationToken);
                    }

                    return true;
                },
                cancellationToken);
        }

        return await PanelAsync(form, entry, mayEdit, plain: false, cancellationToken);
    }

    /// <summary>
    /// Everything of one entry as the panel reads it, sensitive answers unmasked, for the export of its person's data
    /// (ADR-0223); drafts too. It marks nothing seen and audits nothing, as the export is audited as a whole. Null when its
    /// version is gone.
    /// </summary>
    /// <param name="form">The entry's form.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public Task<EntryPanel?> ReadAllAsync(StoredForm form, StoredEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(entry);
        return PanelAsync(form, entry, mayEdit: false, plain: true, cancellationToken);
    }

    // The panel of an entry: its answers masked where they are sensitive, or none masked when plain.
    private async Task<EntryPanel?> PanelAsync(StoredForm form, StoredEntry entry, bool mayEdit, bool plain, CancellationToken cancellationToken)
    {
        if (await reads.ReadWholeAsync(entry, form, cancellationToken) is not { } read)
        {
            return null;
        }

        var live = await LiveFieldsAsync(form, cancellationToken);
        var shown = Shown(read.Scope).Select(field => plain ? Plain(field) : field).ToList();
        var texts = await answers.TextsAsync(read.Scope, shown.SelectMany(field => field.Fields.Select(child => new AnswerField(child, field)).Prepend(new AnswerField(field, null))), cancellationToken);
        var removedValues = read.Timeline.Any(entryEvent => entryEvent.Kind == ActionSettlement.ValuesRemovedEvent);
        var pages = new List<EntryPanelPage>();
        foreach (var page in read.Scope.Form.Pages.Where(page => !read.Scope.Answers.Hidden.Contains(page.Key)))
        {
            var groups = new List<EntryPanelGroup>();
            foreach (var group in page.Groups.Where(group => !read.Scope.Answers.Hidden.Contains(group.Key)))
            {
                var found = new List<EntryPanelAnswer>();
                foreach (var field in group.Fields.Where(field => HoldsValue(field) && !read.Scope.Answers.Hidden.Contains(field.Key)))
                {
                    var asked = plain ? Plain(field) : field;
                    var answer = await AnswerAsync(asked, read, texts, live, removedValues, cancellationToken);
                    found.Add(plain ? answer with { Sensitive = field.Sensitive || field.Fields.Any(child => child.Sensitive) } : Editable(answer, field, read, mayEdit));
                }

                if (found.Count > 0)
                {
                    groups.Add(new EntryPanelGroup(group.Key, Text(group.Title, read.Scope.Culture, texts), found));
                }
            }

            if (groups.Count > 0)
            {
                pages.Add(new EntryPanelPage(page.Key, Text(page.Title, read.Scope.Culture, texts), groups));
            }
        }

        var memberName = entry.MemberKey is { } member ? (await members.GetByKeysAsync([member])).FirstOrDefault()?.Name : null;
        return new EntryPanel(
            entry.Key,
            form.Key,
            form.Name,
            entry.Number,
            await titles.RenderAsync(read.Scope, entry.Key, cancellationToken),
            entry.State,
            entry.SubmittedUtc,
            read.Scope.Form.Settings.Submission.Approval == ApprovalMode.Manual,
            entry.IsTest,
            new EntryPanelDetails(
                entry.PageUrl,
                entry.Culture,
                entry.DurationSeconds,
                entry.MemberKey,
                memberName,
                entry.IpAddress,
                [.. entry.SpamReasons.Split(',', StringSplitOptions.RemoveEmptyEntries)]),
            pages,
            (await notes.ListAsync(entry.Id, cancellationToken)).Count,
            EntryDeletions.IsHeld(entry.HoldUntilUtc, time.GetUtcNow()) ? entry.HoldUntilUtc : null);
    }

    /// <summary>
    /// One answer of an entry unmasked, for a user who may see sensitive values, with an audit entry naming the entry and
    /// the field when the answer is sensitive; null when the form has no such entry or its version no such field.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="fieldKey">A field of a group of the entry's version, which may be a repeatable group.</param>
    /// <param name="userKey">The user who reveals it.</param>
    /// <param name="mayEdit">Whether the user may edit entries, so a revealed answer the panel edits says how.</param>
    /// <param name="cancellationToken">Cancels the reads and the audit entry.</param>
    public async Task<EntryPanelAnswer?> RevealAsync(Guid formKey, Guid entryKey, Guid fieldKey, Guid userKey, bool mayEdit, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(formKey, entryKey, cancellationToken) is not { } pair)
        {
            return null;
        }

        var (form, entry) = pair;
        if (await reads.ReadWholeAsync(entry, form, cancellationToken) is not { } read
            || Shown(read.Scope).FirstOrDefault(field => field.Key == fieldKey) is not { } field)
        {
            return null;
        }

        var sensitive = field.Sensitive || field.Fields.Any(child => child.Sensitive);
        var plain = Plain(field);
        var texts = await answers.TextsAsync(read.Scope, plain.Fields.Select(child => new AnswerField(child, plain)).Prepend(new AnswerField(plain, null)), cancellationToken);
        var live = await LiveFieldsAsync(form, cancellationToken);
        var answer = await AnswerAsync(plain, read, texts, live, read.Timeline.Any(entryEvent => entryEvent.Kind == ActionSettlement.ValuesRemovedEvent), cancellationToken);
        if (sensitive)
        {
            await audit.AddAsync(
                new NewAuditEntry(RevealedAction, EntityType, entry.Key, userKey, new AuditDetails().Add("form", form.Key).Add("field", field.Key)),
                cancellationToken);
        }

        return Editable(answer, field, read, mayEdit, revealed: true) with { Sensitive = sensitive };
    }

    // A field and its children as if none were sensitive, so their answers read unmasked.
    private static FormField Plain(FormField field) =>
        field with { Sensitive = false, Fields = [.. field.Fields.Select(child => child with { Sensitive = false })] };

    // An answer with how the panel edits it, for a user who may edit: never a masked one, whose values stay on the server,
    // never one kept only until the actions ran and gone since, which an edit would bring back, and never the address the
    // form's email verification confirms (ADR-0252).
    private EntryPanelAnswer Editable(EntryPanelAnswer answer, FormField field, EntryRead read, bool mayEdit, bool revealed = false) =>
        mayEdit && !answer.Masked && !answer.NotKept && !EntryEdits.IsConfirmedAddress(read.Scope.Form, field)
            ? answer with { Edit = edits.EditOf(field, revealed || !field.Sensitive, [.. read.Answers?.Find(field.Key)?.Values.Select(value => value.Value.Text) ?? []]) }
            : answer;

    // The form and its entry, when the entry is the form's and was sent.
    private async Task<(StoredForm Form, StoredEntry Entry)?> FindAsync(Guid formKey, Guid entryKey, CancellationToken cancellationToken) =>
        await forms.GetAsync(formKey, cancellationToken) is { } form
        && await entries.GetAsync(entryKey, cancellationToken) is { } entry
        && entry.FormId == form.Id
        && entry.State != EntryState.Draft
            ? (form, entry)
            : null;

    // The fields of the groups of the entry's version that hold a value, a repeatable group as one.
    private IEnumerable<FormField> Shown(MergeTagScope scope) =>
        scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Where(HoldsValue);

    private bool HoldsValue(FormField field) =>
        MergeTagAnswerTexts.IsRepeater(field) || (types.Find(field.Type) is { } traits ? traits.HoldsValue : field.Alias is not null);

    // The keys of the fields of the version the entries are shown by (FormDocuments.ServedOfAsync).
    private async Task<HashSet<Guid>> LiveFieldsAsync(StoredForm form, CancellationToken cancellationToken)
    {
        var keys = new HashSet<Guid>();
        if (await documents.ServedOfAsync(form, cancellationToken) is { } version)
        {
            foreach (var field in documents.Readable(version).Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
            {
                keys.Add(field.Key);
                keys.UnionWith(field.Fields.Select(child => child.Key));
            }
        }

        return keys;
    }

    // A field's answer: masked where it is sensitive, its files, consent text and typed name where it has them.
    private async Task<EntryPanelAnswer> AnswerAsync(FormField field, EntryRead read, ScopeTexts texts, HashSet<Guid> live, bool removedValues, CancellationToken cancellationToken)
    {
        var (masked, found) = answers.Summary(field, read.Scope, texts);
        var stored = read.Answers?.Find(field.Key);
        var sensitive = field.Sensitive || field.Fields.Any(child => child.Sensitive);
        var notKept = removedValues && stored is null && found.Count == 0 && ActionSettlement.KeptUntilActions(read.Scope.Form, field.Key);
        IReadOnlyList<EntryPanelFile> filed = [];
        string? consent = null;
        string? signed = null;
        if (!masked && stored is not null)
        {
            var uploaded = stored.Values.Select(value => value.File).OfType<StoredEntryFile>().ToList();
            filed = [.. uploaded.Select(file => new EntryPanelFile(file.Key, file.OriginalName, file.ContentType, file.SizeBytes, ImageTypes.Contains(file.ContentType), downloads.IsHeld(file)))];
            consent = stored.Values.Select(value => value.Consent?.Text).FirstOrDefault(text => text is not null);
            if (field.Type == SignatureType && uploaded.FirstOrDefault() is { } signature)
            {
                signed = await SignedNameAsync(signature, cancellationToken);
            }
        }

        return new EntryPanelAnswer(
            field.Key,
            MergeTagAnswerTexts.Label(field, read.Scope.Culture, texts.Dictionary),
            !live.Contains(field.Key),
            sensitive,
            masked,
            notKept,
            found,
            filed,
            consent,
            signed);
    }

    // The name a signature was typed with, read from its PNG; none for a drawn one, or one that cannot be read.
    private async Task<string?> SignedNameAsync(StoredEntryFile file, CancellationToken cancellationToken)
    {
        if (file.SizeBytes > MaxSignatureBytes || downloads.IsHeld(file))
        {
            return null;
        }

        try
        {
            await using var content = await files.OpenReadAsync(file.StoredName, cancellationToken);
            if (content is null)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            return TypedSignature.NameOf(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (IOException exception)
        {
            LogSignatureUnreadable(logger, LoggedException.Of(exception), file.Key);
            return null;
        }
    }

    private static string? Text(LocalizedText? text, string culture, ScopeTexts texts) =>
        texts.Dictionary.Resolve(text, culture) is { Length: > 0 } resolved ? resolved : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The signature {File} could not be read for the name it was typed with.")]
    private static partial void LogSignatureUnreadable(ILogger logger, LoggedException exception, Guid file);
}
