using System.Buffers.Text;
using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>Where a resume link stands.</summary>
internal enum DraftStatus
{
    /// <summary>The draft is there, and these are its answers.</summary>
    Found,

    /// <summary>No such draft: an unknown token, one that was submitted, or the form is gone.</summary>
    NotFound,

    /// <summary>The link's time is over.</summary>
    Expired,

    /// <summary>The form is for members, and nobody signed in holds the link.</summary>
    MemberRequired,
}

/// <summary>What a resume link gives back.</summary>
/// <param name="Status">Where the link stands.</param>
/// <param name="Form">The form, for the page that holds several.</param>
/// <param name="Revision">The revision of the form the site serves now, which its answers are named by.</param>
/// <param name="PageKey">The page to go on from; null for the first.</param>
/// <param name="Expires">When the link's time is over.</param>
/// <param name="Values">The answers, as a submission's body names them.</param>
internal sealed record DraftAnswer(DraftStatus Status, Guid Form = default, int Revision = 0, Guid? PageKey = null, DateTimeOffset? Expires = null, IReadOnlyDictionary<string, object>? Values = null);

/// <summary>A draft to store, as the submission pipeline read it.</summary>
/// <param name="FormId">The form's row.</param>
/// <param name="FormVersionId">The row of the published version it was read with.</param>
/// <param name="Form">The form as the site serves it.</param>
/// <param name="Answers">The answers that were read.</param>
/// <param name="Culture">The culture, one of the site's languages.</param>
/// <param name="Page">The page the form was on, which the link goes back to.</param>
/// <param name="Email">The address the link goes to, which only the mail keeps.</param>
/// <param name="CurrentPage">The form's page the visitor was on, as the form knows it; null for the first.</param>
/// <param name="ChallengeId">The id of the challenge that made it; null for a request without one.</param>
/// <param name="Replaces">The draft it takes the place of, whose link stops working; null for none.</param>
internal sealed record NewDraft(
    int FormId,
    int FormVersionId,
    PublishedForm Form,
    SubmissionAnswers Answers,
    string Culture,
    RequestPage Page,
    string Email,
    Guid? CurrentPage,
    string? ChallengeId,
    long? Replaces = null);

