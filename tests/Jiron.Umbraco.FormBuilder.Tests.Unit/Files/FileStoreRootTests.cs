using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The physical store's root (ADR-0006, ADR-0041): <c>~/</c> and a path relative to the content root, or an absolute
/// path; never the web root, a folder inside it or one around it, as written or with links followed; and a site
/// configured so does not start.
/// </summary>
[TestFixture]
public sealed class FileStoreRootTests
{
    // Only names: nothing is created.
    private static readonly string Site = Path.Combine(Path.GetTempPath(), "jfb-site");
    private static readonly string WebRoot = Path.Combine(Site, "wwwroot");
    private static readonly string Elsewhere = Path.Combine(Path.GetTempPath(), "jfb-shared", "uploads");

    [Test]
    public void TheDefault_LiesInTheContentRoot()
    {
        Assert.That(Resolve(FormBuilderFilesOptions.DefaultRoot), Is.EqualTo(Path.Combine(Site, "umbraco", "Data", "JironFormBuilder", "Uploads")));
    }

    [Test]
    public void AFolderOfTheContentRoot_AndAnAbsolutePath_AreAllowed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Resolve("~/App_Data/files"), Is.EqualTo(Path.Combine(Site, "App_Data", "files")));
            Assert.That(Resolve("~/data/../files"), Is.EqualTo(Path.Combine(Site, "files")), "normalised");
            Assert.That(Resolve(Elsewhere), Is.EqualTo(Elsewhere), "a shared folder");
            Assert.That(Resolve(Elsewhere + Path.DirectorySeparatorChar), Is.EqualTo(Elsewhere + Path.DirectorySeparatorChar), "with a separator at the end");
            Assert.That(Resolve(WebRoot + "-files"), Is.EqualTo(WebRoot + "-files"), "a name that only starts like the web root's");
            Assert.That(Resolve("~/wwwroot.files"), Is.EqualTo(Path.Combine(Site, "wwwroot.files")));
        });
    }

    [TestCase(null, TestName = "nothing")]
    [TestCase("", TestName = "empty")]
    [TestCase("   ", TestName = "spaces")]
    [TestCase("uploads", TestName = "a relative path")]
    [TestCase("./uploads", TestName = "a path relative to the working folder")]
    [TestCase("~", TestName = "a tilde alone")]
    [TestCase("~/", TestName = "the content root's prefix alone")]
    [TestCase("~uploads", TestName = "a tilde without a slash")]
    [TestCase("~\\uploads", TestName = "a tilde and a backslash")]
    [TestCase("~//uploads", TestName = "a rooted path after the prefix")]
    public void ARootThatIsNeitherOfTheContentRootNorAbsolute_IsRefused(string? root)
    {
        Assert.That(Problem(root), Does.Contain("JironFormBuilder:Files:Root must be ~/"));
    }

    [Test]
    public void ADriveRelativePath_IsRefused_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Drive-relative paths exist only on Windows.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(Problem("C:uploads"), Does.Contain("must be ~/"));
            Assert.That(Problem("\\uploads"), Does.Contain("must be ~/"), "rooted on the current drive");
        });
    }

    [Test]
    public void DevicePaths_AndStreams_AreRefused_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Device paths and streams of files exist only on Windows.");
        }

        Assert.Multiple(() =>
        {
            foreach (var root in new[] { "//?/C:/data/uploads", "//./C:/data/uploads", "/??/C:/data/uploads", "//?/UNC/server/share/uploads" })
            {
                Assert.That(Problem(Backslashed(root)), Does.Contain("must be ~/"), root);
                Assert.That(Problem(root), Does.Contain("must be ~/"), root + " with slashes");
            }

            Assert.That(Problem(Elsewhere + ":stream"), Does.Contain("must be ~/"), "a stream of a file");
            Assert.That(Problem("~/uploads:stream"), Does.Contain("must be ~/"), "a stream in the content root");
        });
    }

    [Test]
    public void AShareOfAnotherServer_IsAllowed_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Shares are named so only on Windows.");
        }

        // A share that does not exist on this machine, so nothing waits for another server.
        var share = Backslashed("//localhost/jfb-no-such-share/uploads");

        Assert.That(Resolve(share), Is.EqualTo(share));
    }

    [Test]
    public void TheWebRoot_AndAnythingInsideIt_AreRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Problem("~/wwwroot"), Does.Contain("inside the web root"));
            Assert.That(Problem("~/wwwroot/uploads"), Does.Contain("inside the web root"));
            Assert.That(Problem("~/umbraco/../wwwroot/uploads"), Does.Contain("inside the web root"), "through ..");
            Assert.That(Problem(Path.Combine(WebRoot, "media", "uploads")), Does.Contain("inside the web root"), "absolute");
            Assert.That(Problem(Path.Combine(Site, "WWWROOT", "uploads")), Does.Contain("inside the web root"), "in another case");
            Assert.That(Problem(WebRoot + Path.DirectorySeparatorChar), Does.Contain("inside the web root"), "with a separator at the end");
        });
    }

    [Test]
    public void AFolderAroundTheWebRoot_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Problem("~/."), Does.Contain("contains the web root"), "the content root");
            Assert.That(Problem(Site), Does.Contain("contains the web root"));
            Assert.That(Problem(Path.GetDirectoryName(Site)), Does.Contain("contains the web root"), "the content root's parent");
            Assert.That(Problem(Path.GetPathRoot(Site)), Does.Contain("contains the web root"), "the drive, whose path ends in a separator");
        });
    }

    [Test]
    public void AWebRootOutsideTheContentRoot_IsComparedAsItIs()
    {
        var web = Path.Combine(Path.GetTempPath(), "jfb-public");

        Assert.Multiple(() =>
        {
            Assert.That(FileStoreRoot.TryResolve("~/wwwroot/uploads", Site, web, out _, out _), Is.True, "no web root there");
            Assert.That(FileStoreRoot.TryResolve(Path.Combine(web, "uploads"), Site, web, out _, out var problem), Is.False);
            Assert.That(problem, Does.Contain("inside the web root"));
        });
    }

    [Test]
    public void AWebRootWithASeparatorAtTheEnd_IsComparedAsWithout()
    {
        var web = WebRoot + Path.DirectorySeparatorChar;

        Assert.Multiple(() =>
        {
            Assert.That(FileStoreRoot.TryResolve("~/wwwroot", Site, web, out _, out var inside), Is.False);
            Assert.That(inside, Does.Contain("inside the web root"));
            Assert.That(FileStoreRoot.TryResolve("~/.", Site, web, out _, out var around), Is.False);
            Assert.That(around, Does.Contain("contains the web root"));
            Assert.That(FileStoreRoot.TryResolve(FormBuilderFilesOptions.DefaultRoot, Site, web, out _, out _), Is.True);
        });
    }

    [Test]
    public void ASiteWithItsFilesInTheWebRoot_DoesNotStart()
    {
        using var services = Services(Path.Combine(WebRoot, "uploads"));

        Assert.That(
            () => services.GetRequiredService<IStartupValidator>().Validate(),
            Throws.InstanceOf<OptionsValidationException>().With.Message.Contains("inside the web root"));
    }

    [Test]
    public void ASiteWithTheDefaultRoot_Starts_WithThePhysicalStoreInTheContentRoot()
    {
        using var services = Services(root: null);

        services.GetRequiredService<IStartupValidator>().Validate();
        var store = services.GetRequiredService<IFormBuilderFileStore>();

        Assert.That(store, Is.InstanceOf<PhysicalFileStore>());
        Assert.That(((PhysicalFileStore)store).Root, Is.EqualTo(Path.Combine(Site, "umbraco", "Data", "JironFormBuilder", "Uploads")));
    }

    [Test]
    public void AStoreTheSiteRegisteredFirst_IsKept()
    {
        var mine = Substitute.For<IFormBuilderFileStore>();
        var services = new ServiceCollection();
        services.AddSingleton(mine);
        services.AddFormBuilderFiles();

        Assert.That(services.Count(service => service.ServiceType == typeof(IFormBuilderFileStore)), Is.EqualTo(1));
    }

    [Test]
    public void AStoreTheSiteRegistersAfterwards_ReplacesTheDefault()
    {
        var mine = Substitute.For<IFormBuilderFileStore>();
        var services = new ServiceCollection();
        services.AddFormBuilderFiles();
        services.AddUnique(mine);

        Assert.That(services.BuildServiceProvider().GetRequiredService<IFormBuilderFileStore>(), Is.SameAs(mine), "either order works, as the store's documentation says");
    }

    // A path written with slashes, in Windows' backslashes.
    private static string Backslashed(string path) => path.Replace('/', (char)0x5C);

    private static string Resolve(string? root)
    {
        var resolved = FileStoreRoot.TryResolve(root, Site, WebRoot, out var path, out var problem);
        Assert.That(resolved, Is.True, problem);
        return path!;
    }

    private static string Problem(string? root)
    {
        var resolved = FileStoreRoot.TryResolve(root, Site, WebRoot, out var path, out var problem);
        Assert.That(resolved, Is.False, $"resolved to {path}");
        Assert.That(path, Is.Null);
        return problem!;
    }

    private static ServiceProvider Services(string? root)
    {
        var settings = new Dictionary<string, string?>();
        if (root is not null)
        {
            settings[FormBuilderFilesOptions.SectionName + ":Root"] = root;
        }

        var folders = Substitute.For<IHostFolders>();
        folders.ContentRoot.Returns(Site);
        folders.WebRoot.Returns(WebRoot);
        var services = new ServiceCollection();
        services.AddSingleton(folders);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddFormBuilderOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddFormBuilderFiles();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
