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
});

/// <summary>The settings of a one-line text (ADR-0002).</summary>
public sealed class TextFieldSettings
{
    /// <summary>The fewest characters an answer has.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textMinLength", Group = FieldSettingGroup.Validation, Public = true)]
    public int MinLength { get; init; }

    /// <summary>The most characters an answer has.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textMaxLength", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int MaxLength { get; init; } = 255;

    /// <summary>A text shown before the input, such as a currency.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textPrefix", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? Prefix { get; init; }

    /// <summary>A text shown after the input, such as a unit.</summary>
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
});

/// <summary>The settings of a long text (ADR-0002).</summary>
public sealed class TextareaFieldSettings
{
    /// <summary>How many lines the input shows at first.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaRows", Group = FieldSettingGroup.Appearance, Public = true)]
    public int Rows { get; init; } = 4;

    /// <summary>Whether the input grows with its text.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaAutoGrow", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public bool AutoGrow { get; init; } = true;

    /// <summary>The fewest characters an answer has.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaMinLength", Group = FieldSettingGroup.Validation, Public = true)]
    public int MinLength { get; init; }

    /// <summary>The most characters an answer has.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaMaxLength", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int MaxLength { get; init; } = 4000;

    /// <summary>The fewest words an answer has, or null for no limit.</summary>
    [FormBuilderSetting("#jfbFieldSettings_textareaMinWords", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public int? MinWords { get; init; }

    /// <summary>The most words an answer has, or null for no limit.</summary>
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
});

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
    [FormBuilderSetting("#jfbFieldSettings_emailDomainsList", Order = 1)]
    public IReadOnlyList<string> List { get; init; } = [];
}

/// <summary>What an email field does with its list of domains.</summary>
public enum EmailDomainMode
{
    /// <summary>Any domain (<c>any</c>).</summary>
    Any,

    /// <summary>Only the listed domains (<c>allow</c>).</summary>
    Allow,

    /// <summary>Any domain but the listed ones (<c>block</c>).</summary>
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
});

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
});

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
    Https,

    /// <summary><c>http</c>.</summary>
    Http,
}
