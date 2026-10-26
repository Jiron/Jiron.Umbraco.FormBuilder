using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.Entries.EntryTestForm;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// A stored entry's answers read back typed (ADR-0070): every type's values from their stored text, a grid's by its
/// rows, a repeatable group's children by row, a file with its upload and a consent with its wording, each value also
/// as its type read it; and what reads as text alone, by storage alone or not at all.
/// </summary>
public sealed class EntryValueReaderTests
{
    private const long EntryId = 41;

    private static readonly StoredConsentText StoredWording = new(WordingHash, "en-GB", Wording().Text, DateTimeOffset.UnixEpoch);

    [Test]
    public void TheTexts_ReadBackAsTheyWereRead()
    {
        var answers = RoundTrip(
            Answer(FullName, "Ada"),
            Answer(Message, "Line one" + (char)10 + "line two"),
            Answer(Returning, "yes"),
            Answer(Fruit, new FieldAnswers(["b", "a"], [Part("other", "Quince")])),
            Answer(Priorities, new FieldAnswers([], [Part("a", "10"), Part("b", "2"), Part("c", "7")])));

        Assert.Multiple(() =>
        {
            Assert.That(Texts(answers, FullName), Is.EqualTo(new[] { "Ada" }));
            Assert.That(Texts(answers, Message), Is.EqualTo(new[] { "Line one" + (char)10 + "line two" }));
            Assert.That(Texts(answers, Returning), Is.EqualTo(new[] { "yes" }));
            Assert.That(Texts(answers, Fruit), Is.EqualTo(new[] { "b", "a", "Quince" }), "the Other text in its place");
            Assert.That(Texts(answers, Priorities), Is.EqualTo(new[] { "b", "c", "a" }), "the rank order");
            Assert.That(answers.All.SelectMany(answer => answer.Values), Has.All.Matches<EntryAnswerValue>(Untyped), "text has no typed value");
        });
    }

    public static IEnumerable<TestCaseData> Numbers()
    {
        yield return new TestCaseData("0", 0m);
        yield return new TestCaseData("-12.5", -12.5m);
        yield return new TestCaseData("123456789012345", 123456789012345m);
        yield return new TestCaseData("-0.000000000000001", -0.000000000000001m);
        yield return new TestCaseData("999999999999999", 999999999999999m);
        yield return new TestCaseData("0.999999999999999", 0.999999999999999m);
    }

    [TestCaseSource(nameof(Numbers))]
    public void ANumber_ReadsBackExactly_ForItsFifteenDigits(string text, decimal number)
    {
        var value = Single(RoundTrip(Answer(Age, text)), Age);

        Assert.Multiple(() =>
        {
            Assert.That(value.Number, Is.EqualTo(number));
            Assert.That(value.Value, Is.EqualTo(new FieldValue(text)));
        });
    }

    [Test]
    public void ALongerNumber_ReadsBackAsItsTextAlone()
    {
        const string Text = "12345678901234567890.123456789";

        var value = Single(RoundTrip(new EntryFieldAnswer(Total, [new FieldValue(Text)])), Total);

        Assert.Multiple(() =>
        {
            Assert.That(value.Number, Is.Null);
            Assert.That(value.Text, Is.EqualTo(Text), "never rounded");
        });
    }

    [Test]
    public void ADate_ReadsBackFromItsText_EvenWhereTheDatabaseKeepsNoDate()
    {
        var answers = RoundTrip(Answer(Birthday, "1752-12-31"), Answer(Visit, "0001-01-01T00:00"));
        var later = RoundTrip(Answer(Birthday, "2026-10-26"), Answer(Visit, "9999-12-31T23:59:59"));

        Assert.Multiple(() =>
        {
            Assert.That(Single(answers, Birthday).Date, Is.EqualTo(new DateOnly(1752, 12, 31)));
            Assert.That(Single(answers, Visit).DateTime, Is.EqualTo(DateTime.MinValue));
            Assert.That(Single(later, Birthday).Date, Is.EqualTo(new DateOnly(2026, 10, 26)));
            Assert.That(Single(later, Visit).DateTime, Is.EqualTo(new DateTime(9999, 12, 31, 23, 59, 59)));
            Assert.That(Single(later, Visit).DateTime!.Value.Kind, Is.EqualTo(DateTimeKind.Unspecified), "the wall clock, in no zone");
            Assert.That(Single(later, Visit).Date, Is.Null);
        });
    }

