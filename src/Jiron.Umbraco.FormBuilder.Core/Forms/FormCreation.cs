using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// How a new form came to be: what its notifications say, its audit action, and what the entry records besides the
/// alias, the version and the folder.
/// </summary>
/// <param name="Source">Blank, a template, a duplicate or an import.</param>
/// <param name="SourceKey">The template, form or exported form it came from.</param>
/// <param name="Action">The audit action of the create.</param>
/// <param name="Details">Adds to the audit entry's details.</param>
internal sealed record FormOrigin(FormSource Source, Guid? SourceKey, string Action, Func<AuditDetails, AuditDetails>? Details = null);

/// <summary>
/// Creates forms for the form service and the exchange (ADR-0044, ADR-0046, ADR-0053): a blank design, the new-form
/// defaults of the general settings, and the create itself with the first free alias, tried again when a create at the
/// same moment takes it. A create is the form service's change, so it logs as the form service.
/// </summary>
internal sealed partial class FormCreation(
    IFormRepository forms,
    FormDocuments documents,
    FormDefinitionSerializer serializer,
    IFormBuilderSettingsService settings,
    FormBuilderChanges<FormBuilderFormService> changes,
    ILogger<FormBuilderFormService> logger)
{
    /// <summary>How often a create takes the next free alias when a create at the same moment took the one it chose.</summary>
    public const int AliasAttempts = 3;

    // The type of the consent field a new form starts with.
    private const string ConsentType = "consent";

    /// <summary>
    /// A form from blank: one page with one group, and one outcome without text, which shows the built-in thank-you
    /// message in the visitor's language; the group starts with the consent field of the new-form defaults.
    /// </summary>
    public Task<FormDefinition> BlankAsync(CancellationToken cancellationToken) =>
        WithNewFormDefaultsAsync(
            new FormDefinition
            {
                Key = Guid.NewGuid(),
                Pages = [new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] }],
                Outcomes = [new FormOutcome { Key = Guid.NewGuid() }],
            },
            cancellationToken);

    /// <summary>
    /// A new design with the new-form defaults (ADR-0044, ADR-0046): while the consent default is set, the consent field
    /// at the end of the last group of the last page, unless the form has a consent field or a field with its alias.
    /// </summary>
    public async Task<FormDefinition> WithNewFormDefaultsAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var general = await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken);
        if (general.Value.NewForms?.Consent is not { } consent
            || definition.Pages.Count == 0
            || definition.Pages[^1].Groups.Count == 0
            || DefinitionKeys.Fields(definition).Any(field => field.Type == ConsentType || string.Equals(field.Alias, NewFormConsent.FieldAlias, StringComparison.OrdinalIgnoreCase)))
        {
            return definition;
        }

        // The settings save validates the texts; a row written another way may hold what a definition refuses, and a new
        // form then starts without the field rather than not at all.
        FormField? field;
        try
        {
            field = ConsentField(consent);
        }
        catch (ArgumentException)
        {
            field = null;
        }

        var page = definition.Pages[^1];
        var group = page.Groups[^1];
        var withConsent = field is null
            ? null
            : definition with { Pages = [.. definition.Pages.SkipLast(1), page with { Groups = [.. page.Groups.SkipLast(1), group with { Fields = [.. group.Fields, field] }] }] };
        if (withConsent is null || serializer.CheckSave(withConsent).Count > 0)
        {
            LogConsentSkipped(logger, definition.Key);
            return definition;
        }

        return withConsent;
    }

    /// <summary>
    /// Creates a form from a definition the save checks took, with <paramref name="alias"/> or the first free alias from
    /// <paramref name="wanted"/>, tried again when a create at the same moment takes it (ADR-0044); the notifications say
    /// where it came from.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">The alias is taken.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    public async Task<FormInfo> CreateAsync(FormDefinition definition, string name, string? alias, string wanted, StoredFolder? folder, Guid? userKey, FormOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(origin);
        var document = documents.Document(definition);
        for (var attempt = 1; ; attempt++)
        {
            var chosen = alias ?? FormAliases.FirstFree(wanted, await forms.ListAliasesAsync(FormAliases.Stem(wanted), cancellationToken));
            await changes.CancelableAsync(new FormSavingNotification(definition, chosen, name, isNew: true, userKey, new EventMessages()) { CreatedFrom = origin.Source, SourceKey = origin.SourceKey });
            SavedVersion created;
            try
            {
                created = await changes.WriteAsync(
                    () => forms.CreateAsync(new NewForm(definition.Key, folder?.Id, chosen, name, document, UserKey: userKey), cancellationToken),
                    saved =>
                    {
                        var details = new AuditDetails().AddIdentifier("alias", chosen).Add("version", saved.Version.Info.Number).Add("folder", folder?.Key);
                        return [FormAudit.Entry(origin.Action, saved.Form, userKey, origin.Details?.Invoke(details) ?? details)];
                    },
                    cancellationToken);
            }
            catch (FormBuilderConflictException exception) when (exception.Conflict == FormBuilderConflict.DuplicateAlias && alias is null && attempt < AliasAttempts)
            {
                // A create at the same moment took the alias: the next free one, announced again.
                continue;
            }

            var info = FormInfos.Info(created.Form, folder?.Key);
            await changes.PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, created.Version.Info.Number, isNew: true, userKey) { CreatedFrom = origin.Source, SourceKey = origin.SourceKey }, FormAudit.Subject(info.Key));
            return info;
        }
    }

    // The required consent field of the new-form defaults, with the site's own label and wording (ADR-0044), or null
    // when the wording is not rich text a definition takes: the save checks do not read a field's settings, so the
    // wording is checked here, as the settings save checks it.
    private static FormField? ConsentField(NewFormConsent consent)
    {
        var wording = new LocalizedHtml(consent.Text);
        if (string.IsNullOrWhiteSpace(wording.Default) || DefinitionSaveChecks.CheckText(wording, html: true).Count > 0)
        {
            return null;
        }

        var text = new JsonObject();
        foreach (var (culture, html) in wording.Texts)
        {
            text[culture] = html;
        }

        return new FormField
        {
            Key = Guid.NewGuid(),
            Type = ConsentType,
            Alias = NewFormConsent.FieldAlias,
            Label = new LocalizedText(consent.Label),
            Required = true,
            Settings = SettingsObject.Parse(new JsonObject { ["text"] = text }.ToJsonString()),
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The consent field of the new-form defaults is not one a definition takes, so the new form {FormKey} starts without it; save the general settings again")]
    private static partial void LogConsentSkipped(ILogger logger, Guid formKey);
}
