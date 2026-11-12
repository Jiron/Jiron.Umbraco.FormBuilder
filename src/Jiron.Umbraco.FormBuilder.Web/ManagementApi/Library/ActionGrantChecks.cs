using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// What a user needs for the actions a definition adds or changes, beyond <c>manageActions</c> (ADR-0235): an action that
/// copies sensitive answers needs <c>viewSensitive</c> on the form, or whoever manages actions could send themselves what
/// they may not read (as ADR-0150 says of lowering a field's sensitivity); and a content action needs the user's own Umbraco
/// rights where it creates, updates or publishes, or a form designer could write to content they may not touch.
/// </summary>
/// <param name="access">What a user holds on a form.</param>
/// <param name="types">The site's action types.</param>
/// <param name="content">Umbraco's rights a content action needs.</param>
internal sealed class ActionGrantChecks(IFormBuilderAccessService access, ActionTypeRegistry types, ContentActionRights content)
{
    /// <summary>
    /// The refusal for <paramref name="after"/>'s actions that are new or changed against <paramref name="before"/>, or null.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="form">The form, for the user's permissions on it.</param>
    /// <param name="before">The definition compared with: the draft a save replaces, the live version a publish replaces; null for none.</param>
    /// <param name="after">The definition whose actions would be kept or run.</param>
    /// <param name="sensitive">Whether copied sensitive answers count: not for the preview's test entries, whose answers the user typed.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<FormBuilderProblemResult?> RefusalAsync(AccessUser user, FormAccessTarget form, FormDefinition? before, FormDefinition after, bool sensitive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(after);
        var changed = DefinitionDiff.ChangedActions(before, after);
        if (changed.Count == 0)
        {
            return null;
        }

        FormBuilderPermissions? held = null;
        foreach (var action in after.Actions.Where(action => changed.Contains(action.Key)))
        {
            var type = types.Find(action.Type);
            if (sensitive && type is not null && CopiesSensitive(type, action))
            {
                var permissions = held ??= await access.GetForFormAsync(user, form, cancellationToken);
                if ((permissions & FormBuilderPermissions.ViewSensitive) == FormBuilderPermissions.None)
                {
                    return FormBuilderProblems.Missing(FormBuilderPermissions.ViewSensitive);
                }
            }

            if (type is ContentActionType contentType && await content.MissingAsync(user.Key, contentType.ReadSettings(action.Settings), after) is { } gap)
            {
                return FormBuilderProblems.ContentRights(gap);
            }
        }

        return null;
    }

    // Whether the action names a sensitive field to copy: any setting of its type that picks sensitive fields holds one.
    private static bool CopiesSensitive(IFormBuilderActionType type, FormAction action) =>
        action.Settings is { } settings
        && type.SettingsSchema.Settings.Any(setting => setting.Picks == FieldPick.Sensitive
            && settings.TryGetProperty(setting.Name, out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0);
}