    [TestCase("09:30", 9, 30, 0)]
    [TestCase("23:59:59", 23, 59, 59)]
    [TestCase("00:00", 0, 0, 0)]
    public void ATime_ReadsBackAsATimeOfDay(string text, int hours, int minutes, int seconds) =>
        Assert.That(Single(RoundTrip(Answer(Arrival, text)), Arrival).Time, Is.EqualTo(new TimeOnly(hours, minutes, seconds)));

    [Test]
    public void ABox_ReadsBackAsYesOrNo()
    {
        var value = Single(RoundTrip(Answer(Newsletter, "true")), Newsletter);
        var unticked = Single(Read([Value(Newsletter, "false")]), Newsletter);

        Assert.Multiple(() =>
        {
            Assert.That(value.Bool, Is.True);
            Assert.That(value.Value, Is.EqualTo(new FieldValue("true")));
            Assert.That(unticked.Bool, Is.False);
        });
    }

    [Test]
    public void AValueOfParts_ReadsBackAsItsParts_InTheTypesOrder()
    {
        var value = Single(RoundTrip(Answer(Name, new FieldAnswers([], [Part("last", "Lovelace"), Part("first", "Ada")]))), Name);

        Assert.Multiple(() =>
        {
            Assert.That(value.Parts, Is.EqualTo(new[] { KeyValuePair.Create("first", "Ada"), KeyValuePair.Create("last", "Lovelace") }));
            Assert.That(value.Value, Is.EqualTo(new FieldValue("""{"first":"Ada","last":"Lovelace"}""")));
        });
    }

    [Test]
    public void AGrid_ReadsBackAsOneAnswer_ByItsRows_EachValueWithItsRow()
    {
        var answers = RoundTrip(
            Answer(Satisfaction, new FieldAnswers([], [Part(Price.ToString("D"), "bad"), Part(Speed.ToString("D"), "good")])),
            Answer(Channels, new FieldAnswers([], [Part(Call.ToString("D"), "never"), Part(Mail.ToString("D"), "weekly", "monthly")])));

        Assert.Multiple(() =>
        {
            Assert.That(answers.Find(Satisfaction)!.Values.Select(value => value.Value), Is.EqualTo(new[] { new FieldValue("good", Speed), new FieldValue("bad", Price) }));
            Assert.That(answers.Find(Channels)!.Values.Select(value => value.Value), Is.EqualTo(new[] { new FieldValue("weekly", Mail), new FieldValue("monthly", Mail), new FieldValue("never", Call) }));
            Assert.That(answers.Find(Speed), Is.Null, "a row is no answer of its own");
            Assert.That(answers.Find(Satisfaction)!.Storage, Is.EqualTo(FieldStorageKind.None));
        });
    }

    [Test]
    public void AFile_ReadsBackWithItsUpload()
    {
        var first = Upload(3, Attachments) with { EntryId = EntryId };
        var second = Upload(4, Attachments) with { EntryId = EntryId };
        var answers = RoundTrip([Answer(Attachments, Token(first), Token(second)) with { Files = [first with { EntryId = null }, second with { EntryId = null }] }], [first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(answers.Find(Attachments)!.Values.Select(value => value.File), Is.EqualTo(new[] { first, second }));
            Assert.That(Texts(answers, Attachments), Is.EqualTo(new[] { Token(first), Token(second) }), "the token an upload answers with is its key");
        });
    }

