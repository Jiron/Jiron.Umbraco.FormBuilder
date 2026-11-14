using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// Save and continue later on real pages (ROADMAP 8.6b, ADR-0163): a form that offers it writes the control's template,
/// in the theme's classes and the form's own wording, and the notices of a link taken up with its other notices; a form
/// that stores no entries, one that does not offer it, and one with no page for a link to come back to write neither.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingResumeTests : RenderingTestBase
{
    [Test]
    public async Task AFormThatSavesForLater_WritesItsControl_AndTheNoticesOfALinkTakenUp_InItsOwnWording()
    {
        _ = await PublishFormAsync("renderingResume", definition => Resumable(definition, EntryStorage.Always, reworded: true));
        _ = await PublishFormAsync("renderingResumeUnstored", definition => Resumable(definition, EntryStorage.Never));
        _ = await PublishFormAsync("renderingResumeOff");
        _ = await PagesAsync("resume");
        var (_, document) = await PageAsync("resume");

        var forms = document.QuerySelectorAll("form.jfb-form");
        var offering = forms[0];
        var template = (IHtmlTemplateElement)(offering.QuerySelector(":scope > template[data-jfb-resume]") ?? throw new AssertionException("no control"));
        var parts = template.Content;
        var open = Find(parts, "button[data-jfb-save-open]");
        var panel = Find(parts, "[data-jfb-save]");
        var email = Find(parts, "input");
        var error = Find(panel, "#jfb-1-save-error");
        Assert.Multiple(() =>
        {
            Assert.That(Attributes(template), Is.EqualTo(new Dictionary<string, string>
            {
                ["data-jfb-resume"] = string.Empty,
                ["data-jfb-saving"] = "Saving your answers",
                ["data-jfb-invalid"] = "Enter an email address",
                ["data-jfb-failed"] = "Your answers could not be saved. Try again in a moment.",
            }), "what the saving says");
            Assert.That(template.PreviousElementSibling?.HasAttribute("data-jfb-templates"), Is.True, "right after the form's templates");
            Assert.That((open.GetAttribute("type"), open.ClassName, open.TextContent), Is.EqualTo(("button", "jfb-button jfb-button-link", "Keep it for later")), "the form's own wording");
            Assert.That(Attributes(panel), Is.EqualTo(new Dictionary<string, string>
            {
                ["class"] = "jfb-notice jfb-save",
                ["role"] = "group",
                ["aria-labelledby"] = "jfb-1-save-title",
                ["data-jfb-save"] = string.Empty,
            }), "a notice in the buttons' place, named by its heading");
            Assert.That((Find(panel, "h2").Id, Find(panel, "h2").TextContent), Is.EqualTo(("jfb-1-save-title", "Keep it for later")));
            Assert.That(Find(panel, ":scope > p").TextContent, Is.EqualTo("We email you a link back to this page, valid for 14 days. Files, consents and sensitive answers are not kept, so you add them again when you come back."), "what is kept, and how long, before the address");
            Assert.That(Find(panel, ".jfb-field > label").GetAttribute("for"), Is.EqualTo(email.Id));
            Assert.That(Attributes(email), Is.EqualTo(new Dictionary<string, string>
            {
                ["class"] = "jfb-control",
                ["type"] = "email",
                ["id"] = "jfb-1-save",
                ["autocomplete"] = "email",
                ["spellcheck"] = "false",
                ["aria-describedby"] = "jfb-1-save-error",
            }), "an address with no name: the form never sends it");
            Assert.That((error.ClassName, error.HasAttribute("hidden"), error.TextContent), Is.EqualTo(("jfb-error", true, string.Empty)), "the field's error, empty until it has one");
            Assert.That(panel.QuerySelectorAll(".jfb-navigation > button").Select(button => (button.GetAttribute("type"), button.ClassName, button.TextContent)), Is.EqualTo(new[]
            {
                ("button", "jfb-button jfb-button-primary", "Send the link"),
                ("button", "jfb-button", "Cancel"),
            }), "the panel's one primary action, while the form's buttons are out of sight");
        });

        var notices = ((IHtmlTemplateElement)offering.QuerySelector(":scope > template[data-jfb-templates]")!).Content;
        Assert.Multiple(() =>
        {
            foreach (var (code, heading, text) in new[]
            {
                ("resume_saved", "We have emailed you a link", string.Empty),
                ("resume_restored", "Welcome back", "Your answers are back, on the page where you left off. Add your files, consents and sensitive answers again: they are not kept with saved answers."),
                ("resume_expired", "This link no longer works", "Saved answers are kept for a limited time and only until the form is sent, so these are gone now. Fill in the form again below."),
            })
            {
                var notice = Find(notices, $"[data-jfb-notice={code}]");
                Assert.That(Attributes(notice), Is.EqualTo(new Dictionary<string, string>
                {
                    ["class"] = "jfb-notice",
                    ["data-jfb-notice"] = code,
                    ["role"] = "alert",
                    ["tabindex"] = "-1",
                    ["aria-labelledby"] = "jfb-1-notice-" + code,
                }), code);
                Assert.That((Find(notice, "h2").Id, Find(notice, "h2").TextContent, Find(notice, "p").TextContent), Is.EqualTo(("jfb-1-notice-" + code, heading, text)), code);
            }

            Assert.That(Find(notices, "[data-jfb-notice=resume_saved] p").GetAttribute("data-jfb-saved"), Is.EqualTo("It went to {0} and works until {1}. You can close this page now, or carry on."), "the address and the last day for the runtime to write");
        });
        Assert.Multiple(() =>
        {
            foreach (var other in new[] { forms[1], forms[2] })
            {
                Assert.That(other.QuerySelector(":scope > template[data-jfb-resume]"), Is.Null, other.Id);
                Assert.That(((IHtmlTemplateElement)other.QuerySelector(":scope > template[data-jfb-templates]")!).Content.QuerySelector("[data-jfb-notice^=resume_]"), Is.Null, other.Id);
            }
        });
    }

    [Test]
    public async Task AFormThatSavesForLater_OffersItOnlyWhereALinkCanComeBack_ToTheContentsPage()
    {
        var key = await PublishFormAsync("renderingResumeHeadless", definition => Resumable(definition, EntryStorage.Always));
        var page = (await PagesAsync("resume-headless"))["resume-headless"];

        using var pageless = await GetAsync($"{Root}/forms/{key}/render?instance=jfb-1&page=/resume-headless/");
        using var onPage = await GetAsync($"{Root}/forms/{key}/render?instance=jfb-1&page=/resume-headless/&contentKey={page:D}");
        var parser = new HtmlParser();
        var without = await parser.ParseDocumentAsync(await pageless.Content.ReadAsStringAsync());
        var with = await parser.ParseDocumentAsync(await onPage.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That((pageless.StatusCode, onPage.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That(without.QuerySelector("template[data-jfb-resume]"), Is.Null, "no page for its link to come back to");
            Assert.That(((IHtmlTemplateElement?)with.QuerySelector("template[data-jfb-resume]"))?.Content.QuerySelector("[data-jfb-save] > p")?.TextContent, Does.StartWith("We email you a link back to this page, valid for 1 day. "), "a link of one day says so");
            Assert.That(with.QuerySelector("form")!.GetAttribute("data-jfb-content"), Is.EqualTo(page.ToString("D")), "the page its saving names");
        });
    }

    private static FormDefinition Resumable(FormDefinition definition, EntryStorage store, bool reworded = false) => definition with
    {
        Settings = definition.Settings with
        {
            Submission = definition.Settings.Submission with
            {
                Store = store,
                Resume = new SaveAndResume { Days = reworded ? 14 : 1 },
                Texts = reworded
                    ? new Dictionary<string, IReadOnlyDictionary<string, string>> { ["form.saveLater"] = new Dictionary<string, string> { [string.Empty] = "Keep it for later" } }
                    : null,
            },
        },
    };

    private static IElement Find(INode root, string selector) =>
        (root is IParentNode parent ? parent.QuerySelector(selector) : null) ?? throw new AssertionException($"no {selector}");

    private static Dictionary<string, string> Attributes(IElement element) => element.Attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value);
}
