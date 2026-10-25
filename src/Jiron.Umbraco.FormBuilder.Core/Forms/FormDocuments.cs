using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Forms and their stored versions as the form services read and write them (ADR-0044, ADR-0053): a definition as the
/// document a write stores, a stored version read and upgraded, the draft, the site's limits every write meets, a form
/// as the services answer with it, and the folder a form may go to. Stored definitions are read with the highest limits
/// a host may set; what is written meets the site's limits (ADR-0042).
/// </summary>
internal sealed class FormDocuments(
    IFormRepository forms,
    IFolderRepository folders,
    FormDefinitionSerializer serializer,
    FieldTypeCatalogue types)
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
            PublicProjection.Hash(definition, types));
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

    /// <summary>A form as the services answer with it, with its folder's key.</summary>
    public async Task<FormInfo> InfoAsync(StoredForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Info(form, form.FolderId is { } folderId ? (await folders.GetByIdAsync(folderId, cancellationToken))?.Key : null);
    }

    /// <summary>A form as the services answer with it, in the folder with <paramref name="folderKey"/> (null: the root).</summary>
    public static FormInfo Info(StoredForm form, Guid? folderKey)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new FormInfo(
            form.Key,
            form.Alias,
            form.Name,
            folderKey,
            form.ConcurrencyVersion,
            form.PublishedVersionId is not null,
            form.PublicRevision,
            form.IsRestricted,
            form.TrashedUtc,
            form.CreatedUtc,
            form.UpdatedUtc,
            form.CreatedByUserKey,
            form.UpdatedByUserKey);
    }

    /// <summary>A folder a form may go to, which is outside the trash; null for the root.</summary>
    /// <exception cref="FormBuilderConflictException">The folder is missing or in the trash.</exception>
    public async Task<StoredFolder?> LiveFolderAsync(Guid? folderKey, CancellationToken cancellationToken)
    {
        if (folderKey is not { } key)
        {
            return null;
        }

        return await folders.GetAsync(key, cancellationToken) is { TrashedUtc: null } folder
            ? folder
            : throw new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The folder does not exist or is in the trash.");
    }

    /// <summary>The audit entry of a change to a form.</summary>
    public static NewAuditEntry Entry(string action, StoredForm form, Guid? userKey, AuditDetails details)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new NewAuditEntry(action, FormBuilderFormService.EntityType, form.Key, userKey, details);
    }
}
