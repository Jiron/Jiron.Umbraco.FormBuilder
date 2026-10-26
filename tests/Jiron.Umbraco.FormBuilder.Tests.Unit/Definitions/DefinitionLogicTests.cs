using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// A definition in the form engine's terms (ROADMAP 5.2a, ADR-0061): what <see cref="DefinitionLogic.From"/> makes of
/// the publishable document of the structure tests, and what the engine decides from it. The engine's own behaviour is
/// the vectors' (tests/vectors/rules.json).
/// </summary>
public sealed class DefinitionLogicTests
{
    private const string Page2 = "10000000-0000-4000-8000-000000000002";
    private const string Group2 = "20000000-0000-4000-8000-000000000002";
    private const string Group3 = "20000000-0000-4000-8000-000000000003";
    private const string GridRow = "31000000-0000-4000-8000-000000000001";
    private const string CheckAcross = "41000000-0000-4000-8000-000000000001";
    private const string Action = "50000000-0000-4000-8000-000000000001";
    private const string Redirect = "60000000-0000-4000-8000-000000000001";
    private const string Message = "60000000-0000-4000-8000-000000000002";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues = new Dictionary<string, IReadOnlyList<string>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>> NoRows =
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>>();

    // Field n of the publishable document, by its key's last digits.
    private static string K(int n) => $"30000000-0000-4000-8000-{n:x12}";

    [Test]
    public void From_ThePublishableDocument_GivesEachElementItsIdKindAndRules()
    {
        var form = From(StructureTests.Publishable);
        var fields = form.Pages[0].Groups[0].Fields;
        var others = form.Pages[0].Groups[1].Fields;
        var guests = fields[5].Rules.Single();
        var condition = (LogicCondition)guests.Conditions.Single();

        Assert.Multiple(() =>
        {
            Assert.That(form.Pages.Select(page => page.Id), Is.EqualTo(new[] { "10000000-0000-4000-8000-000000000001", Page2 }));
            Assert.That(fields.Select(field => field.Id), Is.EqualTo(Enumerable.Range(1, 8).Select(K)));
            Assert.That(fields.Select(field => field.Kind), Is.EqualTo(new FieldKind?[] { FieldKind.Text, FieldKind.Text, FieldKind.Text, FieldKind.Choice, FieldKind.Choice, FieldKind.Number, null, null }), "a name and a heading hold no value rules compare");
            Assert.That((guests.Effect, guests.Enabled, guests.Match), Is.EqualTo(("show", true, "all")));
            Assert.That(condition, Is.EqualTo(new LogicCondition(K(4), null, "equals", new TextOperand("ch"))));
            Assert.That(others[0].Rows, Is.EqualTo(new[] { new LogicGridRow(GridRow, FieldKind.Choice) }), "a grid's rows are subjects of conditions");
            Assert.That(others[1].Fields.Select(child => (child.Id, child.Kind)), Is.EqualTo(new[] { (K(0xb), (FieldKind?)FieldKind.Text) }), "a repeatable group's child fields");
            Assert.That(others[2].Rules.Single().Effect, Is.EqualTo("require"));
            Assert.That(form.Pages[1].Rules.Single().Conditions.Single(), Is.EqualTo(new LogicCondition(null, "query.ref", "startsWith", new TextOperand("partner"))));
            Assert.That(form.ValidationRules.Select(rule => (rule.Id, rule.Field, rule.Enabled, rule.Match)), Is.EqualTo(new[] { (CheckAcross, K(6), true, "all") }));
            Assert.That(form.Actions.Select(action => (action.Id, action.Rules.Single().Effect)), Is.EqualTo(new[] { (Action, "run") }));
            Assert.That(form.Outcomes.Select(outcome => (outcome.Id, outcome.Rules.Count)), Is.EqualTo(new[] { (Redirect, 1), (Message, 0) }));
        });
    }

