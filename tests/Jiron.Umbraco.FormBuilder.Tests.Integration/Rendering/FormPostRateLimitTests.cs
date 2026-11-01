using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A post without JavaScript over a rate limit (ROADMAP 7.1a, ADR-0012, ADR-0137): the page comes again with <c>429</c>,
/// <c>Retry-After</c>, every answer and a notice that says when to send again, and stores nothing; a flood gets a bare
/// <c>429</c> that costs no render; a button that only moves the visitor counts as a read; and a post from another origin
/// to a form for members is refused before anything is rendered.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPostRateLimitTests : RenderingTestBase
{
    private const string Limits = FormBuilderOptions.SectionName + ":Limits:";

    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("71a00000-0000-4000-8000-000000000010"),
        Type = "text",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
        Required = true,
    };

    private static readonly FormField Note = new()
    {
        Key = Guid.Parse("71a00000-0000-4000-8000-000000000011"),
        Type = "text",
        Alias = "note",
        Label = new LocalizedText("Note"),
    };

    private static readonly FormField Guests = new()
    {
        Key = Guid.Parse("71a00000-0000-4000-8000-000000000012"),
        Type = "repeater",
        Alias = "guests",
        Label = new LocalizedText("Guests"),
        Settings = SettingsObject.Parse("""{"maxRows":9}"""),
        Fields =
        [
            new FormField { Key = Guid.Parse("71a00000-0000-4000-8000-000000000013"), Type = "text", Alias = "guest", Label = new LocalizedText("Guest") },
        ],
    };

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("71a00000-0000-4000-8000-000000000014"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
        Settings = SettingsObject.Parse("""{"types":["pdf"],"maxBytes":2048}"""),
    };

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task APostOverTheLimit_ComesBackWith429_ItsAnswers_AndWhenToSendAgain_AndStoresNothing()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Note);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        Fill(form, "note", "A note");

        using var first = await SubmitAsync(form, FormPostFields.Submit);
        using var second = await SubmitAsync(form, FormPostFields.Submit);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "the first two go through");
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var stored = await CountEntriesAsync(key);

        using var refused = await SubmitAsync(form, FormPostFields.Submit);
        var html = await refused.Content.ReadAsStringAsync();
        var page = await new HtmlParser().ParseDocumentAsync(html);
        var again = page.QuerySelector("form#jfb-1")!;
        var notice = page.QuerySelector(".jfb-notice-error");
        var wait = refused.Headers.RetryAfter!.Delta!.Value;

        Assert.Multiple(async () =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), html);
            Assert.That(wait, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(refused.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(notice?.TextContent, Does.Contain("seconds").And.Contain("Your answers are kept"), html);
            Assert.That(again.QuerySelector("input[name='topic']")!.GetAttribute("value"), Is.EqualTo("Ideas"));
            Assert.That(again.QuerySelector("input[name='note']")!.GetAttribute("value"), Is.EqualTo("A note"));
            Assert.That(await CountEntriesAsync(key), Is.EqualTo(stored), "the refused post stored nothing");
        });
    }

    [Test]
    public async Task AFlood_GetsABare429_ThatRendersNothing()
    {
        _ = await PublishFormAsync("limitedPost", null, Topic);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        var answers = new List<HttpResponseMessage>();
        foreach (var _ in Enumerable.Range(0, 21))
        {
            answers.Add(await SubmitAsync(form, FormPostFields.Submit));
        }

        var last = answers[^1];
        var body = await last.Content.ReadAsStringAsync();
        var rendered = await answers[10].Content.ReadAsStringAsync();
        answers.ForEach(answer => answer.Dispose());

        Assert.Multiple(() =>
        {
            Assert.That(rendered, Does.Contain("jfb-notice-error"), "a little over the limit still renders");
            Assert.That(last.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(last.Headers.RetryAfter, Is.Not.Null);
            Assert.That(body, Does.Not.Contain("Ideas"));
            Assert.That(body, Does.Not.Contain("<form"));
        });
    }

    [Test]
    public async Task AButtonThatOnlyMovesTheVisitor_CountsAsARead_NotAsASubmission()
    {
        _ = await PublishFormAsync("limitedPost", null, Topic, Guests);
        _ = await PagesAsync("limited");
        var form = await FormAsync();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            using var added = await SubmitAsync(form, FormPostFields.Add + ":guests");
            Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a row's button is not held to the sending limit");
        }

        Fill(form, "topic", "Ideas");
        using var sent = await SubmitAsync(form, FormPostFields.Submit);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "and uses up none of it");
    }

    [Test]
    public async Task APostAskingToCheckAPage_CountsAsASubmission()
    {
        _ = await PublishFormAsync("limitedPost", definition => definition with
        {
            Pages =
            [
                new FormPage { Key = Guid.Parse("71a00000-0000-4000-8000-000000000020"), Groups = [new FormGroup { Key = Guid.Parse("71a00000-0000-4000-8000-000000000021"), Fields = [Topic] }] },
                new FormPage { Key = Guid.Parse("71a00000-0000-4000-8000-000000000022"), Groups = [new FormGroup { Key = Guid.Parse("71a00000-0000-4000-8000-000000000023"), Fields = [Note] }] },
            ],
        });
        _ = await PagesAsync("limited");
        var form = await FormAsync();

        using var one = await SubmitAsync(form, FormPostFields.Next);
        using var two = await SubmitAsync(form, FormPostFields.Next);
        using var three = await SubmitAsync(form, FormPostFields.Next);

        Assert.That(three.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task APostFromAnotherOrigin_ToAFormForMembers_IsRefused_BeforeAnythingIsRendered()
    {
        var key = await PublishFormAsync("limitedPost", definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        }, Topic);
        _ = await PublishFormAsync("limitedOpen", null, Note);
        _ = await PagesAsync("limited");
        var carrier = await FormAsync("jfb-2");
        var posts = new List<HttpStatusCode>();
        foreach (var _ in Enumerable.Range(0, 3))
        {
            using var post = await PostFormAsync(carrier, key, "https://elsewhere.example");
            posts.Add(post.StatusCode);
        }

        Assert.That(posts, Is.All.EqualTo(HttpStatusCode.Forbidden), "over the limit too: a 403, never the page");
    }

    [Test]
    public async Task APostWithFiles_CountsOnce_AndOverTheLimit_StoresNothing_AndSaysToChooseThemAgain()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Assert.That(form.GetAttribute("action"), Does.Contain(FormPostFields.PostMarker), "its post names its form in its address");
        Fill(form, "topic", "Ideas");

        using var first = await SubmitFilesAsync(form);
        using var second = await SubmitFilesAsync(form);
        var stored = await CountEntriesAsync(key);
        var files = GetRequiredService<MemoryFileStore>().Ids.Count;

        using var refused = await SubmitFilesAsync(form);
        var html = await refused.Content.ReadAsStringAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "counted once, or the limit of two would refuse the second");
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(html, Does.Contain("Choose your files again"));
            Assert.That(await CountEntriesAsync(key), Is.EqualTo(stored));
            Assert.That(GetRequiredService<MemoryFileStore>().Ids.Count, Is.EqualTo(files), "the refused post stored no file");
        });
    }

    [Test]
    public async Task AFloodOfPostsWithFiles_GetsABare429_BeforeItIsRead()
    {
        _ = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        foreach (var _ in Enumerable.Range(0, 20))
        {
            using var post = await SubmitFilesAsync(form);
        }

        using var flood = await SubmitFilesAsync(form);
        var body = await flood.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(flood.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(flood.Headers.RetryAfter, Is.Not.Null);
            Assert.That(flood.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(flood.Headers.ConnectionClose, Is.True, "turned away before its body was read, so the rest of it never is");
            Assert.That(body, Is.Empty);
        });
    }

    [Test]
    public async Task ALongWait_IsSaidInMinutes()
    {
        _ = await PublishFormAsync("limitedPost", null, Topic);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        using var first = await SubmitAsync(form, FormPostFields.Submit);

        using var refused = await SubmitAsync(form, FormPostFields.Submit);
        var html = await refused.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(html, Does.Contain("minutes, then send it again"));
            Assert.That(html, Does.Not.Contain("Choose your files again"), "this post held none");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration[Limits + "Submit:PerAddressPerMinute"] = "0";
        configuration[Limits + "Submit:PerAddressPerHour"] = "0";
        configuration[Limits + "Submit:PerAddressAndFormPerMinute"] = "2";
        configuration[Limits + "Submit:PerFormPerMinute"] = "0";
        configuration[Limits + "Submit:SitePerSecond"] = "0";
        if (TestContext.CurrentContext.Test.MethodName == nameof(ALongWait_IsSaidInMinutes))
        {
            configuration[Limits + "Submit:PerAddressAndFormPerMinute"] = "0";
            configuration[Limits + "Submit:PerAddressPerHour"] = "1";
        }
    }

    private async Task<IElement> FormAsync(string instance = "jfb-1")
    {
        using var page = await SendAsync(HttpMethod.Get, "/limited/");
        var html = await page.Content.ReadAsStringAsync();
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK), html);
        return (await new HtmlParser().ParseDocumentAsync(html)).QuerySelector($"form#{instance}") ?? throw new AssertionException("no form in: " + html);
    }

    private Task<HttpResponseMessage> SubmitAsync(IElement form, string? nav) =>
        SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = new FormUrlEncodedContent(Posted(form, nav));
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(Client.BaseAddress!, "/limited/");
        });

    // Posts the form as a browser without JavaScript does with a file: multipart, to the address that names the form.
    private Task<HttpResponseMessage> SubmitFilesAsync(IElement form)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Posted(form, FormPostFields.Submit))
        {
            content.Add(new StringContent(value), name);
        }

        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 a file"));
        file.Headers.TryAddWithoutValidation("Content-Disposition", "form-data; name=\"attachment\"; filename=\"a.pdf\"");
        content.Add(file);
        return SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(Client.BaseAddress!, "/limited/");
        });
    }

    // A form for members shows a visitor no form, so its post carries the route token of the page's other form.
    private Task<HttpResponseMessage> PostFormAsync(IElement carrier, Guid key, string origin) =>
        SendAsync(HttpMethod.Post, "/limited/", request =>
        {
            request.Content = new FormUrlEncodedContent(
            [
                new(FormPostFields.RouteToken, carrier.QuerySelector("input[name='ufprt']")!.GetAttribute("value")!),
                new(FormPostFields.Form, key.ToString("D")),
                new(FormPostFields.Instance, "jfb-1"),
                new(FormPostFields.Revision, "1"),
                new(FormPostFields.Nav, FormPostFields.Submit),
            ]);
            request.Headers.TryAddWithoutValidation("Origin", origin);
        });

    private async Task<int> CountEntriesAsync(Guid key) => (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50)).Count;
}
