using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

// Duplicates, copied actions, templates, imports and exports (ADR-0046).
internal sealed partial class FormBuilderFormService
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
        EnsureOwnUnit();
        var name = CheckName(request.Name);
        CheckAlias(request.Alias);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckOutsideTrash(form);
        if (await DraftOfAsync(form, cancellationToken) is not { } draft)
        {
            return null;
        }

        // The copy is the draft the designer shows, with new keys that its references follow; access stays with the
        // source, so the copy takes its folder's, as a new form does.
        var definition = Readable(draft);
        var copy = DefinitionKeys.Renew(request.IncludeActions ? definition : definition with { Actions = [] }, serializer);
        var folder = await LiveFolderAsync(request.FolderKey, cancellationToken);
        CheckSaveable(copy);
        var creation = new Creation(
            FormSource.Duplicate,
            form.Key,
            DuplicatedAction,
            details => details.Add("source", form.Key).Add("sourceVersion", draft.Info.Number).Add("actions", request.IncludeActions));
        return await CreateFormAsync(copy, name, request.Alias, FormAliases.FromName(name), folder, request.UserKey, creation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormActionsCopy?> CopyActionsAsync(CopyActionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOwnUnit();
        if (await forms.GetAsync(request.SourceKey, cancellationToken) is not { } source
            || await forms.GetAsync(request.TargetKey, cancellationToken) is not { } target)
        {
            return null;
        }

        CheckOutsideTrash(source);
        CheckCurrent(target, request.ExpectedConcurrencyVersion);
        if (await DraftOfAsync(source, cancellationToken) is not { } sourceDraft || await DraftOfAsync(target, cancellationToken) is not { } targetDraft)
        {
            return null;
        }

        var actions = Chosen(Readable(sourceDraft), request.ActionKeys);
        if (actions.Count == 0)
        {
            return new FormActionsCopy(new FormVersionResult(await InfoAsync(target, cancellationToken), null), []);
        }

        var copy = ActionCopies.Copy(Readable(sourceDraft), actions, Readable(targetDraft), serializer);
        var definition = copy.Target ?? throw new FormBuilderValidationException(copy.Problems);
        CheckSaveable(definition);
        var document = Document(definition);
        await CancelableAsync(new FormSavingNotification(definition, target.Alias, target.Name, isNew: false, request.UserKey, new EventMessages()) { ActionsCopiedFrom = source.Key });
        var saved = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.SaveDraftAsync(target.Id, request.ExpectedConcurrencyVersion, document, null, request.UserKey, cancellationToken);
                if (stored is not null)
                {
                    var details = new AuditDetails()
                        .Add("version", stored.Version.Info.Number)
                        .Add("source", source.Key)
                        .Add("actions", copy.Actions.Count)
                        .Add("unmapped", copy.Actions.Sum(action => action.Unmapped.Count));
                    await audit.AddAsync(Entry(ActionsCopiedAction, stored.Form, request.UserKey, details), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (saved is null)
        {
            return null;
        }

        var info = await InfoAsync(saved.Form, cancellationToken);
        await PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, saved.Version.Info.Number, isNew: false, request.UserKey) { ActionsCopiedFrom = source.Key });
        return new FormActionsCopy(new FormVersionResult(info, saved.Version.Info.Number), copy.Actions);
    }

    /// <inheritdoc />
    public async Task<FormInfo?> CreateFromTemplateAsync(TemplateFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOwnUnit();
        var requestedName = request.Name is null ? null : CheckName(request.Name);
        CheckAlias(request.Alias);
        if (await TemplateAsync(request.TemplateKey, cancellationToken) is not { } template)
        {
            return null;
        }

        var name = requestedName ?? CheckName(template.Name);
        var folder = await LiveFolderAsync(request.FolderKey, cancellationToken);
        var definition = await WithNewFormDefaultsAsync(DefinitionKeys.Renew(template.Definition, serializer), cancellationToken);
        CheckSaveable(definition);
        var creation = new Creation(
            FormSource.Template,
            request.TemplateKey,
            CreatedFromTemplateAction,
            details => details.Add("template", request.TemplateKey).Add("builtIn", template.BuiltIn));
        return await CreateFormAsync(definition, name, request.Alias, FormAliases.FromName(name), folder, request.UserKey, creation, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormImport> ImportAsync(ImportFormRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOwnUnit();
        var requestedName = request.Name is null ? null : CheckName(request.Name);
        CheckAlias(request.Alias);
        var read = FormExportFormat.Read(request.Json, serializer);
        var envelope = read.Envelope ?? throw new FormBuilderValidationException(read.Problems);
        var name = requestedName ?? CheckName(envelope.Name, JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "form"), "name"));
        var wanted = FormAliases.IsValid(envelope.Alias) ? envelope.Alias : FormAliases.FromName(name);
        var folder = await LiveFolderAsync(request.FolderKey, cancellationToken);

        // Always new keys (12.5 adds the import into an existing form); the theme and the option lists as this site has them.
        var notes = new List<FormImportNote>();
        var definition = DefinitionKeys.Renew(envelope.Definition, serializer);
        definition = await WithThemeAsync(definition, notes, cancellationToken);
        definition = await WithOptionListsAsync(definition, envelope, notes, cancellationToken);
        CheckSaveable(definition);
        var codes = NoteCodes(notes);
        var creation = new Creation(
            FormSource.Import,
            envelope.FormKey,
            ImportedAction,
            details => details
                .Add("source", envelope.FormKey)
                .AddIdentifier("sourceAlias", AuditDetails.IsIdentifier(envelope.Alias) ? envelope.Alias : null)
                .AddIdentifier("packageVersion", AuditDetails.IsIdentifier(envelope.PackageVersion) ? envelope.PackageVersion : null)
                .AddIdentifiers("notes", codes.Select(code => code.Code))
                .AddNumbers("noteCounts", codes.Select(code => (long)code.Count)));
        var form = await CreateFormAsync(definition, name, request.Alias, wanted, folder, request.UserKey, creation, cancellationToken);
        return new FormImport(form, notes);
    }

    // The codes of an import's notes, each once in the order it first came, with how many notes have it: an import can
    // note hundreds of option lists, more than an audit entry lists.
    private static List<(string Code, int Count)> NoteCodes(IEnumerable<FormImportNote> notes) =>
        [.. notes.GroupBy(note => note.Code, StringComparer.Ordinal).Select(group => (group.Key, group.Count()))];

    /// <inheritdoc />
    public async Task<FormExport?> ExportAsync(Guid key, int? version = null, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        var stored = version is { } number ? await forms.GetVersionAsync(form.Id, number, cancellationToken) : await DraftOfAsync(form, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var definition = Readable(stored);
        var lists = new List<ExportedOptionList>();
        foreach (var listKey in DefinitionKeys.Fields(definition).Select(field => field.Options?.List).OfType<Guid>().Distinct())
        {
            if (await optionLists.GetAsync(listKey, cancellationToken) is { } list && Exported(list) is { } exported)
            {
                lists.Add(exported);
            }
        }

        var envelope = new FormExportEnvelope(form.Key, form.Alias, form.Name, FormBuilderVersion.Package, database.Time.GetUtcNow(), definition, lists);
        return new FormExport(form.Alias + ".json", FormExportFormat.Write(envelope));
    }

    // Creates a form from a definition the save checks took, with the alias given or the first free one from wanted,
    // tried again when a create at the same moment takes it (ADR-0044); the notifications say where it came from.
    private async Task<FormInfo> CreateFormAsync(FormDefinition definition, string name, string? alias, string wanted, StoredFolder? folder, Guid? userKey, Creation creation, CancellationToken cancellationToken)
    {
        var document = Document(definition);
        for (var attempt = 1; ; attempt++)
        {
            var chosen = alias ?? FormAliases.FirstFree(wanted, await forms.ListAliasesAsync(FormAliases.Stem(wanted), cancellationToken));
            await CancelableAsync(new FormSavingNotification(definition, chosen, name, isNew: true, userKey, new EventMessages()) { CreatedFrom = creation.Source, SourceKey = creation.SourceKey });
            SavedVersion created;
            try
            {
                created = await database.WriteAsync(
                    async _ =>
                    {
                        var saved = await forms.CreateAsync(new NewForm(definition.Key, folder?.Id, chosen, name, document, UserKey: userKey), cancellationToken);
                        var details = new AuditDetails().AddIdentifier("alias", chosen).Add("version", saved.Version.Info.Number).Add("folder", folder?.Key);
                        await audit.AddAsync(Entry(creation.Action, saved.Form, userKey, creation.Details?.Invoke(details) ?? details), cancellationToken);
                        return saved;
                    },
                    cancellationToken);
            }
            catch (FormBuilderConflictException exception) when (exception.Conflict == FormBuilderConflict.DuplicateAlias && alias is null && attempt < AliasAttempts)
            {
                // A create at the same moment took the alias: the next free one, announced again.
                continue;
            }

            var info = Info(created.Form, folder?.Key);
            await PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, created.Version.Info.Number, isNew: true, userKey) { CreatedFrom = creation.Source, SourceKey = creation.SourceKey });
            return info;
        }
    }

    // The form's draft as stored, or null.
    private async Task<StoredFormVersion?> DraftOfAsync(StoredForm form, CancellationToken cancellationToken) =>
        form.DraftVersionId is { } draftId ? await forms.GetVersionByIdAsync(draftId, cancellationToken) : null;

    // What the service writes meets the site's limits (ADR-0042).
    private void CheckSaveable(FormDefinition definition)
    {
        var problems = serializer.CheckSave(definition);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }
    }

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
                if (fields[i].Options?.List is { } list && seen.Add(list))
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

    // Whether the site's list holds what the file's list holds: the same kind, configuration and items.
    private static bool Same(StoredOptionList site, ExportedOptionList list)
    {
        if (!string.Equals(site.Kind, list.Kind, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(site.Config), list.Config) && JsonNode.DeepEquals(JsonNode.Parse(site.Items), list.Items);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // A stored list as an export carries it, or null when its stored JSON does not read: it is left out, and an import
    // then says that the list is missing.
    private ExportedOptionList? Exported(StoredOptionList list)
    {
        try
        {
            if (JsonNode.Parse(list.Config) is JsonObject config && JsonNode.Parse(list.Items) is JsonArray items)
            {
                return new ExportedOptionList(list.Key, list.Alias, list.Name, list.Kind, config, items, list.CacheSeconds);
            }
        }
        catch (JsonException)
        {
            // Left out below.
        }

        LogListLeftOut(logger, list.Key);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The option list {ListKey} was left out of an export because its stored configuration or items are not JSON of their kind")]
    private static partial void LogListLeftOut(ILogger logger, Guid listKey);

    // How a new form came to be: what its notifications say, its audit action, and what the entry records besides the
    // alias, the version and the folder.
    private sealed record Creation(FormSource Source, Guid? SourceKey, string Action, Func<AuditDetails, AuditDetails>? Details = null);
}
