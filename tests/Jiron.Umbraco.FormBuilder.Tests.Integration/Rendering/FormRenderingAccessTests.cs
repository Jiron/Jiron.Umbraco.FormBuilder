using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;
using IElement = AngleSharp.Dom.IElement;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// Who sees a form for members (ROADMAP 6.2c, ADR-0095): a visitor who is not signed in gets its notice in the page's
/// culture, a member of none of its groups the groups' notice, and a member it admits the form; a form for anyone beside
/// it renders as usual; and a form for members that takes no entry now says so first, since signing in would not help.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingAccessTests : RenderingTestBase
{
    [Test]
    public async Task AFormForMembers_ShowsItsNoticeInThePagesCulture_ToAVisitorWhoIsNotSignedIn()
    {
        _ = await PublishFormAsync("renderingMembers", definition => For(definition, MemberAccess.Members));
        _ = await PublishFormAsync("renderingContact");
        _ = await CulturePagesAsync("de-DE", "German", "members");
        var (_, document) = await PageAsync("members");

        var gated = document.QuerySelector("#jfb-1")!;
        Assert.Multiple(() =>
        {
            Assert.That(gated.LocalName, Is.EqualTo("div"), "no form to fill in");
            Assert.That(gated.GetAttribute("lang"), Is.EqualTo("de-DE"));
            Assert.That(gated.QuerySelector(".jfb-notice h2")!.TextContent, Is.EqualTo("Dieses Formular ist nur f" + (char)0xFC + "r Mitglieder"));
            Assert.That(gated.QuerySelector(".jfb-notice p")!.TextContent, Is.EqualTo("Melden Sie sich an, um es auszuf" + (char)0xFC + "llen."));
            Assert.That(gated.QuerySelectorAll("input, script[data-jfb-model]"), Is.Empty, "nothing of the form");
            Assert.That(document.QuerySelector("#jfb-2")!.LocalName, Is.EqualTo("form"), "a form for anyone renders beside it");
        });
    }

    [Test]
    public async Task ASignedInMember_SeesAFormForMembers_AndOneForTheirGroup_ButOneForOtherGroupsShowsItsNotice()
    {
        var readers = await GroupAsync("Readers");
        var staff = await GroupAsync("Staff");
        _ = await PublishFormAsync("renderingMembers", definition => For(definition, MemberAccess.Members));
        _ = await PublishFormAsync("renderingReaders", definition => For(definition, MemberAccess.Groups, readers));
        _ = await PublishFormAsync("renderingStaff", definition => For(definition, MemberAccess.Groups, staff));
        _ = await PagesAsync("groups");
        await SignInMemberAsync("reader@jfb.test");
        GetRequiredService<IMemberService>().AssignRole(MemberName, "Readers");

        var (_, document) = await PageAsync("groups");

        var other = document.QuerySelector("#jfb-3")!;
        Assert.Multiple(() =>
        {
            Assert.That(document.QuerySelector("#jfb-1")!.LocalName, Is.EqualTo("form"), "a signed-in member may fill in a form for members");
            Assert.That(document.QuerySelector("#jfb-2")!.LocalName, Is.EqualTo("form"), "and one for their group");
            Assert.That(other.LocalName, Is.EqualTo("div"));
            Assert.That(Notice(other), Is.EqualTo(("This form is not open to your account", "It is for members of certain groups.")));
        });
    }

    [Test]
    public async Task AFormForAGroupThatIsGone_AdmitsNoMember_AndAVisitorWhoIsNotSignedIn_IsAskedToSignIn()
    {
        _ = await PublishFormAsync("renderingStaff", definition => For(definition, MemberAccess.Groups, Guid.NewGuid()));
        _ = await PublishFormAsync("renderingReaders");
        _ = await PublishFormAsync("renderingMembers", definition => For(definition, MemberAccess.Members));
        _ = await PagesAsync("groups");

        var (_, visitor) = await PageAsync("groups");
        await SignInMemberAsync("member@jfb.test");
        var (_, member) = await PageAsync("groups");

        Assert.Multiple(() =>
        {
            Assert.That(Notice(visitor.QuerySelector("#jfb-3")!), Is.EqualTo(("This form is for members", "Sign in to fill it in.")), "signing in comes first");
            Assert.That(Notice(member.QuerySelector("#jfb-3")!), Is.EqualTo(("This form is not open to your account", "It is for members of certain groups.")), "a group that is gone admits nobody");
            Assert.That((visitor.QuerySelector("#jfb-2")!.LocalName, member.QuerySelector("#jfb-2")!.LocalName), Is.EqualTo(("form", "form")), "a form for anyone is for both");
            Assert.That(member.QuerySelector("#jfb-1")!.LocalName, Is.EqualTo("form"));
        });
    }

    [Test]
    public async Task AClosedFormForMembers_SaysItIsClosed_BeforeAskingToSignIn()
    {
        var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _ = await PublishFormAsync("renderingMembers", definition =>
        {
            var members = For(definition, MemberAccess.Members);
            return members with { Settings = members.Settings with { Availability = members.Settings.Availability with { ClosesAt = past } } };
        });
        _ = await PublishFormAsync("renderingContact");
        _ = await PagesAsync("members");

        var (_, visitor) = await PageAsync("members");

        Assert.That(Notice(visitor.QuerySelector("#jfb-1")!), Is.EqualTo(("This form is closed", "It no longer takes answers.")));
    }

    private static FormDefinition For(FormDefinition definition, MemberAccess access, params Guid[] groups) =>
        definition with { Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = access, MemberGroups = groups } } };

    private async Task<Guid> GroupAsync(string name)
    {
        var created = await GetRequiredService<IMemberGroupService>().CreateAsync(new MemberGroup { Name = name });
        Assert.That(created.Success, Is.True, created.Status.ToString());
        return created.Result!.Key;
    }

    private static (string, string) Notice(IElement form) =>
        (form.QuerySelector(".jfb-notice h2")?.TextContent ?? "(no heading)", form.QuerySelector(".jfb-notice p")?.TextContent ?? "(no text)");
}
