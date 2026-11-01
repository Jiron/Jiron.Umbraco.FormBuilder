using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The upload endpoint at the cap of what may wait for an entry (ROADMAP 7.4d, ADR-0148): it answers <c>503 uploads_full</c> with
/// <c>Retry-After</c>, stores nothing, and takes files again once there is room.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormUploadCapTests : PublicApiTestBase
{
    private const string Boundary = "jfb-cap-boundary";

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("74d00000-0000-4000-8000-000000000001"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
    };

    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8];

    private TestTimeProvider _clock = null!;

    /// <summary>A fixture of the cap, for which the challenge is beside the point: PublicFormChallengeTests holds its tests.</summary>
    public PublicFormUploadCapTests() => SendsWithServerKey = true;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    [Test]
    public async Task PastTheCap_AnUploadIsRefusedBeforeItIsStored_AndTakenAgainOnceThereIsRoom()
    {
        var key = await PublishAsync(fields: [NameField, Attachment]);

        using var first = await UploadAsync(key);
        var kept = await WaitingAsync();
        using var refused = await UploadAsync(key);
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        var afterRefusal = await WaitingAsync();
        await Entries.DeleteFileAsync(kept.Single().Id, entryId: null);
        using var heldByTheReading = await UploadAsync(key);
        _clock.Advance(WaitingUploads.ReadEvery);
        using var later = await UploadAsync(key);

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo(FormBuilderPublicProblems.UploadsFullCode));
            Assert.That(refused.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(refused.Headers.ConnectionClose, Is.True, "the rest of its body is not read");
            Assert.That(afterRefusal, Has.Count.EqualTo(1), "nothing more recorded");
            Assert.That(heldByTheReading.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(later.StatusCode, Is.EqualTo(HttpStatusCode.OK), "room again");
            Assert.That(await WaitingAsync(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task PastItsShare_AClientIsToldToWait_AndStoresNothing()
    {
        var key = await PublishAsync(fields: [NameField, Attachment]);

        using var first = await UploadAsync(key);
        using var refused = await UploadAsync(key);
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        var waiting = await WaitingAsync();
        _clock.Advance(Core.Retention.OrphanFileCleanup.KeptFor);
        using var later = await UploadAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo(FormBuilderPublicProblems.RateLimitedCode));
            Assert.That(refused.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(refused.Headers.ConnectionClose, Is.True);
            Assert.That(waiting, Has.Count.EqualTo(1), "nothing more recorded");
            Assert.That(later.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a day later");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        // The host's configuration stays from test to test, so each says both.
        var oneShare = TestContext.CurrentContext.Test.MethodName == nameof(PastItsShare_AClientIsToldToWait_AndStoresNothing);
        configuration[FormBuilderOptions.SectionName + ":Files:MaxWaitingBytesPerAddress"] = oneShare ? "1048576" : "0";
        configuration[FormBuilderOptions.SectionName + ":Files:MaxWaitingBytes"] = oneShare ? "0" : "10";
        configuration[FormBuilderOptions.SectionName + ":Limits:ServerKeyFactor"] = "1";
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        builder.Services.AddUnique<TimeProvider>(_clock);
    }

    private async Task<HttpResponseMessage> UploadAsync(Guid key)
    {
        using var content = new MultipartFormDataContent(Boundary);
        content.Add(new ByteArrayContent(Pdf), "file", "a.pdf");
        return await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads?fieldKey={Attachment.Key}", request => request.Content = content);
    }

    private Task<IReadOnlyList<StoredEntryFile>> WaitingAsync() => Entries.ListOrphanFilesAsync(DateTimeOffset.UtcNow.AddDays(1), 100);
}
