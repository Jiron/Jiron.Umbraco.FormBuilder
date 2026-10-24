using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// The access service on a real site (ADR-0048): grants and restrictions change with an audit entry and refuse what the
/// model does not allow, a user group Umbraco deletes takes its grants, and the cached evaluation follows every change of
/// a grant, a restriction, a folder, a form deleted for good and a user group, dropped here at once and on the other
/// servers through the refresher. Umbraco's test host runs without caches, so this fixture gives it a runtime cache, as a
/// site has.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class AccessServiceTests : RepositoryTestBase
{
    private Personas _personas = null!;

    private IFormBuilderAccessService Access => GetRequiredService<IFormBuilderAccessService>();

    private IFormBuilderFolderService FolderService => GetRequiredService<IFormBuilderFolderService>();

    private IFormBuilderFormService FormService => GetRequiredService<IFormBuilderFormService>();

    private HookedPermissionRepository Rows => GetRequiredService<HookedPermissionRepository>();

    private RecordingServerMessenger Messenger => GetRequiredService<RecordingServerMessenger>();

    private ICacheRefresher Refresher => GetRequiredService<CacheRefresherCollection>()[FormBuilderAccessCacheRefresher.UniqueId]!;

    /// <summary>The group of the persona that holds nothing globally, which the tests grant to.</summary>
    private Guid Nobody => _personas.GroupKeys["noAccess"];

    [SetUp]
    public async Task CreateThePersonasAsync() => _personas = await ActivatorUtilities.CreateInstance<AccessPersonas>(Services).CreateAsync();

    [Test]
    public async Task AFolderGrant_IsMade_Changed_AndRemoved_EachWithAnAuditEntry()
    {
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));

        var made = await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries));
        var changed = await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.ViewEntries, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export));
        var stored = await Permissions.GetAsync(Nobody, PermissionTarget.Folder, events.Key);
        var removed = await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export, FormBuilderPermissions.None));

        var entries = await GrantEntriesAsync(events.Key);
        Assert.Multiple(async () =>
        {
            Assert.That(made, Is.EqualTo(new AccessGrant(Nobody, PermissionTarget.Folder, events.Key, FormBuilderPermissions.ViewEntries)));
            Assert.That(changed, Is.EqualTo(new AccessGrant(Nobody, PermissionTarget.Folder, events.Key, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export)));
            Assert.That(stored!.Permissions, Is.EqualTo(68), "the flags as stored");
            Assert.That(removed, Is.EqualTo(new AccessGrant(Nobody, PermissionTarget.Folder, events.Key, FormBuilderPermissions.None)));
            Assert.That(await Permissions.GetAsync(Nobody, PermissionTarget.Folder, events.Key), Is.Null, "no grant leaves no row");
            Assert.That(entries.Select(entry => (entry.EntityType, entry.UserKey)), Is.All.EqualTo(("folder", (Guid?)Editor)));
            Assert.That(entries.Select(entry => entry.Details), Is.EqualTo(new[]
            {
                $$"""{"target":"folder","group":"{{Nobody}}","before":[],"after":["viewEntries"]}""",
                $$"""{"target":"folder","group":"{{Nobody}}","before":["viewEntries"],"after":["viewEntries","export"]}""",
                $$"""{"target":"folder","group":"{{Nobody}}","before":["viewEntries","export"],"after":[]}""",
            }));
        });
    }

    [Test]
    public async Task AGlobalGrant_AndAFormGrant_AreAuditedOnWhatTheyGrant()
    {
        var contact = await FormService.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));

        await Access.ChangeGrantAsync(Change(PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ManageLibrary));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Form, contact.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions));

        var entries = (await Audit.QueryAsync(new AuditQuery { Action = FormBuilderAccessService.GrantChangedAction, UserKey = Editor }, PageRequest.First(10))).Items.OrderBy(entry => entry.Id);
        Assert.That(entries.Select(entry => (entry.EntityType, entry.EntityKey, entry.Details)), Is.EqualTo(new[]
        {
            ("access", (Guid?)null, $$"""{"target":"global","group":"{{Nobody}}","before":[],"after":["manageLibrary"]}"""),
            ("form", contact.Key, $$"""{"target":"form","group":"{{Nobody}}","before":[],"after":["design","manageActions"]}"""),
        }));
    }

    [Test]
    public async Task AGrantTheModelDoesNotAllow_IsRefused_NamingEveryProblem_AndWritesNothing()
    {
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        var grants = Count("jfbPermission");
        var entries = Count("jfbAuditEntry");

        Assert.Multiple(() =>
        {
            Assert.That(
                () => Access.ChangeGrantAsync(new AccessGrantChange(Guid.NewGuid(), PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.ManageSettings | FormBuilderPermissions.Export, Editor)),
                Throws.InstanceOf<FormBuilderSettingsValidationException>().With.Property(nameof(FormBuilderSettingsValidationException.Errors)).EqualTo(new[]
                {
                    new SettingsError("access.permissions", "globalOnly", "manageSettings can be granted globally only."),
                    new SettingsError("access.permissions", "needs", "'export' needs 'viewEntries'."),
                    new SettingsError("access.group", "unknownGroup", "No user group has this key."),
                }));
            Assert.That(
                () => Access.ChangeGrantAsync(Change(PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, (FormBuilderPermissions)4096)),
                Throws.InstanceOf<FormBuilderSettingsValidationException>().With.Property(nameof(FormBuilderSettingsValidationException.Errors)).EqualTo(new[] { new SettingsError("access.permissions", "unknown", "Holds a value that is no permission.") }));
            Assert.That(
                () => Access.ChangeGrantAsync(Change(PermissionTarget.Global, Guid.Empty, (FormBuilderPermissions)4096, FormBuilderPermissions.None)),
                Throws.InstanceOf<FormBuilderSettingsValidationException>(),
                "nor what the editor saw");
            Assert.That(
                () => Access.ChangeGrantAsync(Change(PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ViewSensitive | FormBuilderPermissions.ManageActions)),
                Throws.InstanceOf<FormBuilderSettingsValidationException>().With.Property(nameof(FormBuilderSettingsValidationException.Errors)).EqualTo(new[]
                {
                    new SettingsError("access.permissions", "needs", "'manageActions' needs 'design'."),
                    new SettingsError("access.permissions", "needs", "'viewSensitive' needs 'viewEntries'."),
                }));
            Assert.That(Count("jfbPermission"), Is.EqualTo(grants));
            Assert.That(Count("jfbAuditEntry"), Is.EqualTo(entries));
        });
    }

    [Test]
    public async Task AGrantThatChangedMeanwhile_IsRefused_AndOneThatStaysTheSame_IsChecked()
    {
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries));
        var entries = Count("jfbAuditEntry");

        Assert.Multiple(async () =>
        {
            Assert.That(
                () => Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design)),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "another editor granted first");
            Assert.That(
                () => Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.None)),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "nothing to change, but not what the editor saw");
            Assert.That(
                await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.ViewEntries, FormBuilderPermissions.ViewEntries)),
                Is.EqualTo(new AccessGrant(Nobody, PermissionTarget.Folder, events.Key, FormBuilderPermissions.ViewEntries)),
                "nothing to change");
            Assert.That(
                await Access.ChangeGrantAsync(Change(PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.None)),
                Is.EqualTo(new AccessGrant(Nobody, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None)),
                "no grant, as the editor saw");
            Assert.That((await Permissions.GetAsync(Nobody, PermissionTarget.Folder, events.Key))!.Permissions, Is.EqualTo(4));
            Assert.That(Count("jfbAuditEntry"), Is.EqualTo(entries), "a refusal or no change records nothing");
        });
    }

    [Test]
    public async Task AGrantOnAFolderOrFormThatIsGone_IsNull_AndWritesNothing()
    {
        var grants = Count("jfbPermission");
        var entries = Count("jfbAuditEntry");

        Assert.Multiple(async () =>
        {
            Assert.That(await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, Guid.NewGuid(), FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries)), Is.Null);
            Assert.That(await Access.ChangeGrantAsync(Change(PermissionTarget.Form, Guid.NewGuid(), FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries)), Is.Null);
            Assert.That(await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, Guid.NewGuid(), FormBuilderPermissions.ViewEntries, FormBuilderPermissions.None)), Is.Null, "nor a removal");
            Assert.That(await Permissions.ChangeAsync(new GrantChange(Nobody, PermissionTarget.Folder, Guid.NewGuid(), 0, 4)), Is.Null, "the statement grants nothing without its folder");
            Assert.That(await Permissions.ChangeAsync(new GrantChange(Nobody, PermissionTarget.Form, Guid.NewGuid(), 0, 4)), Is.Null, "nor without its form");
            Assert.That(Count("jfbPermission"), Is.EqualTo(grants));
            Assert.That(Count("jfbAuditEntry"), Is.EqualTo(entries));
        });
    }

    [Test]
    public async Task AGrantOnAFolderThatGoesAtTheSameMoment_IsNull_AndRecordsNothing()
    {
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        var folder = (await Folders.GetAsync(events.Key))!;
        await Folders.TrashAsync(folder.Id, Editor);
        var entries = Count("jfbAuditEntry");
        Rows.BeforeNextChange = async () => Assert.That(await Folders.DeleteAsync(folder.Id), Is.True, "another editor deletes it for good");

        var grant = await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries));

        Assert.Multiple(() =>
        {
            Assert.That(grant, Is.Null);
            Assert.That(Count("jfbPermission", $"{Q("targetKey")} = @0", events.Key), Is.Zero);
            Assert.That(Count("jfbAuditEntry"), Is.EqualTo(entries));
        });
    }

    [Test]
    public async Task AGrantOfAGroupUmbracoNoLongerHas_CanStillBeRemoved()
    {
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        var gone = Guid.NewGuid();
        await Permissions.ChangeAsync(new GrantChange(gone, PermissionTarget.Folder, events.Key, 0, 1));

        var removed = await Access.ChangeGrantAsync(new AccessGrantChange(gone, PermissionTarget.Folder, events.Key, FormBuilderPermissions.Design, FormBuilderPermissions.None, Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(removed!.Permissions, Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(await Permissions.GetAsync(gone, PermissionTarget.Folder, events.Key), Is.Null);
        });
    }

    [Test]
    public async Task AFolder_IsRestricted_AndFreed_EachOnceWithAnAuditEntry()
    {
        var hr = await FolderService.CreateAsync(new NewFolderRequest("HR", UserKey: Editor));

        var restricted = await Access.RestrictFolderAsync(hr.Key, true, Editor);
        var again = await Access.RestrictFolderAsync(hr.Key, true, Editor);
        var stored = (await Folders.GetAsync(hr.Key))!;
        var freed = await Access.RestrictFolderAsync(hr.Key, false, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(restricted, Is.EqualTo(new AccessRestriction(hr.Key, true, null)));
            Assert.That(again, Is.EqualTo(restricted), "restricted already: nothing to do");
            Assert.That((stored.IsRestricted, stored.Name, stored.ParentId), Is.EqualTo((true, "HR", (int?)null)), "only the flag changed");
            Assert.That(freed, Is.EqualTo(new AccessRestriction(hr.Key, false, null)));
            Assert.That((await Folders.GetAsync(hr.Key))!.IsRestricted, Is.False);
            Assert.That(await ActionsOnAsync(hr.Key), Is.EqualTo(new[] { "folder.created", "access.restricted", "access.unrestricted" }));
            Assert.That((await Audit.QueryAsync(new AuditQuery { EntityKey = hr.Key, Action = "access.restricted" }, PageRequest.First(1))).Items.Single().EntityType, Is.EqualTo("folder"));
            Assert.That(await Access.RestrictFolderAsync(Guid.NewGuid(), true, Editor), Is.Null, "a folder that does not exist");
        });
    }

    [Test]
    public async Task AForm_IsRestricted_AtTheVersionItWasRead_WithAnAuditEntry()
    {
        var form = await FormService.CreateAsync(new NewFormRequest("Payroll", UserKey: Editor));

        var restricted = (await Access.RestrictFormAsync(form.Key, form.ConcurrencyVersion, true, Editor))!;
        var again = await Access.RestrictFormAsync(form.Key, restricted.ConcurrencyVersion!.Value, true, Editor);
        var read = (await FormService.GetAsync(form.Key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(restricted, Is.EqualTo(new AccessRestriction(form.Key, true, form.ConcurrencyVersion + 1)));
            Assert.That(again, Is.EqualTo(restricted), "restricted already: nothing to do");
            Assert.That((read.IsRestricted, read.ConcurrencyVersion, read.Name, read.Alias, read.FolderKey), Is.EqualTo((true, form.ConcurrencyVersion + 1, form.Name, form.Alias, form.FolderKey)), "only the flag changed");
            Assert.That(() => Access.RestrictFormAsync(form.Key, form.ConcurrencyVersion, false, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>(), "the version it was read at is gone");
            Assert.That(() => Access.RestrictFormAsync(form.Key, form.ConcurrencyVersion, true, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>(), "even with nothing to do");
            Assert.That(await Access.RestrictFormAsync(Guid.NewGuid(), 1, true, Editor), Is.Null, "a form that does not exist");
            Assert.That(await Access.RestrictFormAsync(form.Key, read.ConcurrencyVersion, false, Editor), Is.EqualTo(new AccessRestriction(form.Key, false, form.ConcurrencyVersion + 2)));
            Assert.That(await ActionsOnAsync(form.Key), Is.EqualTo(new[] { "form.created", "access.restricted", "access.unrestricted" }));
        });
    }

    [Test]
    public async Task AFolderOrFormInTheTrash_IsNotRestricted_UntilItIsRestored()
    {
        var hr = await FolderService.CreateAsync(new NewFolderRequest("HR", UserKey: Editor));
        await FolderService.TrashAsync(hr.Key, Editor);
        var form = await FormService.CreateAsync(new NewFormRequest("Payroll", UserKey: Editor));
        var trashed = (await FormService.TrashAsync(form.Key, form.ConcurrencyVersion, Editor))!;
        var secret = await FolderService.CreateAsync(new NewFolderRequest("Secret", UserKey: Editor));
        await Access.RestrictFolderAsync(secret.Key, true, Editor);
        await FolderService.TrashAsync(secret.Key, Editor);
        var memo = await FormService.CreateAsync(new NewFormRequest("Memo", UserKey: Editor));
        var restricted = (await Access.RestrictFormAsync(memo.Key, memo.ConcurrencyVersion, true, Editor))!;
        var memoTrashed = (await FormService.TrashAsync(memo.Key, restricted.ConcurrencyVersion!.Value, Editor))!;
        var entries = Count("jfbAuditEntry");

        Assert.Multiple(async () =>
        {
            Assert.That(
                () => Access.RestrictFolderAsync(hr.Key, true, Editor),
                Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.Trashed));
            Assert.That(
                () => Access.RestrictFormAsync(form.Key, trashed.ConcurrencyVersion, true, Editor),
                Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.Trashed));
            Assert.That(
                () => Access.RestrictFolderAsync(secret.Key, true, Editor),
                Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.Trashed),
                "even when nothing would change");
            Assert.That(
                () => Access.RestrictFormAsync(memo.Key, memoTrashed.ConcurrencyVersion, true, Editor),
                Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.Trashed),
                "even when nothing would change");
            Assert.That((await Folders.GetAsync(hr.Key))!.IsRestricted, Is.False);
            Assert.That((await FormService.GetAsync(form.Key))!.IsRestricted, Is.False);
            Assert.That(Count("jfbAuditEntry"), Is.EqualTo(entries));
        });
    }

    [Test]
    public void AChangeOfAccess_InsideAnotherUnitOfWork_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => Database.WriteAsync(async _ => await Access.ChangeGrantAsync(Change(PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries))),
                Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"));
            Assert.That(
                () => Database.WriteAsync(async _ => await Access.RestrictFolderAsync(Guid.NewGuid(), true, Editor)),
                Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"));
            Assert.That(
                () => Database.WriteAsync(async _ => await Access.RestrictFormAsync(Guid.NewGuid(), 1, true, Editor)),
                Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"));
            Assert.That(Count("jfbPermission", $"{Q("userGroupKey")} = @0", Nobody), Is.Zero);
        });
    }

    [Test]
    public async Task AUserGroupUmbracoDeletes_TakesItsGrants_WithAnAuditEntry_AndTheEvaluationFollows()
    {
        var people = GetRequiredService<TestPeople>();
        var userGroups = GetRequiredService<IUserGroupService>();
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        var temps = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbTemps", Name = "Temps", Sections = { AccessPersonas.Section } });
        var empty = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbEmpty", Name = "Empty", Sections = { AccessPersonas.Section } });
        await Access.ChangeGrantAsync(new AccessGrantChange(temps.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, Editor));
        await Access.ChangeGrantAsync(new AccessGrantChange(temps.Key, PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design, Editor));
        var temp = new AccessUser(Guid.NewGuid(), new HashSet<Guid> { temps.Key }, false, true);
        var before = await Access.GetForFolderAsync(temp, events.Key);
        var others = Count("jfbPermission", $"{Q("userGroupKey")} <> @0", temps.Key);

        var deleted = await userGroups.DeleteAsync(temps.Key);
        var deletedEmpty = await userGroups.DeleteAsync(empty.Key);

        var removals = (await Audit.QueryAsync(new AuditQuery { Action = FormBuilderAccessService.GroupRemovedAction }, PageRequest.First(10))).Items;
        Assert.Multiple(async () =>
        {
            Assert.That((deleted.Success, deletedEmpty.Success), Is.EqualTo((true, true)));
            Assert.That(before, Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Design));
            Assert.That(await Permissions.ListForGroupsAsync([temps.Key]), Is.Empty);
            Assert.That(Count("jfbPermission"), Is.EqualTo(others), "the other groups keep theirs");
            Assert.That(await Access.GetForFolderAsync(temp, events.Key), Is.EqualTo(FormBuilderPermissions.None), "the cached evaluation follows");
            Assert.That(removals.Select(entry => (entry.EntityType, entry.EntityKey, entry.UserKey, entry.Details)), Is.EqualTo(new[] { ("userGroup", (Guid?)temps.Key, (Guid?)null, """{"grants":2}""") }), "a group without grants leaves no entry");
            Assert.That(await Access.RemoveGroupAsync(temps.Key), Is.Zero, "removing again finds nothing");
        });
    }

    [Test]
    public async Task TheEvaluation_IsCachedOnEachServer_UntilTheRefresherDropsIt()
    {
        var nobody = _personas["noAccess"];
        var before = await Access.GetGlobalAsync(nobody);

        // Another server grants: the row is here at once, its instruction comes later.
        await Permissions.ChangeAsync(new GrantChange(Nobody, PermissionTarget.Global, Guid.Empty, 0, (int)FormBuilderPermissions.ViewEntries));
        var cached = await Access.GetGlobalAsync(nobody);
        Refresher.RefreshAll();
        var refreshed = await Access.GetGlobalAsync(nobody);

        Assert.Multiple(() =>
        {
            Assert.That((before, cached), Is.EqualTo((FormBuilderPermissions.None, FormBuilderPermissions.None)), "the snapshot answers until the instruction");
            Assert.That(refreshed, Is.EqualTo(FormBuilderPermissions.ViewEntries));
            Assert.That(Refresher, Is.InstanceOf<FormBuilderAccessCacheRefresher>());
            Assert.That(Refresher.Name, Is.EqualTo("Jiron Form Builder access"));
            Assert.That(GetRequiredService<AppCaches>().RuntimeCache, Is.InstanceOf<ObjectCacheAppCache>(), "the tests of the cache use a real one");
        });
    }

    [Test]
    public async Task ASnapshotThatRacedAChange_AnswersTheCall_ButIsNotKept()
    {
        var nobody = _personas["noAccess"];
        var runtime = GetRequiredService<AppCaches>().RuntimeCache;
        FormBuilderAccessCacheRefresher.Forget(runtime);
        Rows.AfterNextList = async () =>
        {
            // Another server grants after this read took the rows, and its instruction reaches this server.
            await Permissions.ChangeAsync(new GrantChange(Nobody, PermissionTarget.Global, Guid.Empty, 0, (int)FormBuilderPermissions.ViewEntries));
            FormBuilderAccessCacheRefresher.Forget(runtime);
        };

        var raced = await Access.GetGlobalAsync(nobody);
        var next = await Access.GetGlobalAsync(nobody);

        Assert.That((raced, next), Is.EqualTo((FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries)));
    }

    [Test]
    public async Task TheCachedEvaluation_FollowsGrants_AndRestrictions_AndTellsTheOtherServers()
    {
        var hr = await FolderService.CreateAsync(new NewFolderRequest("HR", UserKey: Editor));
        var payroll = await FormService.CreateAsync(new NewFormRequest("Payroll", FolderKey: hr.Key, UserKey: Editor));
        var viewer = _personas["viewer"];
        var viewers = _personas.GroupKeys["viewer"];

        var open = await Access.GetForFormAsync(viewer, FormAccessTarget.Of(payroll));
        var restricted = await AfterAsync(() => Access.RestrictFolderAsync(hr.Key, true, Editor), () => Access.GetForFormAsync(viewer, FormAccessTarget.Of(payroll)));
        var freed = await AfterAsync(() => Access.RestrictFolderAsync(hr.Key, false, Editor), () => Access.GetForFormAsync(viewer, FormAccessTarget.Of(payroll)));
        var granted = await AfterAsync(
            () => Access.ChangeGrantAsync(new AccessGrantChange(viewers, PermissionTarget.Folder, hr.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export, Editor)),
            () => Access.GetFormScopeAsync(viewer, FormBuilderPermissions.Export));
        var formRestricted = await AfterAsync(
            () => Access.RestrictFormAsync(payroll.Key, payroll.ConcurrencyVersion, true, Editor),
            async () => await Access.GetForFormAsync(viewer, FormAccessTarget.Of((await FormService.GetAsync(payroll.Key))!)));

        Assert.Multiple(async () =>
        {
            Assert.That(open, Is.EqualTo(FormBuilderPermissions.ViewEntries), "the global grant reaches the form");
            Assert.That(restricted, Is.EqualTo((FormBuilderPermissions.None, 1)), "a restricted folder stops it");
            Assert.That(freed, Is.EqualTo((FormBuilderPermissions.ViewEntries, 1)), "and lets it through again");
            Assert.That(granted.Value.FolderIds, Is.EqualTo(new[] { (await Folders.GetAsync(hr.Key))!.Id }), "the folder's own grant holds export");
            Assert.That(granted.Instructions, Is.EqualTo(1));
            Assert.That(formRestricted, Is.EqualTo((FormBuilderPermissions.None, 1)), "a restricted form holds only its own grants");
        });
    }

    [Test]
    public async Task TheCachedEvaluation_FollowsFoldersCreated_AndMoved()
    {
        var company = await FolderService.CreateAsync(new NewFolderRequest("Company", UserKey: Editor));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, company.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design));
        var loose = await FolderService.CreateAsync(new NewFolderRequest("Loose", UserKey: Editor));
        var nobody = _personas["noAccess"];
        var outside = await Access.GetForFolderAsync(nobody, loose.Key);

        var sent = Instructions();
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", company.Key, Editor));
        var created = (await Access.GetForFolderAsync(nobody, events.Key), Instructions() - sent);
        var moved = await AfterAsync(() => FolderService.MoveAsync(loose.Key, company.Key, Editor), () => Access.GetForFolderAsync(nobody, loose.Key));
        var movedOut = await AfterAsync(() => FolderService.MoveAsync(events.Key, null, Editor), () => Access.GetForFolderAsync(nobody, events.Key));

        Assert.Multiple(() =>
        {
            Assert.That(outside, Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(created, Is.EqualTo((FormBuilderPermissions.Design, 1)), "a new folder inherits at once");
            Assert.That(moved, Is.EqualTo((FormBuilderPermissions.Design, 1)), "moved in");
            Assert.That(movedOut, Is.EqualTo((FormBuilderPermissions.None, 1)), "moved out");
        });
    }

    [Test]
    public async Task TheCachedEvaluation_FollowsARestore_ThatBringsAFolderToTheRoot()
    {
        var company = await FolderService.CreateAsync(new NewFolderRequest("Company", UserKey: Editor));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, company.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design));
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", company.Key, Editor));
        await FolderService.TrashAsync(events.Key, Editor);
        await FolderService.TrashAsync(company.Key, Editor);
        var nobody = _personas["noAccess"];
        var inTheTrash = await Access.GetForFolderAsync(nobody, events.Key);

        var restored = await AfterAsync(() => FolderService.RestoreAsync(events.Key, Editor), () => Access.GetForFolderAsync(nobody, events.Key));

        Assert.Multiple(async () =>
        {
            Assert.That(inTheTrash, Is.EqualTo(FormBuilderPermissions.Design), "in the trash, it stays below its folder");
            Assert.That((await FolderService.GetAsync(events.Key))!.ParentKey, Is.Null, "its folder is in the trash, so it came back to the root");
            Assert.That(restored, Is.EqualTo((FormBuilderPermissions.None, 1)));
        });
    }

    [Test]
    public async Task TheCachedEvaluation_FollowsAFolderAndAForm_DeletedForGood()
    {
        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Folder, events.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design));
        await FolderService.TrashAsync(events.Key, Editor);
        var contact = await FormService.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Form, contact.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries));
        var trashed = (await FormService.TrashAsync(contact.Key, contact.ConcurrencyVersion, Editor))!;
        var nobody = _personas["noAccess"];
        var target = FormAccessTarget.Of(trashed);
        var before = (await Access.GetForFolderAsync(nobody, events.Key), await Access.GetForFormAsync(nobody, target));

        var folderGone = await AfterAsync(() => FolderService.DeleteAsync(events.Key, Editor), () => Access.GetForFolderAsync(nobody, events.Key));
        var formGone = await AfterAsync(() => FormService.DeleteAsync(contact.Key, trashed.ConcurrencyVersion, Editor), () => Access.GetForFormAsync(nobody, target));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo((FormBuilderPermissions.Design, FormBuilderPermissions.ViewEntries)));
            Assert.That(folderGone, Is.EqualTo((FormBuilderPermissions.None, 1)), "its grants went with it");
            Assert.That(formGone, Is.EqualTo((FormBuilderPermissions.None, 1)));
            Assert.That(Count("jfbPermission", $"{Q("userGroupKey")} = @0", Nobody), Is.Zero);
        });
    }

    [Test]
    public async Task ADeleteThatStopsPartWay_StillDropsTheSnapshot_ForWhatWent()
    {
        var old = await FolderService.CreateAsync(new NewFolderRequest("Old", UserKey: Editor));
        var alpha = await FormService.CreateAsync(new NewFormRequest("Alpha", FolderKey: old.Key, UserKey: Editor));
        var zulu = await FormService.CreateAsync(new NewFormRequest("Zulu", FolderKey: old.Key, UserKey: Editor));
        await Access.ChangeGrantAsync(Change(PermissionTarget.Form, alpha.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries));
        await FolderService.TrashAsync(old.Key, Editor);
        var target = FormAccessTarget.Of((await FormService.GetAsync(alpha.Key))!);
        var nobody = _personas["noAccess"];
        var before = await Access.GetForFormAsync(nobody, target);

        // Zulu, which goes after Alpha, changes once the delete has read the forms.
        GetRequiredService<AccessHooks>().BeforeFolderDelete = () =>
        {
            Write(db => db.Execute($"UPDATE {T("jfbForm")} SET {Q("concurrencyVersion")} = {Q("concurrencyVersion")} + 1 WHERE {Q("key")} = @0", zulu.Key));
            return Task.CompletedTask;
        };

        Assert.That(() => FolderService.DeleteAsync(old.Key, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>());
        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(FormBuilderPermissions.ViewEntries));
            Assert.That(await FormService.GetAsync(alpha.Key), Is.Null, "Alpha went");
            Assert.That(await FormService.GetAsync(zulu.Key), Is.Not.Null, "Zulu stays");
            Assert.That(await Access.GetForFormAsync(nobody, target), Is.EqualTo(FormBuilderPermissions.None), "Alpha's grant went, and the snapshot with it");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<AccessHooks>();
        builder.AddNotificationAsyncHandler<FolderDeletingNotification, FolderDeletingHook>();
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
        builder.Services.AddSingleton(provider => new HookedPermissionRepository(new PermissionRepository(provider.GetRequiredService<FormBuilderDatabase>())));
        builder.Services.AddUnique<IPermissionRepository>(provider => provider.GetRequiredService<HookedPermissionRepository>());
        builder.Services.AddSingleton<RecordingServerMessenger>();
        builder.Services.AddUnique<IServerMessenger>(provider => provider.GetRequiredService<RecordingServerMessenger>());
    }

    private AccessGrantChange Change(PermissionTarget target, Guid key, FormBuilderPermissions expected, FormBuilderPermissions permissions) =>
        new(Nobody, target, key, expected, permissions, Editor);

    // What the evaluation answers after a change, which comes after a read that filled the cache, and how many
    // instructions the change sent to the other servers.
    private async Task<(T Value, int Instructions)> AfterAsync<TChange, T>(Func<Task<TChange>> change, Func<Task<T>> evaluate)
    {
        _ = await evaluate();
        var sent = Instructions();
        await change();
        var instructions = Instructions() - sent;
        return (await evaluate(), instructions);
    }

    private int Instructions() => Messenger.RefreshedAll.Count(id => id == FormBuilderAccessCacheRefresher.UniqueId);

    private async Task<IReadOnlyList<StoredAuditEntry>> GrantEntriesAsync(Guid key) =>
        [.. (await Audit.QueryAsync(new AuditQuery { Action = FormBuilderAccessService.GrantChangedAction, EntityKey = key }, PageRequest.First(10))).Items.OrderBy(entry => entry.Id)];

    private async Task<IReadOnlyList<string>> ActionsOnAsync(Guid key) =>
        [.. (await Audit.QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10))).Items.OrderBy(entry => entry.Id).Select(entry => entry.Action)];
}
