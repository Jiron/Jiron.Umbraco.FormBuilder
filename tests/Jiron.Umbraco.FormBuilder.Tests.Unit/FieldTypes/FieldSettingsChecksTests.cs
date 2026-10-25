using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

// NUnit has a [Range] of its own.
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// Publishing holds a field's settings to its type's settings schema (ADR-0056): each setting of its kind, keeping the
/// rules of its attributes, and no setting the schema lacks, each problem at the path of its value; only then does the
/// type's own check read them as its class, and a check that throws is reported, not passed on.
/// </summary>
public sealed class FieldSettingsChecksTests
{
    private const string FieldPath = "$.pages[0].groups[0].fields[0]";

    private static readonly FieldTypeRegistry Types = TestFieldTypes.Build(builder => builder
        .Append<ProbeFieldType>()
        .Append<ThrowingFieldType>()
        .Append<CancellingFieldType>()
        .Append<MisplacedFieldType>()
        .Append<PlainFieldType>());

    public static IEnumerable<TestCaseData> Cases()
    {
        static TestCaseData Case(string name, Action<JsonObject> change, params string[] problems) =>
            new TestCaseData(change, problems).SetName($"Checks({name})");
        static JsonObject Text(string text) => new() { [string.Empty] = text };

        yield return Case("settings that keep every rule", _ => { });
        yield return Case("no settings, so neither required one", s => s.Clear(), "setting_missing at .settings.title", "setting_missing at .settings.heading");
        yield return Case("a setting the type lacks", s => s["colour"] = "red", "setting_unknown at .settings.colour");

        // Required settings.
        yield return Case("a blank required text", s => s["title"] = "  ", "setting_missing at .settings.title");
        yield return Case("a required localised text whose default is blank", s => s["heading"] = new JsonObject { [string.Empty] = " ", ["de"] = "Hallo" }, "setting_missing at .settings.heading['']");
        yield return Case("a required localised text blank in a culture only", s => s["heading"] = new JsonObject { [string.Empty] = "Hi", ["de"] = " " });

        // Kinds.
        yield return Case("a yes or no as text", s => s["on"] = "yes", "setting_invalid at .settings.on");
        yield return Case("a whole number as text", s => s["count"] = "3", "setting_invalid at .settings.count");
        yield return Case("a text as a number", s => s["title"] = 5, "setting_invalid at .settings.title");
        yield return Case("a number as a JSON number", s => s["ratio"] = 2, "setting_invalid at .settings.ratio");
        yield return Case("a number with an exponent", s => s["ratio"] = "1e2", "setting_invalid at .settings.ratio");
        yield return Case("a number with a plus", s => s["ratio"] = "+2", "setting_invalid at .settings.ratio");
        yield return Case("a number of 16 digits", s => s["ratio"] = "12.34567890123456", "setting_invalid at .settings.ratio");
        yield return Case("a date that is not one", s => s["day"] = "2026-02-30", "setting_invalid at .settings.day");
        yield return Case("a date", s => s["day"] = "2028-02-29");
        yield return Case("a time that is not one", s => s["at"] = "24:00", "setting_invalid at .settings.at");
        yield return Case("a time with seconds", s => s["at"] = "23:59:59");
        yield return Case("a localised text as text", s => s["prompt"] = "Pick", "setting_invalid at .settings.prompt");
        yield return Case("a localised text without its default", s => s["prompt"] = new JsonObject { ["de"] = "Wahl" }, "setting_invalid at .settings.prompt");
        yield return Case("a localised text with a name that is no culture", s => s["prompt"] = new JsonObject { [string.Empty] = "a", ["1a"] = "b" }, "setting_invalid at .settings.prompt['1a']");
        yield return Case("a localised text with a culture twice", s => s["prompt"] = new JsonObject { [string.Empty] = "a", ["DE"] = "b", ["de"] = "c" }, "setting_invalid at .settings.prompt.de");
        yield return Case("a localised text with a number", s => s["prompt"] = new JsonObject { [string.Empty] = 5 }, "setting_invalid at .settings.prompt['']");
        yield return Case("a localised text in a region's culture", s => s["prompt"] = new JsonObject { [string.Empty] = "a", ["de-CH"] = "b" });
        yield return Case("a choice", s => s["look"] = "bigCards");
        yield return Case("a choice in another letter case", s => s["look"] = "BigCards", "setting_invalid at .settings.look");
        yield return Case("a choice the enum lacks", s => s["look"] = "huge", "setting_invalid at .settings.look");
        yield return Case("a key", s => s["page"] = "5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c");
        yield return Case("a key in capitals", s => s["page"] = "5C0F9B5E-3A4D-4B8E-9B51-6F1D2C3A4B5C", "setting_invalid at .settings.page");
        yield return Case("a key that is none", s => s["page"] = "home", "setting_invalid at .settings.page");
        yield return Case("a list as text", s => s["tags"] = "a", "setting_invalid at .settings.tags");
        yield return Case("a list with an item of the wrong kind", s => s["tags"] = new JsonArray(1, "a"), "setting_invalid at .settings.tags[0]");
        yield return Case("an object as a number", s => s["box"] = 5, "setting_invalid at .settings.box");
        yield return Case("free JSON of any shape", s => s["extra"] = new JsonObject { ["a"] = new JsonArray(1, "b") });

        // [Range].
        yield return Case("a whole number below its range", s => s["count"] = 0, "setting_invalid at .settings.count");
        yield return Case("a whole number above its range", s => s["count"] = 11, "setting_invalid at .settings.count");
        yield return Case("a whole number at its lowest", s => s["count"] = 1);
        yield return Case("a whole number at its highest", s => s["count"] = 10);
        yield return Case("an int beyond what an int holds", s => s["plain"] = 3000000000, "setting_invalid at .settings.plain");
        yield return Case("an int below zero", s => s["plain"] = -5);
        yield return Case("a number at its exclusive lowest", s => s["ratio"] = "0.5", "setting_invalid at .settings.ratio");
        yield return Case("a number just above its exclusive lowest", s => s["ratio"] = "0.51");
        yield return Case("a number at its highest, with a trailing zero", s => s["ratio"] = "100.0");
        yield return Case("a number above its highest", s => s["ratio"] = "100.5", "setting_invalid at .settings.ratio");
        yield return Case("a list item below its range", s => s["sizes"] = new JsonArray(0, 3), "setting_invalid at .settings.sizes[0]");

        // [StringLength], and the limits of a text without it.
        yield return Case("a text shorter than its fewest", s => s["code"] = "a", "setting_invalid at .settings.code");
        yield return Case("a text longer than its most", s => s["code"] = "abcdefghijk", "setting_invalid at .settings.code");
        yield return Case("a text of its most", s => s["code"] = "abcdefghij");
        yield return Case("a text without a rule, of 2,000 characters", s => s["free"] = new string('a', FieldSettingRules.TextLength));
        yield return Case("a text without a rule, longer", s => s["free"] = new string('a', FieldSettingRules.TextLength + 1), "setting_invalid at .settings.free");
        yield return Case("a localised text too long in a culture", s => s["prompt"] = new JsonObject { [string.Empty] = "ok", ["de"] = "zu lang" }, "setting_invalid at .settings.prompt.de");
        yield return Case("rich text longer than 50,000 characters", s => s["body"] = Text(new string('a', FieldSettingRules.HtmlLength + 1)), "setting_invalid at .settings.body['']");
        yield return Case("a list item too long", s => s["tags"] = new JsonArray("a", "abcde"), "setting_invalid at .settings.tags[1]");

        // [Length], and each value once.
        yield return Case("a list with fewer items than its fewest", s => s["tags"] = new JsonArray("a"), "setting_invalid at .settings.tags");
        yield return Case("a list with more items than its most", s => s["tags"] = new JsonArray("a", "b", "c", "d"), "setting_invalid at .settings.tags");
        yield return Case("a list with a text twice", s => s["tags"] = new JsonArray("a", "a"), "setting_invalid at .settings.tags[1]");
        yield return Case("a list with a number twice, as numbers compare", s => s["ratios"] = new JsonArray("1.5", "1.50"), "setting_invalid at .settings.ratios[1]");
        yield return Case("a list with a time twice, as times compare", s => s["times"] = new JsonArray("09:30", "09:30:00"), "setting_invalid at .settings.times[1]");
        yield return Case("a list with a date twice", s => s["days"] = new JsonArray("2026-12-24", "2026-12-24"), "setting_invalid at .settings.days[1]");
        yield return Case("a list with a choice twice", s => s["looks"] = new JsonArray("plain", "plain"), "setting_invalid at .settings.looks[1]");
        yield return Case("a list with a key twice", s => s["pages"] = new JsonArray("5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c", "5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c"), "setting_invalid at .settings.pages[1]");

        // [AllowedValues].
        yield return Case("a text it does not allow", s => s["side"] = "up", "setting_invalid at .settings.side");
        yield return Case("a text it allows", s => s["side"] = "right");
        yield return Case("an allowed text in another letter case", s => s["side"] = "Right", "setting_invalid at .settings.side");

        // Objects of settings.
        yield return Case("an object without its required setting", s => s["box"] = new JsonObject { ["size"] = 1 }, "setting_missing at .settings.box.label");
        yield return Case("an object with a setting it lacks", s => s["box"] = new JsonObject { ["label"] = "x", ["shade"] = 1 }, "setting_unknown at .settings.box.shade");
        yield return Case("an object's whole number out of its range", s => s["box"] = new JsonObject { ["label"] = "x", ["size"] = 0 }, "setting_invalid at .settings.box.size");

        // The type's own check runs only on settings that keep the schema.
        yield return Case("a lower limit above the upper one", s => (s["low"], s["high"]) = (5, 3), "setting_invalid at .settings.low");
        yield return Case("a lower limit above the upper one, and a rule broken", s => (s["low"], s["high"], s["count"]) = (5, 3, 0), "setting_invalid at .settings.count");
    }

