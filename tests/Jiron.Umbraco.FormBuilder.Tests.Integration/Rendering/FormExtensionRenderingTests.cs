using System.Net;
using System.Text.Encodings.Web;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>What the tests' handler of <see cref="EntrySubmittingNotification"/> does.</summary>
internal sealed class SubmittingRule
{
    public Action<EntrySubmittingNotification>? Apply { get; set; }
}

/// <summary>A site's handler of <see cref="EntrySubmittingNotification"/>, for the tests.</summary>
internal sealed class SubmittingRuleHandler(SubmittingRule rule) : INotificationAsyncHandler<EntrySubmittingNotification>
{
    public Task HandleAsync(EntrySubmittingNotification notification, CancellationToken cancellationToken)
    {
        rule.Apply?.Invoke(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A site's code on the website (ROADMAP 15.1, ADR-0233): a handler's errors on a post without JavaScript, at a field in
/// its words and at the form first in the summary, and a form rendered by <see cref="IFormBuilderRenderer"/> from code as
/// the tag helper renders it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormExtensionRenderingTests : RenderingTestBase
{
    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("71c00000-0000-4000-8000-000000001510"),
        Type = "text",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
    };

    private SubmittingRule Rule => GetRequiredService<SubmittingRule>();

    [Test]
    public async Task AHandlersErrors_ShowOnAPostWithoutJavaScript_AtTheirFieldAndAtTheFormFirst()
    {
        _ = await PublishFormAsync("handled", null, Topic);
        _ = await PagesAsync("handled");
        Rule.Apply = notification =>
        {
            notification.AddFormError("We take no ideas on Sundays.");
            notification.AddError("topic", "Name a topic we know.");
        };
        var form = await FormAtAsync("/handled/");
        Fill(form, "topic", "Ideas");

        using var sent = await PostAsync(form, FormPostFields.Submit, "/handled/");
        var shown = (await new HtmlParser().ParseDocumentAsync(await sent.Content.ReadAsStringAsync())).QuerySelector("form.jfb-form")!;
        Rule.Apply = notification => notification.Cancel = true;
        var again = await FormAtAsync("/handled/");
        Fill(again, "topic", "Ideas");
        using var bare = await PostAsync(again, FormPostFields.Submit, "/handled/");
        var wordless = (await new HtmlParser().ParseDocumentAsync(await bare.Content.ReadAsStringAsync())).QuerySelector("form.jfb-form")!;

        var links = shown.QuerySelectorAll(".jfb-error-summary a").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(links.Select(link => link.TextContent.Trim()), Is.EqualTo(new[] { "We take no ideas on Sundays.", "Name a topic we know." }), "a handler's words show as written, as an editor's own message does");
            Assert.That(links.Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-topic", "#jfb-1-topic" }), "the form's error links to the first field");
            Assert.That(shown.QuerySelector("#jfb-1-topic-error")?.TextContent.Trim(), Is.EqualTo("Name a topic we know."));
            Assert.That(shown.QuerySelector("[name='topic']")!.GetAttribute("value"), Is.EqualTo("Ideas"), "the answers stay");
            Assert.That(wordless.QuerySelectorAll(".jfb-error-summary a").Select(link => link.TextContent.Trim()), Is.EqualTo(new[] { "Check this answer" }), "a cancel without words shows a check's own");
        });
    }

    [Test]
    public async Task CodeRendersAForm_AsTheTagHelperDoes_WithItsAssetsOnce()
    {
        var key = await PublishFormAsync("rendered", null, Topic);
        var renderer = GetRequiredService<IFormBuilderRenderer>();
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("localhost");
        http.Request.Path = "/fragment/";

        var first = Html((await renderer.RenderAsync(http, new FormBuilderRenderRequest("RENDERED")))!);
        var second = Html((await renderer.RenderAsync(http, new FormBuilderRenderRequest(key) { Assets = false }))!);
        var missing = await renderer.RenderAsync(http, new FormBuilderRenderRequest("nothing"));

        var form = (await new HtmlParser().ParseDocumentAsync(second)).QuerySelector("form.jfb-form");
        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("jfb-runtime").And.Contain("data-jfb-form"), "the assets, then the form");
            Assert.That(second, Does.Not.Contain("jfb-runtime"), "assets come once per request, and not when asked not to");
            Assert.That(form?.QuerySelector("[name='topic']"), Is.Not.Null);
            Assert.That(missing, Is.Null);
        });
    }

    private static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<SubmittingRule>();
        builder.AddNotificationAsyncHandler<EntrySubmittingNotification, SubmittingRuleHandler>();
    }
}
