using System.Collections.Immutable;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;

/// <summary>
/// Settings, Access (ADR-0013, ADR-0048, ADR-0218): every user group's global grant, as a matrix of groups and
/// permissions, with the folders and forms that have access of their own; and a folder's or form's grants and
/// restriction, as its Permissions sidebar shows them. A save changes the groups it sends in one unit of work, each
/// from what the editor saw, so a grant changed meanwhile refuses all of it (<c>409</c>). Every endpoint needs
/// <c>managePermissions</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/access")]
[ApiExplorerSettings(GroupName = "Access")]
internal sealed class AccessController(
    IFormBuilderAccessService access,
    IUserGroupService userGroups,
    IFolderRepository folders,
    IFormRepository forms) : FormBuilderManagementApiController
{
    /// <summary>The error of a change of the administrators' grant, who hold every permission everywhere.</summary>
    public const string AdministratorsCode = "administrators";

    // A page of the forms a list reads, until the last.
    private const int Page = 200;

    // The keys of forms one read names at most.
    private const int KeysAtOnce = 500;

    private const string FolderType = "folder";
    private const string FormType = "form";

    // The permissions in Access's three parts, in the order of ADR-0013's table.
    private static readonly ImmutableArray<(string Alias, FormBuilderPermissions Permissions)> Parts =
    [
        ("forms", FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions),
        ("entries", FormBuilderPermissions.ViewEntries | FormBuilderPermissions.ViewSensitive | FormBuilderPermissions.EditEntries | FormBuilderPermissions.DeleteEntries | FormBuilderPermissions.Export),
        ("library", PermissionRules.GlobalOnly),
    ];

    /// <summary>Every user group's global grant, the permissions in their parts, the host's lock, and the folders and forms with access of their own.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManagePermissions)]
    [ProducesResponseType(typeof(AccessResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) => Ok(await OverviewAsync(cancellationToken));

    /// <summary>
    /// Saves the global grants of the groups it sends, in one unit of work. A permission without the one it needs, a
    /// change of the administrators' grant or an unknown group answers <c>400</c>; a grant changed meanwhile, or the host's
    /// lock, <c>409</c>, with nothing saved.
    /// </summary>
    /// <param name="request">The groups' grants.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManagePermissions)]
    [ProducesResponseType(typeof(AccessResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(AccessRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await access.SaveAsync(new AccessSave(PermissionTarget.Global, Guid.Empty, Edits(request.Grants), null, null, RequestAccess.User.Key), cancellationToken);
        return Ok(await OverviewAsync(cancellationToken));
    }

    /// <summary>A folder's grants and restriction, as its Permissions sidebar shows them; <c>404</c> for no such folder.</summary>
    /// <param name="key">The folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("folder/{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManagePermissions)]
    [ProducesResponseType(typeof(AccessTargetResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolder(Guid key, CancellationToken cancellationToken = default) =>
        await TargetAsync(PermissionTarget.Folder, key, cancellationToken) is { } target ? Ok(target) : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);

    /// <summary>A form's grants and restriction, as its Permissions sidebar shows them; <c>404</c> for no such form.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("form/{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManagePermissions)]
    [ProducesResponseType(typeof(AccessTargetResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForm(Guid key, CancellationToken cancellationToken = default) =>
        await TargetAsync(PermissionTarget.Form, key, cancellationToken) is { } target ? Ok(target) : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);

    /// <summary>
    /// Saves a folder's grants and restriction in one unit of work. A global-only permission, one without the one it
    /// needs or an unknown group answers <c>400</c>; a folder in the trash, a grant changed meanwhile or the host's lock
    /// <c>409</c>, with nothing saved; <c>404</c> for no such folder.
    /// </summary>
    /// <param name="key">The folder.</param>
    /// <param name="request">The groups' grants and the restriction.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("folder/{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManagePermissions)]
    [ProducesResponseType(typeof(AccessTargetResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> SaveFolder(Guid key, AccessTargetRequestModel request, CancellationToken cancellationToken = default) =>
        SaveTargetAsync(PermissionTarget.Folder, key, request, cancellationToken);

    /// <summary>
    /// Saves a form's grants and restriction in one unit of work, its restriction at the version the editor read. As a
    /// folder's save answers, and <c>409</c> for a form changed since it was read.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The groups' grants, the restriction and the form's version.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("form/{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManagePermissions)]
    [ProducesResponseType(typeof(AccessTargetResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> SaveForm(Guid key, AccessTargetRequestModel request, CancellationToken cancellationToken = default) =>
        SaveTargetAsync(PermissionTarget.Form, key, request, cancellationToken);

    // The names of a set of permissions, in the order of ADR-0013's table.
    private static IReadOnlyList<string> Names(FormBuilderPermissions permissions) => PermissionRules.NamesOf(permissions);

    // The permissions a list of names holds; an unknown name refuses the save.
    private static FormBuilderPermissions Parse(IEnumerable<string> names)
    {
        var permissions = FormBuilderPermissions.None;
        foreach (var name in names)
        {
            if (!PermissionRules.TryParse(name, out var permission))
            {
                throw new FormBuilderSettingsValidationException([new SettingsError(FormBuilderAccessService.PermissionsField, FormBuilderAccessService.UnknownCode, $"'{name}' is no permission.")]);
            }

            permissions |= permission;
        }

        return permissions;
    }

    // The grants a request sends, the administrators' refused, as they hold every permission everywhere.
    private static List<AccessGrantEdit> Edits(IReadOnlyList<AccessGrantRequestModel> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var edits = grants.Select(grant => new AccessGrantEdit(grant.Group, Parse(grant.Expected), Parse(grant.Permissions))).ToList();
        if (edits.Any(edit => edit.UserGroupKey == Constants.Security.AdminGroupKey && edit.Expected != edit.Permissions))
        {
            throw new FormBuilderSettingsValidationException([new SettingsError(FormBuilderAccessService.GroupField, AdministratorsCode, "The administrators hold every permission everywhere, so their grant can't change.")]);
        }

        return edits;
    }

    private static AccessLockResponseModel? LockOf(AccessHostLock? hostLock) =>
        hostLock is null ? null : new AccessLockResponseModel(hostLock.Field, hostLock.ConfigurationKey);

    private static IReadOnlyList<AccessNeedResponseModel> Needs() =>
        [.. PermissionRules.Each.Where(permission => PermissionRules.Needs(permission) != FormBuilderPermissions.None)
            .Select(permission => new AccessNeedResponseModel(PermissionRules.NameOf(permission), PermissionRules.NameOf(PermissionRules.Needs(permission))))];

    private async Task<IActionResult> SaveTargetAsync(PermissionTarget target, Guid key, AccessTargetRequestModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = target == PermissionTarget.Form ? FormBuilderAccessResourceKind.Form : FormBuilderAccessResourceKind.Folder;
        var saved = await access.SaveAsync(new AccessSave(target, key, Edits(request.Grants), request.IsRestricted, request.Version, RequestAccess.User.Key), cancellationToken);
        if (saved is null || await TargetAsync(target, key, cancellationToken) is not { } now)
        {
            return FormBuilderProblems.NotFound(kind);
        }

        return Ok(now);
    }

    // Every user group, the administrators first, then by name.
    private async Task<List<IUserGroup>> GroupsAsync()
    {
        var all = await userGroups.GetAllAsync(0, int.MaxValue);
        return [.. all.Items
            .OrderByDescending(group => group.Key == Constants.Security.AdminGroupKey)
            .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static AccessGroupResponseModel GroupOf(IUserGroup group, FormBuilderPermissions permissions) =>
        new(
            group.Key,
            group.Name ?? group.Alias,
            group.Icon,
            group.Key == Constants.Security.AdminGroupKey,
            group.AllowedSections.Contains(FormBuilderConstants.SectionAlias, StringComparer.Ordinal),
            Names(group.Key == Constants.Security.AdminGroupKey ? PermissionRules.All : permissions));

    private async Task<AccessResponseModel> OverviewAsync(CancellationToken cancellationToken)
    {
        var snapshot = await access.ReadSnapshotAsync(cancellationToken);
        var groups = await GroupsAsync();
        var (grantedFolders, grantedForms) = snapshot.Granted();
        IReadOnlyList<AccessGrantResponseModel> GrantsOn(PermissionTarget target, Guid key) =>
            [.. groups.Where(group => group.Key != Constants.Security.AdminGroupKey)
                .Select(group => (group.Key, Permissions: snapshot.OwnOf(group.Key, target, key)))
                .Where(grant => grant.Permissions != FormBuilderPermissions.None)
                .Select(grant => new AccessGrantResponseModel(grant.Key, Names(grant.Permissions)))];

        var folderItems = (await folders.ListAllAsync(cancellationToken))
            .Where(folder => folder.TrashedUtc is null && (folder.IsRestricted || grantedFolders.Contains(folder.Key)))
            .Select(folder => new AccessItemResponseModel(FolderType, folder.Key, folder.Name, folder.IsRestricted, GrantsOn(PermissionTarget.Folder, folder.Key)));
        var restricted = await FormsAsync(FormFilter.Live with { RestrictedOnly = true }, cancellationToken);
        var granted = new List<StoredForm>();
        foreach (var chunk in grantedForms.Chunk(KeysAtOnce))
        {
            granted.AddRange(await FormsAsync(FormFilter.Live with { Keys = chunk }, cancellationToken));
        }

        var formItems = restricted.Concat(granted).DistinctBy(form => form.Key)
            .Select(form => new AccessItemResponseModel(FormType, form.Key, form.Name, form.IsRestricted, GrantsOn(PermissionTarget.Form, form.Key)));
        return new AccessResponseModel
        {
            Groups = [.. groups.Select(group => GroupOf(group, snapshot.OwnOf(group.Key, PermissionTarget.Global, Guid.Empty)))],
            Parts = [.. Parts.Select(part => new AccessPartResponseModel(part.Alias, Names(part.Permissions)))],
            Needs = Needs(),
            Lock = LockOf(access.LockOf(PermissionTarget.Global)),
            Items = [.. folderItems.Concat(formItems).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Type, StringComparer.Ordinal)],
        };
    }

    // A folder's or form's grants and restriction; null when it does not exist.
    private async Task<AccessTargetResponseModel?> TargetAsync(PermissionTarget target, Guid key, CancellationToken cancellationToken)
    {
        string name;
        bool isRestricted;
        int? version = null;
        if (target == PermissionTarget.Folder)
        {
            if (await folders.GetAsync(key, cancellationToken) is not { } folder)
            {
                return null;
            }

            (name, isRestricted) = (folder.Name, folder.IsRestricted);
        }
        else
        {
            if (await forms.GetAsync(key, cancellationToken) is not { } form)
            {
                return null;
            }

            (name, isRestricted, version) = (form.Name, form.IsRestricted, form.ConcurrencyVersion);
        }

        var snapshot = await access.ReadSnapshotAsync(cancellationToken);
        var groups = await GroupsAsync();
        return new AccessTargetResponseModel
        {
            Type = target == PermissionTarget.Folder ? FolderType : FormType,
            Key = key,
            Name = name,
            IsRestricted = isRestricted,
            Version = version,
            Groups = [.. groups.Where(group => group.Key != Constants.Security.AdminGroupKey).Select(group => GroupOf(group, snapshot.OwnOf(group.Key, target, key)))],
            Permissions = Names(PermissionRules.FolderLevel),
            Needs = Needs(),
            Lock = LockOf(access.LockOf(target)),
        };
    }

    // Every form a filter holds, page by page.
    private async Task<List<StoredForm>> FormsAsync(FormFilter filter, CancellationToken cancellationToken)
    {
        var all = new List<StoredForm>();
        for (var skip = 0; ; skip += Page)
        {
            var page = await forms.ListAsync(filter, new PageRequest(skip, Page), cancellationToken);
            all.AddRange(page.Items);
            if (page.Items.Count < Page || all.Count >= page.Total)
            {
                return all;
            }
        }
    }
}
