using System.Collections.Frozen;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>Which entries a list holds and in which order (ADR-0207): the shape a filter set keeps, too.</summary>
internal sealed class EntryListFilterRequestModel
{
    /// <summary>The states: <c>unverified</c>, <c>submitted</c>, <c>approved</c>, <c>rejected</c> or <c>spam</c>; none for every state but Spam.</summary>
    public IReadOnlyList<string> States { get; init; } = [];

    /// <summary>When the entries were received, or any time.</summary>
    public EntryReceivedRequestModel? Received { get; init; }

    /// <summary>The entries by their action runs: <c>failed</c>, <c>uncertain</c> or <c>delivered</c>; none for any.</summary>
    public string? Actions { get; init; }

    /// <summary>The field filters, all of which an entry matches, at most 10.</summary>
    public IReadOnlyList<EntryFieldFilterRequestModel> Fields { get; init; } = [];

    /// <summary>The words, number or reference to look for.</summary>
    public string? Search { get; init; }

    /// <summary>The forms of the Inbox or Spam, at most 100; none for every form the user may view. A form's list names none, or its own.</summary>
    public IReadOnlyList<Guid> Forms { get; init; } = [];

    /// <summary>The order; none for the list's own: newest first, or in the Inbox the unseen entries first.</summary>
    public EntryListSortRequestModel? Sort { get; init; }
}

/// <summary>When the entries were received, in the site's time zone: a preset or a range of days.</summary>
internal sealed class EntryReceivedRequestModel
{
    /// <summary><c>today</c>, <c>thisWeek</c>, <c>last7Days</c>, <c>thisMonth</c> or <c>lastMonth</c>; none for a range.</summary>
    public string? Preset { get; init; }

    /// <summary>The first day of a range, included.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last day of a range, included.</summary>
    public DateOnly? To { get; init; }
}

/// <summary>A filter on one of the form's fields.</summary>
internal sealed class EntryFieldFilterRequestModel
{
    /// <summary>The field's key.</summary>
    public Guid Field { get; init; }

    /// <summary>
    /// <c>oneOf</c>, <c>noneOf</c>, <c>startsWith</c>, <c>between</c>, <c>checked</c>, <c>notChecked</c>, <c>answered</c>
    /// or <c>notAnswered</c>: those the field's <c>tests</c> name.
    /// </summary>
    public string Test { get; init; } = string.Empty;

    /// <summary>The values of <c>oneOf</c> and <c>noneOf</c>, or the one of <c>startsWith</c>.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>The lowest number of a number's <c>between</c>.</summary>
    public decimal? Min { get; init; }

    /// <summary>The highest number of a number's <c>between</c>.</summary>
    public decimal? Max { get; init; }

    /// <summary>The first day of a date's <c>between</c>.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last day of a date's <c>between</c>.</summary>
    public DateOnly? To { get; init; }
}

/// <summary>The order of a list.</summary>
internal sealed class EntryListSortRequestModel
{
    /// <summary>The field to sort by, one whose <c>sorts</c> is true; none for when the entries were received.</summary>
    public Guid? Field { get; init; }

    /// <summary>Whether the newest or the largest comes first.</summary>
    public bool Descending { get; init; } = true;
}

/// <summary>A page of a form's entries to read.</summary>
internal sealed class EntryListRequestModel
{
    /// <summary>Which entries; none for every entry a list holds, newest first.</summary>
    public EntryListFilterRequestModel? Filter { get; init; }

    /// <summary>The fields to show as columns, at most 20, each one whose <c>column</c> is true; none for the entries' details only.</summary>
    public IReadOnlyList<Guid>? Columns { get; init; }

    /// <summary>The <c>next</c> of the page before; none for the first page.</summary>
    public string? After { get; init; }

    /// <summary>Whether to count the matching entries too, as the first page does.</summary>
    public bool Count { get; init; }
}

/// <summary>A page of a form's entries.</summary>
internal sealed class EntryListPageResponseModel
{
    /// <summary>The entries, in the list's order: at most 50.</summary>
    public required IReadOnlyList<EntryListRowResponseModel> Items { get; init; }

