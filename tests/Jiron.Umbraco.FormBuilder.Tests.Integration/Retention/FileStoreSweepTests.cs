using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Retention;

/// <summary>
/// The sweep of the file store (ROADMAP 13.1, ADR-0041, ADR-0222): what a dead process leaves there goes once it is a day
/// old, a save's partial file, a deleted file's bytes and a stored file no row names, and nothing younger, nothing a row
/// names and no name the store never writes. A store of the site's own is not swept.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FileStoreSweepTests : RepositoryTestBase
{
    private string _root = string.Empty;

    [SetUp]
    public void MakeAStore()
    {
        _root = Path.Combine(Path.GetTempPath(), $"jfb-sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveTheStore()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task ThreeOldLeftovers_Go_AndTheYoungOnes_ThoseARowNames_AndStrangeNames_Stay()
    {
        var store = Store();
        var form = await CreateTestFormAsync("upload");
        var named = (await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id)))!;
        var old = Clock.GetUtcNow() - TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1);
        var young = Clock.GetUtcNow() - TimeSpan.FromHours(23);
        var files = new Dictionary<string, DateTimeOffset>
        {
            [Id() + PhysicalFileStore.PartialSuffix] = old,
            [Id() + PhysicalFileStore.DeletedInfix + FormBuilderFileId.New()[..16]] = old,
            [Id()] = old,
            [Id() + PhysicalFileStore.PartialSuffix] = young,
            [Id() + PhysicalFileStore.DeletedInfix + FormBuilderFileId.New()[..16]] = young,
            [Id()] = young,
            [named.StoredName] = old,
        };
        var strange = Path.Combine(_root, "ab", "notes.txt");
        foreach (var (name, written) in files)
        {
            Write(name, written);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(strange)!);
        await File.WriteAllTextAsync(strange, "kept");
        File.SetLastWriteTimeUtc(strange, old.UtcDateTime);

        await Sweep(store).RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);

        var left = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet();
        var gone = files.Keys.Where(name => !left.Contains(name)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(gone, Is.EquivalentTo(files.Where(file => file.Value == old && file.Key != named.StoredName).Select(file => file.Key)), "exactly the three old leftovers");
            Assert.That(gone, Has.Count.EqualTo(3));
            Assert.That(left, Does.Contain("notes.txt"), "a name the store never writes");
        });
    }

    [Test]
    public async Task APass_GoesOnWhereItStopped_AndTheNextStartsADayAfterItEnded()
    {
        var store = Store();
        var old = Clock.GetUtcNow() - TimeSpan.FromDays(2);
        var first = "00" + Id()[2..];
        var second = "ff" + Id()[2..];
        Write(first, old);
        Write(second, old);
        var sweep = Sweep(store);

        await sweep.RunAsync(Clock.GetUtcNow(), CancellationToken.None);
        var stopped = await KeyValues.GetAsync(FileStoreSweep.ProgressKey);
        await sweep.RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);
        var ended = await KeyValues.GetAsync(FileStoreSweep.ProgressKey);
        var third = "77" + Id()[2..];
        Write(third, old);
        await sweep.RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);
        var thirdWithinTheDay = File.Exists(PathOf(third));
        Clock.Advance(TimeSpan.FromDays(1));
        await sweep.RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.Null.Or.Empty, "a deadline already passed sweeps nothing");
            Assert.That(File.Exists(PathOf(first)) || File.Exists(PathOf(second)), Is.False);
            Assert.That(ended, Does.StartWith("done:"));
            Assert.That(thirdWithinTheDay, Is.True, "the next pass waits a day");
            Assert.That(File.Exists(PathOf(third)), Is.False);
        });
    }

    [Test]
    public async Task AStoreOfTheSitesOwn_IsNotSwept()
    {
        var own = Substitute.For<IFormBuilderFileStore>();

        await Sweep(own).RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(own.ReceivedCalls(), Is.Empty);
            Assert.That(await KeyValues.GetAsync(FileStoreSweep.ProgressKey), Is.Null);
        });
    }

    private static string Id() => FormBuilderFileId.New();

    private PhysicalFileStore Store()
    {
        var options = new FormBuilderOptions();
        options.Files.Root = _root;
        var folders = Substitute.For<IHostFolders>();
        folders.ContentRoot.Returns(_root + "-site");
        folders.WebRoot.Returns(Path.Combine(_root + "-site", "wwwroot"));
        return new PhysicalFileStore(Options.Create(options), folders, NullLogger<PhysicalFileStore>.Instance);
    }

    private FileStoreSweep Sweep(IFormBuilderFileStore store) => new(store, Entries, KeyValues, Clock, NullLogger<FileStoreSweep>.Instance);

    private string PathOf(string name) => Path.Combine(_root, name[..2], name);

    private void Write(string name, DateTimeOffset written)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "bytes");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
    }
}
