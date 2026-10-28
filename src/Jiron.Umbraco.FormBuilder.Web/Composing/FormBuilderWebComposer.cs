using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Web.Files;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Web.Composing;

/// <summary>
/// Registers Jiron Form Builder's APIs, rendering and backoffice with Umbraco, which discovers this composer
/// automatically. So far it supplies the site's folders, which the file store's root is checked against (ADR-0041),
/// the management API with its authorization policies (ADR-0049, ADR-0050), and the public API (ADR-0084).
/// </summary>
public sealed class FormBuilderWebComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddUnique<IHostFolders, WebHostFolders>();
        builder.AddFormBuilderManagementApi();
        builder.AddFormBuilderPublicApi();
    }
}