    [TestCaseSource(nameof(Cases))]
    public void Publishing_HoldsTheSettingsToTheirSchema(Action<JsonObject> change, string[] problems)
    {
        var settings = new JsonObject { ["title"] = "Hello", ["heading"] = new JsonObject { [string.Empty] = "Hi" } };
        change(settings);

        Assert.That(Problems("probe", settings).Select(problem => $"{problem.Code} at {problem.Path[FieldPath.Length..]}"), Is.EqualTo(problems));
    }

    [Test]
    public void EachProblem_SaysWhatIsWrong()
    {
        string MessageOf(Action<JsonObject> change)
        {
            var settings = new JsonObject { ["title"] = "Hello", ["heading"] = new JsonObject { [string.Empty] = "Hi" } };
            change(settings);
            return Problems("probe", settings).Single().Message;
        }

        Assert.Multiple(() =>
        {
            Assert.That(MessageOf(s => s["colour"] = "red"), Is.EqualTo("A probe field has no setting 'colour'."));
            Assert.That(MessageOf(s => s.Remove("title")), Is.EqualTo("The setting 'title' needs a value."));
            Assert.That(MessageOf(s => s["title"] = " "), Is.EqualTo("The setting 'title' needs a text that is not blank."));
            Assert.That(MessageOf(s => s["heading"] = new JsonObject { [string.Empty] = " " }), Is.EqualTo("The default text of the setting 'heading' needs a text that is not blank."));
            Assert.That(MessageOf(s => s["on"] = "yes"), Is.EqualTo("The setting 'on' is true or false, not a string."));
            Assert.That(MessageOf(s => s["count"] = 0), Is.EqualTo("The setting 'count' is 1 to 10, not 0."));
            Assert.That(MessageOf(s => s["ratio"] = "0.5"), Is.EqualTo("The setting 'ratio' is more than 0.5 and at most 100, not 0.5."));
            Assert.That(MessageOf(s => s["ratio"] = "1e2"), Is.EqualTo("The setting 'ratio' is a number such as \"-12.5\" (digits, with a minus and a point if need be), not \"1e2\"."));
            Assert.That(MessageOf(s => s["code"] = "abcdefghijk"), Is.EqualTo("The setting 'code' has at most 10 characters, not 11."));
            Assert.That(MessageOf(s => s["prompt"] = new JsonObject { [string.Empty] = "ok", ["de"] = "zu lang" }), Is.EqualTo("The setting 'prompt' in 'de' has at most 5 characters, not 7."));
            Assert.That(MessageOf(s => s["tags"] = new JsonArray("a")), Is.EqualTo("The setting 'tags' has at least 2 items, not 1."));
            Assert.That(MessageOf(s => s["tags"] = new JsonArray("a", "abcde")), Is.EqualTo("An item of the setting 'tags' has at most 4 characters, not 5."));
            Assert.That(MessageOf(s => s["tags"] = new JsonArray("a", "a")), Is.EqualTo("The setting 'tags' holds each value once; \"a\" is there before."));
            Assert.That(MessageOf(s => s["look"] = "huge"), Is.EqualTo("The setting 'look' is one of 'plain', 'bigCards', not 'huge'."));
            Assert.That(MessageOf(s => s["side"] = "up"), Is.EqualTo("The setting 'side' is one of 'left', 'right', not 'up'."));
            Assert.That(MessageOf(s => s["day"] = "31.12.2026"), Is.EqualTo("The setting 'day' is a date, YYYY-MM-DD, not \"31.12.2026\"."));
            Assert.That(MessageOf(s => s["box"] = new JsonObject { ["label"] = "x", ["shade"] = 1 }), Is.EqualTo("The setting 'box' has no setting 'shade'."));
            Assert.That(MessageOf(s => (s["low"], s["high"]) = (5, 3)), Is.EqualTo("The low limit, 5, is above the high one, 3."));
        });
    }

