using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// What a user needs for the actions a definition adds or changes, beyond <c>manageActions</c> (ADR-0235): an action that
/// copies sensitive answers needs <c>viewSensitive</c> on the form, or whoever manages actions could send themselves what
/// they may not read (as ADR-0150 says of lowering a field's sensitivity); an action that names one of the site's secrets
/// needs <c>manageSettings</c>, or whoever manages actions could send a secret to an address they choose; and a content action
/// needs the user's own Umbraco rights where it creates, updates or publishes, or a form designer could write to content they
/// may not touch.
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
            if (await SecretRefusalAsync(user, action, cancellationToken) is { } secret)
            {
                return secret;
            }

            var type = types.Find(action.Type);
            if (sensitive && type is not null && CopiesSensitive(type, action))
            {
                var permissions = held ??= await access.GetForFormAsync(user, form, cancellationToken);
                if ((permissions & FormBuilderPermissions.ViewSensitive) == FormBuilderPermissions.None)
                {
                    return FormBuilderProblems.Missing(FormBuilderPermissions.ViewSensitive);
                }
            }

            if (type is ContentActionType contentType && Fitting(() => contentType.ReadSettings(action.Settings)) is { } contentSettings
                && await content.MissingAsync(user.Key, contentSettings, after) is { } gap)
            {
                return FormBuilderProblems.ContentRights(gap);
            }
        }

        return null;
    }

    /// <summary>
    /// The problems of the actions <paramref name="after"/> adds or changes that hold a secret as itself where only a
    /// reference may stand (16.1's review): publishing refused them, but a draft kept them in the database and its History.
    /// A save refuses them at their path, as the designer's own editor does.
    /// </summary>
    /// <param name="before">The draft the save replaces; null for none.</param>
    /// <param name="after">The definition saved.</param>
    public IReadOnlyList<DefinitionProblem> LiteralSecrets(FormDefinition? before, FormDefinition after)
    {
        ArgumentNullException.ThrowIfNull(after);
        var changed = DefinitionDiff.ChangedActions(before, after);
        var problems = new List<DefinitionProblem>();
        for (var index = 0; index < after.Actions.Count; index++)
        {
            var action = after.Actions[index];
            if (changed.Contains(action.Key) && types.Find(action.Type) is WebhookActionType webhook && Fitting(() => webhook.ReadSettings(action.Settings)) is { } webhookSettings)
            {
                var settings = JsonPaths.Property(JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "actions"), index), "settings");
                problems.AddRange(WebhookActionType.LiteralSecrets(webhookSettings)
                    .Select(literal => new DefinitionProblem(SettingsErrors.SecretCode, JsonPaths.Property(settings, literal.Setting), literal.Message)));
            }
        }

        return problems;
    }

    /// <summary>
    /// The refusal for an action that sends one of the site's secrets, for a user without <c>manageSettings</c>; null
    /// otherwise. Whoever manages settings decides where the site's secrets go, so only they set up or change an action
    /// that sends one, or test one that is not as it was saved.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="action">The new or changed action.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<FormBuilderProblemResult?> SecretRefusalAsync(AccessUser user, FormAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(action);
        return action.Settings is { } settings && SendsASecret(action.Type, settings.Value)
            && (await access.GetGlobalAsync(user, cancellationToken) & FormBuilderPermissions.ManageSettings) == FormBuilderPermissions.None
            ? FormBuilderProblems.Missing(FormBuilderPermissions.ManageSettings)
            : null;
    }

    // Whether the action sends a secret's value to where it says. The webhook's address as a reference is where it goes, and
    // its signing secret only signs, so of its settings only a header sends one (Slack's and Teams' are its presets); any
    // other type's reference counts wherever it is.
    private static bool SendsASecret(string type, JsonElement settings) =>
        type == WebhookActionType.Alias
            ? settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("headers", out var headers) && NamesASecret(headers)
            : NamesASecret(settings);

    // Whether any text in the settings, however deep, is a reference to a secret.
    private static bool NamesASecret(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => FormBuilderSecrets.IsReference(value.GetString()),
        JsonValueKind.Array => value.EnumerateArray().Any(NamesASecret),
        JsonValueKind.Object => value.EnumerateObject().Any(property => NamesASecret(property.Value)),
        _ => false,
    };

    // Whether the action names a sensitive field to copy: any setting of its type that picks sensitive fields holds one.
    // The settings as their type reads them; null when they do not fit its schema. A draft may hold such settings, but they
    // run nowhere: publishing refuses them first, and a run reads them as this does, so the checks above leave them alone.
    private static T? Fitting<T>(Func<T> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool CopiesSensitive(IFormBuilderActionType type, FormAction action) =>
        action.Settings is { } settings
        && type.SettingsSchema.Settings.Any(setting => setting.Picks == FieldPick.Sensitive
            && settings.TryGetProperty(setting.Name, out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0);
}
