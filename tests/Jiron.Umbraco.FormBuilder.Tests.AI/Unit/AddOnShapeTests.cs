using System.Reflection;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.AI;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Architecture;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Unit;

/// <summary>
/// The add-on's shape (ADR-0018): its public surface is its composer and nothing else a site could start to depend on;
/// what it records belongs to a group of the audit log and has its sentence in English and German, as the core's
/// actions do; it keeps no static state that can change, by the rule Core and Web keep; and it reaches Umbraco.AI only
/// through its own assembly.
/// </summary>
[TestFixture]
public sealed class AddOnShapeTests
{
    private static readonly Assembly AddOn = typeof(FormBuilderAIComposer).Assembly;

    [Test]
    public void ThePublicSurface_IsTheComposer()
    {
        var exported = AddOn.GetExportedTypes().Select(type => type.FullName).Order(StringComparer.Ordinal);

        Assert.That(exported, Is.EqualTo(new[] { typeof(FormBuilderAIComposer).FullName }));
    }

    [TestCase("en")]
    [TestCase("de")]
    public void EveryRecordedAction_BelongsToAGroup_AndHasItsSentence(string language)
    {
        var lang = Path.Combine(RepositoryRoot.Path(typeof(AddOnShapeTests).Assembly, "src"), "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "backoffice", "src", "lang");
        var dictionary = File.ReadAllText(Path.Combine(lang, $"activity.{language}.ts"));
        var keys = Regex.Matches(dictionary, @"^\s{4}([A-Za-z_]+):", RegexOptions.Multiline).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var actions = AddOn.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string) && field.Name.EndsWith("Action", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(AuditRepository.IsAction)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(actions, Is.EquivalentTo(new[] { FormBuilderAI.DraftedAction, FormBuilderAI.ProfileChosenAction }));
            Assert.That(actions.Where(action => AuditGroups.GroupOf(action) is null), Is.Empty, "actions no group holds");
            Assert.That(actions.Select(action => "audit_" + action.Replace('.', '_')).Where(key => !keys.Contains(key)), Is.Empty, $"actions without words in {language}");
        });
    }

    [Test]
    public void EveryStaticField_IsReadOnly_AndHoldsOnlyWhatCannotChange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StaticDataRule.Writable([AddOn]), Is.Empty, "fields that can be set");
            Assert.That(StaticDataRule.Changeable([AddOn]), Is.Empty, "fields whose value can change");
        });
    }

    [Test]
    public void EveryLogMessageThatTakesAnException_TakesALoggedException()
    {
        // As LoggedExceptionTests holds for Core and Web (ADR-0226, 16.5's review): a provider's message may hold what was sent.
        var methods = AddOn.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<Microsoft.Extensions.Logging.LoggerMessageAttribute>() is not null)
            .ToList();
        var raw = methods
            .Where(method => method.GetParameters().Any(parameter => typeof(Exception).IsAssignableFrom(parameter.ParameterType) && parameter.ParameterType != typeof(Core.LoggedException)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(methods, Has.Count.GreaterThanOrEqualTo(3), "the add-on's log messages were found");
            Assert.That(raw, Is.Empty, string.Join(Environment.NewLine, raw));
        });
    }

    [Test]
    public void TheCorePackages_NeverReferenceUmbracoAI_AndTheAddOnDoes()
    {
        static IEnumerable<string> References(Assembly assembly) => assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(References(typeof(Core.FormBuilderConstants).Assembly), Has.None.StartsWith("Umbraco.AI"), "Core (D23)");
            Assert.That(References(typeof(Web.ManagementApi.FormBuilderManagementApi).Assembly), Has.None.StartsWith("Umbraco.AI"), "Web (D23)");
            Assert.That(References(AddOn), Has.Some.EqualTo("Umbraco.AI.Core"));
        });
    }
}