    /// <summary>What the next page's <c>after</c> is; none on the last page.</summary>
    public string? Next { get; init; }

    /// <summary>How many entries match, when asked; for a search at most 1,000.</summary>
    public int? Total { get; init; }

    /// <summary>Whether more than <c>total</c> match, which only a search's count says.</summary>
    public bool More { get; init; }

    /// <summary>
    /// In the Inbox and Spam, the forms of the page's entries and then the forms the filter chose, with what the user may do
    /// on each; none in a form's list.
    /// </summary>
    public IReadOnlyList<EntryListFormResponseModel> Forms { get; init; } = [];
}

/// <summary>A form the Inbox or Spam names: on a page, with what the user may do on it, or for the Form filter.</summary>
internal sealed class EntryListFormResponseModel
{
    /// <summary>The form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The user's permissions on it, by name; none where the answer names forms only.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

/// <summary>An entry as the list shows it.</summary>
internal sealed class EntryListRowResponseModel
{
    /// <summary>The entry's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its number within the form.</summary>
    public required int Number { get; init; }

    /// <summary>Its title, without sensitive answers; none, and its number names it.</summary>
    public string? Title { get; init; }

    /// <summary>Its state.</summary>
    public required string State { get; init; }

    /// <summary>When it was received.</summary>
    public required DateTimeOffset Received { get; init; }

    /// <summary>Whether no backoffice user has opened it.</summary>
    public required bool Unseen { get; init; }

    /// <summary>Its action runs, counted.</summary>
    public required EntryListActionsResponseModel Actions { get; init; }

    /// <summary>The columns' cells, in the columns' order.</summary>
    public required IReadOnlyList<EntryListCellResponseModel> Cells { get; init; }

    /// <summary>Its form.</summary>
    public required Guid Form { get; init; }
}

/// <summary>An entry's action runs, counted by where they are.</summary>
internal sealed class EntryListActionsResponseModel
{
    /// <summary>Delivered.</summary>
    public required int Delivered { get; init; }

    /// <summary>Failed for good.</summary>
    public required int Failed { get; init; }

    /// <summary>Uncertain: they may have been delivered.</summary>
    public required int Uncertain { get; init; }

    /// <summary>Waiting for an attempt, or in one.</summary>
    public required int Waiting { get; init; }

    /// <summary>Never run.</summary>
    public required int Skipped { get; init; }
}

/// <summary>A column's cell.</summary>
internal sealed class EntryListCellResponseModel
{
    /// <summary>The column's field.</summary>
    public required Guid Field { get; init; }

    /// <summary>The answer's texts, one for each row of a repeatable group; none when unanswered.</summary>
    public required IReadOnlyList<string> Texts { get; init; }

    /// <summary>Whether a sensitive answer is there, which the entry shows.</summary>
    public required bool Masked { get; init; }
}

/// <summary>What a form's Entries view opens with.</summary>
internal sealed class EntryListViewResponseModel
{
    /// <summary>The form's fields that hold answers: the live version's in their order, then the removed ones.</summary>
    public required IReadOnlyList<EntryListFieldResponseModel> Fields { get; init; }

    /// <summary>The user's columns for this form, in their order.</summary>
    public required IReadOnlyList<Guid> Columns { get; init; }

    /// <summary>The user's filter sets of this form and of every form.</summary>
    public required IReadOnlyList<EntryFilterSetResponseModel> FilterSets { get; init; }
}

/// <summary>A field of the form as the list offers it.</summary>
internal sealed class EntryListFieldResponseModel
{
    /// <summary>The field's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its label in the default culture, or its alias.</summary>
    public required string Label { get; init; }

    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>Its field type's alias.</summary>
    public required string Type { get; init; }

    /// <summary><c>text</c>, <c>longText</c>, <c>number</c>, <c>date</c>, <c>checkbox</c>, <c>file</c>, <c>parts</c> or <c>group</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The repeatable group it is a child of, or none.</summary>
    public Guid? Parent { get; init; }

