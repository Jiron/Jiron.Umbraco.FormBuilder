using Jiron.Umbraco.FormBuilder.Core.Access;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Access;

/// <summary>How the evaluation reads a backoffice user (ADR-0048): their groups, the administrators group and the Forms section.</summary>
[TestFixture]
public sealed class AccessUserTests
{
    private const string Section = "Jiron.FormBuilder.Section";

    [Test]
    public void AUser_BringsTheKeysOfTheirGroups_AndWhetherTheyHoldTheFormsSection()
    {
        var editors = Group(Guid.NewGuid(), "editor");
        var writers = Group(Guid.NewGuid(), "writer");

        var user = AccessUser.Of(User([editors, writers], "content", Section));

        Assert.Multiple(() =>
        {
            Assert.That(user.GroupKeys, Is.EquivalentTo(new[] { editors.Key, writers.Key }));
            Assert.That(user.IsAdministrator, Is.False);
            Assert.That(user.HasSection, Is.True);
        });
    }

    [Test]
    public void AUserInTheAdministratorsGroup_IsAnAdministrator_AsUmbracoDecidesIt()
    {
        var admin = AccessUser.Of(User([Group(Constants.Security.AdminGroupKey, Constants.Security.AdminGroupAlias)], Section));
        var alike = AccessUser.Of(User([Group(Guid.NewGuid(), "Admin"), Group(Guid.NewGuid(), "admins")], Section));

        Assert.Multiple(() =>
        {
            Assert.That(Constants.Security.AdminGroupAlias, Is.EqualTo("admin"));
            Assert.That(admin.IsAdministrator, Is.True);
            Assert.That(alike.IsAdministrator, Is.False, "only the alias itself, in its case");
        });
    }

    [Test]
    public void TheFormsSection_IsMatchedExactly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AccessUser.Of(User([], "content", "settings")).HasSection, Is.False);
            Assert.That(AccessUser.Of(User([], Section.ToUpperInvariant())).HasSection, Is.False);
            Assert.That(AccessUser.Of(User([])).GroupKeys, Is.Empty);
            Assert.That(() => AccessUser.Of(null!), Throws.ArgumentNullException);
        });
    }

    private static IReadOnlyUserGroup Group(Guid key, string alias)
    {
        var group = Substitute.For<IReadOnlyUserGroup>();
        group.Key.Returns(key);
        group.Alias.Returns(alias);
        return group;
    }

    private static IUser User(IReadOnlyUserGroup[] groups, params string[] sections)
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns(Guid.NewGuid());
        user.Groups.Returns(groups);
        user.AllowedSections.Returns(sections);
        return user;
    }
}
