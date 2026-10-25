using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// A site whose host sets the global grants (<c>JironFormBuilder:Access:Grants</c>, ADR-0048): they replace the stored
/// global grants and lock them, folder and form grants still work, a group the host names counts once the site has it,
/// and the site checks the grants when it starts. The fixture gives Umbraco's test host a runtime cache, as a site has.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class HostGrantsBootTests : RepositoryTestBase
{
    private const string Grants = FormBuilderOptions.SectionName + ":Access:Grants:";

    /// <summary>The host configuration of every test's site, as a host passes it in its appsettings.json.</summary>
    public HostGrantsBootTests()
    {
        InMemoryConfiguration[Grants + "0:Group"] = "jfbHostViewers";
        InMemoryConfiguration[Grants + "0:Permissions:0"] = "viewEntries";
        InMemoryConfiguration[Grants + "0:Permissions:1"] = "export";
        InMemoryConfiguration[Grants + "1:Group"] = "jfbLater";
        InMemoryConfiguration[Grants + "1:Permissions:0"] = "design";
        InMemoryConfiguration[Grants + "1:Permissions:1"] = "manageLibrary";
    }

    private IFormBuilderAccessService Access => GetRequiredService<IFormBuilderAccessService>();

    private TestPeople People => GetRequiredService<TestPeople>();

    [Test]
    public async Task TheHostsGrants_ReplaceTheStoredGlobalGrants()
    {
        var viewers = await GroupAsync("jfbHostViewers");
        var others = await GroupAsync("jfbOthers");

        // Grants stored before the host set its own.
        await Permissions.ChangeAsync(new GrantChange(viewers, PermissionTarget.Global, Guid.Empty, 0, (int)FormBuilderPermissions.Design));
        await Permissions.ChangeAsync(new GrantChange(others, PermissionTarget.Global, Guid.Empty, 0, (int)FormBuilderPermissions.ViewEntries));

        Assert.Multiple(async () =>
        {
            Assert.That(await Access.GetGlobalAsync(Member(viewers)), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export));
            Assert.That(await Access.GetGlobalAsync(Member(others)), Is.EqualTo(FormBuilderPermissions.None), "a stored global grant no longer counts");
            Assert.That(await Access.GetForFolderAsync(Member(viewers), null), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export), "the root follows them");
            Assert.That(await Access.GetGlobalAsync(Member(viewers, others)), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export));
        });
    }

    [Test]
    public async Task AGroupTheHostNames_GrantsNothingUntilTheSiteHasIt_ThenAtOnce()
    {
        var unknown = Member(Guid.NewGuid());
        var before = await Access.GetGlobalAsync(unknown);

        var later = await GroupAsync("jfbLater");

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(await Access.GetGlobalAsync(Member(later)), Is.EqualTo(FormBuilderPermissions.Design | FormBuilderPermissions.ManageLibrary), "saving the group dropped the cached snapshot");
            Assert.That(GetRequiredService<AppCaches>().RuntimeCache, Is.InstanceOf<ObjectCacheAppCache>(), "the test of the cache uses a real one");
        });
    }

    [Test]
    public async Task AGlobalChange_IsRefused_NamingTheHostsKey_WhileFolderGrantsStillWork()
    {
        var viewers = await GroupAsync("jfbHostViewers");
        var events = await GetRequiredService<IFormBuilderFolderService>().CreateAsync(new NewFolderRequest("Events", UserKey: Editor));

        var granted = await Access.ChangeGrantAsync(new AccessGrantChange(viewers, PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design, Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(
                () => Access.ChangeGrantAsync(new AccessGrantChange(viewers, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.Design, Editor)),
                Throws.InstanceOf<FormBuilderSettingLockedException>()
                    .With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("access.grants")
                    .And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo("JironFormBuilder:Access:Grants"));
            Assert.That(granted!.Permissions, Is.EqualTo(FormBuilderPermissions.Design));
            Assert.That(await Access.GetForFolderAsync(Member(viewers), events.Key), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export | FormBuilderPermissions.Design));
            Assert.That(Count("jfbPermission", $"{Q("targetType")} = @0", StoredNames.Of(PermissionTarget.Global)), Is.Zero, "nothing stored globally");
        });
    }

    [Test]
    public void TheSite_BindsTheHostsGrants_AndRefusesInvalidOnes_WhenItStarts()
    {
        var options = GetRequiredService<IOptions<FormBuilderOptions>>().Value;
        var validation = GetRequiredService<IEnumerable<IValidateOptions<FormBuilderOptions>>>().Single();
        var invalid = new FormBuilderOptions { Access = { Grants = { new HostGrant { Group = "editor", Permissions = { "export", "ManageEverything" } } } } };

        Assert.Multiple(() =>
        {
            Assert.That(options.Access.Grants.Select(grant => (grant.Group, string.Join(",", grant.Permissions))), Is.EqualTo(new[] { ("jfbHostViewers", "viewEntries,export"), ("jfbLater", "design,manageLibrary") }));
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing);
            Assert.That(
                validation.Validate(Options.DefaultName, invalid).Failures,
                Is.EqualTo(new[]
                {
                    $"{Grants}0:Permissions:1 ('ManageEverything') is not a permission; use one of design, manageActions, viewEntries, viewSensitive, editEntries, deleteEntries, export, manageLibrary, manageSettings, managePermissions, privacyTools.",
                    $"{Grants}0:Permissions grants 'export' without 'viewEntries', which it needs.",
                }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
    }

    private static AccessUser Member(params Guid[] groupKeys) => new(Guid.NewGuid(), groupKeys.ToHashSet(), false, true);

    private async Task<Guid> GroupAsync(string alias) =>
        (await People.EnsureUserGroupAsync(new TestUserGroup { Alias = alias, Name = alias, Sections = { AccessPersonas.Section } })).Key;
}
