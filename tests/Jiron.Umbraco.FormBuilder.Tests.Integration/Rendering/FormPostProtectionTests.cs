using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The spam score of a post without JavaScript (ROADMAP 7.1c, ADR-0012, ADR-0139): it has no challenge, so no time signal
/// however fast it comes back, and is scored on the honeypot, the blocked ranges and the content rules; and in Spam it leads to the same page
/// as any other post.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPostProtectionTests : RenderingTestBase
{
    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("71c00000-0000-4000-8000-000000000010"),
        Type = "text",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
    };

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("71c00000-0000-4000-8000-000000000011"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
        Settings = SettingsObject.Parse("""{"types":["pdf"],"maxBytes":2048}"""),
    };

    private string? _address;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [SetUp]
    public void NoAddress() => _address = null;

    [Test]
    public async Task APostAtOnce_HasNoTimeSignal()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(entry.State, Is.EqualTo(EntryState.Approved));
            Assert.That(entry.SpamScore, Is.Zero);
            Assert.That(entry.SpamReasons, Is.Empty);
        });
    }

    [Test]
    public async Task AFilledHoneypot_GoesToSpam_AndLeadsWhereAnyPostLeads()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        Fill(form, FormHoneypot.NameOf(key, 1), "Buy now");

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "as a post that is no spam");
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamScore, Is.EqualTo(100));
            Assert.That(entry.SpamReasons, Is.EqualTo("honeypot"));
        });
    }

    [Test]
    public async Task AFilledHoneypot_InAPostWithFiles_GoesToSpam()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic, Attachment);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        Fill(form, FormHoneypot.NameOf(key, 1), "Buy now");

        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Posted(form, FormPostFields.Submit))
        {
            content.Add(new StringContent(value), name);
        }

        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 a file"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"attachment\"", FileName = "\"a.pdf\"" };
        content.Add(file);
        using var sent = await SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            request.Headers.Referrer = new Uri(Client.BaseAddress!, "/limited/");
        });

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamReasons, Is.EqualTo("honeypot"));
        });
    }

    [Test]
    public async Task TheContentRules_HoldForAPost()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic);
        _ = await PagesAsync("limited");
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>();
        await settings.SaveAsync(current.Value with { BlockedWords = ["casino"] }, current.Version, null);
        var form = await FormAsync();
        Fill(form, "topic", "A casino");

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.EqualTo(40));
            Assert.That(entry.SpamReasons, Is.EqualTo("blocked_word"));
        });
    }

    [Test]
    public async Task OnAFormWithACaptcha_APost_HasNoTokenToBeRefusedFor_AndGoesToSpam()
    {
        var key = await PublishFormAsync(
            "limitedPost",
            definition => definition with { Settings = definition.Settings with { Protection = definition.Settings.Protection with { Level = ProtectionLevel.Captcha, Captcha = new CaptchaChoice { Provider = HcaptchaProvider.ChosenAs } } } },
            Topic);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamReasons, Is.EqualTo("no_script"));
        });
    }

    [Test]
    public async Task OnAFormWhoseProtectionNeedsJavaScript_APost_GoesToSpam_AndIsNeverLost()
    {
        var key = await PublishFormAsync(
            "limitedPost",
            definition => definition with { Settings = definition.Settings with { Protection = definition.Settings.Protection with { Level = ProtectionLevel.ProofOfWork } } },
            Topic);
        _ = await PagesAsync("limited");
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamScore, Is.EqualTo(100));
            Assert.That(entry.SpamReasons, Is.EqualTo("no_script"));
        });
    }

    [Test]
    public async Task APostOnAPageThatDoesNotHoldTheForm_IsHeldToTheStrictestSite_AndOneThatHoldsIt_ToItsOwn()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic);
        var pages = await PagesAsync("limited");
        await GetRequiredService<IFormBuilderSettingsService>().SaveSiteAsync(Guid.NewGuid(), new ProtectionSettings { Level = ProtectionLevel.ProofOfWork }, ["level"], 0, null);
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        await GetRequiredService<IReferenceRepository>().ReplaceContentAsync(pages["limited"], [new ContentFormReference(key, "form", string.Empty)]);
        var held = await FormAsync();
        Fill(held, "topic", "Ideas");
        using var sentHeld = await SubmitAsync(held);
        var entries = await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10);
        var newest = await Entries.GetByIdAsync(entries.Max());
        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamReasons, Is.EqualTo("no_script"));
            Assert.That(newest!.State, Is.EqualTo(EntryState.Approved), "its page's site asks for nothing a post without JavaScript cannot do");
        });
    }

    [Test]
    public async Task ABlockedAddress_GoesToSpam()
    {
        var key = await PublishFormAsync("limitedPost", null, Topic);
        _ = await PagesAsync("limited");
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>();
        await settings.SaveAsync(current.Value with { BlockedRanges = ["2001:db8::/32"] }, current.Version, null);
        var form = await FormAsync();
        Fill(form, "topic", "Ideas");
        _address = "2001:db8::7";

        using var sent = await SubmitAsync(form);

        var entry = await OnlyAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamReasons, Is.EqualTo("blocked_range"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<IStartupFilter>(new AddressFilter(() => _address));
    }

    private async Task<IElement> FormAsync()
    {
        using var page = await SendAsync(HttpMethod.Get, "/limited/");
        var html = await page.Content.ReadAsStringAsync();
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK), html);
        return (await new HtmlParser().ParseDocumentAsync(html)).QuerySelector("form#jfb-1") ?? throw new AssertionException("no form in: " + html);
    }

    private Task<HttpResponseMessage> SubmitAsync(IElement form) => PostAsync(form, FormPostFields.Submit, "/limited/");

    private async Task<StoredEntry> OnlyAsync(Guid key)
    {
        var id = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50)).Single();
        return (await Entries.GetByIdAsync(id))!;
    }

    // Gives a request the address a test names, as the connection would.
    private sealed class AddressFilter(Func<string?> address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, go) =>
            {
                if (address() is { } named)
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(named);
                }

                return go();
            });
            next(app);
        };
    }
}
