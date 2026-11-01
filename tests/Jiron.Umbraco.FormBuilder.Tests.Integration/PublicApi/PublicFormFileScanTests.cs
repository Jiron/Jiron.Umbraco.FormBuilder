using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A site with a file scanner (ROADMAP 7.3b, ADR-0144): an upload waits, the job scans what waits on its own timer, a clean file
/// goes into its entry, a quarantined one is refused at its field, and an entry that took a file before its quarantine is
/// marked. The stub quarantines what its name calls so, as the sample will.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormFileScanTests : PublicApiTestBase
{
    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000144"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText([new(string.Empty, "Attachment")]),
    };

    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8];

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private FileScanJob Job => GetRequiredService<IEnumerable<IRecurringBackgroundJob>>().OfType<FileScanJob>().Single();

    [Test]
    public void TheJob_RunsOnTheServerThatSchedules_EveryHalfMinute()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Job.Period, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(Job.Delay, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(Job.ServerRoles, Is.EquivalentTo(new[] { global::Umbraco.Cms.Core.Sync.ServerRole.Single, global::Umbraco.Cms.Core.Sync.ServerRole.SchedulingPublisher }));
        });
    }

    [Test]
    public async Task AnUpload_WaitsForItsScan_AndACleanFile_GoesIntoItsEntry()
    {
        var key = await PublishAsync(fields: Attachment);
        var token = await UploadAsync(key, "report.pdf");
        var waiting = await WaitingAsync();

        await Job.RunJobAsync();
        using var answer = await SubmitAsync(key, token);

        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(waiting, Is.EqualTo(1), "a site with a scanner: it waits");
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That((await Entries.ListFilesAsync(entryId)).Single().ScanState, Is.EqualTo(FileScanStates.Clean));
            Assert.That(await WaitingAsync(), Is.Zero);
        });
    }

    [Test]
    public async Task AQuarantinedFile_IsRefusedAtItsField_AndNoEntryIsMade()
    {
        var key = await PublishAsync(fields: Attachment);
        var token = await UploadAsync(key, "eicar.pdf");

        await Job.RunJobAsync();
        using var answer = await SubmitAsync(key, token);

        var body = await JsonAsync(answer);
        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body["errors"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("attachment"));
            Assert.That(await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10), Is.Empty);
        });
    }

    [Test]
    public async Task APagesCheck_RefusesAQuarantinedFile_AtItsField()
    {
        var key = await PublishAsync(fields: Attachment);
        var token = await UploadAsync(key, "eicar.pdf");
        await Job.RunJobAsync();
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;

        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/validate", request => request.Content = new StringContent($$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"attachment":["{{{token}}}"]}}""", Encoding.UTF8, "application/json"));

        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await answer.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AnEntryThatTookAFile_BeforeItsQuarantine_IsMarked()
    {
        var key = await PublishAsync(fields: Attachment);
        var token = await UploadAsync(key, "eicar.pdf");
        using var answer = await SubmitAsync(key, token);
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();

        await Job.RunJobAsync();

        var events = await GetRequiredService<IEntryEventRepository>().ListForEntryAsync(entryId);
        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.ListFilesAsync(entryId)).Single().ScanState, Is.EqualTo(FileScanStates.Quarantined));
            Assert.That(events.Select(one => one.Kind), Does.Contain(FileScans.QuarantinedEvent));
            Assert.That((await Entries.GetByIdAsync(entryId))!.State, Is.Not.EqualTo(EntryState.Spam), "a mark, and no judgement on the rest of the entry");
        });
    }

    [Test]
    public async Task AScannerThatCannotSay_LeavesItWaiting_AndAskedAgain_ItAnswers()
    {
        var key = await PublishAsync(fields: Attachment);
        await UploadAsync(key, "later.pdf");
        await UploadAsync(key, "report.pdf");
        NamedScanner.Down = true;

        await Job.RunJobAsync();
        var whileDown = await WaitingAsync();
        NamedScanner.Down = false;
        await Job.RunJobAsync();
        await Job.RunJobAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(whileDown, Is.EqualTo(2));
            Assert.That(await WaitingAsync(), Is.Zero, "the run that reached the end starts over");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(global::Umbraco.Cms.Core.DependencyInjection.IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        NamedScanner.Down = false;
        builder.JironFormBuilder().FileScanners().Append<NamedScanner>();
    }

    private async Task<int> WaitingAsync() => (await Entries.ListEntryFilesToScanAsync(0, 100)).Count + (await Entries.ListUploadsToScanAsync(0, 100)).Count;

    private async Task<string> UploadAsync(Guid form, string name)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Pdf), "file", name);
        using var response = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/uploads?fieldKey={Attachment.Key}", request => request.Content = content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response))["token"]!.GetValue<string>();
    }

    private Task<HttpResponseMessage> SubmitAsync(Guid form, string token) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent($$$"""{"revision":1,"values":{"attachment":["{{{token}}}"]}}""", Encoding.UTF8, "application/json"));

    // Quarantines what its name calls so.
    private sealed class NamedScanner : IFormBuilderFileScanner
    {
        public static bool Down { get; set; }

        public Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken) =>
            Down ? throw new IOException("The scanner cannot be reached.")
                : Task.FromResult(file.Name.Contains("eicar", StringComparison.Ordinal) ? FormBuilderFileScanOutcome.Quarantine : FormBuilderFileScanOutcome.Clean);
    }
}
