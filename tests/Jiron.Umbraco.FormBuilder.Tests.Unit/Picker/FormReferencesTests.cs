using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
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
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception?.Message)), Is.EqualTo(new[] { (LogLevel.Warning, LoggedException.Of(broken).Message), (LogLevel.Warning, LoggedException.Of(broken).Message) }));
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
        var pages = new[] { Content(3), Content(7), Content(9) };
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
            Assert.That(_values.Written[0], Does.StartWith("0-"), "a request's own value");
            Assert.That(_values.Written.Skip(1), Is.EqualTo(new[] { "7", "9", FormReferenceRebuild.OrphansNext }), "where the walk goes on, after each batch");
            foreach (var page in pages)
            {
                await _references.Received(1).ReplaceUnchangedContentAsync(page.Id, Version(page.Id), page.Key, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
            }

            await _references.DidNotReceive().ReplaceContentAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());

            await _references.Received(1).DeleteOrphansAsync(Arg.Any<CancellationToken>());
            Assert.That(await rebuild.ContinueAsync(DateTimeOffset.MaxValue), Is.True, "nothing left to do");
        });
    }

    [Test]
    public async Task ARebuildPastItsDeadline_StopsAfterABatch_AndGoesOnFromThereNextTime()
    {
        Walk((0, [3]), (3, []));
        var page = Content(3);
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

    [Test]
    public async Task ACopyOfAnotherVersion_OrOfADocumentWithNoVersionLeft_IsNotWritten()
    {
        Walk((0, [3, 7]), (7, []));
        var older = Content(3);
        older.VersionId.Returns(999);
        var deleted = Content(7);
        _contents.GetByIds(Arg.Any<IEnumerable<int>>()).Returns([older, deleted]);
        _references.VersionsOfAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<int, DocumentVersion> { [3] = Version(3) });
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, "0");

        Assert.That(await Rebuild().ContinueAsync(DateTimeOffset.MaxValue), Is.True);
        await _references.DidNotReceive().ReplaceUnchangedContentAsync(Arg.Any<int>(), Arg.Any<DocumentVersion>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TwoRequests_HoldValuesOfTheirOwn_WhichReadAsNoPlace()
    {
        Walk((0, []));
        var rebuild = Rebuild();

        await rebuild.RequestAsync();
        await rebuild.RequestAsync();

        Assert.That(_values.Written[0], Is.Not.EqualTo(_values.Written[1]), "a walk at its start tells a new request from its own");
        Assert.That(await rebuild.ContinueAsync(DateTimeOffset.MaxValue), Is.True);
        await _references.Received(1).DocumentsAfterAsync(0, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>());
    }

    [Test]
    public void AFailedRequestAfterATypeChange_IsLogged_AndNeverFailsTheChange()
    {
        var values = Substitute.For<IKeyValueRepository>();
        var broken = new InvalidOperationException("JFB_TEST: the database is gone");
        values.SetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(broken);
        var logger = new RecordingLogger<FormReferenceTypeHandler>();
        var handler = new FormReferenceTypeHandler(new FormReferenceRebuild(_references, values, _contents, Index(), _clock, NullLogger<FormReferenceRebuild>.Instance), logger);

        Assert.DoesNotThrowAsync(() => handler.HandleAsync(new DataTypeDeletedNotification(Substitute.For<IDataType>(), new EventMessages()), CancellationToken.None));
        Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception?.Message)), Is.EqualTo(new[] { (LogLevel.Warning, LoggedException.Of(broken).Message) }));
    }

    [Test]
    public async Task ADocumentThatCannotBeRead_IsLogged_AndTheWalkGoesOnToTheNext()
    {
        Walk((0, [3, 7]), (7, []));
        var bad = Content(3);
        var broken = new InvalidOperationException("JFB_TEST: a value its editor cannot read");
        bad.Properties.Returns(_ => throw broken);
        var next = Content(7);
        _contents.GetByIds(Arg.Any<IEnumerable<int>>()).Returns([bad, next]);
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, "0");
        var logger = new RecordingLogger<FormReferenceRebuild>();

        var done = await new FormReferenceRebuild(_references, _values, _contents, Index(), _clock, logger).ContinueAsync(DateTimeOffset.MaxValue);

        Assert.That(done, Is.True);
        await _references.Received(1).ReplaceUnchangedContentAsync(7, Version(7), next.Key, Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>());
        Assert.That(logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).Select(entry => (entry.Level, entry.Exception?.Message)), Is.EqualTo(new[] { (LogLevel.Warning, LoggedException.Of(broken).Message) }));
    }

    [Test]
    public async Task AWriteThatFails_StopsTheWalk_AndTheBatchRunsAgainNextTime()
    {
        Walk((0, [3]), (3, []));
        var page = Content(3);
        _contents.GetByIds(Arg.Any<IEnumerable<int>>()).Returns([page]);
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, "0");
        var broken = new InvalidOperationException("JFB_TEST: the database is gone");
        _references.ReplaceUnchangedContentAsync(Arg.Any<int>(), Arg.Any<DocumentVersion>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ContentFormReference>>(), Arg.Any<CancellationToken>()).ThrowsAsync(broken);

        Assert.ThrowsAsync<InvalidOperationException>(() => Rebuild().ContinueAsync(DateTimeOffset.MaxValue));
        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Is.EqualTo("0"), "the place did not move on");
    }

    [Test]
    public async Task ARequestMadeDuringABatch_EndsTheRun_AndTheNextRunHoldsItBeforeItsWalk()
    {
        Walk((0, []));
        var rebuild = Rebuild();
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, "0");

        // One request only: a run that goes on anyway then fails here, where one on every read would never end.
        var asked = false;
        _references.VersionsOfAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (!asked)
                {
                    asked = true;
                    await rebuild.RequestAsync();
                }

                return (IReadOnlyDictionary<int, DocumentVersion>)new Dictionary<int, DocumentVersion>();
            });
        var job = Job(RuntimeLevel.Run, pending: false, new RecordingLogger<FormReferenceRebuildJob>());

        await job.ExecuteAsync(CancellationToken.None);

        await _references.Received(1).DocumentsAfterAsync(0, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>());
        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Does.StartWith("0-"), "no walk from the old copies in this run");
        Assert.That(await rebuild.HoldNewRequestAsync(), Is.True);
        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Is.EqualTo("0"));
    }

    [Test]
    public async Task ARequestMadeWhileTheOrphansGo_EndsTheRun_AndTheNextRunHoldsItBeforeItsWalk()
    {
        Walk((0, []));
        var rebuild = Rebuild();
        await _values.SetAsync(RequestReferenceRebuildV1.RebuildKey, FormReferenceRebuild.OrphansNext);
        var asked = false;
        _references.DeleteOrphansAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (!asked)
                {
                    asked = true;
                    await rebuild.RequestAsync();
                }

                return 0;
            });
        var job = Job(RuntimeLevel.Run, pending: false, new RecordingLogger<FormReferenceRebuildJob>());

        await job.ExecuteAsync(CancellationToken.None);

        await _references.DidNotReceive().DocumentsAfterAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Does.StartWith("0-"), "the request stands, and no walk starts in this run");
        Assert.That(await rebuild.HoldNewRequestAsync(), Is.True);
    }

    [Test]
    public async Task TheJob_HoldsARequestJustMadeForOneRun_ThenWalks()
    {
        Walk((0, []));
        await Rebuild().RequestAsync();
        var job = Job(RuntimeLevel.Run, pending: false, new RecordingLogger<FormReferenceRebuildJob>());

        await job.ExecuteAsync(CancellationToken.None);
        var held = await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey);
        await _references.DidNotReceive().DocumentsAfterAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await job.ExecuteAsync(CancellationToken.None);

        Assert.That(held, Is.EqualTo(RequestReferenceRebuildV1.FromStart));
        await _references.Received(1).DocumentsAfterAsync(0, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>());
        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Is.Null, "walked, and done");
    }

    [Test]
    public async Task ARequestThatComesAsAnotherIsHeld_StandsAndTheRunStaysAway()
    {
        Walk((0, []));
        await Rebuild().RequestAsync();
        var job = Job(RuntimeLevel.Run, pending: false, new RecordingLogger<FormReferenceRebuildJob>());
        var other = $"{RequestReferenceRebuildV1.FromStart}-other";
        _values.ArrivesBeforeTheNextReplace = other;

        await job.ExecuteAsync(CancellationToken.None);

        await _references.DidNotReceive().DocumentsAfterAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.That(await _values.GetAsync(RequestReferenceRebuildV1.RebuildKey), Is.EqualTo(other));
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
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception?.Message)), Is.EqualTo(new[] { (LogLevel.Error, LoggedException.Of(broken).Message) }));
        });
    }

    private static IContent Content(int id = 0)
    {
        var content = Substitute.For<IContent>();
        content.Id.Returns(id);
        content.VersionId.Returns(Version(id).Id);
        content.Key.Returns(Guid.NewGuid());
        content.Properties.Returns(new PropertyCollection());
        return content;
    }

    // The version a document's id stands for here.
    private static DocumentVersion Version(int id) => new(id + 1000, new DateTime(2026, 10, 31, 12, 0, id % 60, DateTimeKind.Utc));

    // The batches of document ids the walk reads after each id, and each document's version.
    private void Walk(params (int After, int[] Ids)[] batches)
    {
        foreach (var (after, ids) in batches)
        {
            _references.DocumentsAfterAsync(after, FormReferenceRebuild.BatchSize, Arg.Any<CancellationToken>()).Returns(ids);
        }

        _references.VersionsOfAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<int>>().ToDictionary(id => id, Version));
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

        public string? ArrivesBeforeTheNextReplace { get; set; }

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

        public async Task<bool> TryReplaceAsync(string key, string expected, string? value, CancellationToken cancellationToken = default)
        {
            if (ArrivesBeforeTheNextReplace is { } arrives)
            {
                ArrivesBeforeTheNextReplace = null;
                _stored[key] = arrives;
            }

            if (_stored.GetValueOrDefault(key) != expected)
            {
                return false;
            }

            await SetAsync(key, value, cancellationToken);
            return true;
        }

        public Task<bool> TryDeleteAsync(string key, string expected, CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored.GetValueOrDefault(key) == expected && _stored.Remove(key));
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
