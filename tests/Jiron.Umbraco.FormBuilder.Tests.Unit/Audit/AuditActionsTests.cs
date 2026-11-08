using System.Reflection;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Activity;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Audit;

/// <summary>
/// Every action the product records can be filtered and read (ROADMAP 12.4b, ADR-0219): each belongs to a group of the
/// audit log's "What" filter, and has its sentence in the activity screens' words, in English and German, so the log never
/// shows a code. The actions are the product's constants named <c>...Action</c> whose value is <c>entity.verb</c>, as every
/// service names the actions it records.
/// </summary>
[TestFixture]
public sealed class AuditActionsTests
{
    private static readonly string Lang = Path.Combine(
        RepositoryRoot.Path(typeof(AuditActionsTests).Assembly, "src"),
        "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "backoffice", "src", "lang");

    [Test]
    public void EveryRecordedAction_BelongsToAGroup()
    {
        var actions = Recorded();

        Assert.Multiple(() =>
        {
            Assert.That(actions, Has.Count.GreaterThan(50), "the actions the product records");
            Assert.That(actions, Does.Contain(AuditGroups.ExportedAction).And.Contain("email.retried").And.Contain("form.published"));
            Assert.That(actions.Where(action => AuditGroups.GroupOf(action) is null), Is.Empty, "actions no group holds");
        });
    }

    [TestCase("en")]
    [TestCase("de")]
    public void EveryRecordedAction_HasItsSentence(string language)
    {
        var dictionary = File.ReadAllText(Path.Combine(Lang, $"activity.{language}.ts"));
        var keys = new HashSet<string>(Regex.Matches(dictionary, @"^\s{4}([A-Za-z_]+):", RegexOptions.Multiline).Select(match => match.Groups[1].Value), StringComparer.Ordinal);

        Assert.That(Recorded().Select(action => "audit_" + action.Replace('.', '_')).Where(key => !keys.Contains(key)), Is.Empty, $"actions without words in {language}");
    }

    [Test]
    public void AGroupsKinds_AreTheFirstWordsOfItsActions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AuditGroups.GroupOf("form.published"), Is.EqualTo(AuditGroup.Forms));
            Assert.That(AuditGroups.GroupOf("folder.moved"), Is.EqualTo(AuditGroup.Forms));
            Assert.That(AuditGroups.GroupOf("entryFile.archived"), Is.EqualTo(AuditGroup.Entries));
            Assert.That(AuditGroups.GroupOf("email.retried"), Is.EqualTo(AuditGroup.Entries));
            Assert.That(AuditGroups.GroupOf("emailLayout.saved"), Is.EqualTo(AuditGroup.Library));
            Assert.That(AuditGroups.GroupOf("access.restricted"), Is.EqualTo(AuditGroup.Settings));
            Assert.That(AuditGroups.GroupOf("formTemplate.saved"), Is.Null, "a first word, never a prefix of one");
            Assert.That(Enum.GetValues<AuditGroup>().SelectMany(AuditGroups.KindsOf).All(AuditRepository.IsEntityType), Is.True);
        });
    }

    // The constants of Core and Web named ...Action whose value is entity.verb.
    private static List<string> Recorded() =>
    [
        .. new[] { typeof(AuditGroups).Assembly, typeof(AuditController).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string) && field.Name.EndsWith("Action", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(AuditRepository.IsAction)
            .Distinct()
            .Order(StringComparer.Ordinal),
    ];
}
