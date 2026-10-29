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
/// <param name="Gate">Why a form for members shows its notice to the visitor; null when the form shows.</param>
internal sealed record FormRender(
    HttpContext HttpContext,
    CachedForm Form,
    FormShown Shown,
    string Culture,
    string Instance,
    IReadOnlyDictionary<string, string?> Context,
    MemberGate? Gate = null);

/// <summary>
/// The view models of a form (ADR-0015, ADR-0093): its texts as the form endpoint shows them in the culture, a visitor
/// text that holds tags rendered by the merge-tag service for the answers the page holds (ADR-0095), its ids below its
/// instance, and every page, group and field in the state its rules give for what its controls start with (or a post's
/// answers), read as the submission pipeline reads them, with the site's clock and time zone, no member and no query
/// value, so the markup is the same for every visitor but where a form for members does not admit them; a calculation
/// shows the value the rules work out (ADR-0097). A hidden field renders nothing, nor does a field whose type the site
/// lacks, a calculation visitors do not see, or the captcha's place on a form without a captcha. Options from a list or
/// another source, and options a parent field filters, are those the options endpoint answers for the page's answers
/// (ADR-0096).
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="mergeTags">The merge-tag service, which renders the visitor texts that hold tags.</param>
/// <param name="cascading">The options of a field for its parents' answers, as the options endpoint resolves them.</param>
/// <param name="views">Which view writes each part.</param>
/// <param name="paths">The public API's paths.</param>
/// <param name="media">The published media, which an image field shows.</param>
/// <param name="urls">The addresses of media.</param>
/// <param name="time">The site's clock.</param>
/// <param name="cultures">The culture formats a calculation's and a slider's numbers are written with.</param>
/// <param name="logger">Where context values that are not taken are logged.</param>
internal sealed partial class FormModels(
    FieldTypeRegistry types,
    IFormBuilderMergeTagService mergeTags,
    CascadingOptions cascading,
    FormViews views,
    FormBuilderPublicApiPaths paths,
    IPublishedMediaCache media,
    IPublishedUrlProvider urls,
    TimeProvider time,
    MergeTagCultures cultures,
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
        var display = post?.Outcome is not null ? FormDisplay.Outcome : render.Shown.Unavailable is not null || render.Gate is not null ? FormDisplay.Notice : FormDisplay.Form;
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
            FormDisplay.Notice => root.Model(notice: render.Shown.Unavailable is not null ? Unavailable(render, words, culture) : Gated(render.Gate!.Value, words)),
            _ => await FormAsync(render, root, post, words, culture, cancellationToken),
        };
        return (formView, model);
    }

    // The notice of a form for members to a visitor it does not admit, in the culture (ADR-0095); a form that takes no
    // entry now says so first, since signing in would not help.
    private static NoticeViewModel Gated(MemberGate gate, RenderingWords words) => gate == MemberGate.SignIn
        ? new NoticeViewModel { Kind = NoticeKind.SignIn, Heading = words.SignInHeading, Text = Paragraph(words.SignInText) }
        : new NoticeViewModel { Kind = NoticeKind.MemberGroups, Heading = words.GroupsHeading, Text = Paragraph(words.GroupsText) };

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
        var definition = render.Form.Published.Definition;
        var utc = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var now = utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var answers = Answers(shown, definition, post, TimeZoneInfo.ConvertTime(utc, render.Shown.TimeZone).DateTime);
        var read = Read(definition, answers);
        var state = FormRules.Evaluate(
            DefinitionLogic.From(definition, types),
            RuleAnswers(definition, read),
            new EvaluationContext(now, render.Shown.TimeZone.Id, render.Culture, new Member(false, []), NoQuery, []));
        var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
        var required = state.Required.ToHashSet(StringComparer.Ordinal);
        var shownPages = shown.Pages.Where(page => !hidden.Contains(Id(page.Key))).ToList();
        var current = shownPages.FirstOrDefault(page => page.Key == post?.Page) ?? shownPages.FirstOrDefault();
        var recalled = await RecalledAsync(render, read, hidden, cancellationToken);
        var facts = new PageFacts(DateOnly.FromDayNumber(CalendarValues.Today(now, render.Shown.TimeZone.Id)), state.Calculations, cultures.For(render.Culture));
        var build = new Build(render, root, post, words, culture, answers, hidden, required, MarkerOf(settings.RequiredMarker), recalled, settings.ChoiceLayout, facts);
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
                    Title = build.Text(group.Key, "title", group.Title),
                    Description = build.Html(group.Key, "description", group.Description),
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
                Title = build.Text(page.Key, "title", page.Title),
                Description = build.Html(page.Key, "description", page.Description),
                IsCurrent = page == current,
                IsHidden = pageHidden,
                Groups = groups,
                Views = root.Views,
            });
        }

        var navigation = Navigation(settings, shownPages, current, words);
        return root.Model(
            pages: pages,
            defaultButton: pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Any(field => field.Control is RankingControlModel)
                ? navigation.Next ?? navigation.Submit
                : null,
            progress: Progress(settings, shownPages, current, build),
            errors: build.Errors.Count > 0 && settings.ValidationSummary
                ? new ErrorSummaryViewModel { Heading = words.ErrorSummary, HeadingId = root.Id + "-summary", Errors = build.Errors }
                : null,
            navigation: navigation,
            requiredNote: build.MarksRequired
                ? WithMarkup(words.RequiredNote, "<span aria-hidden=\"true\">*</span><span class=\"jfb-visually-hidden\">" + WebUtility.HtmlEncode(words.Asterisk) + "</span>")
                : null,
            notice: post?.Notice is { } notice ? new NoticeViewModel { Kind = NoticeKind.Message, Heading = notice.Heading, Text = Paragraph(notice.Text) } : null,
            context: Context(render));
    }

    // The visitor texts that hold tags, each rendered as the server renders it in its place for the answers the page
    // holds (ADR-0068, ADR-0095), by its owner and name.
    private async Task<Dictionary<(Guid Owner, string Name), string>> RecalledAsync(FormRender render, Dictionary<Guid, ImmutableArray<FieldValue>> read, HashSet<string> hidden, CancellationToken cancellationToken)
    {
        var recalled = new Dictionary<(Guid Owner, string Name), string>();
        if (render.Shown.Tagged.Count == 0)
        {
            return recalled;
        }

        var scope = render.Shown.Scope with { Answers = RecallAnswers(render.Form.Published.Definition, read, hidden) };
        foreach (var text in render.Shown.Tagged)
        {
            var context = text.IsHtml ? MergeTagContext.Html : MergeTagContext.Text;
            recalled[(text.Owner, text.Name)] = (await mergeTags.RenderAsync(text.Text, MergeTagPlace.VisitorText, context, scope with { TextOwner = text.Owner }, cancellationToken)).Text;
        }

        return recalled;
    }

    // The page's answers as their fields' types read them (ADR-0058), which is how both runtimes recall them, and the
    // fields the rules hid.
    private static MergeTagAnswers RecallAnswers(FormDefinition definition, Dictionary<Guid, ImmutableArray<FieldValue>> read, HashSet<string> hidden) =>
        new([.. read.Select(pair => new MergeTagAnswer(pair.Key, pair.Value))], AllFields(definition).Where(field => hidden.Contains(Id(field.Key))).Select(field => field.Key));

    // Each answer as the submission pipeline reads it before its rules (SubmissionReader): cleaned, held to its type's
    // limits and read by its type. An answer past its limits or one its type cannot read is none. A hidden field's answer
    // shows nowhere on the page: publishing keeps rules and formulas off it, and a visitor text shows nothing of it
    // (ADR-0068).
    private Dictionary<Guid, ImmutableArray<FieldValue>> Read(FormDefinition definition, Dictionary<Guid, FieldAnswers> answers)
    {
        var read = new Dictionary<Guid, ImmutableArray<FieldValue>>();
        foreach (var field in AllFields(definition))
        {
            if (!answers.TryGetValue(field.Key, out var given) || types.FindType(field.Type) is not { } type)
            {
                continue;
            }

            var cleaned = SubmissionReader.Clean(new PostedAnswer(
                [.. given.Values],
                given.Parts.ToDictionary(part => part.Key, part => (IReadOnlyList<string?>)[.. part.Value], StringComparer.Ordinal)));
            if (FieldValues.CheckLimits(type, field, cleaned) is null && FieldValues.Read(type, field, cleaned) is { IsUnreadable: false, Values: { Length: > 0 } values })
            {
                read[field.Key] = values;
            }
        }

        return read;
    }

    // The answers as the rules see them, as the pipeline hands them over: each field's values by its id and a grid's by
    // its rows' ids. A repeatable group's rows join with its rows (6.2f).
    private static FormAnswers RuleAnswers(FormDefinition definition, Dictionary<Guid, ImmutableArray<FieldValue>> read)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            foreach (var answered in read.GetValueOrDefault(field.Key, []).GroupBy(value => value.Row))
            {
                values[Id(answered.Key ?? field.Key)] = [.. answered.Select(value => value.Text)];
            }
        }

        return new FormAnswers(values, NoRows);
    }

    // Each field's answers, its child fields' among them: a post's; else what its control starts with (ADR-0097), its
    // default value when that holds no merge tag (a default with tags is the browser's to fill, ADR-0014), or without a
    // default a date's today, a date and time's now and a slider's middle (Start).
    private Dictionary<Guid, FieldAnswers> Answers(PublicForm form, FormDefinition definition, FormPostState? post, DateTime local)
    {
        var defined = AllFields(definition).GroupBy(field => field.Key).ToDictionary(group => group.Key, group => group.First());
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

            var texts = field.DefaultValue is { } defaults ? Untagged(defaults.Values) : [];
            if (texts is { Count: > 0 })
            {
                answers[field.Key] = new FieldAnswers(texts);
            }
            else if (texts is not null && Start(defined[field.Key], local) is { } start)
            {
                answers[field.Key] = new FieldAnswers([start]);
            }
        }

        return answers;
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

    private async Task<FieldViewModel?> FieldAsync(Build build, PublicField field, CancellationToken cancellationToken)
    {
        if (types.Find(field.Type) is not { } traits
            || traits.Type is HiddenFieldType
            || (traits.Type is CalculationFieldType calculation && !calculation.ReadSettings(build.Definition(field.Key).Settings).Visible)
            || (traits.Type is CaptchaFieldType && build.Render.Shown.Form.Settings.CaptchaProvider is null))
        {
            return null;
        }

        var root = build.Root;
        var id = Id(field.Key);
        var control = root.Id + "-" + (field.Alias ?? field.Key.ToString("N"));
        var isRequired = build.Required.Contains(id);
        var isHidden = build.Hidden.Contains(id);
        var shownSettings = new ShownSettings(field.Settings, build.Settings(field.Key));
        var element = traits.HoldsValue ? LabelElementOf(traits.Type, shownSettings) : FieldLabelElement.None;
        var mark = MarkOf(build, field, traits.Type, element, isRequired);
        build.MarksRequired |= mark == FieldMark.Required;
        var error = build.Post?.Errors.GetValueOrDefault(field.Key);
        if (error is not null)
        {
            build.Errors.Add(new ErrorLinkModel(error, control));
        }

        var answers = build.Answers.GetValueOrDefault(field.Key) ?? FieldAnswers.None;
        var (model, described) = await ControlAsync(build, field, traits, shownSettings, control, answers, cancellationToken);
        var help = build.Text(field.Key, "help", field.Help);
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
            LabelElement = element,
            Label = traits.HoldsValue ? build.Text(field.Key, "label", field.Label) : null,
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
            Placeholder = build.Text(field.Key, "placeholder", field.Placeholder),
            Autocomplete = field.Autocomplete ?? DefaultAutocomplete(field.Type),
            Values = answers.Values,
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

    private static ProgressViewModel? Progress(PublicFormSettings settings, List<PublicPage> shownPages, PublicPage? current, Build build)
    {
        var (root, words, culture) = (build.Root, build.Words, build.Culture);
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
            Steps = [.. shownPages.Select((page, index) => new ProgressStepModel(build.Text(page.Key, "title", page.Title) ?? string.Format(culture, words.Page, index + 1), index == at, index < at))],
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
        "country" => "country",
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
            NavigationButtonModel? defaultButton = null,
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
                DefaultButton = defaultButton,
                Progress = progress,
                ErrorSummary = errors,
                Navigation = navigation,
                RequiredNote = requiredNote,
                Notice = notice,
                Outcome = outcome,
                ContextValues = context ?? [],
            };
    }

    // What the controls of a page read besides its answers: the site's today, the calculations' values as the rules work
    // them out, and the culture's formats.
    private sealed record PageFacts(DateOnly Today, IReadOnlyDictionary<string, string?> Calculations, CultureFormats Formats);

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
        RequiredMarker marker,
        Dictionary<(Guid Owner, string Name), string> recalled,
        ChoiceLayout layout,
        PageFacts facts)
    {
        // The prefix of a visitor text that is a field's setting, such as settings.text.
        private const string SettingPrefix = "settings.";

        private readonly Dictionary<string, string> _controls = new(StringComparer.Ordinal);

        private Dictionary<Guid, FormField>? _fields;

        public FormRender Render => render;

        public Root Root => root;

        public FormPostState? Post => post;

        public RenderingWords Words => words;

        public CultureInfo Culture => culture;

        public Dictionary<Guid, FieldAnswers> Answers => answers;

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

        public List<ErrorLinkModel> Errors { get; } = [];

        public bool MarksRequired { get; set; }

        // A visitor text as the page shows it: rendered for the page's answers when it holds tags, otherwise as the form
        // endpoint shows it; an empty text is none.
        public string? Text(Guid owner, string name, LocalizedText? text) =>
            recalled.TryGetValue((owner, name), out var shown) ? (shown.Length > 0 ? shown : null) : FormModels.Text(text);

        public HtmlString? Html(Guid owner, string name, LocalizedHtml? html) =>
            recalled.TryGetValue((owner, name), out var shown) ? (shown.Length > 0 ? new HtmlString(shown) : null) : FormModels.Html(html);

        // A field's settings that are visitor texts holding tags, rendered for the page's answers, by the setting's name.
        public Dictionary<string, string> Settings(Guid owner) =>
            recalled.Where(pair => pair.Key.Owner == owner && pair.Key.Name.StartsWith(SettingPrefix, StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key.Name[SettingPrefix.Length..], pair => pair.Value, StringComparer.Ordinal);

        // A field's answers as rules see them: none for a field a rule hides (FormRules).
        public ImmutableArray<string> ShownAnswers(Guid key) =>
            hidden.Contains(Id(key)) ? [] : answers.TryGetValue(key, out var given) ? given.Values : [];

        // A field of the published version by its key, a repeatable group's child among them; the page shows that
        // version's fields, so each one it asks for is there.
        public FormField Definition(Guid key)
        {
            _fields ??= render.Form.Published.Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
                .SelectMany(field => field.Fields.Prepend(field))
                .GroupBy(field => field.Key)
                .ToDictionary(group => group.Key, group => group.First());
            return _fields[key];
        }

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
