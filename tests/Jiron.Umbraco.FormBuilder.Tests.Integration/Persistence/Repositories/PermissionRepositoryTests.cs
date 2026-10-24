using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The grants of user groups (ADR-0013, ADR-0037): one row per group and target, changed only from the permissions the
/// editor saw, and never stored as 0.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class PermissionRepositoryTests : RepositoryTestBase
{
    private static readonly Guid Designers = Guid.Parse("5c0e9f4a-1d2b-4c3e-8f7a-6b5d4c3b2a10");
    private static readonly Guid Handlers = Guid.Parse("5c0e9f4a-1d2b-4c3e-8f7a-6b5d4c3b2a11");
    private static readonly Guid Hr = Guid.Parse("8e4a1f20-7b3c-4d5e-9f60-1a2b3c4d5e6f");
    private static readonly Guid Contact = Guid.Parse("8e4a1f20-7b3c-4d5e-9f60-1a2b3c4d5e70");

    [Test]
    public async Task AGrant_IsMade_Changed_AndRemoved()
    {
        var made = await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, Hr, 0, 5));
        var changed = await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, Hr, 5, 7));
        var same = await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, Hr, 7, 7));

        Assert.Multiple(async () =>
        {
            Assert.That(made, Is.EqualTo(new StoredGrant(made!.Id, Designers, PermissionTarget.Folder, Hr, 5)));
            Assert.That(changed, Is.EqualTo(made with { Permissions = 7 }));
            Assert.That(same, Is.EqualTo(changed), "a change to what it holds already");
            Assert.That(await Permissions.GetAsync(Designers, PermissionTarget.Folder, Hr), Is.EqualTo(changed));
            Assert.That(await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, Hr, 7, 0)), Is.Null, "removed");
            Assert.That(await Permissions.GetAsync(Designers, PermissionTarget.Folder, Hr), Is.Null);
            Assert.That(Count(FormBuilderTables.Permission), Is.Zero, "no row holds 0");
            Assert.That(await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Folder, Hr, 0, 0)), Is.Null, "nothing to nothing");
        });
    }

    [Test]
    public async Task AChange_FromWhatTheGrantNoLongerHolds_IsRefused_AndChangesNothing()
    {
        await Permissions.ChangeAsync(new GrantChange(Designers, PermissionTarget.Global, Guid.Empty, 0, 3));
        var before = await Permissions.ListAsync();

        Assert.Multiple(async () =>
        {
            AssertStale(new GrantChange(Designers, PermissionTarget.Global, Guid.Empty, 0, 1), "granting what someone granted meanwhile");
            AssertStale(new GrantChange(Designers, PermissionTarget.Global, Guid.Empty, 1, 9), "changing from what it no longer holds");
            AssertStale(new GrantChange(Designers, PermissionTarget.Global, Guid.Empty, 1, 0), "removing from what it no longer holds");
            AssertStale(new GrantChange(Designers, PermissionTarget.Global, Guid.Empty, 0, 0), "nothing to nothing, while a grant exists");
            AssertStale(new GrantChange(Handlers, PermissionTarget.Global, Guid.Empty, 3, 1), "changing a grant that is gone");
            AssertStale(new GrantChange(Handlers, PermissionTarget.Global, Guid.Empty, 3, 0), "removing a grant that is gone");
            Assert.That(await Permissions.ListAsync(), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task ParallelChanges_FromTheSamePermissions_LetExactlyOneThrough()
    {
        var grants = await Task.WhenAll(Enumerable.Range(1, 10).Select(i => Task.Run(() => Succeeds(new GrantChange(Handlers, PermissionTarget.Form, Contact, 0, i)))));
        var winner = (await Permissions.GetAsync(Handlers, PermissionTarget.Form, Contact))!;
        var changes = await Task.WhenAll(Enumerable.Range(20, 10).Select(i => Task.Run(() => Succeeds(new GrantChange(Handlers, PermissionTarget.Form, Contact, winner.Permissions, i)))));

        Assert.Multiple(async () =>
        {
            Assert.That(grants.Count(made => made), Is.EqualTo(1), "a grant is made once");
            Assert.That(changes.Count(made => made), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.Permission), Is.EqualTo(1));
            Assert.That((await Permissions.GetAsync(Handlers, PermissionTarget.Form, Contact))?.Permissions, Is.InRange(20, 29));
        });
    }

    [Test]
    public async Task Lists_ReadAGroupsGrants_AndATargetsGrants_InTheOrderTheyWereMade()
    {
        var global = await Grant(Designers, PermissionTarget.Global, Guid.Empty, 1);
        var folder = await Grant(Handlers, PermissionTarget.Folder, Hr, 2);
        var form = await Grant(Designers, PermissionTarget.Form, Contact, 4);
        var other = await Grant(Guid.NewGuid(), PermissionTarget.Folder, Hr, 8);

        Assert.Multiple(async () =>
        {
            Assert.That(await Permissions.ListAsync(), Is.EqualTo(new[] { global, folder, form, other }));
            Assert.That(await Permissions.ListForGroupsAsync([Designers, Handlers, Designers]), Is.EqualTo(new[] { global, folder, form }));
            Assert.That(await Permissions.ListForGroupsAsync([Guid.NewGuid()]), Is.Empty);
            Assert.That(await Permissions.ListForGroupsAsync([]), Is.Empty);
            Assert.That(await Permissions.ListForTargetAsync(PermissionTarget.Folder, Hr), Is.EqualTo(new[] { folder, other }));
            Assert.That(await Permissions.ListForTargetAsync(PermissionTarget.Form, Hr), Is.Empty, "the target type counts");
            Assert.That(await Permissions.ListForTargetAsync(PermissionTarget.Global, Guid.Empty), Is.EqualTo(new[] { global }));
        });
    }

    [Test]
    public async Task ManyGroups_AreReadInStatementsOfAtMost1000Keys()
    {
        var first = await Grant(Designers, PermissionTarget.Global, Guid.Empty, 1);
        var last = await Grant(Handlers, PermissionTarget.Global, Guid.Empty, 2);
        var keys = new List<Guid> { Handlers };
        keys.AddRange(Enumerable.Range(0, (2 * PermissionRepository.GroupsPerStatement) + 1).Select(_ => Guid.NewGuid()));
        keys.Add(Designers);

        // SQL Server takes at most 2,100 parameters per statement: the read names its keys in statements of at most 1,000.
        var statements = new List<int>();
        var grants = await Database.ReadAsync(async db =>
        {
            var counter = new ParameterCounter(statements);
            db.Interceptors.Add(counter);
            try
            {
                return await Permissions.ListForGroupsAsync(keys);
            }
            finally
            {
                db.Interceptors.Remove(counter);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(grants, Is.EqualTo(new[] { first, last }), "in the order they were made, whichever statement read them");
            Assert.That(statements, Is.EqualTo(new[] { 1000, 1000, 3 }), "the parameters of each statement");
        });
    }

    [Test]
    public async Task ATargetsOrAGroupsGrants_AreDeletedTogether()
    {
        await Grant(Designers, PermissionTarget.Folder, Hr, 1);
        await Grant(Handlers, PermissionTarget.Folder, Hr, 2);
        var form = await Grant(Designers, PermissionTarget.Form, Contact, 4);
        var kept = await Grant(Handlers, PermissionTarget.Global, Guid.Empty, 8);

        Assert.Multiple(async () =>
        {
            Assert.That(await Permissions.DeleteForTargetAsync(PermissionTarget.Folder, Hr), Is.EqualTo(2));
            Assert.That(await Permissions.DeleteForTargetAsync(PermissionTarget.Folder, Hr), Is.Zero);
            Assert.That(await Permissions.ListAsync(), Is.EqualTo(new[] { form, kept }));
            Assert.That(await Permissions.DeleteForGroupAsync(Designers), Is.EqualTo(1));
            Assert.That(await Permissions.ListAsync(), Is.EqualTo(new[] { kept }));
        });
    }

    private async Task<StoredGrant> Grant(Guid group, PermissionTarget type, Guid key, int permissions) =>
        (await Permissions.ChangeAsync(new GrantChange(group, type, key, 0, permissions)))!;

    private async Task<bool> Succeeds(GrantChange change)
    {
        try
        {
            await Permissions.ChangeAsync(change);
            return true;
        }
        catch (FormBuilderConcurrencyException)
        {
            return false;
        }
    }

    // Refused by the statement's own condition: a refusal the unique index made carries the database's error inside.
    private void AssertStale(GrantChange change, string why) =>
        Assert.That(
            async () => await Permissions.ChangeAsync(change),
            Throws.InstanceOf<FormBuilderConcurrencyException>().With.Property(nameof(Exception.InnerException)).Null,
            why);
}
