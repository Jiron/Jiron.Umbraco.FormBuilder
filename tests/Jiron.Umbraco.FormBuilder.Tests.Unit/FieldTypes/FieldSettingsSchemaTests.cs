using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// A field type's settings schema, read from its settings class (ADR-0055): each property's type gives its kind and
/// editor, a new instance its default, the attribute its label, group and order; whatever cannot be a setting is refused
/// with every reason at once, naming the class and the property.
/// </summary>
public sealed class FieldSettingsSchemaTests
{
    public static IEnumerable<TestCaseData> Refusals()
    {
        static TestCaseData Case(string name, Type type, string says) => new TestCaseData(type, says).SetName($"Refuses({name})");

        yield return Case("a property without the attribute", typeof(Unmarked), "Unmarked.Title is not a setting: give it [FormBuilderSetting] or [JsonIgnore].");
        yield return Case("a setting ignored by JSON", typeof(IgnoredSetting), "IgnoredSetting.Title is a setting and ignored by JSON at once");
        yield return Case("a name outside the pattern", typeof(BadlyNamed), "BadlyNamed.Title is named 'Big-Title' in JSON; a setting's name is a lowercase letter");
        yield return Case("two settings of one name", typeof(TwiceNamed), "TwiceNamed cannot be written as JSON:");
        yield return Case("a property without a setter", typeof(GetOnly), "GetOnly.Title cannot be read from JSON: a setting is a public property with a public setter or init.");
        yield return Case("a property with a private setter", typeof(PrivatelySet), "PrivatelySet.Title cannot be read from JSON");
        yield return Case("a double", typeof(WithDouble), "WithDouble.Ratio is of the type System.Double, which no setting holds");
        yield return Case("a date and time", typeof(WithDateTime), "WithDateTime.At is of the type System.DateTime, which no setting holds");
        yield return Case("a dictionary", typeof(WithMap), "WithMap.Map is of the type System.Collections.Generic.Dictionary");
        yield return Case("a list that is not read-only", typeof(WithList), "a list of settings is an IReadOnlyList<T>.");
        yield return Case("an array", typeof(WithArray), "WithArray.Tags is of the type System.String[]; a list of settings is an IReadOnlyList<T>.");
        yield return Case("an object inside an object", typeof(Outer), "Middle.Inner is an object inside an object; settings nest one level deep.");
        yield return Case("a list of yes or no", typeof(ListOfBooleans), "ListOfBooleans.Flags is a list of System.Boolean; a list holds whole numbers, texts, numbers, dates, times, choices or keys.");
        yield return Case("a list of objects", typeof(ListOfObjects), "ListOfObjects.Boxes is a list of " + typeof(Box).FullName + "; a list holds");
        yield return Case("a list of nullable numbers", typeof(ListOfNullables), "ListOfNullables.Sizes is a list of System.Nullable");
        yield return Case("a key without an editor", typeof(KeyWithoutEditor), "KeyWithoutEditor.Page holds a key, which has no editor of its own: name one with Editor.");
        yield return Case("free JSON without an editor", typeof(JsonWithoutEditor), "JsonWithoutEditor.Extra holds free JSON, which has no editor of its own");
        yield return Case("a list of keys without an editor", typeof(KeysWithoutEditor), "KeysWithoutEditor.Pages holds a list of keys, which has no editor of its own");
        yield return Case("a blank editor", typeof(BlankEditor), "BlankEditor.Title holds a text, which has no editor of its own");
        yield return Case("a whole number stored as a date", typeof(NumberAsDate), "NumberAsDate.Count holds a whole number, which cannot be stored as a date; only a text can be a number, a date or a time.");
        yield return Case("a yes or no stored as a text", typeof(BooleanAsText), "BooleanAsText.On holds a yes or no, which cannot be stored as a text");
        yield return Case("a list of whole numbers stored as times", typeof(NumbersAsTimes), "NumbersAsTimes.Counts holds a whole number, which cannot be stored as a time");
        yield return Case("an object stored as a text", typeof(ObjectAsText), "ObjectAsText.Box is an object of settings, which cannot be stored as a text.");
        yield return Case("a flags enum", typeof(WithFlags), "WithFlags.Days is the enum " + typeof(Weekdays).FullName + "; a choice is an enum without [Flags] whose values each have one name.");
        yield return Case("an enum value with two names", typeof(WithAlias), "WithAlias.Size is the enum " + typeof(AliasedSize).FullName + "; a choice is an enum without [Flags]");
        yield return Case("a list of flags", typeof(ListOfFlags), "ListOfFlags.Days is the enum");
        yield return Case("a class without a constructor of no parameters", typeof(WithoutConstructor), typeof(WithoutConstructor).FullName + " is a settings class only as a class with a public constructor without parameters");
        yield return Case("an abstract class", typeof(AbstractSettings), typeof(AbstractSettings).FullName + " is a settings class only as a class");
        yield return Case("a nested class without a constructor", typeof(WithBadObject), typeof(WithoutConstructor).FullName + " is a settings class only as a class");
        yield return Case("a text that starts null where it cannot be", typeof(NullWhereNotNullable), "NullWhereNotNullable.Title starts null, though the class says it cannot be null: give it an initial value, or make it nullable.");
        yield return Case("a constructor that throws", typeof(ThrowingConstructor), "ThrowingConstructor cannot be written as JSON: Not today.");
        yield return Case("within a setting the class lacks", typeof(WithinNothing), "WithinNothing.Field is within 'box', which is no other setting of its class.");
        yield return Case("within itself", typeof(WithinItself), "WithinItself.To is within 'to', which is no other setting of its class.");
        yield return Case("within one that is within another", typeof(WithinTwice), "WithinTwice.Field is within 'copy', which is itself within 'to'; a setting is within one that has its own place.");
        yield return Case("within inside an object", typeof(WithinInsideAnObject), "WithinInside.Field is within 'to' inside an object, whose editor draws all of its settings.");
        yield return Case("within a box whose editor draws none within it", typeof(WithinAPlainBox), "WithinAPlainBox.Creator is within 'to', whose editor draws no setting within it; only the editor recipients does.");
        yield return Case("within the recipients, of a kind they do not draw", typeof(WithinRecipientsANote), "WithinRecipientsANote.Note is within 'to', whose editor recipients draws only an email field's answer");
        yield return Case("two of a kind within the recipients", typeof(TwoCreatorsWithin), "TwoCreatorsWithin.Second is within 'to', which already holds the person who created the form; the editor recipients draws one of each.");
        yield return Case("picking fields without a field picker", typeof(PicksWithoutPicker), "PicksWithoutPicker.Field picks fields of a kind, which only the editor fieldPicker offers.");
    }

