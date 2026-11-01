using System.IO.Compression;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>Files for the tests of the downloads (ROADMAP 7.3c, ADR-0145): a published form with a plain and a sensitive file field, and a store in memory.</summary>
public abstract class EntryFileApiTestBase : ManagementApiTestBase
{
    private protected static readonly FormField Plain = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000145"),
        Type = "file",
        Alias = "plain",
        Label = new LocalizedText([new(string.Empty, "Plain")]),
    };

    private protected static readonly FormField Secret = Plain with
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000146"),
        Alias = "secret",
        Sensitive = true,
    };

    private protected static readonly FormField Inside = Plain with
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000147"),
        Alias = "inside",
    };

    private static readonly FormField Held = Plain with
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000148"),
        Type = "repeater",
        Alias = "held",
        Sensitive = true,
        Fields = [Inside],
    };

    private readonly MemoryStore _store = new();

    private protected IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private protected IAuditRepository Audit => GetRequiredService<IAuditRepository>();

    private protected Dictionary<string, byte[]> Stored => _store.Files;

    /// <summary>A published form with the two fields; its key and row.</summary>
    private protected async Task<(Guid Key, StoredForm Row)> PublishedFormAsync(string name = "Files")
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(name, UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var definition = draft.Definition! with { Pages = [draft.Definition!.Pages[0] with { Groups = [draft.Definition.Pages[0].Groups[0] with { Fields = [Plain, Secret, Held] }] }] };
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, definition, null, null))!;
        _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return (created.Key, (await GetRequiredService<IFormRepository>().GetAsync(created.Key))!);
    }

    /// <summary>A file with its bytes, on a new entry unless told otherwise.</summary>
    private protected async Task<StoredEntryFile> FileAsync(StoredForm form, string name = "report.pdf", FormField? field = null, string state = FileScanStates.Unscanned, string contentType = "application/pdf", bool onEntry = true, EntryState entryState = EntryState.Submitted, bool isTest = false)
    {
        var id = FormBuilderFileId.New();
        var bytes = "%PDF-bytes"u8.ToArray();
        Stored[id] = bytes;
        var file = (await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), form.Id, (field ?? Plain).Key, name, id, contentType, bytes.Length, new string('a', 64), state)))!;
        if (!onEntry)
        {
            return file;
        }

        await EntryAsync(form, entryState, isTest, file.Id);
        return (await Entries.GetFileAsync(file.Key))!;
    }

    /// <summary>A new entry that takes these uploads.</summary>
    private protected Task EntryAsync(StoredForm form, EntryState state, bool isTest, params int[] files) =>
        Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = form.Id,
            FormVersionId = form.PublishedVersionId!.Value,
            State = state,
            IsTest = isTest,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = files,
        });

    /// <summary>The audit entries of an action.</summary>
    private protected async Task<IReadOnlyList<StoredAuditEntry>> AuditedAsync(string action) =>
        (await Audit.QueryAsync(new AuditQuery { Action = action }, PageRequest.First(50))).Items;

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IFormBuilderFileStore>(_store);
    }

    private sealed class MemoryStore : IFormBuilderFileStore
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(id, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

/// <summary>
/// The downloads (ROADMAP 7.3c, ADR-0006, ADR-0145): every file is an attachment nobody runs or guesses at, only for a user who sees
/// the form's entries, never one a scanner quarantined, a sensitive field's only for one who may see it and audited, and a ZIP that no
/// name leads out of.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryFileApiTests : EntryFileApiTestBase
{
    [Test]
    public async Task AnArchive_IsAudited_BeforeItsFirstByte_AndEvenCutOff_WithItsCounts()
    {
        var (key, row) = await PublishedFormAsync();
        await FileAsync(row, "one.pdf");
        await FileAsync(row, "two.pdf");
        var downloads = GetRequiredService<EntryFileDownloads>();
        using var stopped = new CancellationTokenSource();
        var output = new CutOff(stopped, () => AuditedAsync(EntryFileDownloads.ArchiveStartedAction));

        Assert.That(async () => await downloads.WriteArchiveAsync(key, output, maySeeSensitive: true, null, stopped.Token), Throws.InstanceOf<Exception>());

        var ended = await AuditedAsync(EntryFileDownloads.ArchivedAction);
        Assert.Multiple(() =>
        {
            Assert.That(output.StartedBeforeFirstByte, Is.EqualTo(1), "the entry came before the first byte");
            Assert.That(ended, Has.Count.EqualTo(1), "and one with its counts, though the request was cut off");
            Assert.That(ended[0].Details, Does.Contain("\"finished\":false"));
        });
    }

    [Test]
    public async Task AnArchiveSentToTheEnd_SaysSo()
    {
        var (key, row) = await PublishedFormAsync();
        await FileAsync(row, "one.pdf");
        using var output = new MemoryStream();

        await GetRequiredService<EntryFileDownloads>().WriteArchiveAsync(key, output, maySeeSensitive: true, null, CancellationToken.None);

        var ended = await AuditedAsync(EntryFileDownloads.ArchivedAction);
        Assert.Multiple(() =>
        {
            Assert.That(ended.Single().Details, Does.Contain("\"finished\":true").And.Contain("\"files\":1"));
            Assert.That(ended.Single().EntityKey, Is.EqualTo(key));
        });
    }

    [Test]
    public async Task AFile_IsAnAttachment_WithItsBytesAndName_ThatNoBrowserGuessesAt_OrKeeps()
    {
        var (key, row) = await PublishedFormAsync();
        var file = await FileAsync(row, "Report 2026.pdf");

        using var answer = await Api.SendAsync("viewer", $"form/{key}/files/{file.Key}");

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await answer.Content.ReadAsByteArrayAsync(), Is.EqualTo("%PDF-bytes"u8.ToArray()));
            Assert.That(answer.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(answer.Content.Headers.ContentDisposition!.DispositionType, Is.EqualTo("attachment"));
            Assert.That(answer.Content.Headers.ContentDisposition.FileNameStar, Is.EqualTo("Report 2026.pdf"));
            Assert.That(answer.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            Assert.That(answer.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(answer.Headers.GetValues("Content-Security-Policy").Single(), Is.EqualTo("sandbox; default-src 'none'"));
        });
    }

    [TestCase("drawing.svg", "image/svg+xml", TestName = "An SVG, as whatever its row says")]
    [TestCase("page.html", "text/html", TestName = "An HTML page, as whatever its row says")]
    [TestCase("drawing.svg", "application/octet-stream", TestName = "An SVG, as a site that does not block them stores it")]
    [TestCase("page.html", "application/octet-stream", TestName = "An HTML page, as a site that does not block them stores it")]
    public async Task WhatRunsInABrowser_IsNeverServedInline(string name, string contentType)
    {
        var (key, row) = await PublishedFormAsync();
        var file = await FileAsync(row, name, contentType: contentType);

        using var answer = await Api.SendAsync("viewer", $"form/{key}/files/{file.Key}");

        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer.Content.Headers.ContentDisposition!.DispositionType, Is.EqualTo("attachment"));
            Assert.That(answer.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            Assert.That(answer.Headers.GetValues("Content-Security-Policy").Single(), Does.StartWith("sandbox"));
        });
    }

    [TestCase(null, "401", TestName = "An anonymous caller")]
    [TestCase("outsider", "403 no_section", TestName = "A user without the section")]
    [TestCase("noAccess", "404 form_not_found", TestName = "A user of the section who does not see the form")]
    [TestCase("designer", "403 forbidden", TestName = "A designer")]
    [TestCase("viewer", "200", TestName = "A user who sees the entries")]
    [TestCase("admin", "200", TestName = "An administrator")]
    public async Task ADownload_NeedsThePermission(string? role, string outcome)
    {
        var (key, row) = await PublishedFormAsync();
        var file = await FileAsync(row);

        using var answer = await Api.SendAsync(role, $"form/{key}/files/{file.Key}");

        Assert.That(await OutcomeAsync(answer), Is.EqualTo(outcome));
    }

    [Test]
    public async Task WhatNoEntryOfTheFormHolds_IsNotFound()
    {
        var (key, row) = await PublishedFormAsync();
        var (_, other) = await PublishedFormAsync("Others");
        var waiting = await FileAsync(row, onEntry: false);
        var elsewhere = await FileAsync(other);
        var gone = await FileAsync(row);
        Stored.Remove(gone.StoredName);

        foreach (var file in new[] { waiting.Key, elsewhere.Key, gone.Key, Guid.NewGuid() })
        {
            using var answer = await Api.SendAsync("admin", $"form/{key}/files/{file}");

            Assert.That(await OutcomeAsync(answer), Is.EqualTo("404 file_not_found"));
        }
    }

    [Test]
    public async Task AQuarantinedFile_NobodyGets()
    {
        var (key, row) = await PublishedFormAsync();
        var file = await FileAsync(row, state: FileScanStates.Pending);
        await Entries.ChangeScanStateAsync(file.Id, FileScanStates.Pending, FileScanStates.Quarantined);

        using var answer = await Api.SendAsync("admin", $"form/{key}/files/{file.Key}");

        Assert.That(await OutcomeAsync(answer), Is.EqualTo("409 file_quarantined"));
    }

    [TestCase(FileScanStates.Pending, TestName = "One that waits on a site without a scanner")]
    [TestCase(FileScanStates.Unscanned, TestName = "One no scanner saw")]
    [TestCase(FileScanStates.Clean, TestName = "A clean one")]
    public async Task AFile_NothingHoldsBack_IsServed(string state)
    {
        var (key, row) = await PublishedFormAsync();
        var file = await FileAsync(row, state: state);

        using var answer = await Api.SendAsync("viewer", $"form/{key}/files/{file.Key}");

        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ASensitiveFieldsFile_NeedsThatPermission_AndItsReadIsAudited()
    {
        var (key, row) = await PublishedFormAsync();
        var file = await FileAsync(row, field: Secret);
        var plain = await FileAsync(row);

        using var viewer = await Api.SendAsync("viewer", $"form/{key}/files/{file.Key}");
        var beforeRead = await AuditedAsync(EntryFileDownloads.DownloadedAction);
        using var officer = await Api.SendAsync("privacyOfficer", $"form/{key}/files/{file.Key}");
        using var plainRead = await Api.SendAsync("privacyOfficer", $"form/{key}/files/{plain.Key}");

        var audited = await AuditedAsync(EntryFileDownloads.DownloadedAction);
        Assert.Multiple(async () =>
        {
            Assert.That(await OutcomeAsync(viewer), Is.EqualTo("403 forbidden"));
            Assert.That(beforeRead, Is.Empty);
            Assert.That(officer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(audited, Has.Count.EqualTo(1), "the sensitive read only");
            Assert.That(audited[0].EntityType, Is.EqualTo(EntryFileDownloads.FileEntityType));
            Assert.That(audited[0].EntityKey, Is.EqualTo(file.Key));
            Assert.That(audited[0].UserKey, Is.Not.Null);
            Assert.That(audited[0].Details, Does.Contain(key.ToString()).And.Contain(Secret.Key.ToString()).And.Not.Contain("report"));
        });
    }

    [Test]
    public async Task AFieldInsideASensitiveOne_CountsAsSensitive_AndSoDoesOneTheCatalogueDoesNotKnow()
    {
        var (key, row) = await PublishedFormAsync();
        var inside = await FileAsync(row, field: Inside);
        var unknown = await FileAsync(row, field: Plain with { Key = Guid.NewGuid() });

        using var insideRead = await Api.SendAsync("viewer", $"form/{key}/files/{inside.Key}");
        using var unknownRead = await Api.SendAsync("viewer", $"form/{key}/files/{unknown.Key}");
        using var officer = await Api.SendAsync("privacyOfficer", $"form/{key}/files/{inside.Key}");

        Assert.Multiple(async () =>
        {
            Assert.That(await OutcomeAsync(insideRead), Is.EqualTo("403 forbidden"));
            Assert.That(await OutcomeAsync(unknownRead), Is.EqualTo("403 forbidden"));
            Assert.That(officer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [TestCase(null, "401", TestName = "The archive for an anonymous caller")]
    [TestCase("viewer", "403 forbidden", TestName = "The archive for a user who only sees the entries")]
    [TestCase("designer", "403 forbidden", TestName = "The archive for a designer")]
    [TestCase("entryHandler", "200", TestName = "The archive for a user who may export")]
    public async Task TheArchive_NeedsItsPermission(string? role, string outcome)
    {
        var (key, row) = await PublishedFormAsync();
        await FileAsync(row);

        using var answer = await Api.SendAsync(role, $"form/{key}/files/archive");

        Assert.That(await OutcomeAsync(answer), Is.EqualTo(outcome));
    }

    [Test]
    public async Task TheArchive_IsAnAttachment_ThatHoldsWhatTheFormsEntriesGiveOut_AndNoNameLeadsOutOfIt()
    {
        var (key, row) = await PublishedFormAsync();
        var kept = await FileAsync(row, "report.pdf");
        var slip = await FileAsync(row, "../../evil.exe");
        var windows = await FileAsync(row, "C:" + (char)92 + "Windows" + (char)92 + "evil.dll");
        await FileAsync(row, "spam.pdf", entryState: EntryState.Spam);
        await FileAsync(row, "test.pdf", isTest: true);
        await FileAsync(row, "draft.pdf", entryState: EntryState.Draft);
        await FileAsync(row, "waiting.pdf", onEntry: false);
        var bad = await FileAsync(row, "virus.pdf", state: FileScanStates.Pending);
        await Entries.ChangeScanStateAsync(bad.Id, FileScanStates.Pending, FileScanStates.Quarantined);
        await FileAsync(row, "secret.pdf", field: Secret);
        var gone = await FileAsync(row, "gone.pdf");
        Stored.Remove(gone.StoredName);
        var (_, other) = await PublishedFormAsync("Others");
        await FileAsync(other, "other.pdf");

        using var answer = await Api.SendAsync("entryHandler", $"form/{key}/files/archive");
        var bytes = await answer.Content.ReadAsByteArrayAsync();
        using var read = new MemoryStream(bytes);
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        await using var first = await archive.Entries[0].OpenAsync();
        using var firstBytes = new MemoryStream();
        await first.CopyToAsync(firstBytes);

        var audited = await AuditedAsync(EntryFileDownloads.ArchivedAction);
        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/zip"));
            Assert.That(answer.Content.Headers.ContentDisposition!.DispositionType, Is.EqualTo("attachment"));
            Assert.That(answer.Content.Headers.ContentDisposition.FileName, Is.EqualTo("files.zip"));
            Assert.That(answer.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            Assert.That(answer.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(names.Select(name => name[(name.IndexOf('/', StringComparison.Ordinal) + 1)..]), Is.EqualTo(new[] { "report.pdf", "evil.exe", "evil.dll" }), "in the order they came, each one plain segment");
            Assert.That(names[0], Is.EqualTo($"entry-{kept.EntryId}/report.pdf"));
            Assert.That(names[1], Is.EqualTo($"entry-{slip.EntryId}/evil.exe"));
            Assert.That(names[2], Is.EqualTo($"entry-{windows.EntryId}/evil.dll"));
            Assert.That(firstBytes.ToArray(), Is.EqualTo("%PDF-bytes"u8.ToArray()));
            Assert.That(archive.Entries[0].LastWriteTime.DateTime, Is.EqualTo(kept.CreatedUtc.UtcDateTime).Within(TimeSpan.FromSeconds(2)), "when it came, not when it was packed");
            Assert.That(audited, Has.Count.EqualTo(1));
            Assert.That(audited[0].EntityKey, Is.EqualTo(key));
            Assert.That(audited[0].UserKey, Is.Not.Null);
            Assert.That(audited[0].Details, Is.EqualTo("""{"files":3,"leftOut":3,"sensitive":false,"finished":true}"""));
        });
    }

    [Test]
    public async Task TheArchive_HoldsASensitiveFieldsFile_ForAUserWhoMaySeeIt_AndSaysSo()
    {
        var (key, row) = await PublishedFormAsync();
        await FileAsync(row, "secret.pdf", field: Secret);

        using var answer = await Api.SendAsync("admin", $"form/{key}/files/archive");
        using var read = new MemoryStream(await answer.Content.ReadAsByteArrayAsync());
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);

        var audited = await AuditedAsync(EntryFileDownloads.ArchivedAction);
        Assert.Multiple(() =>
        {
            Assert.That(archive.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "secret.pdf" }));
            Assert.That(audited[0].Details, Is.EqualTo("""{"files":1,"leftOut":0,"sensitive":true,"finished":true}"""));
        });
    }

    [Test]
    public async Task TheArchive_OfAFormWithoutFiles_IsAnEmptyZip()
    {
        var (key, _) = await PublishedFormAsync();

        using var answer = await Api.SendAsync("entryHandler", $"form/{key}/files/archive");
        using var read = new MemoryStream(await answer.Content.ReadAsByteArrayAsync());
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);

        var audited = await AuditedAsync(EntryFileDownloads.ArchivedAction);
        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(archive.Entries, Is.Empty);
            Assert.That(audited[0].Details, Is.EqualTo("""{"files":0,"leftOut":0,"sensitive":false,"finished":true}"""));
        });
    }

    [Test]
    public async Task TheArchive_GoesOnPastAPage()
    {
        var (key, row) = await PublishedFormAsync();
        var files = new List<int>();
        for (var number = 0; number < 252; number++)
        {
            files.Add((await FileAsync(row, onEntry: false)).Id);
        }

        await EntryAsync(row, EntryState.Submitted, isTest: false, [.. files]);

        using var answer = await Api.SendAsync("admin", $"form/{key}/files/archive");
        using var read = new MemoryStream(await answer.Content.ReadAsByteArrayAsync());
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);

        Assert.Multiple(() =>
        {
            Assert.That(archive.Entries, Has.Count.EqualTo(252));
            Assert.That(archive.Entries[1].Name, Is.EqualTo("report (2).pdf"));
        });
    }

    private static async Task<string> OutcomeAsync(HttpResponseMessage answer)
    {
        var status = ((int)answer.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (answer.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) != true)
        {
            return status;
        }

        var body = System.Text.Json.Nodes.JsonNode.Parse(await answer.Content.ReadAsStringAsync());
        return body?["code"] is { } code ? $"{status} {code}" : status;
    }
}

