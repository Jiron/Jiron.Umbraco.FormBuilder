using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// A site's base URL (section 4.8, ADR-0008, ADR-0067), the address every link the server builds starts with: the
/// general settings' value for the site (its own, or the global one it follows); without one, the first domain Umbraco
/// assigns within the site that is reached over <c>https</c>, the root's own before those deeper down, each in its sort
/// order; otherwise Umbraco's application URL. Never the request's Host header, which a visitor chooses.
/// </summary>
/// <param name="settings">The general settings.</param>
/// <param name="domains">Umbraco's domains.</param>
/// <param name="ids">Maps a domain's content id to its key.</param>
/// <param name="navigation">Where each content item sits in the tree.</param>
/// <param name="routing">Umbraco's routing settings, which hold its application URL.</param>
internal sealed class SiteBaseUrl(
    IFormBuilderSettingsService settings,
    IDomainService domains,
    IIdKeyMap ids,
    IDocumentNavigationQueryService navigation,
    IOptionsMonitor<WebRoutingSettings> routing)
{
    /// <summary>The base URL of a site, by the key of its root content, or the global one for none; null when there is none.</summary>
    /// <param name="site">The site's root content, or null.</param>
    /// <param name="cancellationToken">Cancelled when the caller is.</param>
    public async Task<string?> GetAsync(Guid? site, CancellationToken cancellationToken)
    {
        var general = await settings.GetAsync<GeneralSettings>(site, cancellationToken);
        if (BaseUrls.Origin(general.Value.BaseUrl) is { } configured)
        {
            return configured;
        }

        if (site is { } root && await DomainAsync(root) is { } domain)
        {
            return domain;
        }

        return BaseUrls.Origin(routing.CurrentValue.UmbracoApplicationUrl);
    }

    /// <summary>
    /// The base URL a site gets when its settings name none, or the global one for no site: the site's first <c>https</c>
    /// domain, otherwise Umbraco's application URL; null when there is none. Settings > General shows it in the empty box
    /// (ADR-0218).
    /// </summary>
    /// <param name="site">The site's root content, or null.</param>
    public async Task<string?> GetDefaultAsync(Guid? site) =>
        site is { } root && await DomainAsync(root) is { } domain ? domain : BaseUrls.Origin(routing.CurrentValue.UmbracoApplicationUrl);

    /// <summary>The origin of the site's first domain reached over <c>https</c>: the root's own first, then by depth, each in its sort order.</summary>
    private async Task<string?> DomainAsync(Guid site)
    {
        var found = new List<(int Depth, int Order, string Origin)>();
        foreach (var domain in await domains.GetAllAsync(includeWildcards: false))
        {
            if (domain.IsWildcard || domain.RootContentId is not { } id || BaseUrls.DomainOrigin(domain.DomainName) is not { } origin)
            {
                continue;
            }

            var key = ids.GetKeyForId(id, UmbracoObjectTypes.Document);
            if (key.Success && Depth(key.Result, site) is { } depth)
            {
                found.Add((depth, domain.SortOrder, origin));
            }
        }

        return found.OrderBy(domain => domain.Depth).ThenBy(domain => domain.Order).Select(domain => domain.Origin).FirstOrDefault();
    }

    /// <summary>How far below the site's root a content item is, 0 for the root itself; null for one outside the site.</summary>
    private int? Depth(Guid content, Guid site)
    {
        if (content == site)
        {
            return 0;
        }

        if (!navigation.TryGetAncestorsKeys(content, out var ancestors))
        {
            return null;
        }

        // The ancestors come parent first.
        var index = ancestors.TakeWhile(ancestor => ancestor != site).Count();
        return ancestors.Contains(site) ? index + 1 : null;
    }
}

/// <summary>The addresses a base URL is made of (ADR-0067): origins of <c>https</c> and <c>http</c> addresses only.</summary>
internal static class BaseUrls
{
    /// <summary>
    /// The origin of an absolute <c>https</c> or <c>http</c> address with a host and no user name: its scheme and host in
    /// lower case and its port when it is not the scheme's, such as <c>https://example.com:8443</c>; null for anything else.
    /// </summary>
    /// <param name="address">The address, for example Umbraco's application URL.</param>
    public static string? Origin(string? address) => TryRead(address, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;

    /// <summary>
    /// The origin of an address that is an origin, with a slash after it at most, as the setting stores it; null for any
    /// other address, one with a path, a query or a fragment among them.
    /// </summary>
    /// <param name="address">The address an editor gave.</param>
    public static string? Normalize(string? address)
    {
        var text = address?.Trim();
        if (!TryRead(text, out var uri) || text.Contains('?', StringComparison.Ordinal) || text.Contains('#', StringComparison.Ordinal) || uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// The origin of a domain Umbraco assigns, such as <c>example.com/de</c>, when it is reached over <c>https</c>: one
    /// written with <c>https://</c>, or without a scheme, as a public site is reached. Null for a wildcard domain, one
    /// written with <c>http://</c> and a name that is no address.
    /// </summary>
    /// <param name="name">The domain's name as Umbraco stores it.</param>
    public static string? DomainOrigin(string? name)
    {
        var text = name?.Trim();
        if (string.IsNullOrEmpty(text) || text.StartsWith('*'))
        {
            return null;
        }

        var address = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
        return TryRead(address, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    private static bool TryRead([NotNullWhen(true)] string? address, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var read))
        {
            return false;
        }

        if (read.Scheme is not ("https" or "http") || read.Host.Length == 0 || read.UserInfo.Length > 0)
        {
            return false;
        }

        uri = read;
        return true;
    }
}
