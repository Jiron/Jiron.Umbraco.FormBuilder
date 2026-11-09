using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Exports;

/// <summary>Where a background export stands.</summary>
internal enum EntryExportState
{
    /// <summary>Waiting for a worker.</summary>
    Queued,

    /// <summary>A worker writes it.</summary>
    Running,

    /// <summary>Its file waits for its owner until it expires.</summary>
    Ready,

    /// <summary>It could not be written; nothing to download.</summary>
    Failed,
}

/// <summary>
/// A background export's record in <c>jfbKeyValue</c> (ADR-0210): who asked for what, where it stands, and its file once it
/// is ready. A worker that runs it holds a lease it renews after each page and every minute meanwhile, so another server
/// restarts it only once the lease ran out.
/// </summary>
internal sealed record EntryExportRecord
{
    /// <summary>The export.</summary>
    public required Guid Key { get; init; }

    /// <summary>The user who asked for it, the only one who sees it and downloads its file.</summary>
    public required Guid Owner { get; init; }

    /// <summary>The form.</summary>
    public required Guid Form { get; init; }

    /// <summary>What the user asked for.</summary>
    public required EntryExportRequest Request { get; init; }

    /// <summary>Whether the user held <c>viewSensitive</c> on the form when they asked.</summary>
    public bool MaySeeSensitive { get; init; }

    /// <summary>The user's backoffice language, for the export's own words.</summary>
    public required string Culture { get; init; }

    /// <summary>Where it stands.</summary>
    public EntryExportState State { get; init; }

    /// <summary>The entries the plan counted; a lower bound when <see cref="More"/>.</summary>
    public int Total { get; init; }

    /// <summary>Whether a search found more than it counts.</summary>
    public bool More { get; init; }

    /// <summary>The entries written so far, or in all once it is ready.</summary>
    public int Done { get; init; }

    /// <summary>How often a worker started it.</summary>
    public int Attempts { get; init; }

    /// <summary>The worker that holds it while it runs.</summary>
    public string? Claimant { get; init; }

    /// <summary>When a running export's lease runs out.</summary>
    public DateTimeOffset? LeaseUtc { get; init; }

    /// <summary>The file's id in the file store, once it is ready.</summary>
    public string? File { get; init; }

    /// <summary>The name the file downloads under.</summary>
    public string? FileName { get; init; }

    /// <summary>The file's media type.</summary>
    public string? ContentType { get; init; }

    /// <summary>When the user asked for it.</summary>
    public DateTimeOffset QueuedUtc { get; init; }

    /// <summary>When its file was ready.</summary>
    public DateTimeOffset? ReadyUtc { get; init; }

    /// <summary>When its file and record go: <see cref="EntryExportJobs.KeptFor"/> after it was ready or failed.</summary>
    public DateTimeOffset? ExpiresUtc { get; init; }

    /// <summary>Why it failed, as a code: <c>form_not_found</c>, <c>export_invalid</c> or <c>export_failed</c>.</summary>
    public string? Problem { get; init; }
}

/// <summary>A background export's file, opened for its owner.</summary>
/// <param name="Content">Its bytes, for the caller to dispose.</param>
/// <param name="FileName">The name it downloads under.</param>
/// <param name="ContentType">Its media type.</param>
internal sealed record EntryExportDownload(Stream Content, string FileName, string ContentType);

