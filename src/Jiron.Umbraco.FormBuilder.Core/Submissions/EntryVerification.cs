using System.Collections.Frozen;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>Where a confirmation link stands.</summary>
internal enum VerificationStatus
{
    /// <summary>It is no link of this site, or its entry is gone.</summary>
    Invalid,

    /// <summary>The entry waits for this confirmation.</summary>
    Waiting,

    /// <summary>The address is confirmed.</summary>
    Confirmed,

    /// <summary>The link's time is over, and the entry still waits: a new link can be asked for.</summary>
    Expired,

    /// <summary>A new link is on its way.</summary>
    Resent,
}

/// <summary>What a confirmation page shows.</summary>
/// <param name="Status">Where the link stands.</param>
/// <param name="Culture">The entry's culture, in which the page speaks; empty when there is none.</param>
internal sealed record VerificationAnswer(VerificationStatus Status, string Culture);

/// <summary>The tokens of confirmation links (ADR-0162): the website's Data Protection writes and reads them.</summary>
internal interface IVerificationTokens
{
    /// <summary>A token for an entry until a time.</summary>
    /// <param name="entry">The entry's key.</param>
    /// <param name="expires">When the link's time is over.</param>
    string Issue(Guid entry, DateTimeOffset expires);

    /// <summary>The entry and time a token names; null for anything this site did not write.</summary>
    /// <param name="token">The token as the link gave it.</param>
    (Guid Entry, DateTimeOffset Expires)? Read(string? token);
}

