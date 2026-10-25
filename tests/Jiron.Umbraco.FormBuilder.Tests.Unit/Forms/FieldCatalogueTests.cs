using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The field catalogue a publish keeps (ADR-0003, ADR-0044): a row per field that holds a value, per repeater child,
/// per grid row and for the grid or repeater itself, in document order, each with how its values are stored; and the
/// rows a publish adds, brings back, ends or leaves alone.
/// </summary>
[TestFixture]
public sealed class FieldCatalogueTests
{
    // Every built-in type and how its values are stored; content, the captcha, grids and repeaters hold none of their own.
    private static readonly Dictionary<string, FieldStorageKind> Storage = new(StringComparer.Ordinal)
    {
        ["text"] = FieldStorageKind.Text,
        ["textarea"] = FieldStorageKind.LongText,
        ["email"] = FieldStorageKind.Text,
        ["phone"] = FieldStorageKind.Text,
        ["url"] = FieldStorageKind.Text,
        ["number"] = FieldStorageKind.Number,
        ["date"] = FieldStorageKind.Date,
        ["time"] = FieldStorageKind.Text,
        ["datetime"] = FieldStorageKind.Date,
        ["rating"] = FieldStorageKind.Number,
        ["scale"] = FieldStorageKind.Number,
        ["slider"] = FieldStorageKind.Number,
        ["calculation"] = FieldStorageKind.Number,
        ["dropdown"] = FieldStorageKind.Text,
        ["radio"] = FieldStorageKind.Text,
        ["checkboxes"] = FieldStorageKind.Text,
        ["grid"] = FieldStorageKind.None,
        ["ranking"] = FieldStorageKind.Text,
        ["checkbox"] = FieldStorageKind.Bool,
        ["yesno"] = FieldStorageKind.Text,
        ["consent"] = FieldStorageKind.Text,
        ["country"] = FieldStorageKind.Text,
        ["name"] = FieldStorageKind.Json,
        ["address"] = FieldStorageKind.Json,
        ["file"] = FieldStorageKind.File,
        ["signature"] = FieldStorageKind.File,
        ["repeater"] = FieldStorageKind.None,
        ["hidden"] = FieldStorageKind.Text,
        ["heading"] = FieldStorageKind.None,
        ["paragraph"] = FieldStorageKind.None,
        ["divider"] = FieldStorageKind.None,
        ["image"] = FieldStorageKind.None,
        ["captcha"] = FieldStorageKind.None,
    };

    private static readonly Guid Email = Key(1);
    private static readonly Guid Grid = Key(2);
    private static readonly Guid Speed = Key(3);
    private static readonly Guid Price = Key(4);
    private static readonly Guid Guests = Key(5);
    private static readonly Guid GuestName = Key(6);
    private static readonly Guid Ticket = Key(7);
    private static readonly Guid Consent = Key(8);
    private static readonly Guid Age = Key(9);
    private static readonly Guid Visit = Key(10);
    private static readonly Guid Newsletter = Key(11);

