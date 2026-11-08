using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;

/// <summary>A site of the settings: a content root, by its key and name (ADR-0043, ADR-0218).</summary>
/// <param name="Key">The root content's key.</param>
/// <param name="Name">Its name in the content tree.</param>
internal sealed record SettingsSite(Guid Key, string Name);

/// <summary>
/// The sites a settings section can be set for: Umbraco's content roots outside the Recycle Bin, in the tree's order
/// (ADR-0043). A site's settings are kept by its root's key, and a save for any other key is refused (ADR-0218). The roots
/// are read from the database, as a root created a moment ago may not be in the navigation's cache yet.
/// </summary>
/// <param name="entities">Umbraco's entities.</param>
internal sealed class SettingsSites(IEntityService entities)
{
    /// <summary>The content roots, in the tree's order.</summary>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public Task<IReadOnlyList<SettingsSite>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SettingsSite>>([.. Roots().Select(root => new SettingsSite(root.Key, root.Name ?? string.Empty))]);
    }

    /// <summary>Whether a key names a content root outside the Recycle Bin.</summary>
    /// <param name="key">The key.</param>
    public bool IsSite(Guid key) => Roots().Any(root => root.Key == key);

    private IEnumerable<global::Umbraco.Cms.Core.Models.Entities.IEntitySlim> Roots() =>
        entities.GetRootEntities(UmbracoObjectTypes.Document).Where(root => !root.Trashed).OrderBy(root => root.SortOrder);
}
