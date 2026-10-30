using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Web;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// What a page renders before it looks for a form (ADR-0093): nothing without a reference, nothing for a value of
/// another type (logged once per type), and nothing for a visitor while the migrations are pending, with no form read
/// (ADR-0033) and the page kept out of every cache (ADR-0104). The forms, the requests, the pages and the models are left
/// out, so a read of any of them would fail the test.
/// </summary>
[TestFixture]
public sealed class FormRenderingTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task WithoutAReference_NothingRenders_AndNothingIsAskedOrLogged(string? form)
    {
        var migrations = new Migrations();
        var logger = new RecordingLogger<FormRendering>();

        var rendered = await Rendering(migrations, logger).RenderAsync(new DefaultHttpContext(), form, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(rendered, Is.Null);
            Assert.That(migrations.Asked, Is.Zero);
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task AValueOfAnotherType_RendersNothing_AndEachTypeIsLoggedOnce()
    {
        var migrations = new Migrations();
        var logger = new RecordingLogger<FormRendering>();
        var rendering = Rendering(migrations, logger);

        foreach (var form in new object[] { 42, 7, 1.5m, 42 })
        {
            Assert.That(await rendering.RenderAsync(new DefaultHttpContext(), form, null, null, CancellationToken.None), Is.Null);
        }

        Assert.Multiple(() =>
        {
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Warning, "A page names a form by a value of type System.Int32, which names no form; give the form's key, its alias or a FormBuilderFormReference."),
                (LogLevel.Warning, "A page names a form by a value of type System.Decimal, which names no form; give the form's key, its alias or a FormBuilderFormReference."),
            }));
            Assert.That(migrations.Asked, Is.Zero);
        });
    }

    [Test]
    public async Task WhileTheMigrationsArePending_NothingRendersForAVisitor_NoFormIsRead_AndThePageIsNoStore()
    {
        var migrations = new Migrations { Pending = true };
        var logger = new RecordingLogger<FormRendering>();
        var rendering = Rendering(migrations, logger);
        var visitors = new[] { new DefaultHttpContext(), new DefaultHttpContext(), new DefaultHttpContext() };

        Assert.Multiple(async () =>
        {
            Assert.That(await rendering.RenderAsync(visitors[0], "contact", null, null, CancellationToken.None), Is.Null);
            Assert.That(await rendering.RenderAsync(visitors[1], Guid.NewGuid(), null, null, CancellationToken.None), Is.Null);
            Assert.That(await rendering.RenderAsync(visitors[2], new FormBuilderFormReference(Guid.NewGuid()), null, null, CancellationToken.None), Is.Null);
            Assert.That(logger.Entries, Is.Empty, "a pending migration is no missing form");
            Assert.That(migrations.Asked, Is.EqualTo(3));
            Assert.That(visitors.Select(visitor => visitor.Response.Headers.CacheControl.ToString()), Is.All.EqualTo("no-store"), "a backoffice user's page shows the note");
        });
    }

    [Test]
    public async Task WithoutAReference_AnyRequestsPageIsLeftAsItIs()
    {
        var post = new DefaultHttpContext();
        post.Request.Method = HttpMethods.Post;
        post.Request.QueryString = new QueryString("?jfb-sent=x");

        Assert.Multiple(async () =>
        {
            Assert.That(await Rendering(new Migrations { Pending = true }, new RecordingLogger<FormRendering>()).RenderAsync(post, null, null, null, CancellationToken.None), Is.Null);
            Assert.That(post.Response.Headers.ContainsKey("Cache-Control"), Is.False, "nothing renders");
        });
    }

    [Test]
    public void WithoutARequest_TheRenderIsRefused() =>
        Assert.ThrowsAsync<ArgumentNullException>(() => Rendering(new Migrations(), new RecordingLogger<FormRendering>()).RenderAsync(null!, "contact", null, null, CancellationToken.None));

    private static FormRendering Rendering(IFormBuilderMigrationState migrations, RecordingLogger<FormRendering> logger) =>
        new(forms: null!, requests: null!, pages: null!, migrations, Substitute.For<IUmbracoContextAccessor>(), models: null!, views: null!, BackOffice(), submissions: null!, sent: null!, copies: null!, themes: null!, logger: logger);

    // The backoffice's cookie options, which a visitor's request carries no cookie of.
    private static BackOfficeVisitors BackOffice()
    {
        var options = Substitute.For<IOptionsMonitor<CookieAuthenticationOptions>>();
        options.Get(Constants.Security.BackOfficeAuthenticationType).Returns(new CookieAuthenticationOptions { Cookie = { Name = "UMB_UCONTEXT" } });
        return new BackOfficeVisitors(options, TimeProvider.System);
    }

    private sealed class Migrations : IFormBuilderMigrationState
    {
        public bool Pending { get; init; }

        public int Asked { get; private set; }

        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default)
        {
            Asked++;
            return ValueTask.FromResult(Pending);
        }

        public void Invalidate()
        {
        }
    }
}
