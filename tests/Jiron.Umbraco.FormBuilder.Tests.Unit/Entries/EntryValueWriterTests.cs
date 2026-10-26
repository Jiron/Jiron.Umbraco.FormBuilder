using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.Entries.EntryTestForm;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The rows an entry's answers are stored as (ADR-0070), for every built-in type at its edges: each value at its field's
/// catalogue row, or a grid's at its row's, with its position among the field's values and its repeatable group's row;
/// a file with the upload it names, a ticked consent with its wording; and what the writer refuses, by name only.
/// </summary>
public sealed class EntryValueWriterTests
{
    // A value of each type that stores text, at its edges: the longest answer of a text and a long text, the forms
    // reading keeps as given, and a hidden field's server value.
    public static IEnumerable<TestCaseData> Texts()
    {
        static TestCaseData Case(Guid field, string text, string name) => new TestCaseData(field, text).SetName($"AText_IsStoredAsRead({name})");

        yield return Case(FullName, "Ada", "text");
        yield return Case(FullName, new string('x', 4000), "text, the longest");
        yield return Case(Message, "Line one" + (char)10 + "line two", "textarea, two lines");
        yield return Case(Message, new string('x', 100000), "textarea, the longest");
        yield return Case(Email, "ada@example.org", "email");
        yield return Case(Phone, "+41 44 668 18 00", "phone");
        yield return Case(Website, "https://example.org/a?b=c", "url");
        yield return Case(Arrival, "09:30", "time");
        yield return Case(Arrival, "23:59:59", "time with seconds");
        yield return Case(Returning, "yes", "yesno");
        yield return Case(Country, "CH", "country");
        yield return Case(Campaign, "spring", "hidden");
        yield return Case(Size, "b", "radio");
    }

    [TestCaseSource(nameof(Texts))]
    public void AText_IsStoredAsRead(Guid field, string text)
    {
        var rows = Of(Answer(field, text));

        Assert.That(rows.Values, Is.EqualTo(new[] { new NewEntryValue(IdOf(field), text) }));
    }

