using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Examine;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// Registers <see cref="StartupMarkerPopulator"/> when the host turns it on (<c>JironFormBuilderTesting:StartupMarker</c>).
/// Umbraco registers its index populators in <see cref="AddExamineComposer"/>, and composers without an order between
/// them run in any order, so this one runs after it: that puts the marker after every populator Umbraco registers.
/// </summary>
[ComposeAfter(typeof(AddExamineComposer))]
public sealed class StartupMarkerComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (builder.Config.GetSection(FormBuilderTestingOptions.SectionName).GetValue<bool>(nameof(FormBuilderTestingOptions.StartupMarker)))
        {
            builder.Services.AddSingleton<IIndexPopulator, StartupMarkerPopulator>();
        }
    }
}
