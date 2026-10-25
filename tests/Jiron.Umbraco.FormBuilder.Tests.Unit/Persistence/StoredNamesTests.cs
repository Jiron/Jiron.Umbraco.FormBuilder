using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The one converter for enums stored as text (ROADMAP 4.8a, ADR-0033): every stored enum is written as the camel case
/// of its names, read back only from exactly that text, and fits its column.
/// </summary>
public sealed class StoredNamesTests
{
    // Every column that holds one of these is 16 characters wide.
    private const int ColumnLength = 16;

    [Test]
    public void EveryStoredEnum_IsWrittenAsTheCamelCaseOfItsNames_AndReadBackFromIt()
    {
        Assert.Multiple(() =>
        {
            RoundTrips<EntryState>();
            RoundTrips<FormVersionKind>();
            RoundTrips<ActionRunState>();
            RoundTrips<ActionTrigger>();
            RoundTrips<EmailKind>();
            RoundTrips<EmailState>();
            RoundTrips<FormTemplateKind>();
            RoundTrips<PermissionTarget>();
            RoundTrips<FieldStorageKind>();
        });
    }

    [TestCase("pending", true)]
    [TestCase("dataType", true)]
    [TestCase("p", true)]
    [TestCase("abcdefghijklmnop", true)]
    [TestCase("abcdefghijklmnopq", false)]
    [TestCase("Pending", false)]
    [TestCase("PENDING", false)]
    [TestCase("pending1", false)]
    [TestCase("data-type", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void AStoredNameThatNoEnumHoldsYet_IsACamelCaseWordOfAsciiLetters(string? text, bool valid) =>
        Assert.That(StoredNames.IsName(text, 16), Is.EqualTo(valid));

    [TestCase("pending", true)]
    [TestCase("Pending", false)]
    [TestCase("PENDING", false)]
    [TestCase("pending ", false)]
    [TestCase("paused", false)]
    [TestCase("", false)]
    public void AStoredName_IsReadOnlyInItsOwnLetterCase(string stored, bool read)
    {
        if (read)
        {
            Assert.That(StoredNames.Parse<ActionRunState>(stored), Is.EqualTo(ActionRunState.Pending));
        }
        else
        {
            Assert.That(() => StoredNames.Parse<ActionRunState>(stored), Throws.InvalidOperationException.With.Message.Contains(nameof(ActionRunState)));
        }
    }

    [Test]
    public void SeveralWords_KeepTheirCapitals_AfterTheFirst()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StoredNames.Of(FieldStorageKind.LongText), Is.EqualTo("longText"));
            Assert.That(StoredNames.Parse<FieldStorageKind>("longText"), Is.EqualTo(FieldStorageKind.LongText));
            Assert.That(() => StoredNames.Parse<FieldStorageKind>("longtext"), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void AValueWithoutAName_IsNeverWritten_AndNullIsNeverRead()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => StoredNames.Of((EntryState)99), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => StoredNames.Parse<EntryState>(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AMessage_NamesAValueAsItIsStored_AndOneWithoutANameByItsNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StoredNames.Describe(FieldStorageKind.LongText), Is.EqualTo("longText"));
            Assert.That(StoredNames.Describe(EntryState.Approved), Is.EqualTo(StoredNames.Of(EntryState.Approved)));
            Assert.That(StoredNames.Describe((EntryState)99), Is.EqualTo("99"), "a refused value, named in its refusal");
        });
    }

    private static void RoundTrips<TEnum>()
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var stored = StoredNames.Of(value);
            Assert.That(stored, Is.EqualTo(JsonNamingPolicy.CamelCase.ConvertName(value.ToString())), $"{typeof(TEnum).Name}.{value}");
            Assert.That(stored, Has.Length.InRange(1, ColumnLength).And.Match("^[a-z][a-zA-Z0-9]*$"), $"{typeof(TEnum).Name}.{value}");
            Assert.That(StoredNames.Parse<TEnum>(stored), Is.EqualTo(value), $"{typeof(TEnum).Name}.{value}");
        }
    }
}
