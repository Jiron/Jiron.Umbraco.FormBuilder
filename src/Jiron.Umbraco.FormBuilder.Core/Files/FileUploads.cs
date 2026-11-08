using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// One file uploaded to a field before its submission (ADR-0006, ADR-0087), for the upload endpoint (6.1) and the posts
/// without JavaScript (6.2): its name cleaned, its first bytes checked, then its bytes stored and a row recorded that
/// waits for the entry. Whatever it does not keep leaves nothing behind, and a file's name is never logged. The site's
/// files settings hold each file to the types it takes and its largest file (ADR-0218), whatever the field allows.
/// </summary>
internal sealed partial class FileUploads(
    FieldTypeRegistry types,
    FieldHostLimits host,
    IFormBuilderFileStore store,
    IFormRepository forms,
    IEntryRepository entries,
    FormBuilderFileScannerCollection scanners,
    WaitingUploads waiting,
    IFormBuilderSettingsService settings,
    ILogger<FileUploads> logger)
{
    /// <summary>What a request may carry besides its file: the multipart boundaries, the part headers and the challenge.</summary>
    public const long OverheadBytes = 65_536;

    /// <summary>The longest cleaned name, as its column holds it.</summary>
    public const int MaxNameLength = EntryRepository.MaxOriginalNameLength;

    private const char Slash = '/';
    private const char Backslash = (char)92;
    private const char Point = '.';
    private const char Colon = ':';

    /// <summary>The most bytes of one request to a field: the field's limit and <see cref="OverheadBytes"/>.</summary>
    /// <param name="type">The field's type, which stores files.</param>
    /// <param name="field">The field.</param>
    public long RequestLimitOf(IFormBuilderFieldType type, FormField field) => LimitOf(type, field) + OverheadBytes;

    /// <summary>Whether the site has room for another upload (<see cref="WaitingUploads"/>, ADR-0148).</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<bool> HasRoomAsync(CancellationToken cancellationToken) => waiting.HasRoomAsync(cancellationToken);

    /// <summary>The type of a field when it stores files, so the field takes uploads; null otherwise.</summary>
    /// <param name="field">The field.</param>
    public IFormBuilderFieldType? TypeOf(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return types.FindType(field.Type) is { Declaration.Storage: FieldStorageKind.File } type ? type : null;
    }

    /// <summary>The most bytes one file of the field may have: its type's limit, or the host's when that is smaller (ADR-0071).</summary>
    /// <param name="type">The field's type, which stores files.</param>
    /// <param name="field">The field.</param>
    public long LimitOf(IFormBuilderFieldType type, FormField field) => FieldUploads.Limit(type, field, host.MaxFileBytes);

    /// <summary>The kind of error of a file past its field's limit: <c>fileSize</c> when the type declares it, otherwise <c>invalid</c>.</summary>
    /// <param name="type">The field's type.</param>
    public static FieldMessageKinds SizeError(IFormBuilderFieldType type) => FieldUploads.Declared(type, FieldMessageKinds.FileSize);

    /// <summary>
    /// A file's name as the visitor's device gave it, cleaned for downloads (ADR-0087): its last path segment, without
    /// control characters or marks that reorder how it reads, with lone surrogates replaced, normalised to NFC, trimmed, and at most
    /// <see cref="MaxNameLength"/> characters with its extension kept. Null for a missing name, and for one that is
    /// empty or only points once cleaned.
    /// </summary>
    /// <param name="name">The name, or null when the part named none.</param>
    public static string? CleanName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var segment = name.AsSpan(name.AsSpan().LastIndexOfAny(Slash, Backslash) + 1);
        var kept = new StringBuilder(segment.Length);
        foreach (var unit in segment)
        {
            if (!char.IsControl(unit) && !ReordersText(unit))
            {
                kept.Append(unit);
            }
        }

        var cleaned = TextSemantics.Clean(kept.ToString());
        return cleaned.AsSpan().Trim(Point).IsEmpty ? null : Shorten(cleaned);
    }

    /// <summary>
    /// The first error of a file, checked on its first bytes in ADR-0087's order before any byte is stored; null when it
    /// passes. Bytes of a program or a script, and an extension the host blocks anywhere in the name, are <c>fileType</c>;
    /// then the type's own check reports its kind; then bytes that are not the kind the extension names, or that hide a
    /// page (ADR-0143), are <c>fileType</c>. A <c>fileType</c> the type does not declare is <c>invalid</c>.
    /// </summary>
    /// <param name="type">The field's type, which stores files.</param>
    /// <param name="field">The field.</param>
    /// <param name="name">The cleaned name (<see cref="CleanName"/>).</param>
    /// <param name="head">The file's first <see cref="FieldUploadCheck.HeadLength"/> bytes, or all of a shorter file.</param>
    public FieldMessageKinds? Check(IFormBuilderFieldType type, FormField field, string name, ReadOnlyMemory<byte> head)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var extension = FileFieldType.ExtensionOf(name);
        if (FileKinds.IsExecutable(head.Span) || NamesABlockedExtension(name))
        {
            return FieldUploads.Declared(type, FieldMessageKinds.FileType);
        }

        var own = FieldUploads.Check(type, field, name, head);
        if (!own.IsEmpty)
        {
            return own[0];
        }

        return FileKinds.Matches(extension, head.Span) && !FileKinds.HidesMarkup(extension, head.Span) ? null : FieldUploads.Declared(type, FieldMessageKinds.FileType);
    }

    /// <summary>
    /// Checks a file and stores it for its field (ADR-0087): the name is cleaned, the first bytes are read and checked,
    /// the form's row is looked up, an upload past what the site lets wait is refused (ADR-0148), the bytes go to the store under a random id, counted against the field's limit and
    /// hashed, a file that must not also be an archive is refused when it ends as one (ADR-0143), and once
    /// <paramref name="endsAfter"/> confirms that nothing follows the file, a row without an entry records it. A file refused, cut off or not recorded leaves nothing in the store; an exception reaches the caller
    /// with the bytes deleted again. An exception reading <paramref name="content"/> arrives as
    /// <see cref="FileUploadReadException"/>.
    /// </summary>
    /// <param name="formKey">The form's key, published unless <paramref name="preview"/>.</param>
    /// <param name="type">The field's type, which stores files (<see cref="TypeOf"/>).</param>
    /// <param name="field">The field.</param>
    /// <param name="name">The file's name as the visitor's device gave it, or null.</param>
    /// <param name="content">The file's bytes, read from their position to their end; the caller owns the stream.</param>
    /// <param name="endsAfter">Reads what follows the file once its bytes are stored: false when the request goes on.</param>
    /// <param name="preview">Whether the preview's frame sends it, for a version its ticket names, which may be a draft never published (ADR-0204).</param>
    /// <param name="cancellationToken">Cancels the upload, which then keeps nothing.</param>
    public async Task<FileUploadResult> StoreAsync(
        Guid formKey,
        IFormBuilderFieldType type,
        FormField field,
        string? name,
        Stream content,
        Func<CancellationToken, Task<bool>> endsAfter,
        bool preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(endsAfter);
        if (CleanName(name) is not { } cleaned)
        {
            return new(FileUploadStatus.InvalidName);
        }

        // The field's limit, within the site's; a request is read up to the field's, the file stored up to this one.
        var site = (await settings.GetAsync<FilesSettings>(cancellationToken: cancellationToken)).Value;
        var limit = Math.Min(LimitOf(type, field), site.LimitWithin(host));

        var head = new byte[FieldUploadCheck.HeadLength];
        var headLength = await ReadHeadAsync(content, head, cancellationToken);
        if (headLength > limit)
        {
            return new(FileUploadStatus.TooLarge, Error: SizeError(type));
        }

        if (Check(type, field, cleaned, head.AsMemory(0, headLength)) is { } error)
        {
            return new(FileUploadStatus.Refused, Error: error);
        }

        if (!site.Takes(FileFieldType.ExtensionOf(cleaned)))
        {
            return new(FileUploadStatus.Refused, Error: FieldUploads.Declared(type, FieldMessageKinds.FileType));
        }

        // A preview's file needs the form only, not a publish (ADR-0204).
        if (await forms.GetAsync(formKey, cancellationToken) is not { TrashedUtc: null } form || (form.PublishedVersionId is null && !preview))
        {
            return new(FileUploadStatus.FormGone);
        }

        if (!await waiting.HasRoomAsync(cancellationToken))
        {
            return new(FileUploadStatus.NoRoom);
        }

        var id = FormBuilderFileId.New();
        var saved = false;
        try
        {
            var keepsTail = FileKinds.MustNotEndAsZip(FileFieldType.ExtensionOf(cleaned));
            using var upload = new FileUploadStream(head.AsMemory(0, headLength), content, limit, hash: true, tailLength: keepsTail ? FileKinds.ZipTailLength : 0);
            await store.SaveAsync(id, upload, cancellationToken);
            saved = true;
            if (!upload.IsAtEnd)
            {
                throw new InvalidOperationException($"The file store {store.GetType().FullName} returned before it read the upload to its end.");
            }

            if (keepsTail && FileKinds.EndsAsZip(upload.Tail))
            {
                await DiscardAsync(id);
                return new(FileUploadStatus.Refused, Error: FieldUploads.Declared(type, FieldMessageKinds.FileType));
            }

            if (!await endsAfter(cancellationToken))
            {
                await DiscardAsync(id);
                return new(FileUploadStatus.Malformed);
            }

            var file = await entries.AddFileAsync(
                new NewEntryFile(
                    Guid.NewGuid(),
                    form.Id,
                    field.Key,
                    cleaned,
                    id,
                    FileKinds.ContentType(FileFieldType.ExtensionOf(cleaned)),
                    upload.BytesRead,
                    upload.Sha256,
                    scanners.Count > 0 ? FileScanStates.Pending : FileScanStates.Unscanned),
                cancellationToken);
            if (file is null)
            {
                await DiscardAsync(id);
                return new(FileUploadStatus.FormGone);
            }

            waiting.Add(file.SizeBytes);
            LogStored(logger, file.Key, file.SizeBytes, formKey);
            return new(FileUploadStatus.Stored, file);
        }
        catch (FileTooLargeException)
        {
            if (saved)
            {
                await DiscardAsync(id);
            }

            return new(FileUploadStatus.TooLarge, Error: SizeError(type));
        }
        catch (Exception exception) when (saved && exception is not OutOfMemoryException)
        {
            await DiscardAsync(id);
            throw;
        }
    }

    // Whether any part of a name after its first is an extension the host blocks: report.exe.pdf and page.php.jpg reach
    // something that reads the name's middle, and report.exe:.pdf a stream of an NTFS file. Spaces around a part do not hide it.
    private bool NamesABlockedExtension(string name)
    {
        foreach (var part in name.Split(Point).Skip(1))
        {
            var cut = part.IndexOf(Colon, StringComparison.Ordinal);
            var extension = BuiltInValidation.AsciiLower((cut < 0 ? part : part[..cut]).Trim());
            if (host.BlockedExtensions.Contains(extension))
            {
                return true;
            }
        }

        return false;
    }

    // The marks that make a name read otherwise than its bytes run: gpj.exe written to show as exe.jpg.
    private static bool ReordersText(char unit) => unit is (char)0x061C or (char)0x200E or (char)0x200F or (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069);

    // The first bytes, as many as the head holds or all of a shorter file.
    private static async Task<int> ReadHeadAsync(Stream content, byte[] head, CancellationToken cancellationToken)
    {
        var length = 0;
        while (length < head.Length)
        {
            var read = await FileUploadStream.ReadSourceAsync(content, head.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return length;
    }

    // The stem cut at a whole character so that the name with its extension fits; a name whose extension alone does not
    // fit is cut as a whole.
    private static string Shorten(string name)
    {
        if (name.Length <= MaxNameLength)
        {
            return name;
        }

        var point = name.LastIndexOf(Point);
        var suffix = point > 0 && name.Length - point < MaxNameLength ? name[point..] : "";
        var stem = name[..(name.Length - suffix.Length)];
        var budget = MaxNameLength - suffix.Length;
        var end = 0;
        while (end < stem.Length)
        {
            var next = StringInfo.GetNextTextElementLength(stem, end);
            if (end + next > budget)
            {
                break;
            }

            end += next;
        }

        return stem[..end] + suffix;
    }

    // After the upload is refused or failed, whatever the caller's cancellation says. What cannot be deleted is logged
    // without its name and left for retention (13.1).
    private async Task DiscardAsync(string id)
    {
        try
        {
            await store.DeleteAsync(id, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogBytesKept(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Jiron Form Builder stored the upload {FileKey} of {SizeBytes} bytes for the form {FormKey}")]
    private static partial void LogStored(ILogger logger, Guid fileKey, long sizeBytes, Guid formKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not delete the bytes of an upload it did not keep ({Failure}); retention deletes them later")]
    private static partial void LogBytesKept(ILogger logger, string failure);
}

/// <summary>What became of an upload (<see cref="FileUploads.StoreAsync"/>).</summary>
internal enum FileUploadStatus
{
    /// <summary>Stored and recorded, waiting for its entry.</summary>
    Stored,

    /// <summary>The part named no file, or one that is empty once cleaned.</summary>
    InvalidName,

    /// <summary>Its first bytes or its name failed a check; the result names the kind of error.</summary>
    Refused,

    /// <summary>Past its field's limit or the request's; the result names the kind of error.</summary>
    TooLarge,

    /// <summary>Something followed the file.</summary>
    Malformed,

    /// <summary>The form is no longer published, or went while the file was read.</summary>
    FormGone,

    /// <summary>The uploads that wait for an entry have reached what the site lets wait (ADR-0148): nothing of it was stored.</summary>
    NoRoom,
}

/// <summary>The result of an upload.</summary>
/// <param name="Status">What became of it.</param>
/// <param name="File">The recorded file when it was stored.</param>
/// <param name="Error">The kind of error when it was refused or too large.</param>
internal sealed record FileUploadResult(FileUploadStatus Status, StoredEntryFile? File = null, FieldMessageKinds? Error = null);

