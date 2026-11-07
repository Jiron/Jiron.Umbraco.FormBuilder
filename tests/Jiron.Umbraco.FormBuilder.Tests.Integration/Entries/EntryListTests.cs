using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>The entries list (ROADMAP 11.1, ADR-0207): its states, filters, search, rows, pages and refusals.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryListTests : ActionTestBase
{
    private static readonly FormField Amount = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000101"),
        Type = "number",
        Alias = "amount",
        Label = new LocalizedText("Amount"),
    };

    private static readonly FormField Day = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000102"),
        Type = "date",
        Alias = "day",
        Label = new LocalizedText("Day"),
    };

    private static readonly FormField Agreed = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000103"),
        Type = "checkbox",
        Alias = "agreed",
        Label = new LocalizedText("Agreed"),
    };

    private static readonly FormField Salary = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000104"),
        Type = "text",
        Alias = "salary",
        Label = new LocalizedText("Salary"),
        Sensitive = true,
    };

    private EntryLists Lists => GetRequiredService<EntryLists>();

    private Task<Guid> FormAsync(string? title = null) => PublishAsync(
        name: $"Requests {Guid.NewGuid():N}",
        change: definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { EntryTitle = title } } },
        fields: [NameField, Amount, Day, Agreed, Salary]);

    [Test]
    public async Task AListThatNamesNoState_LeavesOutDraftsSpamAndTestEntries()
    {
        var form = await FormAsync();
        var submitted = await StoreAsync(form, EntryState.Submitted);
        var approved = await StoreAsync(form, EntryState.Approved);
        var rejected = await StoreAsync(form, EntryState.Rejected);
        var unverified = await StoreAsync(form, EntryState.Unverified);
        await StoreAsync(form, EntryState.Draft, change: entry => entry with { ResumeTokenHash = new string('b', 64), ResumeExpiresUtc = Clock.GetUtcNow().AddDays(7) });
        var spam = await StoreAsync(form, EntryState.Spam);
        await StoreAsync(form, EntryState.Submitted, change: entry => entry with { IsTest = true });

        var all = await KeysAsync(form, new EntryListFilter());
        var count = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter()) { Count = true }))!.Count;
        var asked = await KeysAsync(form, new EntryListFilter { States = [EntryState.Spam] });

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EqualTo(new[] { unverified.Key, rejected.Key, approved.Key, submitted.Key }), "newest first, no draft, Spam or test entry");
            Assert.That(count, Is.EqualTo(new EntryCount(4, More: false)), "the count leaves them out too");
            Assert.That(asked, Is.EqualTo(new[] { spam.Key }), "Spam when asked for");
        });
    }

    [Test]
    public async Task ADraft_IsNoStateAListAsksFor()
    {
        var form = await FormAsync();

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAsync(form, new EntryListRequest(new EntryListFilter { States = [EntryState.Submitted, EntryState.Draft] })));

        Assert.That(refused!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("invalid_state", "filter.states[1]") }));
    }

    [Test]
    public async Task TheActionsFilter_FindsFailedUncertainAndDeliveredEntries()
    {
        var form = await FormAsync();
        var failed = await WithRunsAsync(form, ActionRunState.Succeeded, ActionRunState.Failed);
        var uncertain = await WithRunsAsync(form, ActionRunState.Uncertain);
        var delivered = await WithRunsAsync(form, ActionRunState.Succeeded, ActionRunState.Skipped);
        var waiting = await WithRunsAsync(form, ActionRunState.Succeeded, ActionRunState.Pending);
        var none = await StoreAsync(form, EntryState.Submitted);

        var rows = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter())))!.Rows.ToDictionary(row => row.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(await KeysAsync(form, new EntryListFilter { Actions = EntryActionFilter.Failed }), Is.EqualTo(new[] { failed.Key }));
            Assert.That(await KeysAsync(form, new EntryListFilter { Actions = EntryActionFilter.Uncertain }), Is.EqualTo(new[] { uncertain.Key }));
            Assert.That(await KeysAsync(form, new EntryListFilter { Actions = EntryActionFilter.Delivered }), Is.EqualTo(new[] { delivered.Key }), "every run delivered or skipped, and one run at least");
            Assert.That(rows[failed.Key].Actions, Is.EqualTo(new ActionRunTally(Delivered: 1, Failed: 1, Uncertain: 0, Waiting: 0, Skipped: 0)));
            Assert.That(rows[waiting.Key].Actions, Is.EqualTo(new ActionRunTally(Delivered: 1, Failed: 0, Uncertain: 0, Waiting: 1, Skipped: 0)));
            Assert.That(rows[none.Key].Actions.Total, Is.Zero);
        });
    }

    [Test]
    public async Task ASearch_FindsAnswersNumbersAndReferences()
    {
        var form = await FormAsync();
        var ada = await SubmitAsync(form, """{"name":"Ada Lovelace"}""");
        var grace = await SubmitAsync(form, """{"name":"Grace Hopper"}""");
        var reference = $"{Clock.GetUtcNow().Year}-{grace.Number:00000}";

        Assert.Multiple(async () =>
        {
            Assert.That(await KeysAsync(form, new EntryListFilter { Search = "lovelace" }), Is.EqualTo(new[] { ada.Key }), "an answer");
            Assert.That(await KeysAsync(form, new EntryListFilter { Search = grace.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) }), Is.EqualTo(new[] { grace.Key }), "a number");
            Assert.That(await KeysAsync(form, new EntryListFilter { Search = "#" + ada.Number }), Is.EqualTo(new[] { ada.Key }), "a number after #");
            Assert.That(await KeysAsync(form, new EntryListFilter { Search = reference }), Is.EqualTo(new[] { grace.Key }), "a reference");
            Assert.That(await KeysAsync(form, new EntryListFilter { Search = "   " }), Is.EqualTo(new[] { grace.Key, ada.Key }), "a blank search is none");
            Assert.That((await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter { Search = "a" }) { Count = true }))!.Count, Is.EqualTo(new EntryCount(2, More: false)));
        });
    }

    [Test]
    public async Task ReceivedPresets_CountTheDaysOfTheSitesTimeZone()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(general.Value with { TimeZone = "Europe/Berlin" }, general.Version, Editor);
        var form = await FormAsync();

        // Sunday 6 December 2026: 21:30 UTC is 22:30 in Berlin, and 23:30 UTC is already Monday there.
        At(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var november = await StoreAsync(form, EntryState.Submitted, later: false);
        At(new DateTimeOffset(2026, 12, 6, 21, 30, 0, TimeSpan.Zero));
        var sunday = await StoreAsync(form, EntryState.Submitted, later: false);
        At(new DateTimeOffset(2026, 12, 6, 23, 30, 0, TimeSpan.Zero));
        var mondayInBerlin = await StoreAsync(form, EntryState.Submitted, later: false);
        At(new DateTimeOffset(2026, 12, 7, 9, 0, 0, TimeSpan.Zero));

        Assert.Multiple(async () =>
        {
            Assert.That(await KeysAsync(form, Received(EntryReceivedPreset.Today)), Is.EqualTo(new[] { mondayInBerlin.Key }), "Monday in Berlin, though Sunday in UTC");
            Assert.That(await KeysAsync(form, Received(EntryReceivedPreset.ThisWeek)), Is.EqualTo(new[] { mondayInBerlin.Key }), "a week starts on Monday");
            Assert.That(await KeysAsync(form, Received(EntryReceivedPreset.Last7Days)), Is.EqualTo(new[] { mondayInBerlin.Key, sunday.Key }));
            Assert.That(await KeysAsync(form, Received(EntryReceivedPreset.ThisMonth)), Is.EqualTo(new[] { mondayInBerlin.Key, sunday.Key }));
            Assert.That(await KeysAsync(form, Received(EntryReceivedPreset.LastMonth)), Is.EqualTo(new[] { november.Key }));
            Assert.That(await KeysAsync(form, new EntryListFilter { Received = new EntryReceived { From = new DateOnly(2026, 11, 15), To = new DateOnly(2026, 11, 15) } }), Is.EqualTo(new[] { november.Key }), "a range of days, both included");
            Assert.That(await KeysAsync(form, new EntryListFilter { Received = new EntryReceived { To = new DateOnly(2026, 12, 6) } }), Is.EqualTo(new[] { sunday.Key, november.Key }), "open at its start");
        });
    }

    [Test]
    public async Task FieldFilters_TestEachKindOfAnswer()
    {
        var form = await FormAsync();
        var small = await SubmitAsync(form, """{"name":"Ada","amount":"5","day":"2026-03-01","agreed":true}""");
        var large = await SubmitAsync(form, """{"name":"Grace","amount":"500","day":"2026-04-01"}""");
        var empty = await SubmitAsync(form, """{"name":"Hedy"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await KeysAsync(form, Filter(NameField, EntryFieldTest.OneOf, values: ["ADA", "Hedy"])), Is.EqualTo(new[] { empty.Key, small.Key }), "compared as rules compare text");
            Assert.That(await KeysAsync(form, Filter(NameField, EntryFieldTest.NoneOf, values: ["Ada"])), Is.EqualTo(new[] { empty.Key, large.Key }));
            Assert.That(await KeysAsync(form, Filter(NameField, EntryFieldTest.StartsWith, values: ["gr"])), Is.EqualTo(new[] { large.Key }));
            Assert.That(await KeysAsync(form, Filter(Amount, EntryFieldTest.Between, min: 5, max: 100)), Is.EqualTo(new[] { small.Key }), "both bounds included");
            Assert.That(await KeysAsync(form, Filter(Day, EntryFieldTest.Between, from: new DateOnly(2026, 4, 1), to: new DateOnly(2026, 4, 1))), Is.EqualTo(new[] { large.Key }), "the day an answer names");
            Assert.That(await KeysAsync(form, Filter(Agreed, EntryFieldTest.Checked)), Is.EqualTo(new[] { small.Key }));
            Assert.That(await KeysAsync(form, Filter(Agreed, EntryFieldTest.NotChecked)), Is.EqualTo(new[] { empty.Key, large.Key }));
            Assert.That(await KeysAsync(form, Filter(Amount, EntryFieldTest.NotAnswered)), Is.EqualTo(new[] { empty.Key }));
        });
    }

    [Test]
    public async Task ASort_ByAField_OrTheOldestFirst()
    {
        var form = await FormAsync();
        var middle = await SubmitAsync(form, """{"name":"Ada","amount":"50"}""");
        var largest = await SubmitAsync(form, """{"name":"Grace","amount":"500"}""");
        var smallest = await SubmitAsync(form, """{"name":"Hedy","amount":"5"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await KeysAsync(form, new EntryListFilter { Sort = new EntryListSort { Field = Amount.Key, Descending = true } }), Is.EqualTo(new[] { largest.Key, middle.Key, smallest.Key }));
            Assert.That(await KeysAsync(form, new EntryListFilter { Sort = new EntryListSort { Field = NameField.Key, Descending = false } }), Is.EqualTo(new[] { middle.Key, largest.Key, smallest.Key }));
            Assert.That(await KeysAsync(form, new EntryListFilter { Sort = new EntryListSort { Descending = false } }), Is.EqualTo(new[] { middle.Key, largest.Key, smallest.Key }), "oldest first");
        });
    }

    [Test]
    public async Task ARow_HasItsTitleUnseenMarkAndTheColumnsAnswers()
    {
        var form = await FormAsync("{{ field.name }} {{ field.salary }}");
        var entry = await SubmitAsync(form, """{"name":"Ada","amount":"1234","salary":"50000"}""");

        var page = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter()) { Columns = [Amount.Key, NameField.Key, Day.Key] }))!;
        var row = page.Rows.Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Key, Is.EqualTo(entry.Key));
            Assert.That(row.Number, Is.EqualTo(entry.Number));
            Assert.That(row.Title, Is.EqualTo("Ada"), "a sensitive answer stays out of the title");
            Assert.That(row.State, Is.EqualTo(entry.State));
            Assert.That(row.Unseen, Is.True, "nobody has opened it");
            Assert.That(row.Cells.Select(cell => (cell.Field, cell.Texts.Count, cell.Masked)), Is.EqualTo(new[] { (Amount.Key, 1, false), (NameField.Key, 1, false), (Day.Key, 0, false) }), "in the columns' order, an unanswered one empty");
            Assert.That(row.Cells[1].Texts, Is.EqualTo(new[] { "Ada" }));
            Assert.That(page.Next, Is.Null);
        });
    }

    [Test]
    public async Task ASensitiveOrUnknownField_IsNoColumnFilterOrSort()
    {
        var form = await FormAsync();
        var unknown = Guid.NewGuid();

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAsync(form, new EntryListRequest(new EntryListFilter
        {
            Fields = [new EntryFieldCondition { Field = Salary.Key, Test = EntryFieldTest.Answered }, new EntryFieldCondition { Field = unknown, Test = EntryFieldTest.Answered }, new EntryFieldCondition { Field = Amount.Key, Test = EntryFieldTest.OneOf, Values = ["1"] }],
            Sort = new EntryListSort { Field = Salary.Key },
        })
        {
            Columns = [Salary.Key, unknown],
        }));
        var view = (await Lists.ViewAsync(Editor, form))!;

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EquivalentTo(new[]
            {
                ("field_not_filterable", "filter.fields[0].field"),
                ("field_not_filterable", "filter.fields[1].field"),
                ("test_not_allowed", "filter.fields[2].test"),
                ("field_not_sortable", "filter.sort.field"),
                ("field_not_column", "columns[0]"),
                ("field_not_column", "columns[1]"),
            }));
            Assert.That(view.Fields.Single(field => field.Key == Salary.Key), Has.Property(nameof(EntryListField.Sensitive)).True.And.Property(nameof(EntryListField.Column)).False.And.Property(nameof(EntryListField.Tests)).Empty);
            Assert.That(view.Fields.Select(field => field.Key), Is.EqualTo(new[] { NameField.Key, Amount.Key, Day.Key, Agreed.Key, Salary.Key }), "in the form's order");
        });
    }

    [Test]
    public async Task Pages_OfFifty_FollowOneAnother_AndTheFirstCounts()
    {
        var form = await FormAsync();
        var stored = new List<Guid>();
        for (var i = 0; i < 120; i++)
        {
            stored.Add((await StoreAsync(form, EntryState.Submitted)).Key);
        }

        var first = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter()) { Count = true }))!;
        var second = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter()) { After = first.Next }))!;
        var third = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter()) { After = second.Next }))!;
        stored.Reverse();

        Assert.Multiple(() =>
        {
            Assert.That(first.Count, Is.EqualTo(new EntryCount(120, More: false)));
            Assert.That(first.Rows, Has.Count.EqualTo(EntryLists.PageSize));
            Assert.That(first.Rows.Concat(second.Rows).Concat(third.Rows).Select(row => row.Key), Is.EqualTo(stored), "each once, in order");
            Assert.That(third.Rows, Has.Count.EqualTo(20));
            Assert.That(third.Next, Is.Null);
            Assert.That(second.Count, Is.Null, "only when asked");
        });
    }

    [TestCase("not a cursor", TestName = "A cursor this list never gave")]
    [TestCase("", TestName = "An empty cursor")]
    public async Task ACursor_OfAnotherSortOrNone_IsRefused(string text)
    {
        var form = await FormAsync();
        for (var i = 0; i < EntryLists.PageSize + 1; i++)
        {
            await StoreAsync(form, EntryState.Submitted);
        }

        var newest = (await Lists.ListAsync(form, new EntryListRequest(new EntryListFilter())))!;
        var oldest = new EntryListFilter { Sort = new EntryListSort { Descending = false } };

        Assert.Multiple(() =>
        {
            Assert.That(newest.Next, Is.Not.Null);
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAsync(form, new EntryListRequest(oldest) { After = newest.Next }))!.Problems[0].Path, Is.EqualTo("after"), "a cursor goes on only with its sort");
            Assert.That(Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAsync(form, new EntryListRequest(oldest) { After = text }))!.Problems[0].Code, Is.EqualTo("invalid_cursor"));
        });
    }

    [Test]
    public async Task AChoiceFieldsOwnOptions_AreOfferedToTick_AndThoseFromElsewhereAreNot()
    {
        var position = new FormField
        {
            Key = Guid.Parse("c4000000-0000-4000-8000-000000000105"),
            Type = "dropdown",
            Alias = "position",
            Label = new LocalizedText("Position"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "p1", Label = new LocalizedText("Accountant") }, new FieldOption { Value = "p2" }] },
        };
        var team = new FormField
        {
            Key = Guid.Parse("c4000000-0000-4000-8000-000000000106"),
            Type = "dropdown",
            Alias = "team",
            Label = new LocalizedText("Team"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "t1", ParentValues = ["p1"] }], Parent = position.Key },
        };
        var form = await PublishAsync(name: $"Choices {Guid.NewGuid():N}", fields: [NameField, position, team]);

        var view = (await Lists.ViewAsync(Editor, form))!;

        Assert.Multiple(() =>
        {
            Assert.That(view.Fields.Single(field => field.Key == position.Key).Options, Is.EqualTo(new[] { new EntryListOption("p1", "Accountant"), new EntryListOption("p2", "p2") }), "a value without a label names itself");
            Assert.That(view.Fields.Single(field => field.Key == team.Key).Options, Is.Empty, "options that hang on another answer are typed");
            Assert.That(view.Fields.Single(field => field.Key == NameField.Key).Options, Is.Empty);
        });
    }

    [Test]
    public async Task AFormThatIsGone_HasNoList()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await Lists.ListAsync(Guid.NewGuid(), new EntryListRequest(new EntryListFilter())), Is.Null);
            Assert.That(await Lists.ViewAsync(Editor, Guid.NewGuid()), Is.Null);
        });
    }

    private static EntryListFilter Received(EntryReceivedPreset preset) => new() { Received = new EntryReceived { Preset = preset } };

    // The clock moved on to a time; it never goes back.
    private void At(DateTimeOffset time) => Clock.Advance(time - Clock.GetUtcNow());

    private static EntryListFilter Filter(FormField field, EntryFieldTest test, IReadOnlyList<string>? values = null, decimal? min = null, decimal? max = null, DateOnly? from = null, DateOnly? to = null) =>
        new() { Fields = [new EntryFieldCondition { Field = field.Key, Test = test, Values = values ?? [], Min = min, Max = max, From = from, To = to }] };

    private async Task<List<Guid>> KeysAsync(Guid form, EntryListFilter filter) =>
        [.. (await Lists.ListAsync(form, new EntryListRequest(filter)))!.Rows.Select(row => row.Key)];

    /// <summary>Stores an entry of the form in a state, a minute after the one before unless <paramref name="later"/> says otherwise.</summary>
    private async Task<StoredEntry> StoreAsync(Guid form, EntryState state, bool later = true, Func<NewEntry, NewEntry>? change = null)
    {
        var row = (await FormRows.GetAsync(form))!;
        if (later)
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var entry = new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        };
        return (await Entries.CreateAsync(change is null ? entry : change(entry)))!;
    }

    /// <summary>An entry with a run in each of these states.</summary>
    private async Task<StoredEntry> WithRunsAsync(Guid form, params ActionRunState[] states)
    {
        var entry = await StoreAsync(form, EntryState.Submitted);
        var queued = await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [.. states.Select((_, index) => new NewActionRun(Guid.NewGuid(), "scripted", ActionTrigger.Submitted, index, StopOnFailure: false))]);
        foreach (var (run, state) in queued!.Zip(states))
        {
            if (state == ActionRunState.Pending)
            {
                continue;
            }

            var claimed = (await Runs.ClaimAsync(run.Id, "tests", TimeSpan.FromMinutes(5)))!;
            var token = claimed.LockedBy!;
            _ = state switch
            {
                ActionRunState.Succeeded => await Runs.SucceedAsync(run.Id, token),
                ActionRunState.Failed => await Runs.FailAsync(run.Id, token, new DeliveryError("http_503")),
                ActionRunState.Uncertain => await Runs.CutOffAsync(run.Id, token, new DeliveryError("cut_off")),
                _ => await Runs.FailAsync(run.Id, token, new DeliveryError("http_503")) && await SkipAsync(run.Id),
            };
        }

        return entry;
    }

    // A run skipped as the change that queued it was undone: what an Undo leaves.
    private async Task<bool> SkipAsync(long runId) =>
        await Runs.RequeueAsync(runId, ActionRunState.Failed) is { ChangeKey: { } change } && await Runs.SkipChangeAsync(change) > 0;
}
