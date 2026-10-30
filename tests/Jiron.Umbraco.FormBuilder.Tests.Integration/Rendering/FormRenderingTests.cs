using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The view component and <c>&lt;jfb-render&gt;</c> on real pages (ROADMAP 6.2b, ADR-0093): the markup contract of
/// ADR-0015 that these views hold, the model data equal to the form endpoint's answer, instance ids per request, the
/// assets once, a form's own override, the context values, the ways a page names a form, a reference that names no
/// form, and a hidden field whose member value never reaches the page.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingTests : RenderingTestBase
{
    [TestCase("tag-helper")]
    [TestCase("component")]
    public async Task AForm_RendersWithTheHooksOfItsViews_AndTheModelDataIsTheFormEndpointsAnswer(string page)
    {
        var key = await PublishFormAsync("renderingContact");
        var pages = await PagesAsync(page);
        var (html, document) = await PageAsync(page);
        using var answer = await GetAsync($"{Root}/forms/{key}?contentKey={pages[page]}");
        var expected = await JsonAsync(answer);

        var form = Single(document, "form.jfb-form");
        var model = form.QuerySelectorAll("script[data-jfb-model]");
        var field = Single(document, "[data-jfb-field]");
        var input = Single(document, "input#jfb-1-name");
        Assert.Multiple(() =>
        {
            Assert.That(Attributes(form), Is.EqualTo(new Dictionary<string, string>
            {
                ["class"] = "jfb-form jfb-theme-default",
                ["id"] = "jfb-1",
                ["data-jfb-form"] = key.ToString("D"),
                ["data-jfb-revision"] = "1",
                ["data-jfb-api"] = "/jfb/api/v1",
                ["data-jfb-contract"] = "1",
                ["lang"] = "en-US",
                ["dir"] = "ltr",
                ["method"] = "post",
                ["action"] = $"/{page}/#jfb-1",
                ["novalidate"] = string.Empty,
            }), "the form element, without enctype for a form without files");
            Assert.That(model, Has.Length.EqualTo(1));
            Assert.That(model[0].GetAttribute("type"), Is.EqualTo("application/json"));
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(model[0].TextContent), expected), Is.True, $"the model data is the form endpoint's answer:\n{model[0].TextContent}\n{expected.ToJsonString()}");
            Assert.That(Single(document, "section.jfb-page").GetAttribute("data-jfb-page"), Is.Not.Empty);
            Assert.That(Single(document, "section.jfb-page").HasAttribute("hidden"), Is.False, "the current page shows");
            Assert.That(Single(document, ".jfb-group").GetAttribute("data-jfb-group"), Is.Not.Empty);
            Assert.That(Attributes(field), Is.EqualTo(new Dictionary<string, string>
            {
                ["class"] = "jfb-field jfb-field-text",
                ["data-jfb-field"] = NameField.Key.ToString("D"),
                ["data-jfb-alias"] = "name",
                ["data-jfb-type"] = "text",
            }), field.OuterHtml);
            Assert.That(Single(document, "label.jfb-label").GetAttribute("for"), Is.EqualTo("jfb-1-name"));
            Assert.That(Single(document, "label.jfb-label").TextContent, Is.EqualTo("Name (optional)"), "the default marker style marks optional fields");
            Assert.That(input.GetAttribute("name"), Is.EqualTo("name"));
            Assert.That(input.GetAttribute("class"), Is.EqualTo("jfb-control"));
            Assert.That(form.QuerySelectorAll("[data-jfb-live][aria-live=polite]"), Has.Length.EqualTo(1));
            Assert.That(Single(document, ".jfb-navigation button").OuterHtml, Is.EqualTo("""<button type="submit" class="jfb-button jfb-button-primary" name="jfb-nav" value="submit" data-jfb-nav="submit">Send</button>"""));
            Assert.That(document.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "the assets once");
            Assert.That(document.QuerySelector("script[type=module]")!.GetAttribute("src"), Does.StartWith("/jfb/assets/runtime/jfb-runtime.js?v="));
            Assert.That(Single(document, "#after").TextContent, Is.EqualTo("The page goes on."), "a page goes on after its form");
            Assert.That(html, Does.Not.Contain("<jfb-render"), "the element itself is not written");
        });
    }

    [Test]
    public async Task AForm_CarriesItsHoneypot_NamedForItsRevision_AndHiddenFromAssistiveTechnology()
    {
        var key = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("two-forms");
        _ = await PublishFormAsync("renderingSecond");
        var (_, document) = await PageAsync("two-forms");

        var forms = document.QuerySelectorAll("form.jfb-form");
        var honeypot = forms[0].QuerySelectorAll(".jfb-hp");
        var name = FormHoneypot.NameOf(key, 1);
        Assert.Multiple(() =>
        {
            Assert.That(honeypot, Has.Length.EqualTo(1));
            Assert.That(Attributes(honeypot[0]), Is.EqualTo(new Dictionary<string, string> { ["class"] = "jfb-hp", ["aria-hidden"] = "true" }));
            Assert.That(honeypot[0].NextElementSibling?.HasAttribute("data-jfb-live"), Is.True, "last, before the live region, after every field");
            Assert.That(honeypot[0].Children.Select(child => child.LocalName), Is.EqualTo(new[] { "label", "input" }));
            Assert.That(Attributes(honeypot[0].Children[0]), Is.EqualTo(new Dictionary<string, string> { ["for"] = "jfb-1-" + name }));
            Assert.That(honeypot[0].Children[0].TextContent, Is.EqualTo("Leave this field empty"));
            Assert.That(Attributes(honeypot[0].Children[1]), Is.EqualTo(new Dictionary<string, string>
            {
                ["type"] = "text",
                ["id"] = "jfb-1-" + name,
                ["name"] = name,
                ["tabindex"] = "-1",
                ["autocomplete"] = "off",
            }), "empty, out of the tab order and never filled in by the browser");
            Assert.That(forms[1].QuerySelector(".jfb-hp input")!.Id, Does.StartWith("jfb-2-"), "each form its own");
            Assert.That(forms[1].QuerySelector(".jfb-hp input")!.GetAttribute("name"), Is.Not.EqualTo(name), "each form a name of its own");
        });
    }

    [Test]
    public async Task TwoFormsOnOnePage_TakeTheirOwnInstanceIds_AndTheAssetsComeOnce_BeforeTheFirst()
    {
        var first = await PublishFormAsync("renderingContact");
        var second = await PublishFormAsync("renderingSecond");
        _ = await PagesAsync("two-forms");
        var (html, document) = await PageAsync("two-forms");

        var forms = document.QuerySelectorAll("form.jfb-form");
        Assert.Multiple(() =>
        {
            Assert.That(forms.Select(form => (form.Id, form.GetAttribute("data-jfb-form"))), Is.EqualTo(new[] { ("jfb-1", first.ToString("D")), ("jfb-2", second.ToString("D")) }));
            Assert.That(forms.Select(form => form.QuerySelector("input")!.Id), Is.EqualTo(new[] { "jfb-1-name", "jfb-2-name" }));
            Assert.That(forms.Select(form => form.GetAttribute("action")), Is.EqualTo(new[] { "/two-forms/#jfb-1", "/two-forms/#jfb-2" }));
            Assert.That(document.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1));
            Assert.That(html.IndexOf("type=\"module\"", StringComparison.Ordinal), Is.LessThan(html.IndexOf("<form", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task AJfbAssetsInTheLayoutsHead_WritesTheTags_AndTheFormAddsNone()
    {
        _ = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("head-assets");
        var (_, document) = await PageAsync("head-assets");
        Assert.Multiple(() =>
        {
            Assert.That(document.Head!.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "Razor runs the body before its layout, and the head still wins");
            Assert.That(document.Body!.QuerySelectorAll("script[type=module]"), Is.Empty);
        });
    }

    [Test]
    public async Task AFormWithoutItsAssets_WritesNoTags()
    {
        _ = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("no-assets");
        var (_, document) = await PageAsync("no-assets");
        Assert.Multiple(() =>
        {
            Assert.That(document.QuerySelectorAll("form.jfb-form"), Has.Length.EqualTo(1));
            Assert.That(document.QuerySelectorAll("script[type=module]"), Is.Empty);
        });
    }

    [Test]
    public async Task AViewInTheFormsOwnOverrideFolder_RendersInPlaceOfTheDefault_ForThatFormOnly()
    {
        _ = await PublishFormAsync("renderingOverride");
        _ = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("override");
        var (_, document) = await PageAsync("override");

        var forms = document.QuerySelectorAll("form.jfb-form");
        Assert.Multiple(() =>
        {
            Assert.That(forms, Has.Length.EqualTo(2));
            Assert.That(forms[0].QuerySelectorAll("[data-test-override=field]"), Has.Length.EqualTo(1), "the override");
            Assert.That(forms[0].QuerySelector("[data-test-override=field] input#jfb-1-name"), Is.Not.Null, "with the built-in control inside");
            Assert.That(forms[1].QuerySelectorAll("[data-test-override]"), Is.Empty, "the other form keeps the built-in view");
            Assert.That(forms[1].QuerySelectorAll(".jfb-field-text"), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public async Task AFormWithAFileField_PostsMultipart_AndAControlInItsOverrideFolder_WritesThatControl()
    {
        _ = await PublishFormAsync("renderingFiles", null, NameField, new FormField { Key = Guid.Parse("30000000-0000-4000-8000-0000000062f1"), Type = "file", Alias = "cv", Label = new LocalizedText("Your CV") });
        _ = await PagesAsync("files");
        var (_, document) = await PageAsync("files");

        var form = Single(document, "form.jfb-form");
        Assert.Multiple(() =>
        {
            Assert.That(form.GetAttribute("enctype"), Is.EqualTo("multipart/form-data"));
            Assert.That(form.QuerySelector("[data-jfb-alias=cv] input[data-test-override=file]")?.OuterHtml, Is.EqualTo("""<input class="jfb-control" type="file" id="jfb-1-cv" name="cv" data-test-override="file">"""));
            Assert.That(form.QuerySelector("[data-jfb-alias=cv] label")?.GetAttribute("for"), Is.EqualTo("jfb-1-cv"), "the built-in field around it");
        });
    }

    [Test]
    public async Task AContextValueTheFormLists_Renders_AndOneItDoesNotListOrTooLong_RendersNothing_LoggedOnce()
    {
        _ = await PublishFormAsync("renderingContext", definition => definition with
        {
            Settings = definition.Settings with { Developer = definition.Settings.Developer with { ContextValues = ["Campaign", "note", "source"] } },
        });
        _ = await PagesAsync("context");
        var (html, document) = await PageAsync("context");
        _ = await PageAsync("context");

        var inputs = document.QuerySelectorAll("form.jfb-form input[type=hidden][name^='jfb-context-']");
        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("context value", StringComparison.Ordinal)).Select(entry => entry.Message).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(inputs.Select(input => (input.GetAttribute("name"), input.GetAttribute("value"))), Is.EqualTo(new[] { ("jfb-context-Campaign", "spring <2026> & \"co\" &amp; more"), ("jfb-context-source", "from spring <2026> & \"co\" &amp; more") }), "as the page's code gives them, under the name the form lists, in its letter case");
            Assert.That(html, Does.Not.Contain("not taken").And.Not.Contain("xxxx"));
            Assert.That(warnings, Is.EquivalentTo(new[]
            {
                "A page gives the context value 'unlisted', which the form 'renderingContext' does not list (settings.developer.contextValues); it is left out.",
                "A page gives the context value 'note' of the form 'renderingContext' longer than 2000 characters; it is left out.",
            }), "each once, however often the page renders");
        });
    }

    [Test]
    public async Task AHiddenFieldFromTheSignedInMember_NeverReachesThePage()
    {
        const string Secret = "jfb-secret-7q@example.test";
        var hidden = new FormField
        {
            Key = Guid.Parse("30000000-0000-4000-8000-000000000a93"),
            Type = "hidden",
            Alias = "memberEmail",
            Label = new LocalizedText("Member email"),
            Prefill = new FieldPrefill { Member = "email" },
        };
        _ = await PublishFormAsync("renderingMember", null, NameField, hidden);
        _ = await PagesAsync("member");
        await SignInMemberAsync(Secret);
        var (html, document) = await PageAsync("member");

        Assert.Multiple(() =>
        {
            Assert.That(Single(document, "#member").TextContent, Is.EqualTo(MemberName), "the member is signed in while the page renders");
            Assert.That(document.QuerySelectorAll("form.jfb-form [data-jfb-field]").Select(field => field.GetAttribute("data-jfb-alias")), Is.EqualTo(new[] { "name" }), "a hidden field renders no markup");
            Assert.That(html, Does.Not.Contain(Secret).IgnoreCase);
            Assert.That(html, Does.Not.Contain("jfb-secret").IgnoreCase);
        });
    }

    [Test]
    public async Task AReferenceThatNamesNoPublishedForm_RendersNothing_AndThePageGoesOn_LoggedOncePerReference()
    {
        _ = await PagesAsync("missing");
        var (html, document) = await PageAsync("missing");
        _ = await PageAsync("missing");

        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("is no published form", StringComparison.Ordinal)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(document.QuerySelectorAll(".jfb-form"), Is.Empty);
            Assert.That(document.QuerySelectorAll("script[type=module]"), Is.Empty, "no form, no assets");
            Assert.That(Single(document, "#after").TextContent, Is.EqualTo("The page goes on."));
            Assert.That(html, Does.Not.Contain("<jfb-render"));
            Assert.That(warnings.Select(entry => entry.Message), Is.EqualTo(new[] { "A page names the form 'renderingMissing', which is no published form; the page renders without it." }), "the empty reference is none");
        });
    }

    [Test]
    public async Task AFormNamedByItsKey_ByAPickedReference_OrByItsAliasInSpaces_Renders_AndOtherValuesRenderNothing_ATypeLoggedOnce()
    {
        var key = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("references");
        var (_, document) = await PageAsync("references");
        _ = await PageAsync("references");

        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("names", StringComparison.Ordinal)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(document.QuerySelectorAll("form.jfb-form").Select(form => (form.Id, form.GetAttribute("data-jfb-form"))), Is.EqualTo(new[]
            {
                ("jfb-1", key.ToString("D")),
                ("jfb-2", key.ToString("D")),
                ("jfb-3", key.ToString("D")),
                ("jfb-4", key.ToString("D")),
            }));
            Assert.That(document.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1));
            Assert.That(Single(document, "#after").TextContent, Is.EqualTo("The page goes on."));
            Assert.That(warnings.Select(entry => entry.Message), Is.EqualTo(new[] { "A page names a form by a value of type System.Int32, which names no form; give the form's key, its alias or a FormBuilderFormReference." }), "once per type; null and spaces are no reference");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    private static IElement Single(IHtmlDocument document, string selector)
    {
        var found = document.QuerySelectorAll(selector);
        Assert.That(found, Has.Length.EqualTo(1), selector);
        return found[0];
    }

    private static Dictionary<string, string> Attributes(IElement element) => element.Attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value);
}
