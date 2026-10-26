using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// A form with a field of every built-in type that holds a value, for the tests of how answers are stored and read back
/// (ADR-0070): two grids, one taking several columns of a row, a repeatable group with a text and a file, and a heading
/// that holds none; with its field catalogue as a publish stores it, and answers read as the pipeline reads them.
/// </summary>
internal static class EntryTestForm
{
    /// <summary>The form's id.</summary>
    public const int FormId = 7;

    /// <summary>The hash of a consent wording, as a form posts it.</summary>
    public static readonly string WordingHash = ConsentTextHash.For("en-GB", "I agree to the <a href=\"/terms\">terms</a>.");

    public static readonly Guid FullName = Key(1);
    public static readonly Guid Message = Key(2);
    public static readonly Guid Email = Key(3);
    public static readonly Guid Phone = Key(4);
    public static readonly Guid Website = Key(5);
    public static readonly Guid Age = Key(6);
    public static readonly Guid Stars = Key(7);
    public static readonly Guid Score = Key(8);
    public static readonly Guid Volume = Key(9);
    public static readonly Guid Total = Key(10);
    public static readonly Guid Birthday = Key(11);
    public static readonly Guid Arrival = Key(12);
    public static readonly Guid Visit = Key(13);
    public static readonly Guid Toppings = Key(14);
    public static readonly Guid Size = Key(15);
    public static readonly Guid Fruit = Key(16);
    public static readonly Guid Satisfaction = Key(17);
    public static readonly Guid Speed = Key(18);
    public static readonly Guid Price = Key(19);
    public static readonly Guid Channels = Key(20);
    public static readonly Guid Mail = Key(21);
    public static readonly Guid Call = Key(22);
    public static readonly Guid Priorities = Key(23);
    public static readonly Guid Newsletter = Key(24);
    public static readonly Guid Returning = Key(25);
    public static readonly Guid Terms = Key(26);
    public static readonly Guid Privacy = Key(27);
    public static readonly Guid Country = Key(28);
    public static readonly Guid Name = Key(29);
    public static readonly Guid Address = Key(30);
    public static readonly Guid Attachments = Key(31);
    public static readonly Guid Signature = Key(32);
    public static readonly Guid Campaign = Key(33);
    public static readonly Guid Guests = Key(34);
    public static readonly Guid GuestName = Key(35);
    public static readonly Guid Ticket = Key(36);
    public static readonly Guid Heading = Key(37);

    /// <summary>The form's only version.</summary>
    public static FormDefinition Definition { get; } = new()
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
                            Field(FullName, "text", "fullName"),
                            Field(Message, "textarea", "message"),
                            Field(Email, "email", "email"),
                            Field(Phone, "phone", "phone"),
                            Field(Website, "url", "website"),
                            Field(Age, "number", "age"),
                            Field(Stars, "rating", "stars"),
                            Field(Score, "scale", "score"),
                            Field(Volume, "slider", "volume"),
                            Field(Total, "calculation", "total", """{"formula":"1"}"""),
                            Field(Birthday, "date", "birthday"),
                            Field(Arrival, "time", "arrival"),
                            Field(Visit, "datetime", "visit"),
                            Field(Heading, "heading", null),
                            Field(Toppings, "dropdown", "toppings", """{"multiple":true}"""),
                            Field(Size, "radio", "size", """{"other":true}"""),
                            Field(Fruit, "checkboxes", "fruit", """{"other":true}"""),
                            Field(Satisfaction, "grid", "satisfaction") with { Rows = [new GridRow { Key = Speed }, new GridRow { Key = Price }] },
                            Field(Channels, "grid", "channels", """{"multiple":true}""") with { Rows = [new GridRow { Key = Mail }, new GridRow { Key = Call }] },
                            Field(Priorities, "ranking", "priorities"),
                            Field(Newsletter, "checkbox", "newsletter"),
                            Field(Returning, "yesno", "returning"),
                            Field(Terms, "consent", "terms"),
                            Field(Privacy, "consent", "privacy"),
                            Field(Country, "country", "country"),
                            Field(Name, "name", "name"),
                            Field(Address, "address", "address"),
                            Field(Attachments, "file", "attachments", """{"maxFiles":5}"""),
                            Field(Signature, "signature", "signature"),
                            Field(Campaign, "hidden", "campaign"),
                            Field(Guests, "repeater", "guests") with { Fields = [Field(GuestName, "text", "guestName"), Field(Ticket, "file", "ticket")] },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>The form's field catalogue as its publish stored it: ids from 1 in document order.</summary>
    public static IReadOnlyList<CatalogueField> Catalogue { get; } =
        [.. FieldCatalogue.Of(Definition, TestFieldTypes.Registry).Select((entry, index) => new CatalogueField(
            index + 1,
            FormId,
            new CatalogueFieldValues(entry.FieldKey, entry.Alias, entry.Label, entry.Type, entry.Storage, entry.IsSensitive, index, 1, null, entry.ParentFieldKey)))];

    /// <summary>A key of the form.</summary>
    public static Guid Key(int number) => Guid.Parse($"70000000-0000-4000-8000-{number:D12}");

    /// <summary>The catalogue row of a field or a grid row.</summary>
    public static int IdOf(Guid key) => Catalogue.Single(field => field.Values.FieldKey == key).Id;

    /// <summary>The answer of a field in a row, read by its type as the pipeline reads it.</summary>
    public static EntryFieldAnswer Answer(Guid field, FieldAnswers posted, int row = 0)
    {
        var definition = Find(field);
        return new EntryFieldAnswer(field, FieldValues.Read(TestFieldTypes.Registry.FindType(definition.Type)!, definition, posted).Values, row);
    }

    /// <summary>The answer of a field, posted as its own texts.</summary>
    public static EntryFieldAnswer Answer(Guid field, params string[] texts) => Answer(field, new FieldAnswers(texts));

    /// <summary>A part of a posted answer.</summary>
    public static KeyValuePair<string, IEnumerable<string>> Part(string name, params string[] texts) => new(name, texts);

    /// <summary>A ticked consent with the hash of the wording it was shown.</summary>
    public static EntryFieldAnswer Ticked(Guid consent, string hash) => Answer(consent, new FieldAnswers(["true"], [Part("wording", hash)]));

    /// <summary>The wording of <see cref="WordingHash"/>.</summary>
    public static ConsentWording Wording() => new("en-GB", "I agree to the <a href=\"/terms\">terms</a>.");

    /// <summary>An upload of the form that waits for its entry, unless the test names another form or an entry.</summary>
    public static StoredEntryFile Upload(int id, Guid field, int formId = FormId, long? entryId = null) => new(
        id,
        Key(500 + id),
        entryId,
        formId,
        field,
        $"file{id}.pdf",
        $"{id:D32}",
        "application/pdf",
        100L * id,
        new string('a', 64),
        "clean",
        DateTimeOffset.UnixEpoch);

    /// <summary>A field of the form by its key, a repeatable group's child included.</summary>
    public static FormField Find(Guid key) =>
        Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).Single(field => field.Key == key);

    private static FormField Field(Guid key, string type, string? alias, string? settings = null) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };
}
