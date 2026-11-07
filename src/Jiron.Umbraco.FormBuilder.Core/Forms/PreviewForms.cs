using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The forms the preview shows (ADR-0203): the version a preview ticket names, as the website would serve it if it were
/// published, with the revision that would give it, and its public projection, as <see cref="PublishedFormCache"/> gives
/// the published one. Read for each
/// request and kept nowhere: a preview asks a few times a minute, and only who may design the form gets a ticket.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="types">The field types, which the projection reads.</param>
internal sealed class PreviewForms(IFormBuilderFormService forms, FieldTypeRegistry types)
{
    /// <summary>The version of the form as the preview shows it; null when it is missing, in the trash or unreadable.</summary>
    /// <param name="key">The form's key.</param>
    /// <param name="number">The version's number, as the ticket names it.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<CachedForm?> GetAsync(Guid key, int number, CancellationToken cancellationToken = default)
    {
        try
        {
            // The revision the version would get once published, the form's next: a page posts one of 1 or more, and the
            // ticket, not the revision, holds the frame to the version it shows.
            return await forms.GetPreviewAsync(key, number, cancellationToken) is { } read
                ? new CachedForm(read with { Form = read.Form with { PublicRevision = read.Form.PublicRevision + 1 } }, PublicProjection.Create(read.Definition, types))
                : null;
        }
        catch (FormBuilderValidationException)
        {
            // A version that cannot be read shows as missing, as the published one does (PublishedFormCache).
            return null;
        }
    }
}
