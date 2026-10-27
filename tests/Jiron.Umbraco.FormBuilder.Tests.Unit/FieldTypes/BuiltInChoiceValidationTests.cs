using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The checks of the built-in choice types (ADR-0058, ADR-0062), each run through its type's <c>Validate</c> as the
/// pipeline runs it, at its edges: the options a choice offers, compared exactly, the selections, the "Other" text, a
/// grid's columns and the rows it needs, a ranking's length and each option and position once (ADR-0069), and a
/// country's allowed list. The shared vectors hold the browser's runtime to the same; these restate the edges in C# and
/// add a field without any options at all, which only a draft can hold.
/// </summary>
public sealed class BuiltInChoiceValidationTests
{
    private static readonly string[] TypesOfTheStep = ["dropdown", "radio", "checkboxes", "grid", "ranking", "country"];

    private static readonly string[] Colours = ["red", "green", "blue", "white"];
    private static readonly string[] Channels = ["email", "phone", "post", "visit"];
    private static readonly string[] Agreement = ["agree", "neutral", "disagree"];
    private static readonly string[] Aspects = ["price", "quality", "service", "speed"];
    private static readonly Guid Price = new("0b6a4f1e-7c2d-4e8a-9f3b-5d1c2a7e6b40");
    private static readonly Guid Staff = new("1c7b5e2f-8d3e-4f9b-8a4c-6e2d3b8f7c51");

