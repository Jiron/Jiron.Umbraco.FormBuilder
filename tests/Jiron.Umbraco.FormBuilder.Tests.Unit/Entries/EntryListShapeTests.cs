using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The shapes of the entries list (ROADMAP 11.1, ADR-0207): the fields it offers from a catalogue, the numbers a search
/// names, and the filter a request sends being the filter a set keeps.
/// </summary>
public sealed class EntryListShapeTests
{
    // What the management API writes: the web's defaults, a missing value left out.
    private static readonly JsonSerializerOptions Sent = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static readonly Guid Grid = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid GridRow = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly Guid Repeater = Guid.Parse("a1000000-0000-4000-8000-000000000003");
    private static readonly Guid Child = Guid.Parse("a1000000-0000-4000-8000-000000000004");
    private static readonly Guid Gone = Guid.Parse("a1000000-0000-4000-8000-000000000005");
    private static readonly Guid Draft = Guid.Parse("a1000000-0000-4000-8000-000000000006");
    private static readonly Guid Amount = Guid.Parse("a1000000-0000-4000-8000-000000000007");

    [Test]
    public void TheFields_AreThePublishedOnes_TheLiveInOrderThenTheRemoved_AndAGridsRowsAreItsAnswer()
    {
        var fields = EntryLists.Fields(
        [
            Row(1, Gone, "gone", FieldStorageKind.Text, order: 0, last: 2),
            Row(2, Grid, "grid", FieldStorageKind.None, order: 2, type: "grid"),
            Row(3, GridRow, "grid", FieldStorageKind.Text, order: 3, type: "grid", parent: Grid),
            Row(4, Repeater, "people", FieldStorageKind.None, order: 4, type: "repeater", sensitive: true),
            Row(5, Child, "person", FieldStorageKind.Text, order: 5, parent: Repeater),
            Row(6, Draft, "draft", FieldStorageKind.Text, order: 6, first: null),
            Row(7, Amount, "amount", FieldStorageKind.Number, order: 1, type: "number"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(field => field.Key), Is.EqualTo(new[] { Amount, Grid, Repeater, Child, Gone }), "no grid row and no field only a draft has");
            Assert.That(fields.Single(field => field.Key == Gone).Removed, Is.True);
            Assert.That(fields.Single(field => field.Key == Grid), Has.Property(nameof(EntryListField.Kind)).EqualTo(EntryListFieldKind.Group).And.Property(nameof(EntryListField.Column)).True.And.Property(nameof(EntryListField.Tests)).Empty);
            Assert.That(fields.Single(field => field.Key == Child), Has.Property(nameof(EntryListField.Sensitive)).True.And.Property(nameof(EntryListField.Parent)).EqualTo(Repeater), "a sensitive group's child is sensitive");
            Assert.That(fields.Single(field => field.Key == Amount), Has.Property(nameof(EntryListField.Sorts)).True.And.Property(nameof(EntryListField.Column)).True);
        });
    }

    [TestCase("Text", "OneOf NoneOf StartsWith Answered NotAnswered")]
    [TestCase("LongText", "StartsWith Answered NotAnswered")]
    [TestCase("Number", "Between Answered NotAnswered")]
    [TestCase("Date", "Between Answered NotAnswered")]
    [TestCase("Checkbox", "Checked NotChecked")]
    [TestCase("File", "Answered NotAnswered")]
    [TestCase("Parts", "Answered NotAnswered")]
    [TestCase("Group", "")]
    public void EachKind_FiltersByItsTests(string kind, string tests)
    {
        Assert.That(
            EntryLists.TestsOf(Enum.Parse<EntryListFieldKind>(kind)),
            Is.EqualTo(tests.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<EntryFieldTest>)));
    }

    [TestCase("1042", null, new[] { 1042 })]
    [TestCase(" #1042 ", null, new[] { 1042 })]
    [TestCase("2026-01042", null, new[] { 1042 })]
    [TestCase("REQ-2026-01042", "REQ-", new[] { 1042 })]
    [TestCase("req-2026-01042", "REQ-", new[] { 1042 })]
    [TestCase("REQ-01042", "REQ-", new[] { 1042 })]
    [TestCase("2026", null, new[] { 2026 })]
    [TestCase("#0", null, new int[0])]
    [TestCase("#", null, new int[0])]
    [TestCase("Ada", null, new int[0])]
    [TestCase("12345678901", null, new int[0])]
    [TestCase("1042a", null, new int[0])]
    [TestCase("INV-2026-7", "REQ-", new int[0])]
    public void ASearch_NamesTheNumbersOfItsTerm(string term, string? prefix, int[] numbers)
    {
        Assert.That(EntryReferences.Numbers(prefix is null ? null : new EntryReferenceFormat { Prefix = prefix }, term), Is.EqualTo(numbers));
    }

    [Test]
    public void TheFilterARequestSends_IsTheFilterASetKeeps()
    {
        var model = new EntryListFilterRequestModel
        {
            States = ["submitted", "approved"],
            Received = new EntryReceivedRequestModel { Preset = "last7Days" },
            Actions = "failed",
            Fields =
            [
                new EntryFieldFilterRequestModel { Field = Amount, Test = "between", Min = 1.5m, Max = 9 },
                new EntryFieldFilterRequestModel { Field = Child, Test = "oneOf", Values = ["a", "b"] },
                new EntryFieldFilterRequestModel { Field = Grid, Test = "between", From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 31) },
            ],
            Search = "ada",
            Sort = new EntryListSortRequestModel { Field = Amount, Descending = false },
        };
        var problems = new List<DefinitionProblem>();

        var filter = EntryListMapping.Filter(model, "filter", problems);
        var kept = JsonNode.Parse(JsonSerializer.Serialize(filter, EntryListJson.Options));
        var sent = JsonNode.Parse(JsonSerializer.Serialize(EntryListMapping.Model(filter), Sent));

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(JsonNode.DeepEquals(kept, sent), Is.True, $"kept {kept}{Environment.NewLine}sent {sent}");
            Assert.That(JsonSerializer.Deserialize<EntryListFilter>(kept!.ToJsonString(), EntryListJson.Options), Is.EqualTo(filter).Using<EntryListFilter>(Same), "a kept filter reads back as it was");
        });
    }

    [Test]
    public void NamesARequestCannotRead_AreRefusedByTheirPath()
    {
        var problems = new List<DefinitionProblem>();

        EntryListMapping.Filter(
            new EntryListFilterRequestModel
            {
                States = ["submitted", "draft", "Approved"],
                Received = new EntryReceivedRequestModel { Preset = "yesterday" },
                Actions = "broken",
                Fields = [new EntryFieldFilterRequestModel { Field = Amount, Test = "like" }],
            },
            "filter",
            problems);

        Assert.That(problems.Select(problem => problem.Path), Is.EqualTo(new[] { "filter.states[1]", "filter.states[2]", "filter.received.preset", "filter.actions", "filter.fields[0].test" }), "a draft is no state a list holds, and names keep their case");
    }

    private static bool Same(EntryListFilter one, EntryListFilter other) =>
        JsonSerializer.Serialize(one, EntryListJson.Options) == JsonSerializer.Serialize(other, EntryListJson.Options);

    private static CatalogueField Row(int id, Guid key, string alias, FieldStorageKind storage, int order, string type = "text", Guid? parent = null, bool sensitive = false, int? first = 1, int? last = null) =>
        new(id, 1, new CatalogueFieldValues(key, alias, alias.ToUpperInvariant(), type, storage, sensitive, order, first, last, parent));
}
