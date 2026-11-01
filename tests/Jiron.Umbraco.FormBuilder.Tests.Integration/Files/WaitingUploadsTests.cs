using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Files;

/// <summary>
/// The bytes that wait for an entry, against the host's cap (ROADMAP 7.4d, ADR-0148): only uploads no entry took count, the table
/// is read once per period, what the server stores itself counts at once, and no cap reads nothing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class WaitingUploadsTests : RepositoryTestBase
{
    // What EntryRepositoryTests.NewFile records.
    private const long FileBytes = 1234;

    [Test]
    public async Task TheSum_CountsWhatNoEntryTook_AndOnlyThat()
    {
        var form = await CreateTestFormAsync("upload");
        var empty = await Entries.SumWaitingBytesAsync();
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));
        var taken = await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [taken!.Id] });

        Assert.Multiple(async () =>
        {
            Assert.That(empty, Is.Zero);
            Assert.That(await Entries.SumWaitingBytesAsync(), Is.EqualTo(2 * FileBytes));
        });
    }

    [Test]
    public async Task AtTheCap_ThereIsNoRoom_UntilAReadingFindsSome_AndItSaysSoOnce()
    {
        var form = await CreateTestFormAsync("upload");
        var file = await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));
        var waiting = Waiting(Entries, 1000);

        var full = await waiting.HasRoomAsync(CancellationToken.None);
        var stillFull = await waiting.HasRoomAsync(CancellationToken.None);
        await Entries.DeleteFileAsync(file!.Id, entryId: null);
        var heldByTheReading = await waiting.HasRoomAsync(CancellationToken.None);
        Clock.Advance(WaitingUploads.ReadEvery - TimeSpan.FromSeconds(1));
        var justBefore = await waiting.HasRoomAsync(CancellationToken.None);
        Clock.Advance(TimeSpan.FromSeconds(1));
        var after = await waiting.HasRoomAsync(CancellationToken.None);
        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(WaitingUploads).FullName && entry.Level == LogLevel.Warning).ToList();

        Assert.Multiple(() =>
        {
            Assert.That((full, stillFull, heldByTheReading, justBefore), Is.EqualTo((false, false, false, false)));
            Assert.That(after, Is.True, "room again");
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Message, Does.Contain("MaxWaitingBytes").And.Contain(FileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)).And.Contain("1000"));
        });
    }

    [Test]
    public void AReadingHolds_AFewSeconds() => Assert.That(WaitingUploads.ReadEvery, Is.EqualTo(TimeSpan.FromSeconds(10)));

    [Test]
    public async Task BelowTheCap_ThereIsRoom_AndWhatThisServerStores_CountsAtOnce()
    {
        var waiting = Waiting(Entries, 100);

        var before = await waiting.HasRoomAsync(CancellationToken.None);
        waiting.Add(99);
        var below = await waiting.HasRoomAsync(CancellationToken.None);
        waiting.Add(1);
        var at = await waiting.HasRoomAsync(CancellationToken.None);

        Assert.That((before, below, at), Is.EqualTo((true, true, false)));
    }

    [Test]
    public async Task AFlood_ReadsTheTableOncePerPeriod()
    {
        var entries = Substitute.For<IEntryRepository>();
        var waiting = Waiting(entries, 100);

        await waiting.HasRoomAsync(CancellationToken.None);
        await waiting.HasRoomAsync(CancellationToken.None);
        await entries.Received(1).SumWaitingBytesAsync(Arg.Any<CancellationToken>());
        Clock.Advance(WaitingUploads.ReadEvery);
        await waiting.HasRoomAsync(CancellationToken.None);

        await entries.Received(2).SumWaitingBytesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NoCap_LeavesItOut_AndReadsNothing()
    {
        var entries = Substitute.For<IEntryRepository>();
        entries.SumWaitingBytesAsync(Arg.Any<CancellationToken>()).Returns(long.MaxValue);
        var waiting = Waiting(entries, 0);
        waiting.Add(long.MaxValue);

        Assert.Multiple(async () =>
        {
            Assert.That(await waiting.HasRoomAsync(CancellationToken.None), Is.True);
            await entries.DidNotReceiveWithAnyArgs().SumWaitingBytesAsync();
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(global::Umbraco.Cms.Core.DependencyInjection.IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    private WaitingUploads Waiting(IEntryRepository entries, long cap)
    {
        var options = new FormBuilderOptions();
        options.Files.MaxWaitingBytes = cap;
        return new(entries, Options.Create(options), Clock, GetRequiredService<ILogger<WaitingUploads>>());
    }
}
