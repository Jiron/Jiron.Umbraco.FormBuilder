using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Picker;

/// <summary>
/// The output cache's tags of the pages that use a form, and their eviction (ADR-0004, ADR-0132), apart from Umbraco's
/// storage: a page whose forms cannot be read is tagged with every form's tag and logged, never kept from rendering; a
/// change to a form evicts its tag once, and a change to many forms, or one the refresher carries otherwise, every
/// form's; and with the output cache off, which leaves Umbraco's cache manager unregistered, nothing is evicted.
/// </summary>
[TestFixture]
public sealed class FormCacheTagsTests
{
    private static readonly Guid Form = Guid.Parse("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084");

    private static readonly Guid Other = Guid.Parse("2b9e4c10-7d3a-4f5e-8a61-0c4d2e9b7f13");

    [Test]
    public void TheTags_AreEveryFormsTag_AndOnePerForm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormCacheTags.AnyForm, Is.EqualTo("jfb-form"));
            Assert.That(FormCacheTags.Of(Form), Is.EqualTo("jfb-form-6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084"));
        });
    }

    [Test]
    public void APageWhoseFormsCannotBeRead_IsTaggedWithEveryFormsTag_AndLogged()
    {
        var scopes = Substitute.For<IScopeProvider>();
        var broken = new InvalidOperationException("JFB_TEST: the database is gone");
        scopes.CreateScope().ThrowsForAnyArgs(broken);
        var logger = new RecordingLogger();
        var page = Substitute.For<IPublishedContent>();
        page.Key.Returns(Form);

        var tags = new FormCacheTags(new PageForms(scopes), logger).GetTags(page);

        Assert.Multiple(() =>
        {
            Assert.That(tags, Is.EqualTo(new[] { FormCacheTags.AnyForm }));
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception)), Is.EqualTo(new[] { (LogLevel.Warning, (Exception?)broken) }));
        });
    }

    [Test]
    public async Task AChangeToForms_EvictsEachFormsTagOnce_AndAChangeToManyEveryFormsTag()
    {
        var cache = Substitute.For<IWebsiteOutputCacheManager>();
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IWebsiteOutputCacheManager)).Returns(cache);
        var eviction = new FormCacheEviction(services);

        await eviction.HandleAsync(new FormBuilderFormCacheRefresherNotification(new[] { new FormCacheChange(Form), new FormCacheChange(Other), new FormCacheChange(Form) }, MessageType.RefreshByPayload), CancellationToken.None);
        await eviction.HandleAsync(new FormBuilderFormCacheRefresherNotification(new[] { new FormCacheChange(Guid.Empty) }, MessageType.RefreshByPayload), CancellationToken.None);
        await eviction.HandleAsync(new FormBuilderFormCacheRefresherNotification(new object(), MessageType.RefreshAll), CancellationToken.None);

        Received.InOrder(() =>
        {
            cache.EvictByTagAsync(FormCacheTags.Of(Form), Arg.Any<CancellationToken>());
            cache.EvictByTagAsync(FormCacheTags.Of(Other), Arg.Any<CancellationToken>());
            cache.EvictByTagAsync(FormCacheTags.AnyForm, Arg.Any<CancellationToken>());
            cache.EvictByTagAsync(FormCacheTags.AnyForm, Arg.Any<CancellationToken>());
        });
        await cache.Received(4).EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void WithTheOutputCacheOff_AChangeEvictsNothing_AndFailsNothing()
    {
        var services = Substitute.For<IServiceProvider>();

        Assert.DoesNotThrowAsync(() => new FormCacheEviction(services).HandleAsync(new FormBuilderFormCacheRefresherNotification(new[] { new FormCacheChange(Form) }, MessageType.RefreshByPayload), CancellationToken.None));
        services.Received(1).GetService(typeof(IWebsiteOutputCacheManager));
    }

    private sealed class RecordingLogger : ILogger<FormCacheTags>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