    [Test]
    public void AnOwnCheckThatThrows_IsAProblemWithTheSettings_AndCancellingIsPassedOn()
    {
        var thrown = Problems("throwing", null).Single();
        var misplaced = Problems("misplaced", null).Single();

        Assert.Multiple(() =>
        {
            Assert.That((thrown.Code, thrown.Path, thrown.Message), Is.EqualTo((DefinitionErrors.SettingInvalid, $"{FieldPath}.settings", "The field type 'throwing' failed to check these settings: Broken.")));
            Assert.That((misplaced.Code, misplaced.Path), Is.EqualTo((DefinitionErrors.SettingInvalid, $"{FieldPath}.settings")), "a place that is no place");
            Assert.That(misplaced.Message, Does.Contain("'Max' is no place in the settings"));
            Assert.Throws<OperationCanceledException>(() => Problems("cancelling", null));
        });
    }

    [Test]
    public void ATypeWithoutAnOwnCheck_FindsNothing_ButItsSchemaStillHolds()
    {
        var type = Types.FindType("plain")!;
        var problems = new DefinitionProblems();
        var field = new FormField { Key = Guid.NewGuid(), Type = "plain", Settings = SettingsObject.Parse("""{"count":0}""") };
        type.CheckSettings(new FieldSettingsCheck(field, "$.settings", problems));

        Assert.Multiple(() =>
        {
            Assert.That(problems.Found, Is.Zero, "the interface's own check finds nothing");
            Assert.That(Problems("plain", new JsonObject { ["count"] = 3 }), Is.Empty);
            Assert.That(Problems("plain", new JsonObject { ["count"] = 0 }).Select(problem => problem.Path), Is.EqualTo(new[] { $"{FieldPath}.settings.count" }));
        });
    }

