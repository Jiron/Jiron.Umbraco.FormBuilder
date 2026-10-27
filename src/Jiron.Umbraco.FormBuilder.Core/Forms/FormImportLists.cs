using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>What an import does with the option lists its definition names (ADR-0046, ADR-0078).</summary>
/// <param name="Definition">The definition, its fields moved to the site's lists that take the place of the file's.</param>
/// <param name="Created">The lists to create with the form, in the order the fields first name them.</param>
internal sealed record FormImportListPlan(FormDefinition Definition, IReadOnlyList<NewOptionList> Created);

/// <summary>
/// A list an import planned to create whose key or alias another change took between the plan and the write: the form's
/// unit rolls back, and the import plans its lists again (ADR-0078).
/// </summary>
/// <param name="conflict">The database's refusal.</param>
internal sealed class FormImportListTakenException(FormBuilderConflictException conflict) : Exception(conflict.Message, conflict)
{
    /// <summary>The database's refusal: the key or the alias taken.</summary>
    public FormBuilderConflictException Conflict { get; } = conflict;
}

/// <summary>
/// The option lists of an import (ADR-0046, ADR-0078): a list the site has by key stays; the site's list with the
/// file's alias and the same contents takes the place of one it lacks; a list the file carries is created with the form
/// for a user who may manage the library, when its kind's checks on this site take it; any other the fields keep, and
/// the import says so. It logs as the exchange, whose import it is.
/// </summary>
internal sealed partial class FormImportLists(
    IOptionListRepository optionLists,
    OptionListService lists,
    IFormBuilderAccessService access,
    IUserService users,
    FormDefinitionSerializer serializer,
    ILogger<FormBuilderFormExchangeService> logger)
{
    private static readonly string FilePath = JsonPaths.Property(JsonPaths.Root, "definition");

    /// <summary>
    /// The lists <paramref name="definition"/> names as this site has them, with a note for each list the site lacks by
    /// key, once per list at the first field that names it, in file order; and, for a user who may manage the library,
    /// the lists the file carries to create with the form.
    /// </summary>
    /// <param name="definition">The definition, with its new keys.</param>
    /// <param name="carried">The lists the file carries.</param>
    /// <param name="userKey">The user who imports, or null when Jiron Form Builder does, which creates no list.</param>
    /// <param name="notes">Where the notes go.</param>
    /// <param name="cancellationToken">Cancelled when the import is.</param>
    public async Task<FormImportListPlan> PlanAsync(FormDefinition definition, IReadOnlyList<ExportedOptionList> carried, Guid? userKey, List<FormImportNote> notes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(notes);
        var byKey = carried.ToDictionary(list => list.Key);
        var moved = new Dictionary<Guid, Guid>();
        var created = new List<NewOptionList>();
        bool? mayCreate = null;
        foreach (var (listKey, path) in References(definition))
        {
            if (await optionLists.GetAsync(listKey, cancellationToken) is not null)
            {
                continue;
            }

            var list = byKey.GetValueOrDefault(listKey);
            if (list is not null && await optionLists.GetByAliasAsync(list.Alias, cancellationToken) is { } same && Same(same, list))
            {
                moved[listKey] = same.Key;
                notes.Add(new FormImportNote(FormImportNotes.OptionListReused, path, same.Alias));
            }
            else if (list is not null && (mayCreate ??= await MayManageLibraryAsync(userKey, cancellationToken)) && await CreatableAsync(list, created, cancellationToken) is { } create)
            {
                created.Add(create);
                notes.Add(new FormImportNote(FormImportNotes.OptionListCreated, path, create.Alias));
            }
            else
            {
                notes.Add(new FormImportNote(FormImportNotes.OptionListMissing, path, list?.Alias));
            }
        }

        return new FormImportListPlan(moved.Count == 0 ? definition : DefinitionKeys.Remap(definition, moved, serializer), created);
    }

    /// <summary>
    /// Writes the lists a plan creates, in the unit of work that writes the form (ADR-0078). A list whose key or alias
    /// another change took since the plan is refused with <see cref="FormImportListTakenException"/>, which rolls the form
    /// back.
    /// </summary>
    public async Task CreateAsync(IReadOnlyList<NewOptionList> created, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(created);
        foreach (var list in created)
        {
            try
            {
                await optionLists.CreateAsync(list, cancellationToken);
            }
            catch (FormBuilderConflictException exception) when (exception.Conflict is FormBuilderConflict.DuplicateAlias or FormBuilderConflict.DuplicateKey)
            {
                throw new FormImportListTakenException(exception);
            }
        }
    }

    /// <summary>
    /// Forgets, on every server, what was kept of the lists a plan created, once the form's unit has committed: a page
    /// may have asked for one while the site lacked it.
    /// </summary>
    public void Committed(IReadOnlyList<NewOptionList> created)
    {
        ArgumentNullException.ThrowIfNull(created);
        foreach (var list in created)
        {
            lists.Changed(list.Key);
        }
    }

    // Each option list the definition names, once, with the path in the file of the first field that names it.
    private static List<(Guid List, string Path)> References(FormDefinition definition)
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
        string.Equals(EnumNames<OptionListKind>.ByValue[site.Kind], list.Kind, StringComparison.Ordinal) &&
        FormExportFormat.SameListJson(site.Config, list.Config, JsonValueKind.Object) &&
        FormExportFormat.SameListJson(site.Items, list.Items, JsonValueKind.Array);

    // Whether the user the request names may manage the library: a user Umbraco has, with the permission globally
    // (ADR-0048).
    private async Task<bool> MayManageLibraryAsync(Guid? userKey, CancellationToken cancellationToken) =>
        userKey is { } key
        && await users.GetAsync(key) is { } user
        && (await access.GetGlobalAsync(AccessUser.Of(user), cancellationToken)).HasFlag(FormBuilderPermissions.ManageLibrary);

    // A carried list as this site creates it, or null when its kind's checks on this site refuse it: the file's alias,
    // or one from its name when the file's is not valid, with the first free number when another list has it (those
    // this import creates included), as a form's alias takes one (ADR-0044).
    private async Task<NewOptionList?> CreatableAsync(ExportedOptionList list, List<NewOptionList> created, CancellationToken cancellationToken)
    {
        if (!EnumNames<OptionListKind>.ByName.TryGetValue(list.Kind, out var kind))
        {
            LogListRefused(logger, list.Key, DefinitionErrors.OptionsInvalid + " at $.kind");
            return null;
        }

        var wanted = FormAliases.IsValid(list.Alias) ? list.Alias : FormAliases.FromName(list.Name);
        var taken = await optionLists.ListAliasesAsync(FormAliases.Stem(wanted), cancellationToken);
        var alias = FormAliases.FirstFree(wanted, taken.Concat(created.Select(other => other.Alias)));
        var problems = await lists.CheckAsync(new OptionListInput(alias, list.Name, kind, list.Config, list.Items, list.CacheSeconds), cancellationToken);
        if (problems.Count > 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                var refusals = string.Join(", ", problems.Select(problem => problem.Code + " at " + problem.Path));
                LogListRefused(logger, list.Key, refusals);
            }

            return null;
        }

        return new NewOptionList(list.Key, alias, list.Name, kind, list.Config, list.Items, list.CacheSeconds);
    }

    // The codes and paths only: a configuration or an item may hold an address or a visitor-facing text.
    [LoggerMessage(Level = LogLevel.Information, Message = "An import did not create the option list {ListKey} its file carries: the list's kind's checks on this site refused it ({Problems})")]
    private static partial void LogListRefused(ILogger logger, Guid listKey, string problems);
}
