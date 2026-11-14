using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>What the mirror could not keep in step, for the health check (ADR-0220).</summary>
internal enum MirrorProblemKind
{
    /// <summary>The file and the form both changed since the last pass: the site kept the form.</summary>
    Conflict,

    /// <summary>The form's file could not be written.</summary>
    Unwritable,

    /// <summary>A file of the folder is no form file the site can take in; the log says why.</summary>
    Unreadable,

    /// <summary>A changed file was taken into the form's draft, but publishing refused it.</summary>
    NotPublished,
}

/// <summary>Something the mirror could not keep in step.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="File">The file's name in the folder.</param>
internal sealed record MirrorProblem(MirrorProblemKind Kind, string File);

/// <summary>
/// Forms as files (D25, ADR-0220). With <see cref="FormBuilderDefinitionsOptions.Mirror"/> on, each publish writes the form's
/// published version to <c>&lt;alias&gt;.json</c>, and a pass reads the folder: a file whose definition changed while the form
/// did not is taken in as a new published version, with the file's keys; a file whose form changed too is a conflict, which
/// keeps the form and is remembered for the health check; a file of a form the site never had creates it; a file that holds
/// an older published version of its form is written again, as it is a stale copy. What the mirror last saw of each form is
/// kept in <c>jfbKeyValue</c>: the hash of the file's definition, the version it matched and the file's name. Definitions are
/// compared, never bytes, so a file a developer reformats, or one exported at another time, never reads as a change.
/// </summary>
internal sealed partial class FormMirror(
    IOptionsMonitor<FormBuilderOptions> options,
    IHostFolders folders,
    IFormRepository forms,
    IKeyValueRepository values,
    IFormBuilderFormExchangeService exchange,
    IFormBuilderFormService formService,
    FormDocuments documents,
    FormDefinitionSerializer serializer,
    ILogger<FormMirror> logger)
{
    /// <summary>The prefix of what the mirror last saw of a form, by the form's key.</summary>
    public const string RecordPrefix = "mirror/";

    /// <summary>The prefix of what the mirror could not keep in step, by its kind and the form's key or the file's name.</summary>
    public const string ProblemPrefix = "mirror-problem/";

    /// <summary>The comment of a version the mirror publishes from its file.</summary>
    public const string FromFileComment = "Published from its file.";

    private const int Page = 200;

    // The form whose publish the pass makes itself, so the publish's handler leaves its file as the developer wrote it: per
    // async flow, so a publish an editor makes meanwhile still writes its file. The mirror is a singleton, so the handler
    // reads the value the pass set.
    private readonly AsyncLocal<Guid?> _publishingFromFile = new();

    /// <summary>Whether the host turned the mirror on.</summary>
    public bool On => options.CurrentValue.Definitions.Mirror;

    /// <summary>The folder of the files on this server.</summary>
    /// <exception cref="InvalidOperationException">The folder is not one the files may be kept in.</exception>
    public string Folder => FileStoreRoot.TryResolve(options.CurrentValue.Definitions.Folder, folders, FormBuilderDefinitionsOptions.FolderKey, FormBuilderDefinitionsOptions.DefaultFolder, out var path, out var problem)
        ? path
        : throw new InvalidOperationException(problem);

    /// <summary>
    /// Writes a published form's file after its publish, unless the pass published it from its file. A file that changed
    /// since the mirror last saw it is left as it is, as a conflict, so a developer's change is never lost to a publish; a
    /// file that cannot be written is logged and remembered. Neither fails the publish.
    /// </summary>
    public async Task PublishedAsync(Guid formKey, int version, CancellationToken cancellationToken = default)
    {
        if (!On || _publishingFromFile.Value == formKey)
        {
            return;
        }

        var state = await StateAsync(cancellationToken);
        await WriteAsync(formKey, version, state, keepChangedFile: true, cancellationToken);
    }

    /// <summary>
    /// Settles every conflict for the site's forms (the health check's action): each form's published version is written to
    /// its file, over the file's own change. The forms it wrote.
    /// </summary>
    public async Task<int> KeepTheFormsAsync(CancellationToken cancellationToken = default)
    {
        if (!On)
        {
            return 0;
        }

        var state = await StateAsync(cancellationToken);
        var prefix = ProblemKey(MirrorProblemKind.Conflict, string.Empty);
        var written = 0;
        foreach (var key in state.Problems.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (Guid.TryParseExact(key[prefix.Length..], "N", out var formKey)
                && await forms.GetAsync(formKey, cancellationToken) is { TrashedUtc: null, PublishedVersionId: { } id }
                && await forms.GetVersionByIdAsync(id, cancellationToken) is { } published)
            {
                await WriteAsync(formKey, published.Info.Number, state, keepChangedFile: false, cancellationToken);
                written++;
            }
            else
            {
                await ClearProblemAsync(MirrorProblemKind.Conflict, key[prefix.Length..], state, cancellationToken);
            }
        }

        return written;
    }

    /// <summary>One pass over the folder and the published forms, as ADR-0220 says; nothing while the mirror is off.</summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!On)
        {
            return;
        }

        var folder = Folder;
        Directory.CreateDirectory(folder);
        var state = await StateAsync(cancellationToken);
        var live = await PublishedFormsAsync(cancellationToken);
        var seen = new HashSet<Guid>();

        // The files this pass could not read: they may hold someone's work, a form's file with a slip of the hand in it, so
        // nothing is written over them (ADR-0252).
        var unread = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.GetFileName(path);
            if (await ReadAsync(path, cancellationToken) is not { } read)
            {
                unread.Add(file);
                await SetProblemAsync(MirrorProblemKind.Unreadable, NameKey(file), file, state, cancellationToken);
                continue;
            }

            if (!seen.Add(read.Envelope.FormKey))
            {
                LogUnreadable(logger, file, "another file of the folder holds the same form");
                await SetProblemAsync(MirrorProblemKind.Unreadable, NameKey(file), file, state, cancellationToken);
                continue;
            }

            try
            {
                await SyncFileAsync(file, read.Json, read.Envelope, state, live, cancellationToken);
                await ClearProblemAsync(MirrorProblemKind.Unreadable, NameKey(file), state, cancellationToken);
            }
            catch (Exception exception) when (exception is FormBuilderValidationException or FormBuilderSettingLockedException)
            {
                // The site refuses what the file holds, a setting its host locks among it: this file waits, the pass goes on.
                LogUnreadable(logger, file, exception.Message);
                await SetProblemAsync(MirrorProblemKind.Unreadable, NameKey(file), file, state, cancellationToken);
            }
            catch (Exception exception) when (exception is FormBuilderConcurrencyException or FormBuilderConflictException or FormBuilderCancelledException)
            {
                // Someone changed the form meanwhile, or the site refused the change for now: the next pass tries again.
                LogPostponed(logger, file, exception.Message);
            }
        }

        // A file that is gone, of a problem only a file has, is no problem any more.
        foreach (var key in state.Problems.Keys.Where(key => key.StartsWith(ProblemKey(MirrorProblemKind.Unreadable, string.Empty), StringComparison.Ordinal)).ToList())
        {
            if (!File.Exists(Path.Combine(folder, state.Problems[key])))
            {
                await values.DeleteAsync(key, cancellationToken);
            }
        }

        // A published form without a file gets its file, as after the mirror was turned on; never over a file of its name
        // that could not be read.
        foreach (var form in live.Values.Where(form => !seen.Contains(form.Key)))
        {
            if (form.PublishedVersionId is { } id && await forms.GetVersionByIdAsync(id, cancellationToken) is { } published)
            {
                await WriteAsync(form.Key, published.Info.Number, state, keepChangedFile: false, cancellationToken, unread);
            }
        }
    }

    /// <summary>What the mirror could not keep in step, for the health check.</summary>
    public async Task<IReadOnlyList<MirrorProblem>> ProblemsAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<MirrorProblem>();
        foreach (var (key, value) in await values.ListAsync(ProblemPrefix, cancellationToken))
        {
            var kind = key[ProblemPrefix.Length..].Split('/')[0];
            if (value is not null && Enum.GetValues<MirrorProblemKind>().FirstOrDefault(each => KindKey(each) == kind) is var parsed && KindKey(parsed) == kind)
            {
                found.Add(new MirrorProblem(parsed, value));
            }
        }

        return [.. found.OrderBy(problem => problem.Kind).ThenBy(problem => problem.File, StringComparer.Ordinal)];
    }

    // One file of the folder against its form, among the published forms outside the trash.
    private async Task SyncFileAsync(string file, string json, FormExportEnvelope envelope, MirrorState state, Dictionary<Guid, StoredForm> live, CancellationToken cancellationToken)
    {
        var key = envelope.FormKey;
        var record = state.Record(key);
        if (!live.TryGetValue(key, out var form))
        {
            // A form the site never had: created from its file. One the mirror knew was deleted here, and its file stays; a
            // form in the trash, or one taken offline, keeps its file until it is published again.
            if (record is null && await forms.GetAsync(key, cancellationToken) is null)
            {
                await CreateAsync(file, json, envelope, state, cancellationToken);
            }

            return;
        }

        if (form.PublishedVersionId is not { } publishedId || await forms.GetVersionByIdAsync(publishedId, cancellationToken) is not { } published)
        {
            return;
        }

        // The file as the mirror last saw it, and the form not published since: nothing to do.
        var hash = HashOf(envelope.Definition, key);
        if (record?.Hash == hash && record.Version == published.Info.Number)
        {
            return;
        }

        if (HashOf(published) == hash)
        {
            await SetRecordAsync(key, new MirrorRecord(hash, published.Info.Number, file), state, cancellationToken);
            await ClearProblemAsync(MirrorProblemKind.Conflict, key.ToString("N"), state, cancellationToken);
            return;
        }

        if (record?.Hash == hash || await HoldsAnOlderVersionAsync(form.Id, published.Info.Number, hash, cancellationToken))
        {
            // A stale copy, as a folder holds after a publish the mirror missed: written again. History restores an older version.
            await WriteAsync(key, published.Info.Number, state, keepChangedFile: false, cancellationToken);
            return;
        }

        if (record is not null && record.Version == published.Info.Number)
        {
            await ImportAsync(form, file, json, hash, state, cancellationToken);
            return;
        }

        LogConflict(logger, file);
        await SetProblemAsync(MirrorProblemKind.Conflict, key.ToString("N"), file, state, cancellationToken);
    }

    // A changed file into the form's draft, then published, with the file's keys; its hash is remembered as the file has it.
    private async Task ImportAsync(StoredForm form, string file, string json, string hash, MirrorState state, CancellationToken cancellationToken)
    {
        var replaced = await exchange.ReplaceAsync(new ReplaceFormRequest(form.Key, json, form.ConcurrencyVersion), cancellationToken);
        if (replaced is null)
        {
            return;
        }

        LogNotes(file, replaced.Notes);
        if (await PublishFromFileAsync(form.Key, replaced.Result.Form.ConcurrencyVersion, file, state, cancellationToken) is { } version)
        {
            await SetRecordAsync(form.Key, new MirrorRecord(hash, version, file), state, cancellationToken);
            await ClearProblemAsync(MirrorProblemKind.Conflict, form.Key.ToString("N"), state, cancellationToken);
            LogImported(logger, file, version);
        }
    }

    // A file of a form the site never had: the form, with the file's keys, alias and name, then published.
    private async Task CreateAsync(string file, string json, FormExportEnvelope envelope, MirrorState state, CancellationToken cancellationToken)
    {
        var imported = await exchange.ImportAsync(new ImportFormRequest(json) { KeepKeys = true }, cancellationToken);
        LogNotes(file, imported.Notes);
        if (await PublishFromFileAsync(imported.Form.Key, imported.Form.ConcurrencyVersion, file, state, cancellationToken) is { } version)
        {
            await SetRecordAsync(imported.Form.Key, new MirrorRecord(HashOf(envelope.Definition, imported.Form.Key), version, file), state, cancellationToken);
            LogImported(logger, file, version);
        }
    }

    // A publish of the draft its file made, which the publish's handler leaves alone; a refusal leaves the draft and says so.
    private async Task<int?> PublishFromFileAsync(Guid key, int concurrencyVersion, string file, MirrorState state, CancellationToken cancellationToken)
    {
        _publishingFromFile.Value = key;
        try
        {
            var published = await formService.PublishAsync(key, concurrencyVersion, FromFileComment, null, cancellationToken);
            await ClearProblemAsync(MirrorProblemKind.NotPublished, key.ToString("N"), state, cancellationToken);
            return published?.Version;
        }
        catch (FormBuilderValidationException exception)
        {
            LogNotPublished(logger, file, exception.Message);
            await SetProblemAsync(MirrorProblemKind.NotPublished, key.ToString("N"), file, state, cancellationToken);
            return null;
        }
        finally
        {
            _publishingFromFile.Value = null;
        }
    }

    // A form's published version to its file, written to a temporary file and then moved, so a reader never sees half of it. With
    // keepChangedFile, a file someone changed since the mirror last saw it stays as it is, as a conflict, and one that holds this
    // version already is only remembered.
    private async Task WriteAsync(Guid formKey, int version, MirrorState state, bool keepChangedFile, CancellationToken cancellationToken, HashSet<string>? unread = null)
    {
        if (await exchange.ExportAsync(formKey, version, cancellationToken) is not { } export
            || FormExportFormat.Read(export.Json, serializer).Envelope is not { } envelope)
        {
            return;
        }

        var fileName = OnDiskName(export.FileName);
        var record = state.Record(formKey);
        var hash = HashOf(envelope.Definition, formKey);

        // A file of the form's name, or of the name the mirror last saw it under, that cannot be read stays as it is: it may
        // be this form's file with a slip of the hand in it, which a write would lose (ADR-0252). The pass says which files
        // it could not read; a publish looks for itself.
        var own = record is not null && IsOwnFile(record.File) ? record.File : fileName;
        if (unread is null
            ? keepChangedFile && File.Exists(Path.Combine(Folder, own)) && await ReadAsync(Path.Combine(Folder, own), cancellationToken) is null
            : unread.Contains(own) || unread.Contains(fileName))
        {
            await SetProblemAsync(MirrorProblemKind.Unreadable, NameKey(own), own, state, cancellationToken);
            return;
        }

        if (keepChangedFile && await OnDiskAsync(formKey, record, fileName, cancellationToken) is { } onDisk)
        {
            if (onDisk.Hash == hash)
            {
                await SetRecordAsync(formKey, new MirrorRecord(hash, version, onDisk.File), state, cancellationToken);
                await ClearProblemAsync(MirrorProblemKind.Conflict, formKey.ToString("N"), state, cancellationToken);
                return;
            }

            if (onDisk.Hash != record?.Hash
                && !(await forms.GetAsync(formKey, cancellationToken) is { } form && await HoldsAnOlderVersionAsync(form.Id, version, onDisk.Hash, cancellationToken)))
            {
                LogConflict(logger, onDisk.File);
                await SetProblemAsync(MirrorProblemKind.Conflict, formKey.ToString("N"), onDisk.File, state, cancellationToken);
                return;
            }
        }

        try
        {
            var folder = Folder;
            Directory.CreateDirectory(folder);
            var temporary = Path.Combine(folder, $".{fileName}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporary, export.Json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, Path.Combine(folder, fileName), overwrite: true);

            // A renamed form leaves the file of its old alias: gone with the rename, while it still holds this form.
            if (record is not null && !string.Equals(record.File, fileName, StringComparison.OrdinalIgnoreCase) && IsOwnFile(record.File)
                && await ReadAsync(Path.Combine(folder, record.File), cancellationToken) is { } old && old.Envelope.FormKey == formKey)
            {
                File.Delete(Path.Combine(folder, record.File));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogUnwritable(logger, fileName, exception.Message);
            await SetProblemAsync(MirrorProblemKind.Unwritable, formKey.ToString("N"), fileName, state, cancellationToken);
            return;
        }

        await SetRecordAsync(formKey, new MirrorRecord(hash, version, fileName), state, cancellationToken);
        await ClearProblemAsync(MirrorProblemKind.Unwritable, formKey.ToString("N"), state, cancellationToken);
        await ClearProblemAsync(MirrorProblemKind.Conflict, formKey.ToString("N"), state, cancellationToken);
    }

    // The form's file as the folder holds it now, by the name the mirror last saw or else its alias's, with its definition's
    // hash; null without one, or for one that is no file of this form, which a write may replace.
    private async Task<(string File, string Hash)?> OnDiskAsync(Guid formKey, MirrorRecord? record, string fileName, CancellationToken cancellationToken)
    {
        var file = record is not null && IsOwnFile(record.File) ? record.File : fileName;
        var path = Path.Combine(Folder, file);
        return File.Exists(path) && await ReadAsync(path, cancellationToken) is { } read && read.Envelope.FormKey == formKey
            ? (file, HashOf(read.Envelope.Definition, formKey))
            : null;
    }

    // A file and its envelope, or null, logged, for one that cannot be read or is no export of a form.
    private async Task<(string Json, FormExportEnvelope Envelope)?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var file = Path.GetFileName(path);
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            var result = FormExportFormat.Read(json, serializer);
            if (result.Envelope is { } envelope)
            {
                return (json, envelope);
            }

            // Where it goes wrong, by code and path, never a value of the file.
            var problems = string.Join("; ", result.Problems.Take(5).Select(problem => $"{problem.Code} at {problem.Path}"));
            LogUnreadable(logger, file, problems.Length == 0 ? "it is no export of a form" : "it is no export of a form (" + problems + ")");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(logger, file, exception.Message);
        }

        return null;
    }

    // The hash a definition's document has with the form's own key in it, as the versions keep theirs.
    private string HashOf(FormDefinition definition, Guid formKey) => documents.Document(definition with { Key = formKey }).DefinitionHash;

    // A version's hash as a file of it would give it: the stored one, unless an older schema is stored, which is read as now.
    private string HashOf(StoredFormVersion version) =>
        version.Info.SchemaVersion == FormDefinition.CurrentSchemaVersion
            ? version.Info.DefinitionHash
            : documents.Document(documents.Readable(version)).DefinitionHash;

    // Whether a published version before the live one holds this definition: the file is a stale copy.
    private async Task<bool> HoldsAnOlderVersionAsync(int formId, int live, string hash, CancellationToken cancellationToken)
    {
        for (var skip = 0L; ; skip += Page)
        {
            var page = await forms.ListVersionsAsync(formId, FormVersionKind.Published, new PageRequest(skip, Page), cancellationToken);
            foreach (var info in page.Items.Where(info => info.Number < live))
            {
                if (info.SchemaVersion == FormDefinition.CurrentSchemaVersion
                    ? info.DefinitionHash == hash
                    : await forms.GetVersionAsync(formId, info.Number, cancellationToken) is { } older && HashOf(older) == hash)
                {
                    return true;
                }
            }

            if (page.Items.Count < Page)
            {
                return false;
            }
        }
    }

    // The published forms outside the trash, by key: what a pass compares the files with.
    private async Task<Dictionary<Guid, StoredForm>> PublishedFormsAsync(CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, StoredForm>();
        for (var skip = 0L; ; skip += Page)
        {
            var page = await forms.ListAsync(FormFilter.Live with { PublishedOnly = true }, new PageRequest(skip, Page), cancellationToken);
            foreach (var form in page.Items)
            {
                found[form.Key] = form;
            }

            if (page.Items.Count < Page)
            {
                return found;
            }
        }
    }

    /// <summary>
    /// A form's file name in the folder: its alias with .json, and an underscore before an alias Windows keeps for a device
    /// (<c>con</c>, <c>nul</c>, <c>com1</c> and the like), which no alias can start with, so the file is never the device (16.1's review).
    /// </summary>
    /// <param name="fileName">The export's file name, the alias with .json.</param>
    internal static string OnDiskName(string fileName) =>
        Files.FileArchiveNames.IsDeviceName(Path.GetFileNameWithoutExtension(fileName)) ? "_" + fileName : fileName;

    // A file name the mirror writes: an alias with .json, nothing that leaves the folder.
    private static bool IsOwnFile(string file) =>
        file.EndsWith(".json", StringComparison.Ordinal) && !file.StartsWith('.') && file.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\']) < 0;

    // What the mirror remembers, read once per pass or publish.
    private async Task<MirrorState> StateAsync(CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in await values.ListAsync(RecordPrefix, cancellationToken))
        {
            if (value is not null)
            {
                records[key] = value;
            }
        }

        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in await values.ListAsync(ProblemPrefix, cancellationToken))
        {
            if (value is not null)
            {
                problems[key] = value;
            }
        }

        return new MirrorState(records, problems);
    }

    // The record replaced only from the one read, so of two writers at once one wins; the other's next pass reads it.
    private async Task SetRecordAsync(Guid formKey, MirrorRecord record, MirrorState state, CancellationToken cancellationToken)
    {
        var key = RecordPrefix + formKey.ToString("N");
        var text = record.Write();
        var stored = state.Records.TryGetValue(key, out var current)
            ? current == text || await values.TryReplaceAsync(key, current, text, cancellationToken)
            : await values.TryAddAsync(key, text, cancellationToken);
        if (stored)
        {
            state.Records[key] = text;
        }
    }

    private async Task SetProblemAsync(MirrorProblemKind kind, string subject, string file, MirrorState state, CancellationToken cancellationToken)
    {
        var key = ProblemKey(kind, subject);
        if (state.Problems.TryGetValue(key, out var current) && current == file)
        {
            return;
        }

        await values.SetAsync(key, file, cancellationToken);
        state.Problems[key] = file;
    }

    private async Task ClearProblemAsync(MirrorProblemKind kind, string subject, MirrorState state, CancellationToken cancellationToken)
    {
        var key = ProblemKey(kind, subject);
        if (state.Problems.Remove(key))
        {
            await values.DeleteAsync(key, cancellationToken);
        }
    }

    private static string ProblemKey(MirrorProblemKind kind, string subject) => $"{ProblemPrefix}{KindKey(kind)}/{subject}";

    // A kind as a part of a key, which holds no capital letter.
    private static string KindKey(MirrorProblemKind kind) => StoredNames.Of(kind).ToLowerInvariant();

    // A file's name as a key's part: lowercase hex of its hash, as a key holds only lowercase letters, digits and a few marks.
    private static string NameKey(string file) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file)))[..32];

    private void LogNotes(string file, IReadOnlyList<FormImportNote> notes)
    {
        foreach (var note in notes)
        {
            LogNote(logger, file, note.Code, note.Alias);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not take in the form file {File}: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string file, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder kept the form of {File}: the file and the form both changed since the last pass")]
    private static partial void LogConflict(ILogger logger, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder published version {Version} of a form from its file {File}")]
    private static partial void LogImported(ILogger logger, string file, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder took {File} in with a note: {Code} {Alias}")]
    private static partial void LogNote(ILogger logger, string file, string code, string? alias);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder took {File} into its form's draft, but could not publish it: {Reason}")]
    private static partial void LogNotPublished(ILogger logger, string file, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder takes {File} in on the next pass: {Reason}")]
    private static partial void LogPostponed(ILogger logger, string file, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not write the form file {File}: {Reason}")]
    private static partial void LogUnwritable(ILogger logger, string file, string reason);

    // What the mirror remembers, by key, as read and as written since.
    private sealed record MirrorState(Dictionary<string, string> Records, Dictionary<string, string> Problems)
    {
        public MirrorRecord? Record(Guid formKey) =>
            Records.TryGetValue(RecordPrefix + formKey.ToString("N"), out var text) ? MirrorRecord.Read(text) : null;
    }
}

/// <summary>What the mirror last saw of a form: the hash of its file's definition, the version it matched, and the file.</summary>
/// <param name="Hash">The definition's hash, as the versions keep theirs.</param>
/// <param name="Version">The published version the file matched.</param>
/// <param name="File">The file's name.</param>
internal sealed record MirrorRecord(string Hash, int Version, string File)
{
    /// <summary>The record as <c>jfbKeyValue</c> keeps it.</summary>
    public string Write() => JsonSerializer.Serialize(new { hash = Hash, version = Version, file = File });

    /// <summary>A kept record, or null for one that cannot be read.</summary>
    public static MirrorRecord? Read(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("hash", out var hash) && hash.ValueKind == JsonValueKind.String
                && root.TryGetProperty("version", out var version) && version.TryGetInt32(out var number)
                && root.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String
                ? new MirrorRecord(hash.GetString()!, number, file.GetString()!)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Writes a form's file after each publish while the mirror is on (ADR-0220).</summary>
internal sealed class FormMirrorPublishedHandler(FormMirror mirror) : INotificationAsyncHandler<FormPublishedNotification>
{
    /// <inheritdoc />
    public Task HandleAsync(FormPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return mirror.PublishedAsync(notification.FormKey, notification.Version, cancellationToken);
    }
}

/// <summary>
/// The mirror's pass (ADR-0220), on a timer of its own on the server that schedules, as the folder is that server's own and
/// the backoffice publishes there (ADR-0144 does the same for the file scans): the first soon after the site starts, then
/// every minute, once Umbraco runs and no migration is pending. Nothing happens while the mirror is off.
/// </summary>
internal sealed partial class FormMirrorJob(
    FormMirror mirror,
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    IHostApplicationLifetime lifetime,
    ILogger<FormMirrorJob> logger)
    : IRecurringBackgroundJob
{
    /// <inheritdoc />
    public event EventHandler? PeriodChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public TimeSpan Delay => TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public ServerRole[] ServerRoles => [ServerRole.Single, ServerRole.SchedulingPublisher];

    /// <inheritdoc />
    public async Task RunJobAsync()
    {
        var stopping = lifetime.ApplicationStopping;
        if (!mirror.On || runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(stopping))
        {
            return;
        }

        try
        {
            await mirror.SyncAsync(stopping);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            LogPassFailed(logger, LoggedException.Of(exception));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's pass over the form files failed")]
    private static partial void LogPassFailed(ILogger logger, LoggedException exception);
}