    // Each check at its edges: the case, and the errors in order.
    public static IEnumerable<TestCaseData> Edges()
    {
        static TestCaseData Case(string name, ValidationCase given, params string[] errors) =>
            new TestCaseData(given, errors).SetName($"Checks({given.Type}: {name})");
        static ValidationCase Chosen(string type, string? settings, IReadOnlyList<string>? options, params string[] values) =>
            new(type, new FieldAnswers(values)) { Settings = settings, Options = options };
        static ValidationCase Other(string type, string settings, string other, params string[] values) =>
            new(type, new FieldAnswers(values, [new(RadioFieldType.OtherPart, [other])])) { Settings = settings, Options = type == "radio" ? ["yes", "no"] : Channels };
        static ValidationCase Grid(string? settings, IReadOnlyList<string>? options, params (Guid Row, string[] Values)[] rows) =>
            new("grid", new FieldAnswers([], rows.Select(row => KeyValuePair.Create(GridFieldType.RowPart(row.Row), (IEnumerable<string>)row.Values))))
            {
                Settings = settings,
                Options = options,
                Rows = [Price, Staff],
            };

        yield return Case("an option", Chosen("dropdown", null, Colours, "green"));
        yield return Case("not an option", Chosen("dropdown", null, Colours, "purple"), "invalid");
        yield return Case("an option compares exactly", Chosen("dropdown", null, Colours, "Green"), "invalid");
        yield return Case("no options of its own", Chosen("dropdown", null, [], "red"), "invalid");
        yield return Case("a list that could not give its options takes any answer", Chosen("dropdown", null, null, "purple"));
        const string Several = """{"multiple":true,"minSelections":2,"maxSelections":3}""";
        yield return Case("at its fewest", Chosen("dropdown", Several, Colours, "red", "green"));
        yield return Case("below its fewest", Chosen("dropdown", Several, Colours, "red"), "selections");
        yield return Case("at its most", Chosen("dropdown", Several, Colours, "red", "green", "blue"));
        yield return Case("past its most", Chosen("dropdown", Several, Colours, "red", "green", "blue", "white"), "selections");
        yield return Case("several without limits", Chosen("dropdown", """{"multiple":true}""", Colours, "red", "green", "blue", "white"));
        yield return Case("not an option, then too many", Chosen("dropdown", Several, Colours, "red", "green", "blue", "pink"), "invalid", "selections");
        yield return Case("several from a list that could not give its options still count", Chosen("dropdown", """{"multiple":true,"maxSelections":1}""", null, "red", "green"), "selections");
        yield return Case("one choice counts no selections", Chosen("dropdown", """{"minSelections":2}""", Colours, "red"));

        yield return Case("an option", Chosen("radio", null, ["yes", "no"], "no"));
        yield return Case("not an option", Chosen("radio", null, ["yes", "no"], "maybe"), "invalid");
        yield return Case("an option compares exactly", Chosen("radio", null, ["yes", "no"], "No"), "invalid");
        yield return Case("the visitor's own other text", Other("radio", """{"other":true}""", "Now and then"));
        yield return Case("an option beside an other text", Other("radio", """{"other":true}""", "Now and then", "yes"));
        yield return Case("not an option beside an other text", Other("radio", """{"other":true}""", "maybe", "maybe"), "invalid");
        yield return Case("a list that could not give its options takes any answer", Chosen("radio", null, null, "maybe"));

        yield return Case("options", Chosen("checkboxes", null, Channels, "email", "post"));
        yield return Case("not an option", Chosen("checkboxes", null, Channels, "email", "fax"), "invalid");
        yield return Case("at its fewest", Chosen("checkboxes", """{"minSelections":2}""", Channels, "email", "post"));
        yield return Case("below its fewest", Chosen("checkboxes", """{"minSelections":2}""", Channels, "email"), "selections");
        yield return Case("at its most", Chosen("checkboxes", """{"maxSelections":2}""", Channels, "email", "phone"));
        yield return Case("past its most", Chosen("checkboxes", """{"maxSelections":2}""", Channels, "email", "phone", "post"), "selections");
        yield return Case("not an option, then too few", Chosen("checkboxes", """{"minSelections":3}""", Channels, "email", "fax"), "invalid", "selections");
        yield return Case("the visitor's own other text", Other("checkboxes", """{"other":true}""", "fax", "email"));
        yield return Case("the other text counts as one", Other("checkboxes", """{"other":true,"maxSelections":2}""", "pigeon", "email", "phone"), "selections");
        yield return Case("the other text alone at its fewest", Other("checkboxes", """{"other":true,"minSelections":1}""", "pigeon"));
        yield return Case("an other text that repeats an option counts once", Other("checkboxes", """{"other":true,"maxSelections":2}""", "email", "email", "phone"));
        yield return Case("an other text it does not offer is left", Other("checkboxes", """{"maxSelections":2}""", "pigeon", "email", "phone"));
        yield return Case("a list that could not give its options takes any answer", Chosen("checkboxes", """{"maxSelections":3}""", null, "fax", "telex"));

        yield return Case("each row a column", Grid(null, Agreement, (Price, ["agree"]), (Staff, ["disagree"])));
        yield return Case("a row not a column", Grid(null, Agreement, (Price, ["agree"]), (Staff, ["strongly"])), "invalid");
        yield return Case("a column compares exactly", Grid(null, Agreement, (Price, ["Agree"])), "invalid");
        yield return Case("several in a row", Grid("""{"multiple":true}""", Agreement, (Price, ["agree", "neutral"])));
        yield return Case("several in a row, one not a column", Grid("""{"multiple":true}""", Agreement, (Price, ["agree", "maybe"])), "invalid");
        const string Five = """{"scale":{"from":1,"to":5}}""";
        yield return Case("the ends of its scale", Grid(Five, null, (Price, ["1"]), (Staff, ["5"])));
        yield return Case("past its scale", Grid(Five, null, (Price, ["6"])), "invalid");
        yield return Case("below its scale", Grid(Five, null, (Price, ["0"])), "invalid");
        yield return Case("a scale from zero to ten", Grid("""{"scale":{"from":0,"to":10}}""", null, (Price, ["0"]), (Staff, ["10"])));
        yield return Case("a half on its scale", Grid(Five, null, (Price, ["2.5"])), "invalid");
        yield return Case("a word on its scale", Grid(Five, null, (Price, ["good"])), "invalid");
        yield return Case("the scale's default start", Grid("""{"scale":{"to":7}}""", null, (Price, ["1"]), (Staff, ["7"])));
        yield return Case("before the scale's default start", Grid("""{"scale":{"to":7}}""", null, (Price, ["0"])), "invalid");
        yield return Case("a scale and options", Grid("""{"scale":{"from":1,"to":3}}""", Agreement, (Price, ["agree"])), "invalid");
        yield return Case("a list that could not give its options takes any answer", Grid(null, null, (Price, ["whatever"])));

        yield return Case("all the options", Chosen("ranking", null, Aspects, "quality", "price", "speed", "service"));
        yield return Case("one option left out", Chosen("ranking", null, Aspects, "quality", "price", "speed"), "invalid");
        yield return Case("not an option", Chosen("ranking", null, Aspects, "quality", "price", "speed", "looks"), "invalid");
        yield return Case("its top two", Chosen("ranking", """{"top":2}""", Aspects, "speed", "price"));
        yield return Case("three for its top two", Chosen("ranking", """{"top":2}""", Aspects, "speed", "price", "quality"), "invalid");
        yield return Case("one for its top two", Chosen("ranking", """{"top":2}""", Aspects, "speed"), "invalid");
        yield return Case("a top past its options", Chosen("ranking", """{"top":9}""", Aspects, "quality", "price", "speed", "service"));
        yield return Case("a list that could not give its options takes any answer", Chosen("ranking", """{"top":2}""", null, "looks"));

        yield return Case("any country without a list", Chosen("country", null, null, "CH"));
        yield return Case("an allowed country", Chosen("country", """{"allowed":["AT","CH","DE"]}""", null, "AT"));
        yield return Case("a country not allowed", Chosen("country", """{"allowed":["AT","CH","DE"]}""", null, "FR"), "invalid");
    }