    /// <summary>Whether it is sensitive: then the entry shows it, and the list does not.</summary>
    public required bool Sensitive { get; init; }

    /// <summary>Whether the live version no longer has it.</summary>
    public required bool Removed { get; init; }

    /// <summary>Whether it can be a column.</summary>
    public required bool Column { get; init; }

    /// <summary>Whether the list can be sorted by it.</summary>
    public required bool Sorts { get; init; }

    /// <summary>The tests its filter makes; none for a field that is no filter.</summary>
    public required IReadOnlyList<string> Tests { get; init; }

    /// <summary>Its own options, which a filter offers to tick; none where its options come from elsewhere.</summary>
    public required IReadOnlyList<EntryListOptionResponseModel> Options { get; init; }
}

/// <summary>One of a choice field's own options.</summary>
internal sealed class EntryListOptionResponseModel
{
    /// <summary>What entries store.</summary>
    public required string Value { get; init; }

    /// <summary>Its label in the default culture.</summary>
    public required string Label { get; init; }
}

/// <summary>A filter set.</summary>
internal sealed class EntryFilterSetResponseModel
{
    /// <summary>The set's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The form it belongs to, or none for every form.</summary>
    public Guid? Form { get; init; }

    /// <summary>What it filters.</summary>
    public required EntryListFilterRequestModel Filter { get; init; }
}

/// <summary>A filter set to save.</summary>
internal sealed class EntryFilterSetRequestModel
{
    /// <summary>Its name, 1 to 60 characters.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The form it belongs to, or none for every form.</summary>
    public Guid? Form { get; init; }

    /// <summary>What it filters; none for every entry a list holds.</summary>
    public EntryListFilterRequestModel? Filter { get; init; }
}

/// <summary>What a filter set's delete did.</summary>
internal sealed class EntryFilterSetDeleteResponseModel
{
    /// <summary>Whether the user kept the set; false when it was gone already.</summary>
    public required bool Deleted { get; init; }
}

/// <summary>A form's columns to keep for the user.</summary>
internal sealed class EntryColumnsRequestModel
{
    /// <summary>The fields, at most 20, in their order; none to show only the entries' details.</summary>
    public IReadOnlyList<Guid> Columns { get; init; } = [];
}

/// <summary>Entries to delete for good.</summary>
internal sealed class EntryDeleteRequestModel
{
    /// <summary>The entries, 1 to 500.</summary>
    public IReadOnlyList<Guid> Entries { get; init; } = [];

    /// <summary>Whether to delete them; without it, the answer says what would go and deletes nothing.</summary>
    public bool Confirm { get; init; }
}

/// <summary>What a delete did, or would do.</summary>
internal sealed class EntryDeleteResponseModel
{
    /// <summary>How many entries it deletes: those of the form that exist and no legal hold keeps.</summary>
    public required int Entries { get; init; }

    /// <summary>How many uploaded files go with them.</summary>
    public required int Files { get; init; }

    /// <summary>Whether it deleted them, as the request confirmed.</summary>
    public required bool Deleted { get; init; }

    /// <summary>How many of the entries a legal hold keeps, which stay until it ends (ADR-0222).</summary>
    public required int Held { get; init; }
}

/// <summary>Entries whose actions to run again.</summary>
internal sealed class EntryRerunRequestModel
{
    /// <summary>The entries, 1 to 500.</summary>
    public IReadOnlyList<Guid> Entries { get; init; } = [];

    /// <summary>Whether to run them; without it, the answer says what would run and runs nothing.</summary>
    public bool Confirm { get; init; }
}

/// <summary>What a re-run of actions does, or would do.</summary>
internal sealed class EntryRerunResponseModel
{
    /// <summary>The actions it runs for one entry at least, in the form's order.</summary>
    public required IReadOnlyList<EntryRerunActionResponseModel> Actions { get; init; }

    /// <summary>How many entries it runs actions for.</summary>
    public required int Entries { get; init; }

