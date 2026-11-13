using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The definition upgrader's fixtures of the released schemas (ROADMAP 16.5 and 18.4, ADR-0240): every definition a site
/// on a baseline's packages stored (the <c>jfbFormVersion</c> rows of each <c>tests/fixtures/upgrade/schema-v&lt;N&gt;.json</c>,
/// written by that baseline's own form service) reads with today's reader of stored documents and upgrader chain, keeps
/// its form, its pages, groups and fields with their keys, aliases, types and labels, and is stored as the text its hash
/// names. Rewritten by today's writer and read again it stays the same, so a later save of an old form loses nothing.
/// Each released schema adds its fixture (docs/upgrade-testing.md).
/// </summary>
public sealed class StoredDefinitionFixtureTests
{
    private static IEnumerable<TestCaseData> Versions()
    {
        foreach (var path in UpgradeFixtures.Paths(typeof(StoredDefinitionFixtureTests).Assembly))
        {
            var schema = Path.GetFileNameWithoutExtension(path).Replace('-', '_');
            using var fixture = JsonDocument.Parse(File.ReadAllText(path));
            var tables = fixture.RootElement.GetProperty("tables");
            var forms = tables.GetProperty("jfbForm").EnumerateArray().ToDictionary(form => form.GetProperty("id").GetInt64(), form => form.GetProperty("key").GetString()!);
            foreach (var version in tables.GetProperty("jfbFormVersion").EnumerateArray())
            {
                var formKey = forms[version.GetProperty("formId").GetInt64()];
                var number = version.GetProperty("number").GetInt64();
                yield return new TestCaseData(
                        formKey,
                        version.GetProperty("schemaVersion").GetInt32(),
                        version.GetProperty("definition").GetString()!,
                        version.GetProperty("definitionHash").GetString()!)
                    .SetName($"TheBaselinesVersion_{schema}_{formKey[..8]}_{number}_ReadsWithTodaysReader");
            }
        }
    }

    [TestCaseSource(nameof(Versions))]
    public void TheBaselinesStoredDefinition_ReadsWithTodaysReader_AndKeepsWhatItHeld(string formKey, int schemaVersion, string stored, string hash)
    {
        var read = DefinitionFixtures.Serializer().ReadStored(stored);
        using var raw = JsonDocument.Parse(stored);
        // The canonical text leaves out an empty list: a blank form's first version has a group without fields.
        var rawFields = Items(raw.RootElement, "pages")
            .SelectMany(page => Items(page, "groups"))
            .SelectMany(group => Items(group, "fields"))
            .Select(field => (
                Key: Guid.Parse(field.GetProperty("key").GetString()!),
                Alias: field.TryGetProperty("alias", out var alias) ? alias.GetString() : null,
                Type: field.GetProperty("type").GetString(),
                Label: field.TryGetProperty("label", out var label) && label.TryGetProperty(string.Empty, out var text) ? text.GetString() : null))
            .ToList();

        Assert.That(read.Problems, Is.Empty, "today's reader reads the baseline's stored document");
        var definition = read.Definition!;
        var fields = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields)
            .Select(field => (field.Key, field.Alias, field.Type, Label: field.Label?.Default))
            .ToList();
        var again = DefinitionFixtures.Serializer().ReadStored(FormDefinitionSerializer.WriteString(definition));

        Assert.Multiple(() =>
        {
            Assert.That(read.SchemaVersion, Is.EqualTo(schemaVersion), "the version it was written in");
            Assert.That(definition.SchemaVersion, Is.EqualTo(FormDefinition.CurrentSchemaVersion), "upgraded to today's");
            Assert.That(definition.Key, Is.EqualTo(Guid.Parse(formKey)), "the form's own key");
            Assert.That(definition.Pages.Count, Is.EqualTo(Items(raw.RootElement, "pages").Count()));
            Assert.That(fields, Is.EqualTo(rawFields), "every field with its key, alias, type and label");
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stored))), Is.EqualTo(hash), "the text its hash names");
            Assert.That(again.Problems, Is.Empty);
            Assert.That(FormDefinitionSerializer.WriteString(again.Definition!), Is.EqualTo(FormDefinitionSerializer.WriteString(definition)), "a rewrite reads back the same");
        });
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.TryGetProperty(name, out var items) ? items.EnumerateArray() : Enumerable.Empty<JsonElement>();

}
