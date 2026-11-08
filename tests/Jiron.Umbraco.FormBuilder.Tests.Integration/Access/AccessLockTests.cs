using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// A site whose host locks the access section (<c>JironFormBuilder:LockedSections</c>, ADR-0048): no grant and no
/// restriction changes, globally, on a folder or on a form, while the evaluation still reads what is stored and a user
/// group Umbraco deletes still takes its grants.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class AccessLockTests : RepositoryTestBase
{
    private const string LockKey = "JironFormBuilder:LockedSections:1";

    /// <summary>The host configuration of every test's site; the second entry locks the access section.</summary>
    public AccessLockTests()
    {
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:0"] = "privacy";
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:1"] = "Access";
    }

    private IFormBuilderAccessService Access => GetRequiredService<IFormBuilderAccessService>();

    [Test]
    public async Task EveryChangeOfAccess_IsRefused_NamingTheLock_AndWritesNothing()
    {
        var group = await GetRequiredService<TestPeople>().EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbLocked", Name = "Locked" });
        var hr = await GetRequiredService<IFormBuilderFolderService>().CreateAsync(new NewFolderRequest("HR", UserKey: Editor));
        var payroll = await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Payroll", UserKey: Editor));
        var entries = Count("jfbAuditEntry");

        Assert.Multiple(async () =>
        {
            Assert.That(
                () => Access.ChangeGrantAsync(new AccessGrantChange(group.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, Editor)),
                Throws.InstanceOf<FormBuilderSettingLockedException>()
                    .With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("access")
                    .And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(LockKey));
            Assert.That(
                () => Access.ChangeGrantAsync(new AccessGrantChange(group.Key, PermissionTarget.Folder, hr.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, Editor)),
                Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(LockKey));
            Assert.That(
                () => Access.ChangeGrantAsync(new AccessGrantChange(group.Key, PermissionTarget.Form, payroll.Key, FormBuilderPermissions.None, FormBuilderPermissions.None, Editor)),
                Throws.InstanceOf<FormBuilderSettingLockedException>(),
                "even with nothing to change");
            Assert.That(() => Access.RestrictFolderAsync(hr.Key, true, Editor), Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("access"));
            Assert.That(() => Access.RestrictFormAsync(payroll.Key, payroll.ConcurrencyVersion, true, Editor), Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("access"));
            Assert.That(Count("jfbPermission"), Is.Zero);
            Assert.That((await Folders.GetAsync(hr.Key))!.IsRestricted, Is.False);
            Assert.That((await Forms.GetAsync(payroll.Key))!.IsRestricted, Is.False);
            Assert.That(Count("jfbAuditEntry"), Is.EqualTo(entries));
        });
    }

    [Test]
    public async Task ASaveOfSeveralGrants_IsRefused_NamingTheLock_AndTheLockIsTheWholeSection()
    {
        var group = await GetRequiredService<TestPeople>().EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbLocked", Name = "Locked" });
        var hr = await GetRequiredService<IFormBuilderFolderService>().CreateAsync(new NewFolderRequest("HR", UserKey: Editor));

        Assert.Multiple(() =>
        {
            Assert.That(Access.LockOf(PermissionTarget.Folder), Is.EqualTo(new AccessHostLock("*", LockKey)));
            Assert.That(Access.LockOf(PermissionTarget.Global), Is.EqualTo(new AccessHostLock("*", LockKey)));
            Assert.That(
                () => Access.SaveAsync(new AccessSave(PermissionTarget.Folder, hr.Key, [new AccessGrantEdit(group.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries)], true, null, Editor)),
                Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(LockKey));
            Assert.That(Count("jfbPermission"), Is.Zero);
        });
    }

    [Test]
    public async Task TheStoredGrants_StillCount_AndADeletedGroup_StillTakesItsGrants()
    {
        var group = await GetRequiredService<TestPeople>().EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbLocked", Name = "Locked" });
        await Permissions.ChangeAsync(new GrantChange(group.Key, PermissionTarget.Global, Guid.Empty, 0, (int)FormBuilderPermissions.ViewEntries));
        var user = new AccessUser(Guid.NewGuid(), new HashSet<Guid> { group.Key }, false, true);
        var before = await Access.GetGlobalAsync(user);

        var deleted = await GetRequiredService<IUserGroupService>().DeleteAsync(group.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(FormBuilderPermissions.ViewEntries), "the lock keeps grants as they are");
            Assert.That(deleted.Success, Is.True);
            Assert.That(await Permissions.ListForGroupsAsync([group.Key]), Is.Empty, "Umbraco's delete is no change of an editor's");
            Assert.That(await Access.GetGlobalAsync(user), Is.EqualTo(FormBuilderPermissions.None));
        });
    }
}