    [TestCase("0")]
    [TestCase("-12.5")]
    [TestCase("123456789012345")]
    [TestCase("-0.000000000000001")]
    public void ANumber_FillsTheNumber_ForItsFifteenDigits(string text)
    {
        var rows = Of(Answer(Age, text), Answer(Stars, "5"), Answer(Score, "10"), Answer(Volume, "-3.5"));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values[0], Is.EqualTo(new NewEntryValue(IdOf(Age), text, Number: double.Parse(text, CultureInfo.InvariantCulture))));
            Assert.That(rows.Values.Skip(1).Select(row => (row.FieldId, row.Text, row.Number)), Is.EqualTo(new[] { (IdOf(Stars), (string?)"5", (double?)5), (IdOf(Score), "10", 10), (IdOf(Volume), "-3.5", -3.5) }), "a rating, a scale and a slider");
        });
    }

    [TestCase("1234567890123456")]
    [TestCase("12345678901234567890.123456789")]
    public void ANumberPastFifteenDigits_IsTextOnly_SoNoReaderTakesItRounded(string text)
    {
        // The pipeline refuses a typed one (digits, ADR-0069); a calculation's longer result is the server's (5.4c).
        var rows = Of(new EntryFieldAnswer(Total, [new FieldValue(text)]));

        Assert.That(rows.Values, Is.EqualTo(new[] { new NewEntryValue(IdOf(Total), text) }));
    }

    [Test]
    public void ADate_FillsTheDate_WhereBothDatabasesStoreIt()
    {
        var rows = Of(Answer(Birthday, "1753-01-01"), Answer(Visit, "9999-12-31T00:00"));
        var outside = Of(Answer(Birthday, "1752-12-31"), Answer(Visit, "9999-12-31T00:00:01"));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values.Select(row => (row.Text, row.Date)), Is.EqualTo(new[]
            {
                ("1753-01-01", (DateTime?)new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                ("9999-12-31T00:00", new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc)),
            }));
            Assert.That(outside.Values.Select(row => (row.Text, row.Date)), Is.EqualTo(new[] { ("1752-12-31", (DateTime?)null), ("9999-12-31T00:00:01", null) }), "text only, read back from the text");
        });
    }

    [Test]
    public void ATickedBox_FillsTheBool_AndOneLeftEmptyStoresNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Of(Answer(Newsletter, "true")).Values, Is.EqualTo(new[] { new NewEntryValue(IdOf(Newsletter), "true", Bool: true) }));
            Assert.That(Of(Answer(Newsletter)).Values, Is.Empty);
        });
    }

    [Test]
    public void SeveralChoices_KeepTheirOrder_WithTheOtherTextInItsPlace()
    {
        var rows = Of(
            Answer(Fruit, new FieldAnswers(["b", "a"], [Part("other", "Quince")])),
            Answer(Toppings, "ham", "olives"),
            Answer(Size, new FieldAnswers([], [Part("other", "Huge")])));

        Assert.That(rows.Values.Select(row => (row.FieldId, row.Text, row.ValueIndex)), Is.EqualTo(new[]
        {
            (IdOf(Fruit), (string?)"b", 0),
            (IdOf(Fruit), "a", 1),
            (IdOf(Fruit), "Quince", 2),
            (IdOf(Toppings), "ham", 0),
            (IdOf(Toppings), "olives", 1),
            (IdOf(Size), "Huge", 0),
        }));
    }

    [Test]
    public void ARanking_StoresItsRankOrder_WithTheGapsClosed()
    {
        var dragged = Of(Answer(Priorities, "c", "a", "b"));
        var numbered = Of(Answer(Priorities, new FieldAnswers([], [Part("a", "10"), Part("b", "2"), Part("c", "7")])));

        Assert.Multiple(() =>
        {
            Assert.That(dragged.Values.Select(row => (row.Text, row.ValueIndex)), Is.EqualTo(new[] { ((string?)"c", 0), ("a", 1), ("b", 2) }));
            Assert.That(numbered.Values.Select(row => (row.Text, row.ValueIndex)), Is.EqualTo(new[] { ((string?)"b", 0), ("c", 1), ("a", 2) }), "a form without JavaScript");
        });
    }

    [Test]
    public void AGrid_StoresEachRowsColumns_AtTheRowsPlace_AndNothingOfItsOwn()
    {
        var single = Of(Answer(Satisfaction, new FieldAnswers([], [Part(Price.ToString("D"), "bad"), Part(Speed.ToString("D"), "good")])));
        var several = Of(Answer(Channels, new FieldAnswers([], [Part(Mail.ToString("D"), "weekly", "monthly"), Part(Call.ToString("D"), "never")])));

        Assert.Multiple(() =>
        {
            Assert.That(single.Values, Is.EqualTo(new[] { new NewEntryValue(IdOf(Speed), "good"), new NewEntryValue(IdOf(Price), "bad") }), "in the grid's order of rows");
            Assert.That(several.Values.Select(row => (row.FieldId, row.Text, row.ValueIndex)), Is.EqualTo(new[] { (IdOf(Mail), (string?)"weekly", 0), (IdOf(Mail), "monthly", 1), (IdOf(Call), "never", 0) }), "each row from 0");
            Assert.That(single.Values.Concat(several.Values).Select(row => row.FieldId), Has.None.EqualTo(IdOf(Satisfaction)).And.None.EqualTo(IdOf(Channels)));
        });
    }

    [Test]
    public void AValueOfParts_IsItsObject_KeyedByThePartsTexts()
    {
        var rows = Of(
            Answer(Name, new FieldAnswers([], [Part("last", "Lovelace"), Part("first", "Ada")])),
            Answer(Address, new FieldAnswers([], [Part("street", "Main Street 1"), Part("postcode", "3000"), Part("city", "Bern")])));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values.Select(row => row.Text), Is.EqualTo(new[] { """{"first":"Ada","last":"Lovelace"}""", """{"street":"Main Street 1","postcode":"3000","city":"Bern"}""" }));
            Assert.That(rows.Values.Select(row => row.KeyText), Is.EqualTo(new[] { "Ada Lovelace", "Main Street 1 3000 Bern" }));
        });
    }

    [Test]
    public void Files_TakeTheUploadsTheirTokensName_InTheirOrder()
    {
        var first = Upload(3, Attachments);
        var second = Upload(4, Attachments);
        var drawn = Upload(5, Signature);
        var rows = Of(
            Answer(Attachments, "token-a", "token-b") with { Files = [first, second] },
            Answer(Signature, "token-c") with { Files = [drawn] });

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values, Is.EqualTo(new[]
            {
                new NewEntryValue(IdOf(Attachments), first.Key.ToString("D"), FileId: 3),
                new NewEntryValue(IdOf(Attachments), second.Key.ToString("D"), FileId: 4, ValueIndex: 1),
                new NewEntryValue(IdOf(Signature), drawn.Key.ToString("D"), FileId: 5),
            }));
            Assert.That(rows.FileIds, Is.EqualTo(new[] { 3, 4, 5 }));
        });
    }

    [Test]
    public void ATickedConsent_StoresTheHash_WithItsWordingOnce()
    {
        var wording = Wording();
        var rows = Of([Ticked(Terms, WordingHash), Ticked(Privacy, WordingHash)], new Dictionary<Guid, ConsentWording> { [Terms] = wording, [Privacy] = wording });

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values, Is.EqualTo(new[] { new NewEntryValue(IdOf(Terms), WordingHash), new NewEntryValue(IdOf(Privacy), WordingHash) }));
            Assert.That(rows.Wordings, Is.EqualTo(new[] { wording }), "two consents with one wording");
            Assert.That(Of(Answer(Terms)).Wordings, Is.Empty, "a consent not given needs no wording");
        });
    }

    [Test]
    public void ARepeatableGroupsChildren_StoreTheirRows()
    {
        var ticket = Upload(8, Ticket);
        var rows = Of(InRow(GuestName, 0, "Ada"), InRow(GuestName, 1, "Grace"), InRow(Ticket, 1, "token") with { Files = [ticket] });

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values, Is.EqualTo(new[]
            {
                new NewEntryValue(IdOf(GuestName), "Ada"),
                new NewEntryValue(IdOf(GuestName), "Grace", RowIndex: 1),
                new NewEntryValue(IdOf(Ticket), ticket.Key.ToString("D"), FileId: 8, RowIndex: 1),
            }));
            Assert.That(rows.Values.Select(row => row.FieldId), Has.None.EqualTo(IdOf(Guests)), "the group's own row stores nothing");
        });
    }

    [Test]
    public void NoAnswers_StoreNothing()
    {
        var rows = Of();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Values, Is.Empty);
            Assert.That(rows.FileIds, Is.Empty);
            Assert.That(rows.Wordings, Is.Empty);
        });
    }

    public static IEnumerable<TestCaseData> Refused()
    {
        static TestCaseData Case(string name, Func<EntryRows> write) => new TestCaseData(new TestDelegate(() => write())).SetName($"TheWriter_Refuses({name})");

        var catalogueWithoutAge = Catalogue.Where(field => field.Values.FieldKey != Age).ToList();
        var catalogueMovingSpeed = Catalogue.Select(field => field.Values.FieldKey == Speed ? field with { Values = field.Values with { ParentFieldKey = Channels } } : field).ToList();
        yield return Case("a null answer", () => Of([null!], new Dictionary<Guid, ConsentWording>()));
        yield return Case("a field the version lacks", () => Of(new EntryFieldAnswer(Key(999), [new FieldValue("x")])));
        yield return Case("two answers in one row", () => Of(Answer(FullName, "Ada"), Answer(FullName, "Grace")));
        yield return Case("a row outside a repeatable group", () => Of(InRow(FullName, 1, "Ada")));
        yield return Case("a field the catalogue lacks", () => EntryValueWriter.Of(FormId, Definition, catalogueWithoutAge, TestFieldTypes.Registry, [Answer(Age, "5")], new Dictionary<Guid, ConsentWording>()));
        yield return Case("a row the grid lacks", () => Of(new EntryFieldAnswer(Satisfaction, [new FieldValue("good", Mail)])));
        yield return Case("a row of no grid", () => Of(new EntryFieldAnswer(Satisfaction, [new FieldValue("good", Key(998))])));
        yield return Case("a row the catalogue keeps under another grid", () => EntryValueWriter.Of(FormId, Definition, catalogueMovingSpeed, TestFieldTypes.Registry, [new EntryFieldAnswer(Satisfaction, [new FieldValue("good", Speed)])], new Dictionary<Guid, ConsentWording>()));
        yield return Case("a grid row's value on another field", () => Of(new EntryFieldAnswer(FullName, [new FieldValue("good", Speed)])));
        yield return Case("a value its kind does not hold", () => Of(new EntryFieldAnswer(Age, [new FieldValue("twelve")])));
        yield return Case("a file without its upload", () => Of(Answer(Attachments, "a", "b") with { Files = [Upload(3, Attachments)] }));
        yield return Case("an upload of another form", () => Of(Answer(Attachments, "a") with { Files = [Upload(3, Attachments, formId: 8)] }));
        yield return Case("an upload of another field", () => Of(Answer(Attachments, "a") with { Files = [Upload(3, Signature)] }));
        yield return Case("an upload another entry took", () => Of(Answer(Attachments, "a") with { Files = [Upload(3, Attachments, entryId: 12)] }));
        yield return Case("an upload named twice", () => Of(Answer(Attachments, "a", "b") with { Files = [Upload(3, Attachments), Upload(3, Attachments)] }));
        yield return Case("an upload named in two rows", () => Of(InRow(Ticket, 0, "a") with { Files = [Upload(3, Ticket)] }, InRow(Ticket, 1, "b") with { Files = [Upload(3, Ticket)] }));
        yield return Case("an upload of a field that stores no files", () => Of(Answer(FullName, "Ada") with { Files = [Upload(3, FullName)] }));
        yield return Case("a consent without its wording", () => Of(Ticked(Terms, WordingHash)));
        yield return Case("a consent with another wording", () => Of([Ticked(Terms, WordingHash)], new Dictionary<Guid, ConsentWording> { [Terms] = new("en-GB", "Another text.") }));
        yield return Case("a consent with the wording of another culture", () => Of([Ticked(Terms, WordingHash)], new Dictionary<Guid, ConsentWording> { [Terms] = new("de-CH", Wording().Text) }));
    }

    [TestCaseSource(nameof(Refused))]
    public void TheWriter_Refuses(TestDelegate write) => Assert.That(write, Throws.ArgumentException);

    [Test]
    public void ARefusal_NamesTheField_AndNoValue()
    {
        const string Secret = "my-secret-answer";

        var refused = Assert.Throws<ArgumentException>(() => Of(new EntryFieldAnswer(Age, [new FieldValue(Secret)])))!;
        var twice = Assert.Throws<ArgumentException>(() => Of(Answer(FullName, Secret), Answer(FullName, Secret)))!;

        Assert.Multiple(() =>
        {
            Assert.That(refused.Message, Does.Not.Contain(Secret));
            Assert.That(twice.Message, Does.Contain(FullName.ToString()).And.Not.Contain(Secret));
        });
    }

    [Test]
    public void AnAnswer_RefusesAnEmptyKey_ANegativeRow_AndNullValuesOrUploads()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new EntryFieldAnswer(Guid.Empty, []), Throws.ArgumentException);
            Assert.That(() => new EntryFieldAnswer(FullName, [], row: -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EntryFieldAnswer(FullName, [null!]), Throws.ArgumentException);
            Assert.That(() => new EntryFieldAnswer(FullName, null!), Throws.ArgumentNullException);
            Assert.That(() => new EntryFieldAnswer(FullName, []) { Files = [null!] }, Throws.ArgumentException);
            Assert.That(() => new EntryFieldAnswer(FullName, []) { Files = default }, Throws.ArgumentException);
            Assert.That(new EntryFieldAnswer(FullName, [new FieldValue("Ada")], row: 2), Has.Property(nameof(EntryFieldAnswer.Row)).EqualTo(2).And.Property(nameof(EntryFieldAnswer.Files)).Empty);
        });
    }

    private static EntryFieldAnswer InRow(Guid field, int row, string text) => Answer(field, new FieldAnswers([text]), row);

    private static EntryRows Of(params EntryFieldAnswer[] answers) => Of(answers, new Dictionary<Guid, ConsentWording>());

    private static EntryRows Of(EntryFieldAnswer[] answers, Dictionary<Guid, ConsentWording> wordings) =>
        EntryValueWriter.Of(FormId, Definition, Catalogue, TestFieldTypes.Registry, answers, wordings);
}
