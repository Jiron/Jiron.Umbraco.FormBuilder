using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
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
/// member manager is; the submission pipeline asks it the same at every submission (ADR-0231).
/// </summary>
/// <param name="members">Umbraco's member manager, which knows the signed-in member and their groups.</param>
/// <param name="groups">The member groups, by key.</param>
internal sealed class FormMembers(IMemberManager members, IMemberGroupService groups) : IFormMemberContext
{
    /// <summary>The key of the member signed in on the request, or null (ADR-0124).</summary>
    public async Task<Guid?> KeyAsync() => (await members.GetCurrentMemberAsync())?.Key;

    /// <summary>The groups among these, as a form's rules name them, that the member signed in on the request is in (ADR-0138).</summary>
    /// <param name="named">The groups, by key or name.</param>
    public async Task<IReadOnlyList<string>> GroupsAmongAsync(IReadOnlySet<string> named)
    {
        ArgumentNullException.ThrowIfNull(named);
        if (await members.GetCurrentMemberAsync() is not { } member)
        {
            return [];
        }

        var roles = (await members.GetRolesAsync(member)).ToHashSet(StringComparer.Ordinal);
        var keys = named.Select(name => Guid.TryParse(name, out var key) ? key : (Guid?)null).OfType<Guid>().ToList();
        var byKey = keys.Count == 0 ? [] : (await groups.GetAsync(keys)).ToList();
        return [.. named.Where(name => roles.Contains(name) || byKey.Any(group => group.Name is { } own && roles.Contains(own) && Guid.TryParse(name, out var key) && key == group.Key))];
    }

    /// <inheritdoc />
    public async Task<bool> AdmitsAsync(FormAvailabilitySettings availability) => await GateAsync(availability) is null;

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
