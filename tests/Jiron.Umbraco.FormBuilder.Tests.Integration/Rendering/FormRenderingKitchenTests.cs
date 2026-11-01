using System.Collections.Immutable;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The kitchen sink (ROADMAP 6.2f, ADR-0015, ADR-0098): a form with a field of every built-in type and of a site's own
/// type, and a repeatable group whose row holds a field of every type a row may hold, each rendered empty, answered,
/// with an error and hidden by a rule, and the markup contract's hooks checked on each the same way, in a row by the
/// row's path, so that a type added later joins the check; in every built-in theme, with each theme's classes beside
/// ours on every slot of the class map (ADR-0105).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingKitchenTests : RenderingTestBase
{
    private const string RowPath = "kRepeater[0].";
    private const string RowIds = "jfb-1-kRepeater-0-";
    private static readonly Guid Trigger = K(0x01);
    private static readonly Guid GridRow = K(0x90);
    private static readonly Guid RowGridRow = K(0x91);

    // The built-in types and the site's own, in the form's order; the captcha's place sits in a group of its own.
    private static readonly ImmutableArray<string> Types =
    [
        "text", "textarea", "email", "number", "phone", "url", "date", "time", "datetime", "dropdown", "radio", "checkboxes",
        "grid", "ranking", "checkbox", "yesno", "consent", "rating", "scale", "slider", "signature", "name", "address",
        "country", "file", "hidden", "calculation", "repeater", "heading", "paragraph", "divider", "image", "captcha",
        MoodFieldType.TypeAlias,
    ];

    // Each field of the sink: its key, alias and type.
    private static readonly ImmutableArray<(Guid Key, string Alias, string Type)> Sink =
        [.. Types.Select((type, at) => (K(0x10 + at), "k" + Pascal(type), type))];

    // Each field of the repeatable group's row: every type but the repeatable group and the captcha's place, which a
    // row may not hold (ADR-0002).
    private static readonly ImmutableArray<(Guid Key, string Alias, string Type)> RowSink =
        [.. Types.Where(type => type is not ("repeater" or "captcha")).Select((type, at) => (K(0x40 + at), "r" + Pascal(type), type))];

    // The types without an alias: the content types and the captcha's place.
    private static readonly ImmutableArray<string> Unnamed = ["heading", "paragraph", "divider", "image", "captcha"];

    // The built-in types whose fields take a help text, and the site's own.
    private static readonly ImmutableHashSet<string> Helped =
    [
        .. TestFieldTypes.Registry.Types.Select(type => type.Declaration).Where(type => type.Features.HasFlag(FieldFeatures.Help)).Select(type => type.Alias),
        MoodFieldType.TypeAlias,
    ];

    /// <summary>The state the view <c>sink</c> leaves for the form: none, or a post's answers, errors or the rules' trigger.</summary>
    /// <param name="state">The state's name: answered, errors or hidden.</param>
    internal static FormPostState? State(string state) => state switch
    {
        "answered" => new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [Of("text")] = new FieldAnswers(["Ada"]),
                [Of("textarea")] = new FieldAnswers(["Two lines"]),
                [Of("email")] = new FieldAnswers(["ada@example.org"]),
                [Of("number")] = new FieldAnswers(["7"]),
                [Of("phone")] = new FieldAnswers(["+44 20 7946 0000"]),
                [Of("url")] = new FieldAnswers(["https://example.org/"]),
                [Of("date")] = new FieldAnswers(["2026-11-05"]),
                [Of("time")] = new FieldAnswers(["10:30"]),
                [Of("datetime")] = new FieldAnswers(["2026-11-05T10:30"]),
                [Of("dropdown")] = new FieldAnswers(["b"]),
                [Of("radio")] = new FieldAnswers(["b"]),
                [Of("checkboxes")] = new FieldAnswers(["a", "b"]),
                [Of("grid")] = new FieldAnswers([], Parts((GridRow.ToString("D"), "b"))),
                [Of("ranking")] = new FieldAnswers([], Parts(("b", "1"), ("a", "2"))),
                [Of("checkbox")] = new FieldAnswers([FieldAnswerDefaults.Ticked]),
                [Of("yesno")] = new FieldAnswers([YesNoFieldType.Yes]),
                [Of("consent")] = new FieldAnswers([FieldAnswerDefaults.Ticked]),
                [Of("rating")] = new FieldAnswers(["4"]),
                [Of("scale")] = new FieldAnswers(["8"]),
                [Of("slider")] = new FieldAnswers(["30"]),
                [Of("signature")] = new FieldAnswers([], Parts(("typed", "Ada Lovelace"))),
                [Of("name")] = new FieldAnswers([], Parts(("first", "Ada"), ("last", "Lovelace"))),
                [Of("address")] = new FieldAnswers([], Parts(("street", "Quay Street 1"), ("postcode", "1010"), ("city", "Auckland"))),
                [Of("country")] = new FieldAnswers(["NZ"]),
                [Of(MoodFieldType.TypeAlias)] = new FieldAnswers(["bright"]),
            },
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>>
            {
                [Of("repeater")] =
                [
                    new FormPostRow
                    {
                        Answers = new Dictionary<Guid, FieldAnswers>
                        {
                            [InRow("text")] = new FieldAnswers(["Sam"]),
                            [InRow("checkboxes")] = new FieldAnswers(["a"], Parts(("other", "Extra"))),
                            [InRow("name")] = new FieldAnswers([], Parts(("first", "Sam"), ("last", "Okafor"))),
                        },
                    },
                ],
            },
        },
        "errors" => new FormPostState
        {
            Errors = Sink.Where(field => Fails(field.Type)).ToDictionary(field => field.Key, field => ErrorOf(field.Type)),
            Rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>>
            {
                [Of("repeater")] = [new FormPostRow { Errors = RowSink.Where(field => Fails(field.Type)).ToDictionary(field => field.Key, field => ErrorOf(field.Type)) }],
            },
        },
        "hidden" => new FormPostState { Answers = new Dictionary<Guid, FieldAnswers> { [Trigger] = new FieldAnswers(["yes"]) } },
        _ => null,
    };

    [TestCase(BuiltInThemes.DefaultAlias)]
    [TestCase("unstyled")]
    [TestCase("bootstrap5")]
    public async Task EveryFieldType_EmptyAnsweredWithAnErrorAndHidden_CarriesTheMarkupContractsHooks_InEveryBuiltInTheme(string alias)
    {
        var theme = GetRequiredService<FormBuilderThemes>().Find(alias)!;
        _ = await PublishFormAsync("renderingSink", SinkForm);
        _ = await PagesAsync("sink");

        foreach (var state in new[] { "empty", "answered", "errors", "hidden" })
        {
            var form = (await DocumentAsync($"/sink/?state={state}&theme={alias}")).QuerySelector("form.jfb-form")!;
            var wrappers = form.QuerySelectorAll("[data-jfb-field]").Where(wrapper => wrapper.ParentElement?.Closest(".jfb-row") is null);
            Assert.Multiple(() =>
            {
                Assert.That(wrappers.Select(wrapper => wrapper.GetAttribute("data-jfb-type")), Is.EqualTo(Types.Where(type => type != "hidden").Prepend("text")), $"{state}: every type but the hidden field, after the trigger");
                Assert.That(form.QuerySelectorAll("[data-jfb-error]"), Has.Length.EqualTo(state == "errors" ? Sink.Concat(RowSink).Count(field => Fails(field.Type)) : 0), $"{state}: an error for each field that can fail, its row's among them");
                Assert.That(form.QuerySelector(".jfb-error-summary") is null, Is.EqualTo(state != "errors"), $"{state}: the summary only with errors");
                Assert.That(form.QuerySelectorAll("[aria-invalid]").Length > 0, Is.EqualTo(state == "errors"), $"{state}: aria-invalid only after errors");
                Assert.That(form.ClassList, Does.Contain("jfb-theme-" + alias), state);
            });

            var error = theme.Classes.TryGetValue(FormBuilderThemeSlots.Error, out var errorClasses) ? "jfb-error " + errorClasses : "jfb-error";
            foreach (var field in Sink.Where(field => field.Type != "hidden"))
            {
                Hooks(form, state, field, inRow: false, error);
            }

            foreach (var field in RowSink.Where(field => field.Type != "hidden"))
            {
                Hooks(form, state, field, inRow: true, error);
            }

            ThemeClasses(form, $"{alias}, {state}", theme);

            Rows(form, state);
            if (state == "answered")
            {
                Answered(form);
            }
        }
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().FieldTypes().Append<MoodFieldType>();
    }

    // ADR-0015's hooks on one field in one state: its wrapper, and hidden with disabled controls when a rule hides it or
    // its row's group; for a built-in field with an alias, its control's id, its label or legend, its names and its
    // help; after an error, the error, aria-invalid on the control or a group's choices, and the summary's link to the
    // control. A row's field has its row's ids and is named by the row's path (ADR-0098).
    private static void Hooks(IElement form, string state, (Guid Key, string Alias, string Type) field, bool inRow, string error)
    {
        var (key, sinkAlias, type) = field;
        var alias = Unnamed.Contains(type) ? null : sinkAlias;
        var path = alias is null ? null : (inRow ? RowPath : string.Empty) + alias;
        var at = $"{state}, {(inRow ? "a row's " : string.Empty)}{type}";
        var wrapper = form.QuerySelector($"[data-jfb-field='{key:D}']") ?? throw new AssertionException($"{at}: no field");
        var control = (inRow ? RowIds : "jfb-1-") + (alias ?? key.ToString("N"));
        Assert.Multiple(() =>
        {
            Assert.That(wrapper.GetAttribute("class")!.Split(' ').Take(2), Is.EqualTo(new[] { "jfb-field", "jfb-field-" + type }), at);
            Assert.That(wrapper.GetAttribute("data-jfb-type"), Is.EqualTo(type), at);
            Assert.That(wrapper.GetAttribute("data-jfb-alias"), Is.EqualTo(path), at);
            Assert.That(wrapper.Closest("[hidden][data-jfb-hidden]") is not null, Is.EqualTo(state == "hidden"), $"{at}: hidden by its rule, its row's group's, or the captcha's place by its group's");
            Assert.That(wrapper.QuerySelectorAll("input, select, textarea, button").Select(input => input.HasAttribute("disabled")), Is.All.EqualTo(state == "hidden"), $"{at}: a hidden field's controls are disabled");
        });
        if (path is not null)
        {
            var target = wrapper.QuerySelector($"[id='{control}']") ?? throw new AssertionException($"{at}: no control with the field's id");
            var fieldset = wrapper.QuerySelector(":scope > fieldset");
            var described = fieldset ?? target;
            // A fieldset's role takes no invalid state (6.8c): a group of choices' own choices carry the error instead.
            var choices = fieldset is null ? [] : wrapper.QuerySelectorAll("input[type=radio], input[type=checkbox]").Where(input => input.Closest("[data-jfb-field]") == wrapper).ToArray();
            var refused = state == "errors" && Fails(type);
            Assert.Multiple(() =>
            {
                Assert.That(fieldset?.QuerySelector(":scope > legend") ?? wrapper.QuerySelector($"label[for='{control}']"), Is.Not.Null, $"{at}: a legend for a fieldset, otherwise a label for the control");
                Assert.That(wrapper.QuerySelectorAll("[name]").Select(named => named.GetAttribute("name")!).Where(name => !Named(name, path)), Is.Empty, $"{at}: named by the alias or the row's path, a part or a row after it");
                Assert.That(wrapper.QuerySelector($"[id='{control}-help']")?.TextContent, Is.EqualTo(Helped.Contains(type) ? "Help for " + alias : null), at);
                Assert.That(Ids(described, "aria-describedby").Contains($"{control}-help"), Is.EqualTo(Helped.Contains(type)), $"{at}: the help described");
                Assert.That(described.GetAttribute("aria-invalid"), Is.EqualTo(refused && fieldset is null ? "true" : null), at);
                Assert.That(Ids(described, "aria-describedby").Contains($"{control}-error"), Is.EqualTo(refused && choices.Length == 0), $"{at}: the error described");
                Assert.That(choices.Select(choice => (choice.GetAttribute("aria-invalid"), Ids(choice, "aria-describedby").Contains($"{control}-error"))), Is.All.EqualTo(refused ? ("true", true) : ((string?)null, false)), $"{at}: a group's choices carry its error");
            });
        }

        if (state == "errors" && Fails(type))
        {
            Assert.Multiple(() =>
            {
                Assert.That(wrapper.QuerySelector($"[id='{control}-error']")?.OuterHtml, Is.EqualTo($"""<p class="{error}" id="{control}-error" data-jfb-error="">{Messages[type]}</p>"""), at);
                Assert.That(form.QuerySelectorAll($".jfb-error-summary a[href='#{control}']"), Has.Length.EqualTo(1), $"{at}: the summary's link to the control");
            });
        }
    }

    // Each slot's elements carry the theme's classes beside ours (ADR-0105): one of ours keeps its classes and gains the
    // theme's for its slot, and one without a class of ours gets the theme's alone. Every class on the form is ours or
    // one the theme's map gives, but in the site's own type's view, which writes its own. The slots the sink always
    // shows are each seen.
    private static void ThemeClasses(IElement form, string at, IFormBuilderTheme theme)
    {
        string[] Of(string slot) => theme.Classes.TryGetValue(slot, out var classes) ? classes.Split(' ') : [];
        bool Ours(IElement element) => element.Closest($"[data-jfb-type='{MoodFieldType.TypeAlias}']") is null;
        var slots = new (string Selector, string Slot, bool Seen)[]
        {
            ("form.jfb-form", FormBuilderThemeSlots.Form, true),
            (".jfb-page", FormBuilderThemeSlots.Page, true),
            (".jfb-group", FormBuilderThemeSlots.Group, true),
            (".jfb-field", FormBuilderThemeSlots.Field, true),
            ("label.jfb-label, legend.jfb-label", FormBuilderThemeSlots.Label, true),
            (".jfb-part > label", FormBuilderThemeSlots.Label, true),
            (".jfb-help", FormBuilderThemeSlots.Help, true),
            (".jfb-error", FormBuilderThemeSlots.Error, false),
            ("input.jfb-control, textarea.jfb-control", FormBuilderThemeSlots.Control, true),
            ("input[type=file]", FormBuilderThemeSlots.Control, true),
            ("select.jfb-control", FormBuilderThemeSlots.Select, true),
            (".jfb-choices", FormBuilderThemeSlots.Choices, true),
            (".jfb-choice", FormBuilderThemeSlots.Choice, true),
            (".jfb-choice > input:is([type=radio], [type=checkbox])", FormBuilderThemeSlots.ChoiceInput, true),
            (".jfb-choice > label", FormBuilderThemeSlots.ChoiceLabel, true),
            ("input[type=range]", FormBuilderThemeSlots.Range, true),
            (".jfb-affix", FormBuilderThemeSlots.Affix, false),
            (".jfb-affix > span", FormBuilderThemeSlots.AffixText, false),
            (".jfb-counter", FormBuilderThemeSlots.Counter, false),
            (".jfb-navigation", FormBuilderThemeSlots.Navigation, true),
            (".jfb-button-primary", FormBuilderThemeSlots.ButtonPrimary, true),
            (".jfb-button-link", FormBuilderThemeSlots.ButtonLink, false),
            (".jfb-button:not(.jfb-button-primary):not(.jfb-button-link)", FormBuilderThemeSlots.Button, true),
            (".jfb-progress", FormBuilderThemeSlots.Progress, false),
            (".jfb-error-summary", FormBuilderThemeSlots.ErrorSummary, false),
        };
        var allowed = theme.Classes.Values.SelectMany(classes => classes.Split(' ')).ToHashSet(StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            foreach (var (selector, slot, seen) in slots)
            {
                var elements = (selector == "form.jfb-form" ? new[] { form } : form.QuerySelectorAll(selector).AsEnumerable()).Where(Ours).ToList();
                if (seen)
                {
                    Assert.That(elements, Is.Not.Empty, $"{at}: the sink shows {selector}");
                }

                foreach (var element in elements)
                {
                    Assert.That(element.ClassList, Is.SupersetOf(Of(slot)), $"{at}: {selector} carries the theme's classes for {slot}: {element.OuterHtml}");
                }
            }

            var foreign = form.QuerySelectorAll("[class]").Where(Ours).SelectMany(element => element.ClassList).Where(name => !name.StartsWith("jfb-", StringComparison.Ordinal) && !allowed.Contains(name)).Distinct();
            Assert.That(foreign, Is.Empty, $"{at}: every class is ours or the theme's");
        });
    }

    // What the answered state shows of each answer: a text in its input, a choice checked or selected, a part in its
    // input, a ranking's order, a calculation's value, the site's own type in its own view.
    private static void Answered(IElement form)
    {
        bool Has(string selector) => form.QuerySelector(selector) is not null;
        Assert.Multiple(() =>
        {
            foreach (var (type, value) in new[] { ("text", "Ada"), ("email", "ada@example.org"), ("number", "7"), ("phone", "+44 20 7946 0000"), ("url", "https://example.org/"), ("date", "2026-11-05"), ("time", "10:30"), ("datetime", "2026-11-05T10:30"), ("slider", "30") })
            {
                Assert.That(form.QuerySelector($"#jfb-1-{AliasOf(type)}")!.GetAttribute("value"), Is.EqualTo(value), type);
            }

            Assert.That(form.QuerySelector("#jfb-1-kTextarea")!.TextContent, Is.EqualTo("Two lines"));
            Assert.That(Has("#jfb-1-kDropdown option[value=b][selected]"), Is.True, "dropdown");
            Assert.That(Has("input[name=kRadio][value=b][checked]"), Is.True, "radio");
            Assert.That(form.QuerySelectorAll("input[name=kCheckboxes][checked]").Select(box => box.GetAttribute("value")), Is.EqualTo(new[] { "a", "b" }), "checkboxes");
            Assert.That(Has($"input[name='kGrid.{GridRow:D}'][value=b][checked]"), Is.True, "grid");
            Assert.That(form.QuerySelectorAll("input[type=hidden][name^='kRanking.']").Select(input => (input.GetAttribute("name"), input.GetAttribute("value"))), Is.EqualTo(new[] { ("kRanking.b", "1"), ("kRanking.a", "2") }), "ranking");
            Assert.That(Has("input[name=kCheckbox][value=true][checked]"), Is.True, "checkbox");
            Assert.That(Has("input[name=kYesno][value=yes][checked]"), Is.True, "yesno");
            Assert.That(Has("input[name=kConsent][value=true][checked]"), Is.True, "consent");
            Assert.That(Has("input[name=kRating][value='4'][checked]"), Is.True, "rating");
            Assert.That(Has("input[name=kScale][value='8'][checked]"), Is.True, "scale");
            Assert.That(form.QuerySelector("input[name='kSignature.typed']")!.GetAttribute("value"), Is.EqualTo("Ada Lovelace"), "signature");
            Assert.That(form.QuerySelector("input[name='kName.last']")!.GetAttribute("value"), Is.EqualTo("Lovelace"), "name");
            Assert.That(form.QuerySelector("input[name='kAddress.city']")!.GetAttribute("value"), Is.EqualTo("Auckland"), "address");
            Assert.That(Has("#jfb-1-kCountry option[value=NZ][selected]"), Is.True, "country");
            Assert.That(form.QuerySelector("#jfb-1-kCalculation")!.TextContent, Is.EqualTo("14.00"), "the rules' value for the number");
            Assert.That(form.QuerySelector($"[data-jfb-field='{Of(MoodFieldType.TypeAlias):D}'] select[data-test-custom=mood] option[selected]")?.GetAttribute("value"), Is.EqualTo("bright"), "the site's own type in its own view");
        });
    }

    // The repeatable group's row in each state: its fieldset and its fields in order, a part's name and id, a few answers,
    // a consent's wording by the row's path, and the row's errors linked from the summary in the row's order.
    private static void Rows(IElement form, string state)
    {
        var row = form.QuerySelector($"[data-jfb-field='{Of("repeater"):D}'] .jfb-row") ?? throw new AssertionException($"{state}: no row");
        var name = row.QuerySelector($"[data-jfb-field='{InRow("name"):D}']")!;
        Assert.Multiple(() =>
        {
            Assert.That(row.Id, Is.EqualTo("jfb-1-kRepeater-0"), state);
            Assert.That(row.QuerySelectorAll(":scope > div > [data-jfb-field]").Select(child => child.GetAttribute("data-jfb-type")), Is.EqualTo(RowSink.Where(field => field.Type != "hidden").Select(field => field.Type)), $"{state}: every type a row may hold but the hidden field");
            Assert.That(name.QuerySelectorAll("input").Select(input => (input.Id, input.GetAttribute("name"))), Is.EqualTo(new[]
            {
                (RowIds + "rName", RowPath + "rName.first"),
                (RowIds + "rName-last", RowPath + "rName.last"),
            }), $"{state}: a part's name after its row's path");
            Assert.That(name.QuerySelectorAll("input").Select(input => input.GetAttribute("aria-invalid")), Is.All.EqualTo(state == "errors" ? "true" : null), $"{state}: its parts carry its row's error");
            Assert.That(form.QuerySelector($"#{RowIds}rText")!.GetAttribute("value"), Is.EqualTo(state == "answered" ? "Sam" : null), state);
            Assert.That(form.QuerySelector($"#{RowIds}rName-last")!.GetAttribute("value"), Is.EqualTo(state == "answered" ? "Okafor" : null), state);
            Assert.That(form.QuerySelector($"#{RowIds}rCheckboxes-other")!.GetAttribute("name"), Is.EqualTo(RowPath + "rCheckboxes.other"), state);
            Assert.That(form.QuerySelector($"#{RowIds}rCheckboxes-other")!.GetAttribute("value"), Is.EqualTo(state == "answered" ? "Extra" : null), state);
            Assert.That(row.QuerySelectorAll("input[type=hidden]").Select(input => input.GetAttribute("name")), Does.Contain(FormModels.ConsentPrefix + RowPath + "rConsent"), $"{state}: a consent's wording in its row");
            Assert.That(form.QuerySelectorAll($".jfb-error-summary a[href^='#{RowIds}']").Select(link => link.GetAttribute("href")), Is.EqualTo(state == "errors" ? RowSink.Where(field => Fails(field.Type)).Select(field => "#" + RowIds + field.Alias) : []), $"{state}: a row's errors link to their controls in the row's order");
        });
    }

    // Whether a type's field can carry an error: every type but the content types, the hidden field and a calculation.
    // The error each type's field gets: no answer, a group's rows too few, and the captcha's answer unread.
    private static FormPostError ErrorOf(string type) => new(type switch
    {
        "repeater" => "rowCount",
        "captcha" => "invalid",
        _ => "required",
    });

    // Each type's default message for its error (ADR-0099); the site's own type gets the general words.
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["text"] = "Enter an answer",
        ["textarea"] = "Enter an answer",
        ["email"] = "Enter an email address",
        ["number"] = "Enter a number",
        ["phone"] = "Enter a phone number",
        ["url"] = "Enter a web address",
        ["date"] = "Enter a date",
        ["time"] = "Enter a time",
        ["datetime"] = "Enter a date and a time",
        ["dropdown"] = "Choose an answer",
        ["radio"] = "Choose an answer",
        ["checkboxes"] = "Choose at least one answer",
        ["grid"] = "Choose an answer in each row",
        ["ranking"] = "Put the options in order",
        ["checkbox"] = "Tick the box",
        ["yesno"] = "Choose Yes or No",
        ["consent"] = "Tick the box to agree",
        ["rating"] = "Choose a rating",
        ["scale"] = "Choose a point on the scale",
        ["slider"] = "Choose a value",
        ["signature"] = "Add your signature, or type your name instead",
        ["name"] = "Enter the name in full",
        ["address"] = "Enter the address in full",
        ["country"] = "Choose a country",
        ["file"] = "Choose a file",
        ["repeater"] = "Add at least 1 row",
        ["captcha"] = "Check this answer",
        [MoodFieldType.TypeAlias] = "Answer this question",
    };

    private static bool Fails(string type) => type is not ("heading" or "paragraph" or "divider" or "image" or "hidden" or "calculation");

    // A control's name for a field: its alias, a part or row after it, a move or a row's button, or a consent's hash
    // named so after its prefix.
    private static bool Named(string name, string alias)
    {
        var named = name.StartsWith(FormModels.ConsentPrefix, StringComparison.Ordinal) ? name[FormModels.ConsentPrefix.Length..]
            : name.StartsWith(FormPostFields.RowsPrefix, StringComparison.Ordinal) ? name[FormPostFields.RowsPrefix.Length..]
            : name.StartsWith(FormPostFields.OrderPrefix, StringComparison.Ordinal) ? name[FormPostFields.OrderPrefix.Length..]
            : name;
        return name == "jfb-nav" || named == alias || named.StartsWith(alias + ".", StringComparison.Ordinal) || named.StartsWith(alias + "[", StringComparison.Ordinal);
    }

    // A field of each type with help where its type takes one and a rule that hides it, after a trigger; the captcha's
    // place in a group of its own that a rule hides; the form uses a captcha.
    internal static FormDefinition SinkForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Protection = new FormProtectionSettings { Level = ProtectionLevel.Captcha, Captcha = new CaptchaChoice { Provider = "hcaptcha" } } },
        Pages =
        [
            new FormPage
            {
                Key = K(0x02),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x03),
                        Columns = 2,
                        Fields = [new FormField { Key = Trigger, Type = "text", Alias = "hide", Label = new LocalizedText("Hide") }, .. Sink.Where(field => field.Type is not ("captcha" or MoodFieldType.TypeAlias)).Select(field => SinkField(field, inRow: false))],
                    },
                    new FormGroup
                    {
                        Key = K(0x04),
                        Rules = [HideRule(K(0x1ff))],
                        Fields = [.. Sink.Where(field => field.Type is "captcha" or MoodFieldType.TypeAlias).Select(field => SinkField(field, inRow: false))],
                    },
                ],
            },
        ],
    };

    // A field of the sink, or of the repeatable group's row, which its group's rule hides instead of a rule of its own.
    private static FormField SinkField((Guid Key, string Alias, string Type) field, bool inRow)
    {
        var (key, alias, type) = field;
        return new FormField
        {
            Key = key,
            Type = type,
            Alias = Unnamed.Contains(type) ? null : alias,
            Label = Unnamed.Contains(type) ? null : new LocalizedText(alias),
            Help = Helped.Contains(type) ? new LocalizedText("Help for " + alias) : null,
            Settings = SinkSettings(type, inRow),
            Options = type is "dropdown" or "radio" or "checkboxes" or "ranking" or "grid"
                ? new FieldOptions { Items = [new FieldOption { Value = "a", Label = new LocalizedText("Alpha") }, new FieldOption { Value = "b", Label = new LocalizedText("Beta") }] }
                : null,
            Rows = type == "grid" ? [new GridRow { Key = inRow ? RowGridRow : GridRow, Label = new LocalizedText("The room") }] : [],
            Fields = type == "repeater" ? [.. RowSink.Select(child => SinkField(child, inRow: true))] : [],
            Rules = inRow || type is "captcha" or MoodFieldType.TypeAlias ? [] : [HideRule(K(0x100 + key.ToByteArray()[15]))],
        };
    }

    // A type's settings in the sink: a calculation over the number beside it, on the page or in the row.
    private static SettingsObject? SinkSettings(string type, bool inRow) => type switch
    {
        "consent" => SettingsObject.Parse("""{"text":{"":"<p>I agree to the terms.</p>"}}"""),
        "checkboxes" => SettingsObject.Parse("""{"other":true}"""),
        "calculation" => SettingsObject.Parse(inRow ? """{"formula":"rNumber * 2"}""" : """{"formula":"kNumber * 2"}"""),
        "repeater" => SettingsObject.Parse("""{"minRows":1,"maxRows":2}"""),
        "heading" => SettingsObject.Parse("""{"text":{"":"Before you send"}}"""),
        "paragraph" => SettingsObject.Parse("""{"html":{"":"<p>We reply within a day.</p>"}}"""),
        "image" => SettingsObject.Parse($$$"""{"media":"{{{K(0xa0):D}}}","alt":{"":"A missing picture"}}"""),
        "slider" => SettingsObject.Parse("""{"min":"0","max":"60","step":"5"}"""),
        MoodFieldType.TypeAlias => SettingsObject.Parse("""{"moods":["calm","bright"],"prompt":{"":"How do you feel?"}}"""),
        _ => null,
    };

    // A rule that hides its element while the trigger says yes.
    private static FormRule HideRule(Guid key) => new()
    {
        Key = key,
        Effect = RuleEffect.Hide,
        Conditions = [new RuleCondition { Field = Trigger, Operator = "equals", Value = new ConditionOperand.Text("yes") }],
    };

    private static Guid Of(string type) => Sink.Single(field => field.Type == type).Key;

    private static string AliasOf(string type) => Sink.Single(field => field.Type == type).Alias;

    private static Guid InRow(string type) => RowSink.Single(field => field.Type == type).Key;

    // A type's alias in Pascal case, after a site's own type's prefix.
    private static string Pascal(string type)
    {
        var name = type.Split('.')[^1];
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-00000062f{number + 0x200:x3}");

    private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> Parts(params (string Name, string Text)[] parts) =>
        parts.Select(part => new KeyValuePair<string, IEnumerable<string>>(part.Name, [part.Text]));

    private static string[] Ids(IElement element, string attribute) => (element.GetAttribute(attribute) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{path} answers {(int)response.StatusCode}: {html}");
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