/// <summary>
/// Save and resume (section 4.4, ROADMAP 8.6, ADR-0163). A draft is an entry in the state <see cref="EntryState.Draft"/>: it holds
/// its answers but takes no place, no unique answer and no count, and runs nothing. Only the hash of its token is stored, and the token
/// itself goes to the address in the mail. Submitting makes an ordinary entry through the whole pipeline and takes the draft away
/// in that entry's unit. What cannot wait is left out: files, consents and sensitive answers are asked for again.
/// </summary>
internal sealed class EntryDrafts(
    FormBuilderDatabase database,
    EntryAnswerStore store,
    IEntryRepository entries,
    IEntryEventRepository events,
    IEmailRepository emails,
    IFormRepository forms,
    PublishedFormCache cache,
    SubmissionEntries submissions,
    EmailOutbox outbox,
    SiteBaseUrl baseUrls,
    ActionSettlement settlement,
    OutboxWake wake,
    TimeProvider time)
{
    /// <summary>The kind of the event that falls due when a draft's time is over.</summary>
    public const string ExpiresEvent = "draftExpires";

    /// <summary>The name in a link's fragment, which a browser never sends to a server.</summary>
    public const string FragmentName = "jfb-resume";

    /// <summary>The longest token a request may post.</summary>
    public const int MaxTokenLength = 128;

    private const int TokenBytes = 32;

    private static readonly FrozenSet<string> NotKept = new[] { "file", "signature", "consent", "captcha", "ranking", "hidden" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The hash a token is stored by.</summary>
    /// <param name="token">The token as a link gave it.</param>
    public static string HashOf(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Whether a link can come back here: a page with an address, on a site that knows its own.</summary>
    /// <param name="page">The page the form is on.</param>
    /// <param name="culture">The culture.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<bool> HasWayBackAsync(RequestPage page, string culture, CancellationToken cancellationToken = default) =>
        await submissions.PathAsync(page, culture, cancellationToken) is not null && await baseUrls.GetAsync(page.Site, cancellationToken) is not null;

    /// <summary>Whether a challenge was spent by a request that stored nothing.</summary>
    /// <param name="challengeId">The challenge's stored id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<bool> ChallengeSpentAsync(string challengeId, CancellationToken cancellationToken = default) => settlement.ChallengeUsedAsync(challengeId, cancellationToken);

    /// <summary>Spends the challenge of a request that stores nothing but is answered as if it had.</summary>
    /// <param name="challengeId">The challenge's stored id; null for a request without one.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task DropAsync(string? challengeId, CancellationToken cancellationToken = default) =>
        challengeId is null ? Task.CompletedTask : settlement.SpendChallengeAsync(challengeId, cancellationToken);

    /// <summary>
    /// Stores a draft, and queues the mail with its link in that unit. A mail the limits hold back takes the draft with it, as nothing
    /// would ever bring those answers back; the answer is the same.
    /// </summary>
    /// <param name="draft">The draft.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task SaveAsync(NewDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var definition = draft.Form.Definition;
        var path = (await submissions.PathAsync(draft.Page, draft.Culture, cancellationToken))!;
        var baseUrl = (await baseUrls.GetAsync(draft.Page.Site, cancellationToken))!;
        var days = definition.Settings.Submission.Resume!.Days;
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var words = ResumeWords.Of(draft.Culture);
        var link = $"{baseUrl.TrimEnd('/')}{path}#{FragmentName}={token}";
        var prepared = await outbox.PrepareAsync(
            new OutgoingEmail
            {
                Kind = EmailKind.Resume,
                To = [draft.Email],
                Subject = words.Subject,
                Body = $"<p>{WebUtility.HtmlEncode(string.Format(CultureInfo.InvariantCulture, words.Intro, draft.Form.Form.Name, days))}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(words.Button)}</a></p><p>{WebUtility.HtmlEncode(words.Again)}</p><p>{WebUtility.HtmlEncode(words.NotYou)}</p>",
                Culture = draft.Culture,
                ToVisitor = true,
                Site = draft.Page.Site,
            },
            cancellationToken);
        var errors = draft.Answers.Errors.Select(error => error.Field).ToHashSet();
        var kept = KeptFields(definition);
        var answers = draft.Answers.Answers.Where(answer => !errors.Contains(answer.Field) && kept.Contains(answer.Field)).ToList();
        await store.CreateAsync(
            new NewEntry
            {
                Key = Guid.NewGuid(),
                FormId = draft.FormId,
                FormVersionId = draft.FormVersionId,
                State = EntryState.Draft,
                SubmissionId = Guid.NewGuid(),
                ChallengeId = draft.ChallengeId,
                SubmissionHash = HashOf(token),
                Culture = draft.Culture,
                ContentKey = draft.Page.Content,
                PageUrl = path,
                ResumeTokenHash = HashOf(token),
                ResumeExpiresUtc = time.GetUtcNow() + TimeSpan.FromDays(days),
                CurrentPageKey = draft.CurrentPage,
            },
            definition,
            answers,
            FrozenDictionary<Guid, ConsentWording>.Empty,
            async created =>
            {
                if (created.ChallengeId is { } challenge && await settlement.ChallengeUsedAsync(challenge, cancellationToken))
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.ChallengeUsed, "The challenge was spent a moment ago.");
                }

                if (draft.Replaces is { } replaced)
                {
                    await emails.DeleteUnsentAsync(replaced, cancellationToken);
                    await entries.DeleteAsync(replaced, cancellationToken);
                }

                await events.AddAsync(new NewEntryEvent(created.Id, ExpiresEvent, DueIn: TimeSpan.FromDays(days)), cancellationToken);
                if (prepared is null || await outbox.QueueAsync(prepared with { EntryId = created.Id }, cancellationToken) is { State: EmailState.Limited })
                {
                    await DropAsync(created.ChallengeId, cancellationToken);
                    await entries.DeleteAsync(created.Id, cancellationToken);
                }
            },
            cancellationToken);
        wake.Wake();
    }

    /// <summary>What a link gives back: the answers by the form the site serves now, as a later version may have changed its aliases.</summary>
    /// <param name="token">The token as the link gave it.</param>
    /// <param name="member">The member signed in on the request, or null.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<DraftAnswer> ResumeAsync(string token, Guid? member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (await entries.GetByResumeTokenAsync(HashOf(token), cancellationToken) is not { State: EntryState.Draft } draft
            || await forms.GetByIdAsync(draft.FormId, cancellationToken) is not { TrashedUtc: null, PublishedVersionId: not null } row
            || await cache.GetAsync(row.Key, cancellationToken) is not { } served
            || await store.ReadAsync(draft.Id, cancellationToken) is not { } answers)
        {
            return new DraftAnswer(DraftStatus.NotFound);
        }

        if (draft.ResumeExpiresUtc is not { } expires || time.GetUtcNow() >= expires)
        {
            return new DraftAnswer(DraftStatus.Expired);
        }

        var definition = served.Published.Definition;
        if (definition.Settings.Submission.Resume is null)
        {
            return new DraftAnswer(DraftStatus.NotFound);
        }

        if (definition.Settings.Availability.Access != MemberAccess.Anyone && member is null)
        {
            return new DraftAnswer(DraftStatus.MemberRequired);
        }

        var page = definition.Pages.Any(each => each.Key == draft.CurrentPageKey) ? draft.CurrentPageKey : null;
        return new DraftAnswer(DraftStatus.Found, row.Key, served.Published.Form.PublicRevision, page, expires, ValuesOf(answers, definition));
    }

    /// <summary>The draft a submission names, if it is one of this form that has not gone yet; a link that says nothing more never stops a submission.</summary>
    /// <param name="token">The token as the page posted it.</param>
    /// <param name="formId">The form's row.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<long?> FindAsync(string? token, int formId, CancellationToken cancellationToken = default) =>
        token is not null && await entries.GetByResumeTokenAsync(HashOf(token), cancellationToken) is { State: EntryState.Draft } draft && draft.FormId == formId ? draft.Id : null;

    /// <summary>Deletes a draft whose time is over, with any mail still waiting; the claim makes one server do it.</summary>
    /// <param name="entry">The draft.</param>
    /// <param name="due">The event that fell due.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    internal Task ExpireAsync(StoredEntry entry, StoredEntryEvent due, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async db =>
            {
                await RowHolds.HoldFormAsync(db, new SqlNames(db), entry.FormId);
                if (!await events.ClaimDueAsync(due.Id, cancellationToken) || entry.State != EntryState.Draft)
                {
                    return false;
                }

                await emails.DeleteUnsentAsync(entry.Id, cancellationToken);
                return await entries.DeleteAsync(entry.Id, cancellationToken);
            },
            cancellationToken);

    /// <summary>The answers as a submission's body names them, for the fields the form still has by the alias it has now.</summary>
    /// <param name="answers">The draft's answers.</param>
    /// <param name="definition">The form the site serves.</param>
    internal static Dictionary<string, object> ValuesOf(EntryAnswers answers, FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(definition);
        var fields = DefinitionKeys.Fields(definition).ToDictionary(field => field.Key);
        var kept = KeptFields(definition);
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var rows = new Dictionary<string, SortedDictionary<int, Dictionary<string, object>>>(StringComparer.Ordinal);
        foreach (var answer in answers.All)
        {
            if (!kept.Contains(answer.Field) || !fields.TryGetValue(answer.Field, out var field) || field.Alias is not { Length: > 0 } alias)
            {
                continue;
            }

            if (answer.Repeater is { } repeater)
            {
                if (fields.TryGetValue(repeater, out var group) && group.Alias is { Length: > 0 } name)
                {
                    var row = rows.TryGetValue(name, out var found) ? found : rows[name] = [];
                    Write(row.TryGetValue(answer.Row, out var cells) ? cells : row[answer.Row] = new Dictionary<string, object>(StringComparer.Ordinal), alias, answer);
                }

                continue;
            }

            Write(values, alias, answer);
        }

        foreach (var (name, row) in rows)
        {
            values[name] = row.Values.ToList();
        }

        return values;
    }

    // The fields a draft keeps. Not one a form marks as sensitive or keeps only until its actions run, on its own or through its
    // repeatable group, and not one the server fills in itself, which no visitor gets to read back.
    private static HashSet<Guid> KeptFields(FormDefinition definition)
    {
        static bool Keeps(FormField field) =>
            !NotKept.Contains(field.Type) && !field.Sensitive && field.Keep != FieldKeep.UntilActionsRun && field.Prefill is not { MemberMode: PrefillMemberMode.Hide };

        var kept = new HashSet<Guid>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Where(Keeps))
        {
            kept.Add(field.Key);
            kept.UnionWith(field.Fields.Where(Keeps).Select(child => child.Key));
        }

        return kept;
    }

    private static void Write(Dictionary<string, object> target, string alias, EntryAnswer answer)
    {
        var plain = new List<string>();
        foreach (var value in answer.Values.Select(each => each.Value))
        {
            if (value.Row is { } gridRow)
            {
                Add(target, $"{alias}.{gridRow:D}", value.Text);
            }
            else if (answer.Type is "name" or "address" && FieldPartsJson.Read(value.Text) is { } parts)
            {
                foreach (var (part, text) in parts)
                {
                    target[$"{alias}.{part}"] = new List<string> { text };
                }
            }
            else
            {
                plain.Add(value.Text);
            }
        }

        if (plain.Count > 0)
        {
            target[alias] = plain;
        }
    }

    private static void Add(Dictionary<string, object> target, string name, string text)
    {
        if (target.TryGetValue(name, out var found) && found is List<string> list)
        {
            list.Add(text);
        }
        else
        {
            target[name] = new List<string> { text };
        }
    }
}

