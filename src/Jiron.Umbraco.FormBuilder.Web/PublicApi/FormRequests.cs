using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the form endpoint reads of a request and answers with (section 4.6, ADR-0084): the form a key or an alias names,
/// the culture among the site's languages, the site of the content the form is shown on, and the answer with its
/// <c>ETag</c>, a hash of the answer itself (ADR-0002).
/// </summary>
internal sealed class FormRequests(
    PublishedFormCache forms,
    VisitorForms visitorForms,
    SiteTimeZones timeZones,
    ILanguageService languages,
    IPublishedContentCache contents,
    IDocumentNavigationQueryService navigation,
    TimeProvider time)
{
    /// <summary>The longest culture name a request may give.</summary>
    public const int MaxCultureLength = 85;

    /// <summary>
    /// The key of the form a request names: a key as a GUID with hyphens, or an alias in any letter case; null when it
    /// names none. A text that is neither is answered without reading anything.
    /// </summary>
    public async Task<Guid?> KeyOfAsync(string keyOrAlias, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyOrAlias);
        if (Guid.TryParseExact(keyOrAlias, "D", out var key))
        {
            return key;
        }

        return DefinitionStructureChecks.IsAlias(keyOrAlias.ToLowerInvariant()) ? await forms.KeyOfAsync(keyOrAlias, cancellationToken) : null;
    }

    /// <summary>Whether a request's culture can be read: none, or a culture name such as <c>de-CH</c>.</summary>
    public static bool IsCulture(string? culture) => culture is null || (culture.Length <= MaxCultureLength && LocalizedString.IsCultureName(culture));

    /// <summary>
    /// The culture a request is answered in: the site's language it names in any letter case, as Umbraco spells it, and
    /// otherwise the default language. So no culture a visitor makes up reaches a cache or the dictionary (ADR-0081).
    /// </summary>
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
    /// (section 4.8; the host's domain comes with 14.4).
    /// </summary>
    public async Task<RequestPage> PageAsync(Guid? contentKey)
    {
        if (contentKey is not { } key || await contents.GetByIdAsync(key) is null)
        {
            return new RequestPage(null, null);
        }

        return new RequestPage(key, navigation.TryGetAncestorsKeys(key, out var ancestors) && ancestors.LastOrDefault() is { } root && root != Guid.Empty ? root : key);
    }

    /// <summary>
    /// The answer of the form endpoint: the form's alias, its public revision, the culture and the site's time zone it is
    /// in, whether it is open now by its opening and closing times, the visitor's form (ADR-0084), what the browser renders
    /// its visitor texts with, and its consents' wording hashes (ADR-0085).
    /// </summary>
    public async Task<FormAnswer> DefinitionAsync(CachedForm form, string culture, RequestPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(page);
        var zone = await timeZones.GetAsync(page.Site, cancellationToken);
        var scope = new MergeTagScope
        {
            Form = form.Published.Definition,
            FormName = form.Published.Form.Name,
            FormAlias = form.Published.Form.Alias,
            Culture = culture,
            Site = page.Site,
            Page = page.Content,
        };
        var visitor = await visitorForms.WriteAsync(form.Projection, scope, cancellationToken);
        var buffer = new ArrayBufferWriter<byte>(visitor.Form.Length + visitor.MergeTags.Length + visitor.Consents.Length + 256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("alias", form.Published.Form.Alias);
            writer.WriteNumber("revision", form.Published.Form.PublicRevision);
            writer.WriteString("culture", culture);
            writer.WriteString("timeZone", zone.Id);
            writer.WriteBoolean("open", IsOpen(form.Published.Definition.Settings.Availability, time.GetUtcNow()));
            writer.WritePropertyName("form");
            writer.WriteRawValue(visitor.Form, skipInputValidation: true);
            writer.WritePropertyName("mergeTags");
            writer.WriteRawValue(visitor.MergeTags, skipInputValidation: true);
            writer.WritePropertyName("consents");
            writer.WriteRawValue(visitor.Consents, skipInputValidation: true);
            writer.WriteEndObject();
        }

        var bytes = buffer.WrittenSpan.ToArray();
        return new FormAnswer(bytes, $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"");
    }

    // Open from its opening time, up to but not at its closing time; the entry limit is the submission's to count (6.1e).
    private static bool IsOpen(FormAvailabilitySettings availability, DateTimeOffset now) =>
        (availability.OpensAt is not { } opens || now.UtcDateTime >= opens) && (availability.ClosesAt is not { } closes || now.UtcDateTime < closes);
}

/// <summary>The page a form is shown on.</summary>
/// <param name="Content">The published content page, which <c>page.*</c> reads; null for none.</param>
/// <param name="Site">Its site's root, whose settings apply; null for the values every site shares.</param>
internal sealed record RequestPage(Guid? Content, Guid? Site);

/// <summary>An answer of the form endpoint.</summary>
/// <param name="Body">The answer as UTF-8 JSON.</param>
/// <param name="ETag">Its entity tag, quoted.</param>
internal sealed record FormAnswer(byte[] Body, string ETag);
