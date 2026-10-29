using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// Whom a form admits (ADR-0095): anyone a form for anyone, without asking who is signed in; a signed-in member a form
/// for members; a member of one of its groups a form for groups, by the groups' names in any letter case; and nobody a
/// form whose groups are none or gone.
/// </summary>
[TestFixture]
public sealed class FormMembersTests
{
    private static readonly Guid Readers = Guid.Parse("30000000-0000-4000-8000-0000000062c1");
    private static readonly Guid Staff = Guid.Parse("30000000-0000-4000-8000-0000000062c2");

    private IMemberManager _members = null!;
    private IMemberGroupService _groups = null!;

    [SetUp]
    public void CreateTheSite()
    {
        _members = Substitute.For<IMemberManager>();
        _groups = Substitute.For<IMemberGroupService>();
        _groups.GetAsync(Arg.Any<IEnumerable<Guid>>()).Returns(call => Task.FromResult(call.Arg<IEnumerable<Guid>>()
            .Select(key => key == Readers ? Group("Readers") : key == Staff ? Group("Staff") : null)
            .OfType<IMemberGroup>()));
    }

    [Test]
    public async Task AFormForAnyone_AdmitsEveryVisitor_WithoutAskingWhoIsSignedIn()
    {
        Assert.That(await Members().GateAsync(new FormAvailabilitySettings()), Is.Null);
        await _members.DidNotReceive().GetCurrentMemberAsync();
    }

    [Test]
    public async Task AFormForMembers_AsksAVisitorToSignIn_AndAdmitsAnyMember()
    {
        var form = new FormAvailabilitySettings { Access = MemberAccess.Members };

        var visitor = await Members().GateAsync(form);
        SignIn("Staff");
        var member = await Members().GateAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That(visitor, Is.EqualTo(MemberGate.SignIn));
            Assert.That(member, Is.Null);
        });
        await _groups.DidNotReceive().GetAsync(Arg.Any<IEnumerable<Guid>>());
    }

    [Test]
    public async Task AFormForGroups_AdmitsAMemberOfOne_InAnyLetterCase_AndNoOtherMember()
    {
        var form = new FormAvailabilitySettings { Access = MemberAccess.Groups, MemberGroups = [Staff, Readers] };

        var visitor = await Members().GateAsync(form);
        SignIn("readers");
        var reader = await Members().GateAsync(form);
        SignIn("Volunteers");
        var volunteer = await Members().GateAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That(visitor, Is.EqualTo(MemberGate.SignIn), "signing in comes first");
            Assert.That(reader, Is.Null);
            Assert.That(volunteer, Is.EqualTo(MemberGate.Groups));
        });
    }

    [Test]
    public async Task AFormWhoseGroupsAreGoneOrNone_AdmitsNoMember()
    {
        SignIn("Readers");

        Assert.Multiple(async () =>
        {
            Assert.That(await Members().GateAsync(new FormAvailabilitySettings { Access = MemberAccess.Groups, MemberGroups = [Guid.NewGuid()] }), Is.EqualTo(MemberGate.Groups));
            Assert.That(await Members().GateAsync(new FormAvailabilitySettings { Access = MemberAccess.Groups }), Is.EqualTo(MemberGate.Groups));
        });
        await _groups.Received(1).GetAsync(Arg.Any<IEnumerable<Guid>>());
    }

    [Test]
    public void WithoutAvailability_TheQuestionIsRefused() =>
        Assert.That(() => Members().GateAsync(null!), Throws.ArgumentNullException);

    private FormMembers Members() => new(_members, _groups);

    private void SignIn(params string[] roles)
    {
        var member = new MemberIdentityUser();
        _members.GetCurrentMemberAsync().Returns(Task.FromResult<MemberIdentityUser?>(member));
        _members.GetRolesAsync(member).Returns(Task.FromResult<IList<string>>([.. roles]));
    }

    private static IMemberGroup Group(string name)
    {
        var group = Substitute.For<IMemberGroup>();
        group.Name.Returns(name);
        return group;
    }
}
