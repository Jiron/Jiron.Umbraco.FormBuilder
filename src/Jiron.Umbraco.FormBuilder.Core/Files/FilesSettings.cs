using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The files settings (ADR-0008, ADR-0218), the same for every site, on Settings > Files: the types of file the site takes
/// and the largest file any field may take. The upload holds each file to them, and the form check warns about a file
/// field that asks for more. The host configuration sets what no setting can widen: <c>Files:MaxFileBytes</c> caps the
/// size, and <c>Files:BlockedExtensions</c> refuses extensions whatever their type.
/// </summary>
internal sealed record FilesSettings : ISettingsSection
{
    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Files;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <summary>
    /// The groups of file types the site takes, all by default: a file field may pick only from these, and a file of a
    /// group left out is refused, whatever the field allows. An extension a field names that belongs to no group stays
    /// the field's to allow, and the host's blocked extensions refuse it still.
    /// </summary>
    [SettingsField("types", Editor = "fileTypes")]
    public IReadOnlyList<FileTypeGroup> AllowedTypes { get; init; } = [FileTypeGroup.Images, FileTypeGroup.Pdf, FileTypeGroup.Office, FileTypeGroup.Text, FileTypeGroup.Archives];

    /// <summary>The most bytes any file may have; null for the host's limit (<c>Files:MaxFileBytes</c>), which caps it.</summary>
    [SettingsField("types", Order = 1, Editor = "fileSize", Unit = true)]
    [Range(typeof(long), "1", "999999999999999")]
    public long? MaxFileBytes { get; init; }

    /// <summary>The most bytes a file may have here: the site's limit, or the host's without one.</summary>
    /// <param name="host">The host's limits.</param>
    public long LimitWithin(FieldHostLimits host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return Math.Min(MaxFileBytes ?? host.MaxFileBytes, host.MaxFileBytes);
    }

    /// <summary>Whether the site takes a file of a name's extension: one of a group it allows, or of no group at all.</summary>
    /// <param name="extension">The extension, in lowercase without the point.</param>
    public bool Takes(string extension) => FileFieldType.GroupOf(extension) is not { } group || AllowedTypes.Contains(group);
}

/// <summary>Checks the files settings (ADR-0008): at least one type, each once, and a size of at least one byte.</summary>
internal sealed class FilesSettingsValidator : ISettingsValidator<FilesSettings>
{
    /// <inheritdoc />
    public void Validate(FilesSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        if (section.AllowedTypes.Count == 0)
        {
            errors.Add("allowedTypes", SettingsErrors.Range, "Allow at least one type of file; a form without file fields takes none anyway.");
        }
        else if (section.AllowedTypes.Distinct().Count() != section.AllowedTypes.Count || section.AllowedTypes.Any(group => !Enum.IsDefined(group)))
        {
            errors.Add("allowedTypes", SettingsErrors.Range, "Names each type once, of the known types.");
        }

        if (section.MaxFileBytes is < 1 or > FormBuilderFilesOptions.HighestMaxFileBytes)
        {
            errors.Add("maxFileBytes", SettingsErrors.Range, string.Create(CultureInfo.InvariantCulture, $"Must be from 1 to {FormBuilderFilesOptions.HighestMaxFileBytes} bytes."));
        }
    }
}

/// <summary>
/// The host's cap on the largest file (ADR-0008, ADR-0218): a size above <c>Files:MaxFileBytes</c> is refused, and one
/// stored before the host lowered its cap reads as the cap.
/// </summary>
/// <param name="options">The host configuration.</param>
internal sealed class FilesHostLimitRule(IOptions<FormBuilderOptions> options) : ISettingsHostRule<FilesSettings>
{
    /// <summary>The configuration key behind the rule.</summary>
    public const string ConfigurationKey = FormBuilderFilesOptions.SectionName + ":MaxFileBytes";

    /// <inheritdoc />
    public FilesSettings Apply(FilesSettings section, SettingsLocks locks)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(locks);
        var cap = options.Value.Files.MaxFileBytes;
        locks.Add("maxFileBytes", SettingsLockKind.Clamped, ConfigurationKey);
        return section.MaxFileBytes > cap ? section with { MaxFileBytes = cap } : section;
    }
}
