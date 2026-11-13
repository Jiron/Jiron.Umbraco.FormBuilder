using System.Collections.Frozen;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
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
    PersonalData = true,
})
{
    /// <summary>The longest part of a name a field takes.</summary>
    public const int LongestPart = 200;

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(NameFieldSettings settings, FormField field) => new(LongestPart, Enum.GetValues<NamePart>().Length);

    /// <inheritdoc />
    /// <remarks>The parts the field asks for, each by its name (<c>first</c>), become one value in the order of <see cref="NamePart"/>.</remarks>
    protected override void Read(NameFieldSettings settings, FieldReading reading) =>
        BuiltInAnswers.ReadParts(reading, settings.Parts.Distinct().Order().Select(BuiltInChecks.Name));

    /// <inheritdoc />
    /// <remarks>
    /// The required parts the field asks for have a text; with none listed, a required name needs every part it asks for.
    /// </remarks>
    protected override void Validate(NameFieldSettings settings, FieldValidation validation)
    {
        var asked = settings.Parts.Select(BuiltInChecks.Name).ToList();
        BuiltInValidation.RequiredParts(validation, asked, [.. settings.RequiredParts.Select(BuiltInChecks.Name)], asked);
    }

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

    /// <inheritdoc />
    /// <remarks>The parts in their order apart by spaces, such as <c>Dr Anna Muster</c>.</remarks>
    protected override void Display(NameFieldSettings settings, FieldDisplay display)
    {
        foreach (var value in display.Values)
        {
            display.Add(FieldPartsJson.Read(value.Text) is { } parts ? FieldPartsJson.Joined(parts) : value.Text);
        }
    }
}

/// <summary>The settings of a name (ADR-0002).</summary>
public sealed class NameFieldSettings
{
    /// <summary>The parts the field asks for.</summary>
    [FormBuilderSetting("#jfbFieldSettings_nameParts", Public = true)]
    public IReadOnlyList<NamePart> Parts { get; init; } = [NamePart.First, NamePart.Last];

    /// <summary>The parts a name needs once any part is given; with none listed, a required name needs every part the field asks for.</summary>
    [FormBuilderSetting("#jfbFieldSettings_nameRequiredParts", Group = FieldSettingGroup.Validation, Public = true)]
    public IReadOnlyList<NamePart> RequiredParts { get; init; } = [];
}

/// <summary>A part of a name.</summary>
public enum NamePart
{
    /// <summary>A title, such as Dr (<c>title</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_namePartTitle")]
    Title,

    /// <summary>The first name (<c>first</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_namePartFirst")]
    First,

    /// <summary>A middle name (<c>middle</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_namePartMiddle")]
    Middle,