    [TestCaseSource(nameof(Edges))]
    public void AChoiceType_ChecksItsSettings_AtTheirEdges(ValidationCase given, string[] errors) =>
        Assert.That(given.Errors(), Is.EqualTo(errors));

    [Test]
    public void TheEdges_CoverEveryTypeOfTheStep() =>
        Assert.That(Edges().Select(data => ((ValidationCase)data.Arguments[0]!).Type).Distinct(), Is.EquivalentTo(TypesOfTheStep));

    [TestCase("dropdown")]
    [TestCase("radio")]
    [TestCase("checkboxes")]
    [TestCase("ranking")]
    public void AChoiceWithoutAnyOptions_TakesNothing(string type)
    {
        // Only a draft holds a choice without options; it offers nothing, so any value is no option.
        var builtIn = TestFieldTypes.Registry.FindType(type)!;
        var field = new FormField { Key = Guid.NewGuid(), Type = type, Alias = type };
        var read = FieldValues.Read(builtIn, field, new FieldAnswers(["red"]));

        Assert.That(FieldValues.Check(builtIn, field, read, new FieldCheckInput(false, ValidationCase.Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
    }

    [Test]
    public void AGridWithoutColumns_TakesNothing()
    {
        var given = new ValidationCase("grid", new FieldAnswers([], [new(GridFieldType.RowPart(Price), ["agree"])])) { Rows = [Price] };
        var builtIn = TestFieldTypes.Registry.FindType("grid")!;
        var field = given.Field(builtIn) with { Options = null };
        var read = FieldValues.Read(builtIn, field, given.Answers);

        Assert.That(FieldValues.Check(builtIn, field, read, new FieldCheckInput(false, ValidationCase.Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
    }

    [Test]
    public void AGrid_NeedsTheRowsMarkedRequired_OrEveryRowWhenItIsRequired()
    {
        static string[] Checked(bool required, Guid[] marked, params Guid[] answered) =>
            new ValidationCase("grid", new FieldAnswers([], answered.Select(row => new KeyValuePair<string, IEnumerable<string>>(GridFieldType.RowPart(row), ["agree"]))))
            {
                Options = Agreement,
                Rows = [Price, Staff],
                RequiredRows = marked,
                Required = required,
            }.Errors();

        Assert.Multiple(() =>
        {
            Assert.That(Checked(false, [], Price), Is.Empty, "an optional grid needs no row");
            Assert.That(Checked(false, []), Is.Empty, "an optional grid left empty");
            Assert.That(Checked(false, [Staff], Price), Is.EqualTo(new[] { "required" }), "a marked row left out");
            Assert.That(Checked(false, [Staff], Staff), Is.Empty, "a marked row answered alone");
            Assert.That(Checked(false, [Staff]), Is.EqualTo(new[] { "required" }), "a marked row needs an answer though the grid is optional");
            Assert.That(Checked(true, [], Price), Is.EqualTo(new[] { "required" }), "a required grid needs every row");
            Assert.That(Checked(true, [], Price, Staff), Is.Empty);
            Assert.That(Checked(true, []), Is.EqualTo(new[] { "required" }));
        });
    }

    [Test]
    public void AGrid_ChecksItsColumns_BeforeTheRowsItNeeds()
    {
        var given = new ValidationCase("grid", new FieldAnswers([], [new(GridFieldType.RowPart(Price), ["strongly"])]))
        {
            Options = Agreement,
            Rows = [Price, Staff],
            RequiredRows = [Staff],
        };

        Assert.That(given.Errors(), Is.EqualTo(new[] { "invalid", "required" }));
    }

    [Test]
    public void ARanking_RefusesAnOptionOrAPositionGivenTwice()
    {
        static string[] Ranked(string? settings, IReadOnlyList<string>? options, params string[] values) =>
            new ValidationCase("ranking", new FieldAnswers(values)) { Settings = settings, Options = options }.Errors();
        static string[] Positioned(params (string Option, string Position)[] parts) =>
            new ValidationCase("ranking", new FieldAnswers([], parts.Select(part => new KeyValuePair<string, IEnumerable<string>>(part.Option, [part.Position]))))
            {
                Settings = """{"top":2}""",
                Options = Aspects,
            }.Errors();

        Assert.Multiple(() =>
        {
            Assert.That(Ranked(null, Aspects, "quality", "quality", "speed", "service"), Is.EqualTo(new[] { "invalid" }), "an option twice");
            Assert.That(Ranked("""{"top":2}""", null, "looks", "looks"), Is.EqualTo(new[] { "invalid" }), "an option twice from a list");
            Assert.That(Positioned(("price", "1"), ("quality", "1")), Is.EqualTo(new[] { "invalid" }), "two options in one position");
            Assert.That(Positioned(("price", "01"), ("quality", "1")), Is.EqualTo(new[] { "invalid" }), "positions compare as numbers");
            Assert.That(Positioned(("speed", "1"), ("price", "3")), Is.Empty, "a gap between positions");
        });
    }
}
