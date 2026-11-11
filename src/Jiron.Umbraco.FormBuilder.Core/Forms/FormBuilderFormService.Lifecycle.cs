using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

// Create, save, publish, unpublish, rename and move (ADR-0044).
internal sealed partial class FormBuilderFormService
{
    /// <inheritdoc />
    public async Task<FormInfo> CreateAsync(NewFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        var name = FormChecks.Name(request.Name);
        FormChecks.Alias(request.Alias);
        var folder = await LibraryFolders.OutsideTrashAsync(folders, request.FolderKey, cancellationToken);
        var definition = await creation.BlankAsync(cancellationToken);
        return await creation.CreateAsync(definition, name, request.Alias, FormAliases.FromName(name), folder, request.UserKey, new FormOrigin(FormSource.Blank, null, CreatedAction), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormVersionResult?> SaveDraftAsync(Guid key, int expectedConcurrencyVersion, FormDefinition definition, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        FormChecks.Comment(comment);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion);
        if (definition.Key != form.Key)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, JsonPaths.Property(JsonPaths.Root, "key"), "The definition's key is not the form's.");
        }

        documents.CheckSaveable(definition);
        var document = documents.Document(definition);
        var draft = await documents.DraftOfAsync(form, cancellationToken);
        if (draft is { Info.SchemaVersion: FormDefinition.CurrentSchemaVersion } && draft.Info.DefinitionHash == document.DefinitionHash)
        {
            return new FormVersionResult(await infos.InfoAsync(form, cancellationToken), null);
        }

        // A locked setting keeps the lock's value, or the one the draft held from before the lock came (ADR-0060).
        var stored = draft is null ? null : serializer.ReadStored(draft.Definition).Definition;
        locks.CheckSave(definition, stored);
        actionLocks.CheckSave(definition, stored);

        await changes.CancelableAsync(new FormSavingNotification(definition, form.Alias, form.Name, isNew: false, userKey, new EventMessages()));
        var saved = await changes.WriteAsync(
            () => forms.SaveDraftAsync(form.Id, expectedConcurrencyVersion, document, comment, userKey, cancellationToken),
            stored => stored is null ? [] : [FormAudit.Entry(SavedAction, stored.Form, userKey, new AuditDetails().Add("version", stored.Version.Info.Number))],
            cancellationToken);
        if (saved is null)
        {
            return null;
        }

