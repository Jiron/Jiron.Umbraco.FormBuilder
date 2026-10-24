using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

// Create, save, publish, unpublish, rename and move (ADR-0044).
internal sealed partial class FormBuilderFormService
{
    /// <summary>The type of the consent field a new form starts with.</summary>
    private const string ConsentType = "consent";

    /// <inheritdoc />
    public async Task<FormInfo> CreateAsync(NewFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOwnUnit();
        var name = CheckName(request.Name);
        CheckAlias(request.Alias);
        var folder = await LiveFolderAsync(request.FolderKey, cancellationToken);
        var definition = await BlankAsync(cancellationToken);
        var document = Document(definition);
        var wanted = request.Alias ?? FormAliases.FromName(name);
        for (var attempt = 1; ; attempt++)
        {
            var alias = request.Alias ?? FormAliases.FirstFree(wanted, await forms.ListAliasesAsync(wanted, cancellationToken));
            await CancelableAsync(new FormSavingNotification(definition, alias, name, isNew: true, request.UserKey, new EventMessages()));
            SavedVersion created;
            try
            {
                created = await database.WriteAsync(
                    async _ =>
                    {
                        var saved = await forms.CreateAsync(new NewForm(definition.Key, folder?.Id, alias, name, document, UserKey: request.UserKey), cancellationToken);
                        var details = new AuditDetails().AddIdentifier("alias", alias).Add("version", saved.Version.Info.Number).Add("folder", folder?.Key);
                        await audit.AddAsync(Entry(CreatedAction, saved.Form, request.UserKey, details), cancellationToken);
                        return saved;
                    },
                    cancellationToken);
            }
            catch (FormBuilderConflictException exception) when (exception.Conflict == FormBuilderConflict.DuplicateAlias && request.Alias is null && attempt < AliasAttempts)
            {
                // A create at the same moment took the alias: the next free one, announced again.
                continue;
            }

            var info = Info(created.Form, folder?.Key);
            await PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, created.Version.Info.Number, isNew: true, request.UserKey));
            return info;
        }
    }

    /// <inheritdoc />
    public async Task<FormVersionResult?> SaveDraftAsync(Guid key, int expectedConcurrencyVersion, FormDefinition definition, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        EnsureOwnUnit();
        CheckComment(comment);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion);
        if (definition.Key != form.Key)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, JsonPaths.Property(JsonPaths.Root, "key"), "The definition's key is not the form's.");
        }

        var problems = serializer.CheckSave(definition);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var document = Document(definition);
        var draft = form.DraftVersionId is { } draftId ? await forms.GetVersionByIdAsync(draftId, cancellationToken) : null;
        if (draft is { Info.SchemaVersion: FormDefinition.CurrentSchemaVersion } && draft.Info.DefinitionHash == document.DefinitionHash)
        {
            return new FormVersionResult(await InfoAsync(form, cancellationToken), null);
        }

        await CancelableAsync(new FormSavingNotification(definition, form.Alias, form.Name, isNew: false, userKey, new EventMessages()));
        var saved = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.SaveDraftAsync(form.Id, expectedConcurrencyVersion, document, comment, userKey, cancellationToken);
                if (stored is not null)
                {
                    await audit.AddAsync(Entry(SavedAction, stored.Form, userKey, new AuditDetails().Add("version", stored.Version.Info.Number)), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (saved is null)
        {
            return null;
        }

        var info = await InfoAsync(saved.Form, cancellationToken);
        await PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, saved.Version.Info.Number, isNew: false, userKey));
        return new FormVersionResult(info, saved.Version.Info.Number);
    }

    /// <inheritdoc />
    public async Task<FormVersionResult?> PublishAsync(Guid key, int expectedConcurrencyVersion, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        CheckComment(comment);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion);
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

        var document = Document(definition);
        if (form.PublishedVersionId is { } liveId
            && await forms.GetVersionByIdAsync(liveId, cancellationToken) is { } live
            && HashOf(live) == document.DefinitionHash)
        {
            return new FormVersionResult(await InfoAsync(form, cancellationToken), null);
        }

        var problems = new List<DefinitionProblem>();
        foreach (var check in _publishChecks)
        {
            problems.AddRange(await check.CheckAsync(definition, cancellationToken));
        }

        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        await CancelableAsync(new FormPublishingNotification(definition, form.Alias, form.Name, userKey, new EventMessages()));
        var catalogue = FormCatalogue.Of(definition, types);
        var published = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.PublishAsync(form.Id, expectedConcurrencyVersion, document, definition.Settings.Appearance.Theme, comment, userKey, cancellationToken);
                if (stored is null)
                {
                    return null;
                }

                var number = stored.Version.Info.Number;
                var fields = await forms.ListFieldsAsync(form.Id, cancellationToken);
                var previous = await forms.GetLastPublishedNumberAsync(form.Id, number, cancellationToken);
                foreach (var (values, isNew) in FormCatalogue.Changes(catalogue, fields, number, previous))
                {
                    GC.KeepAlive(isNew
                        ? await forms.AddFieldAsync(form.Id, values, cancellationToken)
                        : await forms.UpdateFieldAsync(form.Id, values, cancellationToken));
                }

                var details = new AuditDetails()
                    .Add("version", number)
                    .Add("revision", stored.Form.PublicRevision)
                    .Add("revisionChanged", stored.Form.PublicRevision != form.PublicRevision);
                await audit.AddAsync(Entry(PublishedAction, stored.Form, userKey, details), cancellationToken);
                return stored;
            },
            cancellationToken);
        if (published is null)
        {
            return null;
        }

        var info = await InfoAsync(published.Form, cancellationToken);
        var revisionChanged = published.Form.PublicRevision != form.PublicRevision;
        await PublishedAsync(new FormPublishedNotification(info.Key, info.Alias, info.Name, published.Version.Info.Number, info.PublicRevision, revisionChanged, userKey));
        return new FormVersionResult(info, published.Version.Info.Number);
    }

    /// <inheritdoc />
    public async Task<FormInfo?> UnpublishAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        // Taking a form offline only lessens what visitors get, so it applies in the trash too: it lets go of the theme.
        CheckCurrent(form, expectedConcurrencyVersion, inTrash: true);
        if (form.PublishedVersionId is null)
        {
            return await InfoAsync(form, cancellationToken);
        }

        await CancelableAsync(new FormUnpublishingNotification(form.Key, form.Alias, form.Name, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.UnpublishAsync(form.Id, expectedConcurrencyVersion, userKey, cancellationToken);
                if (stored is not null)
                {
                    await audit.AddAsync(Entry(UnpublishedAction, stored, userKey, new AuditDetails()), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        var info = await InfoAsync(changed, cancellationToken);
        await PublishedAsync(new FormUnpublishedNotification(info.Key, info.Alias, info.Name, userKey));
        return info;
    }

    /// <inheritdoc />
    public async Task<FormInfo?> RenameAsync(Guid key, int expectedConcurrencyVersion, string name, string? alias, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        var newName = CheckName(name);
        CheckAlias(alias);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion);
        var newAlias = alias ?? form.Alias;
        if (newName == form.Name && newAlias == form.Alias)
        {
            return await InfoAsync(form, cancellationToken);
        }

        await CancelableAsync(new FormRenamingNotification(form.Key, newAlias, newName, form.Alias, form.Name, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.UpdateAsync(new FormUpdate(form.Id, expectedConcurrencyVersion, newAlias, newName, form.FolderId, form.IsRestricted, userKey), cancellationToken);
                if (stored is not null)
                {
                    var details = new AuditDetails().AddIdentifier("alias", newAlias).AddIdentifier("previousAlias", form.Alias).Add("nameChanged", newName != form.Name);
                    await audit.AddAsync(Entry(RenamedAction, stored, userKey, details), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        var info = await InfoAsync(changed, cancellationToken);
        await PublishedAsync(new FormRenamedNotification(info.Key, info.Alias, info.Name, form.Alias, form.Name, userKey));
        return info;
    }

    /// <inheritdoc />
    public async Task<FormInfo?> MoveAsync(Guid key, int expectedConcurrencyVersion, Guid? folderKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion);
        var folder = await LiveFolderAsync(folderKey, cancellationToken);
        if (folder?.Id == form.FolderId)
        {
            return await InfoAsync(form, cancellationToken);
        }

        var previousFolderKey = form.FolderId is { } previousId ? (await folders.GetByIdAsync(previousId, cancellationToken))?.Key : null;
        await CancelableAsync(new FormMovingNotification(form.Key, form.Alias, form.Name, folder?.Key, previousFolderKey, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.UpdateAsync(new FormUpdate(form.Id, expectedConcurrencyVersion, form.Alias, form.Name, folder?.Id, form.IsRestricted, userKey), cancellationToken);
                if (stored is not null)
                {
                    var details = new AuditDetails().Add("folder", folder?.Key).Add("previousFolder", previousFolderKey);
                    await audit.AddAsync(Entry(MovedAction, stored, userKey, details), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        await PublishedAsync(new FormMovedNotification(changed.Key, changed.Alias, changed.Name, folder?.Key, previousFolderKey, userKey));
        return Info(changed, folder?.Key);
    }

    // A form from blank: one page with one group, and one outcome without text, which shows the built-in thank-you
    // message in the visitor's language; the group starts with the consent field of the new-form defaults.
    private async Task<FormDefinition> BlankAsync(CancellationToken cancellationToken)
    {
        var blank = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages = [new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }],
            Outcomes = [new FormOutcome { Key = Guid.NewGuid() }],
        };
        var general = await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken);
        if (general.Value.NewForms?.Consent is not { } consent)
        {
            return blank;
        }

        // The settings save validates the texts; a row written another way may hold what a definition refuses, and a new
        // form then starts without the field rather than not at all.
        FormField? field;
        try
        {
            field = ConsentField(consent);
        }
        catch (ArgumentException)
        {
            field = null;
        }

        var withConsent = field is null ? null : blank with { Pages = [blank.Pages[0] with { Groups = [blank.Pages[0].Groups[0] with { Fields = [field] }] }] };
        if (withConsent is null || serializer.CheckSave(withConsent).Count > 0)
        {
            LogConsentSkipped(logger, blank.Key);
            return blank;
        }

        return withConsent;
    }

    // The required consent field of the new-form defaults, with the site's own label and wording (ADR-0044), or null
    // when the wording is not rich text a definition takes: the save checks do not read a field's settings, so the
    // wording is checked here, as the settings save checks it.
    private static FormField? ConsentField(NewFormConsent consent)
    {
        var wording = new LocalizedHtml(consent.Text);
        if (string.IsNullOrWhiteSpace(wording.Default) || DefinitionSaveChecks.CheckText(wording, html: true).Count > 0)
        {
            return null;
        }

        var text = new JsonObject();
        foreach (var (culture, html) in wording.Texts)
        {
            text[culture] = html;
        }

        return new FormField
        {
            Key = Guid.NewGuid(),
            Type = ConsentType,
            Alias = NewFormConsent.FieldAlias,
            Label = new LocalizedText(consent.Label),
            Required = true,
            Settings = SettingsObject.Parse(new JsonObject { ["text"] = text }.ToJsonString()),
        };
    }
}
