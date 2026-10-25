using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Testing;
using Json.Schema;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The export file, <c>jfb-form-export/1</c> (ADR-0002, ADR-0046): what the writer writes reads back as it was and
/// validates against its schema; the reader checks the format first, is as strict as the definition reader, measures
/// the definition without its indentation, upgrades an older one and refuses a newer one, with paths into the file. An
/// option list keeps every value it holds, and an escaped lone surrogate is refused where it is (ROADMAP 4.8i).
/// </summary>
public sealed class FormExportFormatTests
{
    private const char Backslash = (char)92;
    private const string Mark = "JFB_MARK";

    private static readonly Guid FormKey = Guid.Parse("f0000000-0000-4000-8000-000000000001");
    private static readonly Guid ListKey = Guid.Parse("72000000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Exported = new(2026, 10, 23, 8, 30, 15, 123, TimeSpan.Zero);

    private static readonly EvaluationOptions Checked = new() { RequireFormatValidation = true };

    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        options.SchemaRegistry.Register(JsonSchema.FromText(SchemaText("jfb-form.v1.schema.json"), options));
        return JsonSchema.FromText(SchemaText("jfb-form-export.v1.schema.json"), options);
    });

    public static IEnumerable<TestCaseData> Refusals()
    {
        TestCaseData Case(string name, Action<JsonObject> change, string code, string path) =>
            new TestCaseData(change, code, path).SetName($"AnEnvelope_IsRefused({name})");

        yield return Case("a newer format, whatever else it holds", e => { e["format"] = "jfb-form-export/2"; e["newer"] = true; }, DefinitionErrors.UnsupportedFormat, "$.format");
        yield return Case("another format", e => e["format"] = "something", DefinitionErrors.UnsupportedFormat, "$.format");
        yield return Case("a format that is no string", e => e["format"] = 1, DefinitionErrors.InvalidType, "$.format");
        yield return Case("no format", e => e.Remove("format"), DefinitionErrors.MissingProperty, "$.format");
        yield return Case("an unknown property", e => e["extra"] = 1, DefinitionErrors.UnknownProperty, "$.extra");
        yield return Case("a property in another letter case", e => { var definition = e["definition"]; e.Remove("definition"); e["Definition"] = definition; }, DefinitionErrors.UnknownProperty, "$.Definition");
        yield return Case("no definition", e => e.Remove("definition"), DefinitionErrors.MissingProperty, "$.definition");
        yield return Case("no form", e => e.Remove("form"), DefinitionErrors.MissingProperty, "$.form");
        yield return Case("no package version", e => e.Remove("packageVersion"), DefinitionErrors.MissingProperty, "$.packageVersion");
        yield return Case("no export time", e => e.Remove("exportedUtc"), DefinitionErrors.MissingProperty, "$.exportedUtc");
        yield return Case("an empty package version", e => e["packageVersion"] = string.Empty, DefinitionErrors.InvalidValue, "$.packageVersion");
        yield return Case("a long package version", e => e["packageVersion"] = new string('1', FormExportFormat.MaxPackageVersionLength + 1), DefinitionErrors.LimitExceeded, "$.packageVersion");
        yield return Case("a package version that is no string", e => e["packageVersion"] = 17, DefinitionErrors.InvalidType, "$.packageVersion");
        yield return Case("an export time that is no time", e => e["exportedUtc"] = "yesterday", DefinitionErrors.InvalidValue, "$.exportedUtc");
        yield return Case("a form that is no object", e => e["form"] = "contact", DefinitionErrors.InvalidType, "$.form");
        yield return Case("a form key in capitals", e => e["form"]!["key"] = FormKey.ToString("D").ToUpperInvariant(), DefinitionErrors.InvalidValue, "$.form.key");
        yield return Case("the empty form key", e => e["form"]!["key"] = Guid.Empty.ToString("D"), DefinitionErrors.InvalidValue, "$.form.key");
        yield return Case("a form key in braces", e => e["form"]!["key"] = FormKey.ToString("B"), DefinitionErrors.InvalidValue, "$.form.key");
        yield return Case("a form without a name", e => e["form"]!.AsObject().Remove("name"), DefinitionErrors.MissingProperty, "$.form.name");
        yield return Case("a form alias that is no string", e => e["form"]!["alias"] = 1, DefinitionErrors.InvalidType, "$.form.alias");
        yield return Case("an unknown property of the form", e => e["form"]!["folder"] = "x", DefinitionErrors.UnknownProperty, "$.form.folder");
        yield return Case("a definition that is no object", e => e["definition"] = new JsonArray(), DefinitionErrors.InvalidType, "$.definition");
        yield return Case("a definition of another form", e => e["definition"]!["key"] = ListKey.ToString("D"), DefinitionErrors.InvalidValue, "$.definition.key");
        yield return Case("a definition a save refuses", e => e["definition"]!["pages"]![0]!["colour"] = "red", DefinitionErrors.UnknownProperty, "$.definition.pages[0].colour");
        yield return Case("a definition of a newer schema", e => e["definition"]!["schemaVersion"] = FormDefinition.CurrentSchemaVersion + 1, DefinitionErrors.UnsupportedSchemaVersion, "$.definition.schemaVersion");
        yield return Case("option lists that are no list", e => e["optionLists"] = new JsonObject(), DefinitionErrors.InvalidType, "$.optionLists");
        yield return Case("an option list without items", e => List(e).Remove("items"), DefinitionErrors.MissingProperty, "$.optionLists[0].items");
        yield return Case("an option list whose items are no list", e => List(e)["items"] = new JsonObject(), DefinitionErrors.InvalidType, "$.optionLists[0].items");
        yield return Case("an option list whose configuration is no object", e => List(e)["config"] = new JsonArray(), DefinitionErrors.InvalidType, "$.optionLists[0].config");
        yield return Case("an option list with a negative cache time", e => List(e)["cacheSeconds"] = -1, DefinitionErrors.InvalidValue, "$.optionLists[0].cacheSeconds");
        yield return Case("an option list with a cache time in pieces", e => List(e)["cacheSeconds"] = 1.5, DefinitionErrors.InvalidValue, "$.optionLists[0].cacheSeconds");
        yield return Case("an option list with a cache time as text", e => List(e)["cacheSeconds"] = "60", DefinitionErrors.InvalidType, "$.optionLists[0].cacheSeconds");
        yield return Case("an option list with a bad key", e => List(e)["key"] = "list", DefinitionErrors.InvalidValue, "$.optionLists[0].key");
        yield return Case("an option list with an unknown property", e => List(e)["source"] = "x", DefinitionErrors.UnknownProperty, "$.optionLists[0].source");
        yield return Case("two option lists with one key", e => e["optionLists"]!.AsArray().Add(List(e).DeepClone()), DefinitionErrors.DuplicateKey, "$.optionLists[1].key");
    }

    [Test]
    public void AnEnvelope_ReadsBack_AsItWasWritten()
    {
        var envelope = Envelope();

        var text = FormExportFormat.Write(envelope);
        var read = FormExportFormat.Read(text, DefinitionFixtures.Serializer());
        var back = read.Envelope!;

        Assert.Multiple(() =>
        {
            Assert.That(read.Problems, Is.Empty);
            Assert.That((back.FormKey, back.Alias, back.Name, back.PackageVersion, back.ExportedUtc), Is.EqualTo((FormKey, "contact", "Contact us", "17.0.0-alpha.0", Exported)));
            Assert.That(FormDefinitionSerializer.WriteString(back.Definition), Is.EqualTo(FormDefinitionSerializer.WriteString(envelope.Definition)));
            Assert.That(back.OptionLists.Single(), Is.EqualTo(envelope.OptionLists[0]), "the list, with its configuration and items as compact JSON text");
            Assert.That(FormExportFormat.Write(back), Is.EqualTo(text), "written again, the same file");
            Assert.That(FormExportFormat.Write(envelope), Is.EqualTo(text), "the same envelope written twice, which it lends its lists to");
            Assert.That(FormExportFormat.Read(Encoding.UTF8.GetBytes(text), DefinitionFixtures.Serializer()).Problems, Is.Empty, "as bytes too");
        });
    }

    [Test]
    public void TheFile_IsIndented_AsciiOnly_InItsOrder_AndValidatesAgainstItsSchema()
    {
        var umlaut = new string([(char)0x47, (char)0x72, (char)0xFC, (char)0xDF, (char)0x65]);
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        definition = definition with { Pages = [definition.Pages[0] with { Title = new LocalizedText(umlaut) }, .. definition.Pages.Skip(1)] };

        var text = FormExportFormat.Write(Envelope() with { Definition = definition });
        using var document = JsonDocument.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("{\n  \"format\": \"jfb-form-export/1\",\n  \"packageVersion\""), "two spaces and a line feed on every system");
            Assert.That(text.All(c => c < 0x80), Is.True, "every character outside ASCII is escaped");
            Assert.That(text, Does.Contain("\"Gr\\u00FC\\u00DFe\"").IgnoreCase);
            Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "format", "packageVersion", "exportedUtc", "form", "definition", "optionLists" }));
            Assert.That(document.RootElement.GetProperty("exportedUtc").GetString(), Is.EqualTo("2026-10-23T08:30:15.123Z"));
            Assert.That(JsonDocument.Parse(FormExportFormat.Write(Envelope() with { ExportedUtc = Exported.ToOffset(TimeSpan.FromHours(2)) })).RootElement.GetProperty("exportedUtc").GetString(), Is.EqualTo("2026-10-23T08:30:15.123Z"), "in UTC, whatever its offset");
            Assert.That(document.RootElement.GetProperty("definition").GetProperty("pages")[0].GetProperty("title").GetProperty(string.Empty).GetString(), Is.EqualTo(umlaut));
            Assert.That(Schema.Value.Evaluate(document.RootElement, Checked).IsValid, Is.True, "the export schema with the form schema");
            Assert.That(FormExportFormat.Read(text, DefinitionFixtures.Serializer()).Problems, Is.Empty);
        });
    }

    [TestCaseSource(nameof(Refusals))]
    public void AnEnvelope_IsRefused(Action<JsonObject> change, string code, string path)
    {
        var envelope = JsonNode.Parse(FormExportFormat.Write(Envelope()))!.AsObject();
        change(envelope);

        var read = FormExportFormat.Read(envelope.ToJsonString(), DefinitionFixtures.Serializer());

        Assert.Multiple(() =>
        {
            Assert.That(read.Envelope, Is.Null);
            Assert.That(read.Problems.Select(problem => (problem.Code, problem.Path)), Has.Member((code, path)), string.Join("; ", read.Problems));
            Assert.That(Schema.Value.Evaluate(JsonDocument.Parse(envelope.ToJsonString()).RootElement, Checked).IsValid, Is.EqualTo(BeyondTheSchema(code, path)), "the schema refuses it too, unless a schema cannot say it");
        });
    }

    [Test]
    public void AFormatThatIsNotTheFirstProperty_IsStillCheckedFirst_AndAnyPropertyTwiceIsRefused()
    {
        var text = FormExportFormat.Write(Envelope());
        var reordered = "{\"extra\":1," + text.TrimStart()[1..].Replace("\"format\": \"jfb-form-export/1\"", "\"format\": \"jfb-form-export/9\"", StringComparison.Ordinal);
        var twice = text.Replace("\"packageVersion\":", "\"form\": {},\"packageVersion\":", StringComparison.Ordinal);
        var withoutFormat = JsonNode.Parse(text)!.AsObject();
        withoutFormat.Remove("format");
        withoutFormat["extra"] = 1;

        Assert.Multiple(() =>
        {
            Assert.That(Problems(reordered), Is.EqualTo(new[] { (DefinitionErrors.UnsupportedFormat, "$.format") }));
            Assert.That(Problems(withoutFormat.ToJsonString()), Is.EqualTo(new[] { (DefinitionErrors.MissingProperty, "$.format") }), "a missing format alone, too");
            Assert.That(Problems(twice), Has.Member((DefinitionErrors.DuplicateProperty, "$.form")));
        });
    }

    [Test]
    public void WhatIsNotAnEnvelope_IsRefused_AtTheRoot()
    {
        var serializer = DefinitionFixtures.Serializer();
        var limit = serializer.Limits.MaxBytes * FormExportFormat.EnvelopeFactor;

        Assert.Multiple(() =>
        {
            Assert.That(Problems("not json"), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$") }));
            Assert.That(Problems("[]"), Is.EqualTo(new[] { (DefinitionErrors.InvalidType, "$") }));
            Assert.That(Problems("\"" + (char)0xD800 + "\""), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$") }), "a lone surrogate");
            Assert.That(FormExportFormat.Read([0x7B, 0xFF, 0x7D], serializer).Problems.Select(problem => problem.Code), Is.EqualTo(new[] { DefinitionErrors.InvalidJson }), "bytes that are not UTF-8");
            Assert.That(Problems(new string(' ', limit + 1)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$") }), "past four times the definition limit");
            Assert.That(Problems(new string(' ', limit)), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$") }), "at the limit, read as JSON");
            Assert.That(FormExportFormat.Read(Encoding.UTF8.GetBytes(new string(' ', limit + 1)), serializer).Problems.Select(problem => problem.Code), Is.EqualTo(new[] { DefinitionErrors.LimitExceeded }), "bytes past the limit");
            Assert.That(FormExportFormat.Read(Encoding.UTF8.GetBytes(new string(' ', limit)), serializer).Problems.Select(problem => problem.Code), Is.EqualTo(new[] { DefinitionErrors.InvalidJson }), "bytes at the limit");
            Assert.That(Problems(new string('[', FormExportFormat.MaxDepth + 1)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$") }), "JSON as far as it goes, but too deep, as the definition reader answers");
        });
    }

    [Test]
    public void AFileWithThousandsOfBadOptionLists_AnswersItsFirstHundredProblems()
    {
        var envelope = JsonNode.Parse(FormExportFormat.Write(Envelope()))!.AsObject();
        var lists = new JsonArray();
        for (var i = 0; i < 5000; i++)
        {
            var list = List(envelope).DeepClone().AsObject();
            list["key"] = "list";
            lists.Add(list);
        }

        envelope["optionLists"] = lists;
        var withBadForm = JsonNode.Parse(FormExportFormat.Write(Envelope()))!.AsObject();
        withBadForm["form"]!["key"] = "form";
        withBadForm["definition"]!["pages"] = new JsonArray([.. Enumerable.Range(0, 300).Select(_ => new JsonObject { ["colour"] = "red" })]);

        var problems = Problems(envelope.ToJsonString());
        var both = Problems(withBadForm.ToJsonString());

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.EqualTo(Enumerable.Range(0, DefinitionProblems.Max).Select(i => (DefinitionErrors.InvalidValue, $"$.optionLists[{i}].key"))), "one problem for each of the first hundred lists");
            Assert.That(both, Has.Count.EqualTo(DefinitionProblems.Max), "the form's problem and the definition's together");
            Assert.That(both[0], Is.EqualTo((DefinitionErrors.InvalidValue, "$.form.key")));
            Assert.That(both.Skip(1).Select(problem => problem.Path), Has.All.StartsWith("$.definition."));
        });
    }

    [Test]
    public void TheDefinition_IsMeasuredWithoutItsIndentation_AgainstTheSitesLimit()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var compact = FormDefinitionSerializer.Write(definition).Length;
        var text = FormExportFormat.Write(Envelope() with { Definition = definition, OptionLists = [] });
        using var document = JsonDocument.Parse(text);
        var indented = document.RootElement.GetProperty("definition").GetRawText().Length;
        var fits = DefinitionFixtures.Serializer(new DefinitionLimits { MaxBytes = compact });
        var tooSmall = DefinitionFixtures.Serializer(new DefinitionLimits { MaxBytes = compact - 1 });

        Assert.Multiple(() =>
        {
            Assert.That(indented, Is.GreaterThan(compact), "the file's definition is larger than what a save stores");
            Assert.That(FormExportFormat.Read(text, fits).Problems, Is.Empty, "a definition at the limit");
            Assert.That(FormExportFormat.Read(text, tooSmall).Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.definition") }));
        });
    }

    [Test]
    public void AnOlderDefinition_IsUpgraded_AndAnEnvelopeWithoutOptionLists_HasNone()
    {
        var serializer = new FormDefinitionSerializer(new FormDefinitionUpgrades([new HeadingToTitle()], currentVersion: FormDefinition.CurrentSchemaVersion), new DefinitionLimits());
        var envelope = JsonNode.Parse(FormExportFormat.Write(Envelope()))!.AsObject();
        envelope.Remove("optionLists");
        var page = envelope["definition"]!["pages"]![0]!.AsObject();
        envelope["definition"]!["schemaVersion"] = 0;
        page["heading"] = page["title"]!.DeepClone();
        page.Remove("title");

        var read = FormExportFormat.Read(envelope.ToJsonString(), serializer);

        Assert.Multiple(() =>
        {
            Assert.That(read.Problems, Is.Empty);
            Assert.That(read.Envelope!.Definition.Pages[0].Title, Is.EqualTo(Envelope().Definition.Pages[0].Title));
            Assert.That(read.Envelope.OptionLists, Is.Empty);
        });
    }

    [Test]
    public void AnEnvelope_KeepsItsOwnListOfOptionLists()
    {
        var lists = new List<ExportedOptionList>(Envelope().OptionLists);
        var changed = Envelope() with { OptionLists = lists };
        var built = new FormExportEnvelope(FormKey, "contact", "Contact us", "17.0.0-alpha.0", Exported, changed.Definition, lists);
        lists.Add(lists[0] with { Key = Guid.NewGuid() });

        Assert.Multiple(() =>
        {
            Assert.That(changed.OptionLists, Has.Count.EqualTo(1), "a list set with with");
            Assert.That(built.OptionLists, Has.Count.EqualTo(1), "a list given to the constructor");
            Assert.Throws<InvalidCastException>(() => ((List<ExportedOptionList>)built.OptionLists).Clear(), "a cast to a list");
        });
    }

    [Test]
    public void AnOptionList_KeepsEveryValue_NullsAndEmptyValuesToo()
    {
        const string Config = """{"sort":"label","note":null,"tags":[],"more":{}}""";
        const string Items = """[{"value":"a","label":{"":"A"},"hint":null,"tags":[]}]""";
        var envelope = Envelope() with { OptionLists = [Envelope().OptionLists[0] with { Config = Config, Items = Items }] };

        var back = FormExportFormat.Read(FormExportFormat.Write(envelope), DefinitionFixtures.Serializer()).Envelope!.OptionLists.Single();

        Assert.Multiple(() =>
        {
            Assert.That((back.Config, back.Items), Is.EqualTo((Config, Items)), "read back as the site has them");
            Assert.That(FormExportFormat.ListJson(Config, JsonValueKind.Object), Is.EqualTo(Config), "as an export takes the site's list");
            Assert.That(FormExportFormat.ListJson(" { \"b\" : [ 1 , null ] } ", JsonValueKind.Object), Is.EqualTo("""{"b":[1,null]}"""), "without whitespace");
        });
    }

    [TestCase("not json", JsonValueKind.Object, TestName = "ListJson_IsNull(text that is not JSON)")]
    [TestCase("[]", JsonValueKind.Object, TestName = "ListJson_IsNull(an array for a configuration)")]
    [TestCase("{}", JsonValueKind.Array, TestName = "ListJson_IsNull(an object for items)")]
    public void ListJson_IsNull_ForWhatAnExportCannotCarry(string json, JsonValueKind kind) =>
        Assert.That(FormExportFormat.ListJson(json, kind), Is.Null);

    [Test]
    public void ListJson_IsNull_ForALoneSurrogate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormExportFormat.ListJson("{\"a\":\"" + Backslash + "ud800\"}", JsonValueKind.Object), Is.Null, "escaped in a string");
            Assert.That(FormExportFormat.ListJson("{\"" + Backslash + "ud800\":null}", JsonValueKind.Object), Is.Null, "escaped in a name");
            Assert.That(FormExportFormat.ListJson("{\"a\":\"" + (char)0xD800 + "\"}", JsonValueKind.Object), Is.Null, "in the text");
        });
    }

    [TestCase(true, TestName = "AnOptionList_NestsAsDeeplyAsADefinition_AndNoDeeper(the configuration)")]
    [TestCase(false, TestName = "AnOptionList_NestsAsDeeplyAsADefinition_AndNoDeeper(the items)")]
    public void AnOptionList_NestsAsDeeplyAsADefinition_AndNoDeeper(bool config)
    {
        // The list's object or array is a level itself, so the value inside it takes the rest.
        static string Nested(int levels) => new string('[', levels) + "1" + new string(']', levels);
        string File(int depth) => FormExportFormat.Write(Envelope() with { OptionLists = [Envelope().OptionLists[0] with { Config = config ? "{\"x\":0}" : "{}", Items = config ? "[]" : "[0]" }] })
            .Replace(config ? "\"x\": 0" : "[\n        0\n      ]", config ? "\"x\": " + Nested(depth - 1) : "[" + Nested(depth - 1) + "]", StringComparison.Ordinal);
        var path = config ? "$.optionLists[0].config" : "$.optionLists[0].items";

        var deepest = FormExportFormat.Read(File(FormExportFormat.ListDepth), DefinitionFixtures.Serializer());
        var deeper = Problems(File(FormExportFormat.ListDepth + 1));

        Assert.Multiple(() =>
        {
            Assert.That(deepest.Problems, Is.Empty, "as deep as a definition");
            var list = deepest.Envelope!.OptionLists.Single();
            Assert.That(FormExportFormat.ListJson(config ? list.Config : list.Items, config ? JsonValueKind.Object : JsonValueKind.Array), Is.Not.Null, "and an export carries it");
            Assert.That(deeper, Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, path) }), "one level deeper");
            Assert.That(FormExportFormat.ListJson("[" + Nested(FormExportFormat.ListDepth) + "]", JsonValueKind.Array), Is.Null, "which an export leaves out");
        });
    }

    [TestCase("""{"a":1,"b":[2,null]}""", """{"b":[2,null],"a":1}""", JsonValueKind.Object, true, TestName = "SameListJson(the same properties in another order)")]
    [TestCase("""{"a":1}""", """{ "a" : 1 }""", JsonValueKind.Object, true, TestName = "SameListJson(the same object written another way)")]
    [TestCase("""{"a":1}""", """{"a":2}""", JsonValueKind.Object, false, TestName = "SameListJson(another value)")]
    [TestCase("""{"a":null}""", "{}", JsonValueKind.Object, false, TestName = "SameListJson(a null against nothing)")]
    [TestCase("[1,2]", "[2,1]", JsonValueKind.Array, false, TestName = "SameListJson(items in another order)")]
    [TestCase("""{"a":1,"a":2}""", """{"a":1,"a":2}""", JsonValueKind.Object, true, TestName = "SameListJson(a name given twice, alike)")]
    [TestCase("""{"a":1,"a":2}""", """{"a":1}""", JsonValueKind.Object, false, TestName = "SameListJson(a name given twice against once)")]
    [TestCase("not json", "{}", JsonValueKind.Object, false, TestName = "SameListJson(text that is not JSON)")]
    [TestCase("[]", "[]", JsonValueKind.Object, false, TestName = "SameListJson(JSON of another kind)")]
    public void SameListJson_ComparesTheValues_InAnyOrderOfProperties(string one, string other, JsonValueKind kind, bool same) =>
        Assert.That(FormExportFormat.SameListJson(one, other, kind), Is.EqualTo(same));

    [TestCase("""{"sort":"JFB_MARK"}""", "[]", "$.optionLists[0].config.sort", TestName = "AnEscapedLoneSurrogate_InAnOptionList_IsRefusedWhereItIs(a string of the configuration)")]
    [TestCase("{}", """[{"value":"a"},{"JFB_MARK":null}]""", "$.optionLists[0].items[1]", TestName = "AnEscapedLoneSurrogate_InAnOptionList_IsRefusedWhereItIs(a name of an item)")]
    public void AnEscapedLoneSurrogate_InAnOptionList_IsRefusedWhereItIs(string config, string items, string path)
    {
        var text = Escaped(FormExportFormat.Write(Envelope() with { OptionLists = [Envelope().OptionLists[0] with { Config = config, Items = items }] }));

        Assert.That(Problems(text), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, path) }));
    }

    [TestCase(false, "$.definition.pages[0].title['']", TestName = "AnEscapedLoneSurrogate_InTheDefinition_IsRefusedAsTheDefinitionReaderRefusesIt(a string)")]
    [TestCase(true, "$.definition.pages[0]", TestName = "AnEscapedLoneSurrogate_InTheDefinition_IsRefusedAsTheDefinitionReaderRefusesIt(the name of a null)")]
    public void AnEscapedLoneSurrogate_InTheDefinition_IsRefusedAsTheDefinitionReaderRefusesIt(bool name, string path)
    {
        var file = JsonNode.Parse(FormExportFormat.Write(Envelope()))!;
        var page = file["definition"]!["pages"]![0]!.AsObject();
        if (name)
        {
            page[Mark] = null;
        }
        else
        {
            page["title"]![""] = Mark;
        }

        var problems = Problems(Escaped(file.ToJsonString()));
        var alone = DefinitionFixtures.Serializer().Read(Escaped(file["definition"]!.ToJsonString())).Problems.Single();

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, path) }), "one problem, where it is, even where the canonical text leaves the value out");
            Assert.That((alone.Code, "$.definition" + alone.Path[1..]), Is.EqualTo(problems.Single()), "as the definition reader names it, below $");
        });
    }

    // A definition as the full fixture has it, with one library option list.
    private static FormExportEnvelope Envelope() => new(
        FormKey,
        "contact",
        "Contact us",
        "17.0.0-alpha.0",
        Exported,
        DefinitionFixtures.Read(DefinitionFixtures.Full),
        [new ExportedOptionList(ListKey, "sessions", "Sessions", "manual", """{"sort":"label"}""", """[{"value":"keynote","label":{"":"Keynote"}}]""", 60)]);

    private static JsonObject List(JsonObject envelope) => envelope["optionLists"]![0]!.AsObject();

    // The text with the mark turned into an escaped lone surrogate, which no JSON writer writes.
    private static string Escaped(string text) => text.Replace(Mark, "a" + Backslash + "ud800", StringComparison.Ordinal);

    private static List<(string Code, string Path)> Problems(string text) =>
        FormExportFormat.Read(text, DefinitionFixtures.Serializer()).Problems.Select(problem => (problem.Code, problem.Path)).ToList();

    private static string SchemaText(string file) => File.ReadAllText(RepositoryRoot.Path(typeof(FormExportFormatTests).Assembly, "schemas", file));

    // What the schema cannot say: two option lists with one key, and a definition whose key is not the form's.
    private static bool BeyondTheSchema(string code, string path) =>
        code == DefinitionErrors.DuplicateKey || path == "$.definition.key";

    /// <summary>Version 0 to 1: a page's title was called its heading.</summary>
    private sealed class HeadingToTitle : IFormDefinitionUpgrader
    {
        public int FromVersion => 0;

        public JsonObject Upgrade(JsonObject definition)
        {
            foreach (var page in definition["pages"]!.AsArray().OfType<JsonObject>())
            {
                if (page["heading"] is { } heading)
                {
                    page.Remove("heading");
                    page["title"] = heading.DeepClone();
                }
            }

            return definition;
        }
    }
}
