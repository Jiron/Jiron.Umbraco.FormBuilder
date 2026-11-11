using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The anonymous form's check (ROADMAP 14.3, ADR-0231): a form for members, one entry per member and a field that takes a
/// value from the member are errors on an anonymous form, a member tag a warning; a form that is not anonymous has none,
/// and rules on whether a member is signed in are fine.
/// </summary>
[TestFixture]
public sealed class AnonymousFormCheckTests
{
    private const string Name = """{"key":"30000000-0000-4000-8000-000000000001","type":"text","alias":"name","label":{"":"Name"},"prefill":{"member":"name"}}""";

    private const string Note = """{"key":"30000000-0000-4000-8000-000000000002","type":"text","alias":"note","label":{"":"Note"},"help":{"":"Hello {{ member.name }}"},"rules":[{"key":"40000000-0000-4000-8000-000000000001","effect":"show","conditions":[{"context":"memberSignedIn","op":"isChecked"}]}]}""";

    private static readonly FieldTypeRegistry Types = TestFieldTypes.Build();

    [Test]
    public async Task AnAnonymousForm_RefusesWhatNeedsTheMember_AndWarnsOfAMemberTag()
    {
        var issues = await new AnonymousFormCheck(Types).CheckAsync(Form("""{"privacy":{"anonymous":true},"availability":{"members":"members","onePerMember":true}}""", Name, Note), CancellationToken.None);

        Assert.That(issues.Select(issue => (issue.Severity, issue.Code, issue.Path)), Is.EquivalentTo(new[]
        {
            (FormCheckSeverity.Error, AnonymousFormCheck.AnonymousForMembers, "$.settings.availability.members"),
            (FormCheckSeverity.Error, AnonymousFormCheck.AnonymousOnePerMember, "$.settings.availability.onePerMember"),
            (FormCheckSeverity.Error, AnonymousFormCheck.AnonymousTakesMember, "$.pages[0].groups[0].fields[0].prefill.member"),
            (FormCheckSeverity.Warning, AnonymousFormCheck.AnonymousMemberTag, "$.pages[0].groups[0].fields[1].help['']"),
        }));
    }

    [Test]
    public async Task AFormThatIsNotAnonymous_OrOneWhoseRulesOnlyAskWhetherAMemberIsSignedIn_HasNone()
    {
        var plain = await new AnonymousFormCheck(Types).CheckAsync(Form("""{"availability":{"members":"members","onePerMember":true}}""", Name, Note), CancellationToken.None);
        var rulesOnly = await new AnonymousFormCheck(Types).CheckAsync(Form("""{"privacy":{"anonymous":true}}""", Note.Replace("Hello {{ member.name }}", "Hello", StringComparison.Ordinal)), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.Empty, "the member link is the form's to keep");
            Assert.That(rulesOnly, Is.Empty, "a rule on whether a member is signed in keeps nothing");
        });
    }

    // A form of one page and group with these fields and these settings.
    private static FormDefinition Form(string settings, params string[] fields) =>
        DefinitionFixtures.Read("{\"schemaVersion\":1,\"key\":\"f0000000-0000-4000-8000-000000000001\",\"settings\":" + settings + ",\"pages\":[{\"key\":\"10000000-0000-4000-8000-000000000001\",\"groups\":[{\"key\":\"20000000-0000-4000-8000-000000000001\",\"fields\":[" + string.Join(",", fields) + "]}]}]}");
}
