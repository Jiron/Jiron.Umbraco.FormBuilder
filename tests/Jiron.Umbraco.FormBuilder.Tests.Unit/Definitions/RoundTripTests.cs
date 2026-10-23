using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Round trips are lossless (ROADMAP 4.2a): every generated definition writes, reads and writes again to the same bytes
/// and the same model, and other spellings of a document (indented, reordered, with nulls and empty lists) read to the
/// model of its canonical form.
/// </summary>
public sealed class RoundTripTests
{
    private const int Definitions = 500;

    [Test]
    public void GeneratedDefinitions_WriteReadWrite_ToTheSameBytesAndModel()
    {
        var serializer = DefinitionFixtures.Serializer();
        for (var seed = 0; seed < Definitions; seed++)
        {
            var model = new DefinitionGenerator(seed).Next();
            Assert.That(serializer.CheckSave(model), Is.Empty, $"seed {seed}: the generator stays within the save checks");

            var first = FormDefinitionSerializer.Write(model);
            var read = serializer.Read(first);
            Assert.That(read.Problems, Is.Empty, $"seed {seed}: the written definition reads");

            var second = FormDefinitionSerializer.Write(read.Definition!);
            Assert.Multiple(() =>
            {
                Assert.That(Encoding.UTF8.GetString(second), Is.EqualTo(Encoding.UTF8.GetString(first)), $"seed {seed}: the same bytes");
                Assert.That(DefinitionComparer.Differences(model, read.Definition), Is.Empty, $"seed {seed}: the same model");
            });
        }
    }

    [Test]
    public void GeneratedDefinitions_CoverEveryPropertyOfTheModel()
    {
        // Property-style tests prove little about a property the generator never sets: every property of every
        // object of the model must appear in at least one generated document.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < Definitions; seed++)
        {
            using var document = JsonDocument.Parse(FormDefinitionSerializer.Write(new DefinitionGenerator(seed).Next()));
            Collect(document.RootElement, "FormDefinition", seen);
        }

        Assert.Multiple(() =>
        {
            Assert.That(ModelProperties.All().Count(), Is.GreaterThan(150), "the model's properties are found");
            Assert.That(ModelProperties.All().Except(seen).Order(StringComparer.Ordinal), Is.Empty, "properties no generated definition sets");
        });
    }

    [Test]
    public void OtherSpellings_ReadToTheCanonicalModel()
    {
        const string Canonical = """
            {"schemaVersion":1,"key":"f0000000-0000-4000-8000-000000000001","pages":[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","columns":1,"style":"plain"}]}]}
            """;
        const string Other = """
            {
              "pages": [ { "groups": [ { "style": "plain", "key": "20000000-0000-4000-8000-000000000001", "fields": [], "title": null } ],
                           "rules": [], "key": "10000000-0000-4000-8000-000000000001", "description": {} } ],
              "outcomes": [], "settings": { "submission": {}, "privacy": { "retentionDays": { "spam": null } } },
              "key": "f0000000-0000-4000-8000-000000000001",
              "schemaVersion": 1
            }
            """;

        var written = FormDefinitionSerializer.WriteString(DefinitionFixtures.Read(Other));

        Assert.That(written, Is.EqualTo(FormDefinitionSerializer.WriteString(DefinitionFixtures.Read(Canonical))));
    }

    [Test]
    public void MissingProperties_TakeTheModelsDefaults()
    {
        var definition = DefinitionFixtures.Read("""
            {"schemaVersion":1,"key":"f0000000-0000-4000-8000-000000000001","pages":[{"key":"10000000-0000-4000-8000-000000000001","groups":[{"key":"20000000-0000-4000-8000-000000000001","fields":[{"key":"30000000-0000-4000-8000-000000000001","type":"text","rules":[{"key":"40000000-0000-4000-8000-000000000001","effect":"show"}]}]}]}],"actions":[{"key":"50000000-0000-4000-8000-000000000001","type":"email"}]}
            """);

        var group = definition.Pages[0].Groups[0];
        var field = group.Fields[0];
        Assert.Multiple(() =>
        {
            Assert.That(group.Columns, Is.EqualTo(1));
            Assert.That(group.Rules, Is.Empty);
            Assert.That(field.Span, Is.EqualTo(1));
            Assert.That(field.IncludeInEmails, Is.True);
            Assert.That(field.FormerAliases, Is.Empty);
            Assert.That(field.Rules[0].Enabled, Is.True);
            Assert.That(field.Rules[0].Conditions, Is.Empty);
            Assert.That(definition.Actions[0].Enabled, Is.True);
            Assert.That(definition.Settings.Privacy.Insights, Is.True);
            Assert.That(definition.Settings.Appearance.ValidationSummary, Is.True);
            Assert.That(definition.Settings.Privacy.RetentionDays, Is.EqualTo(new RetentionDaysByState()));
            Assert.That(definition.ValidationRules, Is.Empty);
        });
    }

    private static void Collect(JsonElement value, string type, HashSet<string> seen)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    Collect(item, type, seen);
                }

                break;
            case JsonValueKind.Object when ModelProperties.Types.TryGetValue(type, out var properties):
                var concrete = type;
                if (value.TryGetProperty("kind", out var kind) && ModelProperties.Derived.TryGetValue((type, kind.GetString()!), out var derived))
                {
                    concrete = derived;
                    properties = ModelProperties.Types[derived];
                }
                else if (type == "RuleItem")
                {
                    concrete = value.TryGetProperty("op", out _) ? "RuleCondition" : "RuleGroup";
                    properties = ModelProperties.Types[concrete];
                }

                foreach (var property in value.EnumerateObject())
                {
                    seen.Add($"{concrete}.{property.Name}");
                    if (properties.TryGetValue(property.Name, out var child) && child is not null)
                    {
                        Collect(property.Value, child, seen);
                    }
                }

                break;
        }
    }
}
