using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Audit;

/// <summary>
/// Audit details take only values that cannot carry what a visitor typed (section 4.10, ADR-0041): keys, numbers, flags,
/// one defined enum value, UTC times and identifiers. Free text never fits, and the type has no way to add any.
/// </summary>
[TestFixture]
public sealed class AuditDetailsTests
{
    private static readonly Guid Key = Guid.Parse("4b1e1c8e-6a51-4f0e-9a0e-2a8d7c4b9f10");
    private static readonly Guid Other = Guid.Parse("0d2f6b7a-1c3e-4a5b-8c9d-0e1f2a3b4c5d");

    [Test]
    public void KeysNumbersFlagsEnumsTimesAndIdentifiers_AreRecorded_NullsLeftOut()
    {
        var json = new AuditDetails()
            .Add("formKey", Key)
            .Add("nobody", (Guid?)null)
            .Add("version", 3)
            .Add("missing", (long?)null)
            .Add("sensitive", true)
            .Add("state", ApprovalMode.Manual)
            .Add("untilUtc", new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)))
            .Add("never", (DateTimeOffset?)null)
            .AddIdentifier("section", "general")
            .AddIdentifier("absent", null)
            .AddIdentifiers("paths", ["defaults.labels[0].text", "privacy.retention-days"])
            .AddKeys("fieldKeys", [Key, Other])
            .ToJson();

        Assert.That(json, Is.EqualTo("""{"formKey":"4b1e1c8e-6a51-4f0e-9a0e-2a8d7c4b9f10","version":3,"sensitive":true,"state":"manual","untilUtc":"2027-01-02T01:04:05.0000000Z","section":"general","paths":["defaults.labels[0].text","privacy.retention-days"],"fieldKeys":["4b1e1c8e-6a51-4f0e-9a0e-2a8d7c4b9f10","0d2f6b7a-1c3e-4a5b-8c9d-0e1f2a3b4c5d"]}"""));
    }

    [Test]
    public void NoDetails_AreAnEmptyObject()
    {
        var details = new AuditDetails().Add("nothing", (long?)null);

        Assert.Multiple(() =>
        {
            Assert.That(details.IsEmpty, Is.True);
            Assert.That(details.ToJson(), Is.EqualTo("{}"));
            Assert.That(new AuditDetails().Add("count", 0).IsEmpty, Is.False);
        });
    }

    [TestCase("Mia Muster", TestName = "a name")]
    [TestCase("mia.muster@example.test", TestName = "an email address")]
    [TestCase("+49 30 1234567", TestName = "a phone number")]
    [TestCase("+4930123456", TestName = "a phone number without spaces")]
    [TestCase("Hauptstrasse 5, 10115 Berlin", TestName = "an address")]
    [TestCase("Ich habe eine Frage zu meinem Termin.", TestName = "a message")]
    [TestCase("line\nbreak", TestName = "a line break")]
    [TestCase("word\n", TestName = "a line break at the end")]
    [TestCase("word\r", TestName = "a carriage return at the end")]
    [TestCase("tab\tbed", TestName = "a tab")]
    [TestCase("M\u00fcller", TestName = "a letter outside ASCII")]
    [TestCase("\u0661\u0662\u0663", TestName = "digits outside ASCII")]
    [TestCase("a/b", TestName = "a slash")]
    [TestCase("a:b", TestName = "a colon")]
    [TestCase("\"quoted\"", TestName = "quotes")]
    [TestCase("<b>bold</b>", TestName = "markup")]
    [TestCase("{\"value\":1}", TestName = "JSON")]
    [TestCase("Rechtsanspruch,Fall12", TestName = "a comma")]
    [TestCase("", TestName = "nothing")]
    public void FreeText_IsRefused_InEveryPlaceThatTakesText(string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(AuditDetails.IsIdentifier(text), Is.False);
            Assert.That(() => new AuditDetails().AddIdentifier("value", text), Throws.ArgumentException);
            Assert.That(() => new AuditDetails().AddIdentifiers("values", ["ok", text]), Throws.ArgumentException);
            Assert.That(() => new AuditDetails().Add(text, true), Throws.ArgumentException, "as a name");
        });
    }

    [Test]
    public void LongIdentifiers_LongLists_AndCombinedFlags_AreRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AuditDetails.IsIdentifier("overrides.de-CH." + new string('k', 184)), Is.True, "200 characters");
            Assert.That(() => new AuditDetails().AddIdentifier("value", new string('a', 201)), Throws.ArgumentException);
            Assert.That(() => new AuditDetails().AddIdentifiers("values", Enumerable.Repeat("a", 200)), Throws.Nothing);
            Assert.That(() => new AuditDetails().AddIdentifiers("values", Enumerable.Repeat("a", 201)), Throws.ArgumentException);
            Assert.That(() => new AuditDetails().AddKeys("keys", Enumerable.Repeat(Key, 201)), Throws.ArgumentException);
            Assert.That(() => new AuditDetails().Add("attributes", FileAttributes.ReadOnly | FileAttributes.Hidden), Throws.ArgumentException, "a combination of flags");
            Assert.That(() => new AuditDetails().Add("state", (ApprovalMode)99), Throws.ArgumentException, "no defined value");
            Assert.That(() => new AuditDetails().AddIdentifiers("values", null!), Throws.ArgumentNullException);
            Assert.That(() => new AuditDetails().AddKeys("keys", null!), Throws.ArgumentNullException);
        });
    }

    // A guard for the review: a new way to add a value, public or not, has to be one that cannot carry text, and these
    // lists change with it.
    [Test]
    public void TheDetails_HaveNoWayToAddText()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var methods = typeof(AuditDetails).GetMethods(All)
            .Select(method => $"{(method.IsPublic ? "public" : "private")}{(method.IsStatic ? " static" : string.Empty)} {method.Name}({string.Join(", ", method.GetParameters().Select(parameter => Name(parameter.ParameterType)))})")
            .Order(StringComparer.Ordinal)
            .ToList();
        var fields = typeof(AuditDetails).GetFields(All).Select(field => $"{Name(field.FieldType)} {field.Name}").Order(StringComparer.Ordinal).ToList();

        Assert.That(methods, Is.EqualTo(new[]
        {
            "private Set(String, JsonNode)",
            "private static Identifier(String)",
            "private static List(IEnumerable<JsonValue>)",
            "public Add(String, Boolean)",
            "public Add(String, Nullable<DateTimeOffset>)",
            "public Add(String, Nullable<Guid>)",
            "public Add(String, Nullable<Int64>)",
            "public Add(String, TEnum)",
            "public AddIdentifier(String, String)",
            "public AddIdentifiers(String, IEnumerable<String>)",
            "public AddKeys(String, IEnumerable<Guid>)",
            "public ToJson()",
            "public get_IsEmpty()",
            "public static IsIdentifier(String)",
        }));
        Assert.That(fields, Is.EqualTo(new[] { "Int32 MaxIdentifierLength", "Int32 MaxListLength", "JsonObject _json" }));
    }

    private static string Name(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>" : type.Name;
}