    [Test]
    public void AFieldOfATypeTheSiteLacks_HasItsSettingsLeftAlone()
    {
        Assert.That(Problems("colourPicker", new JsonObject { ["shade"] = 5 }).Select(problem => problem.Code), Is.EqualTo(new[] { DefinitionErrors.UnknownType }));
    }

    [Test]
    public void TheSettings_ReadAsTheClass_WithItsDefaultsForWhatIsNotGiven()
    {
        var type = (ProbeFieldType)Types.FindType("probe")!;
        var given = type.ReadSettings(SettingsObject.Parse("""{"count":3,"look":"bigCards","title":"Hi"}"""));
        var none = type.ReadSettings(null);

        Assert.Multiple(() =>
        {
            Assert.That((given.Count, given.Look, given.Title, given.High), Is.EqualTo((3, ProbeLook.BigCards, "Hi", 10)));
            Assert.That((none.Count, none.Title), Is.EqualTo((5, (string?)null)));
            Assert.That(none.Tags, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    public static IEnumerable<TestCaseData> Places()
    {
        yield return new TestCaseData("max", "$.f.settings.max").SetName("Reports(a setting)");
        yield return new TestCaseData("domains.list", "$.f.settings.domains.list").SetName("Reports(a setting in an object)");
        yield return new TestCaseData("allowed[2]", "$.f.settings.allowed[2]").SetName("Reports(a list's item)");
        yield return new TestCaseData("box.sizes[10]", "$.f.settings.box.sizes[10]").SetName("Reports(an item in an object)");
    }

    [TestCaseSource(nameof(Places))]
    public void AnOwnCheck_ReportsAtThePlaceItNames(string setting, string path)
    {
        var problems = new DefinitionProblems();
        new FieldSettingsCheck(new FormField { Key = Guid.NewGuid(), Type = "probe" }, "$.f.settings", problems).Invalid(setting, "Wrong.");

        Assert.That(problems.ToReadOnlyList(), Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.SettingInvalid, path, "Wrong.") }));
    }

    [TestCase("")]
    [TestCase("Max")]
    [TestCase("max.")]
    [TestCase(".max")]
    [TestCase("a..b")]
    [TestCase("[0]")]
    [TestCase("a[01]")]
    [TestCase("a[-1]")]
    [TestCase("a[1234567890]")]
    [TestCase("a b")]
    [TestCase("a['b']")]
    public void AnOwnCheck_NamesNoPlaceOutsideTheSettings(string setting)
    {
        var check = new FieldSettingsCheck(new FormField { Key = Guid.NewGuid(), Type = "probe" }, "$.f.settings", new DefinitionProblems());

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => check.Invalid(setting, "Wrong."));
            Assert.Throws<ArgumentException>(() => check.Invalid("max", " "), "a message that is blank");
        });
    }

    private static IReadOnlyList<DefinitionProblem> Problems(string type, JsonObject? settings)
    {
        var field = new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000001", ["type"] = type };
        // Settings with nothing in them are written as none.
        if (settings is { Count: > 0 })
        {
            field["settings"] = settings.DeepClone();
        }

        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["key"] = "f0000000-0000-4000-8000-000000000001",
            ["pages"] = new JsonArray(new JsonObject
            {
                ["key"] = "10000000-0000-4000-8000-000000000001",
                ["groups"] = new JsonArray(new JsonObject { ["key"] = "20000000-0000-4000-8000-000000000001", ["fields"] = new JsonArray(field) }),
            }),
        };
        return new StructuralPublishCheck(DefinitionFixtures.Serializer(), Types).Check(DefinitionFixtures.Read(document.ToJsonString()));
    }

    private static FieldTypeDeclaration Declare(string alias) => new()
    {
        Alias = alias,
        Name = alias,
        Icon = "icon-settings",
        PaletteGroup = FieldPaletteGroup.Content,
        Features = FieldFeatures.VisibilityRules,
    };

    public enum ProbeLook
    {
        Plain,
        BigCards,
    }

    // Every kind of setting, with every rule.
    public sealed class ProbeFieldType() : FormBuilderFieldTypeBase<ProbeSettings>(Declare("probe"))
    {
        protected override void CheckSettings(ProbeSettings settings, FieldSettingsCheck check)
        {
            if (settings.Low > settings.High)
            {
                check.Invalid("low", $"The low limit, {settings.Low}, is above the high one, {settings.High}.");
            }
        }
    }

    public sealed class ProbeSettings
    {
        [Required]
        [StringLength(20)]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [Required]
        [FormBuilderSetting("Heading")]
        public LocalizedText? Heading { get; init; }

        [FormBuilderSetting("On")]
        public bool On { get; init; }

        [Range(1, 10)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; } = 5;

        [FormBuilderSetting("Plain")]
        public int Plain { get; init; }

        [Range(0.5, 100, MinimumIsExclusive = true)]
        [FormBuilderSetting("Ratio", Kind = FieldSettingKind.Number)]
        public string? Ratio { get; init; }

        [FormBuilderSetting("Day", Kind = FieldSettingKind.Date)]
        public string? Day { get; init; }

        [FormBuilderSetting("At", Kind = FieldSettingKind.Time)]
        public string? At { get; init; }

        [StringLength(10, MinimumLength = 2)]
        [FormBuilderSetting("Code")]
        public string? Code { get; init; }

        [FormBuilderSetting("Free")]
        public string? Free { get; init; }

        [StringLength(5)]
        [FormBuilderSetting("Prompt")]
        public LocalizedText? Prompt { get; init; }

        [FormBuilderSetting("Body")]
        public LocalizedHtml? Body { get; init; }

        [FormBuilderSetting("Look")]
        public ProbeLook Look { get; init; }

        [FormBuilderSetting("Page", Editor = "contentPicker")]
        public Guid? Page { get; init; }

        [Length(2, 3)]
        [StringLength(4)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = ["a", "b"];

        [Range(1, 5)]
        [FormBuilderSetting("Sizes")]
        public IReadOnlyList<int> Sizes { get; init; } = [];

        [FormBuilderSetting("Ratios", Kind = FieldSettingKind.Number)]
        public IReadOnlyList<string> Ratios { get; init; } = [];

        [FormBuilderSetting("Times", Kind = FieldSettingKind.Time)]
        public IReadOnlyList<string> Times { get; init; } = [];

        [FormBuilderSetting("Days", Kind = FieldSettingKind.Date)]
        public IReadOnlyList<string> Days { get; init; } = [];

        [FormBuilderSetting("Looks")]
        public IReadOnlyList<ProbeLook> Looks { get; init; } = [];

        [FormBuilderSetting("Pages", Editor = "contentPicker")]
        public IReadOnlyList<Guid> Pages { get; init; } = [];

        [AllowedValues("left", "right")]
        [FormBuilderSetting("Side")]
        public string? Side { get; init; }

        [FormBuilderSetting("Box")]
        public ProbeBox? Box { get; init; }

        [FormBuilderSetting("Extra", Editor = "json")]
        public JsonElement? Extra { get; init; }

        [FormBuilderSetting("Low")]
        public int Low { get; init; }

        [FormBuilderSetting("High")]
        public int High { get; init; } = 10;
    }

    public sealed class ProbeBox
    {
        [Required]
        [FormBuilderSetting("Label")]
        public string? Label { get; init; }

        [Range(1, 9)]
        [FormBuilderSetting("Size")]
        public int? Size { get; init; }
    }

    public sealed class ThrowingFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(Declare("throwing"))
    {
        protected override void CheckSettings(NoFieldSettings settings, FieldSettingsCheck check) => throw new InvalidOperationException("Broken.");
    }

    public sealed class CancellingFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(Declare("cancelling"))
    {
        protected override void CheckSettings(NoFieldSettings settings, FieldSettingsCheck check) => throw new OperationCanceledException();
    }

    public sealed class MisplacedFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(Declare("misplaced"))
    {
        protected override void CheckSettings(NoFieldSettings settings, FieldSettingsCheck check) => check.Invalid("Max", "Too high.");
    }

    // A type written against the interface alone, whose own check is the interface's.
    public sealed class PlainFieldType : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare("plain");

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(PlainSettings));
    }

    public sealed class PlainSettings
    {
        [Range(1, 5)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; } = 1;
    }
}