/// <summary>
/// Exports that run in the background (ADR-0210): an export of more than <see cref="EntryExports.StreamedAtMost"/> entries,
/// or of a search that counts more than it can, is queued as a record in <c>jfbKeyValue</c>; the server that queued it
/// writes it at once on its worker, page by page, into a temporary file and then into the file store, and the distributed
/// job restarts one whose server stopped and removes a file and its record <see cref="KeptFor"/> after it was ready. Only
/// its owner sees it and downloads its file, and only until it expires.
/// </summary>
/// <param name="values">The records.</param>
/// <param name="forms">Whether an export's form is still there.</param>
/// <param name="exports">Writes the exports.</param>
/// <param name="people">Writes the exports of a person's data (ADR-0223).</param>
/// <param name="store">The files.</param>
/// <param name="wake">The worker's wake.</param>
/// <param name="events">Where a finished export is announced.</param>
/// <param name="time">The clock.</param>
/// <param name="timing">How often a worker renews its lease.</param>
/// <param name="logger">Where a failed export is told.</param>
internal sealed partial class EntryExportJobs(
    IKeyValueRepository values,
    IFormRepository forms,
    EntryExports exports,
    PersonExports people,
    IFormBuilderFileStore store,
    EntryExportWake wake,
    IEventAggregator events,
    TimeProvider time,
    EntryExportTiming timing,
    ILogger<EntryExportJobs> logger)
{
    /// <summary>The start of a record's key; the export's key follows.</summary>
    public const string KeyPrefix = "jfb.export.";

    /// <summary>The most exports a user has queued or running at once.</summary>
    public const int MaxUnderWay = 3;

    /// <summary>How often a worker starts an export before it counts as failed.</summary>
    public const int MaxAttempts = 3;

    /// <summary>How long a finished or failed export is kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromHours(24);

    /// <summary>How long a worker holds an export without a word; it renews the lease after each page and every minute.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = FrozenOptions.Of(new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16,
    });

    /// <summary>Queues an export the plan sends to the background, and wakes this server's worker.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="request">What the user asks for, checked by the plan.</param>
    /// <param name="exporter">Who asks.</param>
    /// <param name="plan">The plan's count.</param>
    /// <param name="cancellationToken">Cancels the queueing.</param>
    /// <exception cref="FormBuilderValidationException">The user has <see cref="MaxUnderWay"/> exports under way.</exception>
    public async Task<EntryExportRecord> QueueAsync(Guid formKey, EntryExportRequest request, EntryExporter exporter, EntryExportPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(plan);
        var underWay = (await ListAsync(cancellationToken)).Count(record => record.Owner == exporter.UserKey && record.State is EntryExportState.Queued or EntryExportState.Running);
        if (underWay >= MaxUnderWay)
        {
            throw FormBuilderValidationException.For("exports_busy", "format", $"You have {MaxUnderWay} exports under way. Wait for one to finish.");
        }

        var record = new EntryExportRecord
        {
            Key = Guid.NewGuid(),
            Owner = exporter.UserKey,
            Form = formKey,
            Request = request,
            MaySeeSensitive = exporter.MaySeeSensitive,
            Culture = exporter.Culture,
            State = EntryExportState.Queued,
            Total = plan.Entries,
            More = plan.More,
            QueuedUtc = time.GetUtcNow(),
        };
        await values.SetAsync(KeyOf(record.Key), Write(record), cancellationToken);
        wake.Wake();
        return record;
    }

    /// <summary>
    /// Queues the export of everything about a person (ADR-0223), by the keys of their entries, and wakes this server's
    /// worker. Its record stands for no form (<see cref="PersonExports.Scope"/>) and keeps no address.
    /// </summary>
    /// <param name="entryKeys">The person's entries.</param>
    /// <param name="exporter">Who asks.</param>
    /// <param name="cancellationToken">Cancels the queueing.</param>
    /// <exception cref="FormBuilderValidationException">The user has <see cref="MaxUnderWay"/> exports under way.</exception>
    public Task<EntryExportRecord> QueuePersonAsync(IReadOnlyList<Guid> entryKeys, EntryExporter exporter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        var request = new EntryExportRequest { Format = PersonExports.FormatName, Entries = entryKeys, Sensitive = true };
        return QueueAsync(PersonExports.Scope, request, exporter with { MaySeeSensitive = true }, new EntryExportPlan(entryKeys.Count, More: false, Background: true), cancellationToken);
    }

    /// <summary>An export of the form that the user asked for; null for any other.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="key">The export.</param>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<EntryExportRecord?> GetAsync(Guid formKey, Guid key, Guid userKey, CancellationToken cancellationToken = default) =>
        Read(await values.GetAsync(KeyOf(key), cancellationToken)) is { } record && record.Owner == userKey && record.Form == formKey && !Expired(record) ? record : null;

    /// <summary>The user's exports of the form that have not expired, newest first.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<IReadOnlyList<EntryExportRecord>> MineAsync(Guid formKey, Guid userKey, CancellationToken cancellationToken = default) =>
        [.. (await ListAsync(cancellationToken)).Where(record => record.Owner == userKey && record.Form == formKey && !Expired(record)).OrderByDescending(record => record.QueuedUtc)];

    /// <summary>The file of a ready export of the form that the user asked for; null for any other, or once it expired.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="key">The export.</param>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">Cancels opening the file.</param>
    public async Task<EntryExportDownload?> OpenAsync(Guid formKey, Guid key, Guid userKey, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(formKey, key, userKey, cancellationToken) is not { State: EntryExportState.Ready, File: { } file, FileName: { } name, ContentType: { } type })
        {
            return null;
        }

        return await store.OpenReadAsync(file, cancellationToken) is { } content ? new EntryExportDownload(content, name, type) : null;
    }

    /// <summary>
    /// One pass of this server's worker: it claims each queued export, and each running one whose lease ran out, and writes
    /// them one after the other.
    /// </summary>
    /// <param name="claimant">The name its claims carry: the server and its worker.</param>
    /// <param name="stopping">Cancelled when the host stops.</param>
    public async Task PassAsync(string claimant, CancellationToken stopping)
    {
        foreach (var (key, json) in await values.ListAsync(KeyPrefix, stopping))
        {
            stopping.ThrowIfCancellationRequested();
            if (json is null || Read(json) is not { } record)
            {
                continue;
            }

            var now = time.GetUtcNow();
            var due = record.State == EntryExportState.Queued || (record.State == EntryExportState.Running && (record.LeaseUtc is not { } lease || lease < now));
            if (due)
            {
                await RunAsync(key, json, record, claimant, stopping);
            }
        }
    }

    /// <summary>
    /// Removes each export whose time is up, and each of a form deleted for good, whose entries are gone: its file, then
    /// its record.
    /// </summary>
    /// <param name="cancellationToken">Cancels the clean-up between exports.</param>
    /// <returns>The number of exports removed.</returns>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        var removed = 0;
        foreach (var (key, json) in await values.ListAsync(KeyPrefix, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = json is null ? null : Read(json);
            if (record is not null && !Expired(record) && (record.Form == PersonExports.Scope || await forms.GetAsync(record.Form, cancellationToken) is not null))
            {
                continue;
            }

            if (record?.File is { } file)
            {
                await store.DeleteAsync(file, cancellationToken);
            }

            if (json is null ? await values.DeleteAsync(key, cancellationToken) : await values.TryDeleteAsync(key, json, cancellationToken))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>A record's key.</summary>
    internal static string KeyOf(Guid key) => KeyPrefix + key.ToString("N");

    /// <summary>A record as it is stored.</summary>
    internal static string Write(EntryExportRecord record) => JsonSerializer.Serialize(record, Json);

    /// <summary>A stored record; null for one that cannot be read, which the clean-up removes.</summary>
    internal static EntryExportRecord? Read(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EntryExportRecord>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Every record that can be read.
    private async Task<IReadOnlyList<EntryExportRecord>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await values.ListAsync(KeyPrefix, cancellationToken)).Select(pair => Read(pair.Value)).OfType<EntryExportRecord>()];

    // Whether its time is up: kept for a day once it is ready or failed, and a record that never started for as long
    // after it was queued (a server that queued it and then never ran a worker again).
    private bool Expired(EntryExportRecord record)
    {
        var now = time.GetUtcNow();
        return record.State switch
        {
            EntryExportState.Ready or EntryExportState.Failed => record.ExpiresUtc is not { } expires || expires <= now,
            _ => record.QueuedUtc + KeptFor + KeptFor <= now,
        };
    }

    // Claims an export and writes it: into a temporary file, then into the store, renewing the lease after each page and
    // every minute meanwhile. Losing the record on the way (another server took it over, or the clean-up removed it) stops
    // the export, and so does the host's stop; what it stored goes again. Any other failure, a cancellation it did not ask
    // for among them, counts as a failed attempt.
    private async Task RunAsync(string key, string json, EntryExportRecord record, string claimant, CancellationToken stopping)
    {
        var now = time.GetUtcNow();
        var attempt = record.Attempts + 1;
        var current = record with { State = EntryExportState.Running, Claimant = claimant, LeaseUtc = now + Lease, Attempts = attempt, Done = 0 };
        if (attempt > MaxAttempts)
        {
            await values.TryReplaceAsync(key, json, Write(Failed(record, "export_failed")), stopping);
            return;
        }

        var held = Write(current);
        if (!await values.TryReplaceAsync(key, json, held, stopping))
        {
            return;
        }

        using var lost = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        using var turn = new SemaphoreSlim(1, 1);

        // Renews the lease, with the entries written so far when a page tells them, one renewal at a time; losing the
        // record stops the export.
        async Task RenewAsync(int? done)
        {
            await turn.WaitAsync(CancellationToken.None);
            try
            {
                var next = current with { Done = done ?? current.Done, LeaseUtc = time.GetUtcNow() + Lease };
                var nextJson = Write(next);
                if (await values.TryReplaceAsync(key, held, nextJson, CancellationToken.None))
                {
                    (current, held) = (next, nextJson);
                }
                else
                {
                    await lost.CancelAsync();
                }
            }
            finally
            {
                turn.Release();
            }
        }

        string? file = null;
        try
        {
            EntryExportFile? written = null;
            using (var keeping = new CancellationTokenSource())
            {
                var kept = KeepLeaseAsync(() => RenewAsync(null), keeping.Token);
                try
                {
                    Directory.CreateDirectory(TempFolder);
                    await using var temp = new FileStream(Path.Combine(TempFolder, Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                    var exporter = new EntryExporter(record.Owner, record.MaySeeSensitive, record.Culture);
                    written = record.Form == PersonExports.Scope
                        ? await people.WriteAsync(record.Request.Entries, exporter, temp, done => RenewAsync(done), lost.Token)
                        : await exports.WriteAsync(record.Form, record.Request, exporter, temp, done => RenewAsync(done), announce: false, lost.Token);
                    if (written is not null)
                    {
                        temp.Position = 0;
                        file = FormBuilderFileId.New();
                        await store.SaveAsync(file, temp, lost.Token);
                    }
                }
                finally
                {
                    // What follows replaces the record as the last renewal left it.
                    await keeping.CancelAsync();
                    await kept;
                }
            }

            if (written is null)
            {
                await values.TryReplaceAsync(key, held, Write(Failed(current, "form_not_found")), CancellationToken.None);
                return;
            }

            var ready = time.GetUtcNow();
            var finished = current with
            {
                State = EntryExportState.Ready,
                Done = written.Entries,
                Claimant = null,
                LeaseUtc = null,
                File = file,
                FileName = written.FileName,
                ContentType = written.ContentType,
                ReadyUtc = ready,
                ExpiresUtc = ready + KeptFor,
            };
            if (!await values.TryReplaceAsync(key, held, Write(finished), CancellationToken.None))
            {
                await DeleteQuietlyAsync(file);
                return;
            }

            // A person's export is no form's, and the site hears nothing of it.
            if (record.Form != PersonExports.Scope)
            {
                await events.PublishAsync(new ExportCompletedNotification(record.Form, record.Request.Format, written.Entries, record.Owner), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (lost.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            // Another server holds it now, or it is gone; the one that holds it writes its own file.
            await DeleteQuietlyAsync(file);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The host stops: what it stored goes, as no record names it, and another server starts the export again once
            // the lease ran out.
            await DeleteQuietlyAsync(file);
            throw;
        }
        catch (FormBuilderValidationException)
        {
            // The site no longer has the format, or a field went: the user asks again from the dialog.
            await DeleteQuietlyAsync(file);
            await values.TryReplaceAsync(key, held, Write(Failed(current, "export_invalid")), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failure, or a cancellation that neither the host nor a lost record asked for, as a file store's timeout.
            LogFailed(logger, record.Key, attempt, exception);
            await DeleteQuietlyAsync(file);
            var again = attempt >= MaxAttempts ? Failed(current, "export_failed") : current with { State = EntryExportState.Queued, Claimant = null, LeaseUtc = null, Done = 0 };
            await values.TryReplaceAsync(key, held, Write(again), CancellationToken.None);
        }
    }

    // Renews the lease every so often until it is stopped, as a page of many files or the storing of a large file can take
    // longer than the lease; a renewal that fails is told, and the next one tries again.
    private async Task KeepLeaseAsync(Func<Task> renew, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(timing.RenewEvery, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    await renew();
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                {
                    LogLeaseNotRenewed(logger, exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Written and stored, or stopped.
        }
    }

    private EntryExportRecord Failed(EntryExportRecord record, string problem)
    {
        var now = time.GetUtcNow();
        return record with { State = EntryExportState.Failed, Problem = problem, Claimant = null, LeaseUtc = null, File = null, ExpiresUtc = now + KeptFor };
    }

    private async Task DeleteQuietlyAsync(string? file)
    {
        if (file is null)
        {
            return;
        }

        try
        {
            await store.DeleteAsync(file, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileLeft(logger, exception.GetType().Name);
        }
    }

    /// <summary>Where a worker writes an export before the file store takes it.</summary>
    internal static string TempFolder => XlsxTableSheets.Folder;

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's export {ExportKey} failed on attempt {Attempt}")]
    private static partial void LogFailed(ILogger logger, Guid exportKey, int attempt, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not delete the file of an export it stopped ({ExceptionType}); the store keeps it")]
    private static partial void LogFileLeft(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not renew the lease of an export it writes ({ExceptionType}); it tries again")]
    private static partial void LogLeaseNotRenewed(ILogger logger, string exceptionType);
}

/// <summary>
/// How often a worker renews the lease of the export it writes, besides after each page: a fifth of the lease, so four
/// renewals may fail before another server takes the export over.
/// </summary>
/// <param name="RenewEvery">The time between renewals.</param>
internal sealed record EntryExportTiming(TimeSpan RenewEvery)
{
    /// <summary>A renewal every minute.</summary>
    public static EntryExportTiming Default { get; } = new(EntryExportJobs.Lease / 5);
}

/// <summary>What wakes this server's export worker: a bounded channel of one that drops further writes, so wakes add up to one.</summary>
internal sealed class EntryExportWake
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Asks for a pass.</summary>
    public void Wake() => _channel.Writer.TryWrite(true);

    /// <summary>Waits for the next wake.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);
}

/// <summary>
/// This server's export worker (ADR-0210): it waits for a wake and runs a pass, so exports run one after the other on a
/// server, while Umbraco runs and no migration is pending. Queueing an export wakes it at once, and the job each minute.
/// </summary>
/// <param name="wake">Its wake.</param>
/// <param name="services">Where it finds the jobs and the runtime's state when the first wake comes.</param>
/// <param name="logger">Where a failed pass is told.</param>
internal sealed partial class EntryExportWorker(EntryExportWake wake, IServiceProvider services, ILogger<EntryExportWorker> logger) : BackgroundService
{
    private readonly string _claimant = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await wake.WaitAsync(stoppingToken);
                try
                {
                    if (services.GetRequiredService<IRuntimeState>().Level == RuntimeLevel.Run && !await services.GetRequiredService<IFormBuilderMigrationState>().IsPendingAsync(stoppingToken))
                    {
                        await services.GetRequiredService<EntryExportJobs>().PassAsync(_claimant, stoppingToken);
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException && !stoppingToken.IsCancellationRequested)
                {
                    // A cancellation the host did not ask for, as a database's timeout, ends the pass, not the worker.
                    LogPassFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host stops; an export under way is restarted elsewhere once its lease runs out.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of Jiron Form Builder's export worker failed; the next wake tries again")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}

/// <summary>
/// The exports' job (ADR-0210): one server of the cluster runs it every minute, while Umbraco runs and no migration is
/// pending. It removes what expired and wakes its server's worker, which restarts an export whose server stopped; it
/// returns at once, as Umbraco's runner holds every other distributed job of the server while it waits.
/// </summary>
/// <param name="runtimeState">Whether Umbraco runs.</param>
/// <param name="migrationState">Whether a migration is pending.</param>
/// <param name="jobs">The exports.</param>
/// <param name="wake">The worker's wake.</param>
/// <param name="logger">Where a failed clean-up is told.</param>
internal sealed partial class FormBuilderExportJob(
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    EntryExportJobs jobs,
    EntryExportWake wake,
    ILogger<FormBuilderExportJob> logger)
    : IDistributedBackgroundJob
{
    /// <summary>The job's name in Umbraco's job table.</summary>
    public const string JobName = "Jiron.FormBuilder.Exports";

    /// <inheritdoc />
    public string Name => JobName;

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(cancellationToken))
        {
            return;
        }

        try
        {
            await jobs.CleanUpAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            LogFailed(logger, Period.TotalSeconds.ToString(CultureInfo.InvariantCulture), exception);
        }

        wake.Wake();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's clean-up of exports failed; the job runs again in {Seconds} s")]
    private static partial void LogFailed(ILogger logger, string seconds, Exception exception);
}
