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
/// (ADR-0033). The forms, the requests, the pages and the models are left out, so a read of any of them would fail the
/// test.
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

        var rendered = await Rendering(migrations, logger).RenderAsync(new DefaultHttpContext(), form, null, CancellationToken.None);

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
            Assert.That(await rendering.RenderAsync(new DefaultHttpContext(), form, null, CancellationToken.None), Is.Null);
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
    public async Task WhileTheMigrationsArePending_NothingRendersForAVisitor_AndNoFormIsRead()
    {
        var migrations = new Migrations { Pending = true };
        var logger = new RecordingLogger<FormRendering>();
        var rendering = Rendering(migrations, logger);
        static DefaultHttpContext Visitor() => new();

        Assert.Multiple(async () =>
        {
            Assert.That(await rendering.RenderAsync(Visitor(), "contact", null, CancellationToken.None), Is.Null);
            Assert.That(await rendering.RenderAsync(Visitor(), Guid.NewGuid(), null, CancellationToken.None), Is.Null);
            Assert.That(await rendering.RenderAsync(Visitor(), new FormBuilderFormReference(Guid.NewGuid()), null, CancellationToken.None), Is.Null);
            Assert.That(logger.Entries, Is.Empty, "a pending migration is no missing form");
            Assert.That(migrations.Asked, Is.EqualTo(3));
        });
    }

    [Test]
    public void WithoutARequest_TheRenderIsRefused() =>
        Assert.ThrowsAsync<ArgumentNullException>(() => Rendering(new Migrations(), new RecordingLogger<FormRendering>()).RenderAsync(null!, "contact", null, CancellationToken.None));

    private static FormRendering Rendering(IFormBuilderMigrationState migrations, RecordingLogger<FormRendering> logger) =>
        new(forms: null!, requests: null!, pages: null!, migrations, Substitute.For<IUmbracoContextAccessor>(), models: null!, views: null!, BackOffice(), logger);

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
