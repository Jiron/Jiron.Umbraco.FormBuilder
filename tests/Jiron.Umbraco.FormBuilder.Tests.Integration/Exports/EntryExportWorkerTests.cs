using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Exports;

/// <summary>
/// The background exports with this server's own worker and the exports' job (ADR-0210), unlike the other export tests,
/// which run each pass themselves: queueing an export wakes the worker, which writes it at once, and the job removes what
/// expired and wakes the worker, which takes over an export whose server stopped once its lease ran out.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryExportWorkerTests : ActionTestBase
{
    private static readonly EntryExporter Owner = new(Editor, MaySeeSensitive: false, "en-US");

    private EntryExportJobs Jobs => GetRequiredService<EntryExportJobs>();

    [Test]
    public async Task AQueuedExport_IsWrittenByThisServersWorker_AsSoonAsItIsQueued()
    {
        var form = await PublishAsync(name: "Worker");
        await SubmitAsync(form, """{"name":"Ada"}""");

        var queued = await Jobs.QueueAsync(form, new EntryExportRequest { Format = "csv" }, Owner, new EntryExportPlan(1, More: false, Background: true));
        var ready = await ReadyAsync(form, queued.Key);

        Assert.Multiple(() =>
        {
            Assert.That(queued.State, Is.EqualTo(EntryExportState.Queued));
            Assert.That((ready.Done, ready.Attempts, ready.Claimant), Is.EqualTo((1, 1, (string?)null)));
            Assert.That(ready.FileName, Does.EndWith("-entries.csv"));
        });
    }

    [Test]
    public async Task TheJob_RemovesWhatExpired_AndWakesTheWorker_WhichTakesOverAnExportWhoseLeaseRanOut()
    {
        var form = await PublishAsync(name: "Taken over");
        await SubmitAsync(form, """{"name":"Ada"}""");
        var values = GetRequiredService<IKeyValueRepository>();
        var now = Clock.GetUtcNow();
        var stopped = Record(form) with { State = EntryExportState.Running, Attempts = 1, Claimant = "stopped/server", LeaseUtc = now - TimeSpan.FromMinutes(1), QueuedUtc = now };
        var expired = Record(form) with { State = EntryExportState.Failed, Problem = "export_failed", QueuedUtc = now - EntryExportJobs.KeptFor, ExpiresUtc = now - TimeSpan.FromMinutes(1) };
        await values.SetAsync(EntryExportJobs.KeyOf(stopped.Key), EntryExportJobs.Write(stopped));
        await values.SetAsync(EntryExportJobs.KeyOf(expired.Key), EntryExportJobs.Write(expired));

        await Services.GetServices<IDistributedBackgroundJob>().OfType<FormBuilderExportJob>().Single().ExecuteAsync(CancellationToken.None);
        var taken = await ReadyAsync(form, stopped.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(await values.GetAsync(EntryExportJobs.KeyOf(expired.Key)), Is.Null, "the expired export's record is gone");
            Assert.That((taken.State, taken.Attempts, taken.Done), Is.EqualTo((EntryExportState.Ready, 2, 1)), "written again from the beginning");
        });
    }

    private static EntryExportRecord Record(Guid form) => new()
    {
        Key = Guid.NewGuid(),
        Owner = Editor,
        Form = form,
        Request = new EntryExportRequest { Format = "csv" },
        Culture = "en-US",
        Total = 1,
    };

    // The export once the worker made it ready; the worker runs on its own, so the test waits, at most half a minute.
    private async Task<EntryExportRecord> ReadyAsync(Guid form, Guid key)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (await Jobs.GetAsync(form, key, Editor) is { State: EntryExportState.Ready } record)
            {
                return record;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The worker did not write the export.");
        return null!;
    }
}