    /// <summary>How many it leaves out: in Spam, waiting for their address, or with no action to run.</summary>
    public required int LeftOut { get; init; }

    /// <summary>Whether it ran them, as the request confirmed.</summary>
    public required bool Queued { get; init; }
}

/// <summary>An action a re-run runs.</summary>
internal sealed class EntryRerunActionResponseModel
{
    /// <summary>The action's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its type's alias.</summary>
    public required string Type { get; init; }

    /// <summary>Its name, or none.</summary>
    public string? Name { get; init; }

    /// <summary>What starts it: <c>submitted</c>, <c>verified</c>, <c>approved</c> or <c>rejected</c>.</summary>
    public required string Trigger { get; init; }
}

/// <summary>Turns the list's models into the list service's records and back, each name as the filter set's JSON writes it.</summary>
internal static class EntryListMapping
{
    private static readonly FrozenDictionary<string, EntryState> States = Names<EntryState>(EntryState.Unverified, EntryState.Submitted, EntryState.Approved, EntryState.Rejected, EntryState.Spam);
    private static readonly FrozenDictionary<string, EntryReceivedPreset> Presets = Names(Enum.GetValues<EntryReceivedPreset>());
    private static readonly FrozenDictionary<string, EntryActionFilter> ActionFilters = Names(EntryActionFilter.Failed, EntryActionFilter.Uncertain, EntryActionFilter.Delivered);
    private static readonly FrozenDictionary<string, EntryFieldTest> Tests = Names(Enum.GetValues<EntryFieldTest>());

    /// <summary>A name as the JSON writes it.</summary>
    public static string NameOf<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>The filter a model asks for, its unreadable names added as problems under <paramref name="path"/>.</summary>
    public static EntryListFilter Filter(EntryListFilterRequestModel? model, string path, List<DefinitionProblem> problems)
    {
        model ??= new EntryListFilterRequestModel();
        var states = new List<EntryState>();
        for (var index = 0; index < model.States.Count; index++)
        {
            if (model.States[index] is { } name && States.TryGetValue(name, out var state))
            {
                states.Add(state);
            }
            else
            {
                problems.Add(new DefinitionProblem("invalid_state", $"{path}.states[{index}]", "This is no state a list holds."));
            }
        }

        EntryReceived? received = null;
        if (model.Received is { } when)
        {
            EntryReceivedPreset? preset = null;
            if (when.Preset is { } name)
            {
                if (Presets.TryGetValue(name, out var found))
                {
                    preset = found;
                }
                else
                {
                    problems.Add(new DefinitionProblem("invalid_value", $"{path}.received.preset", "This is no span of days the list knows."));
                }
            }

            received = new EntryReceived { Preset = preset, From = when.From, To = when.To };
        }

        var actions = EntryActionFilter.Any;
        if (model.Actions is { Length: > 0 } actionName && actionName != "any")
        {
            if (ActionFilters.TryGetValue(actionName, out var found))
            {
                actions = found;
            }
            else
            {
                problems.Add(new DefinitionProblem("invalid_value", $"{path}.actions", "The action filter is failed, uncertain or delivered."));
            }
        }

        var fields = new List<EntryFieldCondition>();
        for (var index = 0; index < model.Fields.Count; index++)
        {
            var field = model.Fields[index];
            if (field is null || !Tests.TryGetValue(field.Test ?? string.Empty, out var test))
            {
                problems.Add(new DefinitionProblem("test_not_allowed", $"{path}.fields[{index}].test", "This is no test a filter makes."));
                continue;
            }

            fields.Add(new EntryFieldCondition { Field = field.Field, Test = test, Values = field.Values ?? [], Min = field.Min, Max = field.Max, From = field.From, To = field.To });
        }

        return new EntryListFilter
        {
            States = states,
            Received = received,
            Actions = actions,
            Fields = fields,
            Search = model.Search,
            Forms = model.Forms ?? [],
            Sort = model.Sort is { } sort ? new EntryListSort { Field = sort.Field, Descending = sort.Descending } : null,
        };
    }

