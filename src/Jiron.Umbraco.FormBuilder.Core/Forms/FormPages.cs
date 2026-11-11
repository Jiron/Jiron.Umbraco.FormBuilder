using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The culture and the page a visitor's request is for (ADR-0084, ADR-0088), read alike by every public endpoint and by
/// the submission pipeline: the culture among the site's languages, and the published content the form is shown on with
/// its site, whose settings apply; without a page, the site the request's host names (ADR-0008, ADR-0232), and for an entry
/// whose page is gone, the site it was sent from.
/// </summary>
/// <param name="languages">Umbraco's languages.</param>
/// <param name="contents">The published content.</param>
/// <param name="navigation">Where each content item sits in the tree.</param>
/// <param name="hosts">The site a request's host names.</param>
internal sealed class FormPages(ILanguageService languages, IPublishedContentCache contents, IDocumentNavigationQueryService navigation, SiteHosts hosts)
{
    /// <summary>The longest culture name a request may give.</summary>
    public const int MaxCultureLength = 85;

    /// <summary>Whether a request's culture can be read: none, or a culture name such as <c>de-CH</c>.</summary>
    /// <param name="culture">The culture as the request gives it.</param>
    public static bool IsCulture(string? culture) => culture is null || (culture.Length <= MaxCultureLength && LocalizedString.IsCultureName(culture));

    /// <summary>
    /// The culture a request is answered in: the site's language it names in any letter case, as Umbraco spells it, and
    /// otherwise the default language. So no culture a visitor makes up reaches a cache or the dictionary (ADR-0081).
    /// </summary>
    /// <param name="requested">The culture as the request gives it, or null.</param>
    public async Task<string> CultureAsync(string? requested)
    {
        if (requested is not null)
        {
            foreach (var language in await languages.GetAllAsync())
            {
                if (string.Equals(language.IsoCode, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return language.IsoCode;
                }
            }
        }

        return await languages.GetDefaultIsoCodeAsync();
    }

    /// <summary>
    /// The page a request is for: the published content it names, or none for none or for content that is not published,
    /// and its site, the root of that content, whose settings apply; the values every site shares apply without one
    /// (section 4.8). An entry's reads take <see cref="EntryPageAsync"/>, and a visitor's request the overload with its host.
    /// </summary>
    /// <param name="contentKey">The content the request names, or null.</param>
    public async Task<RequestPage> PageAsync(Guid? contentKey)
    {
        // Content the tree no longer holds, such as a page deleted a moment ago that a cache still serves, is no page, never a
        // site of its own.
        if (contentKey is not { } key || await contents.GetByIdAsync(key) is null || !navigation.TryGetAncestorsKeys(key, out var ancestors))
        {
            return new RequestPage(null, null);
        }

        return new RequestPage(key, ancestors.LastOrDefault() is { } root && root != Guid.Empty ? root : key);
    }

    /// <summary>
    /// The page a visitor's request is for, as <see cref="PageAsync(Guid?)"/> finds it, and without one the site its host
    /// names (ADR-0008, ADR-0232): a headless call on a site's domain gets that site's texts, time zone, theme and mail.
    /// What protects a form never reads the host's site, since a request chooses its host as it chooses a page (ADR-0147).
    /// </summary>
    /// <param name="contentKey">The content the request names, or null.</param>
    /// <param name="host">The host the request was sent to, with its port if any, or null.</param>
    public async Task<RequestPage> PageAsync(Guid? contentKey, string? host)
    {
        var page = await PageAsync(contentKey);
        return page.Content is null && hosts.SiteOf(host) is { } site ? page with { Site = site } : page;
    }

    /// <summary>
    /// The page an entry was sent from and its site, as its page now gives them; for an entry whose page is gone or was never
    /// named, the site it was sent from (ADR-0232), so its mail and its links still follow that site.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public async Task<RequestPage> EntryPageAsync(StoredEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var page = await PageAsync(entry.ContentKey);
        return page.Content is null && entry.SiteKey is { } site ? page with { Site = site } : page;
    }
}

/// <summary>The page a form is shown on.</summary>
/// <param name="Content">The published content page, which <c>page.*</c> reads; null for none.</param>
/// <param name="Site">Its site's root, whose settings apply; null for the values every site shares.</param>
internal sealed record RequestPage(Guid? Content, Guid? Site);
