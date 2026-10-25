using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Access;

/// <summary>
/// The evaluation (ADR-0013, ADR-0048): a union of the user's groups' grants within the boundary of the nearest
/// restricted folder or form, administrators holding everything, nothing without the Forms section, the scope of a
/// user's lists, the folders on the way to what they may see, and what a move gives each group. The tree:
/// <code>
/// Company
///   Events
///   Hr (restricted)
///     Payroll
///       Secret (restricted)
/// Loose
/// </code>
/// </summary>
[TestFixture]
public sealed class AccessSnapshotTests
{
    private const FormBuilderPermissions View = FormBuilderPermissions.ViewEntries;
    private const FormBuilderPermissions Design = FormBuilderPermissions.Design;
    private const FormBuilderPermissions Export = FormBuilderPermissions.Export;

    private static readonly Guid Viewers = Key(101);
    private static readonly Guid Designers = Key(102);
    private static readonly Guid HrTeam = Key(103);
    private static readonly Guid PayrollTeam = Key(104);
    private static readonly Guid Contact = Key(201);

    private static readonly AccessFolder Company = new(1, Key(1), null, false);
    private static readonly AccessFolder Events = new(2, Key(2), 1, false);
    private static readonly AccessFolder Hr = new(3, Key(3), 1, true);
    private static readonly AccessFolder Payroll = new(4, Key(4), 3, false);
    private static readonly AccessFolder Secret = new(5, Key(5), 4, true);
    private static readonly AccessFolder Loose = new(6, Key(6), null, false);
    private static readonly AccessFolder[] Tree = [Company, Events, Hr, Payroll, Secret, Loose];

    [Test]
    public void WithoutTheFormsSection_AUserHoldsNothing_AnAdministratorIncluded()
    {
        var snapshot = Snapshot(Grant(Viewers, PermissionTarget.Global, View));
        AccessUser[] withoutSection = [User(false, Viewers), Administrator() with { HasSection = false }];

        Assert.Multiple(() =>
        {
            foreach (var user in withoutSection)
            {
                Assert.That(snapshot.Global(user), Is.EqualTo(FormBuilderPermissions.None));
                Assert.That(snapshot.Folder(user, null), Is.EqualTo(FormBuilderPermissions.None));
                Assert.That(snapshot.Folder(user, Events.Key), Is.EqualTo(FormBuilderPermissions.None));
                Assert.That(snapshot.Form(user, InFolder(Events)), Is.EqualTo(FormBuilderPermissions.None));
                Assert.That(snapshot.FormVisibilityOf(user, View), Is.SameAs(FormVisibility.Nothing));
                Assert.That(snapshot.FormVisibilityOf(user, View).IsNothing, Is.True);
            }
        });
    }

