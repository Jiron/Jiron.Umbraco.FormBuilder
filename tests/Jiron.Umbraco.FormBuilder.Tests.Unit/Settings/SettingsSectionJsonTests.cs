using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// How sections are stored, migrated and combined (ADR-0008, ADR-0043): defaults for what a row leaves out, old rows
/// migrated in order, site rows over the global row field by field, and the paths a save changed.
/// </summary>
[TestFixture]
public sealed class SettingsSectionJsonTests
{
    private static readonly ISettingsSectionMigration[] Migrations = [new SampleCountToNumber(), new SampleTitleToName()];

    [Test]
    public void AnEmptyRow_ReadsAsTheDefaults()
    {
        Assert.That(SettingsSectionJson.Read<SampleSettings>([]), Is.EqualTo(new SampleSettings()).Using<SampleSettings>(Same));
    }

    [Test]
    public void ARow_KeepsTheDefaults_OfWhatItLeavesOut_OrSetsToNull_AndDropsWhatTheSectionHasNot()
    {
        var read = SettingsSectionJson.Read<SampleSettings>(Node("""{"count":7,"name":null,"limits":{"label":"L"},"retired":true,"mode":"loudAndClear"}"""));

        Assert.That(read, Is.EqualTo(new SampleSettings { Count = 7, Mode = SampleMode.LoudAndClear, Limits = new SampleLimits { Label = "L" } }).Using<SampleSettings>(Same));
    }

