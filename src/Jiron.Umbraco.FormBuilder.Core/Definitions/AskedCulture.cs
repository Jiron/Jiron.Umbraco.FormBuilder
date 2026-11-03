using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// The culture a designer's request asks texts in (ADR-0172, ADR-0173): a culture's name such as <c>de-CH</c>, and
/// anything else the default language's texts, as the empty culture asks for them.
/// </summary>
internal static partial class AskedCulture
{
    /// <summary>The culture as asked, or the empty culture when it is no culture's name.</summary>
    /// <param name="culture">The culture the request names.</param>
    public static string Of(string? culture) => culture is not null && CultureName().IsMatch(culture) ? culture : string.Empty;

    [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CultureName();
}