    [Test]
    public void AnAdministrator_HoldsEverything_Everywhere_RestrictionsIncluded()
    {
        var snapshot = Snapshot();
        var admin = Administrator();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Global(admin), Is.EqualTo(PermissionRules.All));
            Assert.That(snapshot.Folder(admin, null), Is.EqualTo(PermissionRules.FolderLevel), "on a folder, the permissions a folder knows");
            Assert.That(snapshot.Folder(admin, Secret.Key), Is.EqualTo(PermissionRules.FolderLevel));
            Assert.That(snapshot.Form(admin, InFolder(Hr) with { IsRestricted = true }), Is.EqualTo(PermissionRules.FolderLevel));
            Assert.That(snapshot.FormVisibilityOf(admin, View), Is.SameAs(FormVisibility.Everything));
            Assert.That(snapshot.FolderVisibilityOf(snapshot.FormVisibilityOf(admin, View), []), Is.SameAs(FolderVisibility.Everything));
            Assert.That(snapshot.FolderVisibilityOf(snapshot.FormVisibilityOf(admin, View), []).Allows(Guid.NewGuid()), Is.True);
        });
    }

    [Test]
    public void AGlobalGrant_ReachesEveryFolderAndForm_OutsideTheRestrictions()
    {
        var snapshot = Snapshot(Grant(Viewers, PermissionTarget.Global, View));
        var viewer = User(Viewers);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Global(viewer), Is.EqualTo(View));
            Assert.That(snapshot.Folder(viewer, null), Is.EqualTo(View), "the root");
            Assert.That(snapshot.Folder(viewer, Events.Key), Is.EqualTo(View));
            Assert.That(snapshot.Form(viewer, AtTheRoot()), Is.EqualTo(View));
            Assert.That(snapshot.Form(viewer, InFolder(Events)), Is.EqualTo(View));
            Assert.That(snapshot.Folder(viewer, Hr.Key), Is.EqualTo(FormBuilderPermissions.None), "a restricted folder");
            Assert.That(snapshot.Folder(viewer, Payroll.Key), Is.EqualTo(FormBuilderPermissions.None), "below it");
            Assert.That(snapshot.Form(viewer, InFolder(Payroll)), Is.EqualTo(FormBuilderPermissions.None));
        });
    }

    [Test]
    public void TheGlobalOnlyPermissions_StayGlobal()
    {
        var snapshot = Snapshot(Grant(Designers, PermissionTarget.Global, Design | FormBuilderPermissions.ManageActions | FormBuilderPermissions.ManageLibrary));
        var designer = User(Designers);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Global(designer), Is.EqualTo(Design | FormBuilderPermissions.ManageActions | FormBuilderPermissions.ManageLibrary));
            Assert.That(snapshot.Folder(designer, null), Is.EqualTo(Design | FormBuilderPermissions.ManageActions));
            Assert.That(snapshot.Folder(designer, Events.Key), Is.EqualTo(Design | FormBuilderPermissions.ManageActions));
            Assert.That(snapshot.Form(designer, InFolder(Events)), Is.EqualTo(Design | FormBuilderPermissions.ManageActions));
        });
    }

    [Test]
    public void TheGrants_AreAUnion_OverTheUsersGroups_AndDownTheTree()
    {
        var snapshot = Snapshot(
            Grant(Viewers, PermissionTarget.Global, View),
            Grant(Designers, PermissionTarget.Folder, Design, Company.Key),
            Grant(HrTeam, PermissionTarget.Form, Export | View, Contact));
        var all = User(Viewers, Designers, HrTeam);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Form(all, InFolder(Events)), Is.EqualTo(View | Design | Export));
            Assert.That(snapshot.Form(all, InFolder(Events) with { Key = Guid.NewGuid() }), Is.EqualTo(View | Design), "another form in the folder");
            Assert.That(snapshot.Form(User(Designers), InFolder(Events)), Is.EqualTo(Design));
            Assert.That(snapshot.Form(User(Designers), AtTheRoot()), Is.EqualTo(FormBuilderPermissions.None), "outside the folder");
            Assert.That(snapshot.Folder(all, Loose.Key), Is.EqualTo(View));
            Assert.That(snapshot.Global(all), Is.EqualTo(View), "folder and form grants are not global");
        });
    }

    [Test]
    public void ARestrictedFolder_StopsTheGrantsAboveIt_AndCountsItsOwnAndThoseBelow()
    {
        var snapshot = Snapshot(
            Grant(Viewers, PermissionTarget.Global, View),
            Grant(Designers, PermissionTarget.Folder, Design, Company.Key),
            Grant(HrTeam, PermissionTarget.Folder, View, Hr.Key),
            Grant(PayrollTeam, PermissionTarget.Folder, Export | View, Payroll.Key),
            Grant(HrTeam, PermissionTarget.Folder, Export, Payroll.Key));
        var everyone = User(Viewers, Designers);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Folder(everyone, Hr.Key), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Form(everyone, InFolder(Payroll)), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Folder(User(HrTeam), Hr.Key), Is.EqualTo(View));
            Assert.That(snapshot.Form(User(HrTeam), InFolder(Hr)), Is.EqualTo(View));
            Assert.That(snapshot.Form(User(HrTeam), InFolder(Payroll)), Is.EqualTo(View | Export), "the restricted folder's grants, and those between it and the form");
            Assert.That(snapshot.Form(User(PayrollTeam), InFolder(Payroll)), Is.EqualTo(View | Export));
            Assert.That(snapshot.Form(User(PayrollTeam), InFolder(Hr)), Is.EqualTo(FormBuilderPermissions.None), "a grant below does not reach up");
            Assert.That(snapshot.Form(User(HrTeam, Viewers, Designers), InFolder(Payroll)), Is.EqualTo(View | Export), "the union stays inside the boundary");
        });
    }

    [Test]
    public void TheNearestRestriction_IsTheBoundary()
    {
        var snapshot = Snapshot(
            Grant(HrTeam, PermissionTarget.Folder, View, Hr.Key),
            Grant(PayrollTeam, PermissionTarget.Folder, Design, Secret.Key));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Folder(User(HrTeam), Secret.Key), Is.EqualTo(FormBuilderPermissions.None), "Hr's grant stops at Secret");
            Assert.That(snapshot.Form(User(HrTeam), InFolder(Secret)), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Form(User(PayrollTeam), InFolder(Secret)), Is.EqualTo(Design));
            Assert.That(snapshot.Folder(User(PayrollTeam), Payroll.Key), Is.EqualTo(FormBuilderPermissions.None));
        });
    }

    [Test]
    public void ARestrictedForm_HoldsOnlyItsOwnGrants()
    {
        var snapshot = Snapshot(
            Grant(Viewers, PermissionTarget.Global, View),
            Grant(Designers, PermissionTarget.Folder, Design, Company.Key),
            Grant(HrTeam, PermissionTarget.Form, Export | View, Contact));
        var restricted = InFolder(Events) with { IsRestricted = true };

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Form(User(Viewers, Designers), restricted), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Form(User(HrTeam), restricted), Is.EqualTo(Export | View));
            Assert.That(snapshot.Form(User(HrTeam, Viewers, Designers), restricted), Is.EqualTo(Export | View));
            Assert.That(snapshot.Form(User(HrTeam), AtTheRoot() with { IsRestricted = true }), Is.EqualTo(Export | View), "at the root too");
        });
    }

    [Test]
    public void AFolderTheSnapshotDoesNotKnow_GivesOnlyItsOwnGrants_SoItFailsClosed()
    {
        var unknown = Guid.NewGuid();
        var snapshot = Snapshot(Grant(Viewers, PermissionTarget.Global, View), Grant(Designers, PermissionTarget.Folder, Design, unknown));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Folder(User(Viewers), unknown), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Form(User(Viewers), new FormAccessTarget(Contact, unknown, false)), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Folder(User(Designers), unknown), Is.EqualTo(Design));
            Assert.That(snapshot.Form(User(Designers, Viewers), new FormAccessTarget(Contact, unknown, false)), Is.EqualTo(Design));
        });
    }

    [Test]
    public void AFolderWhoseParentTheSnapshotDoesNotKnow_InheritsNothing()
    {
        var orphan = new AccessFolder(7, Key(7), 99, false);
        var snapshot = Snapshot([Company, orphan], Grant(Viewers, PermissionTarget.Global, View), Grant(Designers, PermissionTarget.Folder, Design, orphan.Key));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Folder(User(Viewers), orphan.Key), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Folder(User(Viewers, Designers), orphan.Key), Is.EqualTo(Design));
            Assert.That(snapshot.Folder(User(Viewers), Company.Key), Is.EqualTo(View));
        });
    }

    [Test]
    public void AFolderInARing_FailsClosed_AndTheWalkEnds()
    {
        // No folder may be its own ancestor; if the data ever held a ring, the walk would meet no root.
        AccessFolder[] ring = [new(1, Key(1), 2, false), new(2, Key(2), 1, false)];
        var snapshot = Snapshot(ring, Grant(Viewers, PermissionTarget.Global, View), Grant(Designers, PermissionTarget.Folder, Design, Key(2)));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Folder(User(Viewers), Key(1)), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Folder(User(Designers), Key(1)), Is.EqualTo(Design));
            Assert.That(snapshot.FormVisibilityOf(User(Viewers), View).FolderIds, Is.Empty);
        });
    }

    [Test]
    public void HostGrants_ReplaceTheStoredGlobalGrants_AndLeaveTheFolderAndFormGrants()
    {
        var snapshot = AccessSnapshot.Create(
            [Grant(Designers, PermissionTarget.Global, Design), Grant(Designers, PermissionTarget.Folder, Design, Events.Key), Grant(HrTeam, PermissionTarget.Form, View, Contact)],
            Tree,
            new Dictionary<Guid, FormBuilderPermissions> { [Viewers] = View | FormBuilderPermissions.ManageSettings });

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HostGrants, Is.True);
            Assert.That(Snapshot().HostGrants, Is.False);
            Assert.That(snapshot.Global(User(Designers)), Is.EqualTo(FormBuilderPermissions.None), "the stored global grant counts for nothing");
            Assert.That(snapshot.Folder(User(Designers), Events.Key), Is.EqualTo(Design));
            Assert.That(snapshot.Global(User(Viewers)), Is.EqualTo(View | FormBuilderPermissions.ManageSettings));
            Assert.That(snapshot.Form(User(Viewers), InFolder(Events)), Is.EqualTo(View));
            Assert.That(snapshot.Form(User(HrTeam), InFolder(Events)), Is.EqualTo(View));
        });
    }

    [Test]
    public void AStoredValueThisVersionDoesNotKnow_GrantsNothing_AndAFolderOrFormGrantNeverGrantsAGlobalOnlyPermission()
    {
        var snapshot = Snapshot(
            new StoredGrant(1, Viewers, PermissionTarget.Global, Guid.Empty, 4096 | (int)View),
            new StoredGrant(2, Designers, PermissionTarget.Folder, Events.Key, (int)(Design | FormBuilderPermissions.ManageSettings)),
            new StoredGrant(3, HrTeam, PermissionTarget.Form, Contact, (int)(View | FormBuilderPermissions.PrivacyTools)));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Global(User(Viewers)), Is.EqualTo(View));
            Assert.That(snapshot.Folder(User(Designers), Events.Key), Is.EqualTo(Design));
            Assert.That(snapshot.Global(User(Designers)), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(snapshot.Form(User(HrTeam), AtTheRoot()), Is.EqualTo(View));
            Assert.That(snapshot.Global(User(HrTeam)), Is.EqualTo(FormBuilderPermissions.None));
        });
    }

    [Test]
    public void TheScope_NamesTheFoldersAndTheRoot_WhereTheUserHoldsOneOfThePermissions()
    {
        var snapshot = Snapshot(
            Grant(Viewers, PermissionTarget.Global, View),
            Grant(Designers, PermissionTarget.Folder, Design, Events.Key),
            Grant(HrTeam, PermissionTarget.Folder, View, Payroll.Key));

        var viewer = snapshot.FormVisibilityOf(User(Viewers), View);
        var designerViewing = snapshot.FormVisibilityOf(User(Designers), View);
        var designerDesigning = snapshot.FormVisibilityOf(User(Designers), Design | View);
        var hr = snapshot.FormVisibilityOf(User(HrTeam), PermissionRules.FolderLevel);

        Assert.Multiple(() =>
        {
            Assert.That(viewer.Root, Is.True);
            Assert.That(viewer.FolderIds, Is.EqualTo(new[] { 1, 2, 6 }), "not Hr, the folders below it, or Secret");
            Assert.That(viewer.GroupKeys, Is.Empty);
            Assert.That(viewer.AnyOf, Is.EqualTo(View));
            Assert.That(designerViewing.IsNothing, Is.True, "a designer may not view entries");
            Assert.That(designerDesigning.Root, Is.False);
            Assert.That(designerDesigning.FolderIds, Is.EqualTo(new[] { 2 }));
            Assert.That(hr.Root, Is.False);
            Assert.That(hr.FolderIds, Is.EqualTo(new[] { 4 }), "Secret is restricted below Payroll");
            Assert.That(viewer.IsEverything || viewer.IsNothing, Is.False);
        });
    }

    [Test]
    public void TheScope_NamesTheGroupsWhoseFormGrantsHoldOneOfThePermissions()
    {
        var other = Key(202);
        var snapshot = Snapshot(
            Grant(HrTeam, PermissionTarget.Form, View, Contact),
            Grant(Designers, PermissionTarget.Form, Design, other),
            Grant(Viewers, PermissionTarget.Global, View));
        var user = User(HrTeam, Designers, Viewers);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FormVisibilityOf(user, View).GroupKeys, Is.EqualTo(new[] { HrTeam }), "the global grant is no form grant, and design is not viewEntries");
            Assert.That(snapshot.FormVisibilityOf(user, View | Design).GroupKeys, Is.EquivalentTo(new[] { HrTeam, Designers }));
            Assert.That(snapshot.FormVisibilityOf(User(PayrollTeam), View).IsNothing, Is.True, "a group without grants");
        });
    }

    [Test]
    public void TheScope_IsOfFolderLevelPermissions()
    {
        var snapshot = Snapshot(Grant(Viewers, PermissionTarget.Global, View | FormBuilderPermissions.PrivacyTools));

        Assert.Multiple(() =>
        {
            Assert.That(() => snapshot.FormVisibilityOf(User(Viewers), FormBuilderPermissions.PrivacyTools), Throws.ArgumentException);
            Assert.That(() => snapshot.FormVisibilityOf(User(Viewers), FormBuilderPermissions.None), Throws.ArgumentException);
            Assert.That(snapshot.FormVisibilityOf(User(Viewers), View | FormBuilderPermissions.PrivacyTools).AnyOf, Is.EqualTo(View));
        });
    }

    [Test]
    public void TheFolderList_HoldsTheFoldersOnTheWayToWhatTheUserMaySee()
    {
        var snapshot = Snapshot(Grant(PayrollTeam, PermissionTarget.Folder, View, Payroll.Key), Grant(HrTeam, PermissionTarget.Form, View, Contact));
        var payroll = snapshot.FormVisibilityOf(User(PayrollTeam), PermissionRules.FolderLevel);
        var hr = snapshot.FormVisibilityOf(User(HrTeam), PermissionRules.FolderLevel);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.FolderVisibilityOf(payroll, []).FolderKeys, Is.EquivalentTo(new[] { Payroll.Key, Hr.Key, Company.Key }));
            Assert.That(snapshot.FolderVisibilityOf(payroll, []).Allows(Events.Key), Is.False);
            Assert.That(snapshot.FolderVisibilityOf(payroll, []).Allows(Secret.Key), Is.False, "restricted below what the user holds");
            Assert.That(snapshot.FolderVisibilityOf(hr, [Events.Id]).FolderKeys, Is.EquivalentTo(new[] { Events.Key, Company.Key }), "the folders of a form granted on its own");
            Assert.That(snapshot.FolderVisibilityOf(hr, [99]).FolderKeys, Is.Empty, "a folder the snapshot does not know");
            Assert.That(snapshot.FolderVisibilityOf(FormVisibility.Nothing, []).FolderKeys, Is.Empty);
        });
    }

    [Test]
    public void AMove_ListsTheGroupsThatWouldSeeMore_AndWhatEachGains()
    {
        var snapshot = Snapshot(
            Grant(Viewers, PermissionTarget.Global, View),
            Grant(Designers, PermissionTarget.Folder, Design | FormBuilderPermissions.ManageActions, Company.Key),
            Grant(HrTeam, PermissionTarget.Folder, View | Export, Hr.Key));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.MoveGains(FormMove(null, Events.Key)), Is.EqualTo(new[] { new AccessGain(Designers, Design | FormBuilderPermissions.ManageActions) }), "the viewers saw it at the root already");
            Assert.That(
                snapshot.MoveGains(FormMove(Hr.Key, Events.Key)),
                Is.EquivalentTo(new[] { new AccessGain(Viewers, View), new AccessGain(Designers, Design | FormBuilderPermissions.ManageActions) }),
                "out of a restricted folder; the HR team only loses");
            Assert.That(snapshot.MoveGains(FormMove(Events.Key, Hr.Key)), Is.EqualTo(new[] { new AccessGain(HrTeam, View | Export) }));
            Assert.That(snapshot.MoveGains(FormMove(Events.Key, Events.Key)), Is.Empty);
            Assert.That(snapshot.MoveGains(FormMove(Hr.Key, Events.Key) with { IsRestricted = true }), Is.Empty, "a restricted form keeps who sees it");
        });
    }

    [Test]
    public void AMove_LeavesOutWhatTheItemsOwnGrantsGive_AndUmbracosAdministrators()
    {
        var snapshot = Snapshot(
            Grant(Designers, PermissionTarget.Folder, Design | View, Company.Key),
            Grant(Designers, PermissionTarget.Form, Design, Contact),
            Grant(Constants.Security.AdminGroupKey, PermissionTarget.Folder, Export | View, Company.Key),
            Grant(HrTeam, PermissionTarget.Folder, View, Hr.Key),
            Grant(HrTeam, PermissionTarget.Folder, View, Events.Key));

        Assert.Multiple(() =>
        {
            Assert.That(
                snapshot.MoveGains(new AccessMove(PermissionTarget.Form, Contact, false, null, Events.Key)),
                Is.EqualTo(new[] { new AccessGain(Designers, View), new AccessGain(HrTeam, View) }),
                "the designers held design on the form already, and the administrators hold everything");
            Assert.That(
                snapshot.MoveGains(new AccessMove(PermissionTarget.Folder, Events.Key, false, Company.Key, Hr.Key)),
                Is.Empty,
                "into the restricted folder only the HR team sees it, which it does through Events' own grant already");
            Assert.That(
                snapshot.MoveGains(new AccessMove(PermissionTarget.Folder, Payroll.Key, false, Hr.Key, Company.Key)),
                Is.EqualTo(new[] { new AccessGain(Designers, Design | View) }),
                "a folder takes what it holds with it");
        });
    }

    private static Guid Key(int number) => Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-{number:D12}"));

    private static StoredGrant Grant(Guid group, PermissionTarget target, FormBuilderPermissions permissions, Guid? key = null) =>
        new(0, group, target, key ?? Guid.Empty, (int)permissions);

    private static AccessSnapshot Snapshot(params StoredGrant[] grants) => AccessSnapshot.Create(grants, Tree);

    private static AccessSnapshot Snapshot(AccessFolder[] folders, params StoredGrant[] grants) => AccessSnapshot.Create(grants, folders);

    private static AccessUser User(params Guid[] groups) => User(true, groups);

    private static AccessUser User(bool hasSection, params Guid[] groups) => new(Guid.NewGuid(), groups.ToHashSet(), false, hasSection);

    private static AccessUser Administrator() => new(Guid.NewGuid(), new HashSet<Guid> { Constants.Security.AdminGroupKey }, true, true);

    private static FormAccessTarget InFolder(AccessFolder folder) => new(Contact, folder.Key, false);

    private static FormAccessTarget AtTheRoot() => new(Contact, null, false);

    private static AccessMove FormMove(Guid? from, Guid? to) => new(PermissionTarget.Form, Key(299), false, from, to);
}