    [Test]
    public void From_ConditionsInNestedGroups_KeepEveryMatchAndOperand()
    {
        var form = From(Changed(d => GuestsCondition(d).ReplaceWith(new JsonObject
        {
            ["match"] = "any",
            ["conditions"] = new JsonArray(
                new JsonObject { ["field"] = K(4), ["op"] = "isOneOf", ["value"] = new JsonArray("ch", new JsonObject { ["field"] = K(5) }) },
                new JsonObject { ["context"] = "today", ["op"] = "greaterThan", ["value"] = new JsonObject { ["today"] = "+1d" } }),
        })));

        var group = (LogicGroup)form.Pages[0].Groups[0].Fields[5].Rules.Single().Conditions.Single();
        var isOneOf = (LogicCondition)group.Conditions[0];
        var list = (ListOperand)isOneOf.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(group.Match, Is.EqualTo("any"));
            Assert.That(list.Items, Is.EqualTo(new Operand[] { new TextOperand("ch"), new FieldOperand(K(5)) }));
            Assert.That(group.Conditions[1], Is.EqualTo(new LogicCondition(null, "today", "greaterThan", new TodayOperand("+1d"))));
        });
    }

    [Test]
    public void From_ASwitchedOffAction_IsLeftOut_AndASwitchedOffRuleKept()
    {
        var form = From(Changed(d =>
        {
            d["actions"]![0]!["enabled"] = false;
            d["pages"]![0]!["groups"]![0]!["fields"]![5]!["rules"]![0]!["enabled"] = false;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(form.Actions, Is.Empty, "an action that is switched off never runs");
            Assert.That(form.Pages[0].Groups[0].Fields[5].Rules.Single().Enabled, Is.False, "a switched-off rule is still checked");
        });
    }

    [Test]
    public void From_AFieldWhoseTypeTheSiteLacks_HoldsNoValueAndHasNoRows()
    {
        var form = From(Changed(d => d["pages"]![0]!["groups"]![1]!["fields"]![0]!["type"] = "colourPicker"));
        var grid = form.Pages[0].Groups[1].Fields[0];

        Assert.That((grid.Kind, grid.Rows.Count), Is.EqualTo(((FieldKind?)null, 0)));
    }

    [Test]
    public void From_EveryFieldKeepsItsAlias_AndACalculationItsFormulaAndPlaces()
    {
        var form = From(Changed(d =>
        {
            AddCalculation(d, 0x22, "total", new JsonObject { ["formula"] = "guests * 12", ["format"] = new JsonObject { ["decimals"] = 0 } });
            AddCalculation(d, 0x23, "share", new JsonObject { ["formula"] = "total / 3" });
        }));
        var fields = form.Pages[0].Groups[0].Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(field => field.Alias), Is.EqualTo(new[] { "name", "email", "confirmEmail", "country", "city", "guests", "fullName", null, "total", "share" }), "a heading has no alias");
            Assert.That(fields.Take(8).Select(field => field.Calculation), Is.All.Null);
            Assert.That(fields.Skip(8).Select(field => field.Calculation), Is.EqualTo(new[] { new LogicCalculation("guests * 12", 0), new LogicCalculation("total / 3", null) }), "two places when unset are the engine's");
        });
    }

    [Test]
    public void From_ACalculationWhoseSettingsDoNotFit_HasAnEmptyFormula_WhichTheEngineRefuses()
    {
        var form = From(Changed(d => AddCalculation(d, 0x22, "total", new JsonObject { ["formula"] = 12 })));

        Assert.Multiple(() =>
        {
            Assert.That(form.Pages[0].Groups[0].Fields[8].Calculation, Is.EqualTo(new LogicCalculation(string.Empty, null)));
            Assert.That(
                () => FormRules.Evaluate(form, new FormAnswers(NoValues, NoRows), Context("de-CH", NoValues)),
                Throws.TypeOf<LogicException>().With.Property(nameof(LogicException.Code)).EqualTo(LogicErrors.InvalidFormula));
            Assert.That(() => DefinitionLogic.Calculation(null!, null), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void TheEngine_WorksOutACalculation_AndRulesReadItsResult()
    {
        var form = From(Changed(d =>
        {
            AddCalculation(d, 0x22, "total", new JsonObject { ["formula"] = "guests * 12.5" });

            // The name shows while the total stays under 100.
            d["pages"]![0]!["groups"]![0]!["fields"]![0]!["rules"] = new JsonArray(new JsonObject
            {
                ["key"] = "40000000-0000-4000-8000-000000000010",
                ["effect"] = "show",
                ["conditions"] = new JsonArray(new JsonObject { ["field"] = K(0x22), ["op"] = "lessThan", ["value"] = "100" }),
            });
        }));
        FormState Evaluate(string guests) => FormRules.Evaluate(
            form,
            new FormAnswers(new Dictionary<string, IReadOnlyList<string>> { [K(4)] = ["ch"], [K(6)] = [guests], [K(0x22)] = ["1"] }, NoRows),
            Context("de-CH", NoValues));
        var seven = Evaluate("7");
        var nine = Evaluate("9");

        Assert.Multiple(() =>
        {
            Assert.That(seven.Calculations, Is.EqualTo(new Dictionary<string, string?> { [K(0x22)] = "87.5" }), "seven guests at 12.50, whatever was posted");
            Assert.That(seven.Hidden, Does.Not.Contain(K(1)));
            Assert.That(nine.Calculations, Is.EqualTo(new Dictionary<string, string?> { [K(0x22)] = "112.5" }));
            Assert.That(nine.Hidden, Does.Contain(K(1)), "the name hides at 100 or more");
        });
    }

    [Test]
    public void TheEngine_WithNothingAnswered_HidesWhatTheRulesHide()
    {
        var state = FormRules.Evaluate(From(StructureTests.Publishable), new FormAnswers(NoValues, NoRows), Context("de-CH", NoValues));

        Assert.Multiple(() =>
        {
            // Guests show for Switzerland only; the second group hides without a name; the second page shows for partners.
            Assert.That(state.Hidden, Is.EqualTo(new[] { K(6), Group2, K(9), K(0xa), K(0xc), K(0xd), Page2, Group3, K(0xe) }));
            Assert.That(state.Required, Is.Empty);
            Assert.That(state.Messages, Is.Empty);
            Assert.That(state.Rows.Keys, Is.EqualTo(new[] { K(0xa) }));
            Assert.That(state.Rows[K(0xa)], Is.Empty);
            Assert.That(state.Actions, Is.Empty, "the newsletter is hidden, so it is not checked");
            Assert.That(state.Outcome, Is.EqualTo(Redirect), "the redirect is for German");
        });
    }

    [Test]
    public void TheEngine_WithEverythingAnswered_ShowsRequiresAndRunsWhatTheRulesSay()
    {
        var values = new Dictionary<string, IReadOnlyList<string>>
        {
            [K(1)] = ["Anna"],
            [K(2)] = ["anna@example.org"],
            [K(4)] = ["ch"],
            [K(6)] = ["12"],
            [K(0xc)] = ["true"],
        };
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>>
        {
            [K(0xa)] = [new Dictionary<string, IReadOnlyList<string>> { [K(0xb)] = ["Bea"] }],
        };
        var query = new Dictionary<string, IReadOnlyList<string>> { ["ref"] = ["partner-1"] };

        var state = FormRules.Evaluate(From(StructureTests.Publishable), new FormAnswers(values, rows), Context("fr-CH", query));

        Assert.Multiple(() =>
        {
            Assert.That(state.Hidden, Is.Empty);
            Assert.That(state.Required, Is.EqualTo(new[] { K(0xc) }), "the newsletter is required once there is an email");
            Assert.That(state.Messages.Keys, Is.EqualTo(new[] { K(6) }));
            Assert.That(state.Messages[K(6)], Is.EqualTo(new[] { CheckAcross }), "more than ten guests");
            Assert.That(state.Rows[K(0xa)].Select(row => (row.Hidden.Count, row.Required.Count, row.Messages.Count)), Is.EqualTo(new[] { (0, 0, 0) }));
            Assert.That(state.Actions, Is.EqualTo(new[] { Action }));
            Assert.That(state.Outcome, Is.EqualTo(Message), "the redirect is for German, so the message is next");
        });
    }

    [Test]
    public void TheEngineAndTheAdapter_RefuseNoArguments()
    {
        var form = From(StructureTests.Publishable);
        var answers = new FormAnswers(NoValues, NoRows);
        var context = Context("de-CH", NoValues);

        Assert.Multiple(() =>
        {
            Assert.That(() => DefinitionLogic.From(null!, TestFieldTypes.Registry), Throws.ArgumentNullException);
            Assert.That(() => DefinitionLogic.From(DefinitionFixtures.Read(StructureTests.Publishable), null!), Throws.ArgumentNullException);
            Assert.That(() => FormRules.Evaluate(null!, answers, context), Throws.ArgumentNullException);
            Assert.That(() => FormRules.Evaluate(form, null!, context), Throws.ArgumentNullException);
            Assert.That(() => FormRules.Evaluate(form, answers, null!), Throws.ArgumentNullException);
        });
    }

    private static LogicForm From(string json) => DefinitionLogic.From(DefinitionFixtures.Read(json), TestFieldTypes.Registry);

    // A calculation at the end of the first group, field 8 when it is the first field added there.
    private static void AddCalculation(JsonObject d, int key, string alias, JsonObject settings) =>
        d["pages"]![0]!["groups"]![0]!["fields"]!.AsArray().Add(new JsonObject { ["key"] = K(key), ["type"] = "calculation", ["alias"] = alias, ["settings"] = settings });

    private static string Changed(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(StructureTests.Publishable)!.AsObject();
        change(document);
        return document.ToJsonString();
    }

    private static JsonNode GuestsCondition(JsonObject d) => d["pages"]![0]!["groups"]![0]!["fields"]![5]!["rules"]![0]!["conditions"]![0]!;

    private static EvaluationContext Context(string culture, IReadOnlyDictionary<string, IReadOnlyList<string>> query) =>
        new("2026-06-15T10:00:00Z", "Europe/Zurich", culture, new Member(false, []), query, []);
}
