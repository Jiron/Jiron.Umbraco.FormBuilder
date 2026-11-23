using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The form engine's model of a form, which the browser makes from the form endpoint's answer as Core makes it from the
/// definition (ROADMAP 6.4c, ADR-0111): for the full definition fixture and each of the demo's forms, the committed
/// fixture <c>tests/fixtures/definitions/logic.json</c> holds the form and the fields' logic as the endpoint answers
/// them, and <see cref="DefinitionLogic.From"/>'s model as the page knows it (the switched-on rules, and neither
/// actions nor outcomes, which the projection does not carry). <c>packages/runtime</c>'s test makes the same model from
/// the same answer. After a deliberate change, run the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the file
/// and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
public sealed class LogicModelFixtureTests
{
    private static readonly string FixturePath = RepositoryRoot.Path(typeof(LogicModelFixtureTests).Assembly, "tests", "fixtures", "definitions", "logic.json");

    [Test]
    public async Task TheModelOfEachForm_IsWhatTheBrowserMakesOfTheEndpointsAnswer_AsTheFixtureHoldsIt()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-US", isDefault: true);
        var dictionary = new MergeTagDictionary(umbraco.Dictionary, umbraco.Languages);
        var site = MergeTagTestSite.Create(umbraco: umbraco);
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<GeneralSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SettingsSnapshot<GeneralSettings>(new GeneralSettings(), 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        var forms = new VisitorForms(
            dictionary,
            new EditorHtml(),
            TestFieldTypes.Registry,
            site.Service,
            new MergeTagRecallForms(TestFieldTypes.Registry, new OptionListTestSite().Options(), new MergeTagCultures()),
            new MergeTagCultures(),
            new ConsentWordings(site.Service, dictionary, new EditorHtml(), TestFieldTypes.Registry),
            settings);

        var fixture = new JsonObject();
        foreach (var (name, json) in Definitions())
        {
            var definition = DefinitionFixtures.Read(json);
            var answer = await forms.WriteAsync(PublicProjection.Create(definition, TestFieldTypes.Registry), MergeTagTestSite.Scope(definition, culture: "en-US"));
            fixture[name] = new JsonObject
            {
                ["form"] = JsonNode.Parse(answer.Form),
                ["fields"] = JsonNode.Parse(answer.Fields),
                ["logic"] = Model(DefinitionLogic.From(definition, TestFieldTypes.Registry)),
            };
        }

        var written = fixture.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Default }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(FixturePath, written, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {FixturePath}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(File.Exists(FixturePath), Is.True, $"the reviewed fixture {FixturePath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(FixturePath)).ReplaceLineEndings("\n"), Is.EqualTo(written), "the model or the endpoint's answer changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    // The full definition fixture, and the demo's forms by their file names.
    private static IEnumerable<(string Name, string Json)> Definitions()
    {
        yield return ("full", DefinitionFixtures.Full);
        var demo = RepositoryRoot.Path(typeof(LogicModelFixtureTests).Assembly, "demo", "Jiron.Umbraco.FormBuilder.Demo", "Seeding", "Forms");
        foreach (var path in Directory.EnumerateFiles(demo, "*.json").Order(StringComparer.Ordinal))
        {
            yield return (Path.GetFileNameWithoutExtension(path), File.ReadAllText(path));
        }
    }

    // The model as the page knows it, in the shared vectors' form: every list and value written, a rule only when it is
    // switched on, and no actions or outcomes.
    private static JsonObject Model(LogicForm form) => new()
    {
        ["pages"] = Array(form.Pages, page => new JsonObject
        {
            ["id"] = page.Id,
            ["rules"] = Rules(page.Rules),
            ["groups"] = Array(page.Groups, group => new JsonObject { ["id"] = group.Id, ["rules"] = Rules(group.Rules), ["fields"] = Array(group.Fields, Field) }),
        }),
        ["validationRules"] = Array(form.ValidationRules.Where(rule => rule.Enabled), rule => new JsonObject
        {
            ["id"] = rule.Id,
            ["field"] = rule.Field,
            ["enabled"] = true,
            ["match"] = rule.Match,
            ["conditions"] = Items(rule.Conditions),
        }),
    };

    private static JsonObject Field(LogicField field) => new()
    {
        ["id"] = field.Id,
        ["kind"] = field.Kind is { } kind ? EnumNames<FieldKind>.ByValue[kind] : null,
        ["canBeRequired"] = field.CanBeRequired,
        ["alias"] = field.Alias,
        ["calculation"] = field.Calculation is { } calculation ? new JsonObject { ["formula"] = calculation.Formula, ["decimals"] = calculation.Decimals } : null,
        ["required"] = field.Required,
        ["rules"] = Rules(field.Rules),
        ["fields"] = Array(field.Fields, Field),
        ["rows"] = Array(field.Rows, row => new JsonObject { ["id"] = row.Id, ["kind"] = EnumNames<FieldKind>.ByValue[row.Kind] }),
    };

    private static JsonArray Rules(IEnumerable<LogicRule> rules) =>
        Array(rules.Where(rule => rule.Enabled), rule => new JsonObject { ["effect"] = rule.Effect, ["enabled"] = true, ["match"] = rule.Match, ["conditions"] = Items(rule.Conditions) });

    private static JsonArray Items(IEnumerable<LogicItem> items) => Array(items, item => item switch
    {
        LogicGroup group => new JsonObject { ["match"] = group.Match, ["conditions"] = Items(group.Conditions) },
        LogicCondition condition => Condition(condition),
        _ => throw new ArgumentOutOfRangeException(nameof(items), item, "A rule item is a condition or a group of conditions."),
    });

    private static JsonObject Condition(LogicCondition condition)
    {
        var json = new JsonObject();
        if (condition.Field is { } field)
        {
            json["field"] = field;
        }

        if (condition.Context is { } context)
        {
            json["context"] = context;
        }

        json["op"] = condition.Operator;
        if (condition.Value is { } value)
        {
            json["value"] = Operand(value);
        }

        return json;
    }

    private static JsonNode Operand(Operand operand) => operand switch
    {
        TextOperand text => JsonValue.Create(text.Value),
        FieldOperand field => new JsonObject { ["field"] = field.Field },
        TodayOperand today => new JsonObject { ["today"] = today.Expression },
        ListOperand list => Array(list.Items, Operand),
        _ => new JsonObject { ["invalid"] = true },
    };

    private static JsonArray Array<T>(IEnumerable<T> items, Func<T, JsonNode> write) => new([.. items.Select(write)]);
}