    [Test]
    public void EachPropertysType_GivesItsKind_AndTheEditorOfThatKind()
    {
        var schema = FieldSettingsReader.Read(typeof(EveryKind));

        Assert.That(
            schema.Settings.Select(setting => (setting.Name, setting.Kind, setting.ItemKind, setting.Editor)),
            Is.EqualTo(new (string, FieldSettingKind, FieldSettingKind?, string)[]
            {
                ("on", FieldSettingKind.Boolean, null, "boolean"),
                ("maybe", FieldSettingKind.Boolean, null, "boolean"),
                ("count", FieldSettingKind.WholeNumber, null, "wholeNumber"),
                ("bytes", FieldSettingKind.WholeNumber, null, "wholeNumber"),
                ("limit", FieldSettingKind.WholeNumber, null, "wholeNumber"),
                ("title", FieldSettingKind.Text, null, "text"),
                ("amount", FieldSettingKind.Number, null, "number"),
                ("day", FieldSettingKind.Date, null, "date"),
                ("at", FieldSettingKind.Time, null, "time"),
                ("caption", FieldSettingKind.LocalizedText, null, "localizedText"),
                ("body", FieldSettingKind.LocalizedHtml, null, "localizedHtml"),
                ("look", FieldSettingKind.Choice, null, "choice"),
                ("page", FieldSettingKind.Key, null, "contentPicker"),
                ("extra", FieldSettingKind.Json, null, "dateBound"),
                ("sizes", FieldSettingKind.List, FieldSettingKind.WholeNumber, "list"),
                ("tags", FieldSettingKind.List, FieldSettingKind.Text, "list"),
                ("days", FieldSettingKind.List, FieldSettingKind.Date, "list"),
                ("amounts", FieldSettingKind.List, FieldSettingKind.Number, "list"),
                ("looks", FieldSettingKind.List, FieldSettingKind.Choice, "list"),
                ("pages", FieldSettingKind.List, FieldSettingKind.Key, "contentPicker"),
                ("box", FieldSettingKind.Nested, null, "nested"),
                ("frame", FieldSettingKind.Nested, null, "nested"),
            }));
        Assert.Multiple(() =>
        {
            Assert.That(schema.Find("box")!.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "to", "from", "label", "notes" }));
            Assert.That(schema.Find("look")!.Choices.Select(choice => choice.Value), Is.EqualTo(new[] { "plain", "bigCards", "x-small" }), "In the enum's order, as its JSON writes them.");
            Assert.That(schema.Find("look")!.Choices.Select(choice => choice.Label), Is.EqualTo(new[] { "Plain", "#acme_bigCards", "Tiny" }), "[FormBuilderChoice], else the C# name.");
            Assert.That(schema.Find("looks")!.Choices, Is.EqualTo(schema.Find("look")!.Choices), "A list of choices has them too.");
            Assert.That(schema.Find("title")!.Choices, Is.Empty);
            Assert.That(schema.Find("title")!.Settings, Is.Empty);
            Assert.That(schema.Find("nothing"), Is.Null);
        });
    }

    [Test]
    public void ASettingsOrAChoicesLabel_IsNeverBlank()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new FormBuilderSettingAttribute(" "), "a setting's label");
            Assert.Throws<ArgumentNullException>(() => _ = new FormBuilderSettingAttribute(null!), "a setting without a label");
            Assert.Throws<ArgumentException>(() => _ = new FormBuilderChoiceAttribute(" "), "a choice's label");
            Assert.Throws<ArgumentNullException>(() => _ = new FormBuilderChoiceAttribute(null!), "a choice without a label");
            Assert.That(new FormBuilderChoiceAttribute("#acme_bigCards").Label, Is.EqualTo("#acme_bigCards"));
        });
    }

    [Test]
    public void TheDefaults_AreWhatANewInstanceHolds_AsSettingsStoreThem()
    {
        var schema = FieldSettingsReader.Read(typeof(Defaults));

        string? DefaultOf(string name) => schema.Find(name)!.Default?.GetRawText();
        Assert.Multiple(() =>
        {
            Assert.That(DefaultOf("on"), Is.EqualTo("true"));
            Assert.That(DefaultOf("off"), Is.EqualTo("false"));
            Assert.That(DefaultOf("count"), Is.EqualTo("4"));
            Assert.That(DefaultOf("zero"), Is.EqualTo("0"));
            Assert.That(DefaultOf("title"), Is.EqualTo("\"Hello\""));
            Assert.That(DefaultOf("none"), Is.Null, "A null is left out.");
            Assert.That(DefaultOf("look"), Is.EqualTo("\"bigCards\""));
            Assert.That(DefaultOf("tags"), Is.EqualTo("[\"b\",\"a\"]"), "A list keeps its order.");
            Assert.That(DefaultOf("empty"), Is.Null, "An empty list is left out.");
            Assert.That(DefaultOf("box"), Is.EqualTo("{\"from\":1,\"to\":5}"), "Sorted, and without its empty list and null.");
            Assert.That(DefaultOf("blank"), Is.Null, "An object with nothing left is left out.");
            Assert.That(DefaultOf("maybe"), Is.Null);
            Assert.That(schema.Find("box")!.Settings.Select(setting => setting.Default?.GetRawText()), Is.EqualTo(new[] { "5", "1", null, null }));
        });
    }

    [Test]
    public void TheSettings_AreInTheirGroups_ThenByOrder_ThenAsDeclared_WithTheBaseClassFirst()
    {
        var schema = FieldSettingsReader.Read(typeof(Ordered));

        Assert.That(schema.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "c", "z", "b", "e", "d", "a" }));
    }

    [Test]
    public void TheAttribute_GivesTheSettingWhatTheInspectorShows_AndTheJsonNameMayBeItsOwn()
    {
        var setting = FieldSettingsReader.Read(typeof(Described)).Settings.Single();

        Assert.That(setting, Is.EqualTo(new FieldSettingDescriptor
        {
            Name = "intro",
            Kind = FieldSettingKind.LocalizedText,
            Label = "#jfbFieldSettings_intro",
            Description = "Shown above the field.",
            Editor = "richIntro",
            Group = FieldSettingGroup.Appearance,
            Order = 3,
            SupportsMergeTags = true,
            IsPublic = true,
            Rules = new FieldSettingRules { MaximumLength = FieldSettingRules.TextLength },
        }).Using<FieldSettingDescriptor>(SameSetting));
    }

    [Test]
    public void ASettingWithinAnother_AndAFieldPickersKind_AreWhatTheAttributeSays()
    {
        var schema = FieldSettingsReader.Read(typeof(Boxed));

        Assert.Multiple(() =>
        {
            Assert.That(schema.Find("to")!.Within, Is.Null, "The box has its own place.");
            Assert.That(schema.Find("field")!.Within, Is.EqualTo("to"));
            Assert.That(schema.Find("field")!.Picks, Is.EqualTo(FieldPick.Email));
            Assert.That(schema.Find("files")!.Picks, Is.EqualTo(FieldPick.File), "A list of fields picks too.");
            Assert.That(schema.Find("any")!.Picks, Is.EqualTo(FieldPick.Any), "Any field unless the setting says otherwise.");
            Assert.That(schema.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "to", "field", "files", "any" }), "A setting within another keeps its place among the settings.");
        });
    }

    [Test]
    public void APublicSetting_CountsAtTheTop_NotInsideAnObject()
    {
        var schema = FieldSettingsReader.Read(typeof(PublicObject));

        Assert.Multiple(() =>
        {
            Assert.That(schema.Find("box")!.IsPublic, Is.True);
            Assert.That(schema.Find("box")!.Settings.Single().IsPublic, Is.False, "What visitors see of an object is the object.");
            Assert.That(schema.Find("note")!.IsPublic, Is.False, "Off unless the type says so.");
        });
    }

    [Test]
    public void AJsonIgnoredProperty_IsNoSetting()
    {
        var schema = FieldSettingsReader.Read(typeof(WithIgnored));

        Assert.That(schema.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "title" }));
    }

    [Test]
    public void AClassOfNoSettings_HasAnEmptySchema()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldSettingsReader.Read(typeof(NoFieldSettings)).Settings, Is.Empty);
            Assert.That(FieldSettingsSchema.Empty.Settings, Is.Empty);
            Assert.That(new FieldSettingsSchema(default).Settings, Is.Empty, "A default array is no settings.");
        });
    }

    [TestCaseSource(nameof(Refusals))]
    public void Refuses(Type type, string says)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => FieldSettingsReader.Read(type));

        Assert.That(refused!.Message, Does.Contain(says));
    }

    [Test]
    public void EveryProblem_IsNamedAtOnce()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => FieldSettingsReader.Read(typeof(BrokenTwice)));

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Message, Does.Contain("BrokenTwice.Ratio is of the type System.Double"));
            Assert.That(refused.Message, Does.Contain("BrokenTwice.Loose is not a setting"));
            Assert.That(refused.Message, Does.Contain("BrokenTwice.Page holds a key"));
        });
    }

    [Test]
    public void ATypeWhoseSettingsCannotBeRead_IsRefusedAsItIsCreated_NamingTheTypeAndItsClass()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => _ = new DoubleFieldType());

        Assert.That(
            refused!.Message,
            Does.StartWith($"The settings of the field type 'acme.double' ({typeof(DoubleFieldType).FullName}) cannot be read: ")
                .And.Contain("WithDouble.Ratio is of the type System.Double"));
    }

    [Test]
    public void TheSettingsJson_ReadsWhatTheSchemaDescribes_AndRefusesTheRest()
    {
        static Defaults Read(string json) => JsonSerializer.Deserialize<Defaults>(json, FieldSettingsJson.Options)!;

        var read = Read("""{"count":7,"look":"x-small","box":{"from":2},"tags":["c"]}""");
        Assert.Multiple(() =>
        {
            Assert.That((read.Count, read.Look, read.Box.From, read.Box.To, read.Tags), Is.EqualTo((7, Look.Tiny, 2, 5, (IReadOnlyList<string>)["c"])));
            Assert.That(read.On, Is.True, "What the JSON leaves out keeps its default.");
            Assert.That(() => Read("""{"colour":"red"}"""), Throws.InstanceOf<JsonException>(), "A setting the class lacks.");
            Assert.That(() => Read("""{"look":1}"""), Throws.InstanceOf<JsonException>(), "A choice by number.");
            Assert.That(() => Read("""{"title":null}"""), Throws.InstanceOf<JsonException>(), "Null where the class says there is a text.");
            Assert.That(() => Read("""{"count":"7"}"""), Throws.InstanceOf<JsonException>(), "A number as text.");
            Assert.That(() => Read("""{"count":7,"count":8}"""), Throws.InstanceOf<JsonException>(), "A setting twice.");
        });
    }

    // The record's own equality compares the arrays by reference.
    private static bool SameSetting(FieldSettingDescriptor a, FieldSettingDescriptor b) =>
        a with { Choices = [], Settings = [], Default = null } == b with { Choices = [], Settings = [], Default = null }
        && a.Choices.SequenceEqual(b.Choices)
        && a.Settings.Length == b.Settings.Length
        && a.Default?.GetRawText() == b.Default?.GetRawText();

    public enum Look
    {
        Plain = 2,
        [FormBuilderChoice("#acme_bigCards")]
        BigCards = 0,
        [JsonStringEnumMemberName("x-small")]
        Tiny = 1,
    }

    [Flags]
    public enum Weekdays
    {
        None = 0,
        Monday = 1,
        Tuesday = 2,
    }

    public enum AliasedSize
    {
        Small,
        Large,
        Big = Large,
    }

    public sealed class EveryKind
    {
        [FormBuilderSetting("On")]
        public bool On { get; init; }

        [FormBuilderSetting("Maybe")]
        public bool? Maybe { get; init; }

        [FormBuilderSetting("Count")]
        public int Count { get; init; }

        [FormBuilderSetting("Bytes")]
        public long Bytes { get; init; }

        [FormBuilderSetting("Limit")]
        public int? Limit { get; init; }

        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [FormBuilderSetting("Amount", Kind = FieldSettingKind.Number)]
        public string? Amount { get; init; }

        [FormBuilderSetting("Day", Kind = FieldSettingKind.Date)]
        public string? Day { get; init; }

        [FormBuilderSetting("At", Kind = FieldSettingKind.Time)]
        public string? At { get; init; }

        [FormBuilderSetting("Caption")]
        public LocalizedText? Caption { get; init; }

        [FormBuilderSetting("Body")]
        public LocalizedHtml? Body { get; init; }

        [FormBuilderSetting("Look")]
        public Look Look { get; init; }

        [FormBuilderSetting("Page", Editor = "contentPicker")]
        public Guid? Page { get; init; }

        [FormBuilderSetting("Extra", Editor = "dateBound")]
        public JsonElement? Extra { get; init; }

        [FormBuilderSetting("Sizes")]
        public IReadOnlyList<int> Sizes { get; init; } = [];

        [FormBuilderSetting("Tags", Kind = FieldSettingKind.List)]
        public IReadOnlyList<string> Tags { get; init; } = [];

        [FormBuilderSetting("Days", Kind = FieldSettingKind.Date)]
        public IReadOnlyList<string> Days { get; init; } = [];

        [FormBuilderSetting("Amounts", Kind = FieldSettingKind.Number)]
        public IReadOnlyList<string> Amounts { get; init; } = [];

        [FormBuilderSetting("Looks")]
        public IReadOnlyList<Look> Looks { get; init; } = [];

        [FormBuilderSetting("Pages", Editor = "contentPicker")]
        public IReadOnlyList<Guid> Pages { get; init; } = [];

        [FormBuilderSetting("Box")]
        public Box Box { get; init; } = new();

        [FormBuilderSetting("Frame", Kind = FieldSettingKind.Nested)]
        public Box? Frame { get; init; }
    }

    public sealed class Box
    {
        [FormBuilderSetting("To")]
        public int To { get; init; } = 5;

        [FormBuilderSetting("From")]
        public int From { get; init; } = 1;

        [FormBuilderSetting("Label")]
        public string? Label { get; init; }

        [FormBuilderSetting("Notes")]
        public IReadOnlyList<string> Notes { get; init; } = [];
    }

    public sealed class Blank
    {
        [FormBuilderSetting("Label")]
        public string? Label { get; init; }
    }

    public sealed class Defaults
    {
        [FormBuilderSetting("On")]
        public bool On { get; init; } = true;

        [FormBuilderSetting("Off")]
        public bool Off { get; init; }

        [FormBuilderSetting("Count")]
        public int Count { get; init; } = 4;

        [FormBuilderSetting("Zero")]
        public int Zero { get; init; }

        [FormBuilderSetting("Title")]
        public string Title { get; init; } = "Hello";

        [FormBuilderSetting("None")]
        public string? None { get; init; }

        [FormBuilderSetting("Look")]
        public Look Look { get; init; } = Look.BigCards;

        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = ["b", "a"];

        [FormBuilderSetting("Empty")]
        public IReadOnlyList<string> Empty { get; init; } = [];

        [FormBuilderSetting("Box")]
        public Box Box { get; init; } = new();

        [FormBuilderSetting("Blank")]
        public Blank Blank { get; init; } = new();

        [FormBuilderSetting("Maybe")]
        public Box? Maybe { get; init; }
    }

    public class OrderedBase
    {
        [FormBuilderSetting("Z")]
        public string? Z { get; init; }
    }

    public sealed class Ordered : OrderedBase
    {
        [FormBuilderSetting("A", Group = FieldSettingGroup.Advanced)]
        public string? A { get; init; }

        [FormBuilderSetting("B")]
        public string? B { get; init; }

        [FormBuilderSetting("C", Order = -1)]
        public string? C { get; init; }

        [FormBuilderSetting("D", Group = FieldSettingGroup.Validation)]
        public string? D { get; init; }

        [FormBuilderSetting("E")]
        public string? E { get; init; }
    }

    public sealed class Described
    {
        [FormBuilderSetting(
            "#jfbFieldSettings_intro",
            Description = "Shown above the field.",
            Editor = "richIntro",
            Group = FieldSettingGroup.Appearance,
            Order = 3,
            SupportsMergeTags = true,
            Public = true)]
        [JsonPropertyName("intro")]
        public LocalizedText? Introduction { get; init; }
    }

    public sealed class Boxed
    {
        [FormBuilderSetting("To", Editor = "recipients")]
        public IReadOnlyList<string> To { get; init; } = [];

        [FormBuilderSetting("Field", Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.Email, Within = "to")]
        public Guid? Field { get; init; }

        [FormBuilderSetting("Files", Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.File)]
        public IReadOnlyList<Guid> Files { get; init; } = [];

        [FormBuilderSetting("Any", Kind = FieldSettingKind.Key, Editor = "fieldPicker")]
        public Guid? Any { get; init; }
    }

    public sealed class WithinAPlainBox
    {
        [FormBuilderSetting("To")]
        public IReadOnlyList<string> To { get; init; } = [];

        [FormBuilderSetting("Creator", Within = "to")]
        public bool Creator { get; init; }
    }

    public sealed class WithinRecipientsANote
    {
        [FormBuilderSetting("To", Editor = "recipients")]
        public IReadOnlyList<string> To { get; init; } = [];

        [FormBuilderSetting("Note", Within = "to")]
        public string? Note { get; init; }
    }

    public sealed class TwoCreatorsWithin
    {
        [FormBuilderSetting("To", Editor = "recipients")]
        public IReadOnlyList<string> To { get; init; } = [];

        [FormBuilderSetting("First", Within = "to")]
        public bool First { get; init; }

        [FormBuilderSetting("Second", Within = "to")]
        public bool Second { get; init; }
    }

    public sealed class WithinNothing
    {
        [FormBuilderSetting("Field", Within = "box")]
        public string? Field { get; init; }
    }

    public sealed class WithinItself
    {
        [FormBuilderSetting("To", Within = "to")]
        public string? To { get; init; }
    }

    public sealed class WithinTwice
    {
        [FormBuilderSetting("To")]
        public string? To { get; init; }

        [FormBuilderSetting("Copy", Within = "to")]
        public string? Copy { get; init; }

        [FormBuilderSetting("Field", Within = "copy")]
        public string? Field { get; init; }
    }

    public sealed class WithinInsideAnObject
    {
        [FormBuilderSetting("Box")]
        public WithinInside Box { get; init; } = new();
    }

    public sealed class WithinInside
    {
        [FormBuilderSetting("To")]
        public string? To { get; init; }

        [FormBuilderSetting("Field", Within = "to")]
        public string? Field { get; init; }
    }

    public sealed class PicksWithoutPicker
    {
        [FormBuilderSetting("Field", Kind = FieldSettingKind.Key, Editor = "mediaPicker", Picks = FieldPick.Email)]
        public Guid? Field { get; init; }
    }

    public sealed class PublicObject
    {
        [FormBuilderSetting("Box", Public = true)]
        public PublicInside Box { get; init; } = new();

        [FormBuilderSetting("Note")]
        public string? Note { get; init; }
    }

    public sealed class PublicInside
    {
        [FormBuilderSetting("Width", Public = true)]
        public int Width { get; init; }
    }

    public sealed class WithIgnored
    {
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [JsonIgnore]
        public string Shown => Title ?? "";
    }

    public sealed class Unmarked
    {
        public string? Title { get; init; }
    }

    public sealed class IgnoredSetting
    {
        [FormBuilderSetting("Title")]
        [JsonIgnore]
        public string? Title { get; init; }
    }

    public sealed class BadlyNamed
    {
        [FormBuilderSetting("Title")]
        [JsonPropertyName("Big-Title")]
        public string? Title { get; init; }
    }

    public sealed class TwiceNamed
    {
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [FormBuilderSetting("Heading")]
        [JsonPropertyName("title")]
        public string? Heading { get; init; }
    }

    public sealed class GetOnly
    {
        [FormBuilderSetting("Title")]
        public string? Title { get; }
    }

    public sealed class PrivatelySet
    {
        [FormBuilderSetting("Title")]
        public string? Title { get; private set; }
    }

    public sealed class WithDouble
    {
        [FormBuilderSetting("Ratio")]
        public double Ratio { get; init; }
    }

    public sealed class WithDateTime
    {
        [FormBuilderSetting("At")]
        public DateTime At { get; init; }
    }

    public sealed class WithMap
    {
        [FormBuilderSetting("Map")]
        public Dictionary<string, string>? Map { get; init; }
    }

    public sealed class WithList
    {
        [FormBuilderSetting("Tags")]
        public List<string>? Tags { get; init; }
    }

    public sealed class WithArray
    {
        [FormBuilderSetting("Tags")]
        public string[]? Tags { get; init; }
    }

    public sealed class Outer
    {
        [FormBuilderSetting("Middle")]
        public Middle Middle { get; init; } = new();
    }

    public sealed class Middle
    {
        [FormBuilderSetting("Inner")]
        public Blank? Inner { get; init; }
    }

    public sealed class ListOfBooleans
    {
        [FormBuilderSetting("Flags")]
        public IReadOnlyList<bool> Flags { get; init; } = [];
    }

    public sealed class ListOfObjects
    {
        [FormBuilderSetting("Boxes")]
        public IReadOnlyList<Box> Boxes { get; init; } = [];
    }

    public sealed class ListOfNullables
    {
        [FormBuilderSetting("Sizes")]
        public IReadOnlyList<int?> Sizes { get; init; } = [];
    }

    public sealed class KeyWithoutEditor
    {
        [FormBuilderSetting("Page")]
        public Guid? Page { get; init; }
    }

    public sealed class JsonWithoutEditor
    {
        [FormBuilderSetting("Extra")]
        public JsonElement? Extra { get; init; }
    }

    public sealed class KeysWithoutEditor
    {
        [FormBuilderSetting("Pages")]
        public IReadOnlyList<Guid> Pages { get; init; } = [];
    }

    public sealed class BlankEditor
    {
        [FormBuilderSetting("Title", Editor = " ")]
        public string? Title { get; init; }
    }

    public sealed class NumberAsDate
    {
        [FormBuilderSetting("Count", Kind = FieldSettingKind.Date)]
        public int Count { get; init; }
    }

    public sealed class BooleanAsText
    {
        [FormBuilderSetting("On", Kind = FieldSettingKind.Text)]
        public bool On { get; init; }
    }

    public sealed class NumbersAsTimes
    {
        [FormBuilderSetting("Counts", Kind = FieldSettingKind.Time)]
        public IReadOnlyList<int> Counts { get; init; } = [];
    }

    public sealed class ObjectAsText
    {
        [FormBuilderSetting("Box", Kind = FieldSettingKind.Text)]
        public Box? Box { get; init; }
    }

    public sealed class WithFlags
    {
        [FormBuilderSetting("Days")]
        public Weekdays Days { get; init; }
    }

    public sealed class WithAlias
    {
        [FormBuilderSetting("Size")]
        public AliasedSize Size { get; init; }
    }

    public sealed class ListOfFlags
    {
        [FormBuilderSetting("Days")]
        public IReadOnlyList<Weekdays> Days { get; init; } = [];
    }

    public sealed class WithoutConstructor(string title)
    {
        [FormBuilderSetting("Title")]
        public string Title { get; init; } = title;
    }

    public abstract class AbstractSettings
    {
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class WithBadObject
    {
        [FormBuilderSetting("Inside")]
        public WithoutConstructor? Inside { get; init; }
    }

    public sealed class NullWhereNotNullable
    {
        [FormBuilderSetting("Title")]
        public string Title { get; init; } = null!;
    }

    public sealed class ThrowingConstructor
    {
        public ThrowingConstructor() => throw new InvalidOperationException("Not today.");

        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class BrokenTwice
    {
        [FormBuilderSetting("Ratio")]
        public double Ratio { get; init; }

        public string? Loose { get; init; }

        [FormBuilderSetting("Page")]
        public Guid Page { get; init; }
    }

    public sealed class DoubleFieldType() : FormBuilderFieldTypeBase<WithDouble>(new FieldTypeDeclaration
    {
        Alias = "acme.double",
        Name = "Double",
        Icon = "icon-thing",
        PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    });
}
