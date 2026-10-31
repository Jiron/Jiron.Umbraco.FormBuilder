using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Picker;

/// <summary>
/// "Used on" (ADR-0017, ADR-0131) apart from Umbraco's storage: the picker reports one reference per picked form under
/// the UDI type <c>jfb-form</c>; the content handler writes each item's rows, removes a deleted item's, and logs a write
/// that fails instead of failing the editor's save; the rebuild walks the documents batch by batch from where its
/// request says, ends with the rows of deleted content, and stops at its deadline; and its job runs only while Umbraco
/// runs and no migration is pending, and logs a failure.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FormReferencesTests
{
    private static readonly Guid Form = Guid.Parse("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084");

    private static readonly Guid Other = Guid.Parse("2b9e4c10-7d3a-4f5e-8a61-0c4d2e9b7f13");

    private readonly IReferenceRepository _references = Substitute.For<IReferenceRepository>();

    private readonly MemoryKeyValues _values = new();

    private readonly IContentService _contents = Substitute.For<IContentService>();

    private readonly TestTimeProvider _clock = new();

    [Test]
    public void ThePicker_ReportsEachPickedForm_UnderTheFormsUdiType()
    {
        var picker = new FormPickerReferences();
        var editor = Substitute.For<IDataEditor>();
        editor.Alias.Returns(FormPickerPropertyEditor.EditorAlias);
        var other = Substitute.For<IDataEditor>();
        other.Alias.Returns("Umbraco.TextBox");
        FormPickerReferences.RegisterUdiType();
        FormPickerReferences.RegisterUdiType();

        Assert.Multiple(() =>
        {
            Assert.That((picker.IsForEditor(editor), picker.IsForEditor(other), picker.IsForEditor(null)), Is.EqualTo((true, false, false)));
            Assert.That(picker.GetDataValueReference(), Is.SameAs(picker));
            Assert.That(picker.GetReferences($"{Form:D},{Other:D}").Select(reference => reference.Udi.ToString()), Is.EqualTo(new[] { $"umb://jfb-form/{Form:N}", $"umb://jfb-form/{Other:N}" }));
            Assert.That(picker.GetReferences("not a key"), Is.Empty);
            Assert.That(picker.GetReferences(null), Is.Empty);
            Assert.That(UdiParser.Parse($"umb://jfb-form/{Form:N}"), Is.EqualTo(new GuidUdi(FormPickerReferences.UdiType, Form)), "registered once however often");
        });
    }

    [Test]
    public async Task TheHandler_WritesEachSavedItemsRows_AndRemovesADeletedItemsRows()
    {
        var first = Content();
        var second = Content();
        var handler = Handler(new RecordingLogger<FormReferenceHandler>());

        await handler.HandleAsync(new ContentSavedNotification([first, second], new EventMessages()), CancellationToken.None);
        await handler.HandleAsync(new ContentCopiedNotification(first, second, -1, null, false, new EventMessages()), CancellationToken.None);
        await handler.HandleAsync(new ContentDeletedNotification(first, new EventMessages()), CancellationToken.None);

        // The keys are read first: a substitute's property read inside the check is not a call to it.
        var (firstKey, secondKey) = (first.Key, second.Key);
        Received.InOrder(() =>
        {
            _references.ReplaceContentAsync(firstKey, Arg.Is<IReadOnlyCollection<ContentFormReference>>(rows => rows.Count == 0), Arg.Any<CancellationToken>());
            _references.ReplaceContentAsync(secondKey, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
            _references.ReplaceContentAsync(secondKey, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
            _references.DeleteContentAsync(firstKey, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AWriteThatFails_IsLogged_AndTheOtherItemsAreStillWritten()
    {
        var failing = Content();
        var next = Content();
        var logger = new RecordingLogger<FormReferenceHandler>();
        var broken = new InvalidOperationException("JFB_TEST: the table is locked");
        _references.ReplaceContentAsync(failing.Key, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>()).ThrowsAsync(broken);
        _references.DeleteContentAsync(failing.Key, Arg.Any<CancellationToken>()).ThrowsAsync(broken);

        await Handler(logger).HandleAsync(new ContentSavedNotification([failing, next], new EventMessages()), CancellationToken.None);
        await Handler(logger).HandleAsync(new ContentDeletedNotification(failing, new EventMessages()), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            await _references.Received(1).ReplaceContentAsync(next.Key, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception)), Is.EqualTo(new[] { (LogLevel.Warning, (Exception?)broken), (LogLevel.Warning, (Exception?)broken) }));
            Assert.That(logger.Entries.First().Message, Does.Contain(failing.Key.ToString()).And.Contain("rebuild"));
        });
    }

    [Test]
    public void ACancelledWrite_IsNotSwallowed()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var content = Content();
        _references.ReplaceContentAsync(content.Key, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(cancelled.Token));

        Assert.ThrowsAsync<OperationCanceledException>(() => Handler(new RecordingLogger<FormReferenceHandler>()).HandleAsync(new ContentSavedNotification(content, new EventMessages()), cancelled.Token));
    }

    [Test]
    public async Task TheRebuild_WalksTheDocumentsBatchByBatch_ThenRemovesTheRowsOfDeletedContent_AndEnds()
    {
        var pages = new[] { Content(), Content(), Content() };
        Walk((0, [3, 7]), (7, [9]), (9, []));
        _contents.GetByIds(Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 3, 7 }))).Returns([pages[0], pages[1]]);
        _contents.GetByIds(Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 9 }))).Returns([pages[2]]);
        _references.DeleteOrphansAsync(Arg.Any<CancellationToken>()).Returns(4);
        var rebuild = Rebuild();
        await rebuild.RequestAsync();
        var due = await rebuild.IsDueAsync();

        var done = await rebuild.ContinueAsync(DateTimeOffset.MaxValue);

        Assert.Multiple(async () =>
        {
            Assert.That((due, done), Is.EqualTo((true, true)));
            Assert.That(await rebuild.IsDueAsync(), Is.False, "the request is gone");
            Assert.That(_values.Written, Is.EqualTo(new[] { "0", "7", "9", FormReferenceRebuild.OrphansNext }), "where the walk goes on, after each batch");
            foreach (var page in pages)
            {
                await _references.Received(1).ReplaceContentAsync(page.Key, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
            }

            await _references.Received(1).DeleteOrphansAsync(Arg.Any<CancellationToken>());
            Assert.That(await rebuild.ContinueAsync(DateTimeOffset.MaxValue), Is.True, "nothing left to do");
        });
    }

    [Test]
    public async Task ARebuildPastItsDeadline_StopsAfterABatch_AndGoesOnFromThereNextTime()
    {
        Walk((0, [3]), (3, []));
        var page = Content();
        _contents.GetByIds(Arg.Any<IEnumerable<int>>()).Returns([page]);
        var rebuild = Rebuild();
        await rebuild.RequestAsync();

        var first = await rebuild.ContinueAsync(_clock.GetUtcNow());
        var stored = await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey);
        var second = await rebuild.ContinueAsync(_clock.GetUtcNow());

        Assert.Multiple(async () =>
        {
            Assert.That((first, stored), Is.EqualTo((false, "3")));
            Assert.That(second, Is.False, "the next batch was empty and the orphans are next");
            Assert.That(await rebuild.ContinueAsync(_clock.GetUtcNow()), Is.True);
            await _references.Received(1).DocumentsAfterAsync(0, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>());
            await _references.Received(1).DocumentsAfterAsync(3, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task ARequestThisVersionCannotRead_StartsTheWalkAgain()
    {
        Walk((0, []));
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, "from page 12");

        Assert.That(await Rebuild().ContinueAsync(DateTimeOffset.MaxValue), Is.True);
        await _references.Received(1).DocumentsAfterAsync(0, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>());
    }

    [TestCase(RuntimeLevel.Install, false)]
    [TestCase(RuntimeLevel.Upgrade, false)]
    [TestCase(RuntimeLevel.Run, true)]
    public async Task TheJob_RunsOnlyWhileUmbracoRuns_AndNoMigrationIsPending(RuntimeLevel level, bool pending)
    {
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, FormReferenceRebuild.OrphansNext);

        await Job(level, pending, new RecordingLogger<FormReferenceRebuildJob>()).ExecuteAsync(CancellationToken.None);

        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Is.EqualTo(FormReferenceRebuild.OrphansNext), "nothing done");
    }

    [Test]
    public async Task TheJob_RunsEveryMinute_GoesOnWithinItsBudget_AndLogsAFailure()
    {
        var logger = new RecordingLogger<FormReferenceRebuildJob>();
        var broken = new InvalidOperationException("JFB_TEST: the database is gone");
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, "0");
        _references.DocumentsAfterAsync(Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>()).ThrowsAsync(broken);
        var job = Job(RuntimeLevel.Run, pending: false, logger);

        await job.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That((job.Name, job.Period, FormReferenceRebuildJob.Budget), Is.EqualTo(("Jiron.FormBuilder.References", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(8))));
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception)), Is.EqualTo(new[] { (LogLevel.Error, (Exception?)broken) }));
        });
    }

    private static IContent Content()
    {
        var content = Substitute.For<IContent>();
        content.Key.Returns(Guid.NewGuid());
        content.Properties.Returns(new PropertyCollection());
        return content;
    }

    // The batches of document ids the walk reads after each id.
    private void Walk(params (int After, int[] Ids)[] batches)
    {
        foreach (var (after, ids) in batches)
        {
            _references.DocumentsAfterAsync(after, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>()).Returns(ids);
        }
    }

    private FormReferenceIndex Index() =>
        new(new DataValueReferenceFactoryCollection(() => [], NullLogger<DataValueReferenceFactoryCollection>.Instance), new PropertyEditorCollection(new DataEditorCollection(() => [])), _references);

    private FormReferenceHandler Handler(ILogger<FormReferenceHandler> logger) => new(Index(), logger);

    private FormReferenceRebuild Rebuild() => new(_references, _values, _contents, Index(), _clock, NullLogger<FormReferenceRebuild>.Instance);

    private FormReferenceRebuildJob Job(RuntimeLevel level, bool pending, ILogger<FormReferenceRebuildJob> logger)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        return new FormReferenceRebuildJob(runtime, new FixedMigrationState(pending), Rebuild(), _clock, logger);
    }

    private sealed class FixedMigrationState(bool pending) : IFormBuilderMigrationState
    {
        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(pending);

        public void Invalidate()
        {
        }
    }

    // jfbKeyValue in memory, with every value written in order.
    private sealed class MemoryKeyValues : IKeyValueRepository
    {
        private readonly Dictionary<string, string?> _stored = [];

        public List<string?> Written { get; } = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_stored.GetValueOrDefault(key));

        public Task<IReadOnlyList<KeyValuePair<string, string?>>> ListAsync(string prefix, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KeyValuePair<string, string?>>>([.. _stored.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))]);

        public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            _stored[key] = value;
            Written.Add(value);
            return Task.CompletedTask;
        }

        public Task<bool> TryAddAsync(string key, string? value, CancellationToken cancellationToken = default) => Task.FromResult(_stored.TryAdd(key, value));

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_stored.Remove(key));

        public Task<int> DeleteOlderAsync(string prefix, DateTime before, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
