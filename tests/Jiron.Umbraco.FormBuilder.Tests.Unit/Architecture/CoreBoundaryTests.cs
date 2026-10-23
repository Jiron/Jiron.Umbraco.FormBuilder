using System.Xml.Linq;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Core knows nothing about HTTP (ADR-0001, ADR-0011): it references no ASP.NET Core HTTP, MVC, routing or Razor
/// assembly and no Umbraco web or API package. Web hosts the endpoints, rendering and backoffice.
/// </summary>
[TestFixture]
public sealed class CoreBoundaryTests
{
    [TestCase("Microsoft.AspNetCore.Http.Abstractions", true)]
    [TestCase("Microsoft.AspNetCore.Http", true)]
    [TestCase("Microsoft.AspNetCore.Mvc.Core", true)]
    [TestCase("Microsoft.AspNetCore.Routing", true)]
    [TestCase("Microsoft.AspNetCore.Razor", true)]
    [TestCase("Microsoft.AspNetCore.Html.Abstractions", true)]
    [TestCase("Umbraco.Web.Common", true)]
    [TestCase("Umbraco.Web.Website", true)]
    [TestCase("Umbraco.Cms.Api.Management", true)]
    [TestCase("Umbraco.Cms.Api.Common", true)]
    [TestCase("Umbraco.Core", false)]
    [TestCase("Umbraco.Infrastructure", false)]
    [TestCase("Microsoft.AspNetCore.Cryptography.KeyDerivation", false)]
    [TestCase("Microsoft.Extensions.Logging.Abstractions", false)]
    public void IsWebAssembly_KnownNames_AreSortedAsExpected(string name, bool web)
    {
        Assert.That(IsWebAssembly(name), Is.EqualTo(web));
    }

    [Test]
    public void CoreAssembly_ReferencesNoWebAssembly()
    {
        var references = typeof(FormBuilderCoreComposer).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

        Assert.That(references.Where(IsWebAssembly), Is.Empty);
    }

    [Test]
    public void CoreProject_ReferencesNoWebPackageOrFramework()
    {
        var project = XDocument.Load(RepositoryRoot.Path(typeof(CoreBoundaryTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Core", "Jiron.Umbraco.FormBuilder.Core.csproj"));
        var packages = project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty).ToList();
        var frameworks = project.Descendants("FrameworkReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty).ToList();
        var projects = project.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(packages, Is.Not.Empty, "the project file was read");
            Assert.That(packages.Where(name => name.StartsWith("Umbraco.Cms.Web", StringComparison.Ordinal) || name.StartsWith("Umbraco.Cms.Api", StringComparison.Ordinal)), Is.Empty);
            Assert.That(packages.Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)), Is.Empty);
            Assert.That(frameworks, Is.Empty);
            Assert.That(projects, Is.Empty, "Core references no other project of Jiron Form Builder");
        });
    }

    private static bool IsWebAssembly(string name) =>
        new[] { "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Routing", "Microsoft.AspNetCore.Razor", "Microsoft.AspNetCore.Html", "Umbraco.Web.", "Umbraco.Cms.Web.", "Umbraco.Cms.Api." }
            .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
