using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Samples.SiteSettings;

/// <summary>
/// The site settings sample's merge-tag source (docs/extending.md): <c>{{ siteSettings.phone }}</c> and every other
/// property of the site's settings node, a content item of the type <c>siteSettings</c>, so a mail or a page says the
/// site's phone number or opening hours as the editors keep them. A site with several roots has a settings node in each:
/// the one under the root of the site the form is shown on or sent from, else the first at the root. The picker offers
/// each property of the type by its name. Its values are the same for every visitor.
/// </summary>
/// <param name="contents">The published content.</param>
/// <param name="navigation">Where each item sits in the tree.</param>
/// <param name="types">The content types, whose properties the picker offers.</param>
public sealed class SiteSettingsMergeTagSource(IPublishedContentCache contents, IDocumentNavigationQueryService navigation, IContentTypeService types)
    : IFormBuilderMergeTagSource
{
    /// <summary>The source's name, the first part of its tags.</summary>
    public const string SourceName = "siteSettings";

    /// <summary>The alias of the settings node's content type.</summary>
    public const string ContentTypeAlias = "siteSettings";

    /// <inheritdoc />
    public string Name => SourceName;

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) =>
        types.Get(ContentTypeAlias)?.CompositionPropertyTypes
            .OrderBy(property => property.SortOrder)
            .Select(property => new MergeTagPathInfo(property.Alias, property.Name))
        ?? [];

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Path.Length == 0 || await SettingsAsync(request.Scope.Site) is not { } settings)
        {
            return null;
        }

        if (settings.GetProperty(request.Path) is not { } property)
        {
            return null;
        }

        var culture = property.PropertyType.VariesByCulture() ? request.Scope.Culture : null;
        return property.GetValue(culture) switch
        {
            null => MergeTagValue.Empty,
            string text => MergeTagValue.Of(text),
            IEnumerable<string> texts => MergeTagValue.ListOf([.. texts.Select(each => new MergeTagItem(each))]),
            var value => MergeTagValue.Of(value.ToString() ?? string.Empty),
        };
    }

    // The settings node of the site: under its root, else the first at the root.
    private async Task<IPublishedContent?> SettingsAsync(Guid? site)
    {
        var candidates = new List<Guid>();
        if (site is { } root && navigation.TryGetDescendantsKeysOfType(root, ContentTypeAlias, out var under))
        {
            candidates.AddRange(under);
        }

        if (navigation.TryGetRootKeysOfType(ContentTypeAlias, out var roots))
        {
            candidates.AddRange(roots);
        }

        foreach (var key in candidates)
        {
            if (await contents.GetByIdAsync(key) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
