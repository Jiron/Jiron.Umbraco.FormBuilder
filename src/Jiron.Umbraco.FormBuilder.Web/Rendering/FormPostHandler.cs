using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
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
/// step its button asks for run through the pipeline (Back moves without a check, Next checks the page as the validate
/// endpoint does, Send submits) or a row's Add or Remove or a ranking's move made without one, and the answers, the
/// errors, the page to move from and any notice left for the form's render through the request's items (ADR-0093); a sent
/// form redirects to its outcome. Every answer is <c>no-store</c>.
/// </summary>
/// <param name="forms">The published forms.</param>
/// <param name="requests">What the form endpoint answers, whose consent wordings a changed form's post is checked against.</param>
/// <param name="pages">The culture and the page.</param>
/// <param name="submissions">The submission pipeline.</param>
/// <param name="types">The field types.</param>
/// <param name="sent">The tokens of the redirect after a sent form.</param>
internal sealed class FormPostHandler(
    PublishedFormCache forms,
    FormRequests requests,
    FormPages pages,
    FormSubmissions submissions,
    FieldTypeRegistry types,
    SentTokens sent)
{
    /// <summary>The notice's code after a post that met a form published again meanwhile.</summary>
    public const string FormChangedCode = "form_changed";

    /// <summary>The notice's code after a post the form could not take, whose answers are kept.</summary>
    public const string NotSentCode = "not_sent";

    // The longest instance id a post names: the prefix and a count no page reaches.
    private const int MaxInstanceLength = 16;

    /// <summary>Reads a post and answers it.</summary>
    /// <param name="http">The request, which Umbraco routed to the surface action.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<FormPostAnswer> HandleAsync(HttpContext http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        NoStore(http.Response);
        if (http.Features.Get<UmbracoRouteValues>()?.PublishedRequest is not { } published || !http.Request.HasFormContentType)
        {
            return FormPostAnswer.BadRequest;
        }

        // Umbraco read the form already for its route token, so a post too large or malformed never gets here.
        var posted = await http.Request.ReadFormAsync(cancellationToken);
        if (One(posted, FormPostFields.Form) is not { } formText || !Guid.TryParseExact(formText, "D", out var formKey)
            || One(posted, FormPostFields.Instance) is not { } instance || !IsInstance(instance)
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
        var (fields, rows) = reader.Posted();
        var request = new SubmissionRequest
        {
            Form = formKey,
            Revision = revision,
            Culture = culture,
            ContentKey = content,
            Fields = fields,
            Rows = rows,
            Query = query,
            Context = reader.Context,
        };
        var page = Guid.TryParseExact(One(posted, FormPostFields.Page), "D", out var pageKey) ? pageKey : (Guid?)null;
        var state = new Step(reader, definition, page, query);
        var nav = posted[FormPostFields.Nav].LastOrDefault();
        switch (nav)
        {
            case FormPostFields.Back:
                state.Move = FormPostMove.Back;
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
                        ? url
                        : SentAddress(http.Request, sent.Issue(formKey, receipt.SubmissionId, instance), instance));
                }

                if (!await RefusedAsync(state, result, cached, culture, content, cancellationToken))
                {
                    return FormPostAnswer.Page;
                }

                break;
            default:
                Button(reader, nav);
                break;
        }

        FormPosts.Set(http, formKey, instance, state.State());
        return FormPostAnswer.Page;
    }

    // A row's Add or Remove, or a ranking's move in the order the page showed (ADR-0101): the posted page again with the
    // change and every answer, without a check. An Add past the group's most, a Remove below its
    // least, a move past an end and any other button change nothing.
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

    // A repeatable group's fewest and most rows, held to the most any group may have.
    private (int Least, int Most) Limits(FormField group) =>
        types.FindType(group.Type) is RepeaterFieldType repeater && repeater.ReadSettings(group.Settings) is { } settings
            ? (Math.Min(settings.MinRows, RepeaterFieldType.MostRows), Math.Min(settings.MaxRows, RepeaterFieldType.MostRows))
            : (0, RepeaterFieldType.MostRows);

    // A whole number from a button, or null.
    private static int? Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    // Every answer of the action, set before anything runs and again as the response starts, after anything else set it.
    private static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.OnStarting(() =>
        {
            response.Headers.CacheControl = "no-store";
            response.Headers.Remove("Expires");
            return Task.CompletedTask;
        });
    }

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

    // An instance id as the form's render gives it: the prefix and a number.
    private static bool IsInstance(string instance) =>
        instance.Length <= MaxInstanceLength && instance.StartsWith(FormInstances.Prefix, StringComparison.Ordinal) && instance.Length > FormInstances.Prefix.Length
        && !instance.AsSpan(FormInstances.Prefix.Length).ContainsAnyExceptInRange('0', '9');

    // What a post leaves for the form's render: the answers, the errors at their fields and rows, the page and the move.
    private sealed class Step(FormPostReader reader, FormDefinition definition, Guid? page, IReadOnlyDictionary<string, IReadOnlyList<string>> query)
    {
        private readonly Dictionary<Guid, FormPostError> _errors = [];
        private readonly Dictionary<Guid, Dictionary<int, Dictionary<Guid, FormPostError>>> _rowErrors = [];
        private bool _isEmpty;
        private Guid? _page = page;

        public FormPostReader Reader { get; } = reader;

        public FormPostMove Move { get; set; }

        public FormPostNotice? Notice { get; set; }

        // The pipeline's problems at their fields (ADR-0099): a field's first, by its path; the page then shows the first
        // page that holds one.
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
                _page = holding;
                Move = FormPostMove.Stay;
            }
        }

        public FormPostState State()
        {
            var (answers, rows) = Reader.Shown();
            var orders = Reader.Orders();
            return new FormPostState
            {
                Answers = answers,
                Errors = _errors,
                IsEmpty = _isEmpty,
                Rows = rows.ToDictionary(pair => pair.Key, pair => Rows(pair.Key, pair.Value, orders.Rows[pair.Key])),
                Orders = orders.Fields,
                Page = _page,
                Move = Move,
                Query = query,
                Notice = Notice,
            };
        }

        // A group's rows with their errors and orders; the pipeline names no row past those posted.
        private IReadOnlyList<FormPostRow> Rows(Guid group, IReadOnlyList<Dictionary<Guid, FieldAnswers>> answers, IReadOnlyList<Dictionary<Guid, IReadOnlyList<string>>> orders)
        {
            var errors = _rowErrors.GetValueOrDefault(group);
            return [.. answers.Select((row, index) => new FormPostRow
            {
                Answers = row,
                Errors = errors?.GetValueOrDefault(index) ?? new Dictionary<Guid, FormPostError>(),
                Orders = orders[index],
                IsNew = Reader.IsNew(group, index),
            })];
        }
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
