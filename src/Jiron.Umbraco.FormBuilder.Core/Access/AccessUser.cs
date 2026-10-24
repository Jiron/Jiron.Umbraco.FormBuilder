using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>Who asks, as the evaluation sees them (ADR-0048): read from the backoffice user of the request.</summary>
/// <param name="Key">The user's key.</param>
/// <param name="GroupKeys">The keys of the user's groups.</param>
/// <param name="IsAdministrator">Whether the user is in Umbraco's administrators group, and so holds every permission.</param>
/// <param name="HasSection">Whether the user's groups give them the Forms section, without which they hold nothing.</param>
internal sealed record AccessUser(Guid Key, IReadOnlySet<Guid> GroupKeys, bool IsAdministrator, bool HasSection)
{
    /// <summary>The evaluation's view of a backoffice user.</summary>
    public static AccessUser Of(IUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var groups = user.Groups.ToList();
        return new AccessUser(
            user.Key,
            groups.Select(group => group.Key).ToHashSet(),
            groups.Any(group => string.Equals(group.Alias, Constants.Security.AdminGroupAlias, StringComparison.Ordinal)),
            user.AllowedSections.Contains(FormBuilderConstants.SectionAlias, StringComparer.Ordinal));
    }
}
