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
/// on the server where no visitor sees it, needs none, nor does a consent, whose wording is its checkbox's label. The image's alternative text is its type's required setting, which
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
                && string.IsNullOrWhiteSpace(field.Label?.Default))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Error, LabelMissing, JsonPaths.Property(fieldPath, "label"), "This field has no label, which screen readers and the entries name it by."));
            }

            Fields(issues, field.Fields, JsonPaths.Property(fieldPath, "fields"));
        }
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
/// An email action on a form without an email field (ROADMAP 9.5b, ADR-0160, ADR-0177): a warning, since a reply to the
/// mail then reaches no one who filled in the form. An action switched off sends nothing, and settings that could never
/// send are the action's own check's errors.
/// </summary>
internal sealed class EmailReplyFormCheck : IFormCheck
{
    /// <summary>An email action without an email field to reply to.</summary>
    public const string NoReplyField = "no_reply_field";

    private const string EmailFieldType = "email";

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
        if (hasEmailField)
        {
            return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
        }

        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var a = 0; a < definition.Actions.Count; a++)
        {
            if (definition.Actions[a] is { Enabled: true, Type: EmailActionType.Alias })
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, NoReplyField, JsonPaths.Index(actions, a), "The form has no email field, so a reply to this mail reaches no one who filled in the form."));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
    }
}