/// <summary>
/// Email verification, the double opt-in of a form that asks for it (section 4.4, ROADMAP 8.5, ADR-0162). The request for the mail and the
/// time the entry expires are events in the entry's own unit, which the queue of entry changes acts on, so a stopped server loses
/// neither. The link's page changes nothing, as mail scanners follow links: only its button's <c>POST</c> confirms. An entry whose address
/// nobody confirmed, a week after its link's time, is deleted with what it holds.
/// </summary>
internal sealed partial class EntryVerification(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IEntryEventRepository events,
    IActionRunRepository runs,
    IEmailRepository emails,
    IFormRepository forms,
    IAuditRepository audit,
    FormDocuments documents,
    SubmissionEntries submissions,
    ActionSettlement settlement,
    EmailOutbox outbox,
    SiteBaseUrl baseUrls,
    EntryEvents entryEvents,
    EntryRetention retention,
    IVerificationTokens tokens,
    OutboxWake wake,
    IOptions<FormBuilderOptions> options,
    TimeProvider time,
    ILogger<EntryVerification> logger)
{
    /// <summary>The kind of the event that says a mail is to go.</summary>
    public const string RequestedEvent = "verificationRequested";

    /// <summary>The kind of the event that records the mail.</summary>
    public const string SentEvent = "verificationSent";

    /// <summary>The kind of the event that falls due when the entry's time is over.</summary>
    public const string ExpiresEvent = "verificationExpires";

    /// <summary>The kind of the event that records the confirmation.</summary>
    public const string VerifiedEvent = "verified";

    /// <summary>The audit action of an entry nobody confirmed.</summary>
    public const string ExpiredAction = "entry.verificationExpired";

    /// <summary>How long after its link's time an entry that nobody confirmed is kept, so a late visitor can still ask for a new link.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    /// <summary>The least time between two new links, as one visitor's button must not fill a mailbox's daily limit.</summary>
    public static readonly TimeSpan ResendPause = TimeSpan.FromHours(1);

    /// <summary>The confirmation page's path under the public API's base path; its link carries the code as <c>?t=</c>.</summary>
    internal const string LinkPath = "/v1/verify";

    private readonly string _root = options.Value.PublicApi.BasePath + LinkPath;

    /// <summary>How long an entry of this form may wait before it is deleted.</summary>
    /// <param name="definition">The form's version.</param>
    public static TimeSpan TimeOf(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return TimeSpan.FromDays(definition.Settings.Submission.Verification?.Days ?? 0) + Grace;
    }

    /// <summary>Asks for the mail again, for a link whose time is over; once in a while, however often the button is pressed.</summary>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<VerificationAnswer> ResendAsync(string? token, CancellationToken cancellationToken = default)
    {
        var answer = await ReadAsync(token, cancellationToken);
        if (answer.Status != VerificationStatus.Expired)
        {
            return answer;
        }

        var entry = (await entries.GetAsync(tokens.Read(token)!.Value.Entry, cancellationToken))!;
        var timeline = await events.ListForEntryAsync(entry.Id, cancellationToken);
        var pause = timeline.Any(item => item.Kind is SentEvent or RequestedEvent && time.GetUtcNow() - item.OccurredUtc < ResendPause);
        if (!pause && DefinitionOf(await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken)) is { } definition)
        {
            await database.WriteAsync(
                async _ =>
                {
                    await events.AddAsync(new NewEntryEvent(entry.Id, RequestedEvent, DueIn: TimeSpan.Zero), cancellationToken);
                    await events.AddAsync(new NewEntryEvent(entry.Id, ExpiresEvent, DueIn: TimeOf(definition)), cancellationToken);
                    return true;
                },
                cancellationToken);
            wake.Wake();
        }

        return new VerificationAnswer(VerificationStatus.Resent, entry.Culture);
    }

    /// <summary>What a link's page shows. It changes nothing.</summary>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<VerificationAnswer> ReadAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (tokens.Read(token) is not { } read || await entries.GetAsync(read.Entry, cancellationToken) is not { } entry)
        {
            return new VerificationAnswer(VerificationStatus.Invalid, string.Empty);
        }

        if (entry.VerifiedUtc is not null)
        {
            return new VerificationAnswer(VerificationStatus.Confirmed, entry.Culture);
        }

        return entry.State != EntryState.Unverified
            ? new VerificationAnswer(VerificationStatus.Invalid, entry.Culture)
            : new VerificationAnswer(time.GetUtcNow() >= read.Expires ? VerificationStatus.Expired : VerificationStatus.Waiting, entry.Culture);
    }

    /// <summary>Confirms an address, once: the entry arrives, and its submitted and verified actions run together, in the order shown.</summary>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<VerificationAnswer> ConfirmAsync(string? token, CancellationToken cancellationToken = default)
    {
        var answer = await ReadAsync(token, cancellationToken);
        if (answer.Status != VerificationStatus.Waiting)
        {
            return answer;
        }

        var entry = (await entries.GetAsync(tokens.Read(token)!.Value.Entry, cancellationToken))!;
        if (DefinitionOf(await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken)) is not { } definition
            || await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form)
        {
            return new VerificationAnswer(VerificationStatus.Invalid, entry.Culture);
        }

        var queued = SubmissionEntries.Runs(definition, SubmissionEntries.ChosenActions(await events.ListForEntryAsync(entry.Id, cancellationToken)), SubmissionEntries.ArrivalTriggers(definition, verified: true));
        var arrives = SubmissionEntries.ArrivalState(definition);
        var terms = await retention.TermsAsync(form, cancellationToken);
        IReadOnlyList<StoredEntryFile> gone = [];
        StoredEntry? moved;
        try
        {
            moved = await database.WriteAsync(
                async _ =>
                {
                    gone = [];

                    // The move comes first: it holds the form's row, and only one of two presses gets past it.
                    var row = await entries.ChangeStateAsync(entry.Id, EntryState.Unverified, arrives, null, cancellationToken);
                    if (row is null)
                    {
                        return null;
                    }

                    row = await retention.StampAsync(row, terms, cancellationToken);

                    await entries.MarkVerifiedAsync(entry.Id, cancellationToken);
                    await events.AddAsync(new NewEntryEvent(entry.Id, VerifiedEvent, null, null, EntryStateChanges.Details(EntryState.Unverified, arrives)), cancellationToken);
                    if (queued.Count > 0)
                    {
                        await runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), queued, default, cancellationToken);
                    }
                    else
                    {
                        gone = await settlement.SettleInUnitAsync(entry.Id, definition, cancellationToken);
                    }

                    return row;
                },
                cancellationToken);
        }
        catch (FormBuilderConcurrencyException)
        {
            return await ReadAsync(token, cancellationToken);
        }

        await settlement.DeleteBytesAsync(gone);
        if (moved is null)
        {
            return new VerificationAnswer(VerificationStatus.Invalid, entry.Culture);
        }

        await entryEvents.TellArrivedAsync(moved, form.Alias);
        return new VerificationAnswer(VerificationStatus.Confirmed, entry.Culture);
    }

    /// <summary>Asks for the mail and starts the entry's time, for one that started waiting only now.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="definition">The version it was submitted with.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    internal async Task RequestAsync(StoredEntry entry, FormDefinition? definition, CancellationToken cancellationToken)
    {
        if (definition is null)
        {
            return;
        }

        await database.WriteAsync(
            async _ =>
            {
                await events.AddAsync(new NewEntryEvent(entry.Id, RequestedEvent, DueIn: TimeSpan.Zero), cancellationToken);
                await events.AddAsync(new NewEntryEvent(entry.Id, ExpiresEvent, DueIn: TimeOf(definition)), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>Queues the mail an event asked for, in one unit with the claim of the event, unless the entry no longer waits.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="due">The event.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    internal async Task SendAsync(StoredEntry entry, StoredEntryEvent due, CancellationToken cancellationToken)
    {
        var verification = entry.State == EntryState.Unverified ? DefinitionOf(await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken))?.Settings.Submission.Verification : null;
        var scope = verification is null ? null : await submissions.ScopeAsync(entry, cancellationToken);
        var form = await forms.GetByIdAsync(entry.FormId, cancellationToken);
        NewEmail? prepared = null;
        if (verification is not null && scope is not null && form is not null)
        {
            var hasBase = await baseUrls.GetAsync(scope.Site, cancellationToken) is not null;
            if (hasBase && scope.Answers.Find(verification.EmailField)?.Values[0].Text?.Trim() is { Length: > 0 } address)
            {
                var link = $"{_root}?t={WebUtility.UrlEncode(tokens.Issue(entry.Key, time.GetUtcNow() + TimeSpan.FromDays(verification.Days)))}";
                var words = VerificationWords.Of(entry.Culture);
                prepared = await outbox.PrepareAsync(
                    new OutgoingEmail
                    {
                        Kind = EmailKind.Verification,
                        To = [address],
                        Subject = words.Subject,
                        Body = $"<p>{WebUtility.HtmlEncode(string.Format(System.Globalization.CultureInfo.InvariantCulture, words.Intro, form.Name))}</p><p><a href=\"{link}\">{WebUtility.HtmlEncode(words.Button)}</a></p><p>{WebUtility.HtmlEncode(words.NotYou)}</p>",
                        Culture = entry.Culture,
                        ToVisitor = true,
                        Site = scope.Site,
                        EntryId = entry.Id,
                    },
                    cancellationToken);
            }
            else
            {
                // Nobody can confirm it, as a link needs the site's own address and no request's: it goes like any other nobody confirms.
                LogNoLink(logger, entry.Key, hasBase);
            }
        }

        await database.WriteAsync(
            async _ =>
            {
                if (!await events.ClaimDueAsync(due.Id, cancellationToken))
                {
                    return false;
                }

                if (prepared is not null)
                {
                    await outbox.QueueAsync(prepared, cancellationToken);
                    await events.AddAsync(new NewEntryEvent(entry.Id, SentEvent), cancellationToken);
                }

                return true;
            },
            cancellationToken);
    }

    /// <summary>Deletes an entry whose time is over, when a later link gave it no more, with what it holds and any mail still waiting.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="due">The event that fell due.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    internal async Task ExpireAsync(StoredEntry entry, StoredEntryEvent due, CancellationToken cancellationToken)
    {
        var later = (await events.ListForEntryAsync(entry.Id, cancellationToken)).Last(item => item.Kind == ExpiresEvent).Id != due.Id;
        var files = await entries.ListFilesAsync(entry.Id, cancellationToken);
        var form = await forms.GetByIdAsync(entry.FormId, cancellationToken);
        var deleted = await database.WriteAsync(
            async db =>
            {
                await RowHolds.HoldFormAsync(db, new SqlNames(db), entry.FormId);
                if (!await events.ClaimDueAsync(due.Id, cancellationToken) || later)
                {
                    return false;
                }

                // A press that came first has moved it, and then it stays.
                if ((await entries.GetByIdAsync(entry.Id, cancellationToken))?.State != EntryState.Unverified)
                {
                    return false;
                }

                await emails.DeleteUnsentAsync(entry.Id, cancellationToken);
                await audit.AddAsync(new NewAuditEntry(ExpiredAction, FormAudit.EntityType, form?.Key, null, new AuditDetails().Add("entry", entry.Key)), cancellationToken);
                return await entries.DeleteAsync(entry.Id, cancellationToken);
            },
            cancellationToken);
        if (deleted)
        {
            await settlement.DeleteBytesAsync(files);
        }
    }

    private FormDefinition? DefinitionOf(StoredFormVersion? version) => version is null ? null : documents.Readable(version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entry {Entry} waits for its address, but no confirmation mail can go: the site knows its address ({HasBase}), and the entry holds none if it does")]
    private static partial void LogNoLink(ILogger logger, Guid entry, bool hasBase);
}

/// <summary>What the mail and the page of a confirmation say, in the visitor's language (section 2).</summary>
internal sealed record VerificationWords
{
    private static readonly FrozenDictionary<string, VerificationWords> All = new Dictionary<string, VerificationWords>(StringComparer.Ordinal)
    {
        ["en"] = new()
        {
            Subject = "Please confirm your email address",
            Intro = "You filled in {0}. Please confirm that this is your address.",
            Button = "Confirm my address",
            NotYou = "If this wasn't you, you can ignore this mail. Nothing happens without your confirmation.",
            WaitingHeading = "Confirm your address",
            WaitingText = "One step left: press the button to confirm that this is your address.",
            ConfirmedHeading = "Your address is confirmed",
            ConfirmedText = "Thank you. There is nothing more you need to do.",
            ExpiredHeading = "This link is too old",
            ExpiredText = "You can ask for a new link, which goes to the same address.",
            ResendButton = "Send a new link",
            ResentHeading = "A new link is on its way",
            ResentText = "Please look in your inbox.",
            InvalidHeading = "This link doesn't work",
            InvalidText = "It may be incomplete, or its answers may no longer be here. Please fill in the form again.",
        },
        ["de"] = new()
        {
            Subject = "Bitte best\u00e4tigen Sie Ihre E-Mail-Adresse",
            Intro = "Sie haben {0} ausgef\u00fcllt. Bitte best\u00e4tigen Sie, dass dies Ihre Adresse ist.",
            Button = "Meine Adresse best\u00e4tigen",
            NotYou = "Falls Sie das nicht waren, k\u00f6nnen Sie diese E-Mail ignorieren. Ohne Ihre Best\u00e4tigung geschieht nichts.",
            WaitingHeading = "Adresse best\u00e4tigen",
            WaitingText = "Nur noch ein Schritt: Best\u00e4tigen Sie mit der Schaltfl\u00e4che, dass dies Ihre Adresse ist.",
            ConfirmedHeading = "Ihre Adresse ist best\u00e4tigt",
            ConfirmedText = "Vielen Dank. Sie m\u00fcssen nichts weiter tun.",
            ExpiredHeading = "Dieser Link ist zu alt",
            ExpiredText = "Sie k\u00f6nnen einen neuen Link anfordern. Er geht an dieselbe Adresse.",
            ResendButton = "Neuen Link senden",
            ResentHeading = "Ein neuer Link ist unterwegs",
            ResentText = "Bitte sehen Sie in Ihrem Postfach nach.",
            InvalidHeading = "Dieser Link funktioniert nicht",
            InvalidText = "Er ist vielleicht unvollst\u00e4ndig, oder Ihre Angaben sind nicht mehr hier. Bitte f\u00fcllen Sie das Formular noch einmal aus.",
        },
        ["fr"] = new()
        {
            Subject = "Veuillez confirmer votre adresse e-mail",
            Intro = "Vous avez rempli {0}. Veuillez confirmer qu\u2019il s\u2019agit bien de votre adresse.",
            Button = "Confirmer mon adresse",
            NotYou = "Si ce n\u2019\u00e9tait pas vous, ignorez ce message. Rien ne se passe sans votre confirmation.",
            WaitingHeading = "Confirmez votre adresse",
            WaitingText = "Plus qu\u2019une \u00e9tape\u00a0: appuyez sur le bouton pour confirmer qu\u2019il s\u2019agit bien de votre adresse.",
            ConfirmedHeading = "Votre adresse est confirm\u00e9e",
            ConfirmedText = "Merci. Vous n\u2019avez rien d\u2019autre \u00e0 faire.",
            ExpiredHeading = "Ce lien est trop ancien",
            ExpiredText = "Vous pouvez demander un nouveau lien, envoy\u00e9 \u00e0 la m\u00eame adresse.",
            ResendButton = "Envoyer un nouveau lien",
            ResentHeading = "Un nouveau lien est en route",
            ResentText = "Veuillez consulter votre bo\u00eete de r\u00e9ception.",
            InvalidHeading = "Ce lien ne fonctionne pas",
            InvalidText = "Il est peut-\u00eatre incomplet, ou vos r\u00e9ponses ne sont plus ici. Veuillez remplir \u00e0 nouveau le formulaire.",
        },
        ["it"] = new()
        {
            Subject = "Conferma il tuo indirizzo email",
            Intro = "Hai compilato {0}. Conferma che questo \u00e8 il tuo indirizzo.",
            Button = "Conferma il mio indirizzo",
            NotYou = "Se non hai compilato tu il modulo, ignora questo messaggio. Senza la tua conferma non succede nulla.",
            WaitingHeading = "Conferma il tuo indirizzo",
            WaitingText = "Manca un solo passaggio: premi il pulsante per confermare che questo \u00e8 il tuo indirizzo.",
            ConfirmedHeading = "Il tuo indirizzo \u00e8 confermato",
            ConfirmedText = "Grazie. Non devi fare altro.",
            ExpiredHeading = "Questo link \u00e8 troppo vecchio",
            ExpiredText = "Puoi chiedere un nuovo link, che arriva allo stesso indirizzo.",
            ResendButton = "Invia un nuovo link",
            ResentHeading = "Un nuovo link \u00e8 in arrivo",
            ResentText = "Controlla la tua casella di posta.",
            InvalidHeading = "Questo link non funziona",
            InvalidText = "Forse \u00e8 incompleto, oppure le tue risposte non sono pi\u00f9 qui. Compila di nuovo il modulo.",
        },
        ["nl"] = new()
        {
            Subject = "Bevestig uw e-mailadres",
            Intro = "U hebt {0} ingevuld. Bevestig dat dit uw adres is.",
            Button = "Mijn adres bevestigen",
            NotYou = "Was u dit niet, dan kunt u deze e-mail negeren. Zonder uw bevestiging gebeurt er niets.",
            WaitingHeading = "Bevestig uw adres",
            WaitingText = "Nog \u00e9\u00e9n stap: druk op de knop om te bevestigen dat dit uw adres is.",
            ConfirmedHeading = "Uw adres is bevestigd",
            ConfirmedText = "Dank u. U hoeft verder niets te doen.",
            ExpiredHeading = "Deze link is te oud",
            ExpiredText = "U kunt een nieuwe link aanvragen, die naar hetzelfde adres gaat.",
            ResendButton = "Nieuwe link sturen",
            ResentHeading = "Er is een nieuwe link onderweg",
            ResentText = "Kijk in uw inbox.",
            InvalidHeading = "Deze link werkt niet",
            InvalidText = "Hij is misschien onvolledig, of uw antwoorden zijn hier niet meer. Vul het formulier opnieuw in.",
        },
        ["es"] = new()
        {
            Subject = "Confirme su direcci\u00f3n de correo electr\u00f3nico",
            Intro = "Ha rellenado {0}. Confirme que esta es su direcci\u00f3n.",
            Button = "Confirmar mi direcci\u00f3n",
            NotYou = "Si no ha sido usted, ignore este mensaje. Sin su confirmaci\u00f3n no ocurre nada.",
            WaitingHeading = "Confirme su direcci\u00f3n",
            WaitingText = "Solo queda un paso: pulse el bot\u00f3n para confirmar que esta es su direcci\u00f3n.",
            ConfirmedHeading = "Su direcci\u00f3n est\u00e1 confirmada",
            ConfirmedText = "Gracias. No tiene que hacer nada m\u00e1s.",
            ExpiredHeading = "Este enlace es demasiado antiguo",
            ExpiredText = "Puede pedir un enlace nuevo, que ir\u00e1 a la misma direcci\u00f3n.",
            ResendButton = "Enviar un enlace nuevo",
            ResentHeading = "Un enlace nuevo va de camino",
            ResentText = "Consulte su bandeja de entrada.",
            InvalidHeading = "Este enlace no funciona",
            InvalidText = "Quiz\u00e1 est\u00e9 incompleto, o sus respuestas ya no est\u00e9n aqu\u00ed. Rellene el formulario de nuevo.",
        },
        ["da"] = new()
        {
            Subject = "Bekr\u00e6ft din e-mailadresse",
            Intro = "Du har udfyldt {0}. Bekr\u00e6ft, at dette er din adresse.",
            Button = "Bekr\u00e6ft min adresse",
            NotYou = "Hvis det ikke var dig, kan du ignorere denne mail. Uden din bekr\u00e6ftelse sker der ingenting.",
            WaitingHeading = "Bekr\u00e6ft din adresse",
            WaitingText = "Kun \u00e9t trin mere: tryk p\u00e5 knappen for at bekr\u00e6fte, at dette er din adresse.",
            ConfirmedHeading = "Din adresse er bekr\u00e6ftet",
            ConfirmedText = "Tak. Du beh\u00f8ver ikke g\u00f8re mere.",
            ExpiredHeading = "Dette link er for gammelt",
            ExpiredText = "Du kan bede om et nyt link, som sendes til samme adresse.",
            ResendButton = "Send et nyt link",
            ResentHeading = "Et nyt link er p\u00e5 vej",
            ResentText = "Se efter i din indbakke.",
            InvalidHeading = "Dette link virker ikke",
            InvalidText = "Det er m\u00e5ske ufuldst\u00e6ndigt, eller dine svar er her ikke l\u00e6ngere. Udfyld formularen igen.",
        },
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The mail's subject.</summary>
    public required string Subject { get; init; }

    /// <summary>The mail's first line, with the form's name for <c>{0}</c>.</summary>
    public required string Intro { get; init; }

    /// <summary>The words of the link and the page's button.</summary>
    public required string Button { get; init; }

    /// <summary>What the mail says to someone who did not fill in the form.</summary>
    public required string NotYou { get; init; }

    /// <summary>The page's heading while the entry waits.</summary>
    public required string WaitingHeading { get; init; }

    /// <summary>The page's text while the entry waits.</summary>
    public required string WaitingText { get; init; }

    /// <summary>The page's heading once confirmed.</summary>
    public required string ConfirmedHeading { get; init; }

    /// <summary>The page's text once confirmed.</summary>
    public required string ConfirmedText { get; init; }

    /// <summary>The page's heading for a link whose time is over.</summary>
    public required string ExpiredHeading { get; init; }

    /// <summary>The page's text for a link whose time is over.</summary>
    public required string ExpiredText { get; init; }

    /// <summary>The words of the button that asks for a new link.</summary>
    public required string ResendButton { get; init; }

    /// <summary>The page's heading once a new link was asked for.</summary>
    public required string ResentHeading { get; init; }

    /// <summary>The page's text once a new link was asked for.</summary>
    public required string ResentText { get; init; }

    /// <summary>The page's heading for a link that is no link.</summary>
    public required string InvalidHeading { get; init; }

    /// <summary>The page's text for a link that is no link.</summary>
    public required string InvalidText { get; init; }

    /// <summary>The words of a culture's language, or English.</summary>
    /// <param name="culture">The culture.</param>
    public static VerificationWords Of(string? culture) =>
        All.TryGetValue((culture ?? string.Empty).Split('-')[0].ToLowerInvariant(), out var words) ? words : All["en"];

    /// <summary>The languages this speaks.</summary>
    public static IEnumerable<string> Languages => All.Keys;
}
