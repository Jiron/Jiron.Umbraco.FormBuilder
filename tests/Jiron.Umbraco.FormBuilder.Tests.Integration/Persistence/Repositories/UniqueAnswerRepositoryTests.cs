using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The answers that must be unique in their form (ADR-0091): a value is taken when an entry that counts toward the entry
/// limit has an equal value at the same field, compared as entries filter: a number or a date by its column, a text by
/// its key, cleaned and folded, and whole where the key is cut. The unit's read locks the form's row first and leaves its
/// own entry out.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class UniqueAnswerRepositoryTests : RepositoryTestBase
{
    private static readonly Guid Email = Guid.Parse("50000000-0000-4000-8000-000000000001");
    private static readonly Guid Note = Guid.Parse("50000000-0000-4000-8000-000000000002");
    private static readonly Guid Amount = Guid.Parse("50000000-0000-4000-8000-000000000003");
    private static readonly Guid Day = Guid.Parse("50000000-0000-4000-8000-000000000004");
    private static readonly Guid Group = Guid.Parse("50000000-0000-4000-8000-000000000005");
    private static readonly Guid Member = Guid.Parse("50000000-0000-4000-8000-000000000006");
    private static readonly Guid Name = Guid.Parse("50000000-0000-4000-8000-000000000007");

    private static readonly (string Alias, Guid Key, FieldStorageKind Storage, Guid? Parent)[] Fields =
    [
        ("email", Email, FieldStorageKind.Text, null),
        ("note", Note, FieldStorageKind.LongText, null),
        ("amount", Amount, FieldStorageKind.Number, null),
        ("day", Day, FieldStorageKind.Date, null),
        ("group", Group, FieldStorageKind.None, null),
        ("member", Member, FieldStorageKind.Text, Group),
        ("name", Name, FieldStorageKind.Text, null),
    ];

    [Test]
    public async Task AValue_IsTaken_WhenAnEntryThatHoldsItsAnswersHasItAtItsField_ComparedAsEntriesFilter()
    {
        var form = await CreateUniqueFormAsync();
        await AddAsync(form, EntryState.Submitted, ("email", " Ada@Example.COM "), ("amount", "12.5"), ("day", "2026-10-25"));
        await AddAsync(form, EntryState.Approved, ("email", "bob@example.com"));
        await AddAsync(form, EntryState.Unverified, ("email", "cy@example.com"));
        await AddAsync(form, EntryState.Spam, ("email", "dan@example.com"));
        await AddAsync(form, EntryState.Rejected, ("email", "eve@example.com"));
        await AddAsync(form, EntryState.Draft, ("email", "fay@example.com"));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, Row(form, "email", "gus@example.com")) with { IsTest = true });
        await AddAsync(form, EntryState.Submitted, ("name", "ida@example.com"), ("email", "50x"), ("amount", "1234567890123456"));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, Row(form, "member", "Anna"), Row(form, "member", "kim", rowIndex: 1)));
        var other = await CreateUniqueFormAsync();
        await AddAsync(other, EntryState.Submitted, ("email", "hal@example.com"));
        AnswerValue[] taken =
        [
            Asked("email", "ada@example.com"),
            Asked("email", "  ADA@EXAMPLE.COM"),
            Asked("email", "bob@example.com"),
            Asked("email", "cy@example.com"),
            Asked("email", "50X"),
            Asked("member", "KIM"),
            Asked("amount", "12.50"),
            Asked("amount", "1234567890123456"),
            Asked("day", "2026-10-25"),
        ];
        AnswerValue[] free =
        [
            Asked("email", "dan@example.com"),
            Asked("email", "eve@example.com"),
            Asked("email", "fay@example.com"),
            Asked("email", "gus@example.com"),
            Asked("email", "hal@example.com"),
            Asked("email", "ida@example.com"),
            Asked("email", "50%"),
            Asked("email", "5_x"),
            Asked("member", "ann"),
            Asked("amount", "12.25"),
            Asked("amount", "1234567890123457"),
            Asked("day", "2026-10-26"),
        ];

        var read = await Entries.GetTakenAnswersAsync(form.Id, [.. taken, .. free, taken[0]]);

        Assert.Multiple(() =>
        {
            Assert.That(taken.Select(value => value.Number ?? (object?)value.Date ?? value.Text), Has.Exactly(1).InstanceOf<double>().And.Exactly(1).InstanceOf<DateTime>(), "a number and a date by their columns, a longer number by its text");
            Assert.That(read, Is.EquivalentTo(taken), "letter case and surrounding spaces apart; spam, rejected, drafts, tests, other fields and other forms hold none; wildcards match only themselves");
        });
    }

    [Test]
    public async Task EveryWriteOfAnEntry_ChangesWhetherItHoldsItsAnswers_AndTheUnitLeavesItsOwnEntryOut()
    {
        var form = await CreateUniqueFormAsync();
        var entry = await AddAsync(form, EntryState.Submitted, ("email", "ada@example.com"));
        var ada = Asked("email", "Ada@example.com");
        var steps = new List<(string Step, bool Taken)>();
        async Task StepAsync(string step) => steps.Add((step, (await Entries.GetTakenAnswersAsync(form.Id, [ada])).Contains(ada)));

        await StepAsync("submitted");
        await Entries.ChangeStateAsync(entry, EntryState.Submitted, EntryState.Rejected, Editor);
        await StepAsync("rejecting gives it back");
        await Entries.ChangeStateAsync(entry, EntryState.Rejected, EntryState.Approved, Editor);
        await StepAsync("approving takes it again");
        await Entries.ChangeStateAsync(entry, EntryState.Approved, EntryState.Spam, Editor);
        await StepAsync("marking as spam gives it back");
        await Entries.ChangeStateAsync(entry, EntryState.Spam, EntryState.Submitted, Editor);
        await StepAsync("back from spam takes it again");
        await Entries.ReplaceValuesAsync(entry, [form["email"]], [], string.Empty);
        await StepAsync("erasing the value gives it back");
        await Entries.ReplaceValuesAsync(entry, [form["email"]], [Row(form, "email", "ada@example.com")], string.Empty);
        await StepAsync("the value again takes it");
        await Entries.ReplaceValuesAsync(entry, [form["email"]], [Row(form, "email", "bob@example.com")], string.Empty);
        await StepAsync("a changed value gives it back");
        await Entries.ReplaceValuesAsync(entry, [form["email"]], [Row(form, "email", "ada@example.com")], string.Empty);
        var alone = await Entries.FindTakenAnswersAsync(entry, [ada]);
        var second = await AddAsync(form, EntryState.Submitted, ("email", " ADA@example.com"));
        var both = await Entries.FindTakenAnswersAsync(entry, [ada]);
        await Entries.DeleteAsync(entry);
        await StepAsync("deleting one keeps the other's");
        await Entries.DeleteAsync([second]);
        await StepAsync("deleting several gives it back");

        Assert.Multiple(() =>
        {
            Assert.That(steps, Is.EqualTo(new[]
            {
                ("submitted", true),
                ("rejecting gives it back", false),
                ("approving takes it again", true),
                ("marking as spam gives it back", false),
                ("back from spam takes it again", true),
                ("erasing the value gives it back", false),
                ("the value again takes it", true),
                ("a changed value gives it back", false),
                ("deleting one keeps the other's", true),
                ("deleting several gives it back", false),
            }));
            Assert.That(alone, Is.Empty, "an entry does not take its own answer");
            Assert.That(both, Is.EquivalentTo(new[] { ada }), "another entry's answer is taken");
        });
    }

    [Test]
    public async Task AKeyThatMayBeCut_ComparesTheWholeTexts()
    {
        var form = await CreateUniqueFormAsync();
        var start = new string('a', EntryTextKey.MaxLength + 40);
        var entry = await AddAsync(form, EntryState.Submitted, ("note", start + "1"));
        var other = await AddAsync(form, EntryState.Submitted, ("note", start + "3"));
        var same = Asked("note", " " + start.ToUpperInvariant() + "1");
        var differs = Asked("note", start + "2");

        var read = await Entries.GetTakenAnswersAsync(form.Id, [same, differs]);
        var found = await Entries.FindTakenAnswersAsync(other, [same, differs, Asked("note", start + "3")]);
        var own = await Entries.FindTakenAnswersAsync(entry, [same]);

        Assert.Multiple(() =>
        {
            Assert.That(EntryTextKey.MayBeCut(EntryTextKey.For(start + "1")), Is.True, "the texts share a cut key");
            Assert.That(read, Is.EquivalentTo(new[] { same }));
            Assert.That(found, Is.EquivalentTo(new[] { same }), "the unit compares whole texts too, and leaves its own out");
            Assert.That(own, Is.Empty);
        });
    }

    [Test]
    public async Task TheUnitsRead_LocksTheFormsRowFirst()
    {
        var form = await CreateUniqueFormAsync();
        var entry = await AddAsync(form, EntryState.Submitted, ("email", "ada@example.com"));
        var log = new CommandLog();

        var taken = await WatchedAsync(log, () => Entries.FindTakenAnswersAsync(entry, [Asked("email", "bob@example.com"), Asked("note", new string('n', 300))]));

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.Empty);
            Assert.That(log.Texts[0], Does.StartWith("UPDATE " + T(FormBuilderTables.Form)).And.Contain(Q("lastEntryNumber")), "the form's row, which every write that changes whose answers count locks too");
            Assert.That(log.Texts, Has.Count.EqualTo(4), "the lock, the form, and one statement per value");
        });
    }

    [Test]
    public async Task NoValues_ReadNothing_AnEntryThatDoesNotExist_HoldsNone_AndAValueThatCannotBeCompared_IsRefused()
    {
        var form = await CreateUniqueFormAsync();
        await AddAsync(form, EntryState.Submitted, ("email", "ada@example.com"));
        var log = new CommandLog();
        var ada = Asked("email", "ada@example.com");

        var none = await WatchedAsync(log, async () => (await Entries.GetTakenAnswersAsync(form.Id, [])).Count + (await Entries.FindTakenAnswersAsync(1, [])).Count);

        Assert.Multiple(async () =>
        {
            Assert.That(none, Is.Zero);
            Assert.That(log.Texts, Is.Empty, "no values, no statement");
            Assert.That(await Entries.FindTakenAnswersAsync(long.MaxValue, [ada]), Is.Empty);
            Assert.That(await Entries.GetTakenAnswersAsync(int.MaxValue, [ada]), Is.Empty);
            foreach (var value in new AnswerValue[]
            {
                new(Guid.Empty, "a", null, null),
                new(Email, null, null, null),
                new(Email, "a", 1, null),
                new(Email, null, 1, DateTime.UnixEpoch),
                new(Email, string.Empty, null, null),
                new(Email, null, double.NaN, null),
                new(Email, null, double.PositiveInfinity, null),
            })
            {
                Assert.That(() => Entries.GetTakenAnswersAsync(form.Id, [value]), Throws.ArgumentException, value.ToString());
                Assert.That(() => Entries.FindTakenAnswersAsync(1, [value]), Throws.ArgumentException, value.ToString());
            }
        });
    }

    // A form with a field of each storage kind that compares, a repeatable group's child and a text field without a check.
    private async Task<TestForm> CreateUniqueFormAsync()
    {
        var form = await CreateFormAsync();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (alias, key, storage, parent) in Fields)
        {
            var field = new CatalogueFieldValues(key, alias, alias, alias, storage, false, ids.Count, null, null, parent);
            ids[alias] = (await Forms.AddFieldAsync(form.Id, field))!.Id;
        }

        return new TestForm(form.Id, form.DraftVersionId!.Value, ids);
    }

    private async Task<long> AddAsync(TestForm form, EntryState state, params (string Field, string Text)[] values) =>
        (await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, [.. values.Select(value => Row(form, value.Field, value.Text))]) with { State = state }))!.Id;

    // The row a submission stores for a value of the field.
    private static NewEntryValue Row(TestForm form, string alias, string text, int rowIndex = 0) =>
        EntryValueColumns.Of(Storage(alias), new FieldValue(text), form[alias], rowIndex: rowIndex);

    // The value a submission asks about for an answer to the field.
    private static AnswerValue Asked(string alias, string text) =>
        EntryValueColumns.ComparedBy(Storage(alias), Fields.Single(field => field.Alias == alias).Key, new FieldValue(text));

    private static FieldStorageKind Storage(string alias) => Fields.Single(field => field.Alias == alias).Storage;
}
