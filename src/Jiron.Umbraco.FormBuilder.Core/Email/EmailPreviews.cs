using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>How a request for an email action's preview came out.</summary>
internal enum EmailPreviewStatus
{
    /// <summary>It is rendered; see the preview.</summary>
    Done,

    /// <summary>The definition has no such action.</summary>
    ActionNotFound,

    /// <summary>The action is of another type, which sends no mail of its own.</summary>
    NotEmail,

    /// <summary>The action names a mail view the site lacks and has no body of its own.</summary>
    ViewMissing,

    /// <summary>The action's settings do not fit its type, as the form check says; there is no mail until they do.</summary>
    SettingsInvalid,
}

/// <summary>What a request for an email action's preview came to.</summary>
/// <param name="Status">How it came out.</param>
/// <param name="Preview">The mail; null unless it is rendered.</param>
/// <param name="Direction">Which way the culture's text runs, <c>ltr</c> or <c>rtl</c>.</param>
internal sealed record EmailPreviewResult(EmailPreviewStatus Status, EmailPreview? Preview = null, string Direction = "ltr");

/// <summary>
/// The previews of a draft's email actions in the backoffice (ADR-0189): the mail an action sends for the sample entry, as
/// a run renders it with the site's sender and layout, its locked settings applied; nothing is queued or stored.
/// </summary>
/// <param name="types">The site's action types.</param>
/// <param name="locks">The host's locks of action settings.</param>
/// <param name="samples">The sample entry.</param>
internal sealed class EmailPreviews(ActionTypeRegistry types, ActionSettingLocks locks, SampleEntries samples)
{
    /// <summary>The preview of one of a definition's email actions.</summary>
    /// <param name="form">The form.</param>
    /// <param name="definition">The definition, such as the draft as the action's sidebar would make it.</param>
    /// <param name="action">The key of one of its actions.</param>
    /// <param name="culture">A language of the site.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EmailPreviewResult> RenderAsync(FormInfo form, FormDefinition definition, Guid action, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(culture);
        if (definition.Actions.FirstOrDefault(each => each.Key == action) is not { } found)
        {
            return new EmailPreviewResult(EmailPreviewStatus.ActionNotFound);
        }

        if (types.Find(found.Type) is not EmailActionType email)
        {
            return new EmailPreviewResult(EmailPreviewStatus.NotEmail);
        }

        // Settings the type cannot read, such as a draft written before its editor, are the form check's to say; the type
        // reads them only once the schema's check passes, as the form check does.
        var locked = locks.Apply(found);
        if (!FieldSettingsChecks.Check(email.SettingsSchema, locked.Settings, "$.settings", "An email action", new DefinitionProblems()))
        {
            return new EmailPreviewResult(EmailPreviewStatus.SettingsInvalid);
        }

        var scope = await samples.ScopeAsync(definition, form.Name, form.Alias, culture, cancellationToken);
        var direction = CultureInfo.GetCultureInfo(culture).TextInfo.IsRightToLeft ? "rtl" : "ltr";
        try
        {
            return new EmailPreviewResult(EmailPreviewStatus.Done, await email.PreviewAsync(locked, scope, cancellationToken), direction);
        }
        catch (ViewMissingException)
        {
            return new EmailPreviewResult(EmailPreviewStatus.ViewMissing);
        }
    }
}
