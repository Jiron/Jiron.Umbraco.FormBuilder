using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>A repeatable group of child fields (<c>repeater</c>, section 4.3.2), whose values are stored per row.</summary>
public sealed class RepeaterFieldType() : FormBuilderFieldTypeBase<RepeaterFieldSettings>(new FieldTypeDeclaration
{
    Alias = "repeater",
    Name = "#jfbFieldTypes_repeater",
    Icon = "icon-repeat",
    PaletteGroup = FieldPaletteGroup.Structure,
    HoldsValue = true,
    Storage = FieldStorageKind.None,
    Features = BuiltInFieldTypes.Shown | FieldFeatures.ChildFields,
    Messages = FieldMessageKinds.RowCount,
})
{
    /// <summary>The most rows a repeatable group has.</summary>
    public const int MostRows = 100;

    /// <inheritdoc />
    protected override void CheckSettings(RepeaterFieldSettings settings, FieldSettingsCheck check) =>
        BuiltInChecks.NotAbove(check, "minRows", settings.MinRows, "maxRows", settings.MaxRows);

    /// <inheritdoc />
    /// <remarks>At least <c>minRows</c> and at most <c>maxRows</c> rows; otherwise <c>rowCount</c>.</remarks>
    protected override void ValidateRows(RepeaterFieldSettings settings, FieldRowsValidation validation)
    {
        if (validation.Rows < settings.MinRows || validation.Rows > settings.MaxRows)
        {
            validation.Fail(FieldMessageKinds.RowCount);
        }
    }
}

/// <summary>The settings of a repeatable group (ADR-0002).</summary>
public sealed class RepeaterFieldSettings
{
    /// <summary>The fewest rows an answer has.</summary>
    [Range(0, RepeaterFieldType.MostRows)]
    [FormBuilderSetting("#jfbFieldSettings_repeaterMinRows", Group = FieldSettingGroup.Validation, Public = true)]
    public int MinRows { get; init; }

    /// <summary>The most rows an answer has.</summary>
    [Range(1, RepeaterFieldType.MostRows)]
    [FormBuilderSetting("#jfbFieldSettings_repeaterMaxRows", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int MaxRows { get; init; } = 10;

    /// <summary>The label of the button that adds a row; the type's own text by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_repeaterAddLabel", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? AddLabel { get; init; }

    /// <summary>The label of the button that removes a row; the type's own text by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_repeaterRemoveLabel", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public LocalizedText? RemoveLabel { get; init; }
}

/// <summary>
/// A hidden value (<c>hidden</c>, section 4.3.2): its default value or its query prefill. Values from sources only the
/// server has (the member, the context) are resolved when the entry is submitted and never reach the page (ADR-0014).
/// </summary>
public sealed class HiddenFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
{
    Alias = "hidden",
    Name = "#jfbFieldTypes_hidden",
    Icon = "icon-readonly",
    PaletteGroup = FieldPaletteGroup.Structure,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Text,
    ServerOnlyDefault = true,
    Features = FieldFeatures.Label | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.VisibilityRules,
});

/// <summary>A heading (<c>heading</c>, section 4.3.2), which holds no value.</summary>
public sealed class HeadingFieldType() : FormBuilderFieldTypeBase<HeadingFieldSettings>(new FieldTypeDeclaration
{
    Alias = "heading",
    Name = "#jfbFieldTypes_heading",
    Icon = "icon-heading",
    PaletteGroup = FieldPaletteGroup.Content,
    Features = FieldFeatures.VisibilityRules,
});

/// <summary>The settings of a heading (ADR-0002).</summary>
public sealed class HeadingFieldSettings
{
    /// <summary>The heading's text.</summary>
    [Required]
    [StringLength(300)]
    [FormBuilderSetting("#jfbFieldSettings_headingText", SupportsMergeTags = true, Public = true)]
    public LocalizedText? Text { get; init; }

    /// <summary>Its level, 2 to 4.</summary>
    [Range(2, 4)]
    [FormBuilderSetting("#jfbFieldSettings_headingLevel", Group = FieldSettingGroup.Appearance, Public = true)]
    public int Level { get; init; } = 2;
}

/// <summary>A block of rich text (<c>paragraph</c>, section 4.3.2), sanitised when shown; it holds no value.</summary>
public sealed class ParagraphFieldType() : FormBuilderFieldTypeBase<ParagraphFieldSettings>(new FieldTypeDeclaration
{
    Alias = "paragraph",
    Name = "#jfbFieldTypes_paragraph",
    Icon = "icon-paragraph",
    PaletteGroup = FieldPaletteGroup.Content,
    Features = FieldFeatures.VisibilityRules,
});

/// <summary>The settings of a text block (ADR-0002).</summary>
public sealed class ParagraphFieldSettings
{
    /// <summary>The text.</summary>
    [Required]
    [FormBuilderSetting("#jfbFieldSettings_paragraphHtml", SupportsMergeTags = true, Public = true)]
    public LocalizedHtml? Html { get; init; }
}

/// <summary>A divider (<c>divider</c>, section 4.3.2), which holds no value and has no settings.</summary>
public sealed class DividerFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
{
    Alias = "divider",
    Name = "#jfbFieldTypes_divider",
    Icon = "icon-width",
    PaletteGroup = FieldPaletteGroup.Content,
    Features = FieldFeatures.VisibilityRules,
});

/// <summary>An image from the media library (<c>image</c>, section 4.3.2), which holds no value.</summary>
public sealed class ImageFieldType() : FormBuilderFieldTypeBase<ImageFieldSettings>(new FieldTypeDeclaration
{
    Alias = "image",
    Name = "#jfbFieldTypes_image",
    Icon = "icon-picture",
    PaletteGroup = FieldPaletteGroup.Content,
    Features = FieldFeatures.VisibilityRules,
});

/// <summary>The settings of an image (ADR-0002).</summary>
public sealed class ImageFieldSettings
{
    /// <summary>The media item.</summary>
    [Required]
    [FormBuilderSetting("#jfbFieldSettings_imageMedia", Editor = "mediaPicker", Public = true)]
    public Guid? Media { get; init; }

    /// <summary>The alternative text, which publishing requires.</summary>
    [Required]
    [StringLength(300)]
    [FormBuilderSetting("#jfbFieldSettings_imageAlt", Order = 1, Public = true)]
    public LocalizedText? Alt { get; init; }

    /// <summary>How large the image shows.</summary>
    [FormBuilderSetting("#jfbFieldSettings_imageSize", Group = FieldSettingGroup.Appearance, Public = true)]
    public ImageSize Size { get; init; } = ImageSize.Medium;
}

/// <summary>How large an image field shows its image.</summary>
public enum ImageSize
{
    /// <summary>Small (<c>small</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_imageSizeSmall")]
    Small,

    /// <summary>Medium (<c>medium</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_imageSizeMedium")]
    Medium,

    /// <summary>Large (<c>large</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_imageSizeLarge")]
    Large,

    /// <summary>The width of the form (<c>full</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_imageSizeFull")]
    Full,
}

/// <summary>
/// Where the form's chosen captcha renders (<c>captcha</c>, section 4.3.2); without it, the captcha renders before the
/// submit button (section 4.10). A form has at most one, never inside a repeatable group.
/// </summary>
public sealed class CaptchaFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
{
    Alias = "captcha",
    Name = "#jfbFieldTypes_captcha",
    Icon = "icon-shield",
    PaletteGroup = FieldPaletteGroup.Protection,
    OnePerForm = true,
});
