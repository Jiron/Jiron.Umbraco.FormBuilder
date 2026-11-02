using System.Text.Json.Nodes;
using System.Xml.Linq;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// The appsettings JSON schema for IntelliSense (ADR-0008, ADR-0042): generated from the options and their XML docs,
/// committed in Core, shipped in its package, and every reference in the file resolves within the file. Run the tests
/// with <c>JFB_UPDATE_SCHEMA=1</c> to rewrite the file.
/// </summary>
[TestFixture]
public sealed class AppSettingsSchemaTests
{
    private static readonly string CoreFolder = RepositoryRoot.Path(typeof(AppSettingsSchemaTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Core");
    private static readonly string SchemaFile = Path.Combine(CoreFolder, AppSettingsSchema.FileName);

    [Test]
    public void TheCommittedSchema_MatchesTheOptionsAndTheirDocs()
    {
        var generated = AppSettingsSchema.Generate();
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SCHEMA") == "1")
        {
            File.WriteAllText(SchemaFile, generated);
        }

        Assert.That(File.Exists(SchemaFile), Is.True, $"{AppSettingsSchema.FileName} is missing; run the tests with JFB_UPDATE_SCHEMA=1.");
        Assert.That(File.ReadAllText(SchemaFile).ReplaceLineEndings("\n"), Is.EqualTo(generated), $"{AppSettingsSchema.FileName} is stale; run the tests with JFB_UPDATE_SCHEMA=1.");
    }

    [Test]
    public void EveryDefault_InTheSchema_IsWhatNewOptionsHold()
    {
        var section = JsonNode.Parse(AppSettingsSchema.Generate())!["properties"]!["JironFormBuilder"]!;
        var limits = section["properties"]!["Limits"]!["properties"]!;
        var compared = new List<string>();

        void Compare(JsonNode schema, object options, string path)
        {
            foreach (var (name, node) in schema["properties"]!.AsObject())
            {
                var value = options.GetType().GetProperty(name)!.GetValue(options);
                if (node!["default"] is { } expected && value is int or long or bool or string)
                {
                    Assert.That(expected.ToJsonString(), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(value)), $"{path}:{name}");
                    compared.Add($"{path}:{name}");
                }

                if (node["properties"] is not null && value is not null)
                {
                    Compare(node, value, $"{path}:{name}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Compare(section, new Jiron.Umbraco.FormBuilder.Core.Configuration.FormBuilderOptions(), "JironFormBuilder");
            Assert.That(limits["Read"]!["properties"]!["PerAddressPerMinute"]!["default"]!.GetValue<int>(), Is.EqualTo(120));
            Assert.That(limits["Submit"]!["properties"]!["PerFormPerMinute"]!["default"]!.GetValue<int>(), Is.EqualTo(600));
            Assert.That(limits["Verify"]!["properties"]!["SitePerSecond"]!["default"]!.GetValue<int>(), Is.Zero);
            Assert.That(compared, Has.Member("JironFormBuilder:Files:MaxWaitingBytes"), "a walk that reached them");
        });
    }

    [Test]
    public void TheBlockedExtensions_Description_NamesEveryDefault()
    {
        var description = JsonNode.Parse(AppSettingsSchema.Generate())!["properties"]!["JironFormBuilder"]!["properties"]!["Files"]!["properties"]!["BlockedExtensions"]!["description"]!.GetValue<string>();

        Assert.That(Jiron.Umbraco.FormBuilder.Core.Files.FormBuilderFilesOptions.DefaultBlockedExtensions.Where(extension => !System.Text.RegularExpressions.Regex.IsMatch(description, $@"\b{extension}\b")), Is.Empty);
    }

    [Test]
    public void TheSchema_DescribesEveryKey_WithItsDefault_AndNoOtherKeys()
    {
        var schema = JsonNode.Parse(AppSettingsSchema.Generate())!;
        var section = schema["properties"]!["JironFormBuilder"]!;
        var definitions = section["properties"]!["Limits"]!["properties"]!["Definitions"]!;
        var settingLock = section["properties"]!["Designer"]!["properties"]!["SettingLocks"]!["items"]!;

        Assert.Multiple(() =>
        {
            Assert.That(schema["$schema"]!.GetValue<string>(), Is.EqualTo("http://json-schema.org/draft-04/schema#"), "as Umbraco's own schemas");
            Assert.That(Keys(section), Is.EqualTo(new[] { "PublicApi", "Rendering", "Files", "Actions", "Limits", "Designer", "Access", "Captcha", "Privacy", "Secrets", "Webhooks", "LockedSections" }));
            Assert.That(section["properties"]!["Rendering"]!["properties"]!["IncludeAssetsAutomatically"]!["default"]!.GetValue<bool>(), Is.True);
            Assert.That(section["properties"]!["PublicApi"]!["properties"]!["BasePath"]!["default"]!.GetValue<string>(), Is.EqualTo("/jfb/api"));
            Assert.That(section["description"]!.GetValue<string>(), Does.StartWith("Jiron Form Builder's host configuration: the JironFormBuilder section of appsettings. It is for developers"));
            Assert.That(section["properties"]!["Files"]!["properties"]!["Root"]!["default"]!.GetValue<string>(), Is.EqualTo("~/umbraco/Data/JironFormBuilder/Uploads"));
            Assert.That(Keys(section["properties"]!["Files"]!), Is.EqualTo(new[] { "Root", "MaxFileBytes", "MaxWaitingBytes", "MaxWaitingBytesPerAddress", "BlockedExtensions" }));
            Assert.That(Keys(section["properties"]!["Captcha"]!), Is.EqualTo(new[] { "RecaptchaV2", "RecaptchaV3", "Hcaptcha", "UseRecaptchaNet" }));
            Assert.That(Keys(section["properties"]!["Captcha"]!["properties"]!["Hcaptcha"]!), Is.EqualTo(new[] { "SiteKey", "SecretKey", "VerifyAddress" }));
            Assert.That(section["properties"]!["Files"]!["properties"]!["MaxFileBytes"]!["default"]!.GetValue<long>(), Is.EqualTo(52_428_800));
            Assert.That(section["properties"]!["Files"]!["properties"]!["BlockedExtensions"]!["items"]!["type"]!.ToJsonString(), Does.Contain("string"));
            Assert.That(Keys(definitions), Is.EqualTo(new[] { "MaxBytes", "MaxPages", "MaxGroups", "MaxFields", "MaxOptionsPerField", "MaxActions", "MaxOutcomes", "MaxRules" }));
            Assert.That(definitions["properties"]!["MaxFields"]!["default"]!.GetValue<int>(), Is.EqualTo(300));
            Assert.That(definitions["properties"]!["MaxBytes"]!["description"]!.GetValue<string>(), Is.EqualTo("The largest document, in UTF-8 bytes: 2 MB."));
            Assert.That(Keys(settingLock), Is.EqualTo(new[] { "Kind", "Type", "Setting", "Value", "Hidden" }));
            Assert.That(settingLock["properties"]!["Kind"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()), Is.EqualTo(new[] { "field", "action" }));
            Assert.That(settingLock["properties"]!["Hidden"]!["default"]!.GetValue<bool>(), Is.False);
            Assert.That(section["properties"]!["LockedSections"]!["items"]!["type"]!.ToJsonString(), Does.Contain("string"));
        });
        Assert.Multiple(() =>
        {
            foreach (var (path, node) in Objects(section, "JironFormBuilder"))
            {
                Assert.That(node["additionalProperties"]?.GetValue<bool>(), Is.False, $"{path} refuses keys it does not have, as start-up does");
                foreach (var (name, property) in node["properties"]!.AsObject())
                {
                    Assert.That(property!["description"]?.GetValue<string>(), Is.Not.Null.And.Not.Contains("ADR-").And.Not.Contains("section 4."), $"{path}:{name} has a description for a site's developer");
                }
            }
        });
    }

    [Test]
    public void EveryReferenceInTheCommittedFile_ResolvesWithinTheFile()
    {
        var file = JsonNode.Parse(File.ReadAllText(SchemaFile))!;

        Assert.Multiple(() =>
        {
            foreach (var reference in AppSettingsSchema.References(file))
            {
                Assert.That(reference, Does.StartWith("#/properties/JironFormBuilder"), "a reference within the file, under the section");
                Assert.That(AppSettingsSchema.Resolve(file, reference), Is.Not.Null, reference);
            }
        });
    }

    [Test]
    public void AShapeMetTwice_IsReferredTo_AndTheReferenceResolvesUnderTheSection()
    {
        // When two lists or dictionaries share the shape of their items (the webhook and option-list hosts will), the
        // exporter describes the items once and refers to them, relative to the options' own schema.
        var schema = JsonNode.Parse(AppSettingsSchema.Generate(typeof(Twice), "Sample", "SampleSchema"))!;
        var references = AppSettingsSchema.References(schema).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(references, Is.Not.Empty);
            foreach (var reference in references)
            {
                Assert.That(reference, Does.StartWith("#/properties/Sample"));
                Assert.That(AppSettingsSchema.Resolve(schema, reference)?["properties"]?["Key"], Is.Not.Null, reference);
            }
        });
    }

    [TestCase("#/properties/A", "a")]
    [TestCase("#/properties/B~1C", "b/c")]
    [TestCase("#/properties/D~0E", "d~e")]
    [TestCase("#/list/1", "second")]
    [TestCase("#/list/2", null)]
    [TestCase("#/list/x", null)]
    [TestCase("#/properties/Z", null)]
    [TestCase("#/properties/A/deeper", null)]
    [TestCase("other.json#/properties/A", null)]
    public void AReference_IsResolvedAsAJsonPointer(string reference, string? expected)
    {
        var document = JsonNode.Parse("""{ "properties": { "A": "a", "B/C": "b/c", "D~E": "d~e" }, "list": ["first", "second"] }""")!;

        Assert.That(AppSettingsSchema.Resolve(document, reference)?.GetValue<string>(), Is.EqualTo(expected));
        Assert.That(AppSettingsSchema.Resolve(document, "#"), Is.SameAs(document));
    }

    [Test]
    public void ThePackage_ShipsTheSchema_AndAddsItToUmbracosSchemaFiles()
    {
        var project = XDocument.Load(Path.Combine(CoreFolder, "Jiron.Umbraco.FormBuilder.Core.csproj"));
        var props = XDocument.Load(Path.Combine(CoreFolder, "buildTransitive", "Jiron.Umbraco.FormBuilder.Core.props"));
        var packed = project.Descendants("None").Where(item => item.Attribute("Pack")?.Value == "true").ToDictionary(item => item.Attribute("Include")!.Value, item => item.Attribute("PackagePath")!.Value);
        var schemaFiles = props.Descendants("UmbracoJsonSchemaFiles").Select(item => item.Attribute("Include")!.Value).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(packed, Does.ContainKey(AppSettingsSchema.FileName).WithValue(string.Empty), "at the package's root");
            Assert.That(packed, Does.ContainKey("buildTransitive/Jiron.Umbraco.FormBuilder.Core.props").WithValue("buildTransitive"));
            Assert.That(schemaFiles, Is.EqualTo(new[] { "$(MSBuildThisFileDirectory)../" + AppSettingsSchema.FileName }));
        });
    }

    private static IEnumerable<string> Keys(JsonNode schema) => schema["properties"]!.AsObject().Select(property => property.Key);

    // Every object of the schema that has properties, with its key.
    private static IEnumerable<(string Path, JsonObject Node)> Objects(JsonNode node, string path)
    {
        if (node is not JsonObject obj)
        {
            yield break;
        }

        if (obj["properties"] is JsonObject properties)
        {
            yield return (path, obj);
            foreach (var (name, property) in properties)
            {
                foreach (var inner in Objects(property!, $"{path}:{name}"))
                {
                    yield return inner;
                }
            }
        }

        if (obj["items"] is { } items)
        {
            foreach (var inner in Objects(items, $"{path}:0"))
            {
                yield return inner;
            }
        }
    }

    private sealed class Twice
    {
        public IList<Shape> First { get; } = [];

        public IDictionary<string, Shape> Second { get; } = new Dictionary<string, Shape>();

        public IList<Shape> Third { get; } = [];
    }

    private sealed class Shape
    {
        public string Key { get; set; } = "k";
    }
}
