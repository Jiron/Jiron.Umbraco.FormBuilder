using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// What the Umbraco sources read from Umbraco (ADR-0067): published content and where it sits in the tree, a page's
/// address, the default language, and members with their types.
/// </summary>
/// <param name="contents">The published content.</param>
/// <param name="navigation">Where each content item sits in the tree.</param>
/// <param name="contexts">Gives the Umbraco context the URL provider reads, also outside a request.</param>
/// <param name="urls">The URL provider.</param>
/// <param name="languages">The languages.</param>
/// <param name="members">The members.</param>
/// <param name="memberTypes">The member types, which say what a member may see of their own.</param>
/// <param name="publishedMembers">The members' published values.</param>
/// <param name="publicAccess">Which pages visitors must sign in to see.</param>
internal sealed class MergeTagUmbraco(
    IPublishedContentCache contents,
    IDocumentNavigationQueryService navigation,
    IUmbracoContextFactory contexts,
    IPublishedUrlProvider urls,
    ILanguageService languages,
    IMemberService members,
    IMemberTypeService memberTypes,
    IPublishedMemberCache publishedMembers,
    IPublicAccessService publicAccess)
{
    /// <summary>A published content item by its key, a draft in a preview; null when there is none.</summary>
    public Task<IPublishedContent?> ContentAsync(Guid key) => contents.GetByIdAsync(key);

    /// <summary>Whether visitors must sign in as a member to see the page, by a rule on it or on a page above it.</summary>
    public bool IsProtected(IPublishedContent page) => publicAccess.IsProtected(page.Path).Success;

    /// <summary>The keys of a content item's ancestors, its parent first.</summary>
    public IReadOnlyList<Guid> Ancestors(Guid key) => navigation.TryGetAncestorsKeys(key, out var ancestors) ? [.. ancestors] : [];

    /// <summary>The culture Umbraco reads variants in: the template's, or Umbraco's default language for the invariant culture.</summary>
    public async Task<string> CultureAsync(string culture) => culture.Length > 0 ? culture : await languages.GetDefaultIsoCodeAsync();

    /// <summary>
    /// A page's absolute address in a culture as Umbraco routes it: under a domain, that domain's address; otherwise the
    /// base URL followed by the page's path. Umbraco gets the base URL as the current address, so it never reads the
    /// request's. Null for a page Umbraco gives no address.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="culture">The culture, as <see cref="CultureAsync"/> gives it.</param>
    /// <param name="baseUrl">The site's base URL, an origin.</param>
    public string? Url(IPublishedContent page, string culture, string baseUrl)
    {
        string? url;
        using (contexts.EnsureUmbracoContext())
        {
            url = urls.GetUrl(page, UrlMode.Absolute, culture, new Uri(baseUrl + "/", UriKind.Absolute));
        }

        if (string.IsNullOrEmpty(url) || url == "#")
        {
            return null;
        }

        // On Linux a path reads as an absolute file address, so only http and https count as absolute.
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http")
        {
            return url;
        }

        return url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) ? baseUrl + url : null;
    }

    /// <summary>A member by their key, with their type and their published values; null when there is none.</summary>
    public async Task<MergeTagMember?> MemberAsync(Guid key)
    {
        if ((await members.GetByKeysAsync([key])).FirstOrDefault() is not { } member)
        {
            return null;
        }

        var type = await memberTypes.GetAsync(member.ContentType.Key);
        return new MergeTagMember(member, type, await publishedMembers.GetAsync(member));
    }
}

/// <summary>A member as merge tags read them.</summary>
/// <param name="Member">The member.</param>
/// <param name="Type">Their member type, which says which of their values they may see; null when it is gone.</param>
/// <param name="Published">Their published values; null when Umbraco has none.</param>
internal sealed record MergeTagMember(IMember Member, IMemberType? Type, IPublishedContent? Published);
