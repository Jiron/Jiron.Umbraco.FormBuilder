using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The stored versions of forms as the form services read and write them (ADR-0044, ADR-0053): a definition as the
/// document a write stores, a stored version read and upgraded, the draft, and the site's limits every write meets.
/// Stored definitions are read with the highest limits a host may set; what is written meets the site's limits
/// (ADR-0042). A form as the services answer with it is <see cref="FormInfos"/>'s, the checks before a change are
/// <see cref="FormChecks"/>' and the audit entries are <see cref="FormAudit"/>'s.
/// </summary>
internal sealed class FormDocuments(
    IFormRepository forms,
    FormDefinitionSerializer serializer,
    FieldTypeRegistry types)
{
    /// <summary>The stored document of a definition: its canonical form, its hash and the hash of its public projection.</summary>
    public VersionDocument Document(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var canonical = FormDefinitionSerializer.Write(definition);
        return new VersionDocument(
            FormDefinition.CurrentSchemaVersion,
            Encoding.UTF8.GetString(canonical),
            CanonicalJson.Hash(canonical),
            PublicProjection.Hash(definition, types))
        {
            Uses = FormLibraryUses.Of(definition),
        };
    }

    /// <summary>The hash of a definition's canonical form in the current schema.</summary>
    public static string HashOf(FormDefinition definition) => CanonicalJson.Hash(FormDefinitionSerializer.Write(definition));

    /// <summary>The hash of a stored version once read and upgraded, or null when it cannot be read.</summary>
    public string? HashOf(StoredFormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return serializer.ReadStored(version.Definition).Definition is { } definition ? HashOf(definition) : null;
    }

    /// <summary>A stored version read and upgraded.</summary>
    /// <exception cref="FormBuilderValidationException">The version cannot be read, with what is wrong with it.</exception>
    public FormDefinition Readable(StoredFormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var read = serializer.ReadStored(version.Definition);
        return read.Definition ?? throw new FormBuilderValidationException(read.Problems);
    }

    /// <summary>Refuses a definition above the site's limits (ADR-0042), with every problem.</summary>
    /// <exception cref="FormBuilderValidationException">A save refuses the definition.</exception>
    public void CheckSaveable(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var problems = serializer.CheckSave(definition);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }
    }

    /// <summary>The form's draft as stored, or null.</summary>
    public async Task<StoredFormVersion?> DraftOfAsync(StoredForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return form.DraftVersionId is { } draftId ? await forms.GetVersionByIdAsync(draftId, cancellationToken) : null;
    }

    /// <summary>
    /// The version a form's entries are shown and exported by: the one the site serves, the last one it served when the
    /// form was taken offline, or the draft of a form never published; null without any. Entries come only from
    /// published versions, as their field catalogue does, so a draft's changes never decide what entries show.
    /// </summary>
    public async Task<StoredFormVersion?> ServedOfAsync(StoredForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.PublishedVersionId is { } publishedId)
        {
            return await forms.GetVersionByIdAsync(publishedId, cancellationToken);
        }

        return await forms.GetLastPublishedNumberAsync(form.Id, int.MaxValue, cancellationToken) is { } number
            ? await forms.GetVersionAsync(form.Id, number, cancellationToken)
            : await DraftOfAsync(form, cancellationToken);
    }
}
