using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The row each storage kind stores a value as (ADR-0003, ADR-0058): its text, and the typed column it fills, or none
/// where a value is text only and so filters and sorts by its key.
/// </summary>
public sealed class EntryValueColumnsTests
{
    private const int FieldId = 9;

    private static readonly Guid FieldKey = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly StoredEntryFile Upload = new(
        7, Guid.Parse("33333333-3333-3333-3333-333333333333"), 1, 1, Guid.NewGuid(), "cv.pdf", "a.bin", "application/pdf", 10, "hash", "clean", DateTimeOffset.UnixEpoch);

    [TestCase(FieldStorageKind.Text)]
    [TestCase(FieldStorageKind.LongText)]
    public void AText_IsTextOnly(FieldStorageKind storage)
    {
        var row = EntryValueColumns.Of(storage, new FieldValue(" Ada "), FieldId);

        Assert.Multiple(() =>
        {
            Assert.That(row, Is.EqualTo(new NewEntryValue(FieldId, " Ada ")));
            Assert.That(row.IsText, Is.True);
            Assert.That(row.KeyText, Is.Null, "the key is the text's own");
        });
    }

    [Test]
    public void AValue_StoresTheTextItStoresInPlaceOfItsOwn() =>
        Assert.That(EntryValueColumns.Of(FieldStorageKind.Text, new FieldValue("true") { StoredText = "wording-hash" }, FieldId).Text, Is.EqualTo("wording-hash"));

    [Test]
    public void AGridRowsValue_IsTextAtItsRowsPlace() =>
        Assert.That(EntryValueColumns.Of(FieldStorageKind.Text, new FieldValue("good", Guid.NewGuid()), 12), Is.EqualTo(new NewEntryValue(12, "good")));

    [TestCase("0")]
    [TestCase("-12.5")]
    [TestCase("0.1")]
    [TestCase("123456789012345")]
    [TestCase("-0.123456789012345")]
    [TestCase("0.000000000000001")]
    [TestCase("999999999999999")]
    public void ANumber_FillsTheNumber_ExactForItsFifteenDigits(string text)
    {
        var row = EntryValueColumns.Of(FieldStorageKind.Number, new FieldValue(text), FieldId);

        Assert.Multiple(() =>
        {
            Assert.That(row.Text, Is.EqualTo(text));
            Assert.That(row.Number, Is.Not.Null);
            Assert.That((decimal)row.Number!.Value, Is.EqualTo(decimal.Parse(text, CultureInfo.InvariantCulture)), "a double read back to 15 digits is the number");
            Assert.That(row.IsText, Is.False);
        });
    }

    [TestCase("1234567890123456")]
    [TestCase("0.1234567890123456")]
    [TestCase("12345678901234567890.5")]
    public void ANumberOfMoreDigits_IsTextOnly(string text)
    {
        var row = EntryValueColumns.Of(FieldStorageKind.Number, new FieldValue(text), FieldId);

        Assert.Multiple(() =>
        {
            Assert.That(row, Is.EqualTo(new NewEntryValue(FieldId, text)), "no rounded column for a reader to take for the value");
            Assert.That(row.IsText, Is.True);
        });
    }

