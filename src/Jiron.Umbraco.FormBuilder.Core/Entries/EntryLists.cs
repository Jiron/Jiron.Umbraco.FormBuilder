using System.Buffers.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.Logging;
using FieldStorageKind = Jiron.Umbraco.FormBuilder.Core.FieldTypes.FieldStorageKind;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>How a field reads in the entries list (ADR-0207), which decides the tests its filter makes.</summary>
internal enum EntryListFieldKind
{
    /// <summary>Text, as typed or chosen.</summary>
    Text,

    /// <summary>A long answer.</summary>
    LongText,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A date, or a date with a time.</summary>
    Date,

    /// <summary>Checked or not.</summary>
    Checkbox,

    /// <summary>Uploaded files.</summary>
    File,

    /// <summary>A value of several parts, such as a name or an address.</summary>
    Parts,

    /// <summary>A repeatable group or a grid, whose parts hold its answers.</summary>
    Group,
}

/// <summary>A field of the form as the entries list offers it: every field a published version had that holds answers.</summary>
/// <param name="Key">The field's key.</param>
/// <param name="Label">Its label in the default culture, or its alias without one.</param>
/// <param name="Alias">Its alias.</param>
/// <param name="Type">Its field type's alias.</param>
/// <param name="Kind">How it reads.</param>
/// <param name="Parent">The repeatable group it is a child of, or none.</param>
/// <param name="Sensitive">Whether it, or its repeatable group, is sensitive: then it is no column, filter or sort.</param>
/// <param name="Removed">Whether the live version no longer has it; older entries keep its answers.</param>
internal sealed record EntryListField(Guid Key, string Label, string Alias, string Type, EntryListFieldKind Kind, Guid? Parent, bool Sensitive, bool Removed)
{
    /// <summary>Whether it can be a column: a field of a group that is not sensitive.</summary>
    public bool Column { get; init; }

    /// <summary>Whether the list can be sorted by it.</summary>
    public bool Sorts { get; init; }

    /// <summary>The tests its filter makes; none for a field that is no filter.</summary>
    public IReadOnlyList<EntryFieldTest> Tests { get; init; } = [];

    /// <summary>
    /// The field's own options in the version the site serves, which a filter offers to tick; none for a field whose
    /// options come from a list or a source, or hang on another field's answer, whose values it types.
    /// </summary>
    public IReadOnlyList<EntryListOption> Options { get; init; } = [];
}

/// <summary>One of a choice field's own options, as a filter offers it to tick.</summary>
/// <param name="Value">What entries store.</param>
/// <param name="Label">Its label in the default culture, or its value without one.</param>
internal sealed record EntryListOption(string Value, string Label);

/// <summary>A column's cell: the answer's texts as the summary page lists them, one for each row of a repeatable group.</summary>
/// <param name="Field">The column's field.</param>
/// <param name="Texts">The texts; none when it is unanswered, hidden by the rules or masked.</param>
/// <param name="Masked">Whether a sensitive answer is there, which the entry shows.</param>
internal sealed record EntryListCell(Guid Field, IReadOnlyList<string> Texts, bool Masked);

/// <summary>An entry as the list shows it (ADR-0207).</summary>
/// <param name="Key">The entry.</param>
/// <param name="Number">Its number within the form.</param>
/// <param name="Title">Its title, or none, and its number names it.</param>
/// <param name="State">Its state.</param>
/// <param name="Received">When it was received.</param>
/// <param name="Unseen">Whether no backoffice user has opened it.</param>
/// <param name="Actions">Its action runs, counted.</param>
/// <param name="Cells">The columns' cells, in the columns' order.</param>
/// <param name="Form">Its form.</param>
internal sealed record EntryListRow(Guid Key, int Number, string? Title, EntryState State, DateTimeOffset Received, bool Unseen, ActionRunTally Actions, IReadOnlyList<EntryListCell> Cells, Guid Form);

/// <summary>A page of a list.</summary>
/// <param name="Rows">The page's entries.</param>
/// <param name="Next">Where the next page starts, or none on the last page.</param>
/// <param name="Count">How many entries match, when asked for.</param>
internal sealed record EntryListPage(IReadOnlyList<EntryListRow> Rows, string? Next, EntryCount? Count)
{
    /// <summary>
    /// In a list across forms, which names them: the forms the rows belong to, in the order they first appear, then the
    /// forms its filter chose that no row of the page holds.
    /// </summary>
    public IReadOnlyList<EntryListForm> Forms { get; init; } = [];
}

/// <summary>A form a page of a list across forms names.</summary>
/// <param name="Key">The form.</param>
/// <param name="Name">Its name.</param>
internal sealed record EntryListForm(Guid Key, string Name);

/// <summary>Where a list across forms looks (ADR-0209).</summary>
internal enum EntryListPlace
{
    /// <summary>The Inbox: every state but drafts and Spam, the entries nobody opened yet first.</summary>
    Inbox,

    /// <summary>Spam, newest first.</summary>
    Spam,
}

/// <summary>What a list asks for: which entries, the columns to show, and the page.</summary>
/// <param name="Filter">Which entries, in which order.</param>
internal sealed record EntryListRequest(EntryListFilter Filter)
{
    /// <summary>The fields to show as columns, at most <see cref="EntryUserSettings.MaxColumns"/>.</summary>
    public IReadOnlyList<Guid> Columns { get; init; } = [];

