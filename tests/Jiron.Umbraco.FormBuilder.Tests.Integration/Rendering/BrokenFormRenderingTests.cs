using System.Text.Encodings.Web;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// One broken form on a page breaks neither the page nor its other forms (ROADMAP 16.4, ADR-0237): the form renderingBroken
/// has a field view of its own that throws, as a site's broken override would.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class BrokenFormRenderingTests : RenderingTestBase
{
    [Test]
    public async Task AFormThatFailsWhileItRenders_LeavesNothingOfItself_AndThePageAndItsOtherFormsRenderWithTheirAssets()
    {
        _ = await PublishFormAsync("renderingBroken");
        var first = await PublishFormAsync("renderingContact");
        var second = await PublishFormAsync("renderingSecond");
        _ = await PagesAsync("broken");

        using var response = await GetAsync("/broken/");
        var html = await response.Content.ReadAsStringAsync();
        var document = await new HtmlParser().ParseDocumentAsync(html);

        var forms = document.QuerySelectorAll("form.jfb-form");
        Assert.Multiple(() =>
        {
            Assert.That(Succeeded(response), Is.True, html);
            Assert.That(forms.Select(form => form.GetAttribute("data-jfb-form")), Is.EqualTo(new[] { first.ToString("D"), second.ToString("D") }), "the forms that work, each way a page names one");
            Assert.That(forms.Select(form => form.QuerySelector("input[name='name']")), Has.All.Not.Null, "each in full");
            Assert.That(html, Does.Not.Contain("data-test-broken").And.Not.Contain("A view of this site that fails"), "nothing of the broken form reaches the page");
            Assert.That(document.QuerySelector("[data-jfb-broken]"), Is.Null, "a visitor sees no note");
            Assert.That(document.QuerySelector("#after")?.TextContent, Is.EqualTo("The page goes on."));
            Assert.That(document.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1), "the broken form, first, took no form's assets with it");
            Assert.That(html.IndexOf("type=\"module\"", StringComparison.Ordinal), Is.LessThan(html.IndexOf("<form", StringComparison.Ordinal)));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "no cache keeps the page without its form");
        });
    }

    [Test]
    public async Task ASignedInBackOfficeUser_SeesANoteInItsPlace_ThatNamesWhatWentWrong()
    {
        _ = await PublishFormAsync("renderingBroken");
        _ = await PublishFormAsync("renderingContact");
        _ = await PublishFormAsync("renderingSecond");
        _ = await PagesAsync("broken");
        await SignInBackOfficeAsync();

        var (_, document) = await PageAtAsync("localhost", "broken", secure: true);

        var notes = document.QuerySelectorAll("[data-jfb-broken]");
        Assert.Multiple(() =>
        {
            Assert.That(notes, Has.Length.EqualTo(2), "one for each place that names it");
            Assert.That(notes[0].QuerySelector("h2")?.TextContent, Is.EqualTo("This form couldn't be shown"));
            Assert.That(notes[0].QuerySelector("p")?.TextContent, Does.Contain("System.InvalidOperationException").And.Contain("Only signed-in backoffice users see this note."));
            Assert.That(notes[0].TextContent, Does.Not.Contain("A view of this site that fails"), "the type, never the message");
            Assert.That(document.QuerySelectorAll("form.jfb-form"), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public async Task CodeThatRendersABrokenForm_GetsNothing_AndThePageGoesOn()
    {
        _ = await PublishFormAsync("renderingBroken");
        var renderer = GetRequiredService<IFormBuilderRenderer>();
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("localhost");
        http.Request.Path = "/fragment/";

        var rendered = await renderer.RenderAsync(http, new FormBuilderRenderRequest("renderingBroken"));

        Assert.Multiple(() =>
        {
            Assert.That(rendered is null ? null : Html(rendered), Is.Null);
            Assert.That(http.Response.Headers.CacheControl.ToString(), Does.Contain("no-store"));
        });
    }

    private static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
