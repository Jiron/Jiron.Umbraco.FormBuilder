using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>Changes a test makes while a service runs, at the moments the hooks name.</summary>
internal sealed class AccessHooks
{
    /// <summary>Runs once, while the folder service announces a delete, after it read what goes and before it deletes.</summary>
    public Func<Task>? BeforeFolderDelete { get; set; }
}

/// <summary>Runs <see cref="AccessHooks.BeforeFolderDelete"/> as an extension's handler of a folder's delete would.</summary>
internal sealed class FolderDeletingHook(AccessHooks hooks) : INotificationAsyncHandler<FolderDeletingNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(FolderDeletingNotification notification, CancellationToken cancellationToken)
    {
        if (hooks.BeforeFolderDelete is { } hook)
        {
            hooks.BeforeFolderDelete = null;
            await hook();
        }
    }
}

/// <summary>
/// The grants repository with hooks, so a test can let another server change a grant while this one reads the rows or
/// writes its change.
/// </summary>
internal sealed class HookedPermissionRepository(IPermissionRepository inner) : IPermissionRepository
{
    /// <summary>Runs once, right after the next read of every grant.</summary>
    public Func<Task>? AfterNextList { get; set; }

    /// <summary>Runs once, right before the next change of a grant, inside its unit of work.</summary>
    public Func<Task>? BeforeNextChange { get; set; }

    /// <inheritdoc />
    public Task<StoredGrant?> GetAsync(Guid userGroupKey, PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default) =>
        inner.GetAsync(userGroupKey, targetType, targetKey, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredGrant>> ListAsync(CancellationToken cancellationToken = default)
    {
        var grants = await inner.ListAsync(cancellationToken);
        await RunOnceAsync(AfterNextList, () => AfterNextList = null);
        return grants;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredGrant>> ListForGroupsAsync(IReadOnlyCollection<Guid> userGroupKeys, CancellationToken cancellationToken = default) =>
        inner.ListForGroupsAsync(userGroupKeys, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredGrant>> ListForTargetAsync(PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default) =>
        inner.ListForTargetAsync(targetType, targetKey, cancellationToken);

    /// <inheritdoc />
    public async Task<StoredGrant?> ChangeAsync(GrantChange change, CancellationToken cancellationToken = default)
    {
        await RunOnceAsync(BeforeNextChange, () => BeforeNextChange = null);
        return await inner.ChangeAsync(change, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<int>> ListFoldersOfGrantedFormsAsync(IReadOnlyCollection<Guid> userGroupKeys, int anyOf, CancellationToken cancellationToken = default) =>
        inner.ListFoldersOfGrantedFormsAsync(userGroupKeys, anyOf, cancellationToken);

    /// <inheritdoc />
    public Task<int> DeleteForTargetAsync(PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default) =>
        inner.DeleteForTargetAsync(targetType, targetKey, cancellationToken);

    /// <inheritdoc />
    public Task<int> DeleteForGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default) =>
        inner.DeleteForGroupAsync(userGroupKey, cancellationToken);

    private static async Task RunOnceAsync(Func<Task>? hook, Action clear)
    {
        if (hook is not null)
        {
            clear();
            await hook();
        }
    }
}
