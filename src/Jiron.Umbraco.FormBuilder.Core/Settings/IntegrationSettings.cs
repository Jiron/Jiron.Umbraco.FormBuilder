using Jiron.Umbraco.FormBuilder.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The integrations settings (section 4.8, ADR-0161), the same for every site. What it names of the outside are references to the host's
/// secrets, as a Slack or Teams address holds its own key, and never a value.
/// </summary>
internal sealed record IntegrationSettings : ISettingsSection
{
    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Integrations;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <summary>A reference to the secret webhooks are signed with, unless an action names its own; null to send them unsigned.</summary>
    [SettingsField("webhooks", Editor = "secretReference")]
    public string? SigningSecret { get; init; }

    /// <summary>A reference to the address of Slack's incoming webhook, which a Slack action goes to unless it names its own.</summary>
    [SettingsField("chat", Editor = "secretReference")]
    public string? SlackWebhook { get; init; }

    /// <summary>A reference to the address of the Microsoft Teams workflow, which a Teams action goes to unless it names its own.</summary>
    [SettingsField("chat", Order = 1, Editor = "secretReference")]
    public string? TeamsWebhook { get; init; }

    /// <summary>Whether entry events reach Umbraco's own webhooks (Settings, Webhooks); on by default, and they send only to what a site set up.</summary>
    [SettingsField("webhooks", Order = 1)]
    public bool UmbracoWebhookEvents { get; init; } = true;
}

/// <summary>Holds the integrations settings to references: a secret never reaches the database.</summary>
internal sealed class IntegrationSettingsValidator : ISettingsValidator<IntegrationSettings>
{
    /// <inheritdoc />
    public void Validate(IntegrationSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var (field, value) in new[] { ("signingSecret", section.SigningSecret), ("slackWebhook", section.SlackWebhook), ("teamsWebhook", section.TeamsWebhook) })
        {
            if (value is not null && !FormBuilderSecrets.IsReference(value))
            {
                errors.Add(field, SettingsErrors.SecretCode, "Must be a reference such as $JironFormBuilder:Secrets:Crm, never the secret itself.");
            }
        }
    }
}
