using System.IO.Compression;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>What became of a request for an entry's file.</summary>
internal enum EntryFileStatus
{
    /// <summary>The bytes are open.</summary>
    Found,

    /// <summary>The form has no such file on an entry, or its bytes are gone.</summary>
    NotFound,

    /// <summary>A scanner quarantined it: nobody gets it (ADR-0144).</summary>
    Quarantined,

    /// <summary>It waits for the site's scanners (ADR-0144).</summary>
    NotScanned,

    /// <summary>Its field is sensitive, and the user may not see sensitive values (ADR-0013).</summary>
    Sensitive,
}

/// <summary>A request's file: its bytes, which the caller closes, with what to send them as.</summary>
/// <param name="Status">What became of the request.</param>
/// <param name="Content">The bytes when found.</param>
/// <param name="Name">The name the visitor's device gave it, cleaned.</param>
/// <param name="ContentType">The content type of its kind, never the client's (ADR-0006).</param>
internal sealed record EntryFileDownload(EntryFileStatus Status, Stream? Content = null, string? Name = null, string? ContentType = null);

/// <summary>
/// An entry's files for a backoffice user (ADR-0006, ADR-0145), one or a form's in a ZIP: only what an entry holds, never what a
/// scanner quarantined or still has to answer about, a sensitive field's only for a user who may see it, with that read audited.
/// A file's name is never logged.
/// </summary>
internal sealed partial class EntryFileDownloads(
    IEntryRepository entries,
    IFormRepository forms,
    IFormBuilderFileStore store,
    FormBuilderFileScannerCollection scanners,
    IAuditRepository audit,
    ILogger<EntryFileDownloads> logger)
{
    /// <summary>The audit entity type of a file; the item is the file's key.</summary>
    public const string FileEntityType = "entryFile";

    /// <summary>The audit action of a sensitive field's file that was read.</summary>
    public const string DownloadedAction = "entryFile.sensitiveDownloaded";

    /// <summary>The audit action of the archive of a form's files.</summary>
    public const string ArchivedAction = "entryFile.archived";

    private const int Page = 250;

    /// <summary>Opens an entry's file.</summary>
    /// <param name="form">The form the access was checked on.</param>
    /// <param name="fileKey">The file's key.</param>
    /// <param name="maySeeSensitive">Whether the user holds <c>viewSensitive</c> on the form.</param>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<EntryFileDownload> OpenAsync(Guid form, Guid fileKey, bool maySeeSensitive, Guid? userKey, CancellationToken cancellationToken)
    {
        if (await forms.GetAsync(form, cancellationToken) is not { } row
            || await entries.GetFileAsync(fileKey, cancellationToken) is not { EntryId: not null } file
            || file.FormId != row.Id)
        {
            return new(EntryFileStatus.NotFound);
        }

        if (IsHeldBack(file) is { } held)
        {
            return new(held);
        }

        var sensitive = !(await PlainFieldsAsync(row.Id, cancellationToken)).Contains(file.FieldKey);
        if (sensitive && !maySeeSensitive)
        {
            return new(EntryFileStatus.Sensitive);
        }

        if (await store.OpenReadAsync(file.StoredName, cancellationToken) is not { } content)
        {
            return new(EntryFileStatus.NotFound);
        }

        if (sensitive)
        {
            await audit.AddAsync(new NewAuditEntry(DownloadedAction, FileEntityType, file.Key, userKey, new AuditDetails().Add("form", form).Add("field", file.FieldKey)), cancellationToken);
        }

        return new(EntryFileStatus.Found, content, file.OriginalName, file.ContentType);
    }

    /// <summary>
    /// Writes a form's files as a ZIP, in the order they came, each under a name that stays in its folder (<see cref="FileArchiveNames"/>):
    /// those of its entries that count, not Spam, drafts or test entries, and only what <see cref="OpenAsync"/> would give the user.
    /// A page at a time, each its own read, so submissions wait behind one page at most.
    /// </summary>
    /// <param name="form">The form the access was checked on.</param>
    /// <param name="output">Where the archive goes; left open.</param>
    /// <param name="maySeeSensitive">Whether the user holds <c>viewSensitive</c> on the form.</param>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task WriteArchiveAsync(Guid form, Stream output, bool maySeeSensitive, Guid? userKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        var row = await forms.GetAsync(form, cancellationToken) ?? throw new InvalidOperationException("The access filter read this form.");
        var plain = await PlainFieldsAsync(row.Id, cancellationToken);
        var names = new FileArchiveNames();
        var written = 0L;
        var left = 0L;
        await using var held = new HeldWritesStream(output);
        await using (var archive = await ZipArchive.CreateAsync(held, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
        {
            var after = 0;
            while (await entries.ListFilesToArchiveAsync(row.Id, after, Page, cancellationToken) is { Count: > 0 } files)
            {
                foreach (var file in files)
                {
                    after = file.Id;
                    if (IsHeldBack(file) is not null || (!maySeeSensitive && !plain.Contains(file.FieldKey)))
                    {
                        left++;
                    }
                    else if (await store.OpenReadAsync(file.StoredName, cancellationToken) is not { } content)
                    {
                        LogBytesGone(logger, file.Key);
                        left++;
                    }
                    else
                    {
                        await using (content)
                        {
                            var entry = archive.CreateEntry(names.Next(file.EntryId!.Value, file.OriginalName), CompressionLevel.Fastest);
                            entry.LastWriteTime = file.CreatedUtc;
                            await using var into = await entry.OpenAsync(cancellationToken);
                            await content.CopyToAsync(into, cancellationToken);
                        }

                        written++;
                    }
                }
            }
        }

        await held.FlushAsync(cancellationToken);
        await audit.AddAsync(new NewAuditEntry(ArchivedAction, "form", form, userKey, new AuditDetails().Add("files", written).Add("leftOut", left).Add("sensitive", maySeeSensitive)), cancellationToken);
    }

    // Why a file is held back from everybody. A site that took its scanner away has nothing left to wait for (ADR-0144).
    private EntryFileStatus? IsHeldBack(StoredEntryFile file) => file.ScanState switch
    {
        FileScanStates.Quarantined => EntryFileStatus.Quarantined,
        FileScanStates.Pending when scanners.Count > 0 => EntryFileStatus.NotScanned,
        _ => null,
    };

    // The form's fields whose files anyone who sees its entries may have: not sensitive, and not inside one that is. A field
    // the catalogue does not know counts as sensitive, as nothing says it is not.
    private async Task<HashSet<Guid>> PlainFieldsAsync(int formId, CancellationToken cancellationToken)
    {
        var fields = await forms.ListFieldsAsync(formId, cancellationToken);
        var sensitive = fields.Where(field => field.Values.IsSensitive).Select(field => field.Values.FieldKey).ToHashSet();
        return [.. fields.Where(field => !field.Values.IsSensitive && !(field.Values.ParentFieldKey is { } parent && sensitive.Contains(parent))).Select(field => field.Values.FieldKey)];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder left the file {FileKey} out of an archive: its bytes are gone")]
    private static partial void LogBytesGone(ILogger logger, Guid fileKey);
}
