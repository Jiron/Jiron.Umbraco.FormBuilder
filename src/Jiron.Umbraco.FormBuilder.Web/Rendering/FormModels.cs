using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Web.Common.Security;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>A form to render: the published form, as the form endpoint shows it, where and in which culture.</summary>
/// <param name="HttpContext">The request.</param>
/// <param name="Form">The published form.</param>
/// <param name="Shown">The form as the form endpoint shows it for the culture and the page.</param>
/// <param name="Culture">The culture, one of the site's languages.</param>
/// <param name="Instance">The instance id.</param>
/// <param name="Context">The context values the page gives, by name; empty for none.</param>
/// <param name="Theme">The form's theme (ADR-0105), whose classes and view folder the models take.</param>
/// <param name="Gate">Why a form for members shows its notice to the visitor; null when the form shows.</param>
/// <param name="Page">
/// The path of the page the form posts to without JavaScript, as the render endpoint gives it (ADR-0104); null for the
/// request's own.
/// </param>
/// <param name="Member">The key of the member signed in, for a form that takes values from the member (ADR-0124); null otherwise.</param>
/// <param name="Redirect">The placement's page after a send (ADR-0129), which the route token carries; null for the outcome.</param>
/// <param name="Content">The page the form is on, whose site's settings apply to a send (ADR-0120); null outside a page.</param>
internal sealed record FormRender(
    HttpContext HttpContext,
    CachedForm Form,
    FormShown Shown,
    string Culture,
    string Instance,
    IReadOnlyDictionary<string, string?> Context,
    IFormBuilderTheme Theme,
    MemberGate? Gate = null,
    string? Page = null,
    Guid? Member = null,
    Guid? Redirect = null,
    Guid? Content = null)
{
    /// <summary>What the server prefills (ADR-0124), worked out as the form's page is built.</summary>
    public FormPrefill Prefill { get; init; } = FormPrefill.None;
}

