using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Signatures;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>What the surface action answers a post with (ADR-0100).</summary>
internal enum FormPostAnswerKind
{
    /// <summary>The page again, which renders the form with the state the post left.</summary>
    Page,

    /// <summary>A redirect: to the page with the sent token, or to the outcome's address.</summary>
    Redirect,

    /// <summary><c>400</c>: a post that reached the action without a page, or that names no form.</summary>
    BadRequest,

    /// <summary><c>403</c>: a post to a form for members from another origin.</summary>
    Forbidden,
}

/// <summary>The surface action's answer to a post.</summary>
/// <param name="Kind">What it answers.</param>
/// <param name="Url">For a redirect, where to.</param>
internal sealed record FormPostAnswer(FormPostAnswerKind Kind, string? Url = null)
{
    /// <summary>The page again.</summary>
    public static readonly FormPostAnswer Page = new(FormPostAnswerKind.Page);

    /// <summary><c>400</c>.</summary>
    public static readonly FormPostAnswer BadRequest = new(FormPostAnswerKind.BadRequest);

    /// <summary><c>403</c>.</summary>
    public static readonly FormPostAnswer Forbidden = new(FormPostAnswerKind.Forbidden);
}

/// <summary>
/// A form's post without JavaScript (ADR-0004, ADR-0100): read into the request the JSON endpoint reads (ADR-0088), the
/// step its button asks for run through the pipeline (Back and an answer's "Change" on the summary page move without a
/// check, Next checks the page as the validate endpoint does, Send submits) or a row's Add or Remove or a ranking's move
/// made without one, and the answers, the
/// errors, the page to move from and any notice left for the form's render through the request's items (ADR-0093); a sent
/// form redirects to its outcome. Every answer is <c>no-store</c>.
/// </summary>
/// <param name="forms">The published forms.</param>
/// <param name="requests">What the form endpoint answers, whose consent wordings a changed form's post is checked against.</param>
/// <param name="pages">The culture and the page.</param>
/// <param name="submissions">The submission pipeline.</param>
/// <param name="types">The field types.</param>
/// <param name="sent">The tokens of the redirect after a sent form.</param>
/// <param name="uploads">The upload's rules, which the post's files are held to and stored by (ADR-0102).</param>
/// <param name="tokens">The upload tokens, which a file field's answer holds.</param>
/// <param name="entries">The entries' files, which a kept file's token names.</param>
internal sealed class FormPostHandler(
    PublishedFormCache forms,
    FormRequests requests,
    FormPages pages,
    FormSubmissions submissions,
    FieldTypeRegistry types,
    SentTokens sent,
    FileUploads uploads,
    UploadTokens tokens,
    IEntryRepository entries)
{
    /// <summary>The notice's code after a post that met a form published again meanwhile.</summary>
    public const string FormChangedCode = "form_changed";

    /// <summary>The notice's code after a post the form could not take, whose answers are kept.</summary>
    public const string NotSentCode = "not_sent";

    /// <summary>The notice's code after a post too large to read, whose answers are lost (ADR-0102).</summary>
    public const string TooLargeCode = "too_large";

    /// <summary>Reads a post and answers it.</summary>
    /// <param name="http">The request, which Umbraco routed to the surface action.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<FormPostAnswer> HandleAsync(HttpContext http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        PageCaching.NoStore(http);
        if (http.Features.Get<UmbracoRouteValues>()?.PublishedRequest is not { } published || !http.Request.HasFormContentType)
        {
            return FormPostAnswer.BadRequest;
        }

        // Umbraco read the form already for its route token, so a post too large or malformed never gets here.
        var posted = await http.Request.ReadFormAsync(cancellationToken);
        if (One(posted, FormPostFields.Form) is not { } formText || !Guid.TryParseExact(formText, "D", out var formKey)
            || One(posted, FormPostFields.Instance) is not { } instance || !FormInstances.IsId(instance)
            || !int.TryParse(One(posted, FormPostFields.Revision), NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
        {
            return FormPostAnswer.BadRequest;
        }

        if (await forms.GetAsync(formKey, cancellationToken) is not { } cached)
        {
            // A form gone meanwhile: the page renders without it, as for any visitor now.
            return FormPostAnswer.Page;
        }

        var definition = cached.Published.Definition;
        if (definition.Settings.Availability.Access != MemberAccess.Anyone && !IsOwnOrigin(http.Request))
        {
            return FormPostAnswer.Forbidden;
        }

        var culture = await pages.CultureAsync(FormPages.IsCulture(published.Culture) ? published.Culture : null);
        var content = published.PublishedContent?.Key;
        var reader = FormPostReader.Read(posted, definition, types);
        var listed = definition.Settings.Developer.QueryValues;
        var query = Held(reader.Query.Count > 0 ? reader.Query : Referred(http.Request), listed);
        var noted = await FilesAsync(http, posted.Files, reader, formKey, instance, cancellationToken);
        var nav = posted[FormPostFields.Nav].LastOrDefault();
        var moves = nav is FormPostFields.Back or FormPostFields.Next or FormPostFields.Submit or null || Goto(nav) is not null;
        if (moves)
        {
            noted.AddRange(await SignaturesAsync(reader, formKey, cancellationToken));
        }

        var (fields, rows) = reader.Posted();
        var request = new SubmissionRequest
        {
            Form = formKey,
            Revision = revision,
            Culture = culture,
            ContentKey = content,
            Redirect = Placement(http),
            Fields = fields,
            Rows = rows,
            Query = query,
            Context = reader.Context,
            Tokens = tokens,
            Member = await http.RequestServices.GetRequiredService<FormMembers>().KeyAsync(),
        };
        var page = Guid.TryParseExact(One(posted, FormPostFields.Page), "D", out var pageKey) ? pageKey : (Guid?)null;
        var state = new Step(reader, definition, page, query);
        switch (nav)
        {
            // A file the post could not take: nothing is sent and no page is left (ADR-0102).
            case var _ when moves && noted.Count > 0:
                var shownCheck = page is { } posting ? await submissions.ValidateAsync(request, posting, cancellationToken) : null;
                state.Problems([.. noted, .. shownCheck is { Refusal: SubmissionRefusal.ValidationFailed } ? shownCheck.Problems : []]);
                break;
            case FormPostFields.Back:
                state.Move = FormPostMove.Back;
                break;
            case var _ when Goto(nav) is { } alias:
                state.Open(reader.TopOf(alias));
                break;
            case FormPostFields.Next when page is { } shown:
                var check = await submissions.ValidateAsync(request, shown, cancellationToken);
                if (check.Refusal is null)
                {
                    state.Move = FormPostMove.Next;
                }
                else if (!await RefusedAsync(state, check, cached, culture, content, cancellationToken))
                {
                    return FormPostAnswer.Page;
                }

                break;
            case FormPostFields.Submit or null:
                var result = await submissions.SubmitAsync(request, cancellationToken);
                if (result is { Receipt: { } receipt, Outcome: { } outcome })
                {
                    return new FormPostAnswer(FormPostAnswerKind.Redirect, outcome is { Kind: OutcomeKind.Redirect, Url: { } url }
                        ? AwayFromTheForm(url)
                        : SentAddress(http.Request, sent.Issue(formKey, receipt.SubmissionId, instance), instance));
                }

                if (!await RefusedAsync(state, result, cached, culture, content, cancellationToken))
                {
                    return FormPostAnswer.Page;
                }

                break;
            default:
                Button(reader, nav);
                state.Problems(noted);
                break;
        }

        FormPosts.Set(http, formKey, instance, state.State(await KeptAsync(reader, definition, formKey, cancellationToken)));
        return FormPostAnswer.Page;
    }

    // The page after a send the form's placement names (ADR-0129), from the route token only the server writes.
    private static Guid? Placement(HttpContext http) =>
        http.GetRouteValue(FormPostFields.Redirect) is string text && Guid.TryParse(text, out var page) && page != Guid.Empty ? page : null;

    // A row's Add or Remove, or a ranking's move in the order the page showed (ADR-0101): the posted page again with the
    // change and every answer, without a check. An Add past the group's most, a Remove below its least, a move past an
    // end and any other button change nothing.
    private void Button(FormPostReader reader, string? nav)
    {
        switch (nav?.Split(FormPostFields.Separator))
        {
            case [FormPostFields.Add, var alias] when reader.GroupOf(alias) is { } group:
                reader.AddRow(group, Limits(group).Most);
                break;
            case [FormPostFields.Remove, var alias, var row] when reader.GroupOf(alias) is { } group && Number(row) is { } at:
                reader.RemoveRow(group, at, Limits(group).Least);
                break;
            case [var way and (FormPostFields.Up or FormPostFields.Down), var name, var place] when reader.FieldOf(name) is { } found
                && types.FindType(found.Field.Type) is RankingFieldType ranking && Number(place) is { } n:
                // A post without the order shown (a site's own view) moves among the options it ranks.
                var answers = reader.AnswersOf(found.Field, found.Row);
                var posted = reader.OrderOf(found.Field, found.Row);
                var shown = RankingOrder.Of(posted.Count > 0 ? posted : [.. answers.Parts.Keys, .. answers.Values], answers);
                var order = RankingOrder.Moved(shown, n, way == FormPostFields.Up);
                reader.Rank(found.Field, found.Group, found.Row, order, RankingOrder.Positions(order, ranking.ReadSettings(found.Field.Settings).Top));
                break;
        }
    }

    // The post's files (ADR-0102): each one the files' reader took goes through the upload's rules and is stored, its
    // token joining its field's answer; each it could not take, or the rules refused, is a problem at its field.
    private async Task<List<SubmissionProblem>> FilesAsync(HttpContext http, IFormFileCollection files, FormPostReader reader, Guid formKey, string instance, CancellationToken cancellationToken)
    {
        var problems = new List<SubmissionProblem>();
        foreach (var note in FormPostFileNotes.Get(http, formKey, instance)?.Notes ?? [])
        {
            if (reader.FieldOf(note.Name) is { } noted)
            {
                problems.Add(Problem(noted, note.Kind));
            }
        }

        foreach (var file in files)
        {
            if (reader.FieldOf(file.Name) is not { } found || uploads.TypeOf(found.Field) is not { } type)
            {
                continue;
            }

            await using var content = file.OpenReadStream();
            var stored = await uploads.StoreAsync(formKey, type, found.Field, file.FileName, content, _ => Task.FromResult(true), cancellationToken);
            if (stored is { Status: FileUploadStatus.Stored, File: { } kept })
            {
                reader.AddFile(found.Field, found.Group, found.Row, tokens.Issue(formKey, found.Field.Key, kept.Key), FormPostFilesMiddleware.Most(type, found.Field) == 1);
            }
            else
            {
                problems.Add(Problem(found, stored.Error ?? FieldMessageKinds.Invalid));
            }
        }

        return problems;
    }

    // The names typed instead of signatures (ADR-0103), when a page button may check or send the post: each drawn on its
    // pad as the signature's PNG and stored as an upload is, its token the field's answer in place of any other; a name
    // too long, or one the upload's rules refuse, is a problem at its field.
    private async Task<List<SubmissionProblem>> SignaturesAsync(FormPostReader reader, Guid formKey, CancellationToken cancellationToken)
    {
        var problems = new List<SubmissionProblem>();
        foreach (var (field, group, row, typed) in reader.Typed().ToList())
        {
            if (TypedSignature.Clean(typed) is not { } name || types.FindType(field.Type) is not SignatureFieldType signature)
            {
                continue;
            }

            if (name.Length > TypedSignature.LongestName)
            {
                problems.Add(Problem((field, group, row), FieldMessageKinds.Invalid));
                continue;
            }

            var pad = signature.ReadSettings(field.Settings);
            await using var drawn = new MemoryStream(TypedSignature.Draw(name, pad.Width ?? TypedSignature.DefaultWidth, pad.Height ?? TypedSignature.DefaultHeight));
            var stored = await uploads.StoreAsync(formKey, signature, field, TypedSignature.FileName, drawn, _ => Task.FromResult(true), cancellationToken);
            if (stored is { Status: FileUploadStatus.Stored, File: { } kept })
            {
                reader.AddFile(field, group, row, tokens.Issue(formKey, field.Key, kept.Key), replace: true);
            }
            else
            {
                problems.Add(Problem((field, group, row), stored.Error ?? FieldMessageKinds.Invalid));
            }
        }

        return problems;
    }

    // A file's problem at its field, by the live version's names as the pipeline names them (ADR-0088).
    private static SubmissionProblem Problem((FormField Field, FormField? Group, int? Row) at, FieldMessageKinds kind) =>
        new(
            (FieldMessageProperties.All.FirstOrDefault(property => property.Kind == kind) ?? FieldMessageProperties.All.First(property => property.Kind == FieldMessageKinds.Invalid)).Name,
            at is { Group: { } group, Row: { } row } ? group.Alias + "[" + row.ToString(CultureInfo.InvariantCulture) + "]." + at.Field.Alias : at.Field.Alias!);

    // The stored files each file field's answer holds (ADR-0102), for the page to offer to keep: each token that names a
    // file of this form and field that no entry holds yet, with its name and size; any other token is left out.
    private async Task<KeptFiles> KeptAsync(FormPostReader reader, FormDefinition definition, Guid formKey, CancellationToken cancellationToken)
    {
        var (fields, rows) = reader.Shown();
        var kept = new KeptFiles();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (uploads.TypeOf(field) is FileFieldType && fields.GetValueOrDefault(field.Key) is { } answers)
            {
                kept.Fields[field.Key] = await StoredAsync(formKey, field.Key, answers, cancellationToken);
            }

            var given = rows.GetValueOrDefault(field.Key) ?? [];
            var rowFiles = new List<Dictionary<Guid, IReadOnlyList<FormPostFile>>>();
            foreach (var row in given)
            {
                var inRow = new Dictionary<Guid, IReadOnlyList<FormPostFile>>();
                foreach (var child in field.Fields.Where(child => uploads.TypeOf(child) is FileFieldType))
                {
                    if (row.GetValueOrDefault(child.Key) is { } childAnswers)
                    {
                        inRow[child.Key] = await StoredAsync(formKey, child.Key, childAnswers, cancellationToken);
                    }
                }

                rowFiles.Add(inRow);
            }

            kept.Rows[field.Key] = rowFiles;
        }

        return kept;
    }

    // The stored files an answer's tokens name.
    private async Task<IReadOnlyList<FormPostFile>> StoredAsync(Guid formKey, Guid fieldKey, FieldAnswers answers, CancellationToken cancellationToken)
    {
        var files = new List<FormPostFile>();
        foreach (var token in answers.Values.Distinct(StringComparer.Ordinal))
        {
            if (tokens.FileOf(token, formKey, fieldKey) is { } key && await entries.GetFileAsync(key, cancellationToken) is { EntryId: null } file && file.FieldKey == fieldKey)
            {
                files.Add(new FormPostFile(token, file.OriginalName, file.SizeBytes));
            }
        }

        return files;
    }

    // A repeatable group's fewest and most rows, held to the most any group may have.
    private (int Least, int Most) Limits(FormField group) =>
        types.FindType(group.Type) is RepeaterFieldType repeater && repeater.ReadSettings(group.Settings) is { } settings
            ? (Math.Min(settings.MinRows, RepeaterFieldType.MostRows), Math.Min(settings.MaxRows, RepeaterFieldType.MostRows))
            : (0, RepeaterFieldType.MostRows);

    // The alias an answer's "Change" names, goto:<alias> (ADR-0122); null for any other button.
    private static string? Goto(string? nav) =>
        nav?.Split(FormPostFields.Separator) is [FormPostFields.Goto, { Length: > 0 } alias] ? alias : null;

    // A whole number from a button, or null.
    private static int? Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    // A refusal as the page shows it (ADR-0100); false when the page shows the form as for any visitor now, with nothing of the post.
    private async Task<bool> RefusedAsync(Step state, SubmissionResult result, CachedForm cached, string culture, Guid? content, CancellationToken cancellationToken)
    {
        var words = RenderingWords.Of(culture);
        switch (result.Refusal)
        {
            case SubmissionRefusal.ValidationFailed:
                state.Problems(result.Problems);
                return true;
            case SubmissionRefusal.FormChanged:
                var shown = await requests.ShowAsync(cached, culture, await pages.PageAsync(content), cancellationToken);
                var changed = state.Reader.Ticked().Where(tick => shown.Consents.GetValueOrDefault(tick.Field) != tick.Hash).ToList();
                foreach (var (field, row, _) in changed)
                {
                    state.Reader.Untick(field, row);
                }

                var text = changed.Count switch
                {
                    0 => words.ChangedText,
                    1 => words.ChangedText + " " + words.ChangedConsent,
                    _ => words.ChangedText + " " + words.ChangedConsents,
                };
                state.Notice = new FormPostNotice(words.ChangedHeading, text, FormChangedCode);
                return true;
            case SubmissionRefusal.MemberRequired or SubmissionRefusal.InvalidRequest or SubmissionRefusal.DuplicateSubmission:
                state.Notice = new FormPostNotice(words.NotSentHeading, words.NotSentText, NotSentCode);
                return true;
            default:
                // Closed, full or gone: the form's render shows its notice, or nothing, as for any visitor now.
                return false;
        }
    }

    // An outcome's or a placement's page after a send (ADR-0129, ADR-0136) with a fragment of its own. The post's address
    // ends in the form's #<instance>, which a redirect without a fragment carries on (RFC 9110, 10.2.2), so the visitor
    // would land at whatever bears that id on the other page, such as another form; an empty fragment opens it at its top.
    private static string AwayFromTheForm(string url) => url.Contains('#', StringComparison.Ordinal) ? url : url + "#";

    // The page's address with the sent token, at the form (ADR-0004): the post's own address, which holds no query.
    private static string SentAddress(HttpRequest request, string token, string instance)
    {
        var path = (request.PathBase + request.Path).ToUriComponent();
        return QueryHelpers.AddQueryString(path.Length == 0 ? "/" : path, SentTokens.QueryName, token) + "#" + instance;
    }

    // Whether a post comes from the site's own origin, as the public API counts it (ADR-0012): a post without an origin
    // passes, as every browser sends one with a post; "null", several or another site's do not.
    private static bool IsOwnOrigin(HttpRequest request)
    {
        StringValues values = request.Headers.Origin;
        if (StringValues.IsNullOrEmpty(values))
        {
            return true;
        }

        if (values.Count != 1 || PublicApiRules.Origin(values[0]) is not { } origin)
        {
            return false;
        }

        // A proxy that ends TLS before the site hands it the site's own https pages' posts over http.
        return origin == PublicApiRules.Origin($"{request.Scheme}://{request.Host.Value}")
            || (!request.IsHttps && origin == PublicApiRules.Origin($"https://{request.Host.Value}"));
    }

    // The query of the page the post came from, by its Referer, when that is an address of the site's own origin; none otherwise.
    private static Dictionary<string, IReadOnlyList<string>> Referred(HttpRequest request)
    {
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (!Uri.TryCreate(request.Headers.Referer.LastOrDefault(), UriKind.Absolute, out var referer)
            || referer.Scheme is not ("http" or "https")
            || !string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return query;
        }

        foreach (var (name, values) in QueryHelpers.ParseQuery(referer.Query))
        {
            query[name] = [.. values.OfType<string>()];
        }

        return query;
    }

    // The query values of the names the form lists, each held to the pipeline's limits, so a long address never refuses a post.
    private static Dictionary<string, IReadOnlyList<string>> Held(IReadOnlyDictionary<string, IReadOnlyList<string>> query, IReadOnlyList<string> listed)
    {
        var held = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in query)
        {
            if (listed.Contains(name, StringComparer.Ordinal))
            {
                held[name] = [.. values.Where(value => value.Length <= FormSubmissions.MaxValueLength).Take(FormSubmissions.MaxQueryValues)];
            }
        }

        return held;
    }

    // A name's one value; null for none or several.
    private static string? One(IFormCollection posted, string name) => posted.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    // What a post leaves for the form's render: the answers, the errors at their fields and rows, the page and the move.
    private sealed class Step(FormPostReader reader, FormDefinition definition, Guid? page, IReadOnlyDictionary<string, IReadOnlyList<string>> query)
    {
        private readonly Dictionary<Guid, FormPostError> _errors = [];
        private readonly Dictionary<Guid, Dictionary<int, Dictionary<Guid, FormPostError>>> _rowErrors = [];
        private bool _isEmpty;
        private Guid? _page = page;
        private Guid? _errorPage;

        public FormPostReader Reader { get; } = reader;

        public FormPostMove Move { get; set; }

        public FormPostNotice? Notice { get; set; }

        // The pipeline's problems at their fields (ADR-0099): a field's first, by its path; the page then shows the first
        // page that holds one, or on the summary page that page itself (ADR-0122).
        public void Problems(IEnumerable<SubmissionProblem> problems)
        {
            var paths = new ProblemPaths(definition);
            Guid? first = null;
            foreach (var problem in problems)
            {
                if (problem.Code == SubmissionProblems.Empty)
                {
                    _isEmpty = true;
                    continue;
                }

                if (paths.Find(problem.Path) is not { } at)
                {
                    continue;
                }

                first ??= at.Top;
                var error = new FormPostError(problem.Code, problem.Check, problem.Rule, at.GridRow);
                if (at.Row is { } row)
                {
                    if (!_rowErrors.TryGetValue(at.Top, out var rows))
                    {
                        _rowErrors[at.Top] = rows = [];
                    }

                    if (!rows.TryGetValue(row, out var children))
                    {
                        rows[row] = children = [];
                    }

                    children.TryAdd(at.Field, error);
                }
                else
                {
                    _errors.TryAdd(at.Field, error);
                }
            }

            if (first is { } field && paths.PageOf(field) is { } holding)
            {
                _errorPage = holding;
                Move = FormPostMove.Stay;
            }
        }

        // An answer's "Change" on the summary page (ADR-0122): the page that holds the field, without a check; a name of
        // no field shows the posted page again.
        public void Open(FormField? field)
        {
            if (field is not null && new ProblemPaths(definition).PageOf(field.Key) is { } holding)
            {
                _page = holding;
            }

            Move = FormPostMove.Stay;
        }

        public FormPostState State(KeptFiles kept)
        {
            var (answers, rows) = Reader.Shown();
            var orders = Reader.Orders();
            return new FormPostState
            {
                Answers = answers,
                Errors = _errors,
                IsEmpty = _isEmpty,
                Rows = rows.ToDictionary(pair => pair.Key, pair => Rows(pair.Key, pair.Value, orders.Rows[pair.Key], kept.Rows.GetValueOrDefault(pair.Key) ?? [])),
                Orders = orders.Fields,
                Files = kept.Fields,
                Page = _page,
                ErrorPage = _errorPage,
                Move = Move,
                Query = query,
                Notice = Notice,
            };
        }

        // A group's rows with their errors, orders and files; the pipeline names no row past those posted.
        private IReadOnlyList<FormPostRow> Rows(Guid group, IReadOnlyList<Dictionary<Guid, FieldAnswers>> answers, IReadOnlyList<Dictionary<Guid, IReadOnlyList<string>>> orders, IReadOnlyList<Dictionary<Guid, IReadOnlyList<FormPostFile>>> files)
        {
            var errors = _rowErrors.GetValueOrDefault(group);
            return [.. answers.Select((row, index) => new FormPostRow
            {
                Answers = row,
                Errors = errors?.GetValueOrDefault(index) ?? new Dictionary<Guid, FormPostError>(),
                Orders = orders[index],
                Files = index < files.Count ? files[index] : new Dictionary<Guid, IReadOnlyList<FormPostFile>>(),
                IsNew = Reader.IsNew(group, index),
            })];
        }
    }

    // The stored files a post kept: each file field's outside groups, and each repeatable group's rows' by the child's key.
    private sealed class KeptFiles
    {
        public Dictionary<Guid, IReadOnlyList<FormPostFile>> Fields { get; } = [];

        public Dictionary<Guid, IReadOnlyList<Dictionary<Guid, IReadOnlyList<FormPostFile>>>> Rows { get; } = [];
    }

    // Where a problem's path is (ADR-0088): "alias", "alias.<row key>" for a grid's row, "group[row].child" and
    // "group[row].child.<row key>", by the live version's aliases, as the pipeline names them.
    private sealed class ProblemPaths
    {
        private readonly Dictionary<string, FormField> _top = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Dictionary<string, FormField>> _children = [];
        private readonly Dictionary<Guid, Guid> _pages = [];

        public ProblemPaths(FormDefinition definition)
        {
            foreach (var page in definition.Pages)
            {
                foreach (var field in page.Groups.SelectMany(group => group.Fields).Where(field => field.Alias is not null))
                {
                    _top[field.Alias!] = field;
                    _pages[field.Key] = page.Key;
                    _children[field.Key] = field.Fields.Where(child => child.Alias is not null).ToDictionary(child => child.Alias!, StringComparer.Ordinal);
                }
            }
        }

        public Guid? PageOf(Guid field) => _pages.TryGetValue(field, out var page) ? page : null;

        // The field outside groups, the field itself, its row in a group, and a grid's row; null for a path of no field.
        public (Guid Top, Guid Field, int? Row, Guid? GridRow)? Find(string path)
        {
            var open = path.IndexOf('[', StringComparison.Ordinal);
            if (open < 0)
            {
                var (alias, grid) = Split(path);
                return _top.TryGetValue(alias, out var field) ? (field.Key, field.Key, null, grid) : null;
            }

            var close = path.IndexOf(']', open);
            if (close < 0 || close + 1 >= path.Length || path[close + 1] != '.'
                || !int.TryParse(path.AsSpan(open + 1, close - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var row)
                || !_top.TryGetValue(path[..open], out var group))
            {
                return null;
            }

            var (child, childGrid) = Split(path[(close + 2)..]);
            return _children[group.Key].TryGetValue(child, out var inRow) ? (group.Key, inRow.Key, row, childGrid) : null;
        }

        // An alias and the grid row's key after its dot, if any.
        private static (string Alias, Guid? GridRow) Split(string path)
        {
            var dot = path.IndexOf('.', StringComparison.Ordinal);
            return dot < 0 ? (path, null) : (path[..dot], Guid.TryParseExact(path[(dot + 1)..], "D", out var key) ? key : null);
        }
    }
}
