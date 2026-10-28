using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The places held on options (ADR-0090): a count is made with the places held at that moment, and every write of an
/// entry keeps it equal to the form's values at the field that equal the option's value and whose entry holds places,
/// each write after locking the form's row.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class OptionPlaceRepositoryTests : RepositoryTestBase
{
    private static readonly Guid Slot = Guid.Parse("40000000-0000-4000-8000-000000000001");
    private static readonly Guid Boxes = Guid.Parse("40000000-0000-4000-8000-000000000002");
    private static readonly Guid Group = Guid.Parse("40000000-0000-4000-8000-000000000003");
    private static readonly Guid Seat = Guid.Parse("40000000-0000-4000-8000-000000000004");
    private static readonly Guid Name = Guid.Parse("40000000-0000-4000-8000-000000000005");

    [Test]
    public async Task ACount_IsMadeWithThePlacesHeldThen_AndEveryWriteOfAnEntryKeepsIt()
    {
        var form = await CreatePlacesFormAsync();
        var first = await AddAsync(form, EntryState.Submitted, ("slot", "a"));
        var second = await AddAsync(form, EntryState.Approved, ("slot", "a"));
        var unverified = await AddAsync(form, EntryState.Unverified, ("slot", "b"));
        await AddAsync(form, EntryState.Submitted, ("slot", "A"));
        var named = await AddAsync(form, EntryState.Submitted, ("name", "a"));
        await AddAsync(form, EntryState.Spam, ("slot", "a"));
        var rejected = await AddAsync(form, EntryState.Rejected, ("slot", "a"));
        await AddAsync(form, EntryState.Draft, ("slot", "a"));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["slot"], "a")) with { IsTest = true });
        var other = await CreatePlacesFormAsync();
        var elsewhere = await AddAsync(other, EntryState.Submitted, ("slot", "a"));
        var a = new OptionPlace(Slot, "a");
        var b = new OptionPlace(Slot, "b");

        var read = await Entries.GetPlacesTakenAsync(form.Id, [a, b, new OptionPlace(Slot, "c"), a]);
        var rowsAfterTheRead = Count(FormBuilderTables.OptionCapacity);
        var counted = await Entries.CountPlacesAsync(form.Id, [a]);
        var row = Row(form.Id, a);
        var steps = new List<(string Step, int? Count, int Held)>();
        async Task StepAsync(string step) => steps.Add((step, Row(form.Id, a)?.Taken, await HeldAsync(form, a)));

        await StepAsync("made");
        var third = await AddAsync(form, EntryState.Submitted, ("slot", "a"));
        await StepAsync("a new entry adds its value");
        var spam = await AddAsync(form, EntryState.Spam, ("slot", "a"));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["slot"], "a")) with { IsTest = true });
        await StepAsync("spam and a test entry add nothing");
        await Entries.ChangeStateAsync(third, EntryState.Submitted, EntryState.Approved, Editor);
        await StepAsync("approving keeps it");
        await Entries.ChangeStateAsync(third, EntryState.Approved, EntryState.Rejected, Editor);
        await StepAsync("rejecting gives it back");
        await Entries.ChangeStateAsync(third, EntryState.Rejected, EntryState.Spam, Editor);
        await StepAsync("from rejected to spam changes nothing");
        await Entries.ChangeStateAsync(third, EntryState.Spam, EntryState.Submitted, Editor);
        await StepAsync("back from spam takes it again");
        await Entries.ChangeStateAsync(third, EntryState.Submitted, EntryState.Spam, Editor);
        await StepAsync("marking as spam gives it back");
        await Entries.ReplaceValuesAsync(first, [form["slot"]], [new NewEntryValue(form["slot"], "b")], string.Empty);
        await StepAsync("a changed value moves it");
        await Entries.ReplaceValuesAsync(unverified, [form["slot"]], [new NewEntryValue(form["slot"], "a")], string.Empty);
        await StepAsync("a value changed to the option takes a place");
        await Entries.ReplaceValuesAsync(first, [form["name"]], [new NewEntryValue(form["name"], "Anna")], string.Empty);
        await StepAsync("a change of another field keeps it");
        await Entries.ReplaceValuesAsync(rejected, [form["slot"]], [new NewEntryValue(form["slot"], "b")], string.Empty);
        await StepAsync("a change of an entry that holds none changes nothing");
        await Entries.ChangeStateAsync(named, EntryState.Submitted, EntryState.Rejected, Editor);
        await StepAsync("rejecting an entry without the option changes nothing");
        await Entries.DeleteAsync(spam);
        await StepAsync("deleting an entry in spam changes nothing");
        await Entries.DeleteAsync([second, third, elsewhere]);
        await StepAsync("deleting several gives back the places of those that hold them, each form its own");
        var uncounted = Row(form.Id, b);
        var again = await Entries.CountPlacesAsync(form.Id, [a, b]);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EquivalentTo(new Dictionary<OptionPlace, int> { [a] = 2, [b] = 1, [new OptionPlace(Slot, "c")] = 0 }), "the values' count, letter case and other fields and forms apart");
            Assert.That(rowsAfterTheRead, Is.Zero, "a read makes no count");
            Assert.That(counted, Is.EquivalentTo(new Dictionary<OptionPlace, int> { [a] = 2 }));
            Assert.That((row?.OptionValue, row?.ValueHash), Is.EqualTo(("a", OptionValueHash.For("a"))));
            Assert.That(steps.Select(step => (step.Step, step.Count)), Is.EqualTo(new (string, int?)[]
            {
                ("made", 2),
                ("a new entry adds its value", 3),
                ("spam and a test entry add nothing", 3),
                ("approving keeps it", 3),
                ("rejecting gives it back", 2),
                ("from rejected to spam changes nothing", 2),
                ("back from spam takes it again", 3),
                ("marking as spam gives it back", 2),
                ("a changed value moves it", 1),
                ("a value changed to the option takes a place", 2),
                ("a change of another field keeps it", 2),
                ("a change of an entry that holds none changes nothing", 2),
                ("rejecting an entry without the option changes nothing", 2),
                ("deleting an entry in spam changes nothing", 2),
                ("deleting several gives back the places of those that hold them, each form its own", 1),
            }));
            Assert.That(steps.Select(step => step.Count), Is.EqualTo(steps.Select(step => (int?)step.Held)), "every count is the values' count");
            Assert.That(uncounted, Is.Null, "an option without a count gets none from a write");
            Assert.That(Row(other.Id, a), Is.Null, "nor does another form's");
            Assert.That(again, Is.EquivalentTo(new Dictionary<OptionPlace, int> { [a] = 1, [b] = 1 }), "a count made again is the one kept, and a new one counts the values then");
            Assert.That(Count(FormBuilderTables.OptionCapacity), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task EachRow_AndEachChosenOption_HoldsAPlace_AndAMoveBackCountsOnAFullOptionToo()
    {
        var form = await CreatePlacesFormAsync();
        var x = new OptionPlace(Seat, "x");
        var morning = new OptionPlace(Boxes, "morning");
        var evening = new OptionPlace(Boxes, "evening");
        await Entries.CountPlacesAsync(form.Id, [x, morning, evening]);
        var rows = await Entries.CreateAsync(NewEntry(
            form.Id,
            form.VersionId,
            new NewEntryValue(form["seat"], "x", RowIndex: 0),
            new NewEntryValue(form["seat"], "x", RowIndex: 1),
            new NewEntryValue(form["boxes"], "morning", ValueIndex: 0),
            new NewEntryValue(form["boxes"], "evening", ValueIndex: 1)));
        var counts = await Entries.GetPlacesTakenAsync(form.Id, [x, morning, evening]);
        await Entries.ChangeStateAsync(rows!.Id, EntryState.Submitted, EntryState.Rejected, Editor);
        var rejected = await Entries.GetPlacesTakenAsync(form.Id, [x, morning, evening]);
        await Entries.ChangeStateAsync(rows.Id, EntryState.Rejected, EntryState.Submitted, Editor);
        var back = await Entries.GetPlacesTakenAsync(form.Id, [x, morning, evening]);

        Assert.Multiple(() =>
        {
            Assert.That(counts, Is.EquivalentTo(new Dictionary<OptionPlace, int> { [x] = 2, [morning] = 1, [evening] = 1 }));
            Assert.That(rejected.Values, Is.All.Zero);
            Assert.That(back, Is.EquivalentTo(counts), "the repository refuses nothing: whether to overbook is the caller's question");
        });
    }

    [Test]
    public async Task EveryWriteThatChangesPlaces_LocksTheFormsRowFirst()
    {
        var form = await CreatePlacesFormAsync();
        var entry = await AddAsync(form, EntryState.Submitted, ("slot", "a"));
        var other = await AddAsync(form, EntryState.Submitted, ("slot", "a"));
        var logs = new List<(string Write, CommandLog Log)>();
        async Task WatchAsync(string write, Func<Task> run)
        {
            var log = new CommandLog();
            await WatchedAsync(log, async () =>
            {
                await run();
                return 0;
            });
            logs.Add((write, log));
        }

        await WatchAsync("a state change", () => Entries.ChangeStateAsync(entry, EntryState.Submitted, EntryState.Rejected, Editor));
        await WatchAsync("a change of values", () => Entries.ReplaceValuesAsync(entry, [form["slot"]], [new NewEntryValue(form["slot"], "b")], string.Empty));
        await WatchAsync("a delete", () => Entries.DeleteAsync(entry));
        await WatchAsync("a delete of several", () => Entries.DeleteAsync([other]));

        Assert.Multiple(() =>
        {
            foreach (var (write, log) in logs)
            {
                Assert.That(log.Texts[0], Does.StartWith("UPDATE " + T(FormBuilderTables.Form)).And.Contain(Q("lastEntryNumber")), write);
            }
        });
    }

    [Test]
    public async Task AFormThatDoesNotExist_HasNoCounts_AndAnOptionThatCannotBeCounted_IsRefused()
    {
        var form = await CreatePlacesFormAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.CountPlacesAsync(int.MaxValue, [new OptionPlace(Slot, "a")]), Is.Empty);
            Assert.That(Count(FormBuilderTables.OptionCapacity), Is.Zero);
            Assert.That(await Entries.CountPlacesAsync(form.Id, []), Is.Empty);
            Assert.That(await Entries.GetPlacesTakenAsync(form.Id, []), Is.Empty, "no options, no read");
            foreach (var option in new[] { new OptionPlace(Guid.Empty, "a"), new OptionPlace(Slot, string.Empty), new OptionPlace(Slot, new string('a', 201)) })
            {
                Assert.That(() => Entries.GetPlacesTakenAsync(form.Id, [option]), Throws.ArgumentException, option.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.That(() => Entries.CountPlacesAsync(form.Id, [option]), Throws.ArgumentException);
            }

            Assert.That(() => Entries.CountPlacesAsync(form.Id, [new OptionPlace(Slot, "a" + (char)0xD800)]), Throws.ArgumentException, "a lone surrogate has no hash");
            Assert.That((await Entries.CountPlacesAsync(form.Id, [new OptionPlace(Slot, new string('a', 200))])).Values, Is.EqualTo(new[] { 0 }), "the longest value");
        });
    }

    // A form with a choice field, a field of several choices, a repeatable group's child and a text field.
    private async Task<TestForm> CreatePlacesFormAsync()
    {
        var form = await CreateFormAsync();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (alias, key, parent) in new[] { ("slot", Slot, (Guid?)null), ("boxes", Boxes, null), ("group", Group, null), ("seat", Seat, Group), ("name", Name, null) })
        {
            var field = new CatalogueFieldValues(key, alias, alias, alias == "name" ? "text" : "radio", alias == "group" ? FieldStorageKind.None : FieldStorageKind.Text, false, ids.Count, null, null, parent);
            ids[alias] = (await Forms.AddFieldAsync(form.Id, field))!.Id;
        }

        return new TestForm(form.Id, form.DraftVersionId!.Value, ids);
    }

    private async Task<long> AddAsync(TestForm form, EntryState state, params (string Field, string Text)[] values) =>
        (await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, [.. values.Select(value => new NewEntryValue(form[value.Field], value.Text))]) with { State = state }))!.Id;

    // The places held on an option, counted here from the entries and their values, apart from the counts.
    private async Task<int> HeldAsync(TestForm form, OptionPlace option)
    {
        var field = form[option.FieldKey == Slot ? "slot" : option.FieldKey == Boxes ? "boxes" : "seat"];
        var holding = new List<long>();
        foreach (var id in await Entries.ListIdsAsync(form.Id, 1000))
        {
            if (await Entries.GetByIdAsync(id) is { } entry && EntryLimits.Counts(entry.State, entry.IsTest))
            {
                holding.Add(id);
            }
        }

        return (await Entries.ListValuesAsync(holding)).Count(value => value.FieldId == field && string.Equals(value.Text, option.Value, StringComparison.Ordinal));
    }

    private Core.Persistence.Dtos.OptionCapacityDto? Row(int formId, OptionPlace option) =>
        Query(db => db.FirstOrDefault<Core.Persistence.Dtos.OptionCapacityDto>(
            $"SELECT * FROM {T(FormBuilderTables.OptionCapacity)} WHERE {Q("formId")} = @0 AND {Q("fieldKey")} = @1 AND {Q("valueHash")} = @2",
            formId,
            option.FieldKey,
            OptionValueHash.For(option.Value)));
}
