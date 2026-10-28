using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The host's limits for uploaded files (section 4.8, ADR-0087), as a field type's settings check sees them
/// (<see cref="FieldSettingsCheck.Host"/>): the most bytes any file may have, and the extensions no field takes.
/// </summary>
public sealed class FieldHostLimits
{
    /// <summary>The limits of a host that sets none.</summary>
    public static FieldHostLimits Default { get; } = new(FormBuilderFilesOptions.DefaultMaxFileBytes, FormBuilderFilesOptions.DefaultBlockedExtensions);

    /// <summary>Limits of a host.</summary>
    /// <param name="maxFileBytes">The most bytes any file may have, 1 or more.</param>
    /// <param name="blockedExtensions">The extensions no field takes, in lowercase without the point.</param>
    public FieldHostLimits(long maxFileBytes, IEnumerable<string> blockedExtensions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentNullException.ThrowIfNull(blockedExtensions);
        MaxFileBytes = maxFileBytes;
        BlockedExtensions = blockedExtensions.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The most bytes any file may have (<c>Files:MaxFileBytes</c>): a field's own limit is held to it.</summary>
    public long MaxFileBytes { get; }

    /// <summary>The extensions no field takes (<c>Files:BlockedExtensions</c>), in lowercase without the point.</summary>
    public IReadOnlySet<string> BlockedExtensions { get; }

    /// <summary>The limits a host's configuration gives: its blocked extensions, or the defaults while it lists none.</summary>
    internal static FieldHostLimits Of(FormBuilderFilesOptions files) =>
        new(files.MaxFileBytes, files.BlockedExtensions.Count == 0 ? FormBuilderFilesOptions.DefaultBlockedExtensions : files.BlockedExtensions);
}
