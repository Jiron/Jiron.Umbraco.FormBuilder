using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The submission pipeline of section 4.4 (ADR-0088), which every transport runs: the published form, the gate,
/// idempotency, compatibility, availability, the answers read and checked, and one transaction that stores the entry
/// with its event, its action runs and its count; then the outcome. A page's check runs the same steps but idempotency,
/// the empty submission and the transaction, and answers its own fields' errors; a receipt answers an entry as a retry
/// would (ADR-0089). The places on options and the answers that must be unique are held while the answers are read and
/// again inside the transaction (ADR-0090, ADR-0091).
/// The gate reads the request's challenge (ADR-0138); the spam score comes in 7.1c, and waking the action processor in 8.1.
/// </summary>
internal sealed class FormSubmissions(
    PublishedFormCache cache,
    PreviewForms previews,
    IFormRepository forms,
    IEntryRepository entries,
    FormDocuments documents,
    FormAvailability availability,
    FormPages pages,
    SiteTimeZones timeZones,
    SubmissionReader reader,
    ConsentWordings consents,
    SubmissionEntries recorder,
    OptionPlaces places,
    UniqueAnswers unique,
    FieldTypeRegistry types,
    SubmissionProtection protection,
    EntryDrafts drafts,
    VisitorAddresses addresses,
    SubmittingHandlers handlers,
    TimeProvider time)
{
    /// <summary>The most values a query name may hold.</summary>
    public const int MaxQueryValues = 20;

    /// <summary>The longest query or context value, in UTF-16 code units.</summary>
    public const int MaxValueLength = 2_000;

    /// <summary>Where a page key that names no page of the form is, as a request names it.</summary>
    public const string PageKeyPath = "pageKey";

    /// <summary>Where a draft's address is, as a request names it.</summary>
    public const string DraftEmailPath = "email";

    /// <summary>Runs a submission through the pipeline.</summary>
    /// <param name="request">The submission, as its transport read it.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServedAsync(request.Form, request.Preview, cancellationToken) is not { } served
            || await forms.GetVersionAsync(served.Row.Id, served.Form.Version, cancellationToken) is not { } version)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        if (await GateAsync(request, served.Form) is { } refused)
        {
            return SubmissionResult.Refused(refused);
        }

        request = request with { Ticket = TicketOf(request) };

        // Idempotency, before anything that could refuse a retry (section 4.4): only the challenge or the server key that
        // made an entry hears of it.
        var digest = SubmissionDigest.Of(request);
        if (request.SubmissionId is { } posted && await entries.GetBySubmissionAsync(served.Row.Id, posted, cancellationToken) is { } earlier)
        {
            return IsItsOwn(earlier, request)
                ? await RetryAsync(earlier, digest, served.Form, request.Redirect, cancellationToken)
                : SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);
        }

        // A solved challenge makes one entry; its own submission id, stored a moment after the read above, is a retry.
        if (request.Ticket is { } ticket && await entries.GetByChallengeAsync(served.Row.Id, FormChallenges.StoredId(ticket), cancellationToken) is { } solved)
        {
            return request.SubmissionId == solved.SubmissionId
                ? await RetryAsync(solved, digest, served.Form, request.Redirect, cancellationToken)
                : SubmissionResult.Refused(SubmissionRefusal.ChallengeRequired);
        }

        var reading = await ReadAsync(request, served, pageKey: null, cancellationToken);
        if (reading.IsRefused)
        {
            return reading.Refusal;
        }

        // A site's handlers may refuse the entry in their own words before anything is written (ADR-0233).
        var (scope, page, read, wordings) = reading.Read;
        var objections = await handlers.AskAsync(new EntrySubmittingNotification(new EventMessages())
        {
            FormKey = served.Form.Form.Key,
            FormAlias = served.Form.Form.Alias,
            Form = served.Form.Definition,
            Version = served.Form.Version,
            Culture = scope.Culture,
            Answers = read.ForMergeTags,
            Member = scope.Member,
            Site = scope.Site,
            Page = scope.Page,
            Context = scope.Context,
            IsSpam = reading.Verdict?.IsSpam == true,
            IsTest = request.Preview is not null,
            IsFromCode = request.ServerKey == FormBuilderEntryService.CodeKey,
        });
        if (objections.Count > 0)
        {
            return SubmissionResult.Invalid(new FieldPaths(served.Form.Definition).Problems(objections));
        }

        // One transaction (section 4.4, step 8).
        var draft = await drafts.FindAsync(request.ResumeToken, served.Row.Id, cancellationToken);

        // What the entry keeps of the address (ADR-0224): nothing from the preview or a server key, whose address is the
        // editor's or the site's own server, not a visitor's.
        var address = request.Preview is null && request.ServerKey is null
            ? await addresses.KeptAsync(served.Form.Definition, request.Address, cancellationToken)
            : null;
        var submission = new NewSubmission(served.Row.Id, version.Info.Id, served.Form, read, wordings, request.SubmissionId ?? Guid.NewGuid(), digest, scope.Culture, page, scope.Context, request.Redirect, request.Ticket is { } used ? FormChallenges.StoredId(used) : null, reading.Verdict, draft, request.WithoutScript ? null : request.Ticket?.IssuedUtc)
        {
            // A preview's entry is a test entry, which sets off nothing unless its ticket runs actions (ADR-0204).
            IsTest = request.Preview is not null,
            RunsActions = request.Preview is null || request.PreviewRunsActions,
            IpAddress = address,
        };
        try
        {
            return await recorder.CreateAsync(submission, scope, cancellationToken) is { } created
                ? SubmissionResult.Accepted(created.Receipt, created.Outcome, replayed: false)
                : SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.DuplicateSubmission)
        {
            // Another request with this submission id was stored a moment earlier: answer as a retry of it.
            return await entries.GetBySubmissionAsync(served.Row.Id, submission.SubmissionId, cancellationToken) is { } other && IsItsOwn(other, request)
                ? await RetryAsync(other, digest, served.Form, request.Redirect, cancellationToken)
                : SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.ChallengeUsed)
        {
            // Another request with this challenge was stored a moment earlier, under another submission id.
            return SubmissionResult.Refused(SubmissionRefusal.ChallengeRequired);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.EntryLimitReached)
        {
            return SubmissionResult.Refused(SubmissionRefusal.EntryLimitReached);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.AlreadySubmitted)
        {
            // Another of the member's posts was stored a moment earlier.
            return SubmissionResult.Refused(SubmissionRefusal.AlreadySubmitted);
        }
        catch (SubmissionInvalidException invalid)
        {
            // An option ran out of places, or another entry took a unique answer, meanwhile (ADR-0090, ADR-0091): the
            // choices past the places and the answers taken, in the order of the form.
            var paths = new FieldPaths(served.Form.Definition);
            return SubmissionResult.Invalid(invalid.Errors.OrderBy(paths.Rank).Select(paths.Problem));
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.MissingFile)
        {
            // An upload went meanwhile, to another entry that named it at the same moment: each answer that names one is invalid.
            var paths = new FieldPaths(served.Form.Definition);
            return SubmissionResult.Invalid(read.Answers.Where(answer => answer.Files.Length > 0)
                .Select(answer => new SubmissionProblem(NameOf(FieldMessageKinds.Invalid), paths.Of(answer.Field, answer.Row))));
        }
    }

    /// <summary>
    /// Whether the member has an entry of the form that counts as theirs (ADR-0231), so that a form that takes one entry per
    /// member says so in its place rather than taking answers it would refuse.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="member">The member's key.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<bool> HasSentAsync(Guid form, Guid member, CancellationToken cancellationToken = default) =>
        await forms.GetAsync(form, cancellationToken) is { } row && await entries.HasMemberEntryAsync(row.Id, member, cancellationToken);

    /// <summary>
    /// Saves the answers so far as a draft, and mails its link (ADR-0163). It runs what a submission runs up to the answers, the
    /// protection among them, and keeps what reads well: an answer with an error is simply not kept, and places and unique
    /// answers are not looked at, as a draft holds none. A request the protection calls spam is answered the same and stores nothing,
    /// so nobody can use it to make the site send mail.
    /// </summary>
    /// <param name="request">The answers so far, as the transport read them, with the address.</param>
    /// <param name="pageKey">The form's page the visitor is on, or null for the first.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> SaveDraftAsync(SubmissionRequest request, Guid? pageKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServedAsync(request.Form, cancellationToken) is not { } served
            || await forms.GetVersionAsync(served.Row.Id, served.Form.Version, cancellationToken) is not { } version)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        // A form that stores no entries keeps nothing, and a draft would.
        if (served.Form.Definition.Settings.Submission is not { Resume: { } resume, Store: not EntryStorage.Never })
        {
            return SubmissionResult.Refused(SubmissionRefusal.ResumeUnavailable);
        }

        if (await GateAsync(request, served.Form) is { } refused)
        {
            return SubmissionResult.Refused(refused);
        }

        request = request with { Ticket = TicketOf(request) };
        if (request.Ticket is { } ticket
            && (await entries.GetByChallengeAsync(served.Row.Id, FormChallenges.StoredId(ticket), cancellationToken) is not null || await drafts.ChallengeSpentAsync(FormChallenges.StoredId(ticket), cancellationToken)))
        {
            return SubmissionResult.Refused(SubmissionRefusal.ChallengeRequired);
        }

        if (!Email.EmailAddresses.IsOne(request.DraftEmail))
        {
            return SubmissionResult.Invalid([new SubmissionProblem(NameOf(FieldMessageKinds.Invalid), DraftEmailPath)]);
        }

        var reading = await ReadAsync(request, served, pageKey: null, cancellationToken, draft: true);
        if (reading.IsRefused)
        {
            return reading.Refusal;
        }

        var (scope, page, read, _) = reading.Read;
        if (!await drafts.HasWayBackAsync(page, scope.Culture, cancellationToken))
        {
            return SubmissionResult.Refused(SubmissionRefusal.ResumeUnavailable);
        }

        var expires = time.GetUtcNow() + TimeSpan.FromDays(resume.Days);
        var challenge = request.Ticket is { } used ? FormChallenges.StoredId(used) : null;
        if (reading.Verdict is { IsSpam: true })
        {
            // Answered as a draft is, its challenge spent as a draft's is, so nothing tells a bot what tripped.
            await drafts.DropAsync(challenge, cancellationToken);
            return SubmissionResult.Saved(expires, EntryDrafts.NewToken());
        }

        var current = pageKey is { } key && served.Form.Definition.Pages.Any(each => each.Key == key) ? pageKey : null;
        var replaces = await drafts.FindAsync(request.ResumeToken, served.Row.Id, cancellationToken);
        try
        {
            return SubmissionResult.Saved(expires, await drafts.SaveAsync(new NewDraft(served.Row.Id, version.Info.Id, served.Form, read, scope.Culture, page, request.DraftEmail!, current, challenge, replaces, scope.Member), cancellationToken));
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.ChallengeUsed)
        {
            return SubmissionResult.Refused(SubmissionRefusal.ChallengeRequired);
        }
    }

    /// <summary>
    /// Checks one page's answers as the submission would check them (ADR-0089), and stores nothing: the form, the gate,
    /// the revision and the consent wordings, the availability, then every posted answer read, ruled and checked, with
    /// only the errors of the page's fields answered. <c>uniquePerForm</c> is not checked: only a submission checks it
    /// (ADR-0091), so a page's check cannot tell whether an answer is taken.
    /// </summary>
    /// <param name="request">The answers the visitor gave so far, as the transport read them.</param>
    /// <param name="pageKey">The page's key, as the revision the visitor's page showed names it.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> ValidateAsync(SubmissionRequest request, Guid pageKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServedAsync(request.Form, request.Preview, cancellationToken) is not { } served)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        if (await GateAsync(request, served.Form) is { } refused)
        {
            return SubmissionResult.Refused(refused);
        }

        request = request with { Ticket = TicketOf(request) };
        var reading = await ReadAsync(request, served, pageKey, cancellationToken);
        return reading.IsRefused ? reading.Refusal : SubmissionResult.Valid();
    }

    /// <summary>
    /// The receipt and the outcome of the form's entry that a submission id names (ADR-0012, ADR-0089), as the page after a
    /// post without JavaScript shows them: only an entry no server key made (ADR-0126) and, on a form for members, only the
    /// member's own (ADR-0231); any other, a draft or no such entry is <see cref="SubmissionRefusal.EntryNotFound"/>.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the submission posted.</param>
    /// <param name="member">The key of the member signed in on the request, or null.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> ReceiptAsync(Guid form, Guid submissionId, Guid? member, CancellationToken cancellationToken = default)
    {
        if (await ServedAsync(form, cancellationToken) is not { } served)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        return await entries.GetBySubmissionAsync(served.Row.Id, submissionId, cancellationToken) is { State: not EntryState.Draft } entry
            && KeyThatSubmitted(entry) is null
            && IsTheMembers(entry, served.Form, member)
            ? await ReplayAsync(entry, served.Form, placement: null, cancellationToken)
            : SubmissionResult.Refused(SubmissionRefusal.EntryNotFound);
    }

    /// <summary>
    /// The receipt of a submission for a request of the public API (ADR-0012, ADR-0138): only the challenge that made the
    /// entry, for up to <see cref="FormChallenges.ReceiptGrace"/> past its expiry, or the server key that made it, finds it;
    /// any other request gets the same miss as an unknown submission, so submission ids cannot be probed.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission's id.</param>
    /// <param name="serverKey">The name of the server key the request carried, or null.</param>
    /// <param name="challenge">The challenge the request posted, or null.</param>
    /// <param name="challenges">Reads the challenge.</param>
    /// <param name="placement">The page the placement names (ADR-0129), which no entry keeps, or null.</param>
    /// <param name="preview">The version a preview ticket names, whose request finds only a test entry (ADR-0204); null for a visitor's.</param>
    /// <param name="member">The key of the member signed in on the request, which a form for members' challenge must name (ADR-0231).</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<SubmissionResult> ReceiptAsync(Guid form, Guid submissionId, string? serverKey, string? challenge, IChallengeReader challenges, Guid? placement = null, int? preview = null, Guid? member = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenges);
        if (await ServedAsync(form, preview, cancellationToken) is not { } served)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        // A form for members answers its own code to a server key and to a visitor, and only the member's own challenge finds
        // the member's entry (ADR-0231).
        var forMembers = IsForMembers(served.Form) && preview is null;
        if (forMembers && (serverKey is not null || member is null))
        {
            return SubmissionResult.Refused(SubmissionRefusal.MemberRequired);
        }

        var ticket = serverKey is null ? challenges.Read(challenge, form, FormChallenges.ReceiptGrace) : null;
        return await entries.GetBySubmissionAsync(served.Row.Id, submissionId, cancellationToken) is { State: not EntryState.Draft } entry
            && entry.IsTest == (preview is not null)
            && (!forMembers || (ticket?.Member == member && entry.MemberKey == member))
            && (serverKey is not null ? string.Equals(KeyThatSubmitted(entry), serverKey, StringComparison.Ordinal) : ticket is not null && entry.ChallengeId == FormChallenges.StoredId(ticket))
            ? await ReplayAsync(entry, served.Form, placement, cancellationToken)
            : SubmissionResult.Refused(SubmissionRefusal.EntryNotFound);
    }

    // The gate (section 4.4, step 2, ADR-0012, ADR-0231): the challenge's signature and expiry and, on a form for members, a
    // member the form admits now whom the challenge names. A server key skips the challenge but never reaches a form for
    // members, and a transport with no challenges has none to read.
    private static async Task<SubmissionRefusal?> GateAsync(SubmissionRequest request, PublishedForm form)
    {
        // A site's own code vouches for the member it names, as it runs on the site's server (ADR-0233).
        if (request.ServerKey == FormBuilderEntryService.CodeKey)
        {
            return IsForMembers(form) && request.Preview is null && request.Member is null ? SubmissionRefusal.MemberRequired : null;
        }

        var ticket = TicketOf(request);
        // A preview's ticket stands in for a test member, who has no key (ADR-0203).
        if (IsForMembers(form) && request.Preview is null)
        {
            // Groups are compared now, not only when the challenge was issued, so a member taken out of one stops here.
            if (request.Challenges is null || request.ServerKey is not null || request.Member is null || request.Members is null
                || !await request.Members.AdmitsAsync(form.Definition.Settings.Availability))
            {
                return SubmissionRefusal.MemberRequired;
            }

            return ticket is null || ticket.Member != request.Member ? SubmissionRefusal.ChallengeRequired : null;
        }

        return request.Challenges is not null && request.ServerKey is null && ticket is null ? SubmissionRefusal.ChallengeRequired : null;
    }

    // What the request's challenge holds, read while it counts; a server key's request carries none.
    private static ChallengeTicket? TicketOf(SubmissionRequest request) =>
        request.ServerKey is null ? request.Challenges?.Read(request.Challenge, request.Form, TimeSpan.Zero) : null;

    // Whether a request may hear of an entry with its submission id: the challenge or the server key that made it.
    private static bool IsItsOwn(StoredEntry entry, SubmissionRequest request)
    {
        if (request.ServerKey is { } key)
        {
            return string.Equals(KeyThatSubmitted(entry), key, StringComparison.Ordinal);
        }

        return request.Ticket is { } ticket && entry.ChallengeId == FormChallenges.StoredId(ticket);
    }

    // The name of the server key the entry records as the one that submitted it, or null when no key did (ADR-0126): a
    // receipt answers that key alone, and a request without a key only an entry no key made (6.8a).
    private static string? KeyThatSubmitted(StoredEntry entry)
    {
        using var context = System.Text.Json.JsonDocument.Parse(entry.Context);
        return context.RootElement.TryGetProperty(SubmissionRequest.ServerKeyContext, out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String
            ? name.GetString()
            : null;
    }

    /// <summary>
    /// What a copy of the form's entry that a submission id names shows (ADR-0123): its stored answers in the scope its
    /// outcome renders in, with what the rules hid for it; null when the form is not served, when it has no such entry or
    /// only a draft, and on a form for members when the entry is not the member's own (ADR-0231).
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the submission posted.</param>
    /// <param name="member">The key of the member signed in on the request, or null.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<EntryCopy?> CopyAsync(Guid form, Guid submissionId, Guid? member, CancellationToken cancellationToken = default)
    {
        if (await ServedAsync(form, cancellationToken) is not { } served)
        {
            return null;
        }

        return await entries.GetBySubmissionAsync(served.Row.Id, submissionId, cancellationToken) is { State: not EntryState.Draft } entry && IsTheMembers(entry, served.Form, member)
            ? await recorder.CopyAsync(entry, served.Form, cancellationToken)
            : null;
    }

    // The version a preview ticket names, as though it were published, and the form's row read again (ADR-0203); the
    // form as the site serves it without one.
    private async Task<Served?> ServedAsync(Guid key, int? preview, CancellationToken cancellationToken) =>
        preview is not { } number
            ? await ServedAsync(key, cancellationToken)
            : await previews.GetAsync(key, number, cancellationToken) is { } shown
                && await forms.GetAsync(key, cancellationToken) is { TrashedUtc: null } row
                ? new Served(shown.Published, row)
                : null;

    // The form, as the site serves it, and its row read again, so a form trashed a moment ago takes nothing (step 1). Where
    // the row shows this server's copy is behind, as another server published a newer revision or published a form kept
    // here as unpublished, and its instruction has not come yet, the copy is read again (a late cache refresher, 16.4,
    // ADR-0237): the visitor who loaded the newer revision there is not told the form changed here. A key that names no
    // form still costs one lookup.
    private async Task<Served?> ServedAsync(Guid key, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(key, cancellationToken);
        if (cached is null && !cache.IsKeptAsNotServed(key))
        {
            return null;
        }

        if (await forms.GetAsync(key, cancellationToken) is not { TrashedUtc: null, PublishedVersionId: not null } row)
        {
            return null;
        }

        // A form kept as unreadable at this revision reads the same again, so it is not read on every post (16.5's review).
        if (cached is null ? !cache.IsKeptAsUnreadable(key, row.PublicRevision) : row.PublicRevision > cached.Published.Form.PublicRevision)
        {
            cached = await cache.RenewAsync(key, cancellationToken);
        }

        return cached is null ? null : new Served(cached.Published, row);
    }

    // Whether the form is for members (ADR-0095), which the gate admits only its members to (ADR-0231).
    private static bool IsForMembers(PublishedForm form) => form.Definition.Settings.Availability.Access != MemberAccess.Anyone;

    // Whether an entry is the member's own on a form for members; on a form for anyone every entry is (ADR-0231).
    private static bool IsTheMembers(StoredEntry entry, PublishedForm form, Guid? member) =>
        !IsForMembers(form) || (member is not null && entry.MemberKey == member);

    // The groups the form's rules name that the request's member is in: none without a member, or without a rule that names one.
    private static async Task<IReadOnlyList<string>> GroupsOfAsync(SubmissionRequest request, FormDefinition definition)
    {
        if (request.Member is null || request.Members is null)
        {
            return [];
        }

        var named = FormMemberConditions.Of(definition).Groups;
        return named.Count == 0 ? [] : await request.Members.GroupsAmongAsync(named);
    }

    // Compatibility, availability and the answers read and checked (section 4.4, steps 4, 5 and 7), the places left on
    // the options they chose among them (ADR-0090) and, for a submission, the answers that must be unique (ADR-0091). For
    // a page, only the errors of its fields count, and a page without an answer is no error.
    // A draft takes the submission's way past the protection, and its answers need not be whole.
    private async Task<Reading> ReadAsync(SubmissionRequest request, Served served, Guid? pageKey, CancellationToken cancellationToken, bool draft = false)
    {
        var form = served.Form;
        var definition = form.Definition;

        // Compatibility: the aliases of the revision the page showed (ADR-0002); the preview's are its ticket's version's,
        // whatever was published meanwhile, as the ticket holds the frame to it (ADR-0203).
        var aliases = request.Preview is null
            ? await AliasesAsync(request.Revision, served.Row.Id, form, cancellationToken)
            : new Aliases(form.Definition, isCurrent: true);
        if (aliases is null)
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.FormChanged));
        }

        var unreadable = new List<string>();
        var answers = Answers(request, aliases, definition, unreadable, out var changed);
        var onPage = pageKey is { } key ? aliases.OnPage(key, definition) : null;
        if (pageKey is not null && onPage is null)
        {
            changed |= Unknown(aliases, null, PageKeyPath, unreadable);
        }

        var query = Query(request, definition, unreadable);
        var context = Context(request, definition, unreadable);
        if (request.ServerKey is { } serverKey)
        {
            context[SubmissionRequest.ServerKeyContext] = serverKey;
        }

        if (unreadable.Count > 0)
        {
            // As many paths as the body's own reading names at most (16.1's review): a body of unknown names answers no longer than that.
            return Reading.Refused(SubmissionResult.Unreadable([.. unreadable.Distinct(StringComparer.Ordinal).Take(MostUnreadable)]));
        }

        var culture = await pages.CultureAsync(request.Culture);
        var page = await pages.PageAsync(request.ContentKey, request.Host);
        var scope = new MergeTagScope
        {
            Form = definition,
            FormName = form.Form.Name,
            FormAlias = form.Form.Alias,
            Culture = culture,
            Site = page.Site,
            Page = page.Content,
            // An anonymous form runs without the member, so nothing it keeps names them (ADR-0231).
            Member = definition.Settings.Privacy.Anonymous ? null : request.Member,
            Query = query,
            Context = context,
        };
        var wordings = changed ? null : await WordingsAsync(definition, answers, scope, cancellationToken);
        if (wordings is null)
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.FormChanged));
        }

        // Availability, by the site's clock and the entries that count toward the limit.
        switch (await availability.CheckAsync(definition.Settings.Availability, served.Row.Id, cancellationToken))
        {
            case FormUnavailable.Closed:
                return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.FormClosed));
            case FormUnavailable.Full:
                return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.EntryLimitReached));
        }

        // One entry per member (ADR-0231): a member who has one gets no second, which the entry's unit holds too. A draft
        // is no entry, and the preview's test member has none.
        if (!draft && request.Preview is null && definition.Settings.Availability.OnePerMember && request.Member is { } member
            && await entries.HasMemberEntryAsync(served.Row.Id, member, cancellationToken))
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.AlreadySubmitted));
        }

        // The answers, read by their types before the rules and checked once the rules leave them shown (ADR-0072). A draft of
        // nothing yet is a draft: its link comes back to the page the visitor was on (ADR-0252).
        if (pageKey is null && !draft && IsEmpty(request))
        {
            return Reading.Refused(SubmissionResult.Invalid([new SubmissionProblem(SubmissionProblems.Empty, string.Empty)]));
        }

        // The puzzle comes with the challenge, so an answer that does not solve it asks for a new challenge (ADR-0141). A
        // preview's ticket stands in for the puzzle, the captcha and the spam score, which tell a person from a script, not
        // an editor from a visitor: a test entry never lands in Spam (ADR-0204).
        var tested = request.Preview is not null;
        var protects = await protection.SettingsOfAsync(form.Form.Key, page, cancellationToken);
        if (pageKey is null && !tested && !SubmissionProtection.HasItsProof(request, definition, protects))
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.ChallengeRequired));
        }

        var zone = await timeZones.GetAsync(page.Site, cancellationToken);
        var read = await reader.ReadAsync(
            new PostedSubmission
            {
                Scope = scope,
                Now = time.GetUtcNow(),
                TimeZone = zone.Id,
                Fields = answers.Fields,
                Rows = answers.Rows,
                Uploads = await UploadsAsync(request, answers, definition, served.Row.Id, cancellationToken),
                // The rules see the member as the page does: signed in, and in the groups they name (ADR-0138, ADR-0231).
                MemberSignedIn = request.Member is not null,
                MemberGroups = await GroupsOfAsync(request, definition),
            },
            cancellationToken);
        var paths = new FieldPaths(definition);

        // A token is spent when it is verified, so only answers that can be stored ask the provider (ADR-0142).
        var captcha = pageKey is null && !tested && (draft || read.Errors.IsEmpty) ? await protection.CheckCaptchaAsync(request, definition, protects, cancellationToken) : FormBuilderCaptchaOutcome.Passed;
        if (captcha == FormBuilderCaptchaOutcome.Failed)
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.CaptchaFailed));
        }

        // The protection, for a submission (section 4.4, step 6), on the answers as they were read: Spam is told nothing of
        // places or unique answers, which it neither takes nor holds (ADR-0090, ADR-0091).
        var verdict = pageKey is null && !tested ? await protection.ScoreAsync(request, definition, read, scope.Culture, page, protects, captcha, cancellationToken) : SpamVerdict.None;
        if (draft)
        {
            return new Reading(null, new ReadSubmission(scope, page, read, wordings), verdict);
        }

        var full = verdict.IsSpam ? [] : await places.CheckAsync(served.Row.Id, read.Places, cancellationToken);
        // A unique answer is checked once nothing else refuses the post: an answer taken by another entry is then told to
        // a post that would be stored, never to one that stores nothing, which could ask it of any address (ADR-0091).
        var taken = !verdict.IsSpam && pageKey is null && read.Errors.IsEmpty && full.IsEmpty ? await unique.CheckAsync(served.Row.Id, read.Unique, cancellationToken) : [];
        var all = full.IsEmpty && taken.IsEmpty ? read.Errors : [.. read.Errors.Concat(full).Concat(taken).OrderBy(paths.Rank)];
        ImmutableArray<SubmissionError> errors = onPage is null ? all : [.. all.Where(error => onPage.Contains(error.Field))];
        if (!errors.IsEmpty)
        {
            return Reading.Refused(aliases.IsCurrent || errors.All(error => aliases.Shown.Contains(error.Field))
                ? SubmissionResult.Invalid(errors.Select(paths.Problem))
                : SubmissionResult.Refused(SubmissionRefusal.FormChanged));
        }

        return new Reading(null, new ReadSubmission(scope, page, read, wordings), verdict);
    }

    // A retry of an entry's submission id: the same answers get the entry's receipt and outcome, others 409.
    private async Task<SubmissionResult> RetryAsync(StoredEntry entry, string digest, PublishedForm form, Guid? placement, CancellationToken cancellationToken) =>
        string.Equals(entry.SubmissionHash, digest, StringComparison.Ordinal)
            ? await ReplayAsync(entry, form, placement, cancellationToken)
            : SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);

    // An entry's receipt and outcome again, as it was answered.
    private async Task<SubmissionResult> ReplayAsync(StoredEntry entry, PublishedForm form, Guid? placement, CancellationToken cancellationToken)
    {
        var replayed = await recorder.ReplayAsync(entry, form, placement, cancellationToken);
        return SubmissionResult.Accepted(replayed.Receipt, replayed.Outcome, replayed: true);
    }

    // The aliases of the revision the page showed: the live version's for the current revision, the newest published
    // version's that had it for an older one; null for a newer revision or one no version had.
    private async Task<Aliases?> AliasesAsync(int revision, int formId, PublishedForm form, CancellationToken cancellationToken)
    {
        if (revision == form.Form.PublicRevision)
        {
            return new Aliases(form.Definition, isCurrent: true);
        }

        if (revision < 1 || revision > form.Form.PublicRevision || await forms.GetRevisionAsync(formId, revision, cancellationToken) is not { } shown)
        {
            return null;
        }

        try
        {
            return new Aliases(documents.Readable(shown), isCurrent: false);
        }
        catch (FormBuilderValidationException)
        {
            return null;
        }
    }

    // The posted answers by the live version's keys. An alias the current revision lacks cannot be read; for an older
    // revision, one it lacks or a field it had that the live version does not means the form changed.
    private PostedAnswers Answers(SubmissionRequest request, Aliases aliases, FormDefinition definition, List<string> unreadable, out bool changed)
    {
        changed = false;
        var live = new HashSet<Guid>(Fields(definition).SelectMany(field => field.Fields.Prepend(field)).Select(field => field.Key));
        var fields = new Dictionary<Guid, PostedAnswer>();
        foreach (var (alias, answer) in request.Fields)
        {
            if (aliases.Top.TryGetValue(alias, out var group) && IsGroup(group) && answer.Values.Count == 0 && answer.Parts is not { Count: > 0 })
            {
                // An empty list for a repeatable group: no rows, as when it is left out.
                continue;
            }

            if (!aliases.Top.TryGetValue(alias, out var field) || IsGroup(field))
            {
                changed |= Unknown(aliases, field, $"values.{alias}", unreadable);
                continue;
            }

            changed |= !live.Contains(field.Key);
            fields[field.Key] = answer;
        }

        var rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>>();
        foreach (var (alias, posted) in request.Rows)
        {
            if (!aliases.Top.TryGetValue(alias, out var group) || !IsGroup(group))
            {
                changed |= Unknown(aliases, group, $"values.{alias}", unreadable);
                continue;
            }

            changed |= !live.Contains(group.Key);
            var children = group.Fields.Where(child => child.Alias is not null).ToDictionary(child => child.Alias!, StringComparer.Ordinal);
            var list = new List<IReadOnlyDictionary<Guid, PostedAnswer>>();
            for (var index = 0; index < posted.Count; index++)
            {
                var row = new Dictionary<Guid, PostedAnswer>();
                foreach (var (childAlias, answer) in posted[index])
                {
                    if (!children.TryGetValue(childAlias, out var child))
                    {
                        changed |= Unknown(aliases, null, string.Create(CultureInfo.InvariantCulture, $"values.{alias}[{index}].{childAlias}"), unreadable);
                        continue;
                    }

                    changed |= !live.Contains(child.Key);
                    row[child.Key] = answer;
                }

                list.Add(row);
            }

            rows[group.Key] = list;
        }

        return new PostedAnswers(fields, rows);
    }

    /// <summary>The most paths a refusal of values the page should not have posted names, as the body's own reading names at most.</summary>
    internal const int MostUnreadable = 100;

    // An alias the page should not have posted: unreadable for the current revision, a change for an older one, whose
    // fields may have been taken out since. A known field posted in the wrong shape is unreadable either way.
    private static bool Unknown(Aliases aliases, FormField? field, string path, List<string> unreadable)
    {
        if (aliases.IsCurrent || field is not null)
        {
            unreadable.Add(path);
            return false;
        }

        return true;
    }

    // The query values the form lists, each name held to its limits.
    private static Dictionary<string, IReadOnlyList<string>> Query(SubmissionRequest request, FormDefinition definition, List<string> unreadable)
    {
        var listed = definition.Settings.Developer.QueryValues;
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in request.Query)
        {
            if (!listed.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (values.Count > MaxQueryValues || values.Any(value => value.Length > MaxValueLength))
            {
                unreadable.Add($"query.{name}");
                continue;
            }

            query[name] = values;
        }

        return query;
    }

    // The context values the form lists, each held to its limit; a public post's values are the visitor's to set, so no
    // other name is read.
    private static Dictionary<string, string> Context(SubmissionRequest request, FormDefinition definition, List<string> unreadable)
    {
        var listed = definition.Settings.Developer.ContextValues;
        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in request.Context)
        {
            if (!listed.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (value.Length > MaxValueLength)
            {
                unreadable.Add($"context.{name}");
                continue;
            }

            context[name] = value;
        }

        return context;
    }

    // The wording each ticked consent with a posted hash was shown, by the field's key; null when a hash is not the one
    // its wording has now in the culture (ADR-0009, ADR-0085). A tick without a hash is the reader's to refuse.
    private async Task<Dictionary<Guid, ConsentWording>?> WordingsAsync(FormDefinition definition, PostedAnswers answers, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var wordings = new Dictionary<Guid, ConsentWording>();
        foreach (var field in Fields(definition).SelectMany(field => field.Fields.Prepend(field)))
        {
            if (types.FindType(field.Type) is not ConsentFieldType)
            {
                continue;
            }

            var hashes = Hashes(answers, field.Key).ToList();
            if (hashes.Count == 0)
            {
                continue;
            }

            if (await consents.ShownAsync(field, scope, cancellationToken) is not { } shown || hashes.Any(hash => !string.Equals(hash, shown.Hash, StringComparison.Ordinal)))
            {
                return null;
            }

            wordings[field.Key] = shown;
        }

        return wordings;
    }

    // The hashes a field's ticked answers post, in every row it is answered in.
    private static IEnumerable<string> Hashes(PostedAnswers answers, Guid field)
    {
        var posted = answers.Fields.TryGetValue(field, out var own) ? [own] : answers.Rows.Values.SelectMany(rows => rows).Select(row => row.GetValueOrDefault(field)).OfType<PostedAnswer>();
        foreach (var answer in posted)
        {
            if (answer.Values.Any(value => value is not null && TextSemantics.Clean(value) == FieldAnswerDefaults.Ticked)
                && answer.Parts?.GetValueOrDefault(ConsentFieldType.WordingPart) is { } parts
                && parts.FirstOrDefault(text => !string.IsNullOrEmpty(text)) is { } hash)
            {
                yield return TextSemantics.Clean(hash);
            }
        }
    }

    // Whether the submission holds no answer at all: no text but blanks in any value or part, a consent's wording hash
    // aside, which says what the page showed and not what the visitor answered.
    private static bool IsEmpty(SubmissionRequest request)
    {
        static bool Blank(PostedAnswer answer) =>
            answer.Values.All(IsBlank) && (answer.Parts ?? new Dictionary<string, IReadOnlyList<string?>>())
                .Where(part => part.Key != ConsentFieldType.WordingPart).All(part => part.Value.All(IsBlank));
        return request.Fields.Values.All(Blank) && request.Rows.Values.SelectMany(rows => rows).SelectMany(row => row.Values).All(Blank);
    }

    private static bool IsBlank(string? text) => text is null || TextSemantics.Clean(text).Length == 0;

    // The uploads the tokens posted for fields that store files stand for, each a file of this form, read once each.
    private async Task<Dictionary<string, StoredEntryFile>> UploadsAsync(SubmissionRequest request, PostedAnswers answers, FormDefinition definition, int formId, CancellationToken cancellationToken)
    {
        var uploads = new Dictionary<string, StoredEntryFile>(StringComparer.Ordinal);
        if (request.Tokens is not { } tokens)
        {
            return uploads;
        }

        var storing = Fields(definition).SelectMany(field => field.Fields.Prepend(field))
            .Where(field => types.FindType(field.Type)?.Declaration.Storage == FieldStorageKind.File)
            .Select(field => field.Key)
            .ToHashSet();
        var posted = answers.Fields.Concat(answers.Rows.Values.SelectMany(rows => rows).SelectMany(row => row));
        foreach (var (field, answer) in posted)
        {
            if (!storing.Contains(field))
            {
                continue;
            }

            foreach (var token in answer.Values.OfType<string>().Select(TextSemantics.Clean).Where(token => token.Length > 0 && !uploads.ContainsKey(token)))
            {
                if (tokens.FileOf(token, request.Form, field, request.WithoutScript ? null : request.Ticket?.Id) is { } key && await entries.GetFileAsync(key, cancellationToken) is { } file && file.FormId == formId && file.ScanState != FileScanStates.Quarantined)
                {
                    uploads[token] = file;
                }
            }
        }

        return uploads;
    }

    private bool IsGroup(FormField? field) =>
        field is not null && types.FindType(field.Type) is { } type && type.Declaration.Features.HasFlag(FieldFeatures.ChildFields);

    // A kind of error as a field's messages name it; invalid for one they do not name.
    private static string NameOf(FieldMessageKinds kind) =>
        (FieldMessageProperties.All.FirstOrDefault(property => property.Kind == kind) ?? FieldMessageProperties.All.First(property => property.Kind == FieldMessageKinds.Invalid)).Name;

    private static IEnumerable<FormField> Fields(FormDefinition definition) => definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields);

    // A revision's fields outside repeatable groups by alias, and the keys of every field and grid row it has.
    private sealed class Aliases
    {
        private readonly FormDefinition _shown;

        public Aliases(FormDefinition shown, bool isCurrent)
        {
            _shown = shown;
            IsCurrent = isCurrent;
            foreach (var field in Fields(shown))
            {
                if (field.Alias is { } alias)
                {
                    Top[alias] = field;
                }
            }

            Shown.UnionWith(Keys(Fields(shown)));
        }

        public bool IsCurrent { get; }

        public Dictionary<string, FormField> Top { get; } = new(StringComparer.Ordinal);

        public HashSet<Guid> Shown { get; } = [];

        // The keys of the page with this key in this revision and in the live version, whose errors a page's check
        // answers (ADR-0089); null when neither has the page.
        public HashSet<Guid>? OnPage(Guid key, FormDefinition live)
        {
            var found = new[] { _shown, live }.Select(definition => definition.Pages.FirstOrDefault(page => page.Key == key)).OfType<FormPage>().ToList();
            return found.Count == 0 ? null : new HashSet<Guid>(Keys(found.SelectMany(page => page.Groups).SelectMany(group => group.Fields)));
        }

        // The keys of these fields, of their repeatable groups' children and of their grid rows.
        private static IEnumerable<Guid> Keys(IEnumerable<FormField> fields) =>
            fields.SelectMany(field => field.Fields.Prepend(field)).SelectMany(item => item.Rows.Select(row => row.Key).Prepend(item.Key));
    }

    // A form the site serves: as it serves it, and its row.
    private sealed record Served(PublishedForm Form, StoredForm Row);

    // What a request's reading gave: its refusal, or the answers with what they were read in.
    private sealed record Reading(SubmissionResult? Refusal, ReadSubmission? Read, SpamVerdict? Verdict = null)
    {
        [MemberNotNullWhen(true, nameof(Refusal))]
        [MemberNotNullWhen(false, nameof(Read))]
        public bool IsRefused => Refusal is not null;

        public static Reading Refused(SubmissionResult refusal) => new(refusal, null);
    }

    // Answers that stand, with the scope and the page they were read in and the wording each ticked consent was shown.
    private sealed record ReadSubmission(MergeTagScope Scope, RequestPage Page, SubmissionAnswers Answers, IReadOnlyDictionary<Guid, ConsentWording> Wordings);

    // The answers by the live version's keys.
    private sealed record PostedAnswers(
        IReadOnlyDictionary<Guid, PostedAnswer> Fields,
        IReadOnlyDictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> Rows);

    // Where each field, child and grid row of the live version is, as a problem's path names it, and its place in the
    // order of the form and its rows, the order the reader lists errors in.
    private sealed class FieldPaths
    {
        private readonly Dictionary<Guid, (string Alias, FormField? Group)> _fields = [];
        private readonly Dictionary<Guid, (FormField Grid, FormField? Group)> _rows = [];
        private readonly Dictionary<Guid, (int Field, int Child)> _order = [];

        public FieldPaths(FormDefinition definition)
        {
            foreach (var (field, index) in Fields(definition).Select((field, index) => (field, index)))
            {
                Add(field, null, (index, -1));
                foreach (var (child, position) in field.Fields.Select((child, position) => (child, position)))
                {
                    Add(child, field, (index, position));
                }
            }
        }

        // An error's place: its field's, then its row's, then its place among its group's children. Errors that share a
        // place keep their order (OrderBy is stable), so the reader's come before those added after it.
        public (int Field, int Row, int Child) Rank(SubmissionError error) =>
            _order.TryGetValue(error.Field, out var at) ? (at.Field, error.Row ?? -1, at.Child) : (int.MaxValue, 0, 0);

        // A handler's errors (ADR-0233): those at the form first, then those at fields in the order of the form and its
        // rows, each in the handler's words.
        public IEnumerable<SubmissionProblem> Problems(IReadOnlyList<EntrySubmittingError> errors)
        {
            var keys = _fields.Where(pair => pair.Value.Alias.Length > 0).ToDictionary(pair => pair.Value.Alias, pair => pair.Key, StringComparer.Ordinal);
            return errors
                .Select((error, index) => (Error: error, Index: index, Key: error.Field is { } alias ? keys[alias] : (Guid?)null))
                .OrderBy(item => item.Key is { } key ? _order[key] : (-1, -1))
                .ThenBy(item => item.Error.Row ?? -1)
                .ThenBy(item => item.Index)
                .Select(item => new SubmissionProblem(
                    SubmissionProblems.Custom,
                    item.Key is { } key ? Of(key, item.Error.Row) : string.Empty,
                    Text: item.Error.Message.Length == 0 ? null : item.Error.Message));
        }

        public SubmissionProblem Problem(SubmissionError error)
        {
            var path = Of(error.Field, error.Row);
            if (error.Rule is { } rule)
            {
                return new SubmissionProblem(SubmissionProblems.Rule, path, Rule: rule);
            }

            return error.Check is { } check
                ? new SubmissionProblem(SubmissionProblems.Check, path, Check: check)
                : new SubmissionProblem(NameOf(error.Kind), path);
        }

        public string Of(Guid key, int? row)
        {
            if (_rows.TryGetValue(key, out var grid))
            {
                return $"{Of(grid.Grid.Key, row)}.{key:D}";
            }

            // Every problem names a field of the live version, which the reader read the answers by.
            var field = _fields[key];
            return field.Group is { Alias: { } group }
                ? string.Create(CultureInfo.InvariantCulture, $"{group}[{row ?? 0}].{field.Alias}")
                : field.Alias;
        }

        private void Add(FormField field, FormField? group, (int Field, int Child) order)
        {
            _fields[field.Key] = (field.Alias ?? string.Empty, group);
            _order[field.Key] = order;
            foreach (var row in field.Rows)
            {
                _rows[row.Key] = (field, group);
                _order[row.Key] = order;
            }
        }
    }
}