    /// <summary>Where the page starts: the <see cref="EntryListPage.Next"/> of the page before, or none for the first.</summary>
    public string? After { get; init; }

    /// <summary>Whether to count the matches too.</summary>
    public bool Count { get; init; }
}

/// <summary>What a form's Entries view opens with: the fields it offers, the user's columns and filter sets.</summary>
/// <param name="Fields">The fields, the live version's in their order, then the removed ones.</param>
/// <param name="Columns">The columns the user chose, those the form still offers.</param>
/// <param name="FilterSets">The user's filter sets of this form and of every form.</param>
internal sealed record EntryListView(IReadOnlyList<EntryListField> Fields, IReadOnlyList<Guid> Columns, IReadOnlyList<EntryFilterSet> FilterSets);

/// <summary>
/// The entries lists (ADR-0207): one service turns a list's request into a query and a page of rows, for a form's
/// Entries view and, later, the Inbox and Spam. It always names its states, so no draft, test entry or Spam entry
/// reaches a list unless it asks for Spam; it checks every field filter, sort and column against the form's fields,
/// and refuses one on a sensitive field. The caller checks that the user may view the form's entries.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries, for the unseen count.</param>
/// <param name="queries">The entries query.</param>
/// <param name="runs">The action runs.</param>
/// <param name="scopes">Reads a page's scopes.</param>
/// <param name="titles">Writes entry titles.</param>
/// <param name="answers">The answers as texts.</param>
/// <param name="documents">Reads stored versions.</param>
/// <param name="timeZones">The site's time zone.</param>
/// <param name="settings">The user's choices.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Where a row that cannot be read goes.</param>
internal sealed partial class EntryLists(
    IFormRepository forms,
    IEntryRepository entries,
    IEntryQueryRepository queries,
    IActionRunRepository runs,
    SubmissionEntries scopes,
    EntryTitles titles,
    MergeTagAnswerTexts answers,
    FormDocuments documents,
    SiteTimeZones timeZones,
    EntryUserSettings settings,
    TimeProvider time,
    ILogger<EntryLists> logger)
{
    /// <summary>The entries a page holds.</summary>
    public const int PageSize = 50;

    /// <summary>Where a request holds its filter, which the paths of a filter's problems start with.</summary>
    public const string FilterPath = "filter.";

    /// <summary>The longest value a text filter compares with.</summary>
    public const int MaxFilterValueLength = 1000;

    /// <summary>The most forms the Inbox or Spam is filtered to.</summary>
    public const int MaxForms = 100;

    /// <summary>The states a list holds when it names none: every state but drafts and Spam.</summary>
    public static readonly IReadOnlyList<EntryState> ListedStates = [EntryState.Unverified, EntryState.Submitted, EntryState.Approved, EntryState.Rejected];

    /// <summary>
    /// The states an export holds when it names none: a list's, but for the entries still waiting for their email
    /// confirmation, whose addresses nobody has confirmed, so that a file taken for a mailing holds only confirmed ones
    /// (ADR-0226). Choosing the state exports them.
    /// </summary>
    public static readonly IReadOnlyList<EntryState> ExportedStates = [EntryState.Submitted, EntryState.Approved, EntryState.Rejected];

    // The earliest and latest day a range may name: the database stores times in the years 1753 to 9999.
    private static readonly DateOnly FirstDay = new(1753, 1, 2);
    private static readonly DateOnly LastDay = new(9999, 12, 30);

    /// <summary>What the form's Entries view opens with, or null when the form does not exist.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="formKey">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<EntryListView?> ViewAsync(Guid userKey, Guid formKey, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var options = await OptionsAsync(form, cancellationToken);
        var fields = Fields(await forms.ListFieldsAsync(form.Id, cancellationToken))
            .Select(field => options.TryGetValue(field.Key, out var own) && field.Tests.Count > 0 ? field with { Options = own } : field)
            .ToList();
        var columns = fields.Where(field => field.Column).Select(field => field.Key).ToHashSet();
        var chosen = (await settings.GetColumnsAsync(userKey, formKey, cancellationToken)).Where(columns.Contains).ToList();
        var sets = (await settings.GetFilterSetsAsync(userKey, cancellationToken)).Where(set => set.Form is null || set.Form == formKey).ToList();
        return new EntryListView(fields, chosen, sets);
    }

    /// <summary>A page of the form's entries, or null when the form does not exist.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="request">Which entries, the columns and the page.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A filter, sort, column or cursor the form cannot answer, each by its path.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget.</exception>
    public async Task<EntryListPage?> ListAsync(Guid formKey, EntryListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
        var fields = Fields(catalogue).ToDictionary(field => field.Key);
        var ids = catalogue.ToDictionary(row => row.Values.FieldKey, row => row.Id);
        var problems = new List<DefinitionProblem>();
        var query = await QueryAsync(form, fields, ids, request.Filter, problems, cancellationToken);
        var columns = Columns(request.Columns, fields, problems);
        var after = request.After is null ? null : Decode(request.After, query.Sort, problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var page = await queries.ListAsync(query, after, PageSize, request.Count, cancellationToken);
        return new EntryListPage(await RowsAsync(form, page.Entries, columns, cancellationToken), page.Next is { } next ? Encode(next) : null, page.Count);
    }

    /// <summary>
    /// The query of an export of the form's entries (ADR-0210): the list's own, so an export holds what the list shows.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="filter">The list's filter.</param>
    /// <param name="problems">Where a filter's problems go, each by its path in the request.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    internal async Task<EntryQuery> ExportQueryAsync(StoredForm form, EntryListFilter filter, List<DefinitionProblem> problems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
        var fields = Fields(catalogue).ToDictionary(field => field.Key);
        var ids = catalogue.ToDictionary(row => row.Values.FieldKey, row => row.Id);
        var query = await QueryAsync(form, fields, ids, filter, problems, cancellationToken);
        return filter.States.Count == 0 ? query with { States = ExportedStates } : query;
    }

    /// <summary>
    /// A page of the entries of every form the user may view entries of (ADR-0209): the Inbox, unseen first unless the
    /// filter sorts, or Spam. A filter of one form offers that form's fields as filters, columns and sorts; of several,
    /// none. The caller passes the user's visibility with <c>viewEntries</c>.
    /// </summary>
    /// <param name="place">The Inbox or Spam.</param>
    /// <param name="visibility">The forms the user may view entries of.</param>
    /// <param name="request">Which entries, the columns and the page.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A form, filter, sort, column or cursor the list cannot answer, each by its path.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget.</exception>
    public async Task<EntryListPage> ListAcrossAsync(EntryListPlace place, FormVisibility visibility, EntryListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(request);
        if (visibility.IsNothing)
        {
            // A user who may view no form's entries has an empty list, whatever it asks for.
            return new EntryListPage([], null, request.Count ? new EntryCount(0, More: false) : null);
        }

        var filter = request.Filter;
        var problems = new List<DefinitionProblem>();
        var chosen = await ChosenFormsAsync(filter.Forms, visibility, problems, cancellationToken);
        var one = chosen.Count == 1 ? chosen[0] : null;
        var fields = new Dictionary<Guid, EntryListField>();
        var ids = new Dictionary<Guid, int>();
        if (one is not null)
        {
            var catalogue = await forms.ListFieldsAsync(one.Id, cancellationToken);
            fields = Fields(catalogue).ToDictionary(field => field.Key);
            ids = catalogue.ToDictionary(row => row.Values.FieldKey, row => row.Id);
        }

        var found = new List<DefinitionProblem>();
        var states = PlaceStates(place, filter.States, found);
        var query = await FilterQueryAsync(EntryQuery.Across(new EntryFormScope(visibility, [.. chosen.Select(form => form.Id)])), one, fields, ids, filter with { States = states }, found, cancellationToken);
        problems.AddRange(found.Select(problem => problem with { Path = FilterPath + problem.Path }));
        if (place == EntryListPlace.Inbox && filter.Sort is null)
        {
            query = query with { Sort = EntrySort.UnseenFirst };
        }

        var columns = Columns(request.Columns, fields, problems);
        var after = request.After is null ? null : Decode(request.After, query.Sort, problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var page = await queries.ListAsync(query, after, PageSize, request.Count, cancellationToken);
        return await AcrossAsync(page, one, columns, chosen, cancellationToken);
    }

    /// <summary>
    /// A page of a member's entries (ADR-0231): those that keep the member, of every form the user may view entries of,
    /// in any state but a draft, newest first, 50 at a time, with their count on the first page. The caller passes the
    /// user's visibility with <c>viewEntries</c>; a user who may view no form's entries gets none.
    /// </summary>
    /// <param name="visibility">The forms the user may view entries of.</param>
    /// <param name="member">The member's key.</param>
    /// <param name="after">The cursor of the page before, or null for the first.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A cursor the list cannot read, at <c>after</c>.</exception>
    public async Task<EntryListPage> ListOfMemberAsync(FormVisibility visibility, Guid member, string? after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        if (visibility.IsNothing)
        {
            return new EntryListPage([], null, new EntryCount(0, More: false));
        }

        var query = EntryQuery.Across(new EntryFormScope(visibility, [])) with
        {
            Member = member,
            States = [.. Enum.GetValues<EntryState>().Where(state => state != EntryState.Draft)],
            Sort = EntrySort.Newest,
        };
        var problems = new List<DefinitionProblem>();
        var cursor = after is null ? null : Decode(after, query.Sort, problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var page = await queries.ListAsync(query, cursor, PageSize, count: after is null, cancellationToken);
        return await AcrossAsync(page, one: null, columns: [], chosen: [], cancellationToken);
    }

    // The rows of a page of entries of several forms, each by its own form, and the forms they belong to: the groups keep the
    // order their forms first appear in, so the forms do too; then come the forms a filter chose that no row holds, so a chip
    // can name every form it filters by. A form deleted for good since the page was read takes its rows with it.
    private async Task<EntryListPage> AcrossAsync(EntryPage page, StoredForm? one, IReadOnlyList<Guid> columns, IReadOnlyList<StoredForm> chosen, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<Guid, EntryListRow>();
        var named = new List<EntryListForm>();
        foreach (var group in page.Entries.GroupBy(entry => entry.FormId))
        {
            var form = one?.Id == group.Key ? one : await forms.GetByIdAsync(group.Key, cancellationToken);
            if (form is null)
            {
                continue;
            }

            named.Add(new EntryListForm(form.Key, form.Name));
            foreach (var row in await RowsAsync(form, [.. group], columns, cancellationToken))
            {
                rows[row.Key] = row;
            }
        }

        named.AddRange(chosen.Where(form => named.TrueForAll(seen => seen.Key != form.Key)).Select(form => new EntryListForm(form.Key, form.Name)));
        return new EntryListPage([.. page.Entries.Where(entry => rows.ContainsKey(entry.Key)).Select(entry => rows[entry.Key])], page.Next is { } next ? Encode(next) : null, page.Count)
        {
            Forms = named,
        };
    }

    /// <summary>
    /// How many entries nobody opened yet the forms the user may view entries of hold, as the tree marks them: at most
    /// <paramref name="most"/>, and whether there are more (the Inbox's badge, ADR-0209).
    /// </summary>
    /// <param name="visibility">The forms the user may view entries of.</param>
    /// <param name="most">The most the badge says.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<EntryCount> CountUnseenAsync(FormVisibility visibility, int most, CancellationToken cancellationToken = default)
    {
        var counted = await entries.CountUnseenAsync(visibility, most, cancellationToken);
        return counted > most ? new EntryCount(most, More: true) : new EntryCount(counted, More: false);
    }

    /// <summary>The forms the user may view entries of whose name contains the text, by name, for the Form filter.</summary>
    /// <param name="visibility">The forms the user may view entries of.</param>
    /// <param name="search">What the name contains, or none.</param>
    /// <param name="take">How many at most.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<IReadOnlyList<EntryListForm>> ListFormsAsync(FormVisibility visibility, string? search, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        var page = await forms.ListAsync(
            new FormFilter { AllFolders = true, Visibility = visibility, Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() },
            PageRequest.First(take),
            cancellationToken);
        return [.. page.Items.Select(form => new EntryListForm(form.Key, form.Name))];
    }

    // The forms a filter names, each one the user may view entries of; one it may not, or that is gone, is a problem.
    private async Task<IReadOnlyList<StoredForm>> ChosenFormsAsync(IReadOnlyList<Guid> keys, FormVisibility visibility, List<DefinitionProblem> problems, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        if (keys.Count > MaxForms)
        {
            problems.Add(new DefinitionProblem("too_many_forms", FilterPath + "forms", $"A list is filtered to at most {MaxForms} forms."));
            return [];
        }

        var distinct = keys.Distinct().ToList();
        var visible = (await forms.ListAsync(new FormFilter { AllFolders = true, Visibility = visibility, Keys = distinct }, PageRequest.First(MaxForms), cancellationToken)).Items
            .ToDictionary(form => form.Key);
        for (var index = 0; index < keys.Count; index++)
        {
            if (!visible.ContainsKey(keys[index]))
            {
                problems.Add(new DefinitionProblem("form_not_found", $"{FilterPath}forms[{index}]", "The user may not view this form's entries, or it is gone."));
            }
        }

        return [.. distinct.Where(visible.ContainsKey).Select(key => visible[key])];
    }

    // The states of a place: the Inbox's, as a form's list has them, but never Spam; Spam's, Spam alone.
    private static IReadOnlyList<EntryState> PlaceStates(EntryListPlace place, IReadOnlyList<EntryState> states, List<DefinitionProblem> problems)
    {
        for (var index = 0; index < states.Count; index++)
        {
            if ((place == EntryListPlace.Spam) != (states[index] == EntryState.Spam))
            {
                problems.Add(new DefinitionProblem("invalid_state", $"states[{index}]", place == EntryListPlace.Spam ? "Spam holds the entries in Spam only." : "Spam has a list of its own."));
            }
        }

        return place == EntryListPlace.Spam ? [EntryState.Spam] : [.. states.Where(state => state != EntryState.Spam)];
    }

    /// <summary>Checks a form's columns as a list request checks them; false when the form does not exist.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="columns">The fields to show as columns.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A column the form does not show, or too many.</exception>
    public async Task<bool> CheckColumnsAsync(Guid formKey, IReadOnlyList<Guid> columns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return false;
        }

        var problems = new List<DefinitionProblem>();
        Columns(columns, Fields(await forms.ListFieldsAsync(form.Id, cancellationToken)).ToDictionary(field => field.Key), problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        return true;
    }

    /// <summary>
    /// Checks a filter against a form, as a list request is checked, so a filter set keeps only a filter its form can
    /// answer; a set of every form filters by no field. Null when the form does not exist.
    /// </summary>
    /// <param name="formKey">The form, or none for a set of every form.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">What the form cannot answer, each by its path.</exception>
    public async Task<bool> CheckAsync(Guid? formKey, EntryListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var problems = new List<DefinitionProblem>();
        if (formKey is { } key)
        {
            if (await forms.GetAsync(key, cancellationToken) is not { } form)
            {
                return false;
            }

            var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
            await QueryAsync(form, Fields(catalogue).ToDictionary(field => field.Key), catalogue.ToDictionary(row => row.Values.FieldKey, row => row.Id), filter, problems, cancellationToken);
        }
        else
        {
            var found = new List<DefinitionProblem>();
            if (filter.Fields.Count > 0)
            {
                found.Add(new DefinitionProblem("no_form", "fields", "A filter set of every form filters by no field."));
            }

            if (filter.Sort?.Field is not null)
            {
                found.Add(new DefinitionProblem("no_form", "sort.field", "A filter set of every form sorts by no field."));
            }

            CheckStates(filter, found);
            CheckSearch(filter.Search, found);
            if (filter.Received is { } received)
            {
                Received(received, TimeZoneInfo.Utc, found);
            }

            problems.AddRange(found.Select(problem => problem with { Path = FilterPath + problem.Path }));
        }

        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        return true;
    }

    /// <summary>The fields of a form's catalogue as the list offers them: those a published version had, a grid's rows aside.</summary>
    /// <param name="catalogue">The form's catalogue.</param>
    internal static IReadOnlyList<EntryListField> Fields(IReadOnlyList<CatalogueField> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var byKey = catalogue.GroupBy(row => row.Values.FieldKey).ToDictionary(group => group.Key, group => group.First().Values);
        var listed = new List<(EntryListField Field, bool Removed, int Order)>();
        foreach (var values in catalogue.Select(row => row.Values).Where(values => values.FirstVersionNumber is not null).DistinctBy(values => values.FieldKey))
        {
            var parent = values.ParentFieldKey is { } parentKey ? byKey.GetValueOrDefault(parentKey) : null;
            if (parent is not null && string.Equals(parent.Type, values.Type, StringComparison.Ordinal))
            {
                // A grid's row is a part of its grid's answer.
                continue;
            }

            var kind = values.StorageKind switch
            {
                FieldStorageKind.Text => EntryListFieldKind.Text,
                FieldStorageKind.LongText => EntryListFieldKind.LongText,
                FieldStorageKind.Number => EntryListFieldKind.Number,
                FieldStorageKind.Date => EntryListFieldKind.Date,
                FieldStorageKind.Bool => EntryListFieldKind.Checkbox,
                FieldStorageKind.File => EntryListFieldKind.File,
                FieldStorageKind.Json => EntryListFieldKind.Parts,
                _ => EntryListFieldKind.Group,
            };
            var sensitive = values.IsSensitive || parent?.IsSensitive == true;
            var removed = values.LastVersionNumber is not null;
            listed.Add((
                new EntryListField(values.FieldKey, values.Label.Length > 0 ? values.Label : values.Alias, values.Alias, values.Type, kind, parent?.FieldKey, sensitive, removed)
                {
                    Column = !sensitive && parent is null,
                    Sorts = !sensitive && kind is EntryListFieldKind.Text or EntryListFieldKind.Number or EntryListFieldKind.Date,
                    Tests = sensitive ? [] : TestsOf(kind),
                },
                removed,
                values.SortOrder));
        }

        return [.. listed.OrderBy(item => item.Removed).ThenBy(item => item.Order).Select(item => item.Field)];
    }

    /// <summary>The tests a field of a kind filters by.</summary>
    internal static IReadOnlyList<EntryFieldTest> TestsOf(EntryListFieldKind kind) => kind switch
    {
        EntryListFieldKind.Text => [EntryFieldTest.OneOf, EntryFieldTest.NoneOf, EntryFieldTest.StartsWith, EntryFieldTest.Answered, EntryFieldTest.NotAnswered],
        EntryListFieldKind.LongText => [EntryFieldTest.StartsWith, EntryFieldTest.Answered, EntryFieldTest.NotAnswered],
        EntryListFieldKind.Number or EntryListFieldKind.Date => [EntryFieldTest.Between, EntryFieldTest.Answered, EntryFieldTest.NotAnswered],
        EntryListFieldKind.Checkbox => [EntryFieldTest.Checked, EntryFieldTest.NotChecked],
        EntryListFieldKind.File or EntryListFieldKind.Parts => [EntryFieldTest.Answered, EntryFieldTest.NotAnswered],
        _ => [],
    };

    // The query a filter asks for, its problems added by their path in the request's filter.
    private async Task<EntryQuery> QueryAsync(
        StoredForm form,
        IReadOnlyDictionary<Guid, EntryListField> fields,
        IReadOnlyDictionary<Guid, int> ids,
        EntryListFilter filter,
        List<DefinitionProblem> problems,
        CancellationToken cancellationToken)
    {
        var found = new List<DefinitionProblem>();
        if (filter.Forms.Any(key => key != form.Key))
        {
            found.Add(new DefinitionProblem("form_not_allowed", "forms", "A form's list holds its own entries only."));
        }

        var query = await FilterQueryAsync(new EntryQuery(form.Id), form, fields, ids, filter, found, cancellationToken);
        problems.AddRange(found.Select(problem => problem with { Path = FilterPath + problem.Path }));
        return query;
    }

    // The query of a filter, for a form's list or a list across forms; a list of several forms filters by no field.
    private async Task<EntryQuery> FilterQueryAsync(
        EntryQuery scope,
        StoredForm? form,
        IReadOnlyDictionary<Guid, EntryListField> fields,
        IReadOnlyDictionary<Guid, int> ids,
        EntryListFilter filter,
        List<DefinitionProblem> problems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var states = CheckStates(filter, problems);
        var (from, before) = filter.Received is null ? (null, null) : Received(filter.Received, await timeZones.GetAsync(null, cancellationToken), problems);
        if (!Enum.IsDefined(filter.Actions))
        {
            problems.Add(new DefinitionProblem("invalid_value", "actions", "The action filter is failed, uncertain or delivered."));
        }

        var filters = new List<EntryFieldFilter>();
        if (filter.Fields.Count > EntryQuerySql.MaxFilters)
        {
            problems.Add(new DefinitionProblem("too_many_filters", "fields", $"A list filters by at most {EntryQuerySql.MaxFilters} fields."));
        }
        else
        {
            for (var index = 0; index < filter.Fields.Count; index++)
            {
                if (FieldFilter(filter.Fields[index], $"fields[{index}]", fields, ids, problems) is { } made)
                {
                    filters.Add(made);
                }
            }
        }

        var search = CheckSearch(filter.Search, problems);
        var numbers = search is null ? [] : EntryReferences.Numbers(form is null ? null : await ReferenceFormatAsync(form, cancellationToken), search);
        return scope with
        {
            States = states,
            Tests = EntryTestFilter.Exclude,
            SubmittedFrom = from,
            SubmittedBefore = before,
            Actions = Enum.IsDefined(filter.Actions) ? filter.Actions : EntryActionFilter.Any,
            Filters = filters,
            Search = search is not null && EntrySearchText.Normalize(search).Length > 0 ? search : null,
            SearchNumbers = [.. numbers.Take(EntryQuerySql.MaxSearchNumbers)],
            Sort = Sort(filter.Sort, fields, ids, problems),
        };
    }

    // The states a list holds: every one but drafts and Spam when it names none; a draft never.
    private static IReadOnlyList<EntryState> CheckStates(EntryListFilter filter, List<DefinitionProblem> problems)
    {
        if (filter.States.Count == 0)
        {
            return ListedStates;
        }

        for (var index = 0; index < filter.States.Count; index++)
        {
            if (filter.States[index] == EntryState.Draft || !Enum.IsDefined(filter.States[index]))
            {
                problems.Add(new DefinitionProblem("invalid_state", $"states[{index}]", "A list holds entries that were sent, never drafts."));
            }
        }

        return [.. filter.States.Distinct()];
    }

    // The search term, or none for a blank one.
    private static string? CheckSearch(string? search, List<DefinitionProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        if (EntrySearchText.Normalize(search).Length > EntrySearchText.MaxTermLength)
        {
            problems.Add(new DefinitionProblem("search_too_long", "search", $"A search has at most {EntrySearchText.MaxTermLength} characters."));
            return null;
        }

        return search.Trim();
    }

    // The first instant that matches and the first that no longer does, in UTC, of a preset or a range of days in the zone.
    private (DateTime? From, DateTime? Before) Received(EntryReceived received, TimeZoneInfo zone, List<DefinitionProblem> problems)
    {
        DateOnly? first;
        DateOnly? last;
        if (received.Preset is { } preset)
        {
            if (received.From is not null || received.To is not null || !Enum.IsDefined(preset))
            {
                problems.Add(new DefinitionProblem("invalid_value", "received", "Received is a preset or a range of days."));
                return (null, null);
            }

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
            (first, last) = preset switch
            {
                EntryReceivedPreset.Today => (today, today),
                EntryReceivedPreset.ThisWeek => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
                EntryReceivedPreset.Last7Days => (today.AddDays(-6), today),
                EntryReceivedPreset.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
                _ => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1), new DateOnly(today.Year, today.Month, 1).AddDays(-1)),
            };
        }
        else
        {
            (first, last) = (received.From, received.To);
            if (!Days(first, last, "received", problems))
            {
                return (null, null);
            }
        }

        return (first is { } day ? StartOf(day, zone) : null, last is { } end ? StartOf(end.AddDays(1), zone) : null);
    }

    // Whether a range of days has a bound, starts at or before its end, and lies where the database stores times.
    private static bool Days(DateOnly? first, DateOnly? last, string path, List<DefinitionProblem> problems)
    {
        if (first is null && last is null)
        {
            problems.Add(new DefinitionProblem("invalid_value", path, "A range of days has a first or a last day."));
            return false;
        }

        if (first < FirstDay || first > LastDay || last < FirstDay || last > LastDay)
        {
            problems.Add(new DefinitionProblem("invalid_value", path, "A range of days lies in the years 1753 to 9999."));
            return false;
        }

        if (first > last)
        {
            problems.Add(new DefinitionProblem("invalid_value", path, "A range of days starts at or before its last day."));
            return false;
        }

        return true;
    }

    /// <summary>Midnight of a day in a zone, in UTC; a midnight the clocks skip is the first hour that exists.</summary>
    /// <param name="day">The day.</param>
    /// <param name="zone">The zone it is a day of.</param>
    internal static DateTime StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    // A field filter, or null with its problem.
    private static EntryFieldFilter? FieldFilter(EntryFieldCondition condition, string path, IReadOnlyDictionary<Guid, EntryListField> fields, IReadOnlyDictionary<Guid, int> ids, List<DefinitionProblem> problems)
    {
        if (condition is null || !fields.TryGetValue(condition.Field, out var field) || field.Tests.Count == 0 || !ids.TryGetValue(field.Key, out var id))
        {
            problems.Add(new DefinitionProblem("field_not_filterable", path + ".field", "The form has no such field to filter by."));
            return null;
        }

        if (!field.Tests.Contains(condition.Test))
        {
            problems.Add(new DefinitionProblem("test_not_allowed", path + ".test", "This field is not filtered that way."));
            return null;
        }

        var texts = condition.Test is EntryFieldTest.OneOf or EntryFieldTest.NoneOf or EntryFieldTest.StartsWith;
        var range = condition.Test == EntryFieldTest.Between;
        if ((!texts && condition.Values.Count > 0)
            || (!(range && field.Kind == EntryListFieldKind.Number) && (condition.Min is not null || condition.Max is not null))
            || (!(range && field.Kind == EntryListFieldKind.Date) && (condition.From is not null || condition.To is not null)))
        {
            problems.Add(new DefinitionProblem("invalid_value", path, "The filter holds values its test does not take."));
            return null;
        }

        switch (condition.Test)
        {
            case EntryFieldTest.OneOf or EntryFieldTest.NoneOf:
                if (!Values(condition.Values, EntryQuerySql.MaxValuesPerFilter, path, problems))
                {
                    return null;
                }

                return new EntryTextFilter(id, condition.Test == EntryFieldTest.OneOf ? EntryTextMatch.OneOf : EntryTextMatch.NoneOf, condition.Values);
            case EntryFieldTest.StartsWith:
                if (!Values(condition.Values, 1, path, problems))
                {
                    return null;
                }

                if (EntryTextKey.Whole(condition.Values[0]).Length >= EntryTextKey.MaxLength - 1)
                {
                    problems.Add(new DefinitionProblem("invalid_value", path + ".values[0]", $"A start has at most {EntryTextKey.MaxLength - 2} characters."));
                    return null;
                }

                return new EntryTextFilter(id, EntryTextMatch.StartsWith, condition.Values);
            case EntryFieldTest.Between when field.Kind == EntryListFieldKind.Number:
                if (condition.Min is null && condition.Max is null)
                {
                    problems.Add(new DefinitionProblem("invalid_value", path, "A number range has a lowest or a highest number."));
                    return null;
                }

                if (condition.Min > condition.Max)
                {
                    problems.Add(new DefinitionProblem("invalid_value", path, "A number range starts at or below its highest number."));
                    return null;
                }

                return new EntryNumberFilter(id, condition.Min, true, condition.Max, true);
            case EntryFieldTest.Between:
                if (!Days(condition.From, condition.To, path, problems))
                {
                    return null;
                }

                // A date answer is stored as the visitor's wall clock: its day is the day it names, in no zone.
                return new EntryDateFilter(
                    id,
                    condition.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    condition.To?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            case EntryFieldTest.Checked or EntryFieldTest.NotChecked:
                return new EntryCheckedFilter(id, condition.Test == EntryFieldTest.Checked);
            default:
                return new EntryAnsweredFilter(id, condition.Test == EntryFieldTest.Answered);
        }
    }

    // Whether a text filter's values are 1 to the most, each with text and not too long.
    private static bool Values(IReadOnlyList<string> values, int most, string path, List<DefinitionProblem> problems)
    {
        if (values.Count == 0 || values.Count > most)
        {
            problems.Add(new DefinitionProblem("invalid_value", path + ".values", most == 1 ? "The filter compares with one value." : $"The filter compares with 1 to {most} values."));
            return false;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not { } value || EntryTextKey.Whole(value).Length == 0 || value.Length > MaxFilterValueLength)
            {
                problems.Add(new DefinitionProblem("invalid_value", $"{path}.values[{index}]", $"A value has 1 to {MaxFilterValueLength} characters."));
                return false;
            }
        }

        return true;
    }

    // The order: by when the entries were received, or by a field that sorts.
    private static EntrySort Sort(EntryListSort? sort, IReadOnlyDictionary<Guid, EntryListField> fields, IReadOnlyDictionary<Guid, int> ids, List<DefinitionProblem> problems)
    {
        sort ??= new EntryListSort();
        if (sort.Field is not { } key)
        {
            return sort.Descending ? EntrySort.Newest : EntrySort.Oldest;
        }

        if (!fields.TryGetValue(key, out var field) || !field.Sorts || !ids.TryGetValue(key, out var id))
        {
            problems.Add(new DefinitionProblem("field_not_sortable", "sort.field", "The list is not sorted by this field."));
            return EntrySort.Newest;
        }

        var column = field.Kind switch
        {
            EntryListFieldKind.Number => EntrySortColumn.Number,
            EntryListFieldKind.Date => EntrySortColumn.Date,
            _ => EntrySortColumn.Text,
        };
        return EntrySort.ByValue(id, column, sort.Descending);
    }

    // The columns, each a field the list shows as one.
    private static IReadOnlyList<Guid> Columns(IReadOnlyList<Guid> columns, Dictionary<Guid, EntryListField> fields, List<DefinitionProblem> problems)
    {
        if (columns.Count > EntryUserSettings.MaxColumns)
        {
            problems.Add(new DefinitionProblem("too_many_columns", "columns", $"A list shows at most {EntryUserSettings.MaxColumns} columns."));
            return [];
        }

        for (var index = 0; index < columns.Count; index++)
        {
            if (!fields.TryGetValue(columns[index], out var field) || !field.Column)
            {
                problems.Add(new DefinitionProblem("field_not_column", $"columns[{index}]", "The list does not show this field as a column."));
            }
        }

        return [.. columns.Distinct()];
    }

    // The own options of the choice fields of the version the site serves, by field; none before a first publish.
    private async Task<Dictionary<Guid, IReadOnlyList<EntryListOption>>> OptionsAsync(StoredForm form, CancellationToken cancellationToken)
    {
        var options = new Dictionary<Guid, IReadOnlyList<EntryListOption>>();
        if (form.PublishedVersionId is not { } versionId || await forms.GetVersionByIdAsync(versionId, cancellationToken) is not { } version)
        {
            return options;
        }

        foreach (var field in documents.Readable(version).Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
        {
            if (field.Options is { Source: OptionSource.Items, Parent: null, Items.Count: > 0 } own)
            {
                options.TryAdd(field.Key, [.. own.Items.Select(item => new EntryListOption(item.Value, item.Label?.Default is { Length: > 0 } label ? label : item.Value))]);
            }
        }

        return options;
    }

    // The reference format of the version the entries are shown by (FormDocuments.ServedOfAsync).
    private async Task<EntryReferenceFormat?> ReferenceFormatAsync(StoredForm form, CancellationToken cancellationToken) =>
        await documents.ServedOfAsync(form, cancellationToken) is { } version
            ? documents.Readable(version).Settings.Submission.EntryReference
            : null;

    // The rows of a page: each entry's title and cells from its scope, read for the page at once, and its runs counted.
    private async Task<IReadOnlyList<EntryListRow>> RowsAsync(StoredForm form, IReadOnlyList<StoredEntry> entries, IReadOnlyList<Guid> columns, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var tallies = await runs.TallyOfEntriesAsync([.. entries.Select(entry => entry.Id)], cancellationToken);
        var read = await scopes.ScopesAsync(form, entries, cancellationToken);
        var rows = new List<EntryListRow>(entries.Count);
        foreach (var entry in entries)
        {
            string? title = null;
            IReadOnlyList<EntryListCell> cells = [.. columns.Select(column => new EntryListCell(column, [], false))];
            if (read.TryGetValue(entry.Id, out var scope))
            {
                title = await titles.RenderAsync(scope, entry.Key, cancellationToken);
                if (columns.Count > 0)
                {
                    cells = await CellsAsync(entry, scope, columns, cancellationToken) ?? cells;
                }
            }

            rows.Add(new EntryListRow(entry.Key, entry.Number, title, entry.State, entry.SubmittedUtc, entry.FirstSeenUtc is null, tallies.GetValueOrDefault(entry.Id), cells, form.Key));
        }

        return rows;
    }

    // An entry's cells, its answers as the summary page lists them in its culture; null when they cannot be read.
    private async Task<IReadOnlyList<EntryListCell>?> CellsAsync(StoredEntry entry, MergeTagScope scope, IReadOnlyList<Guid> columns, CancellationToken cancellationToken)
    {
        try
        {
            var inVersion = scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
                .GroupBy(field => field.Key)
                .ToDictionary(group => group.Key, group => group.First());
            var shown = columns.Select(inVersion.GetValueOrDefault).OfType<FormField>().ToList();
            var texts = await answers.TextsAsync(scope, shown.SelectMany(field => field.Fields.Select(child => new AnswerField(child, field)).Prepend(new AnswerField(field, null))), cancellationToken);
            return [.. columns.Select(column =>
            {
                if (!inVersion.TryGetValue(column, out var field))
                {
                    return new EntryListCell(column, [], false);
                }

                var (masked, found) = answers.Summary(field, scope, texts);
                return new EntryListCell(column, found, masked);
            })];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not OperationCanceledException)
        {
            LogUnreadable(logger, exception, entry.Key);
            return null;
        }
    }

    // The cursor as an opaque text: its sort, its entry and time, and in a value sort's walk where it stood.
    private static string Encode(EntryCursor cursor) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new CursorText(
                (int)cursor.Sort.Column,
                cursor.Sort.Descending,
                cursor.Sort.FieldId,
                cursor.EntryId,
                cursor.SubmittedUtc.Ticks,
                cursor.Value?.ValueId,
                cursor.Value?.Text,
                cursor.Value?.Number,
                cursor.Value?.Date?.Ticks,
                cursor.Seen),
            CursorJson));

    // A cursor of the same sort, or null with its problem.
    private static EntryCursor? Decode(string text, EntrySort sort, List<DefinitionProblem> problems)
    {
        try
        {
            if (text.Length is > 0 and <= 2048 && JsonSerializer.Deserialize<CursorText>(Base64Url.DecodeFromChars(text), CursorJson) is { } read
                && Enum.IsDefined((EntrySortColumn)read.C)
                && new EntrySort((EntrySortColumn)read.C, read.D, read.F) == sort
                && read.E > 0
                && read.T is >= 0 and <= 3155378975999999999
                && read.A is null or (>= 0 and <= 3155378975999999999)
                && (!read.S || (sort.Column == EntrySortColumn.Unseen && read.V is null)))
            {
                var position = read.V is { } valueId ? new EntryValuePosition(valueId, read.X, read.N, read.A is { } date ? new DateTime(date, DateTimeKind.Utc) : null) : null;
                return new EntryCursor(sort, read.E, new DateTime(read.T, DateTimeKind.Utc), position, read.S);
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            // Not a cursor this list gave.
        }

        problems.Add(new DefinitionProblem("invalid_cursor", "after", "The list changed since its last page. Load it again."));
        return null;
    }

    private static readonly JsonSerializerOptions CursorJson = Definitions.Serialization.FrozenOptions.Of(new(JsonSerializerDefaults.Web));

    // A cursor's parts, by short names: its sort's column, direction and field, its entry and time, its value position, and
    // whether it stands among the entries seen already.
    private sealed record CursorText(int C, bool D, int F, long E, long T, long? V, string? X, double? N, long? A, bool S = false);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The answers of the entry {Entry} could not be read for its row.")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, Guid entry);
}
