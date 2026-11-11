using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Forms that come from other forms, templates and files, and the files forms go out as (ADR-0046, ADR-0053):
/// duplicates, copied actions, forms from templates, imports and exports. A copy or a new form takes the steps of every
/// change to a form (<see cref="IFormBuilderFormService"/>): a unit of work of its own, the cancelable notification, one
/// unit with the change and its audit entry, then the notification that it happened. A missing form gives null.
/// </summary>
public interface IFormBuilderFormExchangeService
{
    /// <summary>
    /// Creates a copy of a form's draft, with or without its actions, in which every element has a new key that the
    /// references follow (ADR-0046). Entries, versions, grants and the restriction stay with the form. Null when the
    /// form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">
    /// The name or the alias is not allowed, or the draft cannot be read or is above a lowered limit.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, the folder is missing or in the trash, or the form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormInfo?> DuplicateAsync(Guid key, DuplicateFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends copies of a form's actions to another form's draft as a draft save: the fields they name map by alias,
    /// and what has no match is kept and reported, as is each merge tag in their settings that names a field the target
    /// lacks (ADR-0046). Nothing to copy writes nothing. Null when either form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the target meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">
    /// An action key is not the source's, a draft cannot be read, or a save refuses the target with the copies.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The source or the target is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the save.</exception>
    Task<FormActionsCopy?> CopyActionsAsync(CopyActionsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a form from a built-in or saved form template, with new keys and the new-form defaults (ADR-0046). Null
    /// when no form template has the key.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The name or the alias is not allowed, or a save refuses the template.</exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, or the folder is missing or in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormInfo?> CreateFromTemplateAsync(TemplateFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a form from an export file, with new keys unless the mirror asks to keep them, and says what it changed or could not resolve
    /// (ADR-0046); for a user who may manage the library, with the option lists the file carries and the site lacks,
    /// when their kinds' checks on this site take them (ADR-0078).
    /// </summary>
    /// <exception cref="FormBuilderValidationException">
    /// The name or the alias is not allowed, or the file is refused, with every problem and its path in the file.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">
    /// The alias is taken, the folder is missing or in the trash, or other changes took the key or alias of a list to
    /// create at the same moment, three times in a row.
    /// </exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormImport> ImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// What an import of the file would make, without making it (ADR-0166): what the file holds and the notes the import
    /// would give, as this site and the user stand now. Nothing is written.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The file is refused, with every problem and its path in the file.</exception>
    Task<FormImportCheck> CheckImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a form's draft with an export file's definition, as a draft save (ADR-0220): the form keeps its key, name,
    /// alias, folder, access, entries and History, and the file's other keys are kept, so answers stay with their fields.
    /// Themes, option lists and actions the site lacks are handled as for an import (ADR-0046, ADR-0078). A file that holds
    /// the draft as it is writes nothing. Null when the form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">The file is refused, or a save refuses the form with its definition.</exception>
    /// <exception cref="FormBuilderConflictException">
    /// The form is in the trash, or other changes took the key or alias of a list to create, three times in a row.
    /// </exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the save.</exception>
    Task<FormReplace?> ReplaceAsync(ReplaceFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a replace of a form's draft with the file would save, without saving it: what the file holds and the notes it
    /// would give. Nothing is written.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The file is refused, with every problem and its path in the file.</exception>
    Task<FormImportCheck> CheckReplaceAsync(ImportFormRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The export file of a form's draft, or of the version with this number (ADR-0046), with the library option lists
    /// it names. Null when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The stored version cannot be read.</exception>
    Task<FormExport?> ExportAsync(Guid key, int? version = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed partial class FormBuilderFormExchangeService(
    IFormRepository forms,
    IFolderRepository folders,
    IOptionListRepository optionLists,
    FormThemes themes,
    IFormTemplateRepository templates,
    FormDocuments documents,
    FormInfos infos,
    FormCreation creation,
    FormImportLists importLists,
    FormDefinitionSerializer serializer,
    FieldSettingLocks locks,
    ActionSettingLocks actionLocks,
    ActionTypeRegistry actionTypes,
    IFormBuilderMergeTagService mergeTags,
    IDocumentNavigationQueryService navigation,
    TimeProvider time,
    FormBuilderChanges<FormBuilderFormExchangeService> changes,
    ILogger<FormBuilderFormExchangeService> logger)
    : IFormBuilderFormExchangeService
{
    /// <summary>The audit action of a form created from a template.</summary>
    public const string CreatedFromTemplateAction = "form.createdFromTemplate";

    /// <summary>The audit action of an import.</summary>
    public const string ImportedAction = "form.imported";

    /// <summary>The audit action of a duplicate.</summary>
    public const string DuplicatedAction = "form.duplicated";

    /// <summary>The audit action of actions copied into a form, on the form they went to.</summary>
    public const string ActionsCopiedAction = "form.actionsCopied";

    /// <summary>The audit action of a draft replaced with a file (ADR-0220).</summary>
    public const string ReplacedAction = "form.replaced";

    private static readonly string FilePath = JsonPaths.Property(JsonPaths.Root, "definition");

    private static readonly string FileNamePath = JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "form"), "name");

    /// <inheritdoc />
    public async Task<FormInfo?> DuplicateAsync(Guid key, DuplicateFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        var name = FormChecks.Name(request.Name);
        FormChecks.Alias(request.Alias);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.OutsideTrash(form);
        if (await documents.DraftOfAsync(form, cancellationToken) is not { } draft)
        {
            return null;
        }

        // The copy is the draft the designer shows, with new keys that its references follow; access stays with the
        // source, so the copy takes its folder's, as a new form does.
        var definition = documents.Readable(draft);
        var copy = DefinitionKeys.Renew(request.IncludeActions ? definition : definition with { Actions = [] }, serializer);
        var folder = await LibraryFolders.OutsideTrashAsync(folders, request.FolderKey, cancellationToken);
        documents.CheckSaveable(copy);
        var origin = new FormOrigin(
            FormSource.Duplicate,
            form.Key,
            DuplicatedAction,
            details => details.Add("source", form.Key).Add("sourceVersion", draft.Info.Number).Add("actions", request.IncludeActions));
        return await creation.CreateAsync(copy, name, request.Alias, FormAliases.FromName(name), folder, request.UserKey, origin, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormActionsCopy?> CopyActionsAsync(CopyActionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        if (await forms.GetAsync(request.SourceKey, cancellationToken) is not { } source
            || await forms.GetAsync(request.TargetKey, cancellationToken) is not { } target)
        {
            return null;
        }

        FormChecks.OutsideTrash(source);
        FormChecks.Current(target, request.ExpectedConcurrencyVersion);
        if (await documents.DraftOfAsync(source, cancellationToken) is not { } sourceDraft || await documents.DraftOfAsync(target, cancellationToken) is not { } targetDraft)
        {
            return null;
        }

        var actions = Chosen(documents.Readable(sourceDraft), request.ActionKeys);
        if (actions.Count == 0)
        {
            return new FormActionsCopy(new FormVersionResult(await infos.InfoAsync(target, cancellationToken), null), []);
        }

        var targetDefinition = documents.Readable(targetDraft);
        var copy = ActionCopies.Copy(documents.Readable(sourceDraft), actions, targetDefinition, serializer, text => MergeTagFieldPaths.Unknown(mergeTags, text, targetDefinition));
        var definition = copy.Target ?? throw new FormBuilderValidationException(copy.Problems);
        documents.CheckSaveable(definition);
        var document = documents.Document(definition);
        await changes.CancelableAsync(new FormSavingNotification(definition, target.Alias, target.Name, isNew: false, request.UserKey, new EventMessages()) { ActionsCopiedFrom = source.Key });
        var saved = await changes.WriteAsync(
            () => forms.SaveDraftAsync(target.Id, request.ExpectedConcurrencyVersion, document, null, request.UserKey, cancellationToken),
            stored => stored is null
                ? []
                : [FormAudit.Entry(
                    ActionsCopiedAction,
                    stored.Form,
                    request.UserKey,
                    new AuditDetails()
                        .Add("version", stored.Version.Info.Number)
                        .Add("source", source.Key)
                        .Add("actions", copy.Actions.Count)
                        .Add("unmapped", copy.Actions.Sum(action => action.Unmapped.Count))
                        .Add("unmappedTags", copy.Actions.Sum(action => action.MergeTags.Count)))],
            cancellationToken);
        if (saved is null)
        {
            return null;
        }

        var info = await infos.InfoAsync(saved.Form, cancellationToken);
        await changes.PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, saved.Version.Info.Number, isNew: false, request.UserKey) { ActionsCopiedFrom = source.Key }, FormAudit.Subject(info.Key));
        return new FormActionsCopy(new FormVersionResult(info, saved.Version.Info.Number), copy.Actions);
    }

    /// <inheritdoc />
    public async Task<FormInfo?> CreateFromTemplateAsync(TemplateFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        var requestedName = request.Name is null ? null : FormChecks.Name(request.Name);
        FormChecks.Alias(request.Alias);
        if (await TemplateAsync(request.TemplateKey, cancellationToken) is not { } template)
        {
            return null;
        }

        var name = requestedName ?? FormChecks.Name(template.Name);
        var folder = await LibraryFolders.OutsideTrashAsync(folders, request.FolderKey, cancellationToken);
        var definition = await creation.WithNewFormDefaultsAsync(DefinitionKeys.Renew(template.Definition, serializer), cancellationToken);
        documents.CheckSaveable(definition);
        var origin = new FormOrigin(
            FormSource.Template,
            request.TemplateKey,
            CreatedFromTemplateAction,
            details => details.Add("template", request.TemplateKey).Add("builtIn", template.BuiltIn));
        return await creation.CreateAsync(definition, name, request.Alias, FormAliases.FromName(name), folder, request.UserKey, origin, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormImport> ImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        var requestedName = request.Name is null ? null : FormChecks.Name(request.Name);
        FormChecks.Alias(request.Alias);
        var (envelope, renewed, siteNotes) = await ReadImportAsync(request.Json, !request.KeepKeys, cancellationToken);
        var name = requestedName ?? FormChecks.Name(envelope.Name, FileNamePath);
        var wanted = FormAliases.IsValid(envelope.Alias) ? envelope.Alias : FormAliases.FromName(name);
        var folder = await LibraryFolders.OutsideTrashAsync(folders, request.FolderKey, cancellationToken);

        // The lists the import creates are written with the form (ADR-0078).
        for (var attempt = 1; ; attempt++)
        {
            var notes = new List<FormImportNote>(siteNotes);
            var lists = await importLists.PlanAsync(renewed, envelope.OptionLists, request.UserKey, notes, cancellationToken);
            documents.CheckSaveable(lists.Definition);
            var codes = NoteCodes(notes);
            var origin = new FormOrigin(
                FormSource.Import,
                envelope.FormKey,
                ImportedAction,
                details => details
                    .Add("source", envelope.FormKey)
                    .AddIdentifier("sourceAlias", AuditDetails.IsIdentifier(envelope.Alias) ? envelope.Alias : null)
                    .AddIdentifier("packageVersion", AuditDetails.IsIdentifier(envelope.PackageVersion) ? envelope.PackageVersion : null)
                    .AddIdentifiers("notes", codes.Select(code => code.Code))
                    .AddNumbers("noteCounts", codes.Select(code => (long)code.Count)),
                lists.Created.Count == 0 ? null : token => importLists.CreateAsync(lists.Created, token));
            try
            {
                var form = await creation.CreateAsync(lists.Definition, name, request.Alias, wanted, folder, request.UserKey, origin, cancellationToken);
                importLists.Committed(lists.Created);
                return new FormImport(form, notes);
            }
            catch (FormImportListTakenException) when (attempt < FormCreation.AliasAttempts)
            {
                // Another change took a list's key or alias at the same moment, and the form rolled back: the lists again,
                // as the site now has them, announced again.
            }
            catch (FormImportListTakenException taken)
            {
                throw new FormBuilderConflictException(taken.Conflict.Conflict, taken.Conflict.Message, taken);
            }
        }
    }

    /// <inheritdoc />
    public async Task<FormImportCheck> CheckImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (envelope, renewed, notes) = await ReadImportAsync(request.Json, true, cancellationToken);
        FormChecks.Name(envelope.Name, FileNamePath);
        var lists = await importLists.PlanAsync(renewed, envelope.OptionLists, request.UserKey, notes, cancellationToken);
        documents.CheckSaveable(lists.Definition);
        return new FormImportCheck(envelope.Name, envelope.Alias, envelope.PackageVersion, envelope.ExportedUtc, DefinitionCounts.Of(lists.Definition), notes);
    }

    /// <inheritdoc />
    public async Task<FormReplace?> ReplaceAsync(ReplaceFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(FormChecks.OwnUnit);
        if (await forms.GetAsync(request.Key, cancellationToken) is not { } target)
        {
            return null;
        }

        FormChecks.OutsideTrash(target);
        FormChecks.Current(target, request.ExpectedConcurrencyVersion);
        var (envelope, read, siteNotes) = await ReadImportAsync(request.Json, false, cancellationToken);
        var keyed = read with { Key = target.Key };
        var draft = await documents.DraftOfAsync(target, cancellationToken);
        for (var attempt = 1; ; attempt++)
        {
            var notes = new List<FormImportNote>(siteNotes);
            var lists = await importLists.PlanAsync(keyed, envelope.OptionLists, request.UserKey, notes, cancellationToken);
            var definition = lists.Definition;
            documents.CheckSaveable(definition);
            var document = documents.Document(definition);
            if (draft is { Info.SchemaVersion: FormDefinition.CurrentSchemaVersion } && draft.Info.DefinitionHash == document.DefinitionHash)
            {
                return new FormReplace(new FormVersionResult(await infos.InfoAsync(target, cancellationToken), null), notes);
            }

            // A locked setting keeps the lock's value, as in every save (ADR-0060).
            var held = draft is null ? null : serializer.ReadStored(draft.Definition).Definition;
            locks.CheckSave(definition, held);
            actionLocks.CheckSave(definition, held);
            await changes.CancelableAsync(new FormSavingNotification(definition, target.Alias, target.Name, isNew: false, request.UserKey, new EventMessages()));
            var codes = NoteCodes(notes);
            try
            {
                var saved = await changes.WriteAsync(
                    async () =>
                    {
                        var version = await forms.SaveDraftAsync(target.Id, request.ExpectedConcurrencyVersion, document, null, request.UserKey, cancellationToken);
                        if (version is not null && lists.Created.Count > 0)
                        {
                            await importLists.CreateAsync(lists.Created, cancellationToken);
                        }

                        return version;
                    },
                    stored => stored is null
                        ? []
                        : [FormAudit.Entry(
                            ReplacedAction,
                            stored.Form,
                            request.UserKey,
                            new AuditDetails()
                                .Add("version", stored.Version.Info.Number)
                                .Add("source", envelope.FormKey)
                                .AddIdentifier("sourceAlias", AuditDetails.IsIdentifier(envelope.Alias) ? envelope.Alias : null)
                                .AddIdentifier("packageVersion", AuditDetails.IsIdentifier(envelope.PackageVersion) ? envelope.PackageVersion : null)
                                .AddIdentifiers("notes", codes.Select(code => code.Code))
                                .AddNumbers("noteCounts", codes.Select(code => (long)code.Count)))],
                    cancellationToken);
                if (saved is null)
                {
                    return null;
                }

                importLists.Committed(lists.Created);
                var info = await infos.InfoAsync(saved.Form, cancellationToken);
                await changes.PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, saved.Version.Info.Number, isNew: false, request.UserKey), FormAudit.Subject(info.Key));
                return new FormReplace(new FormVersionResult(info, saved.Version.Info.Number), notes);
            }
            catch (FormImportListTakenException) when (attempt < FormCreation.AliasAttempts)
            {
                // Another change took a list's key or alias at the same moment, and the save rolled back: the lists again.
            }
            catch (FormImportListTakenException taken)
            {
                throw new FormBuilderConflictException(taken.Conflict.Conflict, taken.Conflict.Message, taken);
            }
        }
    }

    /// <inheritdoc />
    public async Task<FormImportCheck> CheckReplaceAsync(ImportFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (envelope, read, notes) = await ReadImportAsync(request.Json, false, cancellationToken);
        var lists = await importLists.PlanAsync(read, envelope.OptionLists, request.UserKey, notes, cancellationToken);
        documents.CheckSaveable(lists.Definition);
        return new FormImportCheck(envelope.Name, envelope.Alias, envelope.PackageVersion, envelope.ExportedUtc, DefinitionCounts.Of(lists.Definition), notes);
    }

    /// <inheritdoc />
    public async Task<FormExport?> ExportAsync(Guid key, int? version = null, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        var stored = version is { } number ? await forms.GetVersionAsync(form.Id, number, cancellationToken) : await documents.DraftOfAsync(form, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var definition = documents.Readable(stored);
        var lists = new List<ExportedOptionList>();
        foreach (var listKey in DefinitionKeys.Fields(definition).Select(field => field.Options?.OptionList).OfType<Guid>().Distinct())
        {
            if (await optionLists.GetAsync(listKey, cancellationToken) is { } list && Exported(list) is { } exported)
            {
                lists.Add(exported);
            }
        }

        var envelope = new FormExportEnvelope(form.Key, form.Alias, form.Name, FormBuilderVersion.Package, time.GetUtcNow(), definition, lists);
        return new FormExport(form.Alias + ".json", FormExportFormat.Write(envelope));
    }

    // The codes of an import's notes, each once in the order it first came, with how many notes have it: an import can
    // note hundreds of option lists, more than an audit entry lists.
    private static List<(string Code, int Count)> NoteCodes(IEnumerable<FormImportNote> notes) =>
        [.. notes.GroupBy(note => note.Code, StringComparer.Ordinal).Select(group => (group.Key, group.Count()))];

    // The actions a copy takes, in the source's order: all of them, or those named; a key the source lacks is refused.
    private static List<FormAction> Chosen(FormDefinition source, IReadOnlyList<Guid>? keys)
    {
        if (keys is null)
        {
            return [.. source.Actions];
        }

        var known = source.Actions.Select(action => action.Key).ToHashSet();
        for (var i = 0; i < keys.Count; i++)
        {
            if (!known.Contains(keys[i]))
            {
                throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, JsonPaths.Index("actionKeys", i), "The source form has no action with this key.");
            }
        }

        var wanted = keys.ToHashSet();
        return source.Actions.Where(action => wanted.Contains(action.Key)).ToList();
    }

    // A form template by key: a built-in one first, then a saved one of kind Form; null when there is none.
    private async Task<(string Name, FormDefinition Definition, bool BuiltIn)?> TemplateAsync(Guid key, CancellationToken cancellationToken)
    {
        if (BuiltInFormTemplates.Find(key) is { } builtIn)
        {
            var read = FormExportFormat.Read(builtIn.Envelope, serializer);
            var envelope = read.Envelope ?? throw new FormBuilderValidationException(read.Problems);
            return (envelope.Name, envelope.Definition, true);
        }

        if (await templates.GetAsync(key, cancellationToken) is not { Kind: FormTemplateKind.Form } saved)
        {
            return null;
        }

        var stored = serializer.ReadStored(saved.Definition);
        return (saved.Name, stored.Definition ?? throw new FormBuilderValidationException(stored.Problems), false);
    }

    // The file read, always with new keys (12.5 adds the import into an existing form), and what this site lacks of it: the
    // theme, what its actions need and the pages its outcomes send visitors to; the option lists come with the plan,
    // which an import makes again when it retries.
    // The file's definition as this site can take it, with new keys for a copy, or the file's own for a replace or the mirror.
    private async Task<(FormExportEnvelope Envelope, FormDefinition Definition, List<FormImportNote> Notes)> ReadImportAsync(string json, bool renew, CancellationToken cancellationToken)
    {
        var read = FormExportFormat.Read(json, serializer);
        var envelope = read.Envelope ?? throw new FormBuilderValidationException(read.Problems);
        var notes = new List<FormImportNote>();
        var keyed = renew ? DefinitionKeys.Renew(envelope.Definition, serializer) : envelope.Definition;
        var renewed = WithActionsThisSiteCanRun(await WithThemeAsync(keyed, notes, cancellationToken), notes);
        PagesThisSiteLacks(renewed, notes);
        return (envelope, renewed, notes);
    }

    // An outcome that sends visitors to a page this site lacks, or holds in its recycle bin, keeps the page's key until the
    // editor chooses another; the note says so (ADR-0166).
    private void PagesThisSiteLacks(FormDefinition definition, List<FormImportNote> notes)
    {
        var outcomes = JsonPaths.Property(FilePath, "outcomes");
        for (var i = 0; i < definition.Outcomes.Count; i++)
        {
            if (definition.Outcomes[i].Redirect?.Content is { } page && !navigation.TryGetParentKey(page, out _))
            {
                notes.Add(new FormImportNote(FormImportNotes.PageMissing, JsonPaths.Index(outcomes, i), null));
            }
        }
    }

    // The definition with an action that this site cannot run switched off, each with a note (ADR-0046, ADR-0158): one whose
    // type the site lacks, and one whose type says that the site lacks configuration it needs.
    private FormDefinition WithActionsThisSiteCanRun(FormDefinition definition, List<FormImportNote> notes)
    {
        var actions = JsonPaths.Property(FilePath, "actions");
        var changed = definition.Actions.ToArray();
        var any = false;
        for (var i = 0; i < changed.Length; i++)
        {
            var action = changed[i];
            var path = JsonPaths.Index(actions, i);
            var lacking = actionTypes.Find(action.Type) is { } type
                ? MissingConfiguration(type, action).Select(key => new FormImportNote(FormImportNotes.ActionConfigurationMissing, path, key)).ToList()
                : [new FormImportNote(FormImportNotes.ActionTypeMissing, path, action.Type)];
            if (lacking.Count > 0)
            {
                notes.AddRange(lacking);
                changed[i] = action with { Enabled = false };
                any = true;
            }
        }

        return any ? definition with { Actions = changed } : definition;
    }

    // What the site lacks for an action. A file's settings were never held to the type's schema, and those that do not fit are
    // the publish check's to refuse.
    private static IReadOnlyList<string> MissingConfiguration(IFormBuilderActionType type, FormAction action)
    {
        try
        {
            return type.MissingConfiguration(action);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // A theme the site lacks, neither in code nor in the library, gives way to the default theme (IA 4, ADR-0046).
    private async Task<FormDefinition> WithThemeAsync(FormDefinition definition, List<FormImportNote> notes, CancellationToken cancellationToken)
    {
        if (definition.Settings.Appearance.Theme is not { } theme || await themes.HasAsync(theme, cancellationToken))
        {
            return definition;
        }

        var path = JsonPaths.Property(JsonPaths.Property(JsonPaths.Property(FilePath, "settings"), "appearance"), "theme");
        notes.Add(new FormImportNote(FormImportNotes.ThemeMissing, path, theme));
        return definition with { Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { Theme = null } } };
    }

    // A stored list as an export carries it, or null when its stored JSON does not read or its kind is one this version
    // does not know (ADR-0081): it is left out, and an import then says that the list is missing.
    private ExportedOptionList? Exported(StoredOptionList list)
    {
        if (list.Kind is { } kind && FormExportFormat.ListJson(list.Config, JsonValueKind.Object) is { } config && FormExportFormat.ListJson(list.Items, JsonValueKind.Array) is { } items)
        {
            return new ExportedOptionList(list.Key, list.Alias, list.Name, EnumNames<OptionListKind>.ByValue[kind], config, items, list.CacheSeconds);
        }

        LogListLeftOut(logger, list.Key);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The option list {ListKey} was left out of an export because its stored configuration or items are not JSON of their kind")]
    private static partial void LogListLeftOut(ILogger logger, Guid listKey);
}
