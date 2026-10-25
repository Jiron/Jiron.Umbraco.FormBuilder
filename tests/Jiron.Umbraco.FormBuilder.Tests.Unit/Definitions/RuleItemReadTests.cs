using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The model's reader of rule items (ROADMAP 4.8h): it reads a condition or a group straight, which keeps no path, so
/// an error inside an item is read again to be named, and an error inside groups inside groups is named at its full
/// path. The shape reader refuses such documents first; this is the reader's own answer behind it.
/// </summary>
public sealed class RuleItemReadTests
{
    private static readonly JsonTypeInfo<FormRule> Rule = (JsonTypeInfo<FormRule>)DefinitionJsonContext.Default.Options.GetTypeInfo(typeof(FormRule));

    [TestCase("""{ "context": "culture", "op": 5 }""", "$.conditions[0].op")]
    [TestCase("""{ "match": "any", "conditions": [ { "context": "culture", "op": 5 } ] }""", "$.conditions[0].conditions[0].op")]
    [TestCase("""{ "match": "any", "conditions": [ { "context": "culture", "op": "equals" }, { "conditions": [ { "context": "culture", "op": 5 } ] } ] }""", "$.conditions[0].conditions[1].conditions[0].op")]
    public void AReaderErrorInsideARuleItem_IsNamedAtItsFullPath(string item, string path)
    {
        var json = $$"""{ "key": "30000000-0000-4000-8000-000000000001", "effect": "show", "conditions": [ {{item}} ] }""";

        var thrown = Assert.Throws(Is.InstanceOf<JsonException>(), () => JsonSerializer.Deserialize(json, Rule));

        Assert.That(RuleItemConverter.PathOf((JsonException)thrown!), Is.EqualTo(path));
    }

    [Test]
    public void AnErrorOutsideTheRuleItems_KeepsThePathTheSerializerNames()
    {
        var thrown = Assert.Throws(Is.InstanceOf<JsonException>(), () => JsonSerializer.Deserialize("""{ "key": "30000000-0000-4000-8000-000000000001", "effect": 5 }""", Rule));

        Assert.That(RuleItemConverter.PathOf((JsonException)thrown!), Is.EqualTo("$.effect"));
    }
}
