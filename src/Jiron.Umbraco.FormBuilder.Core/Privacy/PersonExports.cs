using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>
/// The export of everything about a person (ADR-0223): a ZIP with a folder per entry, named by its form's alias and its
/// number (a draft by its form's alias and a count), holding <c>entry.json</c> (the form, the entry's own facts with its
/// context values, its spam check and how long it took, every answer by its label with each consent's exact wording, the
/// timeline's kinds and times, the notes, and the mail sent to the address the visitor typed) and the entry's files. Sensitive answers go along, as the copy is the person's own. A file the scanner holds back stays out, and the
/// JSON says so. The export is audited once, by its counts.
/// </summary>
/// <param name="entries">The entries and their files.</param>
/// <param name="forms">The forms.</param>
/// <param name="panels">Reads each entry as the panel does.</param>
/// <param name="events">The timelines.</param>
/// <param name="notes">The notes.</param>
/// <param name="emails">The entries' mail.</param>
/// <param name="store">The files' bytes.</param>
/// <param name="audit">The audit log.</param>
/// <param name="time">The clock.</param>
internal sealed class PersonExports(
    IEntryRepository entries,
    IFormRepository forms,
    EntryPanels panels,
    IEntryEventRepository events,
    IEntryNoteRepository notes,
    IEmailRepository emails,
    IFormBuilderFileStore store,
    IAuditRepository audit,
    TimeProvider time)
{
    /// <summary>What a person's export stands in a background export's record for its form (ADR-0210).</summary>
    public static readonly Guid Scope = Guid.Empty;

    /// <summary>The format a person's export names in its record.</summary>
    public const string FormatName = "person";

    /// <summary>The audit action of a person's export; its details hold its counts, never whom it is about.</summary>
    public const string ExportedAction = "people.exported";

    private const string ContentType = "application/zip";

    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    /// <summary>Writes the export of these entries; those gone since are left out.</summary>
    /// <param name="entryKeys">The person's entries, as a find found them.</param>
    /// <param name="exporter">Who exports.</param>
    /// <param name="output">Where the ZIP goes.</param>
    /// <param name="progress">Told how many entries are written, after each one.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public async Task<EntryExportFile> WriteAsync(IReadOnlyList<Guid> entryKeys, EntryExporter exporter, Stream output, Func<int, Task> progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(progress);
        var names = new FileArchiveNames();
        var written = 0;
        var files = 0;
        var drafts = 0;
        var byId = new Dictionary<int, StoredForm?>();
        await using var held = new HeldWritesStream(output);
        await using (var archive = await ZipArchive.CreateAsync(held, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
        {
            foreach (var key in entryKeys.Distinct())
            {
                if (await entries.GetAsync(key, cancellationToken) is not { } entry)
                {
                    continue;
                }

                if (!byId.TryGetValue(entry.FormId, out var form))
                {
                    byId[entry.FormId] = form = await forms.GetByIdAsync(entry.FormId, cancellationToken);
                }

                if (form is null || await panels.ReadAllAsync(form, entry, cancellationToken) is not { } panel)
                {
                    continue;
                }

                var folder = entry.State == EntryState.Draft
                    ? $"{form.Alias}-draft-{(++drafts).ToString(CultureInfo.InvariantCulture)}"
                    : $"{form.Alias}-{entry.Number.ToString(CultureInfo.InvariantCulture)}";
                var jsonPath = names.NextIn(folder, "entry.json");
                var stored = (await entries.ListFilesAsync(entry.Id, cancellationToken)).ToDictionary(file => file.Key);
                var paths = new Dictionary<Guid, string>();
                foreach (var file in panel.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Answers).SelectMany(answer => answer.Files))
                {
                    if (file.Held || !stored.TryGetValue(file.Key, out var row) || await store.OpenReadAsync(row.StoredName, cancellationToken) is not { } bytes)
                    {
                        continue;
                    }

                    await using (bytes)
                    {
                        var path = names.NextIn(folder, file.Name);
                        var item = archive.CreateEntry(path, CompressionLevel.Fastest);
                        await using var into = await item.OpenAsync(cancellationToken);
                        await bytes.CopyToAsync(into, cancellationToken);
                        paths[file.Key] = path[(folder.Length + 1)..];
                        files++;
                    }
                }

                await using (var into = await archive.CreateEntry(jsonPath, CompressionLevel.Fastest).OpenAsync(cancellationToken))
                {
                    var theirs = (await emails.ListForEntryAsync(entry.Id, cancellationToken)).Where(mail => mail.RecipientKey is not null).ToList();
                    await WriteEntryAsync(into, form, entry, panel, paths, await events.ListForEntryAsync(entry.Id, cancellationToken), await notes.ListAsync(entry.Id, cancellationToken), theirs, cancellationToken);
                }

                written++;
                await progress(written);
            }
        }

        await held.FlushAsync(cancellationToken);
        await audit.AddAsync(new NewAuditEntry(ExportedAction, PersonData.EntityType, null, exporter.UserKey, new AuditDetails().Add("entries", written).Add("files", files)), CancellationToken.None);
        return new EntryExportFile(FileName(time.GetUtcNow()), ContentType, written);
    }

    /// <summary>The name a person's export downloads under, by the day it was written.</summary>
    public static string FileName(DateTimeOffset written) => $"personal-data-{written.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";

    private static async Task WriteEntryAsync(
        Stream into,
        StoredForm form,
        StoredEntry entry,
        EntryPanel panel,
        Dictionary<Guid, string> paths,
        IReadOnlyList<StoredEntryEvent> timeline,
        IReadOnlyList<StoredEntryNote> written,
        IReadOnlyList<StoredEmail> mail,
        CancellationToken cancellationToken)
    {
        await using var json = new Utf8JsonWriter(into, Indented);
        json.WriteStartObject();
        json.WriteStartObject("form");
        json.WriteString("key", form.Key);
        json.WriteString("name", form.Name);
        json.WriteString("alias", form.Alias);
        json.WriteEndObject();

        json.WriteStartObject("entry");
        json.WriteString("key", entry.Key);
        if (entry.State != EntryState.Draft)
        {
            json.WriteNumber("number", entry.Number);
        }

        json.WriteString("state", StoredNames.Of(entry.State));
        json.WriteBoolean("test", entry.IsTest);
        json.WriteString("received", entry.SubmittedUtc);
        json.WriteString("culture", entry.Culture);
        Optional(json, "page", entry.PageUrl);
        Optional(json, "ipAddress", entry.IpAddress);
        if (entry.MemberKey is { } member)
        {
            json.WriteString("member", member);
        }

        if (entry.VerifiedUtc is { } verified)
        {
            json.WriteString("confirmed", verified);
        }

        if (entry.DurationSeconds is { } seconds)
        {
            json.WriteNumber("secondsToComplete", seconds);
        }

        json.WriteStartObject("spamCheck");
        json.WriteNumber("score", entry.SpamScore);
        json.WriteStartArray("reasons");
        foreach (var reason in entry.SpamReasons.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            json.WriteStringValue(reason);
        }

        json.WriteEndArray();
        json.WriteEndObject();
        using (var context = JsonDocument.Parse(entry.Context))
        {
            if (context.RootElement.ValueKind == JsonValueKind.Object && context.RootElement.EnumerateObject().Any())
            {
                json.WritePropertyName("context");
                context.RootElement.WriteTo(json);
            }
        }

        json.WriteEndObject();

        json.WriteStartArray("answers");
        foreach (var page in panel.Pages)
        {
            foreach (var group in page.Groups)
            {
                foreach (var answer in group.Answers)
                {
                    json.WriteStartObject();
                    Optional(json, "page", page.Title);
                    Optional(json, "group", group.Title);
                    json.WriteString("field", answer.Field);
                    json.WriteString("label", answer.Label);
                    json.WriteStartArray("answer");
                    foreach (var text in answer.Texts)
                    {
                        json.WriteStringValue(text);
                    }

                    json.WriteEndArray();
                    if (answer.Files.Count > 0)
                    {
                        json.WriteStartArray("files");
                        foreach (var file in answer.Files)
                        {
                            json.WriteStartObject();
                            json.WriteString("name", file.Name);
                            if (paths.TryGetValue(file.Key, out var path))
                            {
                                json.WriteString("path", path);
                            }
                            else
                            {
                                json.WriteBoolean("leftOut", true);
                            }

                            json.WriteEndObject();
                        }

                        json.WriteEndArray();
                    }

                    Optional(json, "consent", answer.Consent);
                    Optional(json, "signedName", answer.SignedName);
                    if (answer.NotKept)
                    {
                        json.WriteBoolean("notKept", true);
                    }

                    json.WriteEndObject();
                }
            }
        }

        json.WriteEndArray();

        json.WriteStartArray("timeline");
        foreach (var happened in timeline)
        {
            json.WriteStartObject();
            json.WriteString("kind", happened.Kind);
            json.WriteString("at", happened.OccurredUtc);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("notes");
        foreach (var note in written)
        {
            json.WriteStartObject();
            json.WriteString("at", note.CreatedUtc);
            json.WriteString("text", note.Body);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        // The mail to the address the visitor typed, the person's own: whom, what and when, and its text. Mail to the team
        // repeats the answers above and names others, so it stays out.
        json.WriteStartArray("emails");
        foreach (var one in mail)
        {
            json.WriteStartObject();
            json.WriteString("to", one.To);
            json.WriteString("subject", one.Subject);
            json.WriteString("queued", one.CreatedUtc);
            if (one.SentUtc is { } sent)
            {
                json.WriteString("sent", sent);
            }

            json.WriteString("text", one.TextBody);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
    }

    private static void Optional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json.WriteString(name, value);
        }
    }
}
