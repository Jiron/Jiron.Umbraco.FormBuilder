using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A post without JavaScript at the cap of what may wait for an entry (ROADMAP 7.4d, ADR-0148): nothing of it is stored, the page
/// comes again with its answers and a notice, a post that stores no file goes on as ever, and files are taken again once there is room.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPostNoRoomTests : RenderingTestBase
{
    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("74d00000-0000-4000-8000-000000000010"),
        Type = "text",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
    };

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("74d00000-0000-4000-8000-000000000011"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
    };

    private static readonly FormField Sign = new()
    {
        Key = Guid.Parse("74d00000-0000-4000-8000-000000000012"),
        Type = "signature",
        Alias = "sign",
        Label = new LocalizedText("Sign"),
    };

    private TestTimeProvider _clock = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task AtTheCap_APostWithAFile_StoresNothing_KeepsItsAnswers_AndGoesThroughOnceThereIsRoom()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        var waiting = await FillAsync(key);
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");

        using var refused = await PostAsync(form, withFile: true);
        var answered = await new HtmlParser().ParseDocumentAsync(await refused.Content.ReadAsStringAsync());
        var afterRefusal = await WaitingAsync();
        await Entries.DeleteFileAsync(waiting.Id, entryId: null);
        _clock.Advance(WaitingUploads.ReadEvery);
        using var later = await PostAsync(await FormAsync(), withFile: true, topic: "Ideas");

        Assert.Multiple(async () =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.OK), "as a post too large to read, which a proxy leaves alone");
            Assert.That(answered.QuerySelector("[data-jfb-notice], .jfb-notice")?.TextContent, Does.Contain("can't take files right now").And.Contain("Choose your files again"));
            Assert.That(answered.QuerySelector("input[name='topic']")?.GetAttribute("value"), Is.EqualTo("Ideas"), "every answer comes back");
            Assert.That(afterRefusal.Select(file => file.Id), Is.EqualTo(new[] { waiting.Id }), "nothing more stored");
            Assert.That(later.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(await CountEntriesAsync(key), Is.EqualTo(1), "only the post with room");
        });
    }

    [Test]
    public async Task AtTheCap_ATypedSignature_StoresNothing_AndAsksForNoFiles()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Sign);
        _ = await PagesAsync("limited");
        _ = await FillAsync(key);
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        Fill(form, "sign.typed", "Ada King");

        using var refused = await SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = new FormUrlEncodedContent(Posted(form, FormPostFields.Submit));
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        });
        var answered = await new HtmlParser().ParseDocumentAsync(await refused.Content.ReadAsStringAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(answered.QuerySelector("[data-jfb-notice], .jfb-notice")?.TextContent, Does.Contain("can't take files right now").And.Not.Contain("Choose your files again"));
            Assert.That(await WaitingAsync(), Has.Count.EqualTo(1), "nothing more stored");
            Assert.That(await CountEntriesAsync(key), Is.Zero);
        });
    }

    [Test]
    public async Task AtTheCap_APostThatStoresNoFile_GoesOn()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        _ = await FillAsync(key);
        var form = await FormAsync();

        using var sent = await PostAsync(form, withFile: false, topic: "Ideas");

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(await CountEntriesAsync(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task APostWhoseOwnFilesReachTheCap_ShowsTheNotice_NotAnErrorAtAField()
    {
        var two = Attachment with { Settings = SettingsObject.Parse("""{"types":["pdf"],"maxFiles":2}""") };
        var key = await PublishFormAsync("limitedPost", null, Topic, two);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Posted(form, FormPostFields.Submit))
        {
            content.Add(new StringContent(value), name);
        }

        content.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 the first")), "attachment", "a.pdf");
        content.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 the second")), "attachment", "b.pdf");

        using var post = await SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(Client.BaseAddress!, "/limited/");
        });
        var answered = await new HtmlParser().ParseDocumentAsync(await post.Content.ReadAsStringAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(answered.QuerySelector("[data-jfb-notice], .jfb-notice")?.TextContent, Does.Contain("can't take files right now"));
            Assert.That(answered.QuerySelector("#jfb-1-attachment-error"), Is.Null, "the file is not at fault");
            Assert.That(await WaitingAsync(), Is.Empty, "the second could not be held while it was read, so nothing of the post is kept (16.1)");
            Assert.That(await CountEntriesAsync(key), Is.Zero);
        });
    }

    [Test]
    public async Task AFileTheServerCannotHoldWhileItIsRead_IsLetGo_AndThePostStoresNothing()
    {
        // The share of one client, 1,000 bytes here, bounds what its posts hold in temporary files while they are read (16.1).
        var key = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Posted(form, FormPostFields.Submit))
        {
            content.Add(new StringContent(value), name);
        }

        var bytes = new byte[200_000];
        Encoding.ASCII.GetBytes("%PDF-1.7 a long one").CopyTo(bytes, 0);
        content.Add(new ByteArrayContent(bytes), "attachment", "long.pdf");
        using var post = await SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(Client.BaseAddress!, "/limited/");
        });
        var answered = await new HtmlParser().ParseDocumentAsync(await post.Content.ReadAsStringAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answered.QuerySelector("[data-jfb-notice], .jfb-notice")?.TextContent, Does.Contain("can't take files right now").And.Contain("Choose your files again"));
            Assert.That(answered.QuerySelector("input[name='topic']")?.GetAttribute("value"), Is.EqualTo("Ideas"), "every answer comes back");
            Assert.That(await WaitingAsync(), Is.Empty);
            Assert.That(await CountEntriesAsync(key), Is.Zero);
            Assert.That(GetRequiredService<FormPostBuffers>().Held, Is.Zero, "what the post held is given back with it");
        });
    }

    [Test]
    public async Task PastItsShare_APostWithAFile_IsToldToWait_AndKeepsItsAnswers()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        using var first = await PostAsync(await FormAsync(), withFile: true, topic: "Ideas");
        var form = await FormAsync();

        using var refused = await PostAsync(form, withFile: true, topic: "More ideas");
        var answered = await new HtmlParser().ParseDocumentAsync(await refused.Content.ReadAsStringAsync());
        using var withoutAFile = await PostAsync(await FormAsync(), withFile: false, topic: "No file");

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(refused.Headers.RetryAfter, Is.Not.Null);
            Assert.That(answered.QuerySelector("input[name='topic']")?.GetAttribute("value"), Is.EqualTo("More ideas"));
            Assert.That(answered.QuerySelector("[data-jfb-notice], .jfb-notice")?.TextContent, Does.Contain("Choose your files again"));
            Assert.That(withoutAFile.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a post that stores no file goes on");
            Assert.That(await CountEntriesAsync(key), Is.EqualTo(2));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        // The host's configuration stays from test to test, so each says both.
        var oneShare = TestContext.CurrentContext.Test.MethodName == nameof(PastItsShare_APostWithAFile_IsToldToWait_AndKeepsItsAnswers);
        var held = TestContext.CurrentContext.Test.MethodName == nameof(AFileTheServerCannotHoldWhileItIsRead_IsLetGo_AndThePostStoresNothing);
        configuration[FormBuilderOptions.SectionName + ":Files:MaxWaitingBytesPerAddress"] = oneShare ? "1048576" : held ? "1000" : "0";
        configuration[FormBuilderOptions.SectionName + ":Files:MaxWaitingBytes"] = oneShare || held ? "0" : "10";
        configuration[FormBuilderOptions.SectionName + ":Limits:Submit:PerAddressAndFormPerMinute"] = "0";
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        builder.Services.AddUnique<TimeProvider>(_clock);
    }

    // A file that fills the cap, and a new reading that sees it.
    private async Task<StoredEntryFile> FillAsync(Guid key)
    {
        var file = await Entries.AddFileAsync(EntryRepositoryTests.NewFile((await FormRows.GetAsync(key))!.Id));
        _clock.Advance(WaitingUploads.ReadEvery);
        return file!;
    }

    private async Task<IElement> FormAsync()
    {
        using var page = await SendAsync(HttpMethod.Get, "/limited/");
        return (await new HtmlParser().ParseDocumentAsync(await page.Content.ReadAsStringAsync())).QuerySelector("form#jfb-1")!;
    }

    private Task<HttpResponseMessage> PostAsync(IElement form, bool withFile, string? topic = null)
    {
        if (topic is not null)
        {
            Fill(form, "topic", topic);
        }

        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Posted(form, FormPostFields.Submit))
        {
            content.Add(new StringContent(value), name);
        }

        if (withFile)
        {
            content.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 a file")), "attachment", "a.pdf");
        }

        return SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(Client.BaseAddress!, "/limited/");
        });
    }

    private Task<IReadOnlyList<StoredEntryFile>> WaitingAsync() => Entries.ListOrphanFilesAsync(DateTimeOffset.UtcNow.AddDays(1), 100);

    private async Task<int> CountEntriesAsync(Guid key) => (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50)).Count;
}
