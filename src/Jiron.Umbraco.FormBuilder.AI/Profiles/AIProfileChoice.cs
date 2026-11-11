using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;

namespace Jiron.Umbraco.FormBuilder.AI.Profiles;

/// <summary>The profile the add-on uses, or why it has none.</summary>
/// <param name="ChosenId">The profile chosen in Settings > AI, or null for Umbraco.AI's default chat profile.</param>
/// <param name="Profile">The profile the add-on uses, or null when it has none.</param>
/// <param name="Problem">One of <see cref="AIProblems"/> when it has none, else null.</param>
internal sealed record AIProfileState(Guid? ChosenId, AIProfile? Profile, string? Problem)
{
    /// <summary>The profile, or the refusal that says why there is none.</summary>
    /// <exception cref="AIUnavailableException">There is no profile the add-on can use.</exception>
    public AIProfile Usable() => Profile is { } profile && Problem is null
        ? profile
        : throw new AIUnavailableException(Problem ?? AIProblems.NoProfile, "The AI add-on has no chat profile to use.");
}

/// <summary>
/// Settings > AI (ADR-0018): the Umbraco.AI profile the add-on uses, kept in <c>jfbKeyValue</c> and audited; without one,
/// Umbraco.AI's default chat profile. A chosen profile that was deleted, or that cannot chat, is a problem the health
/// check and the features say, never a silent fallback to another profile.
/// </summary>
internal sealed class AIProfileChoice(IKeyValueRepository keyValues, IAIProfileService profiles, FormBuilderChanges<AIProfileChoice> changes)
{
    /// <summary>The profile chosen in Settings > AI, or null for the default.</summary>
    public async Task<Guid?> GetChosenAsync(CancellationToken cancellationToken = default) =>
        Guid.TryParse(await keyValues.GetAsync(FormBuilderAI.ProfileKey, cancellationToken), out var id) ? id : null;

    /// <summary>The profile the add-on uses now, or why it has none.</summary>
    public async Task<AIProfileState> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (await GetChosenAsync(cancellationToken) is { } chosen)
        {
            var profile = await profiles.GetProfileAsync(chosen, cancellationToken);
            return profile switch
            {
                null => new AIProfileState(chosen, null, AIProblems.ProfileMissing),
                { Capability: not AICapability.Chat } => new AIProfileState(chosen, null, AIProblems.ProfileCannotChat),
                _ => new AIProfileState(chosen, profile, null),
            };
        }

        return await profiles.HasDefaultProfileAsync(AICapability.Chat, cancellationToken)
            ? new AIProfileState(null, await profiles.GetDefaultProfileAsync(AICapability.Chat, cancellationToken), null)
            : new AIProfileState(null, null, AIProblems.NoProfile);
    }

    /// <summary>Chooses a chat profile, or the default with null; audited when it changes the choice.</summary>
    /// <exception cref="FormBuilderValidationException">The profile does not exist or cannot chat.</exception>
    public async Task ChooseAsync(Guid? profileId, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit("The AI profile is chosen in its own unit of work.");
        if (profileId is { } id && await profiles.GetProfileAsync(id, cancellationToken) is not { Capability: AICapability.Chat })
        {
            throw FormBuilderValidationException.For("profile_invalid", "$.profileId", "The profile does not exist or cannot chat.");
        }

        var before = await GetChosenAsync(cancellationToken);
        if (before == profileId)
        {
            return;
        }

        await changes.WriteAsync(
            async () =>
            {
                if (profileId is { } chosen)
                {
                    await keyValues.SetAsync(FormBuilderAI.ProfileKey, chosen.ToString("D"), cancellationToken);
                }
                else
                {
                    await keyValues.DeleteAsync(FormBuilderAI.ProfileKey, cancellationToken);
                }

                return true;
            },
            // Recorded as the settings sections' saves are, without a site, its section named, so the audit log says
            // "Settings > AI"; the profiles are details, never the entry's key.
            _ => [new NewAuditEntry(FormBuilderAI.ProfileChosenAction, FormBuilderSettingsService.EntityType, null, userKey, new AuditDetails().AddIdentifier("section", FormBuilderAI.SettingsSection).Add("from", before).Add("to", profileId))],
            cancellationToken);
    }
}
