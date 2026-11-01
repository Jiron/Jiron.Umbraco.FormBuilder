using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Retention;

/// <summary>
/// The orphan cleanup (ROADMAP 7.3b and 7.4d, ADR-0144, ADR-0148): an upload no entry took goes with its bytes once its token has run out and a
/// request that read the token cannot still be under way. A file that cannot go keeps its row and does not stop the others, and
/// a file's name is never logged.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OrphanFileCleanupTests : RepositoryTestBase
{
    private const string SecretName = "JFB_TEST-secret-name.pdf";

    private readonly CountingStore _store = new();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    private OrphanFileCleanup Cleanup => new(Entries, _store, Clock, GetRequiredService<ILogger<OrphanFileCleanup>>());

    private OrphanFileJob Job(RuntimeLevel level, bool pending)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        var migrations = Substitute.For<IFormBuilderMigrationState>();
        migrations.IsPendingAsync(Arg.Any<CancellationToken>()).Returns(pending);
        return new(Cleanup, runtime, migrations, Substitute.For<IHostApplicationLifetime>(), Clock);
    }

    [SetUp]
    public void ForgetTheStore()
    {
        _store.Deleted.Clear();
        _store.Refused.Clear();
    }

    [Test]
    public void TheCleanup_HasATimerOfItsOwn_OnTheServerThatSchedules()
    {
        var job = GetRequiredService<IEnumerable<IRecurringBackgroundJob>>().OfType<OrphanFileJob>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().Select(part => part.GetType()), Does.Not.Contain(typeof(OrphanFileCleanup)), "the hourly job's share would fall behind");
            Assert.That(job.Period, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(job.ServerRoles, Is.EquivalentTo(new[] { ServerRole.Single, ServerRole.SchedulingPublisher }));
            Assert.That(OrphanFileJob.Budget, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
    }

    [Test]
    public async Task ABacklog_IsClearedPastOneRun()
    {
        var form = await CreateTestFormAsync("upload");
        var first = await UploadAsync(form.Id);
        var second = await UploadAsync(form.Id);
        var third = await UploadAsync(form.Id);
        Clock.Advance(TimeSpan.FromDays(2));
        var job = Job(RuntimeLevel.Run, pending: false);
        _store.OnDelete = () => Clock.Advance(OrphanFileJob.Budget);

        await job.RunJobAsync();
        var afterOne = _store.Deleted.Count;
        await job.RunJobAsync();
        await job.RunJobAsync();
        _store.OnDelete = null;

        Assert.Multiple(async () =>
        {
            Assert.That(afterOne, Is.EqualTo(1), "a run ends at its budget");
            Assert.That(await Entries.GetFileAsync(first.Key), Is.Null);
            Assert.That(await Entries.GetFileAsync(second.Key), Is.Null);
            Assert.That(await Entries.GetFileAsync(third.Key), Is.Null, "the next runs go on");
        });
    }

    [TestCase(RuntimeLevel.Upgrade, false, TestName = "The job removes nothing while Umbraco does not run")]
    [TestCase(RuntimeLevel.Run, true, TestName = "The job removes nothing while a migration is pending")]
    public async Task TheJob_RemovesNothing(RuntimeLevel level, bool pending)
    {
        var form = await CreateTestFormAsync("upload");
        var file = await UploadAsync(form.Id);
        Clock.Advance(TimeSpan.FromDays(2));

        await Job(level, pending).RunJobAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(_store.Deleted, Is.Empty);
            Assert.That(await Entries.GetFileAsync(file.Key), Is.Not.Null);
        });
    }

    [Test]
    public void AnUploadIsKept_PastItsTokensDay_ByTheLongestARequestMayTake() =>
        Assert.That(OrphanFileCleanup.KeptFor, Is.EqualTo(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(5)));

    [Test]
    public async Task AnUploadPastItsTime_GoesWithItsBytes_AndOneWithinIt_Stays()
    {
        var form = await CreateTestFormAsync("upload");
        var old = await UploadAsync(form.Id);
        Clock.Advance(TimeSpan.FromSeconds(1));
        var young = await UploadAsync(form.Id);
        Clock.Advance(OrphanFileCleanup.KeptFor - TimeSpan.FromMilliseconds(500));

        await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), CancellationToken.None);

        var logged = Logs.Entries.Where(entry => entry.Category == typeof(OrphanFileCleanup).FullName).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.GetFileAsync(old.Key), Is.Null);
            Assert.That(_store.Deleted, Is.EqualTo(new[] { old.StoredName }));
            Assert.That(await Entries.GetFileAsync(young.Key), Is.Not.Null);
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Information }));
            Assert.That(logged[0].Message, Does.Contain("(1)").And.Not.Contain(SecretName));
        });
    }

    [Test]
    public async Task AFileAnEntryTook_Stays()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await UploadAsync(form.Id);
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [file.Id] });
        Clock.Advance(TimeSpan.FromDays(30));

        await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.GetFileAsync(file.Key), Is.Not.Null);
            Assert.That(_store.Deleted, Is.Empty);
        });
    }

    [Test]
    public async Task AFileWhoseBytesCannotGo_KeepsItsRow_LoggedWithoutItsName_AndTheOthersGo()
    {
        var form = await CreateTestFormAsync("upload");
        var stuck = await UploadAsync(form.Id);
        var other = await UploadAsync(form.Id);
        _store.Refused.Add(stuck.StoredName);
        Clock.Advance(TimeSpan.FromDays(2));

        await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), CancellationToken.None);
        var firstRun = await Entries.GetFileAsync(stuck.Key);
        _store.Refused.Clear();
        await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), CancellationToken.None);

        var warnings = Logs.Entries.Where(entry => entry.Category == typeof(OrphanFileCleanup).FullName && entry.Level == LogLevel.Warning).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(firstRun, Is.Not.Null, "it keeps its row");
            Assert.That(await Entries.GetFileAsync(other.Key), Is.Null);
            Assert.That(await Entries.GetFileAsync(stuck.Key), Is.Null, "the next run takes it");
            Assert.That(warnings, Is.Not.Empty);
            Assert.That(warnings[0].Message, Does.Contain(stuck.Key.ToString()).And.Not.Contain(SecretName));
        });
    }

    [Test]
    public async Task MoreThanOneReadHolds_AllGo_WithinTheDeadline()
    {
        var form = await CreateTestFormAsync("upload");
        var files = new List<StoredEntryFile>();
        for (var number = 0; number < OrphanFileCleanup.Nominations + 2; number++)
        {
            files.Add(await UploadAsync(form.Id));
        }

        Clock.Advance(TimeSpan.FromDays(2));

        await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), CancellationToken.None);

        Assert.That(await Entries.ListOrphanFilesAsync(Clock.GetUtcNow(), 10), Is.Empty);
    }

    [Test]
    public async Task ADeadlineThatHasPassed_RemovesNothing_AndOneThatPassesMidway_Stops()
    {
        var form = await CreateTestFormAsync("upload");
        var first = await UploadAsync(form.Id);
        var second = await UploadAsync(form.Id);
        Clock.Advance(TimeSpan.FromDays(2));

        await Cleanup.RunAsync(Clock.GetUtcNow(), CancellationToken.None);
        var untouched = _store.Deleted.Count;
        _store.OnDelete = () => Clock.Advance(TimeSpan.FromSeconds(2));
        await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), CancellationToken.None);
        _store.OnDelete = null;

        Assert.Multiple(async () =>
        {
            Assert.That(untouched, Is.Zero);
            Assert.That(await Entries.GetFileAsync(first.Key), Is.Null);
            Assert.That(await Entries.GetFileAsync(second.Key), Is.Not.Null, "the next run takes it");
        });
    }

    [Test]
    public async Task TheSitesStopping_EndsIt()
    {
        var form = await CreateTestFormAsync("upload");
        await UploadAsync(form.Id);
        await UploadAsync(form.Id);
        Clock.Advance(TimeSpan.FromDays(2));
        using var stopping = new CancellationTokenSource();
        _store.OnDelete = stopping.Cancel;

        Assert.That(async () => await Cleanup.RunAsync(Clock.GetUtcNow().AddSeconds(1), stopping.Token), Throws.InstanceOf<OperationCanceledException>());
        _store.OnDelete = null;
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(global::Umbraco.Cms.Core.DependencyInjection.IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    private async Task<StoredEntryFile> UploadAsync(int formId) => (await Entries.AddFileAsync(EntryRepositoryTests.NewFile(formId, SecretName)))!;

    private sealed class CountingStore : IFormBuilderFileStore
    {
        public List<string> Deleted { get; } = [];

        public HashSet<string> Refused { get; } = [];

        public Action? OnDelete { get; set; }

        public Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            if (Refused.Contains(id))
            {
                throw new IOException("The store is locked.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Deleted.Add(id);
            OnDelete?.Invoke();
            return Task.CompletedTask;
        }
    }
}
