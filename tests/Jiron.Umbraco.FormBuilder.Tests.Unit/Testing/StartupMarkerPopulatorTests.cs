using Examine;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Examine;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The start-up marker (ROADMAP 3.4c): it logs once the populators before it have run, only as the last populator, and
/// only for Umbraco's standard indexes.
/// </summary>
[TestFixture]
public sealed class StartupMarkerPopulatorTests
{
    [TestCase(Constants.UmbracoIndexes.InternalIndexName)]
    [TestCase(Constants.UmbracoIndexes.ExternalIndexName)]
    [TestCase(Constants.UmbracoIndexes.MembersIndexName)]
    [TestCase(Constants.UmbracoIndexes.DeliveryApiContentIndexName)]
    public void IsRegistered_UmbracosStandardIndex_IsTrue(string name)
    {
        var (marker, _) = Create(last: true);

        Assert.That(marker.IsRegistered(Index(name)), Is.True);
    }

    [Test]
    public void IsRegistered_AnotherIndex_IsFalse()
    {
        var (marker, _) = Create(last: true);

        Assert.That(marker.IsRegistered(Index("JfbEntriesIndex")), Is.False);
    }

    [Test]
    public void Populate_AsTheLastPopulator_LogsTheMarkerWithTheRegisteredIndexes()
    {
        var (marker, logger) = Create(last: true);

        marker.Populate(Index(Constants.UmbracoIndexes.InternalIndexName), Index("JfbEntriesIndex"), Index(Constants.UmbracoIndexes.DeliveryApiContentIndexName));

        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entry.Message, Is.EqualTo($"{StartupMarkerPopulator.Marker} (InternalIndex, DeliveryApiContentIndex)"));
        });
    }

    [Test]
    public void Populate_WithAnotherPopulatorAfterIt_LogsWhyInsteadOfTheMarker()
    {
        var (marker, logger) = Create(last: false);

        marker.Populate(Index(Constants.UmbracoIndexes.ExternalIndexName));

        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(entry.Message, Does.StartWith(StartupMarkerPopulator.NotLast));
            Assert.That(entry.Message, Does.Not.Contain(StartupMarkerPopulator.Marker));
        });
    }

    [Test]
    public void Populate_NoRegisteredIndex_LogsNothing()
    {
        var (marker, logger) = Create(last: true);

        marker.Populate(Index("JfbEntriesIndex"));

        Assert.That(logger.Entries, Is.Empty);
    }

    /// <summary>The marker in a container of populators, either last or followed by another one.</summary>
    private static (StartupMarkerPopulator Marker, ListLogger<StartupMarkerPopulator> Logger) Create(bool last)
    {
        var logger = new ListLogger<StartupMarkerPopulator>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<StartupMarkerPopulator>>(logger);
        services.AddSingleton<IIndexPopulator, OtherPopulator>();
        services.AddSingleton<IIndexPopulator, StartupMarkerPopulator>();
        if (!last)
        {
            services.AddSingleton<IIndexPopulator, OtherPopulator>();
        }

        var provider = services.BuildServiceProvider();
        var marker = provider.GetServices<IIndexPopulator>().OfType<StartupMarkerPopulator>().Single();
        return (marker, logger);
    }

    private static IIndex Index(string name)
    {
        var index = Substitute.For<IIndex>();
        index.Name.Returns(name);
        return index;
    }

    private sealed class OtherPopulator : IndexPopulator
    {
        public OtherPopulator() => RegisterIndex(Constants.UmbracoIndexes.ExternalIndexName);

        protected override void PopulateIndexes(IReadOnlyList<IIndex> indexes)
        {
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