    [Test]
    public void ATickedConsent_ReadsBackTrue_WithItsWording()
    {
        var value = Single(RoundTrip([Ticked(Terms, WordingHash)], [], new Dictionary<string, StoredConsentText> { [WordingHash] = StoredWording }), Terms);

        Assert.Multiple(() =>
        {
            Assert.That(value.Bool, Is.True);
            Assert.That(value.Consent, Is.EqualTo(StoredWording));
            Assert.That(value.Value, Is.EqualTo(new FieldValue("true") { StoredText = WordingHash }), "true to rules, the hash stored");
            Assert.That(value.Text, Is.EqualTo(WordingHash));
        });
    }

    [Test]
    public void AConsentWhoseWordingIsNotStored_ReadsBackTrue_WithoutIt()
    {
        var value = Single(Read([Value(Terms, WordingHash)]), Terms);

        Assert.Multiple(() =>
        {
            Assert.That(value.Bool, Is.True);
            Assert.That(value.Consent, Is.Null);
        });
    }

    [Test]
    public void ARepeatableGroupsChildren_ReadBackByRow()
    {
        var ticket = Upload(8, Ticket) with { EntryId = EntryId };
        var answers = RoundTrip(
            [EntryTestForm.Answer(GuestName, new FieldAnswers(["Ada"])), EntryTestForm.Answer(GuestName, new FieldAnswers(["Grace"]), 1), EntryTestForm.Answer(Ticket, new FieldAnswers([Token(ticket)]), 1) with { Files = [ticket with { EntryId = null }] }],
            [ticket]);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(answers, GuestName, 0), Is.EqualTo(new[] { "Ada" }));
            Assert.That(Texts(answers, GuestName, 1), Is.EqualTo(new[] { "Grace" }));
            Assert.That(answers.Find(Ticket, 1)!.Values.Single().File, Is.EqualTo(ticket));
            Assert.That(answers.Find(Ticket), Is.Null, "no ticket in the first row");
            Assert.That(answers.Rows(Guests), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(answers.Find(GuestName, 1)!.Repeater, Is.EqualTo(Guests));
            Assert.That(answers.Find(FullName), Is.Null);
        });
    }

    [Test]
    public void TheAnswers_ReadInTheCataloguesOrder_ThenByRow_HoweverTheRowsCome()
    {
        var rows = new List<StoredEntryValue>
        {
            Value(GuestName, "Grace", row: 1),
            Value(Fruit, "a", index: 1),
            Value(Price, "bad"),
            Value(GuestName, "Ada"),
            Value(Fruit, "b"),
            Value(Speed, "good"),
            Value(FullName, "Ada"),
        };

        var answers = Read(rows);

        Assert.Multiple(() =>
        {
            Assert.That(answers.All.Select(answer => (answer.Field, answer.Row)), Is.EqualTo(new[] { (FullName, 0), (Fruit, 0), (Satisfaction, 0), (GuestName, 0), (GuestName, 1) }));
            Assert.That(Texts(answers, Fruit), Is.EqualTo(new[] { "b", "a" }));
            Assert.That(Texts(answers, Satisfaction), Is.EqualTo(new[] { "good", "bad" }));
            Assert.That(answers.Find(Fruit)!.Alias, Is.EqualTo("fruit"));
            Assert.That(answers.Find(Fruit)!.Type, Is.EqualTo("checkboxes"));
        });
    }

    [TestCase("age", "twelve")]
    [TestCase("birthday", "soon")]
    [TestCase("visit", "2026-02-30T10:00")]
    [TestCase("newsletter", "maybe")]
    [TestCase("arrival", "25:00")]
    [TestCase("name", "not parts")]
    public void ATextItsKindCannotRead_ReadsBackAsItsTextAlone(string alias, string text)
    {
        // Stored before the field's type changed (ADR-0059), or by a site's own code.
        var key = Catalogue.Single(field => field.Values.Alias == alias && field.Values.ParentFieldKey is null).Values.FieldKey;

        var value = Single(Read([Value(key, text)]), key);

        Assert.Multiple(() =>
        {
            Assert.That(value, Has.Property(nameof(EntryAnswerValue.Text)).EqualTo(text));
            Assert.That(value, Is.EqualTo(new EntryAnswerValue { Value = new FieldValue(text) }));
        });
    }

    [Test]
    public void AFieldWhoseTypeIsGone_ReadsByItsStorageAlone()
    {
        var catalogue = new List<CatalogueField>
        {
            Gone(1, Key(201), FieldStorageKind.Number),
            Gone(2, Key(202), FieldStorageKind.Text),
            Gone(3, Key(203), FieldStorageKind.Date),
        };
        var values = new List<StoredEntryValue> { Stored(1, "42"), Stored(2, "09:30"), Stored(3, "2026-10-26") };

        var answers = EntryValueReader.Read(catalogue, values, [], new Dictionary<string, StoredConsentText>(), TestFieldTypes.Registry);

        Assert.Multiple(() =>
        {
            Assert.That(answers.Find(Key(201))!.Values.Single().Number, Is.EqualTo(42m));
            Assert.That(answers.Find(Key(202))!.Values.Single(), Is.EqualTo(new EntryAnswerValue { Value = new FieldValue("09:30") }), "no rule kind to read a time by");
            Assert.That(answers.Find(Key(203))!.Values.Single().Date, Is.EqualTo(new DateOnly(2026, 10, 26)));
        });
    }

    [Test]
    public void AFileWhoseRowIsGone_ReadsWithoutItsUpload()
    {
        var value = Single(Read([Value(Attachments, Key(600).ToString("D")) with { FileId = 99 }]), Attachments);

        Assert.That(value.File, Is.Null);
    }

    [Test]
    public void ARowWithoutAText_ReadsAsNoValue()
    {
        var answers = Read([Value(FullName, "Ada") with { Text = null }, Value(Message, string.Empty), Value(Email, "ada@example.org")]);

        Assert.That(answers.All.Select(answer => answer.Field), Is.EqualTo(new[] { Email }));
    }

    [Test]
    public void AValueOfAnotherCatalogue_IsRefused() =>
        Assert.That(() => Read([Stored(999, "x")]), Throws.ArgumentException);

    [Test]
    public void TheConsentHashes_NameEachConsentsWordingOnce()
    {
        var values = new List<StoredEntryValue> { Value(Terms, WordingHash), Value(Privacy, WordingHash), Value(FullName, new string('b', 64)), Value(Terms, string.Empty, row: 1) };

        Assert.That(EntryValueReader.ConsentHashes(Catalogue, values, TestFieldTypes.Registry), Is.EqualTo(new[] { WordingHash }));
    }

    [Test]
    public void TheAnswers_GiveMergeTags_WhatTheTypesRead_WithTheFilesNamesAndSizes()
    {
        var upload = Upload(3, Attachments) with { EntryId = EntryId };
        EntryFieldAnswer[] written =
        [
            Answer(FullName, "Ada"),
            Answer(Age, "12.5"),
            Answer(Visit, "2026-10-26T09:30"),
            Answer(Satisfaction, new FieldAnswers([], [Part(Speed.ToString("D"), "good")])),
            Answer(Attachments, Token(upload)) with { Files = [upload with { EntryId = null }] },
            Ticked(Terms, WordingHash),
            EntryTestForm.Answer(GuestName, new FieldAnswers(["Grace"]), 1),
        ];

        var tags = RoundTrip(written, [upload], new Dictionary<string, StoredConsentText> { [WordingHash] = StoredWording }).ForMergeTags([Email]);

        Assert.Multiple(() =>
        {
            foreach (var answer in written)
            {
                Assert.That(tags.Find(answer.Field, answer.Row)!.Values, Is.EqualTo(answer.Values), answer.Field.ToString());
            }

            Assert.That(tags.Find(Attachments)!.Files, Is.EqualTo(new[] { new FieldFile("file3.pdf", 300) }));
            Assert.That(tags.Find(FullName)!.Files, Is.Empty);
            Assert.That(tags.Hidden, Is.EquivalentTo(new[] { Email }));
        });
    }

    [Test]
    public void AFileAnswerWithoutAllItsUploads_GivesMergeTagsItsValuesAlone()
    {
        var upload = Upload(3, Attachments) with { EntryId = EntryId };
        var answers = Read([Value(Attachments, Token(upload)) with { FileId = 3 }, Value(Attachments, Key(601).ToString("D"), index: 1) with { FileId = 99 }], [upload]);

        var tag = answers.ForMergeTags().Find(Attachments)!;

        Assert.Multiple(() =>
        {
            Assert.That(tag.Values, Has.Length.EqualTo(2));
            Assert.That(tag.Files, Is.Empty);
        });
    }

    [Test]
    public void TheAnswers_RefuseANullAnswer_OrTwoInOneRow()
    {
        var answer = new EntryAnswer { Field = FullName, Alias = "fullName", Type = "text", Storage = FieldStorageKind.Text, Values = [] };

        Assert.Multiple(() =>
        {
            Assert.That(() => new EntryAnswers([answer, answer]), Throws.ArgumentException);
            Assert.That(() => new EntryAnswers([answer, answer with { Row = 1 }]), Throws.Nothing);
            Assert.That(() => new EntryAnswers([null!]), Throws.ArgumentException);
        });
    }

    // An upload answers with its key (ADR-0006).
    private static string Token(StoredEntryFile file) => file.Key.ToString("D");

    private static bool Untyped(EntryAnswerValue value) =>
        value.Number is null && value.Date is null && value.DateTime is null && value.Time is null && value.Bool is null && value.File is null && value.Parts is null && value.Consent is null;

    private static string[] Texts(EntryAnswers answers, Guid field, int row = 0) => [.. answers.Find(field, row)!.Values.Select(value => value.Value.Text)];

    private static EntryAnswerValue Single(EntryAnswers answers, Guid field) => answers.Find(field)!.Values.Single();

    // The answers written, stored as the repository stores them, and read back.
    private static EntryAnswers RoundTrip(params EntryFieldAnswer[] answers) => RoundTrip(answers, []);

    private static EntryAnswers RoundTrip(EntryFieldAnswer[] answers, StoredEntryFile[] files, Dictionary<string, StoredConsentText>? wordings = null)
    {
        var shown = answers.Where(answer => answer.Field == Terms || answer.Field == Privacy).ToDictionary(answer => answer.Field, _ => Wording());
        var rows = EntryValueWriter.Of(FormId, Definition, Catalogue, TestFieldTypes.Registry, answers, shown);
        var stored = rows.Values.Select((row, index) => new StoredEntryValue(index + 1, EntryId, row.FieldId, row.RowIndex, row.ValueIndex, row.Text, null, row.Number, row.Date, row.Bool, row.FileId)).ToList();
        return Read(stored, files, wordings);
    }

    private static EntryAnswers Read(IReadOnlyList<StoredEntryValue> values, StoredEntryFile[]? files = null, Dictionary<string, StoredConsentText>? wordings = null) =>
        EntryValueReader.Read(Catalogue, values, files ?? [], wordings ?? new Dictionary<string, StoredConsentText>(), TestFieldTypes.Registry);

    private static StoredEntryValue Value(Guid field, string text, int row = 0, int index = 0) =>
        new(0, EntryId, IdOf(field), row, index, text, null, null, null, null, null);

    private static StoredEntryValue Stored(int fieldId, string text) => new(0, EntryId, fieldId, 0, 0, text, null, null, null, null, null);

    private static CatalogueField Gone(int id, Guid key, FieldStorageKind storage) =>
        new(id, FormId, new CatalogueFieldValues(key, $"gone{id}", string.Empty, "gone", storage, false, id, 1, null, null));
}
