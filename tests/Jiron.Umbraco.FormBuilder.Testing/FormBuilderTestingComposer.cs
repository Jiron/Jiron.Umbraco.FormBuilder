using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// Registers the test-only services in the hosts that reference this project (the test hosts and the demo).
/// It registers nothing yet.
/// </summary>
public sealed class FormBuilderTestingComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
    }
}
