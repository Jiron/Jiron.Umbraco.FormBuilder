using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A post without JavaScript that trickles (ROADMAP 7.4c, ADR-0006): past its time limit its reading ends, the page says nothing was
/// sent, and the post holds nothing more.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPostReadLimitTests : RenderingTestBase
{
    private const string Boundary = "jfb-test";
    private static readonly string Crlf = new([(char)13, (char)10]);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("71a00000-0000-4000-8000-000000000020"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
        Settings = SettingsObject.Parse("""{"types":["pdf"],"maxBytes":2048}"""),
    };

    private readonly ManualTimers _timers = new();

    [Test]
    public async Task APostThatTricklesPastItsLimit_EndsAsOneTooLarge()
    {
        _ = await PublishFormAsync("limitedPost", null, Attachment);
        _ = await PagesAsync("limited");
        using var page = await SendAsync(HttpMethod.Get, "/limited/");
        var form = (await new HtmlParser().ParseDocumentAsync(await page.Content.ReadAsStringAsync())).QuerySelector("form#jfb-1")!;
        var start = new StringBuilder();
        foreach (var (name, value) in Posted(form, FormPostFields.Submit))
        {
            start.Append("--").Append(Boundary).Append(Crlf).Append("Content-Disposition: form-data; name=\"").Append(name).Append('"').Append(Crlf).Append(Crlf).Append(value).Append(Crlf);
        }

        var body = new Trickle(Encoding.ASCII.GetBytes(start.ToString() + "--" + Boundary + Crlf + "Content-Disposition: form-data; name=\"attachment\"; filename=\"a.pdf\"" + Crlf + Crlf + "%PDF-1.7 "));
        body.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + Boundary);

        var sent = SendAsync(HttpMethod.Post, form.GetAttribute("action")!.Split('#')[0], request =>
        {
            request.Content = body;
            request.Headers.TryAddWithoutValidation("Origin", Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        });
        await body.Stalled.WaitAsync(Patience);
        var limit = await WaitForAsync(() => _timers.Timers.LastOrDefault(timer => !timer.Disposed && timer.DueTime == FormPostFilesMiddleware.ReadLimit));
        limit.Fire();
        using var answer = await sent.WaitAsync(Patience);
        var html = await answer.Content.ReadAsStringAsync();
        body.Release();

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), html);
            Assert.That((await new HtmlParser().ParseDocumentAsync(html)).QuerySelector("[data-jfb-notice='too_large']"), Is.Not.Null, html);
            Assert.That(limit.Disposed, Is.True, "the limit ends with its reading");
            Assert.That(FormPostFilesMiddleware.ReadLimit, Is.EqualTo(TimeSpan.FromMinutes(5)), "as long as an upload may take");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<TimeProvider>(_timers);
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> find)
        where T : class
    {
        var until = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < until)
        {
            if (find() is { } found)
            {
                return found;
            }

            await Task.Delay(50);
        }

        throw new AssertionException("It never came.");
    }

    // A body that sends its start and then waits, of no declared length.
    private sealed class Trickle(byte[] start) : HttpContent
    {
        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Stalled => _stalled.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(start);
            await stream.FlushAsync();
            _stalled.TrySetResult();
            await _release.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
