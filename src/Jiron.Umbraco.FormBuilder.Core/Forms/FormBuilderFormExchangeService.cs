using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Forms that come from other forms, templates and files, and the files forms go out as (ADR-0046, ADR-0053):
/// duplicates, copied actions, forms from templates, imports and exports. A copy or a new form takes the steps of every
/// change to a form (<see cref="IFormBuilderFormService"/>): a unit of work of its own, the cancelable notification, one
/// unit with the change and its audit entry, then the notification that it happened. A missing form gives null.
/// </summary>
internal interface IFormBuilderFormExchangeService
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
    /// and what has no match is kept and reported (ADR-0046). Nothing to copy writes nothing. Null when either form
    /// does not exist.
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
    /// Creates a form from an export file, always with new keys, and says what it changed or could not resolve
    /// (ADR-0046).
    /// </summary>
    /// <exception cref="FormBuilderValidationException">
    /// The name or the alias is not allowed, or the file is refused, with every problem and its path in the file.
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The alias is taken, or the folder is missing or in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FormImport> ImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default);

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
    IThemeRepository themes,
    IFormTemplateRepository templates,
    FormDocuments documents,
    FormInfos infos,
    FormCreation creation,
    FormDefinitionSerializer serializer,
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

    private static readonly string FilePath = JsonPaths.Property(JsonPaths.Root, "definition");

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

        var copy = ActionCopies.Copy(documents.Readable(sourceDraft), actions, documents.Readable(targetDraft), serializer);
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
                        .Add("unmapped", copy.Actions.Sum(action => action.Unmapped.Count)))],
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
        var read = FormExportFormat.Read(request.Json, serializer);
        var envelope = read.Envelope ?? throw new FormBuilderValidationException(read.Problems);
        var name = requestedName ?? FormChecks.Name(envelope.Name, JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "form"), "name"));
        var wanted = FormAliases.IsValid(envelope.Alias) ? envelope.Alias : FormAliases.FromName(name);
        var folder = await LibraryFolders.OutsideTrashAsync(folders, request.FolderKey, cancellationToken);

        // Always new keys (12.5 adds the import into an existing form); the theme and the option lists as this site has them.
        var notes = new List<FormImportNote>();
        var definition = DefinitionKeys.Renew(envelope.Definition, serializer);
        definition = await WithThemeAsync(definition, notes, cancellationToken);
        definition = await WithOptionListsAsync(definition, envelope, notes, cancellationToken);
        documents.CheckSaveable(definition);
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
                .AddNumbers("noteCounts", codes.Select(code => (long)code.Count)));
        var form = await creation.CreateAsync(definition, name, request.Alias, wanted, folder, request.UserKey, origin, cancellationToken);
        return new FormImport(form, notes);
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

    // A theme the site lacks gives way to the default theme (IA 4, ADR-0046).
    private async Task<FormDefinition> WithThemeAsync(FormDefinition definition, List<FormImportNote> notes, CancellationToken cancellationToken)
    {
        if (definition.Settings.Appearance.Theme is not { } theme || await themes.GetByAliasAsync(theme, cancellationToken) is not null)
        {
            return definition;
        }

        var path = JsonPaths.Property(JsonPaths.Property(JsonPaths.Property(FilePath, "settings"), "appearance"), "theme");
        notes.Add(new FormImportNote(FormImportNotes.ThemeMissing, path, theme));
        return definition with { Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { Theme = null } } };
    }

    // The option lists the definition names: a list the site has by key stays; the site's list with the file's alias
    // and the same options takes the place of one it lacks; any other the fields keep, and the import says so.
    private async Task<FormDefinition> WithOptionListsAsync(FormDefinition definition, FormExportEnvelope envelope, List<FormImportNote> notes, CancellationToken cancellationToken)
    {
        var carried = envelope.OptionLists.ToDictionary(list => list.Key);
        var moved = new Dictionary<Guid, Guid>();
        foreach (var (listKey, path) in ListReferences(definition))
        {
            if (await optionLists.GetAsync(listKey, cancellationToken) is not null)
            {
                continue;
            }

            var list = carried.GetValueOrDefault(listKey);
            if (list is not null && await optionLists.GetByAliasAsync(list.Alias, cancellationToken) is { } same && Same(same, list))
            {
                moved[listKey] = same.Key;
                notes.Add(new FormImportNote(FormImportNotes.OptionListReused, path, same.Alias));
            }
            else
            {
                notes.Add(new FormImportNote(FormImportNotes.OptionListMissing, path, list?.Alias));
            }
        }

        return moved.Count == 0 ? definition : DefinitionKeys.Remap(definition, moved, serializer);
    }

    // Each option list the definition names, once, with the path in the file of the first field that names it.
    private static List<(Guid List, string Path)> ListReferences(FormDefinition definition)
    {
        var found = new List<(Guid List, string Path)>();
        var seen = new HashSet<Guid>();
        void Visit(IReadOnlyList<FormField> fields, string path)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var at = JsonPaths.Index(JsonPaths.Property(path, "fields"), i);
                if (fields[i].Options?.OptionList is { } list && seen.Add(list))
                {
                    found.Add((list, JsonPaths.Property(JsonPaths.Property(at, "options"), "list")));
                }

                Visit(fields[i].Fields, at);
            }
        }

        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = JsonPaths.Index(JsonPaths.Property(FilePath, "pages"), p);
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                Visit(definition.Pages[p].Groups[g].Fields, JsonPaths.Index(JsonPaths.Property(page, "groups"), g));
            }
        }

        return found;
    }

    // Whether the site's list holds what the file's list holds: the same kind, configuration and items, in any order of
    // their properties. Both are read as an export reads them, so a list that an export carries is the same as the
    // site's list it came from, nulls and empty values included.
    private static bool Same(StoredOptionList site, ExportedOptionList list) =>
        string.Equals(site.Kind, list.Kind, StringComparison.Ordinal) &&
        FormExportFormat.SameListJson(site.Config, list.Config, JsonValueKind.Object) &&
        FormExportFormat.SameListJson(site.Items, list.Items, JsonValueKind.Array);

    // A stored list as an export carries it, or null when its stored JSON does not read: it is left out, and an import
    // then says that the list is missing.
    private ExportedOptionList? Exported(StoredOptionList list)
    {
        if (FormExportFormat.ListJson(list.Config, JsonValueKind.Object) is { } config && FormExportFormat.ListJson(list.Items, JsonValueKind.Array) is { } items)
        {
            return new ExportedOptionList(list.Key, list.Alias, list.Name, list.Kind, config, items, list.CacheSeconds);
        }

        LogListLeftOut(logger, list.Key);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The option list {ListKey} was left out of an export because its stored configuration or items are not JSON of their kind")]
    private static partial void LogListLeftOut(ILogger logger, Guid listKey);
}