    /// <summary>The last name (<c>last</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_namePartLast")]
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
    PersonalData = true,
})
{
    /// <summary>The parts every address asks for, which a required address needs when it lists none.</summary>
    internal static readonly ImmutableArray<AddressPart> Always = [AddressPart.Street, AddressPart.Postcode, AddressPart.City];

    /// <summary>The longest part of an address a field takes.</summary>
    public const int LongestPart = 200;

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(AddressFieldSettings settings, FormField field) => new(LongestPart, Enum.GetValues<AddressPart>().Length);

    /// <inheritdoc />
    /// <remarks>
    /// The street, the postcode, the city and the parts the field asks for, each by its name (<c>street2</c>), become one
    /// value in the order of <see cref="AddressPart"/>.
    /// </remarks>
    protected override void Read(AddressFieldSettings settings, FieldReading reading) =>
        BuiltInAnswers.ReadParts(reading, Always.Concat(settings.Parts).Distinct().Order().Select(BuiltInChecks.Name));

    /// <inheritdoc />
    /// <remarks>
    /// The required parts the field asks for have a text; with none listed, a required address needs the street, the
    /// postcode and the city.
    /// </remarks>
    protected override void Validate(AddressFieldSettings settings, FieldValidation validation) =>
        BuiltInValidation.RequiredParts(
            validation,
            Always.Concat(settings.Parts).Select(BuiltInChecks.Name),
            [.. settings.RequiredParts.Select(BuiltInChecks.Name)],
            Always.Select(BuiltInChecks.Name));

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

    /// <inheritdoc />
    /// <remarks>
    /// The parts on one line apart by commas, the postcode and the city together, and a country given by its code by its
    /// name in the culture: <c>Bahnhofstrasse 1, 8001 Zurich, Schweiz</c>.
    /// </remarks>
    protected override void Display(AddressFieldSettings settings, FieldDisplay display)
    {
        foreach (var value in display.Values)
        {
            if (FieldPartsJson.Read(value.Text) is not { } parts)
            {
                display.Add(value.Text);
                continue;
            }

            var texts = parts.ToDictionary(part => part.Key, part => part.Value, StringComparer.Ordinal);
            string Of(AddressPart part) => texts.GetValueOrDefault(BuiltInChecks.Name(part)) ?? string.Empty;
            var country = Of(AddressPart.Country);
            var lines = new[]
            {
                Of(AddressPart.Street),
                Of(AddressPart.Street2),
                string.Join(' ', new[] { Of(AddressPart.Postcode), Of(AddressPart.City) }.Where(text => text.Length > 0)),
                Of(AddressPart.Region),
                BuiltInChecks.CountryCodes.Contains(country) ? display.CountryName(country) : country,
            };
            display.Add(string.Join(", ", lines.Where(text => text.Length > 0)));
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

    /// <summary>The parts an address needs once any part is given; with none listed, a required address needs the street, the postcode and the city.</summary>
    [FormBuilderSetting("#jfbFieldSettings_addressRequiredParts", Group = FieldSettingGroup.Validation, Public = true)]
    public IReadOnlyList<AddressPart> RequiredParts { get; init; } = [];
}

/// <summary>A part of an address.</summary>
public enum AddressPart
{
    /// <summary>The street and number (<c>street</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_addressPartStreet")]
    Street,

    /// <summary>A second street line (<c>street2</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_addressPartStreet2")]
    Street2,

    /// <summary>The postcode (<c>postcode</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_addressPartPostcode")]
    Postcode,

    /// <summary>The city (<c>city</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_addressPartCity")]
    City,

    /// <summary>The region, state or canton (<c>region</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_addressPartRegion")]
    Region,

    /// <summary>The country (<c>country</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_addressPartCountry")]
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
    /// <summary>The longest upload token a file field takes (ADR-0006), with room to spare.</summary>
    public const int LongestToken = FieldAnswerDefaults.TokenLength;

    /// <summary>The most files a file field takes.</summary>
    public const int MostFiles = 20;

    // The extensions of each group of file types (ADR-0062). SVG is no image here, because it can carry script.
    private static readonly FrozenDictionary<FileTypeGroup, ImmutableArray<string>> GroupExtensions = new Dictionary<FileTypeGroup, ImmutableArray<string>>
    {
        [FileTypeGroup.Images] = ["jpg", "jpeg", "png", "gif", "webp", "avif", "heic", "heif"],
        [FileTypeGroup.Pdf] = ["pdf"],
        [FileTypeGroup.Office] = ["doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "rtf"],
        [FileTypeGroup.Text] = ["txt", "csv"],
        [FileTypeGroup.Archives] = ["zip", "7z", "rar", "tar", "gz"],
    }.ToFrozenDictionary();

    // The group of each extension a group holds.
    private static readonly FrozenDictionary<string, FileTypeGroup> ExtensionGroups = GroupExtensions
        .SelectMany(pair => pair.Value.Select(extension => KeyValuePair.Create(extension, pair.Key)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The extensions of a group of file types, in lowercase and without the point.</summary>
    /// <param name="group">The group.</param>
    public static ImmutableArray<string> ExtensionsOf(FileTypeGroup group) => GroupExtensions[group];

    /// <summary>The group of file types an extension belongs to; null for an extension of no group, such as <c>dwg</c>.</summary>
    /// <param name="extension">The extension, in lowercase without the point.</param>
    public static FileTypeGroup? GroupOf(string extension) => ExtensionGroups.TryGetValue(extension, out var group) ? group : null;

    /// <summary>A file name's extension: the text after its last point, in ASCII lowercase; empty without a point.</summary>
    /// <param name="name">The file's name.</param>
    public static string ExtensionOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var point = name.LastIndexOf('.');
        return point < 0 ? "" : BuiltInValidation.AsciiLower(name[(point + 1)..]);
    }

    /// <inheritdoc />
    /// <remarks>Up to the most files any file field takes, so that a field's check reports too many as <c>fileCount</c>.</remarks>
    protected override FieldAnswerLimits AnswerLimits(FileFieldSettings settings, FormField field) => new(LongestToken, MostFiles);

    /// <inheritdoc />
    /// <remarks>
    /// At most <c>maxFiles</c> files; each file's extension one of its type groups' or its own extensions, or of images
    /// and PDFs when it lists neither; each file at most <c>maxBytes</c>. The host's own limits come with the uploads.
    /// </remarks>
    protected override void Validate(FileFieldSettings settings, FieldValidation validation)
    {
        if (validation.Values.Length > settings.MaxFiles)
        {
            validation.Fail(FieldMessageKinds.FileCount);
        }

        var taken = Taken(settings);
        if (validation.Files.Any(file => !taken.Contains(ExtensionOf(file.Name))))
        {
            validation.Fail(FieldMessageKinds.FileType);
        }

        if (settings.MaxBytes is { } most && validation.Files.Any(file => file.Size > most))
        {
            validation.Fail(FieldMessageKinds.FileSize);
        }
    }

    /// <inheritdoc />
    /// <remarks>The field's <c>maxBytes</c>; the host's limit without one.</remarks>
    protected override long? UploadLimit(FileFieldSettings settings, FormField field) => settings.MaxBytes;

    /// <inheritdoc />
    /// <remarks>An extension the field takes, the same list its check of a submission holds the files to; otherwise <c>fileType</c>.</remarks>
    protected override void CheckUpload(FileFieldSettings settings, FieldUploadCheck check)
    {
        if (!Taken(settings).Contains(ExtensionOf(check.Name)))
        {
            check.Fail(FieldMessageKinds.FileType);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each extension lowercase letters and digits that the host does not block, and <c>maxBytes</c> no more than the
    /// host's limit (ADR-0087); a type group's extensions that the host blocks are left out where the uploads are checked.
    /// </remarks>
    protected override void CheckSettings(FileFieldSettings settings, FieldSettingsCheck check)
    {
        for (var i = 0; i < settings.Extensions.Count; i++)
        {
            var extension = settings.Extensions[i];
            if (extension.Length == 0 || !extension.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            {
                check.Invalid(BuiltInChecks.At("extensions", i), $"An extension is lowercase letters and digits without the point, such as dwg, not '{extension}'.");
            }
            else if (check.Host.BlockedExtensions.Contains(extension))
            {
                check.Invalid(BuiltInChecks.At("extensions", i), $"This site blocks the extension '{extension}' for every field (Files:BlockedExtensions).");
            }
        }

        if (settings.MaxBytes > check.Host.MaxFileBytes)
        {
            check.Invalid("maxBytes", string.Create(CultureInfo.InvariantCulture, $"A file on this site has at most {check.Host.MaxFileBytes:N0} bytes (Files:MaxFileBytes)."));
        }
    }

    /// <summary>
    /// The extensions a field takes, each once in the order of its type groups and then its own: those of images and PDFs
    /// when it lists neither. The page offers the same in the file input's <c>accept</c> (ADR-0097).
    /// </summary>
    /// <param name="settings">The field's settings.</param>
    internal static IReadOnlyList<string> Extensions(FileFieldSettings settings)
    {
        IEnumerable<FileTypeGroup> groups = settings.Types.Count == 0 && settings.Extensions.Count == 0 ? [FileTypeGroup.Images, FileTypeGroup.Pdf] : settings.Types;
        return [.. groups.SelectMany(group => ExtensionsOf(group)).Concat(settings.Extensions).Distinct(StringComparer.Ordinal)];
    }

    // The extensions a field takes, to look one up.
    private static HashSet<string> Taken(FileFieldSettings settings) => Extensions(settings).ToHashSet(StringComparer.Ordinal);
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

    /// <summary>
    /// The most bytes a file has; the site's limit by default (<c>Files:MaxFileBytes</c>). The designer shows it in megabytes,
    /// as the global settings show the site's, and stores bytes.
    /// </summary>
    [Range(typeof(long), "1", "999999999999999")]
    [FormBuilderSetting("#jfbFieldSettings_fileMaxBytes", Description = "#jfbFieldSettings_fileMaxBytesHelp", Editor = "fileSize", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public long? MaxBytes { get; init; }

    /// <summary>The most files a visitor uploads.</summary>
    [Range(1, FileFieldType.MostFiles)]
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
    [FormBuilderChoice("#jfbFieldChoices_fileTypeImages")]
    Images,

    /// <summary>PDF documents (<c>pdf</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_fileTypePdf")]
    Pdf,

    /// <summary>Office documents (<c>office</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_fileTypeOffice")]
    Office,

    /// <summary>Plain text (<c>text</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_fileTypeText")]
    Text,

    /// <summary>Archives (<c>archives</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_fileTypeArchives")]
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
    PersonalData = true,
})
{
    /// <summary>The most bytes a signature's PNG has (ADR-0071), whatever more the host allows.</summary>
    public const long MaxBytes = 1_048_576;

    /// <summary>
    /// The part that holds the name a visitor typed instead of drawing (<c>&lt;alias&gt;.typed</c>), which a page without
    /// JavaScript posts (ADR-0097).
    /// </summary>
    public const string TypedPart = "typed";

    /// <summary>The widest pad, in pixels.</summary>
    public const int WidestPad = 1200;

    /// <summary>The tallest pad, in pixels.</summary>
    public const int TallestPad = 600;

    /// <summary>
    /// How many times the pad's size in pixels a signature's PNG may be (ADR-0071): the runtime draws at the device's
    /// pixel ratio, at most 3.
    /// </summary>
    public const int MostPixelRatio = 3;

    /// <inheritdoc />
    /// <remarks>The token of one upload: the pad's PNG, or the typed name drawn as one.</remarks>
    protected override FieldAnswerLimits AnswerLimits(SignatureFieldSettings settings, FormField field) => new(FileFieldType.LongestToken, 1);

    /// <inheritdoc />
    /// <remarks><see cref="MaxBytes"/>.</remarks>
    protected override long? UploadLimit(SignatureFieldSettings settings, FormField field) => MaxBytes;

    /// <inheritdoc />
    /// <remarks>
    /// A PNG's signature and a well-formed <c>IHDR</c> chunk, at most <see cref="MostPixelRatio"/> times the pad's width
    /// and height in pixels, or the widest and the tallest pad's without them; otherwise <c>invalid</c>.
    /// </remarks>
    protected override void CheckUpload(SignatureFieldSettings settings, FieldUploadCheck check)
    {
        if (PngHeader.Read(check.Head.Span) is not { } size
            || size.Width > MostPixelRatio * (settings.Width ?? WidestPad)
            || size.Height > MostPixelRatio * (settings.Height ?? TallestPad))
        {
            check.Fail(FieldMessageKinds.Invalid);
        }
    }
}

/// <summary>The settings of a signature (ADR-0002): the pad's size in pixels, the theme's size by default.</summary>
public sealed class SignatureFieldSettings
{
    /// <summary>The pad's width.</summary>
    [Range(200, SignatureFieldType.WidestPad)]
    [FormBuilderSetting("#jfbFieldSettings_signatureWidth", Group = FieldSettingGroup.Appearance, Public = true)]
    public int? Width { get; init; }

    /// <summary>The pad's height.</summary>
    [Range(100, SignatureFieldType.TallestPad)]
    [FormBuilderSetting("#jfbFieldSettings_signatureHeight", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public int? Height { get; init; }
}
