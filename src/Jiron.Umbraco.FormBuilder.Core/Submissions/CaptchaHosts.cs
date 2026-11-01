using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The hosts a captcha's token may be made for (ADR-0012, ADR-0142): the site's configured domains and the origins allowed
/// to call the public API, and never the request's <c>Host</c> header, which a caller chooses.
/// </summary>
/// <param name="domains">Umbraco's domains.</param>
/// <param name="options">The allowed origins.</param>
internal sealed class CaptchaHosts(IDomainService domains, IOptions<FormBuilderOptions> options)
{
    /// <summary>The hosts, in lower case; empty for a site with neither domains nor allowed origins.</summary>
    public async Task<IReadOnlySet<string>> GetAsync()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in await domains.GetAllAsync(includeWildcards: false))
        {
            if (HostOf(domain.DomainName) is { } host)
            {
                hosts.Add(host);
            }
        }

        foreach (var origin in options.Value.PublicApi.AllowedOrigins)
        {
            if (HostOf(origin) is { } host)
            {
                hosts.Add(host);
            }
        }

        return hosts;
    }

    // A domain may be written with or without its scheme, and with a path.
    private static string? HostOf(string written) =>
        Uri.TryCreate(written.Contains("://", StringComparison.Ordinal) ? written : "https://" + written, UriKind.Absolute, out var address) ? address.IdnHost : null;
}
