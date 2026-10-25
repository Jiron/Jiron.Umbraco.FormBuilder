using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>A one-line text (<c>text</c>, section 4.3.2).</summary>
public sealed class TextFieldType() : FormBuilderFieldTypeBase<TextFieldSettings>(new FieldTypeDeclaration
{
    Alias = "text",
    Name = "#jfbFieldTypes_text",
    Icon = "icon-autofill",
    PaletteGroup = FieldPaletteGroup.Text,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Text,
    Features = BuiltInFieldTypes.TextInput,
    Messages = FieldMessageKinds.Required | FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength,
})
{
    /// <summary>The longest answer a one-line text takes, and the most its <c>maxLength</c> may be (ADR-0058).</summary>
    public const int LongestAnswer = 4000;

    /// <inheritdoc />
    protected override void CheckSettings(TextFieldSettings settings, FieldSettingsCheck check) =>
        BuiltInChecks.NotAbove(check, "minLength", settings.MinLength, "maxLength", settings.MaxLength);

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(TextFieldSettings settings, FormField field) => new(LongestAnswer, 1);
}

/// <summary>The settings of a one-line text (ADR-0002).</summary>
public sealed class TextFieldSettings
{
    /// <summary>The fewest characters an answer has.</summary>
    [Range(0, TextFieldType.LongestAnswer)]
    [FormBuilderSetting("#jfbFieldSettings_textMinLength", Group = FieldSettingGroup.Validation, Public = true)]
    public int MinLength { get; init; }