/// <summary>The downloads on a site with a scanner (ROADMAP 7.3c, ADR-0144, ADR-0145): what waits for it waits for its answer.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryFileApiScannerTests : EntryFileApiTestBase
{
    [Test]
    public async Task AFileThatWaitsForTheScanner_IsNotServedYet_AndAClean_OneIs()
    {
        var (key, row) = await PublishedFormAsync();
        var waiting = await FileAsync(row, state: FileScanStates.Pending);
        var clean = await FileAsync(row, state: FileScanStates.Clean);

        var held = await CallAsync("admin", HttpMethod.Get, $"form/{key}/files/{waiting.Key}");
        using var served = await Api.SendAsync("admin", $"form/{key}/files/{clean.Key}");

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome, Is.EqualTo("409 file_not_scanned"));
            Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task TheArchive_LeavesOutWhatWaits()
    {
        var (key, row) = await PublishedFormAsync();
        await FileAsync(row, "waiting.pdf", state: FileScanStates.Pending);
        await FileAsync(row, "clean.pdf", state: FileScanStates.Clean);

        using var answer = await Api.SendAsync("admin", $"form/{key}/files/archive");
        using var read = new MemoryStream(await answer.Content.ReadAsByteArrayAsync());
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);

        Assert.That(archive.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "clean.pdf" }));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().FileScanners().Append<Answering>();
    }

    private sealed class Answering : IFormBuilderFileScanner
    {
        public Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken) => Task.FromResult(FormBuilderFileScanOutcome.Unavailable);
    }
}

// Counts what the audit holds at the first write, then cuts the archive off, as a visitor leaving does.
file sealed class CutOff(CancellationTokenSource stopped, Func<Task<IReadOnlyList<StoredAuditEntry>>> started) : MemoryStream
{
    public int? StartedBeforeFirstByte { get; private set; }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        StartedBeforeFirstByte ??= (await started()).Count;
        await stopped.CancelAsync();
        throw new IOException("The connection is gone.");
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}
