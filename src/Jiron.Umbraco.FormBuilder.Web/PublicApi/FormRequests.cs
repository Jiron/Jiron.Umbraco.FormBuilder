using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the form and options endpoints read of a request and answer with (section 4.6, ADR-0084, ADR-0086): the form a
/// key or an alias names, and the answer with its <c>ETag</c>, a hash of the answer itself (ADR-0002); the culture and
/// the page are <see cref="FormPages"/>'s.
/// </summary>
internal sealed class FormRequests(
    PublishedFormCache forms,
    VisitorForms visitorForms,
    CascadingOptions cascading,
    SiteTimeZones timeZones,
    FormAvailability availability)
{
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

    /// <summary>
    /// The answer of the form endpoint: the form's alias, its public revision, the culture and the site's time zone it is
    /// in, whether it takes entries now by its opening and closing times and its entry limit (ADR-0088), the visitor's
    /// form (ADR-0084), what the browser renders its visitor texts with, and its consents' wording hashes (ADR-0085).
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
        var open = await availability.CheckAsync(form.Published.Definition.Settings.Availability, form.Published.Form.Key, cancellationToken) is null;
        var buffer = new ArrayBufferWriter<byte>(visitor.Form.Length + visitor.MergeTags.Length + visitor.Consents.Length + 256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("alias", form.Published.Form.Alias);
            writer.WriteNumber("revision", form.Published.Form.PublicRevision);
            writer.WriteString("culture", culture);
            writer.WriteString("timeZone", zone.Id);
            writer.WriteBoolean("open", open);
            writer.WritePropertyName("form");
            writer.WriteRawValue(visitor.Form, skipInputValidation: true);
            writer.WritePropertyName("mergeTags");
            writer.WriteRawValue(visitor.MergeTags, skipInputValidation: true);
            writer.WritePropertyName("consents");
            writer.WriteRawValue(visitor.Consents, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Answer(buffer);
    }

    /// <summary>
    /// The answer of the options endpoint (ADR-0086): the culture, whether the field's options could be given, and its
    /// options for the answers a request gives up its chain of parents, each held to the options its field offers; a
    /// blank label shows the value, and a description and an image only when the option has one.
    /// </summary>
    /// <param name="form">The published form.</param>
    /// <param name="field">The field, one of the published version's, that has options.</param>
    /// <param name="answers">A field's answers as the request gives them, by the field's key; empty for none.</param>
    /// <param name="culture">The culture, one of the site's languages.</param>
    /// <param name="page">The page the form is shown on.</param>
    /// <param name="cancellationToken">Cancels the sources' work.</param>
    public async Task<FormAnswer> OptionsAsync(
        CachedForm form,
        FormField field,
        Func<Guid, IReadOnlyList<string>> answers,
        string culture,
        RequestPage page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(page);
        var resolved = await cascading.GetAsync(form.Published.Definition, field, answers, culture, page.Content, cancellationToken);
        var buffer = new ArrayBufferWriter<byte>(256 + (resolved.Options.Count * 64));
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("culture", culture);
            writer.WriteBoolean("available", resolved.IsAvailable);
            writer.WriteStartArray("options");
            foreach (var option in resolved.Options)
            {
                writer.WriteStartObject();
                writer.WriteString("value", StrictStringConverter.WellFormed(option.Value));
                writer.WriteString("label", StrictStringConverter.WellFormed(string.IsNullOrWhiteSpace(option.Label) ? option.Value : option.Label));
                if (!string.IsNullOrWhiteSpace(option.Description))
                {
                    writer.WriteString("description", StrictStringConverter.WellFormed(option.Description));
                }

                if (option.Image is { } image)
                {
                    writer.WriteString("image", image);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Answer(buffer);
    }

    // The bytes written, with the first 32 hex digits of their SHA-256 as the entity tag.
    private static FormAnswer Answer(ArrayBufferWriter<byte> buffer)
    {
        var bytes = buffer.WrittenSpan.ToArray();
        return new FormAnswer(bytes, $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"");
    }
}

/// <summary>An answer of the form or the options endpoint.</summary>
/// <param name="Body">The answer as UTF-8 JSON.</param>
/// <param name="ETag">Its entity tag, quoted.</param>
internal sealed record FormAnswer(byte[] Body, string ETag);
