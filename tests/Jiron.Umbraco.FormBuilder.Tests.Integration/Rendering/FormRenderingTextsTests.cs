using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The texts a rendered form shows (ROADMAP 6.2c, ADR-0068, ADR-0095): a visitor text that recalls an earlier answer
/// shows what the server renders for the page's answers, a post's or the defaults, each in its own place, an answer past
/// what its field's type takes recalling nothing and a text that renders as nothing showing nothing; and the
/// General section's required-marker style and button labels, on the site that sets them, where a form has none of its
/// own.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingTextsTests : RenderingTestBase
{
    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);

        // Its forms hold tags their places refuse, which both runtimes write as they stand (ADR-0177).
        builder.PublishesRefusedTags();
    }

    private static readonly Guid PageOne = Key(0x01);
    private static readonly Guid PageTwo = Key(0x02);
    private static readonly Guid Name = Key(0x10);
    private static readonly Guid Note = Key(0x20);
    private static readonly string Masked = new((char)0x2022, 3);

    [Test]
    public async Task AfterAPost_ALabelThatRecallsAnEarlierAnswer_ShowsTheTextTheServerRendersForTheSameAnswers()
    {
        var key = await PublishFormAsync("renderingRecall", Recall);
        _ = await PagesAsync("recall");
        var (_, document) = await PageAsync("recall");

        var form = document.QuerySelector("form.jfb-form")!;
        var page = form.QuerySelector($"section[data-jfb-page='{PageTwo:D}']")!;
        var expected = await RenderedAsync(key, Note, "Anything else, {{ field.name }}?", "Ada <Lovelace>");
        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.EqualTo("Anything else, Ada <Lovelace>?"));
            Assert.That(Label(form, "note"), Is.EqualTo(expected), "the label shows what the server renders for the same answers");
            Assert.That(page.HasAttribute("hidden"), Is.False, "the post's page is current");
            Assert.That(page.QuerySelector("h2.jfb-title")!.TextContent, Is.EqualTo("Almost done, Ada <Lovelace>"));
            Assert.That(form.QuerySelectorAll(".jfb-progress-steps li").Select(step => step.TextContent), Is.EqualTo(new[] { "Your details", "Almost done, Ada <Lovelace>" }), "the step names the page as its title does");
            Assert.That(page.QuerySelector("legend")!.TextContent, Is.EqualTo("For Ada <Lovelace>"));
            Assert.That(form.QuerySelector("#jfb-1-note-help")!.TextContent, Is.EqualTo("Your code: " + Masked + "; extra: ; later: {{ field.after }}"), "a sensitive answer masked, a field the rules hid empty, a later field's tag as it is written");
            Assert.That(form.QuerySelector("#jfb-1-note")!.GetAttribute("placeholder"), Is.EqualTo("Ada <Lovelace>, tell us more"));
            Assert.That(form.QuerySelector(".jfb-heading")!.TextContent, Is.EqualTo("Ada <Lovelace>'s notes"), "a setting that takes merge tags");
            Assert.That(form.QuerySelector(".jfb-text")!.InnerHtml, Is.EqualTo("<p>Hi <b>Ada &lt;Lovelace&gt;</b></p>"), "rich text escapes the answer");
            Assert.That(Hooks(form), Is.EqualTo(new[]
            {
                ("span", $"{PageTwo:D} title"),
                ("h2", $"{PageTwo:D} title"),
                ("legend", $"{Key(0x04):D} title"),
                ("span", $"{Note:D} label"),
                ("p", $"{Note:D} help"),
                ("textarea", $"{Note:D} placeholder"),
                ("h2", $"{Key(0x21):D} settings.text"),
                ("div", $"{Key(0x22):D} settings.html"),
            }), "each text that holds tags carries its hook, in the order of the page (ADR-0115)");
            Assert.That(form.QuerySelector(".jfb-progress-steps li span[data-jfb-text]")!.GetAttribute("data-jfb-fallback"), Is.EqualTo("Page 2"), "what the step shows while the title renders empty");
            Assert.That(form.QuerySelector("[data-jfb-alias=name] label")!.QuerySelector("[data-jfb-text]"), Is.Null, "a label without tags has none");
        });
    }

    [Test]
    public async Task OnTheFirstRender_ATextRecallsTheDefaults_AndTheModelDataKeepsItsTemplateForTheRuntime()
    {
        var key = await PublishFormAsync("renderingRecall", definition => Recall(definition, name: "Grace"));
        _ = await PagesAsync("recall-start");
        var (_, document) = await PageAsync("recall-start");

        var form = document.QuerySelector("form.jfb-form")!;
        var model = JsonNode.Parse(form.QuerySelector("script[data-jfb-model]")!.TextContent)!;
        Assert.Multiple(async () =>
        {
            Assert.That(Label(form, "note"), Is.EqualTo(await RenderedAsync(key, Note, "Anything else, {{ field.name }}?", "Grace")));
            Assert.That(Label(form, "note"), Is.EqualTo("Anything else, Grace?"));
            Assert.That(form.QuerySelector($"section[data-jfb-page='{PageTwo:D}'] h2.jfb-title")!.TextContent, Is.EqualTo("Almost done, Grace"), "a later page recalls them too");
            Assert.That(form.QuerySelector("#jfb-1-note-help")!.TextContent, Is.EqualTo("Your code: ; extra: ; later: {{ field.after }}"), "an unanswered field is empty");
            Assert.That(model["mergeTags"]!["texts"]!.AsArray().Select(text => (text!["owner"]!.GetValue<string>(), text["text"]!.GetValue<string>())), Does.Contain((Note.ToString("D"), "label")), "the runtime renders the label again as the visitor types");
            Assert.That(model["form"]!["pages"]![1]!["groups"]![0]!["fields"]![0]!["label"]!.GetValue<string>(), Is.EqualTo("Anything else, {{ field.name }}?"));
        });
    }

    [Test]
    public async Task AfterAPost_AnAnswerPastWhatItsTypeTakes_RecallsNothing_AndATextThatRendersAsNothing_ShowsNothing()
    {
        _ = await PublishFormAsync("renderingRecall", definition => Recall(definition, name: null, afterHelp: "{{ field.extra }}"));
        _ = await PagesAsync("recall-limits");
        var (_, document) = await PageAsync("recall-limits");

        var form = document.QuerySelector("form.jfb-form")!;
        Assert.Multiple(() =>
        {
            Assert.That(Label(form, "note"), Is.EqualTo("Anything else, ?"), "an answer longer than a text takes recalls nothing");
            var help = form.QuerySelector("#jfb-1-after-help");
            Assert.That(help?.HasAttribute("hidden"), Is.True, "a help that recalls only a hidden field renders empty, hidden, for the runtime to fill (ADR-0115)");
            Assert.That(help?.TextContent, Is.Empty);
            Assert.That(help?.GetAttribute("data-jfb-text"), Is.EqualTo(Key(0x23).ToString("D") + " help"));
            Assert.That(form.QuerySelector("#jfb-1-after")!.GetAttribute("aria-describedby"), Is.EqualTo("jfb-1-after-help"), "the control names it while it is hidden");
        });
    }

    [Test]
    public async Task ASitesRequiredMarkerAndSendLabel_ApplyOnThatSiteOnly_WhereAFormHasNoneOfItsOwn()
    {
        var key = await PublishFormAsync("renderingSited", definition => Marked(definition, RequiredMarker.Inherit, submit: null), NameField with { Required = true }, City);
        _ = await PublishFormAsync("renderingOwn", definition => Marked(definition, RequiredMarker.Optional, submit: "Book now"), NameField with { Required = true }, City);
        _ = await PagesAsync("sited");
        var (second, pages) = await SecondSiteAsync("sited");
        await GetRequiredService<IFormBuilderSettingsService>().SaveSiteAsync(
            second,
            new GeneralSettings { RequiredMarker = RequiredMarker.Required, SubmitLabel = new Dictionary<string, string> { [""] = "Send it", ["de"] = "Abschicken" } },
            ["requiredMarker", "submitLabel"],
            0,
            Editor);

        var (_, there) = await PageAtAsync(SecondHost, "sited");
        var (_, here) = await PageAtAsync(FirstHost, "sited");
        using var answer = await GetAsync($"{Root}/forms/{key}?contentKey={pages["sited"]}");

        var sited = there.QuerySelector("form#jfb-1")!;
        var own = there.QuerySelector("form#jfb-2")!;
        var elsewhere = here.QuerySelector("form#jfb-1")!;
        Assert.Multiple(async () =>
        {
            Assert.That(Marks(sited), Is.EqualTo(new[] { ("name", "*"), ("city", "") }), "the site's style marks the required fields");
            Assert.That(sited.QuerySelector("p.jfb-help")!.TextContent, Is.EqualTo("Questions marked *with an asterisk need an answer."));
            Assert.That(sited.QuerySelector(".jfb-navigation button")!.TextContent, Is.EqualTo("Send it"), "the site's Send label");
            Assert.That(Marks(own), Is.EqualTo(new[] { ("name", ""), ("city", "(optional)") }), "a form's own style stands");
            Assert.That(own.QuerySelector(".jfb-navigation button")!.TextContent, Is.EqualTo("Book now"), "and its own label");
            Assert.That(Marks(elsewhere), Is.EqualTo(new[] { ("name", ""), ("city", "(optional)") }), "another site keeps the default style");
            Assert.That(elsewhere.QuerySelector(".jfb-navigation button")!.TextContent, Is.EqualTo("Send"), "and the package's word");
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(sited.QuerySelector("script[data-jfb-model]")!.TextContent), await JsonAsync(answer)), Is.True, "the endpoint answers the same for the site's page");
        });
        using var global = await GetAsync($"{Root}/forms/{key}");
        var settings = (await JsonAsync(global))["form"]!["settings"]!;
        Assert.Multiple(() =>
        {
            Assert.That(settings["requiredMarker"]!.GetValue<string>(), Is.EqualTo("optional"), "without a page, the values every site shares");
            Assert.That(settings.AsObject().ContainsKey("submitLabel"), Is.False);
        });
    }

    [Test]
    public async Task ASitesAndAFormsOwnWording_ShowOnThePage_InItsAnswer_AndInAPostsErrors()
    {
        // The form rewords the error of an empty answer and the summary's heading; the site the optional mark, the
        // error too (which the form's wording beats) and the word for yes, in every language (ADR-0230).
        var key = await PublishFormAsync(
            "renderingWorded",
            definition => Marked(definition, RequiredMarker.Optional, submit: null) with
            {
                Settings = definition.Settings with
                {
                    Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Optional },
                    Submission = definition.Settings.Submission with
                    {
                        Texts = Worded(("message.answer", "", "Tell us this, please"), ("message.summaryHeading.one", "", "One answer to fix")),
                    },
                },
            },
            NameField with { Required = true },
            City);
        _ = await PagesAsync("worded");
        var (second, pages) = await SecondSiteAsync("worded");
        await GetRequiredService<IFormBuilderSettingsService>().SaveSiteAsync(
            second,
            new GeneralSettings { Texts = Worded(("form.optional", "", "(if you like)"), ("message.answer", "", "The site's words"), ("answer.yes", "", "Sure")) },
            ["texts"],
            0,
            Editor);

        var (_, there) = await PageAtAsync(SecondHost, "worded");
        var (_, here) = await PageAtAsync(FirstHost, "worded");
        using var answer = await GetAsync($"{Root}/forms/{key}?contentKey={pages["worded"]}");
        var json = await JsonAsync(answer);
        var form = there.QuerySelector("form#jfb-1")!;

        // A city but no name, so the name's own error shows rather than the form's call for any answer at all.
        using var posted = await SendAsync(HttpMethod.Post, "http://" + SecondHost + form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = new FormUrlEncodedContent(Posted(form, FormPostFields.Submit).Select(pair => pair.Key == "city" ? new KeyValuePair<string, string>("city", "Bern") : pair));
            request.Headers.TryAddWithoutValidation("Origin", "http://" + SecondHost);
            request.Headers.Referrer = new Uri("http://" + SecondHost + "/worded/");
        });
        var again = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await posted.Content.ReadAsStringAsync()).QuerySelector("form#jfb-1")!;
        var summary = again.Children.FirstOrDefault(child => child.ClassList.Contains("jfb-error-summary"));

        Assert.Multiple(() =>
        {
            Assert.That(Marks(form), Is.EqualTo(new[] { ("name", ""), ("city", "(if you like)") }), "the site's optional mark");
            Assert.That(Marks(here.QuerySelector("form#jfb-1")!), Is.EqualTo(new[] { ("name", ""), ("city", "(optional)") }), "another site keeps the package's");
            Assert.That(json["texts"]!.ToJsonString(), Is.EqualTo("""{"answer":"Tell us this, please","summaryHeading.one":"One answer to fix"}"""), "the messages, the form's before the site's, for the browser");
            Assert.That(json["words"]!["yes"]!.GetValue<string>(), Is.EqualTo("Sure"), "the site's word for yes");
            Assert.That(again.QuerySelector("#jfb-1-name-error")?.TextContent.Trim(), Is.EqualTo("Tell us this, please"), "a post's error in the form's words, which beat the site's");
            Assert.That(summary?.QuerySelector("h2")?.TextContent, Is.EqualTo("One answer to fix"), "the summary's heading in the form's words");
        });
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Worded(params (string Key, string Culture, string Wording)[] texts) =>
        texts.GroupBy(text => text.Key).ToDictionary(
            group => group.Key,
            group => (IReadOnlyDictionary<string, string>)group.ToDictionary(text => text.Culture, text => text.Wording),
            StringComparer.Ordinal);

    private static readonly FormField City = new() { Key = Key(0x30), Type = "text", Alias = "city", Label = new LocalizedText("City") };

    private static Guid Key(int n) => Guid.Parse($"30000000-0000-4000-8000-0000000062{n:x2}");

    // Two pages: a name, a sensitive code and a field the rules hide once a name is given; then texts that recall them,
    // and a field after them.
    private static FormDefinition Recall(FormDefinition definition) => Recall(definition, name: null);

    private static FormDefinition Recall(FormDefinition definition, string? name, string? afterHelp = null) => definition with
    {
        Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.None } },
        Pages =
        [
            new FormPage
            {
                Key = PageOne,
                Title = new LocalizedText("Your details"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(0x03),
                        Fields =
                        [
                            Field(Name, "text", "name", "Your name") with { DefaultValue = name is null ? null : FieldDefaultValue.Of(name) },
                            Field(Key(0x11), "text", "code", "Code") with { Sensitive = true },
                            Field(Key(0x12), "text", "extra", "Extra") with
                            {
                                Rules = [new FormRule { Key = Key(0x40), Effect = RuleEffect.Hide, Conditions = [new RuleCondition { Field = Name, Operator = "isNotEmpty" }] }],
                            },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = PageTwo,
                Title = new LocalizedText("Almost done, {{ field.name }}"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(0x04),
                        Title = new LocalizedText("For {{ field.name }}"),
                        Fields =
                        [
                            Field(Note, "textarea", "note", "Anything else, {{ field.name }}?") with
                            {
                                Help = new LocalizedText("Your code: {{ field.code }}; extra: {{ field.extra }}; later: {{ field.after }}"),
                                Placeholder = new LocalizedText("{{ field.name }}, tell us more"),
                            },
                            Field(Key(0x21), "heading", null, null, """{"text":{"":"{{ field.name }}'s notes"}}"""),
                            Field(Key(0x22), "paragraph", null, null, """{"html":{"":"<p>Hi <b>{{ field.name }}</b></p>"}}"""),
                            Field(Key(0x23), "text", "after", "After") with { Help = afterHelp is null ? null : new LocalizedText(afterHelp) },
                        ],
                    },
                ],
            },
        ],
    };

    private static FormDefinition Marked(FormDefinition definition, RequiredMarker marker, string? submit) => definition with
    {
        Settings = definition.Settings with
        {
            Appearance = definition.Settings.Appearance with { RequiredMarker = marker },
            Submission = definition.Settings.Submission with { SubmitLabel = submit is null ? null : new LocalizedText(submit) },
        },
    };

    private static FormField Field(Guid key, string type, string? alias, string? label, string? settings = null) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = label is null ? null : new LocalizedText(label),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    // What the server renders a visitor text for when the name holds an answer: the merge-tag service in the visitor-text
    // place, for the published form in the site's language, with the text's owner.
    private async Task<string> RenderedAsync(Guid form, Guid owner, string text, string name)
    {
        var cached = (await GetRequiredService<PublishedFormCache>().GetAsync(form, CancellationToken.None))!.Published;
        var scope = new MergeTagScope
        {
            Form = cached.Definition,
            FormName = cached.Form.Name,
            FormAlias = cached.Form.Alias,
            Culture = "en-US",
            TextOwner = owner,
            Answers = new MergeTagAnswers([new MergeTagAnswer(Name, [new FieldValue(name)])]),
        };
        return (await GetRequiredService<IFormBuilderMergeTagService>().RenderAsync(text, MergeTagPlace.VisitorText, MergeTagContext.Text, scope)).Text;
    }

    private static string Label(IElement form, string alias) =>
        (form.QuerySelector($"[data-jfb-alias={alias}] label") ?? throw new AssertionException($"no label of {alias}")).TextContent.Trim();

    // Each element that carries a text's hook, by its tag, with the hook.
    private static (string, string)[] Hooks(IElement form) =>
        [.. form.QuerySelectorAll("[data-jfb-text]").Select(element => (element.LocalName, element.GetAttribute("data-jfb-text")!))];

    // Each field's alias and the mark beside its label.
    private static (string, string)[] Marks(IElement form) =>
        [.. form.QuerySelectorAll("[data-jfb-field]").Select(field => (field.GetAttribute("data-jfb-alias")!, field.QuerySelector(".jfb-mark")?.TextContent ?? string.Empty))];
}