/// <summary>
/// The view models of a form (ADR-0015, ADR-0093): its texts as the form endpoint shows them in the culture, a visitor
/// text that holds tags rendered by the merge-tag service for the answers the page holds (ADR-0095), its ids below its
/// instance, and every page, group and field in the state its rules give for what its controls start with (or a post's
/// answers), read as the submission pipeline reads them, with the site's clock and time zone, no member and no query
/// value, so the markup is the same for every visitor but where a form for members does not admit them; a calculation
/// shows the value the rules work out (ADR-0097), and a repeatable group's rows each follow their own state (ADR-0098).
/// A hidden field renders nothing, nor does a field whose type the site lacks, a calculation visitors do not see, or
/// the captcha's place on a form without a captcha. Options from a list or another source, and options a parent field
/// filters, are those the options endpoint answers for the page's answers (ADR-0096).
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="mergeTags">The merge-tag service, which renders the visitor texts that hold tags.</param>
/// <param name="answerTexts">The answers' texts as merge tags show them, which the summary page lists (ADR-0122).</param>
/// <param name="cascading">The options of a field for its parents' answers, as the options endpoint resolves them.</param>
/// <param name="views">Which view writes each part.</param>
/// <param name="paths">The public API's paths.</param>
/// <param name="media">The published media, which an image field shows.</param>
/// <param name="urls">The addresses of media.</param>
/// <param name="time">The site's clock.</param>
/// <param name="cultures">The culture formats a calculation's and a slider's numbers are written with.</param>
/// <param name="protection">The site's Data Protection, which Umbraco's route token for a post without JavaScript takes.</param>
/// <param name="placements">The tokens that carry a placement's page after a send through a page's send (ADR-0120).</param>
/// <param name="logger">Where context values that are not taken are logged.</param>
internal sealed partial class FormModels(
    FieldTypeRegistry types,
    IFormBuilderMergeTagService mergeTags,
    MergeTagAnswerTexts answerTexts,
    CascadingOptions cascading,
    FormViews views,
    FormBuilderPublicApiPaths paths,
    IPublishedMediaCache media,
    IPublishedUrlProvider urls,
    TimeProvider time,
    MergeTagCultures cultures,
    IDataProtectionProvider protection,
    PlacementTokens placements,
    ILogger<FormModels> logger)
{
    /// <summary>The longest context value a page may give (ADR-0088).</summary>
    public const int MaxContextValueLength = 2_000;

    /// <summary>The form's <c>enctype</c> when it holds a file field.</summary>
    public const string Multipart = "multipart/form-data";

    /// <summary>The prefix of the hidden inputs that carry context values.</summary>
    public const string ContextPrefix = "jfb-context-";

    /// <summary>The prefix of the hidden input that carries a consent's wording hash, before the field's alias (ADR-0015).</summary>
    public const string ConsentPrefix = "jfb-consent-";

    /// <summary>The code of the notice after a send the server failed on (ADR-0022).</summary>
    public const string ServerErrorCode = "server_error";

    /// <summary>The code of the notice after a send that got no answer in time, or none at all (ADR-0022).</summary>
    public const string TimeoutCode = "timeout";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoQuery = FrozenDictionary<string, IReadOnlyList<string>>.Empty;

    // The context values not taken in this process, by form and name, so each is logged once.
    private readonly ConcurrentDictionary<string, bool> _contextLogged = new(StringComparer.Ordinal);

    /// <summary>The view a form starts from and its model.</summary>
    /// <param name="render">The form to render.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="InvalidOperationException">A part, or the control of a field's type, has no view.</exception>
    public async Task<(string View, FormViewModel Model)> BuildAsync(FormRender render, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(render);
        var form = render.Form.Published;
        var (formView, parts) = await views.PartsAsync(render.Theme.ViewFolder, form.Form.Alias, cancellationToken);
        var classes = new ThemeClassesModel(render.Theme.Classes);
        var words = RenderingWords.Of(render.Culture);
        var post = FormPosts.Get(render.HttpContext, form.Form.Key, render.Instance)
            ?? (FormPostFileNotes.Get(render.HttpContext, form.Form.Key, render.Instance) is { IsRefused: true }
                ? new FormPostState { Notice = new FormPostNotice(words.NotSentHeading, words.TooLargeText, FormPostHandler.TooLargeCode) }
                : null);
        var culture = CultureInfo.GetCultureInfo(render.Culture);
        var request = render.HttpContext.Request;
        var path = render.Page ?? (request.PathBase + request.Path).ToUriComponent();
        var display = post?.Outcome is not null ? FormDisplay.Outcome : render.Shown.Unavailable is not null || render.Gate is not null ? FormDisplay.Notice : FormDisplay.Form;
        var root = new Root(
            form.Form.Key,
            form.Form.Alias,
            form.Form.PublicRevision,
            render.Instance,
            render.Culture,
            culture.TextInfo.IsRightToLeft ? "rtl" : "ltr",
            Classes("jfb-form", "jfb-theme-" + render.Theme.Name, render.Theme.Classes.GetValueOrDefault(FormBuilderThemeSlots.Form), render.Shown.Form.Settings.CssClass),
            display,
            (path.Length == 0 ? "/" : path) + (HoldsFile(form.Definition) ? "?" + FormPostFields.PostMarker + "=" + form.Form.Key.ToString("D") + "." + render.Instance : string.Empty) + "#" + render.Instance,
            HoldsFile(form.Definition) ? Multipart : null,
            (request.PathBase + new PathString(paths.Root)).ToUriComponent(),
            // Only a form on show reads it, and a notice or an outcome must not give away what a visitor it does not admit may not see (ADR-0150).
            display == FormDisplay.Form ? ModelData.Of(render.Shown.Answer.Body) : HtmlString.Empty,
            parts,
            classes);
        var model = display switch
        {
            FormDisplay.Outcome => root.Model(outcome: Outcome(post!.Outcome!, words, classes, root.Views, await CopyAsync(render, root, post.Outcome!.Copy, words, culture, cancellationToken))),
            FormDisplay.Notice => root.Model(notice: render.Shown.Unavailable is not null ? Unavailable(render, words, culture, classes) : Gated(render.Gate!.Value, words, classes)),
            _ => await FormAsync(render, root, post, words, culture, cancellationToken),
        };
        return (formView, model);
    }

    // The notice of a form for members to a visitor it does not admit, in the culture (ADR-0095); a form that takes no
    // entry now says so first, since signing in would not help.
    private static NoticeViewModel Gated(MemberGate gate, RenderingWords words, ThemeClassesModel classes) => gate == MemberGate.SignIn
        ? new NoticeViewModel { Classes = classes, CssClass = classes.Join("jfb-notice", FormBuilderThemeSlots.Notice), Kind = NoticeKind.SignIn, Heading = words.SignInHeading, Text = Paragraph(words.SignInText) }
        : new NoticeViewModel { Classes = classes, CssClass = classes.Join("jfb-notice", FormBuilderThemeSlots.Notice), Kind = NoticeKind.MemberGroups, Heading = words.GroupsHeading, Text = Paragraph(words.GroupsText) };

    // The notice of a form that takes no entry now: the form's closed message when it is closed or full, otherwise the
    // package's heading and sentence; a form that opens later says when, in the site's time zone and the culture.
    private NoticeViewModel Unavailable(FormRender render, RenderingWords words, CultureInfo culture, ThemeClassesModel classes)
    {
        var availability = render.Form.Published.Definition.Settings.Availability;
        if (render.Shown.Unavailable == FormUnavailable.Closed && availability.OpensAt is { } opens && time.GetUtcNow().UtcDateTime < opens)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(opens, DateTimeKind.Utc), render.Shown.TimeZone);
            return new NoticeViewModel
            {
                Classes = classes,
                CssClass = classes.Join("jfb-notice", FormBuilderThemeSlots.Notice),
                Kind = NoticeKind.NotOpenYet,
                Heading = string.Format(culture, words.OpensHeading, local.ToString("f", culture)),
                Text = Paragraph(words.OpensText),
            };
        }

        var full = render.Shown.Unavailable == FormUnavailable.Full;
        var kind = full ? NoticeKind.Full : NoticeKind.Closed;
        return render.Shown.Form.Settings.ClosedMessage?.Default is { Length: > 0 } message
            ? new NoticeViewModel { Classes = classes, CssClass = classes.Join("jfb-notice", FormBuilderThemeSlots.Notice), Kind = kind, Text = new HtmlString(message) }
            : new NoticeViewModel { Classes = classes, CssClass = classes.Join("jfb-notice", FormBuilderThemeSlots.Notice), Kind = kind, Heading = full ? words.FullHeading : words.ClosedHeading, Text = Paragraph(full ? words.FullText : words.ClosedText) };
    }

    private static OutcomeViewModel Outcome(FormPostOutcome outcome, RenderingWords words, ThemeClassesModel classes, FormViewPaths views, OutcomeCopyModel? copy) => new()
    {
        Classes = classes,
        Views = views,
        Copy = copy,
        Message = new HtmlString(outcome.Html),
        Reference = outcome.Reference is { Length: > 0 } reference ? WithMarkup(words.Reference, "<strong>" + WebUtility.HtmlEncode(reference) + "</strong>") : null,
        IsPending = outcome.IsPending,
        Continue = outcome.ContinueUrl is { } url ? new OutcomeLinkModel(url, words.Continue) : null,
    };

    // What the runtime shows a send with (ADR-0120, ADR-0022): the notices just above the buttons, whose rate limit's
    // text the runtime words as a post's (WaitNotice), and the outcome without its message or its reference's value.
    private static FormSendingModel Sending(Root root, RenderingWords words, NavigationViewModel? moves) => new()
    {
        Moves = moves,
        SendingText = words.Sending,
        ThanksText = words.ThankYou,
        Notices =
        [
            SendNotice(root, words, FormPostHandler.RateLimitedCode, new HtmlString("<p data-jfb-seconds=\"" + WebUtility.HtmlEncode(words.WaitSecondsText) + "\" data-jfb-minutes=\"" + WebUtility.HtmlEncode(words.WaitMinutesText) + "\"></p>")),
            SendNotice(root, words, ServerErrorCode, Paragraph(words.ServerErrorText)),
            SendNotice(root, words, TimeoutCode, Paragraph(words.ConnectionText)),
            SendNotice(root, words, FormPostHandler.FormChangedCode, new HtmlString("<p data-jfb-one=\"" + WebUtility.HtmlEncode(words.ChangedConsent) + "\" data-jfb-other=\"" + WebUtility.HtmlEncode(words.ChangedConsents) + "\">" + WebUtility.HtmlEncode(words.ChangedText) + "</p>"), words.ChangedHeading),
        ],
        Outcome = new OutcomeViewModel
        {
            Classes = root.Classes,
            Views = root.Views,
            Message = HtmlString.Empty,
            Reference = WithMarkup(words.Reference, "<strong></strong>"),
            Continue = new OutcomeLinkModel("#", words.GoNow),
            Countdown = new OutcomeCountdownModel(words.CountdownOne, words.CountdownOther, words.StayHere),
        },
    };

    private static NoticeViewModel SendNotice(Root root, RenderingWords words, string code, IHtmlContent text, string? heading = null) => new()
    {
        Classes = root.Classes,
        Kind = NoticeKind.Message,
        Heading = heading ?? words.NotSentHeading,
        Text = text,
        Code = code,
        HeadingId = root.Id + "-notice-" + code,
        CssClass = root.Classes.Join(heading is null ? "jfb-notice jfb-notice-error" : "jfb-notice", FormBuilderThemeSlots.Notice),
    };

    // The page a post moves to (ADR-0100): from the posted page, the next one or the last one before it that the rules
    // show, else the posted page again; a page the rules hide, or a first render, shows the first page they show. A post
    // that left errors shows the first page that holds one, unless it came from the summary page (ADR-0122).
    private static PublicPage? CurrentOf(IReadOnlyList<PublicPage> pages, List<PublicPage> shownPages, FormPostState? post, PublicFormSettings settings)
    {
        if (post?.ErrorPage is { } erring)
        {
            var from = shownPages.FirstOrDefault(page => page.Key == post.Page);
            return SummaryPageOf(settings, shownPages, from) ?? shownPages.FirstOrDefault(page => page.Key == erring) ?? from ?? shownPages.FirstOrDefault();
        }

        var at = post?.Page is { } posted ? pages.ToList().FindIndex(page => page.Key == posted) : -1;
        var moved = at < 0 ? null : post!.Move switch
        {
            FormPostMove.Next => pages.Skip(at + 1).FirstOrDefault(shownPages.Contains),
            FormPostMove.Back => pages.Take(at).LastOrDefault(shownPages.Contains),
            _ => null,
        };
        return moved ?? shownPages.FirstOrDefault(page => page.Key == post?.Page) ?? shownPages.FirstOrDefault();
    }

    // What a post without JavaScript carries besides the answers (ADR-0100): Umbraco's route token for the surface
    // action, the form, its instance, its revision and the page shown, and the query values a post carried; none is per
    // visitor on a first render.
    private List<HiddenInputModel> PostValues(Root root, PublicPage? current, FormPostState? post, Guid? redirect)
    {
        // The placement's page after a send rides in the token, which only the server can write and read (ADR-0129).
        var routeValues = redirect is { } page ? new Dictionary<string, object> { [FormPostFields.Redirect] = page.ToString("D") } : null;
        var values = new List<HiddenInputModel>
        {
            new(FormPostFields.RouteToken, EncryptionHelper.CreateEncryptedRouteString(protection, FormBuilderSurfaceController.ControllerName, FormBuilderSurfaceController.ActionName, string.Empty, routeValues)),
            new(FormPostFields.Form, root.Key.ToString("D")),
            new(FormPostFields.Instance, root.Id),
            new(FormPostFields.Revision, root.Revision.ToString(CultureInfo.InvariantCulture)),
        };
        if (current is not null)
        {
            values.Add(new(FormPostFields.Page, current.Key.ToString("D")));
        }

        foreach (var (name, texts) in post?.Query ?? NoQuery)
        {
            values.AddRange(texts.Select(text => new HiddenInputModel(FormPostFields.QueryPrefix + name, text)));
        }

        return values;
    }

    private async Task<FormViewModel> FormAsync(FormRender render, Root root, FormPostState? post, RenderingWords words, CultureInfo culture, CancellationToken cancellationToken)
    {
        var shown = render.Shown.Form;
        var settings = shown.Settings;
        var definition = render.Form.Published.Definition;
        var utc = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var now = utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var local = TimeZoneInfo.ConvertTime(utc, render.Shown.TimeZone).DateTime;
        render = render with { Prefill = await PrefillAsync(render, definition, DateOnly.FromDateTime(local), cancellationToken) };
        var answers = Answers(shown, definition, post, local, render.Prefill);
        var read = Read(definition, answers);
        var state = FormRules.Evaluate(
            DefinitionLogic.From(definition, types),
            RuleAnswers(read),
            new EvaluationContext(now, render.Shown.TimeZone.Id, render.Culture, new Member(false, []), post?.Query ?? NoQuery, []));
        var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
        var required = state.Required.ToHashSet(StringComparer.Ordinal);
        var shownPages = shown.Pages.Where(page => !hidden.Contains(Id(page.Key))).ToList();
        var current = CurrentOf(shown.Pages, shownPages, post, settings);
        var summaryPage = SummaryPageOf(settings, shownPages, current);
        var recalled = await RecalledAsync(render, read, hidden, state, cancellationToken);
        var facts = new PageFacts(DateOnly.FromDayNumber(CalendarValues.Today(now, render.Shown.TimeZone.Id)), state.Calculations, state.Rows, cultures.For(render.Culture));
        var build = new Build(render, root, post, words, culture, answers, hidden, required, MarkerOf(settings.RequiredMarker), recalled, settings.ChoiceLayout, facts, HasRules(shown));
        var pages = new List<PageViewModel>();

        // Where each field's error links are in the build's errors, for the summary page's rows (ADR-0122).
        var linked = new Dictionary<Guid, (int From, int To)>();
        foreach (var page in shown.Pages)
        {
            var pageHidden = hidden.Contains(Id(page.Key));
            var groups = new List<GroupViewModel>();
            foreach (var group in page.Groups)
            {
                var groupHidden = hidden.Contains(Id(group.Key));
                var fields = new List<FieldViewModel>();
                foreach (var field in group.Fields)
                {
                    var from = build.Errors.Count;
                    if (await FieldAsync(build, field, cancellationToken) is { } built)
                    {
                        fields.Add(built);
                    }

                    linked[field.Key] = (from, build.Errors.Count);
                }

                groups.Add(new GroupViewModel
                {
                    Key = group.Key,
                    Title = build.Text(group.Key, "title", group.Title),
                    Description = build.Html(group.Key, "description", group.Description),
                    DescriptionId = root.Id + "-" + group.Key.ToString("N") + "-help",
                    Recalling = build.Recalling(group.Key),
                    Classes = root.Classes,
                    CssClass = Classes("jfb-group", "jfb-columns-" + Math.Clamp(group.Columns, 1, 4).ToString(CultureInfo.InvariantCulture), group.Style == GroupStyle.Card ? "jfb-group-card" : null, render.Theme.Classes.GetValueOrDefault(FormBuilderThemeSlots.Group)),
                    IsHidden = groupHidden,
                    Fields = fields,
                    Views = root.Views,
                });
            }

            pages.Add(new PageViewModel
            {
                Classes = root.Classes,
                Key = page.Key,
                Title = build.Text(page.Key, "title", page.Title),
                Description = build.Html(page.Key, "description", page.Description),
                Recalling = build.Recalling(page.Key),
                IsCurrent = page == current,
                IsHidden = pageHidden,
                Summary = page == summaryPage ? await SummaryAsync(build, pages, shownPages, read, state, linked, cancellationToken) : null,
                Groups = groups,
                Views = root.Views,
            });
        }

        var navigation = Navigation(settings, shownPages, current, words, root.Classes);
        var errors = Summary(build, settings.ValidationSummary, pages.FirstOrDefault(page => page.IsCurrent));
        var honeypot = FormHoneypot.NameOf(root.Key, root.Revision);
        return root.Model(
            pages: pages,
            defaultButton: summaryPage is not null || pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Any(field => field.Control is RankingControlModel or RepeaterControlModel)
                ? navigation.Next ?? navigation.Submit
                : null,
            progress: Progress(settings, shown.Pages, shownPages, current, build),
            errors: errors.Count > 0
                ? new ErrorSummaryViewModel { Classes = root.Classes, Heading = VisitorMessages.SummaryHeading(errors.Count, render.Culture), HeadingId = root.Id + "-summary", Errors = errors }
                : null,
            navigation: navigation,
            requiredNote: build.MarksRequired || (build.Hooks && build.Marker == RequiredMarker.Required && build.WritesMarks)
                ? WithMarkup(words.RequiredNote, "<span aria-hidden=\"true\">*</span><span class=\"jfb-visually-hidden\">" + WebUtility.HtmlEncode(words.Asterisk) + "</span>")
                : null,
            isRequiredNoteHidden: !build.MarksRequired,
            hooks: build.Hooks,
            notice: post?.Notice is { } notice
                ? new NoticeViewModel { Classes = root.Classes, Kind = NoticeKind.Message, Heading = notice.Heading, Text = Paragraph(notice.Text), Code = notice.Code, HeadingId = notice.Code is null ? null : root.Id + "-notice", CssClass = root.Classes.Join(notice.Code is FormPostHandler.NotSentCode or FormPostHandler.TooLargeCode or FormPostHandler.RateLimitedCode ? "jfb-notice jfb-notice-error" : "jfb-notice", FormBuilderThemeSlots.Notice) }
                : null,
            honeypot: new HoneypotModel(root.Id + "-" + honeypot, honeypot, words.Honeypot),
            context: Context(render),
            postValues: PostValues(root, current, post, render.Redirect),
            contentKey: render.Content,
            placement: render.Redirect is { } placement ? placements.Issue(root.Key, placement) : null,
            sending: Sending(root, words, shown.Pages.Count > 1 ? Moves(settings, words, root.Classes) : null));
    }

    // The visitor texts that hold tags, each rendered as the server renders it in its place for the answers the page
    // holds (ADR-0068, ADR-0095), by its owner and name.
    private async Task<Dictionary<(Guid Owner, string Name), string>> RecalledAsync(FormRender render, PageRead read, HashSet<string> hidden, FormState state, CancellationToken cancellationToken)
    {
        var recalled = new Dictionary<(Guid Owner, string Name), string>();
        if (render.Shown.Tagged.Count == 0)
        {
            return recalled;
        }

        var scope = render.Shown.Scope with { Answers = RecallAnswers(render.Form.Published.Definition, read, hidden, state) };
        foreach (var text in render.Shown.Tagged)
        {
            var context = text.IsHtml ? MergeTagContext.Html : MergeTagContext.Text;
            recalled[(text.Owner, text.Name)] = (await mergeTags.RenderAsync(text.Text, MergeTagPlace.VisitorText, context, scope with { TextOwner = text.Owner }, cancellationToken)).Text;
        }

        return recalled;
    }

    // The page's answers as their fields' types read them (ADR-0058), which is how both runtimes recall them, a
    // repeatable group's child in each row that shows it, and the fields the rules hid.
    private static MergeTagAnswers RecallAnswers(FormDefinition definition, PageRead read, HashSet<string> hidden, FormState state) =>
        new(
            [
                .. read.Fields.Select(pair => new MergeTagAnswer(pair.Key, pair.Value)),
                .. read.Rows.SelectMany(group => group.Value.SelectMany((row, at) => row
                    .Where(pair => !(state.Rows.GetValueOrDefault(Id(group.Key)) is { } states && at < states.Count && states[at].Hidden.Contains(Id(pair.Key))))
                    .Select(pair => new MergeTagAnswer(pair.Key, pair.Value, row: at)))),
            ],
            AllFields(definition).Where(field => hidden.Contains(Id(field.Key))).Select(field => field.Key));

    // Each answer as the submission pipeline reads it before its rules (SubmissionReader): cleaned, held to its type's
    // limits and read by its type, and a repeatable group's rows only while their number keeps the group's limits, each
    // row's answers read so. An answer past its limits or one its type cannot read is none. A hidden field's answer
    // shows nowhere on the page: publishing keeps rules and formulas off it, and a visitor text shows nothing of it
    // (ADR-0068).
    private PageRead Read(FormDefinition definition, PageAnswers answers)
    {
        var read = new PageRead([], []);
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (types.FindType(field.Type) is not { } type)
            {
                continue;
            }

            if (!type.Declaration.Features.HasFlag(FieldFeatures.ChildFields))
            {
                ReadInto(read.Fields, read.Unreadable, field, type, answers.Fields);
                continue;
            }

            var rows = answers.Rows.GetValueOrDefault(field.Key) ?? [];
            if (FieldValues.CheckRows(type, field, rows.Count).IsEmpty)
            {
                var unreadable = new List<HashSet<Guid>>();
                read.Rows[field.Key] = [.. rows.Select(row =>
                {
                    var inRow = new Dictionary<Guid, ImmutableArray<FieldValue>>();
                    var unread = new HashSet<Guid>();
                    foreach (var child in field.Fields)
                    {
                        if (types.FindType(child.Type) is { } childType)
                        {
                            ReadInto(inRow, unread, child, childType, row);
                        }
                    }

                    unreadable.Add(unread);
                    return inRow;
                })];
                read.UnreadableRows[field.Key] = unreadable;
            }
        }

        return read;
    }

    // One field's answer read as the pipeline reads it, when it has one its limits and its type take; one over its limits
    // or that its type cannot read is unreadable (ADR-0114).
    private static void ReadInto(Dictionary<Guid, ImmutableArray<FieldValue>> read, HashSet<Guid> unreadable, FormField field, IFormBuilderFieldType type, Dictionary<Guid, FieldAnswers> answers)
    {
        if (!answers.TryGetValue(field.Key, out var given))
        {
            return;
        }

        var cleaned = SubmissionReader.Clean(new PostedAnswer(
            [.. given.Values],
            given.Parts.ToDictionary(part => part.Key, part => (IReadOnlyList<string?>)[.. part.Value], StringComparer.Ordinal)));
        var reading = FieldValues.CheckLimits(type, field, cleaned) is null ? FieldValues.Read(type, field, cleaned) : null;
        if (reading is null or { IsUnreadable: true })
        {
            unreadable.Add(field.Key);
        }
        else if (reading.Values.Length > 0)
        {
            read[field.Key] = reading.Values;
        }
    }

    // The answers as the rules see them, as the pipeline hands them over: each field's values by its id, a grid's by its
    // rows' ids, and each repeatable group's rows so.
    private static FormAnswers RuleAnswers(PageRead read)
    {
        static IReadOnlyDictionary<string, IReadOnlyList<string>> Texts(Dictionary<Guid, ImmutableArray<FieldValue>> answers)
        {
            var texts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (key, values) in answers)
            {
                foreach (var answered in values.GroupBy(value => value.Row))
                {
                    texts[Id(answered.Key ?? key)] = [.. answered.Select(value => value.Text)];
                }
            }

            return texts;
        }

        static IReadOnlySet<string> Ids(HashSet<Guid> keys) => keys.Select(Id).ToHashSet(StringComparer.Ordinal);

        return new FormAnswers(
            Texts(read.Fields),
            read.Rows.ToDictionary(group => Id(group.Key), group => (IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>)[.. group.Value.Select(Texts)], StringComparer.Ordinal))
        {
            Unreadable = Ids(read.Unreadable),
            UnreadableRows = read.UnreadableRows.ToDictionary(group => Id(group.Key), group => (IReadOnlyList<IReadOnlySet<string>>)[.. group.Value.Select(Ids)], StringComparer.Ordinal),
        };
    }

    // Each field's answers and each repeatable group's rows (RowsOf): a post's; else what the controls start with
    // (ADR-0097): a field's default value when that holds no merge tag (a default with tags is the browser's to fill,
    // ADR-0014), or without a default a date's today, a date and time's now and a slider's middle (Start).
    private PageAnswers Answers(PublicForm form, FormDefinition definition, FormPostState? post, DateTime local, FormPrefill prefill)
    {
        var defined = AllFields(definition).GroupBy(field => field.Key).ToDictionary(group => group.Key, group => group.First());
        var answers = new PageAnswers([], []);
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (types.FindType(field.Type)?.Declaration.Features.HasFlag(FieldFeatures.ChildFields) == true)
            {
                answers.Rows[field.Key] = RowsOf(field, post, defined, local, prefill);
                answers.StartedRows[field.Key] = StartedRow(field, defined, local, prefill);
            }
            else if ((post is null ? StartOf(field, defined[field.Key], local, prefill) : post.Answers.GetValueOrDefault(field.Key)) is { } given)
            {
                answers.Fields[field.Key] = given;
            }
        }

        return answers;
    }

    // What a field's control starts with: the server's prefill (the member's value or a context value, ADR-0124), else
    // its default value when that holds no merge tag, or without a default its type's start; none for a default with a tag.
    private FieldAnswers? StartOf(PublicField field, FormField defined, DateTime local, FormPrefill prefill)
    {
        if (prefill.Values.GetValueOrDefault(field.Key) is { } prefilled)
        {
            return prefilled;
        }

        var texts = field.DefaultValue is { } defaults ? Untagged(defaults.Values) : [];
        if (texts is { Count: > 0 })
        {
            return new FieldAnswers(texts);
        }

        return texts is not null && Start(defined, local) is { } start ? new FieldAnswers([start]) : null;
    }

    // What a control without a default starts with: a date that starts at today the site's today; a date and time that
    // does the site's time now, to the minute and back to its step from midnight; a slider its middle (SliderStart).
    private string? Start(FormField field, DateTime local) => types.Find(field.Type)?.Type switch
    {
        DateFieldType date when date.ReadSettings(field.Settings).DefaultToday => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeFieldType dateTime when dateTime.ReadSettings(field.Settings) is { DefaultToday: true } typed => Now(local, typed.StepMinutes ?? 1),
        SliderFieldType slider => SliderStart(slider.ReadSettings(field.Settings)),
        _ => null,
    };

    // A date and time at a local time, to the minute and back to a step of minutes from midnight.
    private static string Now(DateTime local, int step)
    {
        var minutes = (local.Hour * 60) + local.Minute;
        minutes -= minutes % step;
        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T" + (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    // Every field of a version, a repeatable group's children after it.
    private static IEnumerable<FormField> AllFields(FormDefinition definition) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field));

    // The default's texts when none holds a merge tag, the empty ones left out; null when one holds a tag.
    private static List<string>? Untagged(IEnumerable<string> defaults)
    {
        var texts = new List<string>();
        foreach (var text in defaults)
        {
            var parsed = MergeTags.Parse(text, MergeTagContext.Text);
            if (!parsed.Parts.All(part => part is TextPart))
            {
                return null;
            }

            if (string.Concat(parsed.Parts.Cast<TextPart>().Select(part => part.Text)) is { Length: > 0 } plain)
            {
                texts.Add(plain);
            }
        }

        return texts;
    }

    // A field's view model, built where the build stands: the page, or a repeatable group's row (Build.Row).
    private async Task<FieldViewModel?> FieldAsync(Build build, PublicField field, CancellationToken cancellationToken)
    {
        if (types.Find(field.Type) is not { } traits
            || build.Render.Prefill.FromMember.Contains(field.Key)
            || traits.Type is HiddenFieldType
            || (traits.Type is CalculationFieldType calculation && !calculation.ReadSettings(build.Definition(field.Key).Settings).Visible)
            || (traits.Type is CaptchaFieldType && build.Render.Shown.Form.Settings.CaptchaProvider is null))
        {
            return null;
        }

        var id = Id(field.Key);
        var control = build.Prefix + "-" + (field.Alias ?? field.Key.ToString("N"));
        var isRequired = build.Required.Contains(id);
        var isHidden = build.Hidden.Contains(id);
        var shownSettings = new ShownSettings(field.Settings, build.Settings(field.Key));
        var element = traits.HoldsValue ? LabelElementOf(traits.Type, shownSettings) : FieldLabelElement.None;
        var mark = MarkOf(build, field, traits.Type, element, isRequired);
        var (carried, shownWhile) = build.Hooks
            ? Carried(MarkOf(build, field, traits.Type, element, true), MarkOf(build, field, traits.Type, element, false))
            : (mark, null);
        build.MarksRequired |= mark == FieldMark.Required;
        build.WritesMarks |= carried != FieldMark.None;
        var label = traits.HoldsValue ? build.Text(field.Key, "label", field.Label) : null;
        (string Message, string Link)? error = build.Error(field.Key) is { } posted ? ErrorOf(build, field, traits.Type, shownSettings, posted, field.HideLabel ? null : label) : null;
        var answers = build.Answers.GetValueOrDefault(field.Key) ?? FieldAnswers.None;
        var (model, described) = await ControlAsync(build, field, traits, shownSettings, control, answers, cancellationToken);
        if (error is { } shown)
        {
            // After the control's, so a repeatable group's own error follows its rows' in the summary, as on the page.
            build.Errors.Add(new ErrorLinkModel(shown.Link, control));
        }

        var help = build.Text(field.Key, "help", field.Help);
        var describedBy = new List<string>();
        if (help is not null || build.Recalls(field.Key, "help"))
        {
            describedBy.Add(control + "-help");
        }

        describedBy.AddRange(described);
        // A refused group of choices names its error on each choice, as a fieldset's role takes no invalid state (6.8c).
        var choicesCarry = error is not null && element == FieldLabelElement.Legend
            && model is ChoicesControlModel { Empty: null } or GridControlModel { Rows.Count: > 0 };
        if (error is not null && !choicesCarry)
        {
            describedBy.Add(control + "-error");
        }

        return new FieldViewModel
        {
            Key = field.Key,
            Type = field.Type,
            Alias = field.Alias is null ? null : build.Name(field),
            Id = control,
            Name = build.Name(field),
            Classes = build.Root.Classes,
            CssClass = Classes("jfb-field", "jfb-field-" + field.Type, field.Span > 1 ? "jfb-span-" + field.Span.ToString(CultureInfo.InvariantCulture) : null, build.Render.Theme.Classes.GetValueOrDefault(FormBuilderThemeSlots.Field), field.CssClass),
            LabelElement = element,
            Label = label,
            HideLabel = field.HideLabel,
            Mark = carried,
            MarkText = MarkText(build, carried),
            MarkSpoken = carried == FieldMark.Required && SpeaksMark(traits.Type, shownSettings) ? build.Words.Required : null,
            IsMarkShown = mark != FieldMark.None,
            MarkShownWhile = shownWhile,
            Hooks = build.Hooks,
            Help = help,
            HelpId = control + "-help",
            Error = error?.Message,
            ErrorId = control + "-error",
            DescribedBy = describedBy.Count > 0 ? string.Join(' ', describedBy) : null,
            ChoiceErrorId = choicesCarry && model is ChoicesControlModel ? control + "-error" : null,
            IsRequired = isRequired,
            IsHidden = isHidden,
            IsDisabled = isHidden,
            Placeholder = build.Text(field.Key, "placeholder", field.Placeholder),
            Recalling = build.Recalling(field.Key),
            Autocomplete = field.Autocomplete ?? DefaultAutocomplete(field.Type),
            Values = answers.Values,
            FillsDefault = build.Starts && !build.Render.Prefill.Values.ContainsKey(field.Key)
                && ((field.DefaultValue is { } defaults && Untagged(defaults.Values) is null) || field.Prefill?.Query is not null),
            ControlView = await build.ControlViewAsync(views, field.Type, cancellationToken),
            Control = model,
        };
    }

    // What a type adds to its control, and the ids of its parts that the control names in aria-describedby.
    private async Task<(object? Model, IReadOnlyList<string> DescribedBy)> ControlAsync(
        Build build, PublicField field, FieldTypeTraits traits, ShownSettings settings, string id, FieldAnswers answers, CancellationToken cancellationToken)
    {
        if (await ChoiceControlAsync(build, field, traits.Type, settings, id, answers, cancellationToken) is { } choice)
        {
            return choice;
        }

        if (InputControl(build, field, traits.Type, settings, id, answers) is { } input)
        {
            return input;
        }

        switch (traits.Type)
        {
            case TextFieldType:
            case EmailFieldType:
            case PhoneFieldType:
            case UrlFieldType:
                {
                    var text = traits.Type is TextFieldType;
                    var email = traits.Type is EmailFieldType;
                    var prefix = text ? Affix(settings.Text("prefix"), id + "-prefix") : null;
                    var suffix = text ? Affix(settings.Text("suffix"), id + "-suffix") : null;
                    var counter = text && settings.Bool("counter", false) ? Counter(build, id, settings.Int("maxLength", new TextFieldSettings().MaxLength)) : null;
                    var model = new TextControlModel
                    {
                        InputType = traits.Type switch
                        {
                            EmailFieldType => "email",
                            PhoneFieldType => "tel",
                            UrlFieldType => "url",
                            _ => "text",
                        },
                        Spellcheck = email || traits.Type is UrlFieldType ? false : null,
                        Prefix = prefix,
                        Suffix = suffix,
                        Counter = counter,
                        Confirm = email && settings.Bool("confirm", false)
                            ? new ConfirmInputModel(
                                settings.Text("confirmLabel") ?? build.Words.ConfirmEmail,
                                id + "-" + EmailFieldType.ConfirmPart,
                                build.Name(field) + "." + EmailFieldType.ConfirmPart,
                                answers.Part(EmailFieldType.ConfirmPart) is [var again, ..] ? again : string.Empty)
                            : null,
                    };
                    return (model, Ids(prefix, suffix, counter));
                }

            case TextareaFieldType:
                {
                    var defaults = new TextareaFieldSettings();
                    var counter = settings.Bool("counter", defaults.Counter) ? Counter(build, id, settings.Int("maxLength", defaults.MaxLength)) : null;
                    return (new TextAreaControlModel { Rows = Math.Clamp(settings.Int("rows", defaults.Rows), 1, 50), AutoGrow = settings.Bool("autoGrow", defaults.AutoGrow), Counter = counter }, Ids(null, null, counter));
                }

            case NumberFieldType:
                {
                    var places = settings.Int("decimals", 0) > 0;
                    var min = settings.Text("min");
                    var negative = min is null || ExactDecimal.CompareText(min, "0") is not >= 0;
                    var prefix = Affix(settings.Text("prefix"), id + "-prefix");
                    var suffix = Affix(settings.Text("suffix"), id + "-suffix");
                    return (new NumberControlModel
                    {
                        InputType = places ? "text" : "number",
                        InputMode = negative ? null : places ? "decimal" : "numeric",
                        Min = places ? null : min,
                        Max = places ? null : settings.Text("max"),
                        Step = places ? null : settings.Text("step"),
                        Prefix = prefix,
                        Suffix = suffix,
                    }, Ids(prefix, suffix, null));
                }

            case HeadingFieldType:
                return (new HeadingControlModel(settings.Text("text") ?? string.Empty, Math.Clamp(settings.Int("level", new HeadingFieldSettings().Level), 2, 4)), []);

            case ParagraphFieldType:
                {
                    var html = settings.Text("html") ?? string.Empty;
                    return (new TextBlockControlModel(new HtmlString(html)) { IsEmpty = html.Length == 0 }, []);
                }

            case ImageFieldType:
                return (await ImageAsync(settings), []);

            case RepeaterFieldType repeater:
                return (await RowsAsync(build, field, repeater, settings, id, cancellationToken), []);

            default:
                // A site's own type; a built-in one here, such as a divider, adds nothing.
                return (BuiltInFieldTypes.All.Contains(traits.Type.GetType()) ? null : Custom(settings, answers), []);
        }
    }

    private async Task<ImageControlModel> ImageAsync(ShownSettings settings)
    {
        var size = settings.Text("size") is "small" or "medium" or "large" or "full" ? settings.Text("size")! : "medium";
        var alt = settings.Text("alt") ?? string.Empty;
        var (url, width, height) = Guid.TryParse(settings.Text("media"), out var key) ? await MediaAsync(key) : default;
        return new ImageControlModel { Url = url, Alt = alt, Size = size, Width = width, Height = height };
    }

    // A media item's address and size: no address when the item is gone or has none, no size when it gives none.
    private async Task<(string? Url, int? Width, int? Height)> MediaAsync(Guid key)
    {
        if (await media.GetByIdAsync(key) is not { } item)
        {
            return default;
        }

        var url = urls.GetMediaUrl(item);
        return (
            string.IsNullOrEmpty(url) || url == "#" ? null : url,
            Pixels(item.GetProperty("umbracoWidth")?.GetValue()),
            Pixels(item.GetProperty("umbracoHeight")?.GetValue()));
    }

    // Umbraco's image media type holds its width and height as integer labels; none, or zero, is no size.
    private static int? Pixels(object? value) => value is int pixels && pixels > 0 ? pixels : null;

    private static ProgressViewModel? Progress(PublicFormSettings settings, IReadOnlyList<PublicPage> allPages, List<PublicPage> shownPages, PublicPage? current, Build build)
    {
        var (root, words, culture) = (build.Root, build.Words, build.Culture);
        if (allPages.Count < 2 || current is null || settings.Progress == ProgressIndicator.None)
        {
            return null;
        }

        var at = shownPages.IndexOf(current);
        return new ProgressViewModel
        {
            Classes = build.Root.Classes,
            Style = settings.Progress == ProgressIndicator.Bar ? ProgressStyle.Bar : ProgressStyle.Steps,
            IsHidden = shownPages.Count < 2,
            Current = at + 1,
            Total = shownPages.Count,
            Text = string.Format(culture, words.Step, at + 1, shownPages.Count),
            Words = words.Step,
            UntitledWords = words.Page,
            TextId = root.Id + "-progress",
            Label = words.Progress,
            Steps =
            [
                // A page the rules hide has its step too, in a template, for the runtime to show once a rule shows the page (ADR-0155).
                .. allPages.Select(page =>
                {
                    var index = shownPages.IndexOf(page);
                    var untitled = string.Format(culture, words.Page, Math.Max(index, 0) + 1);
                    var title = build.Text(page.Key, "title", page.Title);
                    return new ProgressStepModel(title ?? untitled, index == at, index >= 0 && index < at)
                    {
                        Page = page.Key,
                        IsHidden = index < 0,
                        IsUntitled = title is null,
                        RecallsTitle = build.Recalls(page.Key, "title"),
                        Fallback = untitled,
                    };
                }),
            ],
        };
    }

    // Back unless the current page is the first the rules show, Next unless it is the last, Send on the last; a form
    // whose rules show no page shows Send.
    private static NavigationViewModel Navigation(PublicFormSettings settings, List<PublicPage> shownPages, PublicPage? current, RenderingWords words, ThemeClassesModel classes)
    {
        var at = current is null ? -1 : shownPages.IndexOf(current);
        var last = at == shownPages.Count - 1 || at < 0;
        return new NavigationViewModel
        {
            Classes = classes,
            Back = at > 0 ? new NavigationButtonModel(Text(settings.BackLabel) ?? words.Back, FormPostFields.Back) : null,
            Next = last ? null : new NavigationButtonModel(Text(settings.NextLabel) ?? words.Next, FormPostFields.Next),
            Submit = last ? new NavigationButtonModel(Text(settings.SubmitLabel) ?? words.Send, FormPostFields.Submit) : null,
        };
    }

    // Every button of a form's pages, of which the runtime shows those of the page it moves to (ADR-0155).
    private static NavigationViewModel Moves(PublicFormSettings settings, RenderingWords words, ThemeClassesModel classes) => new()
    {
        Classes = classes,
        Back = new NavigationButtonModel(Text(settings.BackLabel) ?? words.Back, FormPostFields.Back),
        Next = new NavigationButtonModel(Text(settings.NextLabel) ?? words.Next, FormPostFields.Next),
        Submit = new NavigationButtonModel(Text(settings.SubmitLabel) ?? words.Send, FormPostFields.Submit),
    };

    // The context values the page gives that the form lists, under the name the form lists them by (an attribute's name
    // is read in any letter case), each up to its limit; a name the form does not list, and a value too long, are
    // logged once and left out. A value of null is none.
    private List<HiddenInputModel> Context(FormRender render)
    {
        var listed = render.Form.Published.Definition.Settings.Developer.ContextValues;
        var inputs = new List<HiddenInputModel>();
        foreach (var (given, value) in render.Context)
        {
            if (value is null)
            {
                continue;
            }

            var name = listed.FirstOrDefault(candidate => string.Equals(candidate, given, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                if (_contextLogged.TryAdd(render.Form.Published.Form.Key.ToString("N") + ":" + given, true))
                {
                    LogContextNotListed(logger, given, render.Form.Published.Form.Alias);
                }

                continue;
            }

            if (value.Length > MaxContextValueLength)
            {
                if (_contextLogged.TryAdd(render.Form.Published.Form.Key.ToString("N") + ":" + name + ":long", true))
                {
                    LogContextTooLong(logger, name, render.Form.Published.Form.Alias, MaxContextValueLength);
                }

                continue;
            }

            inputs.Add(new HiddenInputModel(ContextPrefix + name, value));
        }

        return inputs;
    }

    // Whether a field of the form, a child field among them, keeps a file the visitor gives.
    private bool HoldsFile(FormDefinition definition) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field))
            .Any(field => types.Find(field.Type)?.Storage == FieldStorageKind.File);

    private static RequiredMarker MarkerOf(RequiredMarker marker) => marker == RequiredMarker.Inherit ? RequiredMarker.Optional : marker;

    // Whether a page, a group or a field, a repeatable group's child included, has a rule; the projection holds only
    // the switched-on ones. Validation rules change no element, so they need no hooks.
    private static bool HasRules(PublicForm form) =>
        form.Pages.Any(page => page.Rules.Count > 0 || page.Groups.Any(group => group.Rules.Count > 0 || group.Fields.Any(HasRules)));

    private static bool HasRules(PublicField field) => field.Rules.Count > 0 || field.Fields.Any(HasRules);

    private static string? DefaultAutocomplete(string type) => type switch
    {
        "email" => "email",
        "phone" => "tel",
        "url" => "url",
        "country" => "country",
        _ => null,
    };

    private static AffixModel? Affix(string? text, string id) => string.IsNullOrEmpty(text) ? null : new AffixModel(text, id);

    private static CounterModel Counter(Build build, string id, int limit) =>
        new(string.Format(build.Culture, build.Words.Counter, limit.ToString("N0", build.Culture)), id + "-count", limit, build.Words.CounterLeft, build.Words.CounterLeftOne, build.Words.CounterOver, build.Words.CounterOverOne);

    private static IReadOnlyList<string> Ids(AffixModel? prefix, AffixModel? suffix, CounterModel? counter) =>
        [.. new[] { prefix?.Id, suffix?.Id, counter?.Id }.OfType<string>()];

    private static string Id(Guid key) => key.ToString("D");

    private static string? Text(LocalizedText? text) => text?.Default is { Length: > 0 } shown ? shown : null;

    private static HtmlString? Html(LocalizedHtml? html) => html?.Default is { Length: > 0 } shown ? new HtmlString(shown) : null;

    private static HtmlString Paragraph(string text) => new("<p>" + WebUtility.HtmlEncode(text) + "</p>");

    private static string Classes(params string?[] classes) => string.Join(' ', classes.Where(name => !string.IsNullOrWhiteSpace(name)));

    // A text of the package's words with markup where it says {0}; the rest encoded.
    private static HtmlString WithMarkup(string template, string markup)
    {
        var at = template.IndexOf("{0}", StringComparison.Ordinal);
        return new HtmlString(WebUtility.HtmlEncode(template[..at]) + markup + WebUtility.HtmlEncode(template[(at + 3)..]));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "A page gives the context value '{Name}', which the form '{Form}' does not list (settings.developer.contextValues); it is left out.")]
    private static partial void LogContextNotListed(ILogger logger, string name, string form);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "A page gives the context value '{Name}' of the form '{Form}' longer than {Limit} characters; it is left out.")]
    private static partial void LogContextTooLong(ILogger logger, string name, string form, int limit);

    // The parts every model of the form shares.
    private sealed record Root(
        Guid Key,
        string Alias,
        int Revision,
        string Id,
        string Culture,
        string Direction,
        string CssClass,
        FormDisplay Display,
        string Action,
        string? EncodingType,
        string ApiBase,
        IHtmlContent ModelData,
        FormViewPaths Views,
        ThemeClassesModel Classes)
    {
        // The form's address opening at another element of the page than the form (ADR-0101).
        public string At(string id) => Action[..(Action.LastIndexOf('#') + 1)] + id;

        public FormViewModel Model(
            IReadOnlyList<PageViewModel>? pages = null,
            NavigationButtonModel? defaultButton = null,
            ProgressViewModel? progress = null,
            ErrorSummaryViewModel? errors = null,
            NavigationViewModel? navigation = null,
            IHtmlContent? requiredNote = null,
            bool isRequiredNoteHidden = false,
            bool hooks = false,
            NoticeViewModel? notice = null,
            OutcomeViewModel? outcome = null,
            IReadOnlyList<HiddenInputModel>? context = null,
            IReadOnlyList<HiddenInputModel>? postValues = null,
            HoneypotModel? honeypot = null,
            Guid? contentKey = null,
            string? placement = null,
            FormSendingModel? sending = null) => new()
            {
                Key = Key,
                Alias = Alias,
                Revision = Revision,
                Id = Id,
                Culture = Culture,
                Direction = Direction,
                CssClass = CssClass,
                Display = Display,
                Action = Action,
                EncodingType = EncodingType,
                ApiBase = ApiBase,
                ModelData = ModelData,
                Views = Views,
                Classes = Classes,
                Pages = pages ?? [],
                DefaultButton = defaultButton,
                Progress = progress,
                ErrorSummary = errors,
                ErrorSummaryTemplate = Display == FormDisplay.Form
                    ? new ErrorSummaryViewModel { Classes = Classes, Heading = string.Empty, HeadingId = Id + "-summary", Errors = [new ErrorLinkModel(string.Empty, string.Empty)] }
                    : null,
                Navigation = navigation,
                RequiredNote = requiredNote,
                IsRequiredNoteHidden = requiredNote is not null && isRequiredNoteHidden,
                Hooks = hooks,
                Notice = notice,
                Outcome = outcome,
                ContextValues = context ?? [],
                PostValues = postValues ?? [],
                Honeypot = honeypot,
                ContentKey = contentKey,
                Placement = placement,
                Sending = sending,
            };
    }

    // What the controls of a page read besides its answers: the site's today, the calculations' values as the rules work
    // them out, and the culture's formats.
    private sealed record PageFacts(DateOnly Today, IReadOnlyDictionary<string, string?> Calculations, IReadOnlyDictionary<string, IReadOnlyList<RowState>> Rows, CultureFormats Formats);

    // The page's answers: each field's outside repeatable groups, and each group's rows of its child fields' answers.
    private sealed record PageAnswers(Dictionary<Guid, FieldAnswers> Fields, Dictionary<Guid, List<Dictionary<Guid, FieldAnswers>>> Rows)
    {
        // Each repeatable group's row as a first render starts it, which the runtime adds in place (ADR-0154).
        public Dictionary<Guid, Dictionary<Guid, FieldAnswers>> StartedRows { get; } = [];
    }

    // The page's answers as their types read them: each field's, and each repeatable group's rows within its limits.
    private sealed record PageRead(Dictionary<Guid, ImmutableArray<FieldValue>> Fields, Dictionary<Guid, List<Dictionary<Guid, ImmutableArray<FieldValue>>>> Rows)
    {
        // The fields whose answers their types could not read or found over their limits, and so in each group's rows.
        public HashSet<Guid> Unreadable { get; } = [];

        public Dictionary<Guid, List<HashSet<Guid>>> UnreadableRows { get; } = [];
    }

    // Where a repeatable group's row stands: the group's alias, the row from 0, its legend, and the errors, the rankings'
    // orders and the kept files a post left in it.
    private sealed record RowPlace(string Alias, int Row, string Legend, IReadOnlyDictionary<Guid, FormPostError> Errors, IReadOnlyDictionary<Guid, IReadOnlyList<string>> Orders, IReadOnlyDictionary<Guid, IReadOnlyList<FormPostFile>> Files, bool Starts);

    // What building one form's fields shares, and what it collects.
    private sealed class Build(
        FormRender render,
        Root root,
        FormPostState? post,
        RenderingWords words,
        CultureInfo culture,
        PageAnswers answers,
        HashSet<string> hidden,
        HashSet<string> required,
        RequiredMarker marker,
        Dictionary<(Guid Owner, string Name), string> recalled,
        ChoiceLayout layout,
        PageFacts facts,
        bool hooks)
    {
        // The prefix of a visitor text that is a field's setting, such as settings.text.
        private const string SettingPrefix = "settings.";

        private static readonly IReadOnlyDictionary<Guid, FormPostError> NoErrors = FrozenDictionary<Guid, FormPostError>.Empty;

        private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<string>> NoOrders = FrozenDictionary<Guid, IReadOnlyList<string>>.Empty;

        private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<FormPostFile>> NoFiles = FrozenDictionary<Guid, IReadOnlyList<FormPostFile>>.Empty;

        private readonly Dictionary<string, string> _controls = new(StringComparer.Ordinal);

        private readonly List<ErrorLinkModel> _errors = [];

        private Dictionary<Guid, FormField>? _fields;

        private Dictionary<Guid, IReadOnlySet<string>>? _recalling;

        private bool _marksRequired;

        private bool _writesMarks;

        public FormRender Render => render;

        public Root Root => root;

        public FormPostState? Post => post;

        public RenderingWords Words => words;

        public CultureInfo Culture => culture;

        // Each field's answers where the build stands: the page's, and in a row the row's over them.
        public Dictionary<Guid, FieldAnswers> Answers => answers.Fields;

        // Each repeatable group's rows of answers.
        public Dictionary<Guid, List<Dictionary<Guid, FieldAnswers>>> RowAnswers => answers.Rows;

        // The page's build a row's build stands in; null for the page's.
        public Build? Outer { get; init; }

        // The row a row's build stands in; null for the page's.
        public RowPlace? Place { get; init; }

        // Whether the controls where the build stands start as a first render starts them (RowsOf): on a first render,
        // and in a row the visitor just added or the group's least added; after a post, its answers stand.
        public bool Starts => Place?.Starts ?? post is null;

        // The start of the ids of the fields where the build stands: the instance's, or the row's below it (ADR-0098).
        public string Prefix => Place is { } place ? RowPrefix(place.Alias, place.Row) : root.Id;

        // The start of the ids in a repeatable group's row, which is the row's own id.
        public string RowPrefix(string alias, int row) => root.Id + "-" + alias + "-" + row.ToString(CultureInfo.InvariantCulture);

        public HashSet<string> Hidden => hidden;

        public HashSet<string> Required => required;

        public RequiredMarker Marker => marker;

        // How the form lays out choices whose field says inherit.
        public ChoiceLayout Layout => layout;

        // The site's today, from which a date's limits relative to today count.
        public DateOnly Today => facts.Today;

        // Each shown calculation's value by its id, as the rules work it out; null without one.
        public IReadOnlyDictionary<string, string?> Calculations => facts.Calculations;

        public CultureFormats Formats => facts.Formats;

        // The error summary's links, which a row's fields add to the page's.
        public List<ErrorLinkModel> Errors => Outer?.Errors ?? _errors;

        // Whether an asterisk shows now, so the form shows the note that explains it.
        public bool MarksRequired
        {
            get => Outer?.MarksRequired ?? _marksRequired;
            set
            {
                if (Outer is { } outer)
                {
                    outer.MarksRequired = value;
                }
                else
                {
                    _marksRequired = value;
                }
            }
        }

        // Whether the views write the hooks the runtime applies the rules by (ADR-0111): the form has a switched-on rule
        // on a page, a group or a field, so its marks are written in both states and its controls say what follows them.
        public bool Hooks => hooks;

        // Whether a label carries a mark, shown or not, so a form with rules writes the note it may need.
        public bool WritesMarks
        {
            get => Outer?.WritesMarks ?? _writesMarks;
            set
            {
                if (Outer is { } outer)
                {
                    outer.WritesMarks = value;
                }
                else
                {
                    _writesMarks = value;
                }
            }
        }

        // A field's name where the build stands: its alias, or in a row its path (guests[0].name); none without an alias.
        public string Name(PublicField field) => field.Alias is not { } alias
            ? string.Empty
            : Place is { } place ? place.Alias + "[" + place.Row.ToString(CultureInfo.InvariantCulture) + "]." + alias : alias;

        // The error a post left for a field where the build stands; null for none.
        public FormPostError? Error(Guid key) => (Place?.Errors ?? post?.Errors)?.GetValueOrDefault(key);

        // A ranking's order as a post left it where the build stands (ADR-0101); none on a first render.
        public IReadOnlyList<string> Order(Guid key) => (Place?.Orders ?? post?.Orders)?.GetValueOrDefault(key) ?? [];

        // The stored files a post kept for a field where the build stands (ADR-0102); none on a first render.
        public IReadOnlyList<FormPostFile> Files(Guid key) => (Place?.Files ?? post?.Files)?.GetValueOrDefault(key) ?? [];

        // The columns of the group a field is in, which a repeatable group's rows take (ADR-0034).
        public int ColumnsOf(Guid key) => Math.Clamp(
            render.Shown.Form.Pages.SelectMany(page => page.Groups).First(group => group.Fields.Any(field => field.Key == key)).Columns, 1, 4);

        // The build of a repeatable group's row (ADR-0098): the row's answers over the page's, its state over the form's
        // (a hidden group hides every row's fields), its calculations' values, its errors, its ids and names, and its legend.
        public Build Row(PublicField group, int row, string legend)
        {
            var inRow = new Dictionary<Guid, FieldAnswers>(Answers);
            var rows = RowAnswers[group.Key];
            foreach (var (key, given) in row < rows.Count ? rows[row] : answers.StartedRows[group.Key])
            {
                inRow[key] = given;
            }

            var state = facts.Rows.GetValueOrDefault(Id(group.Key)) is { } states && row < states.Count ? states[row] : null;
            var rowHidden = new HashSet<string>(hidden, StringComparer.Ordinal);
            rowHidden.UnionWith(state?.Hidden ?? []);
            if (hidden.Contains(Id(group.Key)))
            {
                rowHidden.UnionWith(group.Fields.Select(child => Id(child.Key)));
            }

            var rowRequired = new HashSet<string>(required, StringComparer.Ordinal);
            rowRequired.UnionWith(state?.Required ?? []);
            if (state is null)
            {
                // A row yet to come has no state; what the definition requires it requires, and the rules decide the rest once it is there.
                rowRequired.UnionWith(group.Fields.Where(child => child.Required).Select(child => Id(child.Key)));
            }

            var calculations = new Dictionary<string, string?>(facts.Calculations, StringComparer.Ordinal);
            foreach (var (id, value) in state?.Calculations ?? new Dictionary<string, string?>())
            {
                calculations[id] = value;
            }

            var left = post?.Rows.GetValueOrDefault(group.Key) is { } posted && row < posted.Count ? posted[row] : null;
            return new Build(render, root, post, words, culture, answers with { Fields = inRow }, rowHidden, rowRequired, marker, recalled, layout, facts with { Calculations = calculations }, hooks)
            {
                Outer = this,
                Place = new RowPlace(group.Alias ?? string.Empty, row, legend, left?.Errors ?? NoErrors, left?.Orders ?? NoOrders, left?.Files ?? NoFiles, post is null || left is null || left.IsNew),
            };
        }

        // A visitor text as the page shows it: rendered for the page's answers when it holds tags, otherwise as the form
        // endpoint shows it; an empty text is none.
        public string? Text(Guid owner, string name, LocalizedText? text) =>
            recalled.TryGetValue((owner, name), out var shown) ? (shown.Length > 0 ? shown : null) : FormModels.Text(text);

        public HtmlString? Html(Guid owner, string name, LocalizedHtml? html) =>
            recalled.TryGetValue((owner, name), out var shown) ? (shown.Length > 0 ? new HtmlString(shown) : null) : FormModels.Html(html);

        // Whether an element's visitor text holds tags, so its view writes it with its hook however it renders
        // (ADR-0115).
        public bool Recalls(Guid owner, string name) => recalled.ContainsKey((owner, name));

        // The names of an element's visitor texts that hold tags.
        public IReadOnlySet<string> Recalling(Guid owner)
        {
            _recalling ??= recalled.Keys
                .GroupBy(key => key.Owner)
                .ToDictionary(group => group.Key, group => (IReadOnlySet<string>)group.Select(key => key.Name).ToFrozenSet(StringComparer.Ordinal));
            return _recalling.GetValueOrDefault(owner) ?? FrozenSet<string>.Empty;
        }

        // A field's label in a box's own label, with its hook when it holds tags (ADR-0115).
        public IHtmlContent Label(PublicField field) =>
            FormBuilderHtml.LabelText(field.Key, Text(field.Key, "label", field.Label), Recalls(field.Key, "label"));

        // A field's settings that are visitor texts holding tags, rendered for the page's answers, by the setting's name.
        public Dictionary<string, string> Settings(Guid owner) =>
            recalled.Where(pair => pair.Key.Owner == owner && pair.Key.Name.StartsWith(SettingPrefix, StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key.Name[SettingPrefix.Length..], pair => pair.Value, StringComparer.Ordinal);

        // A field's answers as rules see them: none for a field a rule hides (FormRules).
        public ImmutableArray<string> ShownAnswers(Guid key) =>
            hidden.Contains(Id(key)) ? [] : Answers.TryGetValue(key, out var given) ? given.Values : [];

        // A field of the published version by its key, a repeatable group's child among them; the page shows that
        // version's fields, so each one it asks for is there.
        public FormField Definition(Guid key)
        {
            if (Outer is { } outer)
            {
                return outer.Definition(key);
            }

            _fields ??= render.Form.Published.Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
                .SelectMany(field => field.Fields.Prepend(field))
                .GroupBy(field => field.Key)
                .ToDictionary(group => group.Key, group => group.First());
            return _fields[key];
        }

        public async Task<string> ControlViewAsync(FormViews views, string type, CancellationToken cancellationToken)
        {
            if (Outer is { } outer)
            {
                return await outer.ControlViewAsync(views, type, cancellationToken);
            }

            if (!_controls.TryGetValue(type, out var path))
            {
                path = await views.ControlAsync(render.Theme.ViewFolder, render.Form.Published.Form.Alias, type, cancellationToken);
                _controls[type] = path;
            }

            return path;
        }
    }

    // The public settings as the form endpoint shows them: each text as the string the culture shows (a number's limits
    // are exact decimals in text), a text that holds tags as rendered for the page's answers, everything else as the
    // projection holds it; a setting left out takes its type's default.
    private readonly struct ShownSettings(JsonElement? settings, IReadOnlyDictionary<string, string> recalled)
    {
        public ShownSettings(SettingsObject? settings, IReadOnlyDictionary<string, string> recalled)
            : this(settings?.Value, recalled)
        {
        }

        public string? Text(string name) =>
            recalled.TryGetValue(name, out var shown) ? shown : Find(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

        public int Int(string name, int fallback) => Find(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : fallback;

        public bool Bool(string name, bool fallback) => Find(name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : fallback;

        // The texts of a list setting, such as a country's allowed codes; none for a setting that is no list.
        public IReadOnlyList<string> Texts(string name) =>
            Find(name) is { ValueKind: JsonValueKind.Array } value
                ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
                : [];

        // The settings of an object setting, such as a grid's scale; null for a setting that is no object.
        public ShownSettings? Child(string name) =>
            Find(name) is { ValueKind: JsonValueKind.Object } value ? new ShownSettings(value, FrozenDictionary<string, string>.Empty) : null;

        // The settings as JSON, each text that holds tags as rendered for the page's answers at its path (format.prefix);
        // null for none. A text with tags is a setting the field has.
        public JsonElement? Json()
        {
            if (recalled.Count == 0)
            {
                return settings;
            }

            var json = JsonNode.Parse(settings!.Value.GetRawText())!.AsObject();
            foreach (var (path, text) in recalled)
            {
                var names = path.Split('.');
                var owner = names[..^1].Aggregate(json, (node, name) => node[name]!.AsObject());
                owner[names[^1]] = text;
            }

            return JsonSerializer.SerializeToElement(json);
        }

        private JsonElement? Find(string name) =>
            settings is { ValueKind: JsonValueKind.Object } given && given.TryGetProperty(name, out var value) ? value : null;
    }
}