    [Test]
    public void EachBuiltInType_StoresItsValuesAsWhatItHolds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TestFieldTypes.Registry.All.Select(type => type.Alias).Order(StringComparer.Ordinal), Is.EqualTo(Storage.Keys.Order(StringComparer.Ordinal)));
            foreach (var type in TestFieldTypes.Registry.All)
            {
                Assert.That(type.Storage, Is.EqualTo(Storage[type.Alias]), type.Alias);
            }
        });
    }

    [TestCase("None", ExpectedResult = "none")]
    [TestCase("Text", ExpectedResult = "text")]
    [TestCase("LongText", ExpectedResult = "longText")]
    [TestCase("Number", ExpectedResult = "number")]
    [TestCase("Date", ExpectedResult = "date")]
    [TestCase("Bool", ExpectedResult = "bool")]
    [TestCase("File", ExpectedResult = "file")]
    [TestCase("Json", ExpectedResult = "json")]
    public string AStorageKind_IsStoredByItsName(string storage) => StoredNames.Of(Enum.Parse<FieldStorageKind>(storage));

    [Test]
    public void EveryStorageKind_HasAStoredName_OfAtMost16Characters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<FieldStorageKind>().Select(StoredNames.Of), Is.EquivalentTo(new[] { "text", "longText", "number", "date", "bool", "file", "json", "none" }), "the kinds of section 4.3.2");
            Assert.That(Enum.GetValues<FieldStorageKind>().Select(StoredNames.Of), Is.Unique.And.All.Length.InRange(1, 16));
            Assert.That(() => StoredNames.Of((FieldStorageKind)99), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ADefinition_HasARowPerValue_InDocumentOrder()
    {
        Assert.That(FieldCatalogue.Of(Definition(), TestFieldTypes.Registry), Is.EqualTo(new[]
        {
            new CatalogueEntry(Email, "email", "Email", "email", FieldStorageKind.Text, true, null),
            new CatalogueEntry(Grid, "satisfaction", "Satisfaction", "grid", FieldStorageKind.None, false, null),
            new CatalogueEntry(Speed, "satisfaction", "Speed", "grid", FieldStorageKind.Text, false, Grid),
            new CatalogueEntry(Price, "satisfaction", string.Empty, "grid", FieldStorageKind.Text, false, Grid),
            new CatalogueEntry(Guests, "guests", "Guests", "repeater", FieldStorageKind.None, false, null),
            new CatalogueEntry(GuestName, "guestName", "Name", "text", FieldStorageKind.Text, false, Guests),
            new CatalogueEntry(Ticket, "ticket", string.Empty, "file", FieldStorageKind.File, false, Guests),
            new CatalogueEntry(Consent, "consent", "Terms", "consent", FieldStorageKind.Text, false, null),
            new CatalogueEntry(Age, "age", "Age", "number", FieldStorageKind.Number, false, null),
            new CatalogueEntry(Visit, "visit", "Visit", "datetime", FieldStorageKind.Date, false, null),
            new CatalogueEntry(Newsletter, "newsletter", "News", "checkbox", FieldStorageKind.Bool, false, null),
        }));
    }

    [Test]
    public void TheFirstPublish_AddsEveryRow_FromItsVersion()
    {
        var entries = FieldCatalogue.Of(Definition(), TestFieldTypes.Registry);

        var changes = FieldCatalogue.Changes(entries, [], 3, null);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => change.Values.FieldKey), Is.EqualTo(entries.Select(entry => entry.FieldKey)));
            Assert.That(changes.Select(change => change.IsNew), Is.All.True);
            Assert.That(changes.Select(change => change.Values.SortOrder), Is.EqualTo(Enumerable.Range(0, entries.Count)));
            Assert.That(changes.Select(change => (change.Values.FirstVersionNumber, change.Values.LastVersionNumber)), Is.All.EqualTo(((int?)3, (int?)null)));
            Assert.That(changes[0].Values, Is.EqualTo(new CatalogueFieldValues(Email, "email", "Email", "email", FieldStorageKind.Text, true, 0, 3, null, null)));
            Assert.That(changes[2].Values.ParentFieldKey, Is.EqualTo(Grid));
        });
    }

    [Test]
    public void ARepublish_OfTheSameFields_ChangesNoRow()
    {
        var entries = FieldCatalogue.Of(Definition(), TestFieldTypes.Registry);
        var stored = Stored(FieldCatalogue.Changes(entries, [], 1, null));

        Assert.That(FieldCatalogue.Changes(entries, stored, 4, 1), Is.Empty);
    }

    [Test]
    public void ARowWhoseLabelPlaceOrTypeChanged_IsUpdated_KeepingItsFirstVersion()
    {
        var stored = Stored(FieldCatalogue.Changes(Entries(Row(Email, "email"), Row(Age, "age")), [], 1, null));
        var entries = Entries(Row(Age, "age"), Row(Email, "email") with { Label = "E-mail", Type = "email" });

        var changes = FieldCatalogue.Changes(entries, stored, 2, 1);

        Assert.That(changes, Is.EqualTo(new[]
        {
            (new CatalogueFieldValues(Age, "age", "AGE", "text", FieldStorageKind.Text, false, 0, 1, null, null), false),
            (new CatalogueFieldValues(Email, "email", "E-mail", "email", FieldStorageKind.Text, false, 1, 1, null, null), false),
        }));
    }

    [Test]
    public void ARowAPublishDropped_EndsAtTheLastPublishThatHadIt()
    {
        var stored = Stored(FieldCatalogue.Changes(Entries(Row(Email, "email"), Row(Age, "age")), [], 1, null));

        var changes = FieldCatalogue.Changes(Entries(Row(Email, "email")), stored, 5, 3);

        Assert.That(changes, Is.EqualTo(new[] { (new CatalogueFieldValues(Age, "age", "AGE", "text", FieldStorageKind.Text, false, 1, 1, 3, null), false) }));
    }

    [Test]
    public void ARowDroppedWithoutAnEarlierPublish_EndsAtItsFirstVersion()
    {
        var stored = Stored(FieldCatalogue.Changes(Entries(Row(Age, "age")), [], 2, null));

        Assert.That(FieldCatalogue.Changes([], stored, 4, null).Single().Values.LastVersionNumber, Is.EqualTo(2));
    }

    [Test]
    public void ARowDroppedBefore_StaysAsItIs_UntilItComesBack()
    {
        var ended = Stored([(Values(Age, "age", sortOrder: 0, first: 1, last: 2), true)]);

        var absent = FieldCatalogue.Changes(Entries(Row(Email, "email")), ended, 5, 4);
        var back = FieldCatalogue.Changes(Entries(Row(Age, "age")), ended, 5, 4);

        Assert.Multiple(() =>
        {
            Assert.That(absent.Select(change => change.Values.FieldKey), Is.EqualTo(new[] { Email }), "the ended row needs no change");
            Assert.That(back, Is.EqualTo(new[] { (Values(Age, "age", sortOrder: 0, first: 1, last: null), false) }), "live again, from its first version");
        });
    }

    [Test]
    public void ARowOnlyADraftHas_StaysAsItIs_UntilAPublishHasIt()
    {
        var draftOnly = Stored([(Values(Age, "age", sortOrder: 0, first: null, last: null), true)]);

        var without = FieldCatalogue.Changes(Entries(Row(Email, "email")), draftOnly, 3, null);
        var with = FieldCatalogue.Changes(Entries(Row(Age, "age")), draftOnly, 3, null);

        Assert.Multiple(() =>
        {
            Assert.That(without.Select(change => change.Values.FieldKey), Is.EqualTo(new[] { Email }), "the row of the preview entries stays");
            Assert.That(with, Is.EqualTo(new[] { (Values(Age, "age", sortOrder: 0, first: 3, last: null), false) }), "published from this version");
        });
    }

    [Test]
    public void AFieldOfATypeTheSiteLacks_OrWithoutAnAlias_OrWithoutAValue_HasNoRow()
    {
        var definition = Single(
            new FormField { Key = Key(20), Type = "custom", Alias = "custom" },
            new FormField { Key = Key(21), Type = "text" },
            new FormField { Key = Key(22), Type = "heading" },
            new FormField { Key = Key(23), Type = "heading", Alias = "title" });

        Assert.That(FieldCatalogue.Of(definition, TestFieldTypes.Registry), Is.Empty);
    }

    [Test]
    public void TheCatalogue_NeedsItsArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FieldCatalogue.Of(null!, TestFieldTypes.Registry), Throws.ArgumentNullException);
            Assert.That(() => FieldCatalogue.Of(Definition(), null!), Throws.ArgumentNullException);
            Assert.That(() => FieldCatalogue.Changes(null!, [], 1, null), Throws.ArgumentNullException);
            Assert.That(() => FieldCatalogue.Changes([], null!, 1, null), Throws.ArgumentNullException);
        });
    }

    private static Guid Key(int number) => Guid.Parse($"50000000-0000-4000-8000-{number:D12}");

    private static LocalizedText Label(string text) => new(text);

    // Two pages: a grid with a labelled and an unlabelled row, a repeater with two children and a heading on the first;
    // a number, a date with a time and a checkbox on the second.
    private static FormDefinition Definition() => new()
    {
        Key = Key(100),
        Pages =
        [
            new FormPage
            {
                Key = Key(101),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(102),
                        Fields =
                        [
                            new FormField { Key = Email, Type = "email", Alias = "email", Label = Label("Email"), Sensitive = true },
                            new FormField { Key = Key(103), Type = "heading" },
                            new FormField
                            {
                                Key = Grid,
                                Type = "grid",
                                Alias = "satisfaction",
                                Label = Label("Satisfaction"),
                                Rows = [new GridRow { Key = Speed, Label = Label("Speed") }, new GridRow { Key = Price }],
                            },
                            new FormField
                            {
                                Key = Guests,
                                Type = "repeater",
                                Alias = "guests",
                                Label = Label("Guests"),
                                Fields =
                                [
                                    new FormField { Key = GuestName, Type = "text", Alias = "guestName", Label = Label("Name") },
                                    new FormField { Key = Ticket, Type = "file", Alias = "ticket" },
                                ],
                            },
                            new FormField { Key = Consent, Type = "consent", Alias = "consent", Label = Label("Terms") },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = Key(104),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(105),
                        Fields =
                        [
                            new FormField { Key = Age, Type = "number", Alias = "age", Label = Label("Age") },
                            new FormField { Key = Visit, Type = "datetime", Alias = "visit", Label = Label("Visit") },
                            new FormField { Key = Newsletter, Type = "checkbox", Alias = "newsletter", Label = Label("News") },
                        ],
                    },
                ],
            },
        ],
    };

    private static FormDefinition Single(params FormField[] fields) => new()
    {
        Key = Key(100),
        Pages = [new FormPage { Key = Key(101), Groups = [new FormGroup { Key = Key(102), Fields = fields }] }],
    };

    private static CatalogueEntry Row(Guid key, string alias) => new(key, alias, alias.ToUpperInvariant(), "text", FieldStorageKind.Text, false, null);

    private static CatalogueEntry[] Entries(params CatalogueEntry[] entries) => entries;

    private static CatalogueFieldValues Values(Guid key, string alias, int sortOrder, int? first, int? last) =>
        new(key, alias, alias.ToUpperInvariant(), "text", FieldStorageKind.Text, false, sortOrder, first, last, null);

    // The rows as a publish stored them.
    private static List<CatalogueField> Stored(IReadOnlyList<(CatalogueFieldValues Values, bool IsNew)> changes) =>
        changes.Select((change, index) => new CatalogueField(index + 1, 7, change.Values)).ToList();
}
