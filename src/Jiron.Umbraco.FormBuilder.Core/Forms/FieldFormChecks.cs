using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// A field visitors see without a label (ROADMAP 9.5b, ADR-0002, ADR-0177): an error, since a screen reader names the
/// field by it, a hidden label too, and the entries name its answer by it. A type that takes no label, or keeps its value
/// on the server where no visitor sees it, needs none, nor does a calculation no visitor sees, nor a consent, whose
/// wording is its checkbox's label. The image's alternative text is its type's required setting, which
/// the structural validation already refuses (<c>setting_missing</c>).
/// </summary>
/// <param name="types">The field types.</param>
internal sealed class LabelFormCheck(FieldTypeRegistry types) : IFormCheck
{
    /// <summary>A field visitors see without a label.</summary>
    public const string LabelMissing = "label_missing";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var groups = JsonPaths.Property(JsonPaths.Index(pages, p), "groups");
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                Fields(issues, definition.Pages[p].Groups[g].Fields, JsonPaths.Property(JsonPaths.Index(groups, g), "fields"));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
    }

    private void Fields(List<FormCheckIssue> issues, IReadOnlyList<FormField> fields, string path)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            var fieldPath = JsonPaths.Index(path, f);
            if (types.Find(field.Type) is { } traits
                && traits.Supports(FieldFeatures.Label)
                && !traits.ServerOnlyDefault
                && traits.Type is not ConsentFieldType
                && (traits.Type is not CalculationFieldType calculation || calculation.ReadSettings(field.Settings).Visible)
                && string.IsNullOrWhiteSpace(field.Label?.Default))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Error, LabelMissing, JsonPaths.Property(fieldPath, "label"), "This field has no label, which screen readers and the entries name it by."));
            }

            Fields(issues, field.Fields, JsonPaths.Property(fieldPath, "fields"));
        }
    }
}

/// <summary>
/// A form without a field to fill in (ROADMAP 9.5f, IA 5.1, ADR-0181): an error, so a new blank form never reads "No
/// problems" and is not published with nothing for visitors to send. A field counts when its type holds a value, or
/// when the site lacks its type, which the structural validation refuses already; a text, a heading or an image alone
/// does not. The problem is at the end of the first page, where the designer's Fix it opens the palette. A form without
/// pages is the structural validation's error.
/// </summary>
/// <param name="types">The field types.</param>
internal sealed class FieldsMissingFormCheck(FieldTypeRegistry types) : IFormCheck
{
    /// <summary>A form without a field to fill in.</summary>
    public const string FieldsMissing = "fields_missing";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // A repeatable group holds a value of its own, so its children need no look.
        if (definition.Pages.Count == 0
            || definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Any(field => types.Find(field.Type) is not { HoldsValue: false }))
        {
            return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>([]);
        }

        var groups = JsonPaths.Property(JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "pages"), 0), "groups");
        var count = definition.Pages[0].Groups.Count;
        var path = count == 0 ? groups : JsonPaths.Property(JsonPaths.Index(groups, count - 1), "fields");
        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(
            [new FormCheckIssue(FormCheckSeverity.Error, FieldsMissing, path, "The form has no field to fill in yet, so visitors would have nothing to send.")]);
    }
}

/// <summary>
/// A captcha provider without its keys on this site (ROADMAP 9.5b, ADR-0142, ADR-0177): a warning, not an error, since
/// keys differ between environments and a definition travels between them, but until it has them the form's entries go
/// to Spam. A provider the site lacks is the structural validation's error.
/// </summary>
/// <param name="providers">The site's captcha providers.</param>
internal sealed class CaptchaKeysFormCheck(FormBuilderCaptchaProviderCollection providers) : IFormCheck
{
    /// <summary>A captcha provider without its keys.</summary>
    public const string CaptchaNotConfigured = "captcha_not_configured";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<FormCheckIssue> issues = definition.Settings.Protection.Captcha is { } captcha && providers.Find(captcha.Provider) is { IsConfigured: false }
            ? [new FormCheckIssue(FormCheckSeverity.Warning, CaptchaNotConfigured, "$.settings.protection.captcha.provider", $"The captcha provider '{captcha.Provider}' has no keys on this site yet, so the form's entries go to Spam until it has them.")]
            : [];
        return ValueTask.FromResult(issues);
    }
}

/// <summary>
/// An email action without an email field to reply to (ROADMAP 9.5b, ADR-0160, ADR-0177): on a form without an email
/// field, a warning, since a reply to the mail then reaches no one who filled in the form; on a form with one, a hint
/// where a mail to the site's people names none to reply to. An action switched off sends nothing, a mail to the visitor
/// is their own, and settings that could never send are the action's own check's errors.
/// </summary>
internal sealed class EmailReplyFormCheck : IFormCheck
{
    /// <summary>An email action on a form without an email field to reply to (a warning).</summary>
    public const string NoReplyField = "no_reply_field";

    /// <summary>A mail to the site's people that names no email field to reply to, on a form with one (a hint).</summary>
    public const string ReplyFieldUnset = "reply_field_unset";

    private const string EmailFieldType = "email";

    // Whether the action's settings name a field in a setting.
    private static bool Names(FormAction action, string setting) =>
        action.Settings is { } settings && settings.TryGetProperty(setting, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        var hasEmailField = definition.Pages
            .SelectMany(page => page.Groups)
            .SelectMany(group => group.Fields)
            .SelectMany(field => field.Fields.Prepend(field))
            .Any(field => field.Type == EmailFieldType);
        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var a = 0; a < definition.Actions.Count; a++)
        {
            if (definition.Actions[a] is not { Enabled: true, Type: EmailActionType.Alias } action)
            {
                continue;
            }

            var path = JsonPaths.Index(actions, a);
            if (!hasEmailField)
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, NoReplyField, path, "The form has no email field, so a reply to this mail reaches no one who filled in the form."));
            }
            else if (!Names(action, "replyToField") && !Names(action, "toField"))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Hint, ReplyFieldUnset, JsonPaths.Property(JsonPaths.Property(path, "settings"), "replyToField"), "Choose the email field to reply to, so a reply to this mail reaches the person who filled in the form."));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
    }
}
