using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The field types for the designer (ADR-0057): every type of the registry in its order, with the palette's and the
/// inspector's groups, each type's declaration, settings with their rules, and availability, for any user of the Forms
/// section. The answer on a site with the built-in types matches the committed snapshot <c>docs/api/field-types.json</c>,
/// whose <c>#</c> keys the backoffice's dictionary test holds both languages to. After a deliberate change, run the
/// fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FieldTypeApiTests : ManagementApiTestBase
{
    private const string FieldTypes = "field-type";

    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(FieldTypeApiTests).Assembly, "docs", "api", "field-types.json");

    [Test]
    public async Task TheBuiltInTypes_AreServedToAUserOfTheSectionWithoutGrants_AsTheReviewedSnapshotHolds()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, FieldTypes);
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var served = answer.Body!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(SnapshotPath, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, check the backoffice's dictionaries, commit it, and run again without the variable.");
        }

        Assert.That(File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the field types changed: run with JFB_UPDATE_SNAPSHOTS=1, review the diff and the dictionaries");
    }

    [Test]
    public async Task EachTypeOfTheRegistry_IsServedInItsOrder_WithItsDeclaration_ItsSettings_AndItsAvailability()
    {
        var registry = GetRequiredService<FieldTypeRegistry>();
        var body = (await CallAsync("designer", HttpMethod.Get, FieldTypes)).Body!;
        var types = body["types"]!.AsArray().Select(type => type!.AsObject()).ToList();
        JsonObject Type(string alias) => types.Single(type => Text(type["alias"]) == alias);
        JsonObject Setting(JsonObject type, string name) => type["settings"]!.AsArray().Select(setting => setting!.AsObject()).Single(setting => Text(setting["name"]) == name);

        Assert.Multiple(() =>
        {
            Assert.That(types.Select(type => Text(type["alias"])), Is.EqualTo(registry.Types.Select(type => type.Declaration.Alias)), "the registry's order: the built-in types in palette order");
            Assert.That(Pairs(body["paletteGroups"]!), Is.EqualTo(new[]
            {
                "text #jfbFieldPalette_text", "numbersAndDates #jfbFieldPalette_numbersAndDates", "choices #jfbFieldPalette_choices", "people #jfbFieldPalette_people",
                "files #jfbFieldPalette_files", "structure #jfbFieldPalette_structure", "content #jfbFieldPalette_content", "protection #jfbFieldPalette_protection",
            }));
            Assert.That(Pairs(body["settingGroups"]!), Is.EqualTo(new[] { "essentials #jfbFieldSettingGroups_essentials", "validation #jfbFieldSettingGroups_validation", "appearance #jfbFieldSettingGroups_appearance", "advanced #jfbFieldSettingGroups_advanced" }));
            Assert.That(types.Select(type => (bool)type["availability"]!["isAvailable"]!), Is.All.True, "no built-in type needs configuration yet");

            var text = Type("text");
            Assert.That((Text(text["name"]), Text(text["icon"]), Text(text["paletteGroup"]), (bool)text["holdsValue"]!, Text(text["storage"]), Text(text["ruleKind"])), Is.EqualTo(("#jfbFieldTypes_text", registry.FindType("text")!.Declaration.Icon, "text", true, "text", "text")));
            Assert.That(text["features"]!.AsArray().Select(Text), Is.EqualTo(new[] { "label", "hideLabel", "help", "placeholder", "required", "defaultValue", "prefill", "autocomplete", "patternCheck", "matchesFieldCheck", "uniquePerFormCheck", "visibilityRules" }));
            Assert.That(text["messages"]!.AsArray().Select(Text), Is.EqualTo(FieldMessageProperties.All.Where(message => registry.FindType("text")!.Declaration.Messages.HasFlag(message.Kind)).Select(message => message.Name)));
            Assert.That(text["settings"]!.AsArray().Select(setting => Text(setting!["name"])), Is.EqualTo(registry.FindType("text")!.SettingsSchema.Settings.Select(setting => setting.Name)), "in the inspector's order");

            var maxLength = Setting(text, "maxLength");
            Assert.That((Text(maxLength["kind"]), Text(maxLength["label"]), Text(maxLength["editor"]), Text(maxLength["group"]), maxLength["default"]!.ToJsonString(), (bool)maxLength["isPublic"]!), Is.EqualTo(("wholeNumber", "#jfbFieldSettings_textMaxLength", "wholeNumber", "validation", "255", true)));
            Assert.That((Text(maxLength["rules"]!["minimum"]), Text(maxLength["rules"]!["maximum"]), (bool)maxLength["rules"]!["required"]!), Is.EqualTo(("1", "4000", false)), "the rules of ADR-0056");

            var layout = Setting(Type("radio"), "layout");
            Assert.That(layout["choices"]!.AsArray().Select(choice => $"{Text(choice!["value"])} {Text(choice["label"])}"), Is.EqualTo(new[] { "inherit #jfbFieldChoices_layoutInherit", "vertical #jfbFieldChoices_layoutVertical", "horizontal #jfbFieldChoices_layoutHorizontal", "cards #jfbFieldChoices_layoutCards" }));

            var domains = Setting(Type("email"), "domains");
            Assert.That((Text(domains["kind"]), (bool)domains["isPublic"]!), Is.EqualTo(("nested", false)), "an email field's domains stay on the server");
            Assert.That(domains["settings"]!.AsArray().Select(setting => $"{Text(setting!["name"])} {Text(setting["kind"])} {Text(setting["itemKind"])}"), Is.EqualTo(new[] { "mode choice ", "list list text" }));

            var heading = Setting(Type("heading"), "text");
            Assert.That(((bool)heading["rules"]!["required"]!, (int)heading["rules"]!["maximumLength"]!), Is.EqualTo((true, 300)));

            Assert.That(((bool)Type("hidden")["serverOnlyDefault"]!, (bool)Type("captcha")["onePerForm"]!, (bool)Type("captcha")["holdsValue"]!, Text(Type("captcha")["ruleKind"])), Is.EqualTo((true, true, false, string.Empty)));
            Assert.That((bool)Type("dropdown")["multipleSetting"]!, Is.True);
        });
    }

    private static string Text(JsonNode? node) => node?.GetValue<string>() ?? string.Empty;

    private static IEnumerable<string> Pairs(JsonNode groups) => groups.AsArray().Select(group => $"{Text(group!["alias"])} {Text(group["name"])}");
}
