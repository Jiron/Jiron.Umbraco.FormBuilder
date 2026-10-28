using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>A form to render: the published form, as the form endpoint shows it, where and in which culture.</summary>
/// <param name="HttpContext">The request.</param>
/// <param name="Form">The published form.</param>
/// <param name="Shown">The form as the form endpoint shows it for the culture and the page.</param>
/// <param name="Culture">The culture, one of the site's languages.</param>
/// <param name="Instance">The instance id.</param>
/// <param name="Context">The context values the page gives, by name; empty for none.</param>
internal sealed record FormRender(
    HttpContext HttpContext,
    CachedForm Form,
    FormShown Shown,
    string Culture,
    string Instance,
    IReadOnlyDictionary<string, string?> Context);

/// <summary>
/// The view models of a form (ADR-0015, ADR-0093): its texts as the form endpoint shows them in the culture, its ids
/// below its instance, and every page, group and field in the state its rules give for the default values that hold no
/// merge tag (or a post's answers), with the site's clock and time zone, no member and no query value, so the markup is
/// the same for every visitor. A hidden field renders nothing, nor does a field whose type the site lacks.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="views">Which view writes each part.</param>
/// <param name="paths">The public API's paths.</param>
/// <param name="media">The published media, which an image field shows.</param>
/// <param name="urls">The addresses of media.</param>
/// <param name="time">The site's clock.</param>
/// <param name="logger">Where context values that are not taken are logged.</param>
internal sealed partial class FormModels(
    FieldTypeRegistry types,
    FormViews views,
    FormBuilderPublicApiPaths paths,
    IPublishedMediaCache media,
    IPublishedUrlProvider urls,
    TimeProvider time,
    ILogger<FormModels> logger)
{
    /// <summary>The longest context value a page may give (ADR-0088).</summary>
    public const int MaxContextValueLength = 2_000;

    /// <summary>The form's <c>enctype</c> when it holds a file field.</summary>
    public const string Multipart = "multipart/form-data";

    /// <summary>The prefix of the hidden inputs that carry context values.</summary>
    public const string ContextPrefix = "jfb-context-";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoQuery = FrozenDictionary<string, IReadOnlyList<string>>.Empty;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>> NoRows =
        FrozenDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>>.Empty;

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
        var (formView, parts) = await views.PartsAsync(form.Form.Alias, cancellationToken);
        var post = FormPosts.Get(render.HttpContext, form.Form.Key, render.Instance);
        var words = RenderingWords.Of(render.Culture);
        var culture = CultureInfo.GetCultureInfo(render.Culture);
        var request = render.HttpContext.Request;
        var path = (request.PathBase + request.Path).ToUriComponent();
        var display = post?.Outcome is not null ? FormDisplay.Outcome : render.Shown.Unavailable is not null ? FormDisplay.Notice : FormDisplay.Form;
        var root = new Root(
            form.Form.Key,
            form.Form.Alias,
            form.Form.PublicRevision,
            render.Instance,
            render.Culture,
            culture.TextInfo.IsRightToLeft ? "rtl" : "ltr",
            Classes("jfb-form", "jfb-theme-" + FormViews.DefaultFolder, render.Shown.Form.Settings.CssClass),
            display,
            (path.Length == 0 ? "/" : path) + "#" + render.Instance,
            HoldsFile(form.Definition) ? Multipart : null,
            (request.PathBase + new PathString(paths.Root)).ToUriComponent(),
            ModelData.Of(render.Shown.Answer.Body),
            parts);
        var model = display switch
        {
            FormDisplay.Outcome => root.Model(outcome: Outcome(post!.Outcome!, words)),
            FormDisplay.Notice => root.Model(notice: Unavailable(render, words, culture)),
            _ => await FormAsync(render, root, post, words, culture, cancellationToken),
        };
        return (formView, model);
    }

    // The notice of a form that takes no entry now: the form's closed message when it is closed or full, otherwise the
    // package's heading and sentence; a form that opens later says when, in the site's time zone and the culture.
    private NoticeViewModel Unavailable(FormRender render, RenderingWords words, CultureInfo culture)
    {
        var availability = render.Form.Published.Definition.Settings.Availability;
        if (render.Shown.Unavailable == FormUnavailable.Closed && availability.OpensAt is { } opens && time.GetUtcNow().UtcDateTime < opens)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(opens, DateTimeKind.Utc), render.Shown.TimeZone);
            return new NoticeViewModel
            {
                Kind = NoticeKind.NotOpenYet,
                Heading = string.Format(culture, words.OpensHeading, local.ToString("f", culture)),
                Text = Paragraph(words.OpensText),
            };
        }

        var full = render.Shown.Unavailable == FormUnavailable.Full;
        var kind = full ? NoticeKind.Full : NoticeKind.Closed;
        return render.Shown.Form.Settings.ClosedMessage?.Default is { Length: > 0 } message
            ? new NoticeViewModel { Kind = kind, Text = new HtmlString(message) }
            : new NoticeViewModel { Kind = kind, Heading = full ? words.FullHeading : words.ClosedHeading, Text = Paragraph(full ? words.FullText : words.ClosedText) };
    }

    private static OutcomeViewModel Outcome(FormPostOutcome outcome, RenderingWords words) => new()
    {
        Message = new HtmlString(outcome.Html),
        Reference = outcome.Reference is { Length: > 0 } reference ? WithMarkup(words.Reference, "<strong>" + WebUtility.HtmlEncode(reference) + "</strong>") : null,
        IsPending = outcome.IsPending,
    };

    private async Task<FormViewModel> FormAsync(FormRender render, Root root, FormPostState? post, RenderingWords words, CultureInfo culture, CancellationToken cancellationToken)
    {
        var shown = render.Shown.Form;
        var settings = shown.Settings;
        var answers = Answers(shown, post);
        var now = time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var state = FormRules.Evaluate(
            DefinitionLogic.From(render.Form.Published.Definition, types),
            new FormAnswers(answers.ToDictionary(pair => Id(pair.Key), pair => (IReadOnlyList<string>)pair.Value.Values, StringComparer.Ordinal), NoRows),
            new EvaluationContext(now, render.Shown.TimeZone.Id, render.Culture, new Member(false, []), NoQuery, []));
        var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
        var required = state.Required.ToHashSet(StringComparer.Ordinal);
        var shownPages = shown.Pages.Where(page => !hidden.Contains(Id(page.Key))).ToList();
        var current = shownPages.FirstOrDefault(page => page.Key == post?.Page) ?? shownPages.FirstOrDefault();
        var build = new Build(render, root, post, words, culture, answers, hidden, required, MarkerOf(settings.RequiredMarker));
        var pages = new List<PageViewModel>();
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
                    if (await FieldAsync(build, field, cancellationToken) is { } built)
                    {
                        fields.Add(built);
                    }
                }

                groups.Add(new GroupViewModel
                {
                    Key = group.Key,
                    Title = Text(group.Title),
                    Description = Html(group.Description),
                    DescriptionId = root.Id + "-" + group.Key.ToString("N") + "-help",
                    CssClass = Classes("jfb-group", "jfb-columns-" + Math.Clamp(group.Columns, 1, 4).ToString(CultureInfo.InvariantCulture), group.Style == GroupStyle.Card ? "jfb-group-card" : null),
                    IsHidden = groupHidden,
                    Fields = fields,
                    Views = root.Views,
                });
            }

            pages.Add(new PageViewModel
            {
                Key = page.Key,
                Title = Text(page.Title),
                Description = Html(page.Description),
                IsCurrent = page == current,
                IsHidden = pageHidden,
                Groups = groups,
                Views = root.Views,
            });
        }

        return root.Model(
            pages: pages,
            progress: Progress(settings, shownPages, current, root, words, culture),
            errors: build.Errors.Count > 0 && settings.ValidationSummary
                ? new ErrorSummaryViewModel { Heading = words.ErrorSummary, HeadingId = root.Id + "-summary", Errors = build.Errors }
                : null,
            navigation: Navigation(settings, shownPages, current, words),
            requiredNote: build.MarksRequired
                ? WithMarkup(words.RequiredNote, "<span aria-hidden=\"true\">*</span><span class=\"jfb-visually-hidden\">" + WebUtility.HtmlEncode(words.Asterisk) + "</span>")
                : null,
            notice: post?.Notice is { } notice ? new NoticeViewModel { Kind = NoticeKind.Message, Heading = notice.Heading, Text = Paragraph(notice.Text) } : null,
            context: Context(render));
    }

    // Each field's answers, its child fields' among them: a post's, else its default value, when that holds no merge tag;
    // a default with tags is the browser's to fill (ADR-0014).
    private static Dictionary<Guid, FieldAnswers> Answers(PublicForm form, FormPostState? post)
    {
        var answers = new Dictionary<Guid, FieldAnswers>();
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
        {
            if (post is not null)
            {
                if (post.Answers.TryGetValue(field.Key, out var posted))
                {
                    answers[field.Key] = posted;
                }

                continue;
            }

            if (field.DefaultValue is { } defaults && Untagged(defaults.Values) is { Count: > 0 } texts)
            {
                answers[field.Key] = new FieldAnswers(texts);
            }
        }

        return answers;
    }

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

    private async Task<FieldViewModel?> FieldAsync(Build build, PublicField field, CancellationToken cancellationToken)
    {
        if (types.Find(field.Type) is not { } traits || traits.Type is HiddenFieldType)
        {
            return null;
        }

        var root = build.Root;
        var id = Id(field.Key);
        var control = root.Id + "-" + (field.Alias ?? field.Key.ToString("N"));
        var isRequired = build.Required.Contains(id);
        var isHidden = build.Hidden.Contains(id);
        var labelled = traits.HoldsValue;
        var mark = !labelled ? FieldMark.None : build.Marker switch
        {
            RequiredMarker.Optional when !isRequired => FieldMark.Optional,
            RequiredMarker.Required when isRequired => FieldMark.Required,
            _ => FieldMark.None,
        };
        build.MarksRequired |= mark == FieldMark.Required;
        var error = build.Post?.Errors.GetValueOrDefault(field.Key);
        if (error is not null)
        {
            build.Errors.Add(new ErrorLinkModel(error, control));
        }

        var answers = build.Answers.GetValueOrDefault(field.Key) ?? FieldAnswers.None;
        var settings = new ShownSettings(field.Settings);
        var (model, described) = await ControlAsync(build, field, traits, settings, control, answers);
        var help = Text(field.Help);
        var describedBy = new List<string>();
        if (help is not null)
        {
            describedBy.Add(control + "-help");
        }

        describedBy.AddRange(described);
        if (error is not null)
        {
            describedBy.Add(control + "-error");
        }

        return new FieldViewModel
        {
            Key = field.Key,
            Type = field.Type,
            Alias = field.Alias,
            Id = control,
            Name = field.Alias ?? string.Empty,
            CssClass = Classes("jfb-field", "jfb-field-" + field.Type, field.Span > 1 ? "jfb-span-" + field.Span.ToString(CultureInfo.InvariantCulture) : null, field.CssClass),
            LabelElement = labelled ? FieldLabelElement.Label : FieldLabelElement.None,
            Label = labelled ? Text(field.Label) : null,
            HideLabel = field.HideLabel,
            Mark = mark,
            MarkText = mark switch
            {
                FieldMark.Optional => build.Words.Optional,
                FieldMark.Required => "*",
                _ => string.Empty,
            },
            Help = help,
            HelpId = control + "-help",
            Error = error,
            ErrorId = control + "-error",
            DescribedBy = describedBy.Count > 0 ? string.Join(' ', describedBy) : null,
            IsRequired = isRequired,
            IsHidden = isHidden,
            IsDisabled = isHidden,
            Placeholder = Text(field.Placeholder),
            Autocomplete = field.Autocomplete ?? DefaultAutocomplete(field.Type),
            Values = answers.Values,
            ControlView = await build.ControlViewAsync(views, field.Type, cancellationToken),
            Control = model,
        };
    }

    // What a type adds to its control, and the ids of its parts that the control names in aria-describedby.
    private async Task<(object? Model, IReadOnlyList<string> DescribedBy)> ControlAsync(
        Build build, PublicField field, FieldTypeTraits traits, ShownSettings settings, string id, FieldAnswers answers)
    {
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
                                (field.Alias ?? string.Empty) + "." + EmailFieldType.ConfirmPart,
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
                return (new TextBlockControlModel(new HtmlString(settings.Text("html") ?? string.Empty)), []);

            case ImageFieldType:
                return (await ImageAsync(settings), []);

            default:
                return (null, []);
        }
    }

    private async Task<ImageControlModel> ImageAsync(ShownSettings settings)
    {
        var size = settings.Text("size") is "small" or "medium" or "large" or "full" ? settings.Text("size")! : "medium";
        var alt = settings.Text("alt") ?? string.Empty;
        if (!Guid.TryParse(settings.Text("media"), out var key) || await media.GetByIdAsync(key) is not { } item)
        {
            return new ImageControlModel { Alt = alt, Size = size };
        }

        var url = urls.GetMediaUrl(item);
        return new ImageControlModel
        {
            Url = string.IsNullOrEmpty(url) || url == "#" ? null : url,
            Alt = alt,
            Size = size,
            Width = Pixels(item.GetProperty("umbracoWidth")?.GetValue()),
            Height = Pixels(item.GetProperty("umbracoHeight")?.GetValue()),
        };
    }

    // Umbraco's image media type holds its width and height as integer labels; none, or zero, is no size.
    private static int? Pixels(object? value) => value is int pixels && pixels > 0 ? pixels : null;

    private static ProgressViewModel? Progress(PublicFormSettings settings, List<PublicPage> shownPages, PublicPage? current, Root root, RenderingWords words, CultureInfo culture)
    {
        if (shownPages.Count < 2 || current is null || settings.Progress == ProgressIndicator.None)
        {
            return null;
        }

        var at = shownPages.IndexOf(current);
        return new ProgressViewModel
        {
            Style = settings.Progress == ProgressIndicator.Bar ? ProgressStyle.Bar : ProgressStyle.Steps,
            Current = at + 1,
            Total = shownPages.Count,
            Text = string.Format(culture, words.Step, at + 1, shownPages.Count),
            TextId = root.Id + "-progress",
            Label = words.Progress,
            Steps = [.. shownPages.Select((page, index) => new ProgressStepModel(Text(page.Title) ?? string.Format(culture, words.Page, index + 1), index == at, index < at))],
        };
    }

    // Back unless the current page is the first the rules show, Next unless it is the last, Send on the last; a form
    // whose rules show no page shows Send.
    private static NavigationViewModel Navigation(PublicFormSettings settings, List<PublicPage> shownPages, PublicPage? current, RenderingWords words)
    {
        var at = current is null ? -1 : shownPages.IndexOf(current);
        var last = at == shownPages.Count - 1 || at < 0;
        return new NavigationViewModel
        {
            Back = at > 0 ? new NavigationButtonModel(Text(settings.BackLabel) ?? words.Back, "back") : null,
            Next = last ? null : new NavigationButtonModel(Text(settings.NextLabel) ?? words.Next, "next"),
            Submit = last ? new NavigationButtonModel(Text(settings.SubmitLabel) ?? words.Send, "submit") : null,
        };
    }

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

    private static string? DefaultAutocomplete(string type) => type switch
    {
        "email" => "email",
        "phone" => "tel",
        "url" => "url",
        _ => null,
    };

    private static AffixModel? Affix(string? text, string id) => string.IsNullOrEmpty(text) ? null : new AffixModel(text, id);

    private static CounterModel Counter(Build build, string id, int limit) =>
        new(string.Format(build.Culture, build.Words.Counter, limit), id + "-count", limit);

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
        FormViewPaths Views)
    {
        public FormViewModel Model(
            IReadOnlyList<PageViewModel>? pages = null,
            ProgressViewModel? progress = null,
            ErrorSummaryViewModel? errors = null,
            NavigationViewModel? navigation = null,
            IHtmlContent? requiredNote = null,
            NoticeViewModel? notice = null,
            OutcomeViewModel? outcome = null,
            IReadOnlyList<HiddenInputModel>? context = null) => new()
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
                Pages = pages ?? [],
                Progress = progress,
                ErrorSummary = errors,
                Navigation = navigation,
                RequiredNote = requiredNote,
                Notice = notice,
                Outcome = outcome,
                ContextValues = context ?? [],
            };
    }

    // What building one form's fields shares, and what it collects.
    private sealed class Build(
        FormRender render,
        Root root,
        FormPostState? post,
        RenderingWords words,
        CultureInfo culture,
        Dictionary<Guid, FieldAnswers> answers,
        HashSet<string> hidden,
        HashSet<string> required,
        RequiredMarker marker)
    {
        private readonly Dictionary<string, string> _controls = new(StringComparer.Ordinal);

        public Root Root => root;

        public FormPostState? Post => post;

        public RenderingWords Words => words;

        public CultureInfo Culture => culture;

        public Dictionary<Guid, FieldAnswers> Answers => answers;

        public HashSet<string> Hidden => hidden;

        public HashSet<string> Required => required;

        public RequiredMarker Marker => marker;

        public List<ErrorLinkModel> Errors { get; } = [];

        public bool MarksRequired { get; set; }

        public async Task<string> ControlViewAsync(FormViews views, string type, CancellationToken cancellationToken)
        {
            if (!_controls.TryGetValue(type, out var path))
            {
                path = await views.ControlAsync(render.Form.Published.Form.Alias, type, cancellationToken);
                _controls[type] = path;
            }

            return path;
        }
    }

    // The public settings as the form endpoint shows them: each text as the string the culture shows (a number's limits
    // are exact decimals in text), everything else as the projection holds it; a setting left out takes its type's
    // default.
    private readonly struct ShownSettings(SettingsObject? settings)
    {
        public string? Text(string name) => Find(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

        public int Int(string name, int fallback) => Find(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : fallback;

        public bool Bool(string name, bool fallback) => Find(name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : fallback;

        private JsonElement? Find(string name) =>
            settings is not null && settings.Value.ValueKind == JsonValueKind.Object && settings.Value.TryGetProperty(name, out var value) ? value : null;
    }
}
