using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Files;

/// <summary>
/// The scan of what waits (ROADMAP 7.3b and 7.4d, ADR-0144, ADR-0148), on a migrated database: any scanner's quarantine decides, an
/// answer from each that finds nothing makes a file clean, anything else leaves it waiting, a quarantine marks the entry that holds
/// the file and keeps any entry from taking it, what an entry holds goes before uploads, a file that cannot be opened passes its
/// turn, and a file's name is never logged.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FileScanTests : RepositoryTestBase
{
    private const string SecretName = "JFB_TEST-secret-name.pdf";

    private readonly MemoryStore _store = new();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    [SetUp]
    public void ForgetTheFiles() => _store.Files.Clear();

    [TestCase(new[] { FormBuilderFileScanOutcome.Clean }, FileScanStates.Clean, TestName = "One that finds nothing")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Clean, FormBuilderFileScanOutcome.Clean }, FileScanStates.Clean, TestName = "Two that find nothing")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Quarantine }, FileScanStates.Quarantined, TestName = "One that finds malware")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Clean, FormBuilderFileScanOutcome.Quarantine }, FileScanStates.Quarantined, TestName = "The second finds malware")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Unavailable, FormBuilderFileScanOutcome.Quarantine }, FileScanStates.Quarantined, TestName = "One cannot say and another finds malware")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Unavailable }, FileScanStates.Pending, TestName = "One cannot say")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Unavailable, FormBuilderFileScanOutcome.Clean }, FileScanStates.Pending, TestName = "One cannot say and another finds nothing")]
    [TestCase(new[] { FormBuilderFileScanOutcome.Clean, FormBuilderFileScanOutcome.Unavailable }, FileScanStates.Pending, TestName = "One finds nothing and another cannot say")]
    public async Task AFilesState_IsWhatItsScannersSay(FormBuilderFileScanOutcome[] outcomes, string state)
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);

        await Scans(outcomes.Select(outcome => new Answering(_ => outcome)).ToArray()).ScanAsync(file, CancellationToken.None);

        Assert.That((await Entries.GetFileAsync(file.Key))!.ScanState, Is.EqualTo(state));
    }

    [Test]
    public async Task AQuarantine_Decides_WithoutAskingTheScannersAfterIt()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        var asked = 0;

        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Quarantine), new Answering(_ =>
        {
            asked++;
            return FormBuilderFileScanOutcome.Clean;
        })).ScanAsync(file, CancellationToken.None);

        Assert.That(asked, Is.Zero);
    }

    [Test]
    public async Task AScanner_GetsTheFile_WithItsBytes_AndAStreamOfItsOwn()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        var seen = new List<(FormBuilderFileToScan File, string Bytes)>();
        FormBuilderFileScanOutcome Read(FormBuilderFileToScan scanned)
        {
            using var reader = new StreamReader(scanned.Content, Encoding.ASCII, leaveOpen: true);
            seen.Add((scanned, reader.ReadToEnd()));
            return FormBuilderFileScanOutcome.Clean;
        }

        await Scans(new Answering(Read), new Answering(Read)).ScanAsync(file, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(seen.Select(one => one.Bytes), Is.EqualTo(new[] { "bytes", "bytes" }), "each from the start");
            Assert.That(seen[0].File.Key, Is.EqualTo(file.Key));
            Assert.That(seen[0].File.Name, Is.EqualTo(SecretName));
            Assert.That(seen[0].File.ContentType, Is.EqualTo(file.ContentType));
            Assert.That(seen[0].File.SizeBytes, Is.EqualTo(file.SizeBytes));
            Assert.That(seen[0].File.Sha256, Is.EqualTo(file.FileHash));
            Assert.That(seen[0].File.Content.CanRead, Is.False, "closed after the scan");
        });
    }

    [Test]
    public async Task AQuarantine_MarksTheEntryThatHoldsTheFile_OnItsTimeline()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [file.Id] });

        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Quarantine)).ScanAsync(file, CancellationToken.None);

        var events = await EntryEvents.ListForEntryAsync(entry!.Id);
        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.ListFilesAsync(entry.Id)).Single().ScanState, Is.EqualTo(FileScanStates.Quarantined));
            Assert.That(events.Select(one => one.Kind), Is.EqualTo(new[] { "fileQuarantined" }));
            Assert.That(events[0].Details, Is.EqualTo($$"""{"file":"{{file.Key}}","field":"{{file.FieldKey}}"}"""));
            Assert.That(events[0].UserKey, Is.Null);
        });
    }

    [Test]
    public async Task AFileWithoutAnEntry_IsQuarantinedWithoutAMark_AndACleanOne_MarksNothing()
    {
        var form = await CreateTestFormAsync("upload");
        var waiting = await WaitingAsync(form.Id);
        var held = await WaitingAsync(form.Id);
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [held.Id] });

        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Quarantine)).ScanAsync(waiting, CancellationToken.None);
        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Clean)).ScanAsync(held, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.GetFileAsync(waiting.Key))!.ScanState, Is.EqualTo(FileScanStates.Quarantined));
            Assert.That(await EntryEvents.ListForEntryAsync(entry!.Id), Is.Empty);
        });
    }

    [Test]
    public async Task NoEntry_TakesAQuarantinedFile()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Quarantine)).ScanAsync(file, CancellationToken.None);

        Assert.That(
            async () => await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [file.Id] }),
            Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.MissingFile));
    }

    [Test]
    public async Task AScannerThatThrows_CannotSay_AndItIsLogged_WithoutTheFilesName()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);

        await Scans(new Answering(_ => throw new InvalidOperationException(SecretName))).ScanAsync(file, CancellationToken.None);

        var logged = Logs.Entries.Where(entry => entry.Category == typeof(FileScans).FullName).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.GetFileAsync(file.Key))!.ScanState, Is.EqualTo(FileScanStates.Pending));
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));
            Assert.That(string.Join(' ', logged.Select(entry => entry.Message)), Does.Contain(file.Key.ToString()).And.Not.Contain(SecretName));
        });
    }

    [Test]
    public async Task AQuarantine_IsLogged_WithoutTheFilesName()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);

        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Quarantine)).ScanAsync(file, CancellationToken.None);

        var logged = Logs.Entries.Where(entry => entry.Category == typeof(FileScans).FullName).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));
            Assert.That(logged[0].Message, Does.Contain(file.Key.ToString()).And.Not.Contain(SecretName));
        });
    }

    [Test]
    public async Task AScannerPastItsLimit_CannotSay()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        var timers = new ManualTimers();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new Slow(listening);

        var scanning = new FileScans(new FormBuilderFileScannerCollection(() => [slow]), _store, Entries, EntryEvents, Database, timers, NullLogger<FileScans>.Instance).ScanAsync(file, CancellationToken.None);
        await listening.Task.WaitAsync(TimeSpan.FromMinutes(1));
        var due = timers.Timers[0].DueTime;
        timers.FireAll();
        await scanning.WaitAsync(TimeSpan.FromMinutes(1));

        Assert.Multiple(async () =>
        {
            Assert.That(due, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That((await Entries.GetFileAsync(file.Key))!.ScanState, Is.EqualTo(FileScanStates.Pending));
        });
    }

    [Test]
    public async Task TheSitesStopping_EndsTheScan()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        Assert.That(async () => await Scans(new Answering(_ => throw new OperationCanceledException())).ScanAsync(file, stopping.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task AFileWhoseBytesAreGone_IsUnscanned_AndLoggedWithoutItsName()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        _store.Files.Clear();
        var asked = 0;

        await Scans(new Answering(_ =>
        {
            asked++;
            return FormBuilderFileScanOutcome.Clean;
        })).ScanAsync(file, CancellationToken.None);

        var logged = Logs.Entries.Where(entry => entry.Category == typeof(FileScans).FullName).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.GetFileAsync(file.Key))!.ScanState, Is.EqualTo(FileScanStates.Unscanned));
            Assert.That(asked, Is.Zero);
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));
            Assert.That(logged[0].Message, Does.Not.Contain(SecretName));
        });
    }

    [Test]
    public async Task AScanThatAnotherServerFinishedFirst_LeavesItsAnswer()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [file.Id] });
        await Entries.ChangeScanStateAsync(file.Id, FileScanStates.Pending, FileScanStates.Clean);

        await Scans(new Answering(_ => FormBuilderFileScanOutcome.Quarantine)).ScanAsync(file, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.GetFileAsync(file.Key))!.ScanState, Is.EqualTo(FileScanStates.Clean));
            Assert.That(await EntryEvents.ListForEntryAsync(entry!.Id), Is.Empty, "no mark without the quarantine");
            Assert.That(Logs.Entries.Where(log => log.Category == typeof(FileScans).FullName), Is.Empty, "no quarantine to report");
        });
    }

    [Test]
    public async Task TheFilesThatWait_AreListedInOrder_AfterAnId_WithoutOthers()
    {
        var form = await CreateTestFormAsync("upload");
        var first = await WaitingAsync(form.Id);
        var done = await WaitingAsync(form.Id);
        var second = await WaitingAsync(form.Id);
        var third = await WaitingAsync(form.Id);
        await Entries.ChangeScanStateAsync(done.Id, FileScanStates.Pending, FileScanStates.Clean);

        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.ListUploadsToScanAsync(0, 10)).Select(file => file.Id), Is.EqualTo(new[] { first.Id, second.Id, third.Id }));
            Assert.That((await Entries.ListUploadsToScanAsync(first.Id, 10)).Select(file => file.Id), Is.EqualTo(new[] { second.Id, third.Id }));
            Assert.That((await Entries.ListUploadsToScanAsync(0, 2)).Select(file => file.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(async () => await Entries.ListUploadsToScanAsync(0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(async () => await Entries.ListUploadsToScanAsync(0, EntryRepository.MaxOrphansPerList + 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(async () => await Entries.ListEntryFilesToScanAsync(0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(async () => await Entries.ListEntryFilesToScanAsync(0, EntryRepository.MaxOrphansPerList + 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public async Task WhatAnEntryHolds_AndUploads_AreListedApart()
    {
        var form = await CreateTestFormAsync("upload");
        var first = await WaitingAsync(form.Id);
        var upload = await WaitingAsync(form.Id);
        var second = await WaitingAsync(form.Id);
        var done = await WaitingAsync(form.Id);
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [first.Id, second.Id, done.Id] });
        await Entries.ChangeScanStateAsync(done.Id, FileScanStates.Pending, FileScanStates.Clean);

        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.ListEntryFilesToScanAsync(0, 10)).Select(file => file.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That((await Entries.ListEntryFilesToScanAsync(first.Id, 10)).Select(file => file.Id), Is.EqualTo(new[] { second.Id }));
            Assert.That((await Entries.ListEntryFilesToScanAsync(0, 1)).Select(file => file.Id), Is.EqualTo(new[] { first.Id }));
            Assert.That((await Entries.ListUploadsToScanAsync(0, 10)).Select(file => file.Id), Is.EqualTo(new[] { upload.Id }));
        });
    }

    [Test]
    public async Task WhatAnEntryHolds_IsScannedBeforeUploadsThatCameFirst()
    {
        var form = await CreateTestFormAsync("upload");
        var upload = await WaitingAsync(form.Id);
        var held = await WaitingAsync(form.Id);
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [held.Id] });
        var asked = new List<Guid>();

        await Job(new Answering(file =>
        {
            asked.Add(file.Key);
            return FormBuilderFileScanOutcome.Clean;
        })).RunJobAsync();

        Assert.That(asked, Is.EqualTo(new[] { held.Key, upload.Key }));
    }

    [Test]
    public async Task AFloodOfUploads_HoldsANewEntrysFile_BehindOneBatchAtMost()
    {
        var form = await CreateTestFormAsync("upload");
        var uploads = new List<StoredEntryFile>();
        for (var number = 0; number < 25; number++)
        {
            uploads.Add(await WaitingAsync(form.Id));
        }

        var held = await WaitingAsync(form.Id);
        var asked = new List<Guid>();

        await Job(new AnsweringAfter(async file =>
        {
            asked.Add(file.Key);
            if (asked.Count == 1)
            {
                await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [held.Id] });
            }
        })).RunJobAsync();

        Assert.Multiple(() =>
        {
            Assert.That(asked.IndexOf(held.Key), Is.EqualTo(10), "right after the batch under way");
            Assert.That(asked, Has.Count.EqualTo(26), "and everything else after it");
        });
    }

    [Test]
    public async Task AFileAnEntryTook_BehindWhereTheJobStood_IsFoundByARunSoon()
    {
        var form = await CreateTestFormAsync("upload");
        var behind = await WaitingAsync(form.Id);
        var held = await WaitingAsync(form.Id);
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [held.Id] });
        var asked = new List<Guid>();
        var job = Job(new Answering(file =>
        {
            asked.Add(file.Key);
            Clock.Advance(FileScanJob.Budget);
            return FormBuilderFileScanOutcome.Clean;
        }));

        await job.RunJobAsync();
        var firstRun = asked.ToList();
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [behind.Id] });
        await job.RunJobAsync();
        await job.RunJobAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstRun, Is.EqualTo(new[] { held.Key }));
            Assert.That(asked, Is.EqualTo(new[] { held.Key, behind.Key }), "the entries' list starts over in the run after its end");
        });
    }

    [Test]
    public async Task AFileThatCannotBeOpened_StaysWaiting_AndPassesItsTurn()
    {
        var form = await CreateTestFormAsync("upload");
        var broken = await WaitingAsync(form.Id);
        var next = await WaitingAsync(form.Id);
        _store.Refused.Add(broken.StoredName);
        var asked = new List<Guid>();

        await Job(new Answering(file =>
        {
            asked.Add(file.Key);
            return FormBuilderFileScanOutcome.Clean;
        })).RunJobAsync();
        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(FileScans).FullName && entry.Level == LogLevel.Warning).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(asked, Is.EqualTo(new[] { next.Key }));
            Assert.That((await Entries.GetFileAsync(broken.Key))!.ScanState, Is.EqualTo(FileScanStates.Pending));
            Assert.That((await Entries.GetFileAsync(next.Key))!.ScanState, Is.EqualTo(FileScanStates.Clean));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Message, Does.Contain(broken.Key.ToString()).And.Not.Contain(SecretName));
        });
    }

    [Test]
    public async Task AScanThatFailsOutright_EndsItsRun_AndTheNextGoesPastIt()
    {
        var form = await CreateTestFormAsync("upload");
        var broken = await WaitingAsync(form.Id);
        var next = await WaitingAsync(form.Id);
        _store.Fatal.Add(broken.StoredName);
        var asked = new List<Guid>();
        var job = Job(new Answering(file =>
        {
            asked.Add(file.Key);
            return FormBuilderFileScanOutcome.Clean;
        }));

        Assert.That(job.RunJobAsync, Throws.InstanceOf<OutOfMemoryException>());
        await job.RunJobAsync();

        Assert.That(asked, Is.EqualTo(new[] { next.Key }));
    }

    [Test]
    public async Task TheSitesStopping_EndsAScan_WhileItOpensAFile()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await WaitingAsync(form.Id);
        _store.Refused.Add(file.StoredName);
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        Assert.That(async () => await Scans(new Answering(_ => FormBuilderFileScanOutcome.Clean)).ScanAsync(file, stopping.Token), Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task TheJob_ScansWhatWaits_OldestFirst()
    {
        var form = await CreateTestFormAsync("upload");
        var first = await WaitingAsync(form.Id);
        var second = await WaitingAsync(form.Id);
        var asked = new List<Guid>();

        await Job(new Answering(file =>
        {
            asked.Add(file.Key);
            return FormBuilderFileScanOutcome.Clean;
        })).RunJobAsync();

        Assert.That(asked, Is.EqualTo(new[] { first.Key, second.Key }));
    }

    [Test]
    public async Task TheJob_ReadsMoreThanABatch()
    {
        var form = await CreateTestFormAsync("upload");
        for (var number = 0; number < 52; number++)
        {
            await WaitingAsync(form.Id);
        }

        await Job(new Answering(_ => FormBuilderFileScanOutcome.Clean)).RunJobAsync();

        Assert.That(await Entries.ListUploadsToScanAsync(0, 100), Is.Empty);
    }

    [TestCase(false, RuntimeLevel.Run, false, TestName = "Without a scanner")]
    [TestCase(true, RuntimeLevel.Upgrade, false, TestName = "While Umbraco does not run")]
    [TestCase(true, RuntimeLevel.Run, true, TestName = "While a migration is pending")]
    public async Task TheJob_ReadsNothing(bool scanner, RuntimeLevel level, bool pending)
    {
        var form = await CreateTestFormAsync("upload");
        await WaitingAsync(form.Id);
        var asked = 0;
        IFormBuilderFileScanner[] scanners = scanner
            ? [new Answering(_ =>
            {
                asked++;
                return FormBuilderFileScanOutcome.Clean;
            })]
            : [];
        var entries = Substitute.For<IEntryRepository>();

        await Job(entries, level, pending, scanners).RunJobAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(asked, Is.Zero);
            await entries.DidNotReceiveWithAnyArgs().ListEntryFilesToScanAsync(0, 0);
            await entries.DidNotReceiveWithAnyArgs().ListUploadsToScanAsync(0, 0);
        });
    }

    [Test]
    public async Task TheJob_StopsAtItsBudget_AndGoesOnFromThere_ThenStartsOver()
    {
        var form = await CreateTestFormAsync("upload");
        var first = await WaitingAsync(form.Id);
        var second = await WaitingAsync(form.Id);
        var third = await WaitingAsync(form.Id);
        var asked = new List<Guid>();
        var job = Job(new Answering(file =>
        {
            asked.Add(file.Key);
            Clock.Advance(FileScanJob.Budget);
            return FormBuilderFileScanOutcome.Unavailable;
        }));

        await job.RunJobAsync();
        var firstRun = asked.ToList();
        await job.RunJobAsync();
        await job.RunJobAsync();
        var thirdRun = asked.ToList();
        await job.RunJobAsync();
        await job.RunJobAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstRun, Is.EqualTo(new[] { first.Key }), "no new scan past the budget");
            Assert.That(thirdRun, Is.EqualTo(new[] { first.Key, second.Key, third.Key }), "what cannot answer keeps nobody waiting");
            Assert.That(asked.Skip(3), Is.EqualTo(new[] { first.Key }), "the run that reached the end starts over");
        });
    }

    [Test]
    public void TheJob_IsNotInUmbracosDistributedRunner() =>
        Assert.That(GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().Select(job => job.GetType()), Does.Not.Contain(typeof(FileScanJob)));

    /// <inheritdoc />
    protected override void CustomTestSetup(global::Umbraco.Cms.Core.DependencyInjection.IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    private FileScans Scans(params IFormBuilderFileScanner[] scanners) =>
        new(new FormBuilderFileScannerCollection(() => scanners), _store, Entries, EntryEvents, Database, Clock, GetRequiredService<ILogger<FileScans>>());

    private FileScanJob Job(params IFormBuilderFileScanner[] scanners) => Job(Entries, RuntimeLevel.Run, false, scanners);

    private FileScanJob Job(IEntryRepository entries, RuntimeLevel level, bool pending, IFormBuilderFileScanner[] scanners)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        var migrations = Substitute.For<IFormBuilderMigrationState>();
        migrations.IsPendingAsync(Arg.Any<CancellationToken>()).Returns(pending);
        return new(new FormBuilderFileScannerCollection(() => scanners), Scans(scanners), entries, runtime, migrations, Substitute.For<IHostApplicationLifetime>(), Clock);
    }

    private async Task<StoredEntryFile> WaitingAsync(int formId)
    {
        var file = (await Entries.AddFileAsync(EntryRepositoryTests.NewFile(formId, SecretName)))!;
        _store.Files[file.StoredName] = "bytes"u8.ToArray();
        return file;
    }

    private sealed class Answering(Func<FormBuilderFileToScan, FormBuilderFileScanOutcome> answer) : IFormBuilderFileScanner
    {
        public Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken) => Task.FromResult(answer(file));
    }

    private sealed class AnsweringAfter(Func<FormBuilderFileToScan, Task> work) : IFormBuilderFileScanner
    {
        public async Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken)
        {
            await work(file);
            return FormBuilderFileScanOutcome.Clean;
        }
    }

    // Waits for its limit.
    private sealed class Slow(TaskCompletionSource listening) : IFormBuilderFileScanner
    {
        public async Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken)
        {
            listening.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FormBuilderFileScanOutcome.Clean;
        }
    }

    private sealed class MemoryStore : IFormBuilderFileStore
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public HashSet<string> Refused { get; } = [];

        public HashSet<string> Fatal { get; } = [];

        public Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
        {
            if (Fatal.Remove(id))
            {
                throw new InsufficientMemoryException();
            }

            return Refused.Contains(id)
                ? throw new IOException("The store is locked.")
                : Task.FromResult<Stream?>(Files.TryGetValue(id, out var bytes) ? new MemoryStream(bytes) : null);
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
