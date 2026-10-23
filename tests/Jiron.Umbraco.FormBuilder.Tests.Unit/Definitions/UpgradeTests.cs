using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The upgrader framework (ADR-0002, ROADMAP 4.2a), with synthetic versions: v1 is the only real one, so a synthetic
/// "version 0" with an older shape is read through the real reader, and a synthetic chain to version 3 shows the steps.
/// </summary>
public sealed class UpgradeTests
{
    // Version 0, an imagined older shape: the form's fields sat in one flat list with their page and group names.
    private const string Version0 = """
        {
          "schemaVersion": 0,
          "key": "f0000000-0000-4000-8000-000000000001",
          "fields": [
            { "key": "30000000-0000-4000-8000-000000000001", "type": "text", "alias": "name", "caption": "Name", "page": "Details" },
            { "key": "30000000-0000-4000-8000-000000000002", "type": "email", "alias": "email", "caption": "Email", "page": "Details" }
          ]
        }
        """;

    [Test]
    public void ASyntheticOlderVersion_IsUpgradedAndReadByTheRealReader()
    {
        var serializer = new FormDefinitionSerializer(new FormDefinitionUpgrades([new FlatFieldsToPages()]), new DefinitionLimits());

        var result = serializer.Read(Version0);

        Assert.That(result.Problems, Is.Empty);
        var page = result.Definition!.Pages.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.SchemaVersion, Is.EqualTo(0), "the version it was written in");
            Assert.That(result.Definition.SchemaVersion, Is.EqualTo(FormDefinition.CurrentSchemaVersion));
            Assert.That(page.Title?.Default, Is.EqualTo("Details"));
            Assert.That(page.Groups.Single().Fields.Select(field => (field.Alias, field.Label?.Default)), Is.EqualTo(new[] { ("name", "Name"), ("email", "Email") }));
            Assert.That(FormDefinitionSerializer.WriteString(result.Definition), Does.StartWith("{\"schemaVersion\":1,"), "a save writes the current version");
        });
    }

    [Test]
    public void AnUpgradedDocument_IsStillReadStrictly()
    {
        var serializer = new FormDefinitionSerializer(new FormDefinitionUpgrades([new FlatFieldsToPages()]), new DefinitionLimits());
        var document = JsonNode.Parse(Version0)!.AsObject();
        document["fields"]![0]!["colour"] = "red";

        var result = serializer.Read(document.ToJsonString());

        Assert.That(result.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.UnknownProperty, "$.pages[0].groups[0].fields[0].colour") }));
    }

    [Test]
    public void AnOlderDocument_WithAPropertyTwice_IsRefusedBeforeItIsUpgraded()
    {
        var serializer = new FormDefinitionSerializer(new FormDefinitionUpgrades([new FlatFieldsToPages()]), new DefinitionLimits());
        var json = Version0.Replace("\"page\": \"Details\" },", "\"page\": \"Details\", \"page\": \"Other\" },", StringComparison.Ordinal);

        var result = serializer.Read(json);

        Assert.That(result.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.DuplicateProperty, "$.fields[0].page") }));
    }

    [Test]
    public void AnUpgraderThatCannotReadTheDocument_RefusesIt()
    {
        var serializer = new FormDefinitionSerializer(new FormDefinitionUpgrades([new FlatFieldsToPages()]), new DefinitionLimits());
        var json = Version0.Replace("\"caption\": \"Name\"", "\"caption\": 3", StringComparison.Ordinal);

        var result = serializer.Read(json);

        Assert.Multiple(() =>
        {
            Assert.That(result.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$") }));
            Assert.That(result.Problems[0].Message, Does.Contain("Schema version 0 could not be upgraded"));
        });
    }

    [Test]
    public void TheChain_RunsEachStepInOrder_AndSetsEachVersion()
    {
        var chain = new FormDefinitionUpgrades([new Append(2, "c"), new Append(1, "b")], currentVersion: 3);
        var definition = new JsonObject { ["schemaVersion"] = 1, ["steps"] = "a" };

        var upgraded = chain.Upgrade(definition, fromVersion: 1);

        Assert.Multiple(() =>
        {
            Assert.That(upgraded["steps"]!.GetValue<string>(), Is.EqualTo("abc"));
            Assert.That(upgraded["schemaVersion"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(chain.Oldest, Is.EqualTo(1));
            Assert.That(chain.Upgrade(new JsonObject { ["steps"] = "x" }, fromVersion: 2)["steps"]!.GetValue<string>(), Is.EqualTo("xc"), "a later start runs fewer steps");
        });
    }

    [Test]
    public void TheProductionChain_HasNoUpgraders_AndReadsVersion1Only()
    {
        var chain = new FormDefinitionUpgrades([]);

        Assert.Multiple(() =>
        {
            Assert.That(chain.Current, Is.EqualTo(FormDefinition.CurrentSchemaVersion));
            Assert.That(chain.Oldest, Is.EqualTo(FormDefinition.CurrentSchemaVersion));
            Assert.That(chain.Upgrade(new JsonObject { ["a"] = 1 }, fromVersion: FormDefinition.CurrentSchemaVersion)["a"]!.GetValue<int>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TheChain_RefusesAGap_ATwin_AndAnUpgraderPastTheCurrentVersion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FormDefinitionUpgrades([new Append(1, "b")], currentVersion: 3), Throws.InvalidOperationException.With.Message.Contains("No upgrader reads schema version 2"));
            Assert.That(() => new FormDefinitionUpgrades([new Append(1, "b"), new Append(1, "c")], currentVersion: 2), Throws.InvalidOperationException.With.Message.Contains("Two upgraders read schema version 1"));
            Assert.That(() => new FormDefinitionUpgrades([new Append(2, "b")], currentVersion: 2), Throws.InvalidOperationException.With.Message.Contains("the current version is 2"));
        });
    }

    [Test]
    public void TheChain_RefusesToUpgradeAVersionItCannotRead()
    {
        var chain = new FormDefinitionUpgrades([new Append(1, "b")], currentVersion: 2);

        Assert.That(() => chain.Upgrade([], fromVersion: 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Version 0 to 1: the flat field list becomes one page with one group; "caption" becomes the label.</summary>
    private sealed class FlatFieldsToPages : IFormDefinitionUpgrader
    {
        public int FromVersion => 0;

        public JsonObject Upgrade(JsonObject definition)
        {
            var fields = definition["fields"]?.AsArray() ?? [];
            definition.Remove("fields");
            var pageTitle = fields.Select(field => field?["page"]?.GetValue<string>()).FirstOrDefault(title => title is not null);
            var upgraded = new JsonArray();
            foreach (var field in fields.OfType<JsonObject>())
            {
                var copy = field.DeepClone().AsObject();
                copy.Remove("page");
                if (copy["caption"] is { } caption)
                {
                    copy.Remove("caption");
                    copy["label"] = new JsonObject { [string.Empty] = caption.GetValue<string>() };
                }

                upgraded.Add(copy);
            }

            definition["pages"] = new JsonArray(new JsonObject
            {
                ["key"] = "10000000-0000-4000-8000-000000000001",
                ["title"] = pageTitle is null ? null : new JsonObject { [string.Empty] = pageTitle },
                ["groups"] = new JsonArray(new JsonObject { ["key"] = "20000000-0000-4000-8000-000000000001", ["fields"] = upgraded }),
            });
            return definition;
        }
    }

    /// <summary>A synthetic step that appends its letter, to show the order the chain runs in.</summary>
    private sealed class Append(int fromVersion, string letter) : IFormDefinitionUpgrader
    {
        public int FromVersion => fromVersion;

        public JsonObject Upgrade(JsonObject definition)
        {
            definition["steps"] = definition["steps"]!.GetValue<string>() + letter;
            return definition;
        }
    }
}
