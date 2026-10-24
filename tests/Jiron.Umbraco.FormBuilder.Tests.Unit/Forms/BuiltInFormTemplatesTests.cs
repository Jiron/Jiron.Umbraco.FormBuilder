using System.Reflection;
using System.Xml.Linq;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The templates that ship inside Core (ADR-0046): one per file of Forms/Templates, each an envelope that reads with the
/// site's limits, has its fixed key, texts in English and German, and a definition that publishes. Also the package
/// version an export names.
/// </summary>
[TestFixture]
public sealed class BuiltInFormTemplatesTests
{
    [Test]
    public void EveryTemplateFile_IsABuiltInTemplate_AndEveryBuiltInTemplate_HasAFile()
    {
        var folder = RepositoryRoot.Path(typeof(BuiltInFormTemplatesTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Core", "Forms", "Templates");
        var files = Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(BuiltInFormTemplates.All.Select(template => template.Alias).Order(StringComparer.Ordinal), Is.EqualTo(files));
            Assert.That(BuiltInFormTemplates.All.Select(template => template.Key), Is.Unique);
            foreach (var template in BuiltInFormTemplates.All)
            {
                Assert.That(template.Envelope, Is.EqualTo(File.ReadAllText(Path.Combine(folder, template.Alias + ".json"))), $"{template.Alias}: the assembly holds the file as it is");
            }
        });
    }

    [Test]
    public void EveryTemplate_Reads_WithItsKey_AndPublishes_WithTextsInEnglishAndGerman()
    {
        Assert.Multiple(() =>
        {
            foreach (var template in BuiltInFormTemplates.All)
            {
                var read = FormExportFormat.Read(template.Envelope, DefinitionFixtures.Serializer());
                Assert.That(read.Problems, Is.Empty, template.Alias);
                var envelope = read.Envelope!;
                Assert.That((envelope.FormKey, envelope.Alias), Is.EqualTo((template.Key, template.Alias)), $"{template.Alias}: its key and alias");
                Assert.That(BuiltInFormTemplates.Find(template.Key), Is.SameAs(template));
                Assert.That(DefinitionFixtures.Serializer().CheckPublish(envelope.Definition, FieldTypeCatalog.BuiltIn), Is.Empty, $"{template.Alias}: publishes");
                foreach (var text in Texts(envelope.Definition))
                {
                    Assert.That(text.Texts.Select(pair => pair.Key), Is.EquivalentTo(new[] { string.Empty, "de" }), $"{template.Alias}: \"{text.Default}\" in English and German");
                }
            }

            Assert.That(BuiltInFormTemplates.Find(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void Contact_AsksForANameAnEmailAddressAndAMessage_AndThanksWithTheBuiltInMessage()
    {
        var envelope = FormExportFormat.Read(BuiltInFormTemplates.Find(BuiltInFormTemplates.ContactKey)!.Envelope, DefinitionFixtures.Serializer()).Envelope!;
        var fields = DefinitionKeys.Fields(envelope.Definition).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(envelope.Name, Is.EqualTo("Contact"));
            Assert.That(fields.Select(field => (field.Alias, field.Type, field.Required)), Is.EqualTo(new[] { ("name", "text", true), ("email", "email", true), ("message", "textarea", true) }));
            Assert.That(fields.Select(field => field.Label!.Resolve("de-CH")), Is.EqualTo(new[] { "Name", "E-Mail-Adresse", "Nachricht" }), "German for every German culture");
            Assert.That(fields.Select(field => field.Autocomplete), Is.EqualTo(new[] { "name", "email", null }));
            Assert.That(envelope.Definition.Outcomes.Single().Message, Is.Null, "the built-in thank-you message in the visitor's language");
            Assert.That(envelope.Definition.Actions, Is.Empty);
            Assert.That(envelope.OptionLists, Is.Empty);
        });
    }

    [Test]
    public void ThePackageVersion_IsTheBuildsVersion_WithoutBuildMetadata()
    {
        var props = XDocument.Load(RepositoryRoot.Path(typeof(BuiltInFormTemplatesTests).Assembly, "Directory.Build.props"));
        var prefix = props.Descendants("VersionPrefix").Single().Value;

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderVersion.Package, Does.StartWith(prefix), "the version of Directory.Build.props");
            Assert.That(FormBuilderVersion.Package, Does.Not.Contain("+"));
            Assert.That(FormBuilderVersion.Package, Is.EqualTo(FormBuilderVersion.Read(typeof(BuiltInFormTemplatesTests).Assembly)), "the version this build gave every assembly");
            Assert.That(typeof(FormBuilderVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, Does.StartWith(FormBuilderVersion.Package));
            Assert.That(FormBuilderVersion.WithoutMetadata("17.0.0-alpha.0+5f3c2a1"), Is.EqualTo("17.0.0-alpha.0"), "the commit after the plus");
            Assert.That(FormBuilderVersion.WithoutMetadata("17.0.0+a+b"), Is.EqualTo("17.0.0"), "from the first plus");
            Assert.That(FormBuilderVersion.WithoutMetadata("17.0.0"), Is.EqualTo("17.0.0"), "a version without metadata stays");
        });
    }

    // Every text of a definition a visitor reads: titles, descriptions, labels, help, placeholders and messages.
    private static IEnumerable<LocalizedString> Texts(FormDefinition definition)
    {
        var texts = new List<LocalizedString?>();
        foreach (var page in definition.Pages)
        {
            texts.Add(page.Title);
            texts.Add(page.Description);
            foreach (var group in page.Groups)
            {
                texts.Add(group.Title);
                texts.Add(group.Description);
            }
        }

        foreach (var field in DefinitionKeys.Fields(definition))
        {
            texts.AddRange([field.Label, field.Help, field.Placeholder]);
            texts.AddRange(field.Options?.Items.SelectMany(item => new[] { item.Label, item.Description }) ?? []);
        }

        texts.AddRange(definition.Outcomes.Select(outcome => outcome.Message));
        return texts.OfType<LocalizedString>();
    }
}