    [TestCase("2026-10-25", 2026, 10, 25, 0, 0, 0)]
    [TestCase("2026-10-25T08:30", 2026, 10, 25, 8, 30, 0)]
    [TestCase("2026-10-25T23:59:59", 2026, 10, 25, 23, 59, 59)]
    [TestCase("1753-01-01", 1753, 1, 1, 0, 0, 0)]
    [TestCase("9999-12-31", 9999, 12, 31, 0, 0, 0)]
    public void ADate_FillsTheDate_AsTheWallClockValueItIs_WrittenAsUtc(string text, int year, int month, int day, int hour, int minute, int second)
    {
        var row = EntryValueColumns.Of(FieldStorageKind.Date, new FieldValue(text), FieldId);

        Assert.Multiple(() =>
        {
            Assert.That(row.Text, Is.EqualTo(text));
            Assert.That(row.Date, Is.EqualTo(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc)));
            Assert.That(row.Date!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [TestCase("1752-12-31")]
    [TestCase("0001-01-01T00:00")]
    [TestCase("9999-12-31T00:01")]
    public void ADateOutsideWhatBothDatabasesStore_IsTextOnly(string text) =>
        Assert.That(EntryValueColumns.Of(FieldStorageKind.Date, new FieldValue(text), FieldId), Is.EqualTo(new NewEntryValue(FieldId, text)));

    [TestCase("true", true)]
    [TestCase("false", false)]
    public void AYesOrNo_FillsTheBool(string text, bool value) =>
        Assert.That(EntryValueColumns.Of(FieldStorageKind.Bool, new FieldValue(text), FieldId), Is.EqualTo(new NewEntryValue(FieldId, text, Bool: value)));

    [Test]
    public void AFile_IsTheEntrysFile_ItsUploadBecame() =>
        Assert.That(
            EntryValueColumns.Of(FieldStorageKind.File, new FieldValue("upload-token"), FieldId, Upload),
            Is.EqualTo(new NewEntryValue(FieldId, "33333333-3333-3333-3333-333333333333", FileId: 7)));

    [Test]
    public void AFile_ComesWithTheEntrysFile_AndOnlyAFile()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => EntryValueColumns.Of(FieldStorageKind.File, new FieldValue("upload-token"), FieldId), Throws.ArgumentException);
            Assert.That(() => EntryValueColumns.Of(FieldStorageKind.Text, new FieldValue("a"), FieldId, Upload), Throws.ArgumentException);
        });
    }

    [Test]
    public void AValueWithParts_IsItsObject_KeyedByItsPartsTexts()
    {
        var row = EntryValueColumns.Of(FieldStorageKind.Json, new FieldValue("""{"first":"Ada","last":"Lovelace"}"""), FieldId);

        Assert.Multiple(() =>
        {
            Assert.That(row, Is.EqualTo(new NewEntryValue(FieldId, """{"first":"Ada","last":"Lovelace"}""") { KeyText = "Ada Lovelace" }));
            Assert.That(row.IsText, Is.True);
        });
    }

    [Test]
    public void AValueWithParts_IsWrittenCanonically_InItsOrder() =>
        Assert.That(
            EntryValueColumns.Of(FieldStorageKind.Json, new FieldValue("""{ "last" : "Lovelace", "first": "Ada" }"""), FieldId),
            Is.EqualTo(new NewEntryValue(FieldId, """{"last":"Lovelace","first":"Ada"}""") { KeyText = "Lovelace Ada" }));

    [Test]
    public void TheIndices_ArePassedOn() =>
        Assert.That(
            EntryValueColumns.Of(FieldStorageKind.Text, new FieldValue("a"), FieldId, rowIndex: 2, valueIndex: 3),
            Is.EqualTo(new NewEntryValue(FieldId, "a", RowIndex: 2, ValueIndex: 3)));

    [Test]
    public void AFieldWhosePartsHoldItsValues_StoresNoneOfItsOwn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => EntryValueColumns.Of(FieldStorageKind.None, new FieldValue("a"), FieldId), Throws.ArgumentException);
            Assert.That(() => EntryValueColumns.Of(FieldStorageKind.None, new FieldValue("a", Guid.NewGuid()), FieldId), Throws.ArgumentException, "its rows' places are text");
            Assert.That(EntryValueColumns.Holds(FieldStorageKind.None, new FieldValue("a", Guid.NewGuid())), Is.True, "a grid reads values of its rows");
            Assert.That(EntryValueColumns.Holds(FieldStorageKind.None, new FieldValue("a")), Is.False);
        });
    }

    [TestCase(FieldStorageKind.Number, "1e3")]
    [TestCase(FieldStorageKind.Number, "1,5")]
    [TestCase(FieldStorageKind.Number, "007")]
    [TestCase(FieldStorageKind.Date, "2026-02-30")]
    [TestCase(FieldStorageKind.Date, "08:30")]
    [TestCase(FieldStorageKind.Bool, "True")]
    [TestCase(FieldStorageKind.Bool, "1")]
    [TestCase(FieldStorageKind.Json, "[\"Ada\"]")]
    [TestCase(FieldStorageKind.Json, "{}")]
    [TestCase(FieldStorageKind.Json, "{\"first\":\"\"}")]
    [TestCase(FieldStorageKind.Json, "{\"\":\"Ada\"}")]
    [TestCase(FieldStorageKind.Json, "{\"first\":\"Ada\",\"first\":\"Augusta\"}")]
    [TestCase(FieldStorageKind.Json, "{\"first\":{\"name\":\"Ada\"}}")]
    [TestCase(FieldStorageKind.Json, "{\"first\":\"Ada\"")]
    [TestCase(FieldStorageKind.Json, "\"Ada\"")]
    public void AValueNotInItsKindsForm_IsNeitherHeldNorStored(FieldStorageKind storage, string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(EntryValueColumns.Holds(storage, new FieldValue(text)), Is.False);
            Assert.That(() => EntryValueColumns.Of(storage, new FieldValue(text), FieldId), Throws.ArgumentException.With.Message.Not.Contains(text));
            Assert.That(EntryValueColumns.Holds(storage, new FieldValue("ok") { StoredText = text }), Is.False, "the stored text is what is held");
        });
    }

    [Test]
    public void TheColumns_RefuseNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => EntryValueColumns.Holds(FieldStorageKind.Text, null!), Throws.ArgumentNullException);
            Assert.That(() => EntryValueColumns.Of(FieldStorageKind.Text, null!, FieldId), Throws.ArgumentNullException);
            Assert.That(() => EntryValueColumns.ComparedBy(FieldStorageKind.Text, FieldKey, null!), Throws.ArgumentNullException);
        });
    }

    [TestCase(FieldStorageKind.Text, " Ada ")]
    [TestCase(FieldStorageKind.LongText, "Line one")]
    [TestCase(FieldStorageKind.Number, "-12.5")]
    [TestCase(FieldStorageKind.Number, "1234567890123456")]
    [TestCase(FieldStorageKind.Date, "2026-10-25")]
    [TestCase(FieldStorageKind.Date, "2026-10-25T08:30")]
    public void AUniqueAnswer_ComparesByTheColumnItsRowFills(FieldStorageKind storage, string text)
    {
        var row = EntryValueColumns.Of(storage, new FieldValue(text), FieldId);

        var compared = EntryValueColumns.ComparedBy(storage, FieldKey, new FieldValue(text));

        Assert.That(compared, Is.EqualTo(new AnswerValue(FieldKey, row.IsText ? row.Text : null, row.Number, row.Date)), "a typed column, or else the text whose key the row gets");
    }

    [Test]
    public void AUniqueAnswer_ComparesByTheTextItStores() =>
        Assert.That(EntryValueColumns.ComparedBy(FieldStorageKind.Text, FieldKey, new FieldValue("Ada") { StoredText = "ada-hash" }), Is.EqualTo(new AnswerValue(FieldKey, "ada-hash", null, null)));

    [TestCase(FieldStorageKind.Bool, "true")]
    [TestCase(FieldStorageKind.Json, "{\"first\":\"Ada\"}")]
    [TestCase(FieldStorageKind.File, "token")]
    [TestCase(FieldStorageKind.None, "a")]
    [TestCase(FieldStorageKind.Number, "1e3")]
    [TestCase(FieldStorageKind.Date, "2026-02-30")]
    public void AUniqueAnswer_IsRefused_OfAKindThatDoesNotCompare_OrNotInItsKindsForm(FieldStorageKind storage, string text) =>
        Assert.That(() => EntryValueColumns.ComparedBy(storage, FieldKey, new FieldValue(text)), Throws.ArgumentException);
}
