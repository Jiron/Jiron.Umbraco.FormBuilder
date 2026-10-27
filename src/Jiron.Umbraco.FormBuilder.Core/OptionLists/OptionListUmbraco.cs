using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// What content and data type lists read from Umbraco (ADR-0075): published pages and where they sit in the tree, who
/// may see them, document types, data types and the default language; with the runtime cache their options are kept in
/// and the formats their labels are written in.
/// </summary>
/// <param name="contents">The published content.</param>
/// <param name="navigation">Where each page sits in the tree.</param>
/// <param name="publicAccess">Which pages need a member to sign in.</param>
/// <param name="contentTypes">The document types.</param>
/// <param name="dataTypes">The data types.</param>
/// <param name="languages">The languages.</param>
/// <param name="cultures">The formats of numbers and dates per culture.</param>
/// <param name="appCaches">The caches, whose runtime cache keeps the options.</param>
internal sealed class OptionListUmbraco(
    IPublishedContentCache contents,
    IDocumentNavigationQueryService navigation,
    IPublicAccessService publicAccess,
    IContentTypeService contentTypes,
    IDataTypeService dataTypes,
    ILanguageService languages,
    MergeTagCultures cultures,
    AppCaches appCaches)
{
    /// <summary>The runtime cache the options are kept in.</summary>
    public IAppPolicyCache Cache => appCaches.RuntimeCache;

    /// <summary>The formats of numbers and dates per culture.</summary>
    public MergeTagCultures Cultures => cultures;

    /// <summary>A page as it is published, never a draft, even in a preview; null when it is not published.</summary>
    public Task<IPublishedContent?> PublishedAsync(Guid key) => contents.GetByIdAsync(key, preview: false);

    /// <summary>The keys of a page's children in the tree's order; null when the tree has no such page outside the recycle bin.</summary>
    public IReadOnlyList<Guid>? Children(Guid key) => navigation.TryGetChildrenKeys(key, out var children) ? [.. children] : null;

    /// <summary>Whether the tree has the page outside the recycle bin, published or not.</summary>
    public bool HasPage(Guid key) => navigation.TryGetParentKey(key, out _);

    /// <summary>Whether visitors must sign in as a member to see the page, by a rule on it or on a page above it.</summary>
    public bool IsProtected(IPublishedContent page) => publicAccess.IsProtected(page.Path).Success;

    /// <summary>A document type by its key; null when there is none.</summary>
    public IContentType? DocumentType(Guid key) => contentTypes.Get(key);

    /// <summary>A data type by its key; null when there is none.</summary>
    public Task<IDataType?> DataTypeAsync(Guid key) => dataTypes.GetAsync(key);

    /// <summary>The culture Umbraco reads variants in: the one asked for, or Umbraco's default language for none.</summary>
    public async Task<string> CultureAsync(string culture) => culture.Length > 0 ? culture : await languages.GetDefaultIsoCodeAsync();
}
