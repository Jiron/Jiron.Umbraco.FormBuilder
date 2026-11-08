using System.Reflection;
using System.Text.Json;
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
                Assert.That(new StructuralPublishCheck(DefinitionFixtures.Serializer(), TestFieldTypes.Registry).Check(envelope.Definition), Is.Empty, $"{template.Alias}: publishes");
                foreach (var text in Texts(envelope.Definition))
                {
                    Assert.That(text.Texts.Select(pair => pair.Culture), Is.EquivalentTo(new[] { string.Empty, "de" }), $"{template.Alias}: \"{text.Default}\" in English and German");
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
    public void EveryTextOfATemplate_InItsSettingsAndActionsToo_IsInEnglishAndGerman()
    {
        Assert.Multiple(() =>
        {
            foreach (var template in BuiltInFormTemplates.All)
            {
                using var document = JsonDocument.Parse(template.Envelope);
                foreach (var (path, cultures) in Localized(document.RootElement.GetProperty("definition"), "$"))
                {
                    Assert.That(cultures, Is.EqualTo(new[] { string.Empty, "de" }), $"{template.Alias}: {path}");
                }
            }
        });
    }

    [Test]
    public void EachNewTemplate_HoldsWhatItIsFor()
    {
        var callback = Definition(BuiltInFormTemplates.CallbackKey);
        var newsletter = Definition(BuiltInFormTemplates.NewsletterKey);
        var registration = Definition(BuiltInFormTemplates.EventRegistrationKey);
        var job = Definition(BuiltInFormTemplates.JobApplicationKey);
        var feedback = Definition(BuiltInFormTemplates.FeedbackKey);
        var quote = Definition(BuiltInFormTemplates.QuoteRequestKey);
        var support = Definition(BuiltInFormTemplates.SupportRequestKey);
        var privacy = Definition(BuiltInFormTemplates.PrivacyRequestKey);

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionKeys.Fields(callback).Select(field => (field.Alias, field.Type)), Is.EqualTo(new[] { ("name", "text"), ("phone", "phone"), ("bestTime", "radio"), ("topic", "textarea") }), "callback");
            Assert.That(callback.Actions, Is.Empty, "a callback mails no one it cannot know");

            Assert.That(newsletter.Settings.Submission.Verification?.EmailField, Is.EqualTo(Field(newsletter, "email").Key), "the newsletter's address is verified");
            Assert.That(Field(newsletter, "consent") is { Type: "consent", Required: true }, Is.True);

            Assert.That((registration.Pages.Count, registration.Settings.Submission.SummaryPage, registration.Settings.Availability.MaxEntries), Is.EqualTo((2, true, (int?)120)), "two pages and a summary, an entry limit");
            Assert.That(registration.Settings.Availability.ClosedMessage, Is.Not.Null);
            Assert.That(Field(registration, "session").Options!.Items.Select(option => option.Places), Is.EqualTo(new int?[] { 40, 40, null }), "sessions with places, the stream without");
            Assert.That(Setting(registration.Actions.Single().Settings, "toField"), Is.EqualTo(Field(registration, "email").Key.ToString()), "a confirmation to the person");

            Assert.That(job.Settings.Submission.Approval, Is.EqualTo(ApprovalMode.Manual), "an application waits for approval");
            Assert.That(DefinitionKeys.Fields(job).Where(field => field.Type == "file").Select(field => (field.Alias, field.Required)), Is.EqualTo(new[] { ("cv", true), ("letter", false) }));
            Assert.That(Setting(job.Actions.Single().Settings, "toField"), Is.EqualTo(Field(job, "email").Key.ToString()));

            var score = Field(feedback, "score");
            Assert.That((score.Type, Setting(score.Settings, "from"), Setting(score.Settings, "to")), Is.EqualTo(("scale", "0", "10")), "a 0-to-10 scale");
            Assert.That(Field(feedback, "ratings") is { Type: "grid", Rows.Count: 4 }, Is.True, "a grid of four statements");
            var low = Field(feedback, "reason").Rules.Single();
            Assert.That((low.Effect, low.Conditions.Single() is RuleCondition { Operator: "lessThan" } condition ? condition.Field : null), Is.EqualTo((RuleEffect.Require, (Guid?)score.Key)), "a low score requires the reason");

            Assert.That(Setting(Field(quote, "estimate").Settings, "formula"), Is.EqualTo("service * hours"), "the estimate is calculated");

            Assert.That(support.Settings.Submission.EntryReference is { Prefix: "SR-", IncludeYear: false, Digits: 5 }, Is.True, "the reference");
            Assert.That(support.Outcomes.Single().Message!.Resolve(string.Empty), Does.Contain("{{ entry.reference }}"));

            Assert.That(privacy.Settings.Submission.Verification?.EmailField, Is.EqualTo(Field(privacy, "email").Key), "the person proves the address");
            var team = privacy.Actions.Single();
            Assert.That((team.Trigger, Setting(team.Settings, "toPrivacyTeam"), Setting(team.Settings, "to")), Is.EqualTo((ActionTrigger.Verified, "True", (string?)null)), "the privacy team hears once the address is verified");
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

    private static FormDefinition Definition(Guid key) =>
        FormExportFormat.Read(BuiltInFormTemplates.Find(key)!.Envelope, DefinitionFixtures.Serializer()).Envelope!.Definition;

    private static FormField Field(FormDefinition definition, string alias) => DefinitionKeys.Fields(definition).Single(field => field.Alias == alias);

    // A setting as text: a string as it is, anything else as JSON writes it, a switch as True or False; null when it is not set.
    private static string? Setting(SettingsObject? settings, string name) =>
        settings is not null && settings.Value.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => value.GetRawText(),
            }
            : null;

    // Every object of a definition that holds texts per culture, which the empty culture's text marks, with its cultures.
    private static IEnumerable<(string Path, string[] Cultures)> Localized(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                foreach (var found in Localized(item, $"{path}[{index++}]"))
                {
                    yield return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(string.Empty, out _))
            {
                yield return (path, [.. element.EnumerateObject().Select(property => property.Name)]);
                yield break;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var found in Localized(property.Value, $"{path}.{property.Name}"))
                {
                    yield return found;
                }
            }
        }
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
