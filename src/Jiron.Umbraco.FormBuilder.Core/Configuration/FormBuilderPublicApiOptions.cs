using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>
/// The public API visitors' browsers and headless sites call (section 4.6, ADR-0012, ADR-0084):
/// <c>JironFormBuilder:PublicApi</c>.
/// </summary>
public sealed class FormBuilderPublicApiOptions
{
    /// <summary>The base path unless the host sets another.</summary>
    public const string DefaultBasePath = "/jfb/api";

    /// <summary>
    /// The path the API lives under, before its version (<c>/v1</c>): <c>/</c> and one or more segments of letters,
    /// digits, '.', '-', '_' and '~', at most 100 characters in all, and neither <c>/umbraco</c>, <c>/App_Plugins</c> or
    /// <c>/jfb/assets</c> nor a path below or above one of them.
    /// </summary>
    public string BasePath { get; set; } = DefaultBasePath;

    /// <summary>
    /// The other sites whose pages may call the API from the browser, each an origin such as
    /// <c>https://www.example.com</c>: <c>http</c> or <c>https</c>, a host and a port when it is not the scheme's, and
    /// nothing after them. The site's own pages may always call it. None by default.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];
}

/// <summary>The rules of the public API's host configuration (ADR-0084), which the start-up check and the API share.</summary>
internal static partial class PublicApiRules
{
    /// <summary>The longest base path.</summary>
    public const int MaxBasePathLength = 100;

    // Paths the API must stay out of: Umbraco's own, the backoffice files and the website assets (section 4.6).
    private static readonly ImmutableArray<string> Reserved = ["/umbraco", "/App_Plugins", "/jfb/assets"];

    /// <summary>Whether <paramref name="path"/> may be the base path; <paramref name="problem"/> says why not.</summary>
    public static bool IsBasePath(string? path, out string problem)
    {
        problem = string.Empty;
        if (path is null || path.Length > MaxBasePathLength || !BasePathShape().IsMatch(path)
            || path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment.All(c => c == '.')))
        {
            problem = string.Create(
                CultureInfo.InvariantCulture,
                $"must be '/' and one or more segments of letters, digits, '.', '-', '_' and '~', not only dots, such as '{FormBuilderPublicApiOptions.DefaultBasePath}', at most {MaxBasePathLength} characters, without a '/' at the end.");
            return false;
        }

        foreach (var reserved in Reserved)
        {
            if (path.Equals(reserved, StringComparison.OrdinalIgnoreCase) || path.StartsWith(reserved + "/", StringComparison.OrdinalIgnoreCase)
                || reserved.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
            {
                problem = $"must not be '{reserved}', lie below it or hold it, since the site serves its own pages and files there.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// An origin as browsers send it (<c>https://www.example.com</c>, <c>http://localhost:8080</c>): the scheme and the
    /// host in lowercase, the host in its ASCII form, and the port only when it is not the scheme's; null for anything
    /// else, such as <c>null</c>, <c>*</c> or an address with a path.
    /// </summary>
    public static string? Origin(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048 || text.Trim() != text
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0 || uri.Host.Length == 0
            || uri.PathAndQuery != "/" || uri.Fragment.Length > 0
            || text.EndsWith('/') || text.Contains('?', StringComparison.Ordinal) || text.Contains('#', StringComparison.Ordinal))
        {
            return null;
        }

        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.IdnHost}]" : uri.IdnHost;
        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{host.ToLowerInvariant()}"
            : string.Create(CultureInfo.InvariantCulture, $"{uri.Scheme}://{host.ToLowerInvariant()}:{uri.Port}");
    }

    [GeneratedRegex(@"^(/[A-Za-z0-9._~-]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex BasePathShape();
}
