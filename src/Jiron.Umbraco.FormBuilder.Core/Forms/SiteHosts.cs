using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The site a request's host names, for a request that names no page (ADR-0008, ADR-0232): the root of the content an
/// Umbraco domain on that host is assigned to. Only a domain with a host counts, since a path such as <c>/en</c> names
/// none; a domain that names a port counts on that port alone, and one without on any. A host whose domains lead to two
/// sites names neither, so a request never gets a site by the order the domains were added in. The host only chooses
/// among the sites Umbraco knows: it never becomes an address (ADR-0067) and never chooses what protects a form (ADR-0147).
/// </summary>
/// <param name="domains">Umbraco's domains.</param>
/// <param name="ids">Maps a domain's content id to its key.</param>
/// <param name="navigation">Where each content item sits in the tree, outside the Recycle Bin.</param>
internal sealed class SiteHosts(IDomainCacheService domains, IIdKeyMap ids, IDocumentNavigationQueryService navigation)
{
    /// <summary>The root of the site the host names, or null for a host no domain names, or one that names two sites.</summary>
    /// <param name="host">The request's host as it sent it, with its port if any (<c>site-a.localhost:5538</c>), or null.</param>
    public Guid? SiteOf(string? host)
    {
        if (AddressOf(host) is not { } asked)
        {
            return null;
        }

        Guid? found = null;
        foreach (var domain in domains.GetAll(includeWildcards: false))
        {
            if (domain.IsWildcard || DomainAddress(domain.Name) is not { } named || !Matches(named, asked))
            {
                continue;
            }

            var key = ids.GetKeyForId(domain.ContentId, UmbracoObjectTypes.Document);
            if (!key.Success || RootOf(key.Result) is not { } root)
            {
                continue;
            }

            if (found is { } other && other != root)
            {
                return null;
            }

            found = root;
        }

        return found;
    }

    /// <summary>The host and port of a request's host; null for none, or for one that is not a host.</summary>
    /// <param name="host">The host as the request sent it.</param>
    internal static HostAddress? AddressOf(string? host) =>
        string.IsNullOrWhiteSpace(host) || host.IndexOfAny(['/', '\\', '@', '?', '#', ' ']) >= 0 ? null : Parse("http://" + host);

    /// <summary>The host and port a domain names; null for a domain that names no host, such as <c>/en</c>.</summary>
    /// <param name="name">The domain's name as Umbraco keeps it: <c>site-a.localhost</c>, <c>https://site-a.localhost:5538/en</c>.</param>
    internal static HostAddress? DomainAddress(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/'))
        {
            return null;
        }

        return Parse(name.Contains("://", StringComparison.Ordinal) ? name : "http://" + name);
    }

    /// <summary>Whether a domain names the host a request was sent to: the same host, and the domain's port if it names one.</summary>
    internal static bool Matches(HostAddress domain, HostAddress request) =>
        string.Equals(domain.Host, request.Host, StringComparison.Ordinal) && (domain.Port is null || domain.Port == request.Port);

    private static HostAddress? Parse(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IdnHost.Length > 0
            ? new HostAddress(uri.IdnHost.ToLowerInvariant(), uri.IsDefaultPort ? null : uri.Port)
            : null;

    // The root of the site a content item is in; null for an item in the Recycle Bin or gone.
    private Guid? RootOf(Guid content) =>
        navigation.TryGetAncestorsKeys(content, out var ancestors)
            ? ancestors.LastOrDefault() is { } root && root != Guid.Empty ? root : content
            : null;
}

/// <summary>A host, lowercased in its ASCII form, and the port it names; null for the scheme's own.</summary>
/// <param name="Host">The host.</param>
/// <param name="Port">The port, or null for the default one.</param>
internal sealed record HostAddress(string Host, int? Port);
