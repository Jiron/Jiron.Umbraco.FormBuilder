using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Retention;

/// <summary>
/// The draft-version cleanup in a real site (ADR-0045): by the history settings it removes the draft saves past each
/// form's newest ones that are older than the days, keeps every version something needs, audits what it removed form by
/// form, works in rounds and stops at its deadline; and the hourly retention job that runs it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class DraftVersionCleanupTests : RepositoryTestBase
{
    private IFormBuilderSettingsService SettingsService => GetRequiredService<IFormBuilderSettingsService>();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    private DraftVersionCleanup Cleanup => GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().OfType<DraftVersionCleanup>().Single();

    [Test]
    public async Task DraftSaves_PastTheNewest_AndOlderThanTheDays_AreRemoved_WithAnAuditEntryForEachForm()
    {
        await KeepAsync(days: 10, count: 2);
        var (a, b, c) = await ThreeFormsAsync();
        Clock.Advance(TimeSpan.FromDays(8));

        await Cleanup.RunAsync(Deadline(), CancellationToken.None);

        var pruned = await PrunedAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(await NumbersAsync(a), Is.EqualTo(new[] { 3, 4, 5, 6, 7 }), "1 and 2 go; 3 is pinned, 4 has an entry, 5 is not ten days old, 6 and 7 are the newest");
            Assert.That(await NumbersAsync(b), Is.EqualTo(new[] { 1, 2 }), "old, but the two newest");
            Assert.That(await NumbersAsync(c), Is.EqualTo(new[] { 2, 3, 4 }), "1 goes; a publish is no draft save");
            Assert.That(pruned.Select(entry => (entry.EntityType, entry.EntityKey, entry.UserKey, entry.Details)), Is.EqualTo(new[]
            {
                ("form", a.Key, (Guid?)null, """{"versions":[1,2]}"""),
                ("form", c.Key, null, """{"versions":[1]}"""),
            }));
            Assert.That(Logs.Entries.Where(entry => entry.Category == typeof(DraftVersionCleanup).FullName).Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Information, "Jiron Form Builder removed old draft saves from the History (draft saves: 3, forms: 2)"),
            }));
        });
    }

    [Test]
    public async Task ASecondRun_RemovesNothingMore_UntilAVersionIsLetGo()
    {
        await KeepAsync(days: 10, count: 2);
        var (a, _, _) = await ThreeFormsAsync();
        Clock.Advance(TimeSpan.FromDays(8));
        await Cleanup.RunAsync(Deadline(), CancellationToken.None);
        var logged = Logs.Entries.Count;

        await Cleanup.RunAsync(Deadline(), CancellationToken.None);
        var again = (await PrunedAsync()).Count;
        await Forms.PinVersionAsync((await Forms.GetAsync(a.Key))!.Id, 3, false);
        await Cleanup.RunAsync(Deadline(), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(again, Is.EqualTo(2), "no audit entry for a run that removed nothing");
            Assert.That(Logs.Entries.Skip(logged).Count(entry => entry.Category == typeof(DraftVersionCleanup).FullName), Is.EqualTo(1), "nor a log line");
            Assert.That(await NumbersAsync(a), Is.EqualTo(new[] { 4, 5, 6, 7 }), "3 once it is unpinned");
            Assert.That((await PrunedAsync()).Last().Details, Is.EqualTo("""{"versions":[3]}"""));
        });
    }

    [Test]
    public async Task TheDefaults_KeepFiftyDraftSaves_AndNinetyDays()
    {
        // A stored section without the history, then 52 versions a minute apart, the last one the form's draft.
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, GeneralSettings.Alias, 1, 1, """{"timeZone":"UTC","history":null}""", null));
        var form = await DraftsAsync(52, TimeSpan.FromMinutes(1));
        Clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromMinutes(51));

        await Cleanup.RunAsync(Deadline(), CancellationToken.None);
        var atNinetyDays = (await NumbersAsync(form)).Count;
        Clock.Advance(TimeSpan.FromMinutes(2));
        await Cleanup.RunAsync(Deadline(), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(atNinetyDays, Is.EqualTo(52), "none is older than ninety days yet");
            Assert.That(await NumbersAsync(form), Is.EqualTo(Enumerable.Range(3, 50)), "past the fifty newest and older than ninety days");
        });
    }

    [Test]
    public async Task ManyDraftSaves_AreRemovedInRounds_EachFormInAUnitOfItsOwn()
    {
        await KeepAsync(days: 1, count: 0);
        var form = await DraftsAsync(DraftVersionCleanup.Nominations + 52, TimeSpan.FromSeconds(1));
        Clock.Advance(TimeSpan.FromDays(2));

        await Cleanup.RunAsync(Deadline(), CancellationToken.None);

        var pruned = await PrunedAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(await NumbersAsync(form), Is.EqualTo(new[] { DraftVersionCleanup.Nominations + 52 }), "all but the form's draft");
            Assert.That(pruned.Select(entry => entry.Details), Is.EqualTo(new[] { Versions(1, DraftVersionCleanup.Nominations), Versions(DraftVersionCleanup.Nominations + 1, 51) }), "one entry for each round");
        });
    }

    [Test]
    public async Task TheCleanup_StopsAtItsDeadline_AndLeavesTheRestToTheNextRun()
    {
        await KeepAsync(days: 1, count: 0);
        var first = await DraftsAsync(2, TimeSpan.FromMinutes(1));
        var second = await DraftsAsync(2, TimeSpan.FromMinutes(1));
        Clock.Advance(TimeSpan.FromDays(2));
        // An audit entry that takes a minute: the deadline passes once the first form is done.
        var slow = new DraftVersionCleanup(Forms, new SlowAudit(Audit, Clock), Database, SettingsService, Clock, GetRequiredService<ILogger<DraftVersionCleanup>>());

        await Cleanup.RunAsync(Clock.GetUtcNow(), CancellationToken.None);
        var atTheDeadline = Count(FormBuilderTables.FormVersion);
        await slow.RunAsync(Deadline(), CancellationToken.None);
        var stopped = (await NumbersAsync(first), await NumbersAsync(second));
        await Cleanup.RunAsync(Deadline(), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(atTheDeadline, Is.EqualTo(4), "a deadline that has passed removes nothing");
            Assert.That(stopped, Is.EqualTo((new[] { 2 }, new[] { 1, 2 })));
            Assert.That(await NumbersAsync(second), Is.EqualTo(new[] { 2 }), "the next run does the rest");
        });
    }

    [Test]
    public async Task ANomineeThatChangedSinceTheRead_IsLeftAlone_WithoutAnAuditEntry()
    {
        await KeepAsync(days: 1, count: 0);
        var first = await DraftsAsync(2, TimeSpan.FromMinutes(1));
        var second = await DraftsAsync(2, TimeSpan.FromMinutes(1));
        Clock.Advance(TimeSpan.FromDays(2));
        // While the first form's drafts go, an editor pins the second form's nominee.
        var meanwhile = new AuditThatRunsFirst(Audit, () => Forms.PinVersionAsync(second.Id, 1, true));
        var cleanup = new DraftVersionCleanup(Forms, meanwhile, Database, SettingsService, Clock, GetRequiredService<ILogger<DraftVersionCleanup>>());

        await cleanup.RunAsync(Deadline(), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((await NumbersAsync(first), await NumbersAsync(second)), Is.EqualTo((new[] { 2 }, new[] { 1, 2 })));
            Assert.That((await PrunedAsync()).Select(entry => entry.EntityKey), Is.EqualTo(new[] { first.Key }), "no entry for a form whose nominees all stayed");
            Assert.That(Logs.Entries.Where(entry => entry.Category == typeof(DraftVersionCleanup).FullName).Select(entry => entry.Message), Is.EqualTo(new[] { "Jiron Form Builder removed old draft saves from the History (draft saves: 1, forms: 1)" }));
        });
    }

    [Test]
    public async Task TheRetentionJob_RunsTheCleanup_EveryHour()
    {
        await KeepAsync(days: 1, count: 0);
        var form = await DraftsAsync(2, TimeSpan.FromMinutes(1));
        Clock.Advance(TimeSpan.FromDays(2));
        var job = GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().OfType<FormBuilderRetentionJob>().Single();
        // The plan ran after the site started; forget what the state said then.
        GetRequiredService<IFormBuilderMigrationState>().Invalidate();

        await job.ExecuteAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((job.Name, job.Period), Is.EqualTo(("Jiron.FormBuilder.Retention", TimeSpan.FromHours(1))));
            Assert.That(GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().Select(part => part.GetType()), Is.EqualTo(new[] { typeof(DraftVersionCleanup), typeof(OrphanFileCleanup) }));
            Assert.That(await NumbersAsync(form), Is.EqualTo(new[] { 2 }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    // The audit details of removed versions from a number on.
    private static string Versions(int first, int count) => $$"""{"versions":[{{string.Join(",", Enumerable.Range(first, count))}}]}""";

    private async Task KeepAsync(int days, int count) =>
        await SettingsService.SaveAsync(new GeneralSettings { History = new HistorySettings { DraftDays = days, DraftCount = count } }, 1, Editor);

    private DateTimeOffset Deadline() => Clock.GetUtcNow() + FormBuilderRetentionJob.Budget;

    // Form a: drafts 1 to 7 a day apart, 3 pinned, 4 with an entry, 7 its draft. Form b: drafts 1 and 2, 2 its draft.
    // Form c: drafts 1 and 2, a publish 3, then draft 4, its draft. Every form starts now.
    private async Task<(StoredForm A, StoredForm B, StoredForm C)> ThreeFormsAsync()
    {
        var a = await CreateFormAsync();
        var b = await CreateFormAsync();
        var c = await CreateFormAsync();
        for (var number = 2; number <= 7; number++)
        {
            Clock.Advance(TimeSpan.FromDays(1));
            a = (await Forms.SaveDraftAsync(a.Id, a.ConcurrencyVersion, Document($$"""{"n":{{number}}}"""), null, Editor))!.Form;
            if (number == 2)
            {
                b = (await Forms.SaveDraftAsync(b.Id, b.ConcurrencyVersion, Document("""{"n":2}"""), null, Editor))!.Form;
                c = (await Forms.SaveDraftAsync(c.Id, c.ConcurrencyVersion, Document("""{"n":2}"""), null, Editor))!.Form;
            }
            else if (number == 3)
            {
                c = (await Forms.PublishAsync(c.Id, c.ConcurrencyVersion, Document("""{"n":2}"""), null, null, Editor))!.Form;
            }
            else if (number == 4)
            {
                c = (await Forms.SaveDraftAsync(c.Id, c.ConcurrencyVersion, Document("""{"n":4}"""), null, Editor))!.Form;
            }
        }

        await Forms.PinVersionAsync(a.Id, 3, true);
        await AddEntryAsync(a.Id, (await Forms.GetVersionAsync(a.Id, 4))!.Info.Id);
        return (a, b, c);
    }

    // A form with this many versions, all draft saves, the given time apart; the last is its draft.
    private async Task<StoredForm> DraftsAsync(int versions, TimeSpan apart)
    {
        var form = await CreateFormAsync();
        for (var number = 2; number <= versions; number++)
        {
            Clock.Advance(apart);
            form = (await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, Document($$"""{"n":{{number}}}"""), null, Editor))!.Form;
        }

        return form;
    }

    private async Task<List<int>> NumbersAsync(StoredForm form) =>
        (await Forms.ListVersionsAsync(form.Id, null, PageRequest.First(PageRequest.MaxTake))).Items.Select(version => version.Number).Order().ToList();

    // The cleanup's audit entries, oldest first.
    private async Task<List<StoredAuditEntry>> PrunedAsync() =>
        (await Audit.QueryAsync(new AuditQuery { Action = DraftVersionCleanup.PrunedAction }, PageRequest.First(50))).Items.OrderBy(entry => entry.Id).ToList();

    // An audit repository that does something else first, the first time it adds an entry.
    private sealed class AuditThatRunsFirst(IAuditRepository inner, Func<Task> first) : IAuditRepository
    {
        private Func<Task>? _first = first;

        public async Task<StoredAuditEntry> AddAsync(NewAuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _first, null) is { } run)
            {
                await run();
            }

            return await inner.AddAsync(entry, cancellationToken);
        }

        public Task<StoredAuditEntry?> AddOnceAsync(NewAuditEntry entry, TimeSpan window, CancellationToken cancellationToken = default) => inner.AddOnceAsync(entry, window, cancellationToken);

        public Task<StoredAuditEntry?> GetAsync(long id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

        public Task<Page<StoredAuditEntry>> QueryAsync(AuditQuery query, PageRequest page, CancellationToken cancellationToken = default) => inner.QueryAsync(query, page, cancellationToken);

        public Task<int> PurgeAsync(TimeSpan keep, int take, CancellationToken cancellationToken = default) => inner.PurgeAsync(keep, take, cancellationToken);
    }

    // An audit repository whose every entry takes a minute.
    private sealed class SlowAudit(IAuditRepository inner, TestTimeProvider clock) : IAuditRepository
    {
        public async Task<StoredAuditEntry> AddAsync(NewAuditEntry entry, CancellationToken cancellationToken = default)
        {
            var added = await inner.AddAsync(entry, cancellationToken);
            clock.Advance(TimeSpan.FromMinutes(1));
            return added;
        }

        public Task<StoredAuditEntry?> AddOnceAsync(NewAuditEntry entry, TimeSpan window, CancellationToken cancellationToken = default) => inner.AddOnceAsync(entry, window, cancellationToken);

        public Task<StoredAuditEntry?> GetAsync(long id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

        public Task<Page<StoredAuditEntry>> QueryAsync(AuditQuery query, PageRequest page, CancellationToken cancellationToken = default) => inner.QueryAsync(query, page, cancellationToken);

        public Task<int> PurgeAsync(TimeSpan keep, int take, CancellationToken cancellationToken = default) => inner.PurgeAsync(keep, take, cancellationToken);
    }
}