    /// <summary>The model of a filter.</summary>
    public static EntryListFilterRequestModel Model(EntryListFilter filter) => new()
    {
        States = [.. filter.States.Select(NameOf)],
        Received = filter.Received is { } received ? new EntryReceivedRequestModel { Preset = received.Preset is { } preset ? NameOf(preset) : null, From = received.From, To = received.To } : null,
        Actions = filter.Actions == EntryActionFilter.Any ? null : NameOf(filter.Actions),
        Fields = [.. filter.Fields.Select(field => new EntryFieldFilterRequestModel { Field = field.Field, Test = NameOf(field.Test), Values = field.Values, Min = field.Min, Max = field.Max, From = field.From, To = field.To })],
        Search = filter.Search,
        Forms = filter.Forms,
        Sort = filter.Sort is { } sort ? new EntryListSortRequestModel { Field = sort.Field, Descending = sort.Descending } : null,
    };

    /// <summary>The model of a filter set.</summary>
    public static EntryFilterSetResponseModel Model(EntryFilterSet set) => new() { Key = set.Key, Name = set.Name, Form = set.Form, Filter = Model(set.Filter) };

    /// <summary>The model of a field.</summary>
    public static EntryListFieldResponseModel Model(EntryListField field) => new()
    {
        Key = field.Key,
        Label = field.Label,
        Alias = field.Alias,
        Type = field.Type,
        Kind = NameOf(field.Kind),
        Parent = field.Parent,
        Sensitive = field.Sensitive,
        Removed = field.Removed,
        Column = field.Column,
        Sorts = field.Sorts,
        Tests = [.. field.Tests.Select(NameOf)],
        Options = [.. field.Options.Select(option => new EntryListOptionResponseModel { Value = option.Value, Label = option.Label })],
    };

    /// <summary>
    /// The forms a page of several forms' entries names, each with what the user may do on it, so a list offers per row
    /// what its form allows (ADR-0209); a form gone since the page was read allows nothing.
    /// </summary>
    /// <param name="named">The forms the page names.</param>
    /// <param name="forms">The forms, by key.</param>
    /// <param name="access">The user's permissions on each.</param>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public static async Task<IReadOnlyList<EntryListFormResponseModel>> FormsAsync(IReadOnlyList<EntryListForm> named, IFormBuilderFormService forms, IFormBuilderAccessService access, AccessUser user, CancellationToken cancellationToken)
    {
        var models = new List<EntryListFormResponseModel>();
        foreach (var form in named)
        {
            var permissions = await forms.GetAsync(form.Key, cancellationToken) is { } info
                ? await access.GetForFormAsync(user, FormAccessTarget.Of(info), cancellationToken)
                : FormBuilderPermissions.None;
            models.Add(new EntryListFormResponseModel { Key = form.Key, Name = form.Name, Permissions = PermissionRules.NamesOf(permissions) });
        }

        return models;
    }

    /// <summary>The model of a page.</summary>
    public static EntryListPageResponseModel Model(EntryListPage page) => new()
    {
        Items = [.. page.Rows.Select(row => new EntryListRowResponseModel
        {
            Key = row.Key,
            Number = row.Number,
            Title = row.Title,
            State = StoredNames.Of(row.State),
            Received = row.Received,
            Unseen = row.Unseen,
            Actions = new EntryListActionsResponseModel { Delivered = row.Actions.Delivered, Failed = row.Actions.Failed, Uncertain = row.Actions.Uncertain, Waiting = row.Actions.Waiting, Skipped = row.Actions.Skipped },
            Cells = [.. row.Cells.Select(cell => new EntryListCellResponseModel { Field = cell.Field, Texts = cell.Texts, Masked = cell.Masked })],
            Form = row.Form,
        })],
        Next = page.Next,
        Total = page.Count?.Value,
        More = page.Count?.More ?? false,
    };

    private static FrozenDictionary<string, TEnum> Names<TEnum>(params TEnum[] values)
        where TEnum : struct, Enum => values.ToFrozenDictionary(NameOf, value => value, StringComparer.Ordinal);
}