    /// <summary>The most characters an answer has.</summary>
    [Range(1, TextFieldType.LongestAnswer)]
    [FormBuilderSetting("#jfbFieldSettings_textMaxLength", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int MaxLength { get; init; } = 255;

    /// <summary>A text shown before the input, such as a currency.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_textPrefix", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? Prefix { get; init; }

    /// <summary>A text shown after the input, such as a unit.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_textSuffix", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public LocalizedText? Suffix { get; init; }

    /// <summary>Whether the input counts the characters left.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textCounter", Group = FieldSettingGroup.Appearance, Order = 2, Public = true)]
    public bool Counter { get; init; }
}

/// <summary>A long text (<c>textarea</c>, section 4.3.2).</summary>
public sealed class TextareaFieldType() : FormBuilderFieldTypeBase<TextareaFieldSettings>(new FieldTypeDeclaration
{
    Alias = "textarea",
    Name = "#jfbFieldTypes_textarea",
    Icon = "icon-text-align-left",
    PaletteGroup = FieldPaletteGroup.Text,
    HoldsValue = true,
    Storage = FieldStorageKind.LongText,
    RuleKind = FieldKind.Text,
    Features = BuiltInFieldTypes.TextInput,
    Messages = FieldMessageKinds.Required | FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength | FieldMessageKinds.MinWords | FieldMessageKinds.MaxWords,
})
{
    /// <summary>The longest answer a long text takes, and the most its <c>maxLength</c> may be (ADR-0058).</summary>
    public const int LongestAnswer = 100000;

    /// <inheritdoc />
    protected override void CheckSettings(TextareaFieldSettings settings, FieldSettingsCheck check)
    {
        BuiltInChecks.NotAbove(check, "minLength", settings.MinLength, "maxLength", settings.MaxLength);
        BuiltInChecks.NotAbove(check, "minWords", settings.MinWords, "maxWords", settings.MaxWords);
    }

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(TextareaFieldSettings settings, FormField field) => new(LongestAnswer, 1);
}

/// <summary>The settings of a long text (ADR-0002).</summary>
public sealed class TextareaFieldSettings
{
    /// <summary>How many lines the input shows at first.</summary>
    [Range(1, 50)]
    [FormBuilderSetting("#jfbFieldSettings_textareaRows", Group = FieldSettingGroup.Appearance, Public = true)]
    public int Rows { get; init; } = 4;

    /// <summary>Whether the input grows with its text.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaAutoGrow", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public bool AutoGrow { get; init; } = true;

    /// <summary>The fewest characters an answer has.</summary>
    [Range(0, TextareaFieldType.LongestAnswer)]
    [FormBuilderSetting("#jfbFieldSettings_textareaMinLength", Group = FieldSettingGroup.Validation, Public = true)]
    public int MinLength { get; init; }

    /// <summary>The most characters an answer has.</summary>
    [Range(1, TextareaFieldType.LongestAnswer)]
    [FormBuilderSetting("#jfbFieldSettings_textareaMaxLength", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int MaxLength { get; init; } = 4000;

    /// <summary>The fewest words an answer has, or null for no limit.</summary>
    [Range(1, 20000)]
    [FormBuilderSetting("#jfbFieldSettings_textareaMinWords", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public int? MinWords { get; init; }

    /// <summary>The most words an answer has, or null for no limit.</summary>
    [Range(1, 20000)]
    [FormBuilderSetting("#jfbFieldSettings_textareaMaxWords", Group = FieldSettingGroup.Validation, Order = 3, Public = true)]
    public int? MaxWords { get; init; }

    /// <summary>Whether the input counts the characters left.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaCounter", Group = FieldSettingGroup.Appearance, Order = 2, Public = true)]
    public bool Counter { get; init; }
}

/// <summary>An email address (<c>email</c>, section 4.3.2).</summary>
public sealed class EmailFieldType() : FormBuilderFieldTypeBase<EmailFieldSettings>(new FieldTypeDeclaration
{
    Alias = "email",
    Name = "#jfbFieldTypes_email",
    Icon = "icon-message",
    PaletteGroup = FieldPaletteGroup.Text,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Text,
    Features = BuiltInFieldTypes.TextInput,
    Messages = BuiltInFieldTypes.Readable,
})
{
    /// <summary>The longest address an email field takes: the most a mail server's path allows (RFC 5321).</summary>
    public const int LongestAnswer = 254;

    /// <summary>The part that holds the address typed again (ADR-0058).</summary>
    public const string ConfirmPart = "confirm";

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(EmailFieldSettings settings, FormField field) => new(LongestAnswer, settings.Confirm ? 2 : 1);

    /// <inheritdoc />
    protected override void CheckSettings(EmailFieldSettings settings, FieldSettingsCheck check)
    {
        if (settings.Domains is not { } domains)
        {
            return;
        }

        for (var i = 0; i < domains.List.Count; i++)
        {
            if (!IsDomain(domains.List[i]))
            {
                check.Invalid(BuiltInChecks.At("domains.list", i), $"A domain is a host name in lowercase, such as example.com, not '{domains.List[i]}'.");
            }
        }

        if (domains.Mode == EmailDomainMode.Any && domains.List.Count > 0)
        {
            check.Invalid("domains.list", "An email field that takes any domain lists none; allow or block the listed domains instead.");
        }
        else if (domains.Mode != EmailDomainMode.Any && domains.List.Count == 0)
        {
            check.Invalid("domains.list", "An email field that allows or blocks domains lists at least one.");
        }
    }

    // A host name of at least two labels in lowercase, as IDNA reads it: an internationalised name, or its xn-- form.
    private static bool IsDomain(string domain)
    {
        if (domain.Any(char.IsUpper))
        {
            return false;
        }

        try
        {
            var labels = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(domain).Split('.');
            return labels.Length >= 2 && labels.All(label => label.Length is >= 1 and <= 63);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>
/// The settings of an email address (ADR-0002). The domains it takes are the site's policy and stay on the server, which
/// checks them; the rest is public.
/// </summary>
public sealed class EmailFieldSettings
{
    /// <summary>Whether the visitor types the address twice.</summary>
    [FormBuilderSetting("#jfbFieldSettings_emailConfirm", Public = true)]
    public bool Confirm { get; init; }

    /// <summary>The label of the second input; the type's own text by default.</summary>
    [StringLength(200)]
    [FormBuilderSetting("#jfbFieldSettings_emailConfirmLabel", Order = 1, Public = true)]
    public LocalizedText? ConfirmLabel { get; init; }

    /// <summary>The domains an address may or may not have; any domain without them.</summary>
    [FormBuilderSetting("#jfbFieldSettings_emailDomains", Group = FieldSettingGroup.Validation)]
    public EmailDomains? Domains { get; init; }
}

/// <summary>The domains an email field takes (ADR-0002).</summary>
public sealed class EmailDomains
{
    /// <summary>Whether the list allows or blocks its domains.</summary>
    [FormBuilderSetting("#jfbFieldSettings_emailDomainsMode")]
    public EmailDomainMode Mode { get; init; }

    /// <summary>The domains, such as <c>example.com</c>.</summary>
    [StringLength(253)]
    [FormBuilderSetting("#jfbFieldSettings_emailDomainsList", Order = 1)]
    public IReadOnlyList<string> List { get; init; } = [];
}

/// <summary>What an email field does with its list of domains.</summary>
public enum EmailDomainMode
{
    /// <summary>Any domain (<c>any</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_emailDomainModeAny")]
    Any,

    /// <summary>Only the listed domains (<c>allow</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_emailDomainModeAllow")]
    Allow,

    /// <summary>Any domain but the listed ones (<c>block</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_emailDomainModeBlock")]
    Block,
}

/// <summary>A phone number (<c>phone</c>, section 4.3.2).</summary>
public sealed class PhoneFieldType() : FormBuilderFieldTypeBase<PhoneFieldSettings>(new FieldTypeDeclaration
{
    Alias = "phone",
    Name = "#jfbFieldTypes_phone",
    Icon = "icon-phone",
    PaletteGroup = FieldPaletteGroup.Text,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Text,
    Features = BuiltInFieldTypes.TextInput,
    Messages = BuiltInFieldTypes.Readable,
})
{
    /// <summary>The longest number a phone field takes, with room for spaces, brackets and an extension.</summary>
    public const int LongestAnswer = 64;

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(PhoneFieldSettings settings, FormField field) => new(LongestAnswer, 1);

    /// <inheritdoc />
    protected override void CheckSettings(PhoneFieldSettings settings, FieldSettingsCheck check)
    {
        if (settings.DefaultCountry is { } country)
        {
            BuiltInChecks.IsCountry(check, "defaultCountry", country);
        }
    }
}

/// <summary>The settings of a phone number (ADR-0002).</summary>
public sealed class PhoneFieldSettings
{
    /// <summary>The country a number without a country code belongs to, as an ISO 3166 alpha-2 code; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_phoneDefaultCountry", Public = true)]
    public string? DefaultCountry { get; init; }
}

/// <summary>A web address (<c>url</c>, section 4.3.2).</summary>
public sealed class UrlFieldType() : FormBuilderFieldTypeBase<UrlFieldSettings>(new FieldTypeDeclaration
{
    Alias = "url",
    Name = "#jfbFieldTypes_url",
    Icon = "icon-link",
    PaletteGroup = FieldPaletteGroup.Text,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Text,
    Features = BuiltInFieldTypes.TextInput,
    Messages = BuiltInFieldTypes.Readable,
})
{
    /// <summary>The longest web address a field takes, as long as browsers and servers reliably carry one.</summary>
    public const int LongestAnswer = 2048;

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(UrlFieldSettings settings, FormField field) => new(LongestAnswer, 1);
}

/// <summary>The settings of a web address (ADR-0002).</summary>
public sealed class UrlFieldSettings
{
    /// <summary>The schemes an address may have.</summary>
    [FormBuilderSetting("#jfbFieldSettings_urlSchemes", Group = FieldSettingGroup.Validation, Public = true)]
    public IReadOnlyList<UrlScheme> Schemes { get; init; } = [UrlScheme.Https];
}

/// <summary>A scheme a web address field takes.</summary>
public enum UrlScheme
{
    /// <summary><c>https</c>.</summary>
    [FormBuilderChoice("#jfbFieldChoices_urlSchemeHttps")]
    Https,

    /// <summary><c>http</c>.</summary>
    [FormBuilderChoice("#jfbFieldChoices_urlSchemeHttp")]
    Http,
}
