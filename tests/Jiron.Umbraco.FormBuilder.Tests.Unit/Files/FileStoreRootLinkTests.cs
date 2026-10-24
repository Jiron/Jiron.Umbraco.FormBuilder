using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The store's root with links on the way (ADR-0041), in real folders: a link that carries the root into the web root,
/// a web root that is a link, and a link around the web root are followed and refused; so is a path that the web root
/// holds as written, wherever its links lead; a link elsewhere is fine and the root stays as written; a loop of links
/// is refused.
/// </summary>
[TestFixture]
public sealed class FileStoreRootLinkTests
{
    private readonly List<string> _links = [];
    private string _base = null!;
    private string _site = null!;
    private string _web = null!;

    [SetUp]
    public void MakeTheFolders()
    {
        _base = Path.Combine(Path.GetTempPath(), "jfb-links-" + Guid.NewGuid().ToString("N"));
        _site = Path.Combine(_base, "site");
        _web = Path.Combine(_site, "wwwroot");
        Directory.CreateDirectory(Path.Combine(_web, "media"));
        Directory.CreateDirectory(Path.Combine(_base, "shared"));
    }

    [TearDown]
    public void DeleteTheFolders()
    {
        // Links first and one by one: a recursive delete takes a junction for a mount point, which only an administrator
        // may remove.
        foreach (var link in Enumerable.Reverse(_links))
        {
            Directory.Delete(link);
        }

        _links.Clear();
        Directory.Delete(_base, recursive: true);
    }

    [Test]
    public async Task ALinkIntoTheWebRoot_IsFollowed_AndRefused()
    {
        await LinkAsync(Path.Combine(_site, "data"), Path.Combine(_web, "media"));
        await LinkAsync(Path.Combine(_base, "public"), _web);

        Assert.Multiple(() =>
        {
            Assert.That(Problem("~/data/uploads", _web), Does.Contain("inside the web root"), "a link in the content root");
            Assert.That(Problem(Path.Combine(_base, "public", "uploads"), _web), Does.Contain("inside the web root"), "a link to the web root");
            Assert.That(Problem("~/wwwroot/uploads", Path.Combine(_base, "public")), Does.Contain("inside the web root"), "a web root that is a link");
        });
    }

    [Test]
    public async Task ALinkAroundTheWebRoot_IsFollowed_AndRefused()
    {
        await LinkAsync(Path.Combine(_base, "everything"), _site);

        Assert.That(Problem(Path.Combine(_base, "everything"), _web), Does.Contain("contains the web root"));
    }

    [Test]
    public async Task ARootTheWebRootHolds_IsRefused_WhereverItsLinksLead()
    {
        await LinkAsync(Path.Combine(_web, "files"), Path.Combine(_base, "shared"));

        Assert.That(Problem("~/wwwroot/files/uploads", _web), Does.Contain("inside the web root"), "the web serves the folder through the link");
    }

    [Test]
    public async Task ARootAroundAWebRootThatLeadsElsewhere_IsRefused()
    {
        var web = Path.Combine(_site, "public");
        await LinkAsync(web, Path.Combine(_base, "shared"));

        Assert.That(Problem("~/.", web), Does.Contain("contains the web root"), "the web root lies in the folder as written");
    }

    [Test]
    public async Task ALinkElsewhere_IsAllowed_AndTheRootStaysAsWritten()
    {
        await LinkAsync(Path.Combine(_site, "files"), Path.Combine(_base, "shared"));

        Assert.That(FileStoreRoot.TryResolve("~/files/uploads", _site, _web, out var path, out var problem), Is.True, problem);
        Assert.That(path, Is.EqualTo(Path.Combine(_site, "files", "uploads")));
    }

    [Test]
    public async Task ALoopOfLinks_IsRefused()
    {
        var a = Path.Combine(_base, "a");
        var b = Path.Combine(_base, "b");
        await LinkAsync(a, b);
        await LinkAsync(b, a);

        Assert.That(Problem(Path.Combine(a, "uploads"), _web), Does.Contain("cannot be followed"));
    }

    [Test]
    public async Task ALinkThroughLinks_IsFollowedToTheEnd()
    {
        var hops = Path.Combine(_base, "hop0");
        for (var i = 0; i < FileStoreRoot.MaxLinks - 1; i++)
        {
            await LinkAsync(Path.Combine(_base, "hop" + i), Path.Combine(_base, "hop" + (i + 1)));
        }

        await LinkAsync(Path.Combine(_base, "hop" + (FileStoreRoot.MaxLinks - 1)), _web);
        await LinkAsync(Path.Combine(_base, "far0"), hops);

        Assert.Multiple(() =>
        {
            Assert.That(Problem(Path.Combine(hops, "uploads"), _web), Does.Contain("inside the web root"), $"{FileStoreRoot.MaxLinks} links");
            Assert.That(Problem(Path.Combine(_base, "far0", "uploads"), _web), Does.Contain("cannot be followed"), "one more");
        });
    }

    [Test]
    public void ARelativeLink_IsFollowedFromItsOwnFolder()
    {
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_site, "data"), Path.Combine("wwwroot", "media"));
            _links.Add(Path.Combine(_site, "data"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("This account may not make symbolic links, and junctions have absolute targets only: " + exception.Message);
        }

        Assert.That(Problem("~/data/uploads", _web), Does.Contain("inside the web root"));
    }

    // A junction on Windows, which needs no privilege, and a symbolic link elsewhere.
    private async Task LinkAsync(string link, string target)
    {
        _links.Add(link);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, "mklink /J " + link);
    }

    private string Problem(string root, string webRoot)
    {
        var resolved = FileStoreRoot.TryResolve(root, _site, webRoot, out var path, out var problem);
        Assert.That(resolved, Is.False, $"resolved to {path}");
        return problem!;
    }
}
