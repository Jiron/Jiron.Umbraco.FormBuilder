using Jiron.Umbraco.FormBuilder.Web.Files;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The site's folders come from ASP.NET Core's host environment (ADR-0041); without a configured web root it is
/// <c>wwwroot</c> under the content root, as ASP.NET Core and Umbraco have it.
/// </summary>
[TestFixture]
public sealed class WebHostFoldersTests
{
    private static readonly string Site = Path.Combine(Path.GetTempPath(), "jfb-site");

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void WithoutAWebRoot_TheWebRootIsWwwrootInTheContentRoot(string? webRoot)
    {
        var folders = new WebHostFolders(Environment(webRoot));

        Assert.Multiple(() =>
        {
            Assert.That(folders.ContentRoot, Is.EqualTo(Site));
            Assert.That(folders.WebRoot, Is.EqualTo(Path.Combine(Site, "wwwroot")));
        });
    }

    [Test]
    public void AConfiguredWebRoot_IsTheWebRoot_AsAFullPath()
    {
        var folders = new WebHostFolders(Environment(Path.Combine(Site, "public", "..", "static")));

        Assert.That(folders.WebRoot, Is.EqualTo(Path.Combine(Site, "static")));
    }

    private static IWebHostEnvironment Environment(string? webRoot)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(Site);
        environment.WebRootPath.Returns(webRoot!);
        return environment;
    }
}
