using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The registrations of the website rendering (ADR-0092, ADR-0093), without a host: made twice, they add each service
/// and the pipeline filter before Umbraco's routing once.
/// </summary>
[TestFixture]
public sealed class FormBuilderRenderingServicesTests
{
    private static readonly Type[] Rendering = [typeof(WebsiteAssets), typeof(PageAssets), typeof(FormViews), typeof(FormModels), typeof(FormRendering), typeof(BackOfficeVisitors)];

    [Test]
    public void MadeTwice_TheRegistrationsAddEachServiceAndTheFilterOnce()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<IUmbracoBuilder>();
        builder.Services.Returns(services);

        builder.AddFormBuilderRendering();
        builder.AddFormBuilderRendering();
        using var provider = services.BuildServiceProvider();
        var filters = provider.GetRequiredService<IOptions<UmbracoPipelineOptions>>().Value.PipelineFilters;

        Assert.Multiple(() =>
        {
            Assert.That(services.Count(service => service.ServiceType == typeof(WebsiteAssets)), Is.EqualTo(1));
            Assert.That(services.Count(service => service.ServiceType == typeof(WebsiteAssetsMiddleware)), Is.EqualTo(1));
            Assert.That(services.Count(service => service.ServiceType == typeof(PageAssets)), Is.EqualTo(1));
            Assert.That(services.Count(service => service.ServiceType == typeof(FormViews)), Is.EqualTo(1));
            Assert.That(services.Count(service => service.ServiceType == typeof(FormModels)), Is.EqualTo(1));
            Assert.That(services.Count(service => service.ServiceType == typeof(FormRendering)), Is.EqualTo(1));
            Assert.That(services.Count(service => service.ServiceType == typeof(BackOfficeVisitors)), Is.EqualTo(1));
            Assert.That(services.Single(service => service.ServiceType == typeof(FormMembers)).Lifetime, Is.EqualTo(ServiceLifetime.Scoped), "one per request, as Umbraco's member manager is");
            Assert.That(services.Where(service => Rendering.Contains(service.ServiceType)).Select(service => service.Lifetime), Is.All.EqualTo(ServiceLifetime.Singleton));
            Assert.That(filters.Select(filter => filter.Name), Is.EqualTo(new[] { "Jiron.FormBuilder.WebsiteAssets" }));
            Assert.That(((UmbracoPipelineFilter)filters.Single()).PrePipeline, Is.Not.Null, "before Umbraco's routing");
        });
    }
}