        var info = await infos.InfoAsync(saved.Form, cancellationToken);
        await changes.PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, saved.Version.Info.Number, isNew: false, userKey), FormAudit.Subject(info.Key));
        return new FormVersionResult(info, saved.Version.Info.Number);
    }

    /// <inheritdoc />
    public async Task<FormVersionResult?> PublishAsync(Guid key, int expectedConcurrencyVersion, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        FormChecks.Comment(comment);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion);
        if (form.DraftVersionId is not { } draftId || await forms.GetVersionByIdAsync(draftId, cancellationToken) is not { } draft)
        {
            return null;
        }

        // What goes live is the draft the editor saved, read as stored and upgraded (ADR-0042), then held to the site's
        // limits and every publish check.
        var read = serializer.ReadStored(draft.Definition);
        if (read.Definition is not { } definition)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        var document = documents.Document(definition);
        if (form.PublishedVersionId is { } liveId
            && await forms.GetVersionByIdAsync(liveId, cancellationToken) is { } live
            && documents.HashOf(live) == document.DefinitionHash)
        {
            return new FormVersionResult(await infos.InfoAsync(form, cancellationToken), null);
        }

        // The form check's errors together, over the draft as the site will run it (ADR-0177); its warnings and hints
        // never keep a form from going live.
        var errors = (await formCheck.CheckAsync(definition, cancellationToken))
            .Where(issue => issue.Severity == FormCheckSeverity.Error)
            .Select(issue => issue.ToProblem())
            .ToList();
        if (errors.Count > 0)
        {
            throw new FormBuilderValidationException(errors);
        }

        await changes.CancelableAsync(new FormPublishingNotification(definition, form.Alias, form.Name, userKey, new EventMessages()));
        var catalogue = FieldCatalogue.Of(definition, types);
        var published = await changes.WriteAsync(
            async () =>
            {
                var stored = await forms.PublishAsync(form.Id, expectedConcurrencyVersion, document, definition.Settings.Appearance.Theme, comment, userKey, cancellationToken);
                if (stored is not null)
                {
                    await UpdateCatalogueAsync(form.Id, catalogue, stored.Version.Info.Number, cancellationToken);
                }

                return stored;
            },
            stored => stored is null
                ? []
                : [FormAudit.Entry(PublishedAction, stored.Form, userKey, new AuditDetails().Add("version", stored.Version.Info.Number).Add("revision", stored.Form.PublicRevision).Add("revisionChanged", stored.Form.PublicRevision != form.PublicRevision))],
            cancellationToken);
        if (published is null)
        {
            return null;
        }

        formCache.Changed(key);
        var info = await infos.InfoAsync(published.Form, cancellationToken);
        var revisionChanged = published.Form.PublicRevision != form.PublicRevision;
        await changes.PublishedAsync(new FormPublishedNotification(info.Key, info.Alias, info.Name, published.Version.Info.Number, info.PublicRevision, revisionChanged, userKey), FormAudit.Subject(info.Key));
        return new FormVersionResult(info, published.Version.Info.Number);
    }

    /// <inheritdoc />
    public async Task<FormInfo?> UnpublishAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        // Taking a form offline only lessens what visitors get, so it applies in the trash too: it lets go of the theme.
        FormChecks.Current(form, expectedConcurrencyVersion, inTrash: true);
        if (form.PublishedVersionId is null)
        {
            return await infos.InfoAsync(form, cancellationToken);
        }

        await changes.CancelableAsync(new FormUnpublishingNotification(form.Key, form.Alias, form.Name, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            () => forms.UnpublishAsync(form.Id, expectedConcurrencyVersion, userKey, cancellationToken),
            stored => stored is null ? [] : [FormAudit.Entry(UnpublishedAction, stored, userKey, new AuditDetails())],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        formCache.Changed(key);
        var info = await infos.InfoAsync(changed, cancellationToken);
        await changes.PublishedAsync(new FormUnpublishedNotification(info.Key, info.Alias, info.Name, userKey), FormAudit.Subject(info.Key));
        return info;
    }

    /// <inheritdoc />
    public async Task<FormInfo?> RenameAsync(Guid key, int expectedConcurrencyVersion, string name, string? formAlias, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        var newName = FormChecks.Name(name);
        FormChecks.Alias(formAlias);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion);
        var newAlias = formAlias ?? form.Alias;
        if (newName == form.Name && newAlias == form.Alias)
        {
            return await infos.InfoAsync(form, cancellationToken);
        }

        await changes.CancelableAsync(new FormRenamingNotification(form.Key, newAlias, newName, form.Alias, form.Name, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            () => forms.UpdateAsync(new FormUpdate(form.Id, expectedConcurrencyVersion, newAlias, newName, form.FolderId, form.IsRestricted, userKey), cancellationToken),
            stored => stored is null
                ? []
                : [FormAudit.Entry(RenamedAction, stored, userKey, new AuditDetails().AddIdentifier("alias", newAlias).AddIdentifier("previousAlias", form.Alias).Add("nameChanged", newName != form.Name))],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        formCache.Changed(key);
        var info = await infos.InfoAsync(changed, cancellationToken);
        await changes.PublishedAsync(new FormRenamedNotification(info.Key, info.Alias, info.Name, form.Alias, form.Name, userKey), FormAudit.Subject(info.Key));
        return info;
    }

    /// <inheritdoc />
    public async Task<FormInfo?> MoveAsync(Guid key, int expectedConcurrencyVersion, Guid? folderKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion);
        var folder = await LibraryFolders.OutsideTrashAsync(folders, folderKey, cancellationToken);
        if (folder?.Id == form.FolderId)
        {
            return await infos.InfoAsync(form, cancellationToken);
        }

        var previousFolderKey = form.FolderId is { } previousId ? (await folders.GetByIdAsync(previousId, cancellationToken))?.Key : null;
        await changes.CancelableAsync(new FormMovingNotification(form.Key, form.Alias, form.Name, folder?.Key, previousFolderKey, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            () => forms.UpdateAsync(new FormUpdate(form.Id, expectedConcurrencyVersion, form.Alias, form.Name, folder?.Id, form.IsRestricted, userKey), cancellationToken),
            stored => stored is null
                ? []
                : [FormAudit.Entry(MovedAction, stored, userKey, new AuditDetails().Add("folder", folder?.Key).Add("previousFolder", previousFolderKey))],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        await changes.PublishedAsync(new FormMovedNotification(changed.Key, changed.Alias, changed.Name, folder?.Key, previousFolderKey, userKey), FormAudit.Subject(changed.Key));
        return FormInfos.Info(changed, folder?.Key);
    }

    // The field catalogue after a publish: the fields it adds, and those it updates, brings back or ends.
    private async Task UpdateCatalogueAsync(int formId, IReadOnlyList<CatalogueEntry> catalogue, int number, CancellationToken cancellationToken)
    {
        var fields = await forms.ListFieldsAsync(formId, cancellationToken);
        var previous = await forms.GetLastPublishedNumberAsync(formId, number, cancellationToken);
        foreach (var (values, isNew) in FieldCatalogue.Changes(catalogue, fields, number, previous))
        {
            if (isNew)
            {
                await forms.AddFieldAsync(formId, values, cancellationToken);
            }
            else
            {
                await forms.UpdateFieldAsync(formId, values, cancellationToken);
            }
        }
    }
}