/// <summary>What a resume mail says, in the visitor's language.</summary>
/// <param name="Subject">Its subject.</param>
/// <param name="Intro">Its first line, with the form's name and the days the link works.</param>
/// <param name="Button">The link's text.</param>
/// <param name="Again">What the form asks for again.</param>
/// <param name="NotYou">What to do when it was someone else.</param>
internal sealed record ResumeWords(string Subject, string Intro, string Button, string Again, string NotYou)
{
    private static readonly FrozenDictionary<string, ResumeWords> Words = new Dictionary<string, ResumeWords>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("Continue your form", "Your answers to {0} are saved. This link takes you back to them for {1} days.", "Continue my form", "Files, consents and sensitive answers are not saved, so the form asks for them again.", "If this was not you, you can ignore this mail."),
        ["de"] = new("Ihr Formular fortsetzen", "Ihre Antworten zu {0} sind gespeichert. Dieser Link f\u00fchrt Sie {1} Tage lang zu ihnen zur\u00fcck.", "Formular fortsetzen", "Dateien, Einwilligungen und vertrauliche Antworten werden nicht gespeichert. Das Formular fragt sie erneut ab.", "Falls Sie das nicht waren, k\u00f6nnen Sie diese E-Mail ignorieren."),
        ["fr"] = new("Reprendre votre formulaire", "Vos r\u00e9ponses \u00e0 {0} sont enregistr\u00e9es. Ce lien vous y ram\u00e8ne pendant {1} jours.", "Reprendre mon formulaire", "Les fichiers, consentements et r\u00e9ponses sensibles ne sont pas enregistr\u00e9s\u00a0: le formulaire les redemande.", "Si ce n\u2019\u00e9tait pas vous, ignorez simplement ce message."),
        ["it"] = new("Riprendi il modulo", "Le tue risposte a {0} sono salvate. Questo link ti riporta a esse per {1} giorni.", "Riprendi il modulo", "File, consensi e risposte riservate non vengono salvati: il modulo li richiede di nuovo.", "Se non hai salvato tu queste risposte, puoi ignorare questa email."),
        ["nl"] = new("Ga verder met uw formulier", "Uw antwoorden op {0} zijn opgeslagen. Deze link brengt u er {1} dagen lang naar terug.", "Verder met mijn formulier", "Bestanden, toestemmingen en vertrouwelijke antwoorden worden niet opgeslagen: het formulier vraagt er opnieuw om.", "Was u dit niet? Dan kunt u deze e-mail negeren."),
        ["es"] = new("Contin\u00fae su formulario", "Sus respuestas a {0} est\u00e1n guardadas. Este enlace le lleva a ellas durante {1} d\u00edas.", "Continuar mi formulario", "Los archivos, consentimientos y respuestas confidenciales no se guardan: el formulario los vuelve a pedir.", "Si no ha sido usted, puede ignorar este correo."),
        ["da"] = new("Forts\u00e6t din formular", "Dine svar p\u00e5 {0} er gemt. Dette link f\u00f8rer dig tilbage til dem i {1} dage.", "Forts\u00e6t min formular", "Filer, samtykker og f\u00f8lsomme svar gemmes ikke, s\u00e5 formularen sp\u00f8rger om dem igen.", "Hvis det ikke var dig, kan du se bort fra denne mail."),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The words of a culture's language, or English.</summary>
    /// <param name="culture">The culture.</param>
    public static ResumeWords Of(string? culture) =>
        culture is { Length: > 0 } && Words.TryGetValue(culture.Split('-')[0], out var words) ? words : Words["en"];
}
