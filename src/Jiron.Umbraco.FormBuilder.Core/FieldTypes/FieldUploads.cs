using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// A field's uploads through its type (ADR-0071), for the upload endpoint (6.1) and the posts without JavaScript (6.2),
/// in the order ADR-0006 gives: the limit before a byte of the file is read, the size the file is held to, and the
/// type's check of its name and first bytes before it is stored.
/// </summary>
internal static class FieldUploads
{
    /// <summary>
    /// The most bytes one file of the field may have: the type's limit, or the host's when it is smaller or the type has
    /// none.
    /// </summary>
    /// <param name="type">The field's type, which stores files.</param>
    /// <param name="field">The field.</param>
    /// <param name="hostLimit">The host's limit for any file (<c>Files:MaxFileBytes</c>, section 4.8).</param>
    /// <exception cref="ArgumentException">The type stores no files.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The host's limit is below 1 byte.</exception>
    /// <exception cref="InvalidOperationException">The type answered a limit below 1 byte.</exception>
    public static long Limit(IFormBuilderFieldType type, FormField field, long hostLimit)
    {
        StoresFiles(type, field);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostLimit, 1);
        var own = type.UploadLimit(field);
        if (own < 1)
        {
            throw new InvalidOperationException($"The field type '{type.Declaration.Alias}' ({type.GetType().FullName}) answered an upload limit below 1 byte.");
        }

        return own is { } limit && limit < hostLimit ? limit : hostLimit;
    }

    /// <summary>
    /// The error of a file of the given size: null when it keeps the field's limit (<see cref="Limit"/>); otherwise
    /// <c>fileSize</c> when the type declares that kind, and <c>invalid</c> when it does not.
    /// </summary>
    /// <exception cref="ArgumentException">The type stores no files.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is negative, or the host's limit below 1 byte.</exception>
    public static FieldMessageKinds? CheckSize(IFormBuilderFieldType type, FormField field, long size, long hostLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (size <= Limit(type, field, hostLimit))
        {
            return null;
        }

        return type.Declaration.Messages.HasFlag(FieldMessageKinds.FileSize) ? FieldMessageKinds.FileSize : FieldMessageKinds.Invalid;
    }

    /// <summary>
    /// The errors the type's check finds in a file's name and first bytes, in the order reported, each once; none when
    /// it is fine.
    /// </summary>
    /// <param name="type">The field's type, which stores files.</param>
    /// <param name="field">The field.</param>
    /// <param name="name">The file's name as the visitor's device gave it.</param>
    /// <param name="head">The file's first bytes: <see cref="FieldUploadCheck.HeadLength"/> of them, or all of a shorter file.</param>
    /// <exception cref="ArgumentException">The type stores no files, the name is empty, or the head is longer.</exception>
    public static ImmutableArray<FieldMessageKinds> Check(IFormBuilderFieldType type, FormField field, string name, ReadOnlyMemory<byte> head)
    {
        StoresFiles(type, field);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (head.Length > FieldUploadCheck.HeadLength)
        {
            throw new ArgumentException($"A check sees at most the first {FieldUploadCheck.HeadLength} bytes of a file.", nameof(head));
        }

        var check = new FieldUploadCheck(field, name, head, type.Declaration.Messages);
        type.CheckUpload(check);
        return check.Errors;
    }

    private static void StoresFiles(IFormBuilderFieldType type, FormField field)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        if (type.Declaration.Storage != FieldStorageKind.File)
        {
            throw new ArgumentException($"The field type '{type.Declaration.Alias}' stores no files, so its fields take no uploads.", nameof(type));
        }
    }
}
