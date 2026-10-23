using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Composing;

/// <summary>
/// Registers Jiron Form Builder's APIs, rendering and backoffice with Umbraco, which discovers this composer
/// automatically. It registers nothing yet.
/// </summary>
public sealed class FormBuilderWebComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
    }
}
