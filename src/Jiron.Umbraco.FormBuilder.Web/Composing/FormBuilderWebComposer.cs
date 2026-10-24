using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Web.Files;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Web.Composing;

/// <summary>
/// Registers Jiron Form Builder's APIs, rendering and backoffice with Umbraco, which discovers this composer
/// automatically. So far it supplies the site's folders, which the file store's root is checked against (ADR-0041).
/// </summary>
public sealed class FormBuilderWebComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddUnique<IHostFolders, WebHostFolders>();
    }
}
