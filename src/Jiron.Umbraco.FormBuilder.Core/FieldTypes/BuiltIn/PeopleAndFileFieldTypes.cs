using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>A person's name in parts (<c>name</c>, section 4.3.2), with the autocomplete token of each part.</summary>
public sealed class NameFieldType() : FormBuilderFieldTypeBase<NameFieldSettings>(new FieldTypeDeclaration
{
    Alias = "name",
    Name = "#jfbFieldTypes_name",
    Icon = "icon-user",
    PaletteGroup = FieldPaletteGroup.People,
    HoldsValue = true,
    Storage = FieldStorageKind.Json,
    Features = BuiltInFieldTypes.Input | FieldFeatures.Prefill,
    Messages = BuiltInFieldTypes.Readable,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(NameFieldSettings settings, FieldSettingsCheck check)
    {
        for (var i = 0; i < settings.RequiredParts.Count; i++)
        {
            if (!settings.Parts.Contains(settings.RequiredParts[i]))
            {
                check.Invalid(BuiltInChecks.At("requiredParts", i), $"The part '{BuiltInChecks.Name(settings.RequiredParts[i])}' is required, but 'parts' does not ask for it.");
            }
        }
    }
}

/// <summary>The settings of a name (ADR-0002).</summary>
public sealed class NameFieldSettings
{
    /// <summary>The parts the field asks for.</summary>
    [FormBuilderSetting("#jfbFieldSettings_nameParts", Public = true)]
    public IReadOnlyList<NamePart> Parts { get; init; } = [NamePart.First, NamePart.Last];

    /// <summary>The parts a required name has; with none listed, every part the field asks for.</summary>
    [FormBuilderSetting("#jfbFieldSettings_nameRequiredParts", Group = FieldSettingGroup.Validation, Public = true)]
    public IReadOnlyList<NamePart> RequiredParts { get; init; } = [];
}

/// <summary>A part of a name.</summary>
public enum NamePart
{
    /// <summary>A title, such as Dr (<c>title</c>).</summary>
    Title,

    /// <summary>The first name (<c>first</c>).</summary>
    First,

    /// <summary>A middle name (<c>middle</c>).</summary>
    Middle,

    /// <summary>The last name (<c>last</c>).</summary>
    Last,
}

/// <summary>A postal address in parts (<c>address</c>, section 4.3.2), with the autocomplete token of each part.</summary>
public sealed class AddressFieldType() : FormBuilderFieldTypeBase<AddressFieldSettings>(new FieldTypeDeclaration
{
    Alias = "address",
    Name = "#jfbFieldTypes_address",
    Icon = "icon-addressbook",
    PaletteGroup = FieldPaletteGroup.People,
    HoldsValue = true,
    Storage = FieldStorageKind.Json,
    Features = BuiltInFieldTypes.Input | FieldFeatures.Prefill,
    Messages = BuiltInFieldTypes.Readable,
})
{
    private static readonly ImmutableArray<AddressPart> Always = [AddressPart.Street, AddressPart.Postcode, AddressPart.City];

    /// <inheritdoc />
    protected override void CheckSettings(AddressFieldSettings settings, FieldSettingsCheck check)
    {
        for (var i = 0; i < settings.Parts.Count; i++)
        {
            if (Always.Contains(settings.Parts[i]))
            {
                check.Invalid(BuiltInChecks.At("parts", i), $"The part '{BuiltInChecks.Name(settings.Parts[i])}' is always asked for, so 'parts' leaves it out.");
            }
        }

        for (var i = 0; i < settings.RequiredParts.Count; i++)
        {
            var part = settings.RequiredParts[i];
            if (!Always.Contains(part) && !settings.Parts.Contains(part))
            {
                check.Invalid(BuiltInChecks.At("requiredParts", i), $"The part '{BuiltInChecks.Name(part)}' is required, but 'parts' does not ask for it.");
            }
        }
    }
}

/// <summary>
/// The settings of an address (ADR-0002): the street, the postcode and the city are always asked for; the second
/// street line, the region and the country only when the field says so.
/// </summary>
public sealed class AddressFieldSettings
{
    /// <summary>The parts the field asks for besides the street, the postcode and the city.</summary>
    [FormBuilderSetting("#jfbFieldSettings_addressParts", Public = true)]
    public IReadOnlyList<AddressPart> Parts { get; init; } = [];

    /// <summary>The parts a required address has; with none listed, the street, the postcode and the city.</summary>
    [FormBuilderSetting("#jfbFieldSettings_addressRequiredParts", Group = FieldSettingGroup.Validation, Public = true)]
    public IReadOnlyList<AddressPart> RequiredParts { get; init; } = [];
}

/// <summary>A part of an address.</summary>
public enum AddressPart
{
    /// <summary>The street and number (<c>street</c>).</summary>
    Street,

    /// <summary>A second street line (<c>street2</c>).</summary>
    Street2,

    /// <summary>The postcode (<c>postcode</c>).</summary>
    Postcode,

    /// <summary>The city (<c>city</c>).</summary>
    City,

    /// <summary>The region, state or canton (<c>region</c>).</summary>
    Region,

    /// <summary>The country (<c>country</c>).</summary>
    Country,
}

/// <summary>File uploads (<c>file</c>, section 4.3.2), stored outside the web root through the file store (ADR-0006).</summary>
public sealed class FileFieldType() : FormBuilderFieldTypeBase<FileFieldSettings>(new FieldTypeDeclaration
{
    Alias = "file",
    Name = "#jfbFieldTypes_file",
    Icon = "icon-attachment",
    PaletteGroup = FieldPaletteGroup.Files,
    HoldsValue = true,
    Storage = FieldStorageKind.File,
    RuleKind = FieldKind.Files,
    Features = BuiltInFieldTypes.Input,
    Messages = FieldMessageKinds.Required | FieldMessageKinds.FileType | FieldMessageKinds.FileSize | FieldMessageKinds.FileCount,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(FileFieldSettings settings, FieldSettingsCheck check)
    {
        for (var i = 0; i < settings.Extensions.Count; i++)
        {
            var extension = settings.Extensions[i];
            if (extension.Length == 0 || !extension.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            {
                check.Invalid(BuiltInChecks.At("extensions", i), $"An extension is lowercase letters and digits without the point, such as dwg, not '{extension}'.");
            }
        }
    }
}

/// <summary>The settings of file uploads (ADR-0002).</summary>
public sealed class FileFieldSettings
{
    /// <summary>The groups of file types a visitor may upload; with none listed and no extensions, images and PDFs.</summary>
    [FormBuilderSetting("#jfbFieldSettings_fileTypes", Group = FieldSettingGroup.Validation, Public = true)]
    public IReadOnlyList<FileTypeGroup> Types { get; init; } = [];

    /// <summary>Further extensions a visitor may upload, such as <c>dwg</c>, which the site's blocked extensions still refuse.</summary>
    [StringLength(10)]
    [FormBuilderSetting("#jfbFieldSettings_fileExtensions", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>The most bytes a file has; the site's limit by default (<c>Files:MaxFileBytes</c>).</summary>
    [Range(typeof(long), "1", "999999999999999")]
    [FormBuilderSetting("#jfbFieldSettings_fileMaxBytes", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public long? MaxBytes { get; init; }

    /// <summary>The most files a visitor uploads.</summary>
    [Range(1, 20)]
    [FormBuilderSetting("#jfbFieldSettings_fileMaxFiles", Group = FieldSettingGroup.Validation, Order = 3, Public = true)]
    public int MaxFiles { get; init; } = 1;

    /// <summary>Whether files can be dropped onto the field.</summary>
    [FormBuilderSetting("#jfbFieldSettings_fileDropZone", Group = FieldSettingGroup.Appearance, Public = true)]
    public bool DropZone { get; init; } = true;

    /// <summary>Whether images show as thumbnails once chosen.</summary>
    [FormBuilderSetting("#jfbFieldSettings_fileThumbnails", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public bool Thumbnails { get; init; } = true;

    /// <summary>Whether phones offer the camera.</summary>
    [FormBuilderSetting("#jfbFieldSettings_fileCamera", Group = FieldSettingGroup.Appearance, Order = 2, Public = true)]
    public bool Camera { get; init; }
}

/// <summary>A group of file types an upload field takes.</summary>
public enum FileTypeGroup
{
    /// <summary>Images (<c>images</c>).</summary>
    Images,

    /// <summary>PDF documents (<c>pdf</c>).</summary>
    Pdf,

    /// <summary>Office documents (<c>office</c>).</summary>
    Office,

    /// <summary>Plain text (<c>text</c>).</summary>
    Text,

    /// <summary>Archives (<c>archives</c>).</summary>
    Archives,
}

/// <summary>A signature (<c>signature</c>, section 4.3.2), stored as a PNG file, with typing the name as the keyboard path.</summary>
public sealed class SignatureFieldType() : FormBuilderFieldTypeBase<SignatureFieldSettings>(new FieldTypeDeclaration
{
    Alias = "signature",
    Name = "#jfbFieldTypes_signature",
    Icon = "icon-edit",
    PaletteGroup = FieldPaletteGroup.Files,
    HoldsValue = true,
    Storage = FieldStorageKind.File,
    RuleKind = FieldKind.Files,
    Features = BuiltInFieldTypes.Input,
    Messages = BuiltInFieldTypes.Readable,
});

/// <summary>The settings of a signature (ADR-0002): the pad's size in pixels, the theme's size by default.</summary>
public sealed class SignatureFieldSettings
{
    /// <summary>The pad's width.</summary>
    [Range(200, 1200)]
    [FormBuilderSetting("#jfbFieldSettings_signatureWidth", Group = FieldSettingGroup.Appearance, Public = true)]
    public int? Width { get; init; }

    /// <summary>The pad's height.</summary>
    [Range(100, 600)]
    [FormBuilderSetting("#jfbFieldSettings_signatureHeight", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public int? Height { get; init; }
}