    [TestCase("""{"mode":1}""", Description = "an enum is text, never a number")]
    [TestCase("""{"mode":"loud"}""", Description = "an enum value the section has not")]
    [TestCase("""{"count":"seven"}""", Description = "text for a number")]
    [TestCase("""{"limits":5}""", Description = "a number for an object")]
    public void AValueThatDoesNotFitItsField_DoesNotRead(string json)
    {
        Assert.That(() => SettingsSectionJson.Read<SampleSettings>(Node(json)), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void ANestedNull_KeepsItsDefault_AsATopLevelOneDoes()
    {
        Assert.That(SettingsSectionJson.Read<SampleSettings>(Node("""{"limits":{"max":null,"label":"x"}}""")).Limits, Is.EqualTo(new SampleLimits { Label = "x" }));
    }

    [Test]
    public void AnOldRow_IsMigratedStepByStep_InOrder()
    {
        var migrated = SettingsSectionJson.Migrate<SampleSettings>("""{"title":"Old","count":"7"}""", 1, Migrations);

        Assert.That(SettingsSectionJson.Read<SampleSettings>(migrated), Is.EqualTo(new SampleSettings { Name = "Old", Count = 7 }).Using<SampleSettings>(Same));
    }

    [Test]
    public void ARowOfTheCurrentVersion_IsNotMigrated()
    {
        Assert.That(SettingsSectionJson.Migrate<SampleSettings>("""{"title":"Kept","count":"7"}""", 3, Migrations).ToJsonString(), Is.EqualTo("""{"title":"Kept","count":"7"}"""));
    }

    [Test]
    public void ARowFromANewerVersion_IsTakenAsItIs()
    {
        Assert.That(SettingsSectionJson.Migrate<SampleSettings>("""{"name":"Newer","extra":1}""", 4, Migrations).ToJsonString(), Is.EqualTo("""{"name":"Newer","extra":1}"""));
    }

    [Test]
    public void AMissingStep_Throws_NamingTheSectionAndTheVersion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => SettingsSectionJson.Migrate<SampleSettings>("{}", 0, Migrations),
                Throws.InvalidOperationException.With.Message.EqualTo("The settings section 'sample' has no migration from schema version 0 to 1."));
            Assert.That(
                () => SettingsSectionJson.Migrate<SampleSettings>("{}", 1, [new SampleTitleToName()]),
                Throws.InvalidOperationException.With.Message.EqualTo("The settings section 'sample' has no migration from schema version 2 to 3."));
        });
    }

    [Test]
    public void AStepThereTwice_Throws()
    {
        Assert.That(
            () => SettingsSectionJson.Migrate<SampleSettings>("{}", 1, [.. Migrations, new SampleTitleToName()]),
            Throws.InvalidOperationException.With.Message.EqualTo("The settings section 'sample' has more than one migration from schema version 1."));
    }

    [Test]
    public void AnotherSectionsSteps_AreNotTaken()
    {
        var migrated = SettingsSectionJson.Migrate<SampleSettings>("""{"title":"Old"}""", 1, [.. Migrations, new OtherSectionsStep()]);

        Assert.Multiple(() =>
        {
            Assert.That(migrated.ContainsKey("other"), Is.False, "a step of another section from the same version");
            Assert.That(() => SettingsSectionJson.Migrate<SampleSharedSettings>("{}", 0, Migrations), Throws.InvalidOperationException.With.Message.Contains("'sampleshared'"));
        });
    }

    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("\"text\"")]
    public void ARowThatIsNotAnObject_DoesNotRead(string json)
    {
        Assert.That(() => SettingsSectionJson.Migrate<SampleSettings>(json, 3, Migrations), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void ARowThatIsNoJson_DoesNotRead()
    {
        Assert.That(() => SettingsSectionJson.Migrate<SampleSettings>("{", 3, Migrations), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void ASite_OverridesTheGlobalValues_FieldByField()
    {
        var global = Node("""{"name":"Global","count":9,"note":"Global note","limits":{"max":20,"label":"G"}}""");
        var site = Node("""{"count":3,"limits":{"label":"S"}}""");

        Assert.That(
            SettingsSectionJson.Combine<SampleSettings>(global, site),
            Is.EqualTo(new SampleSettings { Name = "Global", Count = 3, Note = "Global note", Limits = new SampleLimits { Label = "S" } }).Using<SampleSettings>(Same),
            "a nested object is one field: the site's replaces the global one whole");
    }

    [Test]
    public void ASitesNull_TakesTheDefault_NotTheGlobalValue()
    {
        var combined = SettingsSectionJson.Combine<SampleSettings>(Node("""{"name":"Global","note":"Global note"}"""), Node("""{"name":null,"note":null}"""));

        Assert.That(combined, Is.EqualTo(new SampleSettings()).Using<SampleSettings>(Same));
    }

    [Test]
    public void ASiteRow_CannotOverride_AFieldEverySiteShares_OrOneTheSectionHasNot()
    {
        var combined = SettingsSectionJson.Combine<SampleSettings>(Node("""{"keep":30}"""), Node("""{"keep":5,"retired":1,"Count":4}"""));

        Assert.Multiple(() =>
        {
            Assert.That(combined, Is.EqualTo(new SampleSettings { Keep = 30, Count = 4 }).Using<SampleSettings>(Same));
            Assert.That(SettingsSectionJson.Overrides<SampleSettings>(Node("""{"keep":5,"retired":1,"Count":4,"note":null}""")), Is.EquivalentTo(new[] { "count", "note" }));
        });
    }

    [Test]
    public void ASitesField_InAnotherLetterCase_ReplacesTheGlobalOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingsSectionJson.Combine<SampleSettings>(Node("""{"Count":9}"""), Node("""{"count":2}""")).Count, Is.EqualTo(2));
            Assert.That(SettingsSectionJson.Combine<SampleSettings>(Node("""{"Count":9}"""), Node("""{"count":null}""")).Count, Is.EqualTo(5), "a site's null takes the default");
        });
    }

    [Test]
    public void ASectionEverySiteShares_HasNoSiteFields()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingsSectionJson.SiteFieldNames<SampleSharedSettings>(), Is.Empty);
            Assert.That(SettingsSectionJson.Combine<SampleSharedSettings>(Node("""{"enabled":false}"""), Node("""{"enabled":true}""")).Enabled, Is.False);
            Assert.That(SettingsSectionJson.Overrides<SampleSharedSettings>(Node("""{"enabled":true}""")), Is.Empty);
        });
    }

    [Test]
    public void TheFieldNames_AreTheStoredOnes_AndASiteSetsAllButTheSharedOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingsSectionJson.FieldNames<SampleSettings>(), Is.EquivalentTo(new[] { "name", "count", "mode", "limits", "note", "tags", "keep", "summary" }));
            Assert.That(SettingsSectionJson.SiteFieldNames<SampleSettings>(), Is.EquivalentTo(new[] { "name", "count", "mode", "limits", "note", "tags", "summary" }));
            Assert.That(SettingsSectionJson.FieldNames<GeneralSettings>(), Is.EquivalentTo(new[] { "timeZone" }));
            Assert.That(SettingsSectionJson.SiteFieldNames<GeneralSettings>(), Is.EquivalentTo(new[] { "timeZone" }));
        });
    }

    [Test]
    public void AFieldStoredUnderANameOfItsOwn_HasThatName_AndAWorkedOutValue_IsNoField()
    {
        var stored = SettingsSectionJson.ToNode(new SampleSettings { Description = "About" });

        Assert.Multiple(() =>
        {
            Assert.That(stored["summary"]?.GetValue<string>(), Is.EqualTo("About"));
            Assert.That(stored.ContainsKey("display"), Is.False, "never stored");
            Assert.That(SettingsSectionJson.Combine<SampleSettings>(null, Node("""{"summary":"Site"}""")).Description, Is.EqualTo("Site"), "a site overrides it by its stored name");
        });
    }

    [Test]
    public void AGlobalRow_HoldsEveryField_InCamelCase_WithEnumsAsText_AndNoNulls()
    {
        Assert.That(
            SettingsSectionJson.GlobalJson(new SampleSettings { Mode = SampleMode.LoudAndClear, Tags = ["a"] }),
            Is.EqualTo("""{"name":"Jiron","count":5,"mode":"loudAndClear","limits":{"max":10},"tags":["a"],"keep":50}"""));
    }

    [Test]
    public void ASiteRow_HoldsTheFieldsItOverrides_WithNullForADefaultOfNull()
    {
        var row = SettingsSectionJson.SiteRow(new SampleSettings { Count = 3, Name = "Site" }, ["note", "count"]);

        Assert.Multiple(() =>
        {
            Assert.That(row.ToJsonString(), Is.EqualTo("""{"count":3,"note":null}"""), "in the order of the names");
            Assert.That(SettingsSectionJson.Overrides<SampleSettings>(row), Is.EquivalentTo(new[] { "count", "note" }));
        });
    }

    [Test]
    public void TheChangedPaths_NameLeaves_NestedPaths_AndFieldsAddedOrRemoved_EvenAsNull()
    {
        var before = Node("""{"name":"A","count":1,"limits":{"max":10,"label":"x"},"tags":["a","b"],"note":null,"mode":"quiet"}""");
        var after = Node("""{"name":"A","count":2,"limits":{"max":10,"label":"y"},"tags":["b","a"],"keep":null,"mode":"quiet"}""");

        Assert.Multiple(() =>
        {
            Assert.That(SettingsSectionJson.ChangedFields(before, after), Is.EqualTo(new[] { "count", "keep", "limits.label", "note", "tags" }));
            Assert.That(SettingsSectionJson.ChangedFields(before, before.DeepClone().AsObject()), Is.Empty);
            Assert.That(SettingsSectionJson.ChangedFields(Node("""{"limits":{"max":1}}"""), Node("""{"limits":5}""")), Is.EqualTo(new[] { "limits" }));
        });
    }

    [Test]
    public void APath_ReadsNestedValues_AndItsTopField()
    {
        var node = Node("""{"limits":{"max":10},"count":1}""");

        Assert.Multiple(() =>
        {
            Assert.That(SettingsSectionJson.At(node, "limits.max")!.GetValue<int>(), Is.EqualTo(10));
            Assert.That(SettingsSectionJson.At(node, "limits.label"), Is.Null);
            Assert.That(SettingsSectionJson.At(node, "count.max"), Is.Null);
            Assert.That(SettingsSectionJson.At(node, "none"), Is.Null);
            Assert.That(SettingsSectionJson.TopField("limits.max"), Is.EqualTo("limits"));
            Assert.That(SettingsSectionJson.TopField("count"), Is.EqualTo("count"));
        });
    }

    private static JsonObject Node(string json) => JsonNode.Parse(json)!.AsObject();

    // Records with lists compare their lists by reference, so the tests compare what they store.
    private static bool Same(SampleSettings a, SampleSettings b) => SettingsSectionJson.GlobalJson(a) == SettingsSectionJson.GlobalJson(b);
}

/// <summary>A step of another section, from a version the sample section has a step for too.</summary>
file sealed class OtherSectionsStep : ISettingsSectionMigration
{
    public string Section => SampleSharedSettings.Alias;

    public int FromVersion => 1;

    public void Migrate(JsonObject json) => json["other"] = true;
}
