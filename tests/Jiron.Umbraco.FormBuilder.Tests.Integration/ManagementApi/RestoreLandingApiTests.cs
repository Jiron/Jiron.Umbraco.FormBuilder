using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// A restore lands where its endpoint checked it (ADR-0047, amended in 4.8c): the endpoint works out where the form or
/// folder lands, checks what a move there needs, and hands that landing to the write. A folder that comes back from the
/// trash between the check and the write makes the write refuse with <c>landing_changed</c>, so nothing lands where no
/// check looked. The folder comes back right after the action read it: <see cref="RestoreActionHook"/> arms
/// <see cref="HookedFolderRepository"/> once the access filter, which reads the folder too, is done.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RestoreLandingApiTests : ManagementApiTestBase
{
    private HookedFolderRepository Rows => GetRequiredService<HookedFolderRepository>();

    private RestoreActionHook Hook => GetRequiredService<RestoreActionHook>();

    private IFormBuilderFolderService Folders => GetRequiredService<IFormBuilderFolderService>();

    [Test]
    public async Task AFormWhoseFolderComesBackAfterTheCheck_IsRefused_AndStaysInTheTrash()
    {
        // The form went to the trash on its own, then its folder: the endpoint finds that it lands at the root.
        var events = await FolderAsync("Events");
        var offer = await FormAsync("Offer", events);
        await TrashFormAsync(offer);
        await TrashFolderAsync(events);
        var body = new { concurrencyVersion = (await GetRequiredService<IFormBuilderFormService>().GetAsync(offer))!.ConcurrencyVersion, widenAccess = true };
        Hook.AfterTheActionReadsAFolder = () => Folders.RestoreAsync(events, null);

        var restored = await CallAsync("admin", HttpMethod.Post, $"form/{offer}/restore", body);
        var form = await GetRequiredService<IFormBuilderFormService>().GetAsync(offer);

        Assert.Multiple(async () =>
        {
            Assert.That(Hook.AfterTheActionReadsAFolder is null && Rows.AfterNextGetById is null, Is.True, "the folder came back between the check and the write");
            Assert.That((await Folders.GetAsync(events))!.TrashedUtc, Is.Null);
            Assert.That(restored.Outcome, Is.EqualTo("409 landing_changed"), restored.ToString());
            Assert.That((form!.TrashedUtc is not null, form.FolderKey), Is.EqualTo((true, (Guid?)events)), "still in the trash, in its folder");
        });
    }

    [Test]
    public async Task AFolderWhoseParentComesBackAfterTheCheck_IsRefused_AndStaysInTheTrash()
    {
        // Archive went to the trash on its own, then its parent: the endpoint finds that it lands at the root.
        var events = await FolderAsync("Events");
        var archive = await FolderAsync("Archive", events);
        await TrashFolderAsync(archive);
        await TrashFolderAsync(events);
        Hook.AfterTheActionReadsAFolder = () => Folders.RestoreAsync(events, null);

        var restored = await CallAsync("admin", HttpMethod.Post, $"folder/{archive}/restore", new { widenAccess = true });
        var folder = await Folders.GetAsync(archive);

        Assert.Multiple(async () =>
        {
            Assert.That(Hook.AfterTheActionReadsAFolder is null && Rows.AfterNextGetById is null, Is.True, "the parent came back between the check and the write");
            Assert.That((await Folders.GetAsync(events))!.TrashedUtc, Is.Null);
            Assert.That(restored.Outcome, Is.EqualTo("409 landing_changed"), restored.ToString());
            Assert.That((folder!.TrashedUtc is not null, folder.ParentKey), Is.EqualTo((true, (Guid?)events)), "still in the trash, in its parent");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton(provider => new HookedFolderRepository(new FolderRepository(provider.GetRequiredService<FormBuilderDatabase>())));
        builder.Services.AddUnique<IFolderRepository>(provider => provider.GetRequiredService<HookedFolderRepository>());
        builder.Services.AddSingleton<RestoreActionHook>();
        builder.Services.Configure<MvcOptions>(options => options.Filters.AddService<RestoreActionHook>(int.MaxValue));
    }
}

/// <summary>
/// Arms <see cref="HookedFolderRepository.AfterNextGetById"/> as a restore's action starts, after every other filter, so
/// the access filter's reads go by and the action's own read of a folder runs the hook.
/// </summary>
internal sealed class RestoreActionHook(HookedFolderRepository rows) : IAsyncActionFilter
{
    /// <summary>Runs once, right after the next restore action reads a folder by its id.</summary>
    public Func<Task>? AfterTheActionReadsAFolder { get; set; }

    /// <inheritdoc />
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (AfterTheActionReadsAFolder is { } hook && context.HttpContext.Request.Path.Value?.EndsWith("/restore", StringComparison.Ordinal) == true)
        {
            AfterTheActionReadsAFolder = null;
            rows.AfterNextGetById = hook;
        }

        return next();
    }
}

/// <summary>The folder repository with a hook, so a test can change the tree right after a service read one folder.</summary>
internal sealed class HookedFolderRepository(IFolderRepository inner) : IFolderRepository
{
    /// <summary>Runs once, right after the next read of a folder by its id.</summary>
    public Func<Task>? AfterNextGetById { get; set; }

    /// <inheritdoc />
    public Task<StoredFolder> CreateAsync(NewFolder folder, CancellationToken cancellationToken = default) => inner.CreateAsync(folder, cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> GetAsync(Guid key, CancellationToken cancellationToken = default) => inner.GetAsync(key, cancellationToken);

    /// <inheritdoc />
    public async Task<StoredFolder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await inner.GetByIdAsync(id, cancellationToken);
        if (AfterNextGetById is { } hook)
        {
            AfterNextGetById = null;
            await hook();
        }

        return folder;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredFolder>> ListChildrenAsync(int? parentId, bool trashed, CancellationToken cancellationToken = default) =>
        inner.ListChildrenAsync(parentId, trashed, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredFolder>> ListAllAsync(CancellationToken cancellationToken = default) => inner.ListAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> RenameAsync(int id, string name, Guid? userKey, CancellationToken cancellationToken = default) =>
        inner.RenameAsync(id, name, userKey, cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> RestrictAsync(int id, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default) =>
        inner.RestrictAsync(id, isRestricted, userKey, cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> MoveAsync(int id, int? parentId, Guid? userKey, CancellationToken cancellationToken = default) =>
        inner.MoveAsync(id, parentId, userKey, cancellationToken);

    /// <inheritdoc />
    public Task<FolderTreeChange?> TrashAsync(int id, Guid? userKey, CancellationToken cancellationToken = default) => inner.TrashAsync(id, userKey, cancellationToken);

    /// <inheritdoc />
    public Task<FolderTreeChange?> RestoreAsync(int id, int? landingParentId, Guid? userKey, CancellationToken cancellationToken = default) =>
        inner.RestoreAsync(id, landingParentId, userKey, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
}
