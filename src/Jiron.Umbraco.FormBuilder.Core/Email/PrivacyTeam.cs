using Jiron.Umbraco.FormBuilder.Core.Access;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// The people who may use the privacy tools (ADR-0217): the active users with an address of the administrators and of every
/// group granted <c>privacyTools</c>, each group with the Form Builder section, which the tools are in. An email action's
/// <c>toPrivacyTeam</c> goes to them as they are when it runs.
/// </summary>
/// <param name="access">The permissions, which name the groups granted the tools.</param>
/// <param name="groups">Umbraco's user groups.</param>
/// <param name="users">Umbraco's users.</param>
internal sealed class PrivacyTeam(IFormBuilderAccessService access, IUserGroupService groups, IUserService users)
{
    /// <summary>Their addresses, each once in any letter case, in order.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyList<string>> AddressesAsync(CancellationToken cancellationToken = default)
    {
        var keys = new List<Guid> { Constants.Security.AdminGroupKey };
        keys.AddRange(await access.ListGroupsHoldingAsync(FormBuilderPermissions.PrivacyTools, cancellationToken));
        var addresses = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Distinct())
        {
            if (await groups.GetAsync(key) is not { } group || !group.AllowedSections.Contains(FormBuilderConstants.SectionAlias, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var user in users.GetAllInGroup(group.Id))
            {
                if (user.IsApproved && !user.IsLockedOut && EmailAddresses.IsOne(user.Email))
                {
                    addresses.Add(user.Email);
                }
            }
        }

        return [.. addresses];
    }
}
