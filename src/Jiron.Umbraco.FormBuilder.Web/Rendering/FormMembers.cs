using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>Why a form that is for members shows its notice instead (ADR-0095).</summary>
internal enum MemberGate
{
    /// <summary>The visitor is not signed in.</summary>
    SignIn,

    /// <summary>The signed-in member is in none of the form's member groups.</summary>
    Groups,
}

/// <summary>
/// Whether the visitor may see a form (ADR-0004, ADR-0095): anyone a form for anyone, a signed-in member a form for
/// members, and a member of one of its groups a form for member groups, the groups compared by their names as Umbraco
/// names a member's roles. A group the form names that no longer exists admits nobody. One per request, as Umbraco's
/// member manager is.
/// </summary>
/// <param name="members">Umbraco's member manager, which knows the signed-in member and their groups.</param>
/// <param name="groups">The member groups, by key.</param>
internal sealed class FormMembers(IMemberManager members, IMemberGroupService groups)
{
    /// <summary>Why the form shows its notice to the visitor, or null when it shows.</summary>
    /// <param name="availability">The form's availability, which says for whom it is.</param>
    public async Task<MemberGate?> GateAsync(FormAvailabilitySettings availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (availability.Access == MemberAccess.Anyone)
        {
            return null;
        }

        if (await members.GetCurrentMemberAsync() is not { } member)
        {
            return MemberGate.SignIn;
        }

        if (availability.Access != MemberAccess.Groups)
        {
            return null;
        }

        HashSet<string> named = availability.MemberGroups.Count == 0
            ? []
            : (await groups.GetAsync(availability.MemberGroups)).Select(group => group.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = await members.GetRolesAsync(member);
        return roles.Any(named.Contains) ? null : MemberGate.Groups;
    }
}
