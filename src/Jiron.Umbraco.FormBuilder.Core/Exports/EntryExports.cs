using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Exports;

/// <summary>What a user asks an export of a form's entries for (ADR-0210).</summary>
internal sealed record EntryExportRequest
{
    /// <summary>The format's name.</summary>
    public required string Format { get; init; }

    /// <summary>Which entries, as the form's list filters them; ignored when <see cref="Entries"/> names some.</summary>
    public EntryListFilter Filter { get; init; } = new();

    /// <summary>The ticked entries of Export selected\u2026, at most <see cref="EntryExports.MaxSelected"/>; none for the filter's.</summary>
    public IReadOnlyList<Guid> Entries { get; init; } = [];

    /// <summary>The fields, in any order; null for every field.</summary>
    public IReadOnlyList<Guid>? Fields { get; init; }

    /// <summary>Whether sensitive fields go along, which needs <c>viewSensitive</c>.</summary>
    public bool Sensitive { get; init; }

    /// <summary>The delimiter, the headers and the byte-order mark.</summary>
    public FormBuilderExportOptions Options { get; init; } = new();
}

/// <summary>What an export holds before it starts: how many entries, and whether it goes to the background.</summary>
/// <param name="Entries">The number of entries; a lower bound when <paramref name="More"/>.</param>
/// <param name="More">Whether a search found more than it counts.</param>
/// <param name="Background">
/// Whether the export runs in the background rather than in the response: one of more than
/// <see cref="EntryExports.StreamedAtMost"/> entries, and one of a format that writes the entries' files.
/// </param>
internal sealed record EntryExportPlan(int Entries, bool More, bool Background);

/// <summary>Who exports: the user, what they may see, and the language of the export's own words.</summary>
/// <param name="UserKey">The user.</param>
/// <param name="MaySeeSensitive">Whether they hold <c>viewSensitive</c> on the form.</param>
/// <param name="Culture">The user's backoffice language.</param>
internal sealed record EntryExporter(Guid UserKey, bool MaySeeSensitive, string Culture);

/// <summary>A file an export wrote: its name, its media type and the entries it holds.</summary>
/// <param name="FileName">The file's name: the form's alias and the format's extension.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="Entries">The entries it holds.</param>
internal sealed record EntryExportFile(string FileName, string ContentType, int Entries);

/// <summary>
/// Exports a form's entries (ADR-0210): the list's own query or the ticked entries, in keyset pages of 250 entries, each
/// its own gated read (ADR-0003), their answers as the summary page lists them, through a format of
/// <see cref="FormBuilderExportFormatCollection"/>. Sensitive fields go along only when asked for and allowed. Every
/// export is audited, and a finished one is announced.
/// </summary>
/// <param name="forms">The forms and their catalogue.</param>
/// <param name="entries">The entries and their files.</param>
/// <param name="queries">The entries query.</param>
/// <param name="lists">The list's filter, which an export takes.</param>
/// <param name="scopes">Reads a page's answers.</param>
/// <param name="answers">The answers as texts.</param>
/// <param name="documents">Reads stored versions.</param>
/// <param name="options">Resolves a ranking's options.</param>
/// <param name="types">The field types.</param>
/// <param name="formats">The export formats.</param>
/// <param name="store">The files' bytes.</param>
/// <param name="downloads">Which files are held back.</param>
/// <param name="timeZones">The site's time zone.</param>
/// <param name="audit">The audit log.</param>
/// <param name="events">Where a finished export is announced.</param>
/// <param name="logger">Where a file whose bytes are gone is told.</param>
internal sealed partial class EntryExports(
    IFormRepository forms,
    IEntryRepository entries,
    IEntryQueryRepository queries,
    EntryLists lists,
    SubmissionEntries scopes,
    MergeTagAnswerTexts answers,
    FormDocuments documents,
    IFormBuilderOptionService options,
    FieldTypeRegistry types,
    FormBuilderExportFormatCollection formats,
    IFormBuilderFileStore store,
    EntryFileDownloads downloads,
    SiteTimeZones timeZones,
    IAuditRepository audit,
    IEventAggregator events,
    ILogger<EntryExports> logger)
{
    /// <summary>The most entries Export selected\u2026 takes, as the bulk bar's other actions.</summary>
    public const int MaxSelected = 500;

    /// <summary>The entries an export reads at once (ADR-0003).</summary>
    public const int PageSize = 250;

    /// <summary>The most entries an export writes in the response; more go to the background.</summary>
    public const int StreamedAtMost = 10_000;

    /// <summary>The audit action of an export.</summary>
    public const string ExportedAction = "entries.exported";

    /// <summary>
    /// Checks an export and says how many entries it holds and where it runs; null when the form does not exist.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="request">What the user asks for.</param>
    /// <param name="exporter">Who exports.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A format, field, filter or choice the export cannot take, each by its path.</exception>
    public async Task<EntryExportPlan?> PlanAsync(Guid formKey, EntryExportRequest request, EntryExporter exporter, CancellationToken cancellationToken = default)
    {
        if (await PrepareAsync(formKey, request, exporter, cancellationToken) is not { } export)
        {
            return null;
        }

        // A format that writes the entries' files goes to the background whatever the number, as a file of files has no
        // bound: it is stored, and its link saves it as it comes rather than in the browser's memory.
        var files = export.Format.WritesFiles;
        if (export.Selected is { } selected)
        {
            return new EntryExportPlan(selected.Count, More: false, Background: files);
        }

        var count = (await queries.ListAsync(export.Query, null, 1, count: true, cancellationToken)).Count ?? new EntryCount(0, More: false);
        return new EntryExportPlan(count.Value, count.More, Background: files || count.More || count.Value > StreamedAtMost);
    }

    /// <summary>
    /// Writes an export to the output as it reads the entries; null when the form does not exist. The audit has its entry
    /// at the end, also of an export cut off, and a finished export is announced unless the caller announces it once its
    /// file is kept.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="request">What the user asks for.</param>
    /// <param name="exporter">Who exports.</param>
    /// <param name="output">Where the file goes; left open.</param>
    /// <param name="progress">Told the number of entries written after each page; null for none.</param>
    /// <param name="announce">Whether a finished export publishes <see cref="ExportCompletedNotification"/> here.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <exception cref="FormBuilderValidationException">A format, field, filter or choice the export cannot take, each by its path.</exception>
    public async Task<EntryExportFile?> WriteAsync(Guid formKey, EntryExportRequest request, EntryExporter exporter, Stream output, Func<int, Task>? progress = null, bool announce = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (await PrepareAsync(formKey, request, exporter, cancellationToken) is not { } export)
        {
            return null;
        }

        var written = 0;
        var finished = false;
        try
        {
            await export.Format.WriteAsync(
                new FormBuilderExport
                {
                    FormName = export.Form.Name,
                    FormAlias = export.Form.Alias,
                    Culture = exporter.Culture,
                    Columns = export.Columns,
                    Tables = export.Tables,
                    Options = request.Options,
                    Entries = ReadAsync(export, async count =>
                    {
                        written += count;
                        if (progress is not null)
                        {
                            await progress(written);
                        }
                    }, cancellationToken),
                    OpenFileAsync = OpenFileAsync,
                },
                output,
                cancellationToken);
            finished = true;
        }
        finally
        {
            // Whatever the cancellation says, as an export cut off is what this entry is for too.
            await audit.AddAsync(
                new NewAuditEntry(ExportedAction, "form", formKey, exporter.UserKey, new AuditDetails()
                    .AddIdentifier("format", export.Format.Name)
                    .Add("entries", written)
                    .Add("fields", export.FieldCount)
                    .Add("selected", export.Selected is not null)
                    .Add("sensitive", export.Sensitive)
                    .Add("finished", finished)),
                CancellationToken.None);
        }

        if (announce)
        {
            await events.PublishAsync(new ExportCompletedNotification(formKey, export.Format.Name, written, exporter.UserKey), cancellationToken);
        }

        return new EntryExportFile(FileName(export.Form.Alias, export.Format), export.Format.ContentType, written);
    }

    /// <summary>The name of an export's file: the form's alias and the format's extension.</summary>
    public static string FileName(string formAlias, IFormBuilderExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var safe = FileArchiveNames.Safe(string.IsNullOrWhiteSpace(formAlias) ? "entries" : formAlias);
        return $"{safe}-entries{format.FileExtension}";
    }

    // The export, checked: the form, the format, the query or the ticked entries, the columns and the tables.
    private async Task<Prepared?> PrepareAsync(Guid formKey, EntryExportRequest request, EntryExporter exporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exporter);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var problems = new List<DefinitionProblem>();
        var format = formats.Find(request.Format);
        if (format is null)
        {
            problems.Add(new DefinitionProblem("export_format_unknown", "format", "The site has no export format of this name."));
        }

        if (request.Options.Delimiter is not (',' or ';' or '\t'))
        {
            problems.Add(new DefinitionProblem("invalid_value", "options.delimiter", "The delimiter is a comma, a semicolon or a tab."));
        }

        if (request.Sensitive && !exporter.MaySeeSensitive)
        {
            problems.Add(new DefinitionProblem("sensitive_not_allowed", "sensitive", "Sensitive fields go along only for a user who may see them."));
        }

        if (request.Entries.Count > MaxSelected)
        {
            problems.Add(new DefinitionProblem("entries_invalid", "entries", $"An export takes at most {MaxSelected} ticked entries."));
        }

        var query = request.Entries.Count == 0 ? await lists.ExportQueryAsync(form, request.Filter, problems, cancellationToken) : new EntryQuery(form.Id);
        var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
        // Laid out as the entries are shown: by the version the site serves or last served, never by a draft's changes.
        var definition = await documents.ServedOfAsync(form, cancellationToken) is { } version ? documents.Readable(version) : null;
        var known = catalogue.Select(row => row.Values.FieldKey).ToHashSet();
        if (request.Fields is { } chosen)
        {
            for (var index = 0; index < chosen.Count; index++)
            {
                if (!known.Contains(chosen[index]))
                {
                    problems.Add(new DefinitionProblem("field_not_found", $"fields[{index}]", "The form has no such field."));
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var words = ExportWords.Of(exporter.Culture);
        var sensitive = request.Sensitive && exporter.MaySeeSensitive;
        var layout = await LayoutAsync(catalogue, definition, request.Fields?.ToHashSet(), sensitive, words, exporter.Culture, cancellationToken);
        var selected = request.Entries.Count == 0 ? null : await SelectedAsync(form, request.Entries, cancellationToken);
        return new Prepared(form, format!, query, selected, layout.Columns, layout.Specs, layout.Tables, layout.TableSpecs, layout.FileFields, layout.FieldCount, sensitive, words, await timeZones.GetAsync(null, cancellationToken), definition?.Settings.Submission.EntryReference);
    }

    // The ticked entries of the form that a list may hold, in the list's order: newest first.
    private async Task<IReadOnlyList<StoredEntry>> SelectedAsync(StoredForm form, IReadOnlyList<Guid> keys, CancellationToken cancellationToken)
    {
        var found = new List<StoredEntry>();
        foreach (var key in keys.Distinct())
        {
            if (await entries.GetAsync(key, cancellationToken) is { } entry && entry.FormId == form.Id && !entry.IsTest && entry.State != EntryState.Draft)
            {
                found.Add(entry);
            }
        }

        return [.. found.OrderByDescending(entry => entry.SubmittedUtc).ThenByDescending(entry => entry.Id)];
    }

    // The columns and tables: the entry's own, then the chosen fields in the form's order, those only an older version had
    // after them; grids by row, rankings by position, repeatable groups as tables.
    private async Task<Layout> LayoutAsync(IReadOnlyList<CatalogueField> catalogue, FormDefinition? definition, HashSet<Guid>? chosen, bool sensitive, ExportWords words, string culture, CancellationToken cancellationToken)
    {
        var columns = new List<FormBuilderExportColumn>
        {
            new(FormBuilderExportColumnKind.Number, words.Entry, "number"),
            new(FormBuilderExportColumnKind.Reference, words.Reference, "reference"),
            new(FormBuilderExportColumnKind.State, words.State, "state"),
            new(FormBuilderExportColumnKind.Received, words.Received, "received"),
        };
        var specs = new List<ColumnSpec> { new(FormBuilderExportColumnKind.Number), new(FormBuilderExportColumnKind.Reference), new(FormBuilderExportColumnKind.State), new(FormBuilderExportColumnKind.Received) };
        var tables = new List<FormBuilderExportTable>();
        var tableSpecs = new List<TableSpec>();
        var fileFields = new Dictionary<Guid, string>();
        var byKey = catalogue.GroupBy(row => row.Values.FieldKey).ToDictionary(group => group.Key, group => group.First().Values);
        var placed = new HashSet<Guid>();
        var count = 0;

        // A field is sensitive when its version or the catalogue marks it, or the repeatable group it is in.
        bool Marked(Guid key) => byKey.TryGetValue(key, out var row) && row.IsSensitive;
        bool Shows(bool marked) => sensitive || !marked;
        bool Chosen(Guid key) => chosen is null || chosen.Contains(key);
        string LabelOf(Guid key, string fallback) => byKey.TryGetValue(key, out var row) && row.Label.Length > 0 ? row.Label : fallback;
        string AliasOf(FormField field) => field.Alias ?? (byKey.TryGetValue(field.Key, out var row) ? row.Alias : field.Key.ToString("N"));

        foreach (var field in definition?.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields) ?? [])
        {
            if (types.Find(field.Type) is not { HoldsValue: true } || !Chosen(field.Key) || !Shows(field.Sensitive || Marked(field.Key)))
            {
                continue;
            }

            placed.Add(field.Key);
            var alias = AliasOf(field);
            var label = LabelOf(field.Key, alias);
            count++;
            if (MergeTagAnswerTexts.IsRepeater(field))
            {
                var children = field.Fields.Where(child => types.Find(child.Type) is { HoldsValue: true } && Shows(child.Sensitive || Marked(child.Key))).ToList();
                tables.Add(new FormBuilderExportTable(label, alias, [.. children.Select(child => new FormBuilderExportColumn(FormBuilderExportColumnKind.Answer, LabelOf(child.Key, AliasOf(child)), AliasOf(child), AliasOf(child)))]));
                tableSpecs.Add(new TableSpec(field.Key, [.. children.Select(child => child.Key)]));
                foreach (var child in children.Where(child => types.Find(child.Type) is { Storage: FieldStorageKind.File }))
                {
                    fileFields[child.Key] = AliasOf(child);
                }

                continue;
            }

            if (field.Rows.Count > 0)
            {
                foreach (var row in field.Rows)
                {
                    var rowLabel = LabelOf(row.Key, row.Label?.Default ?? string.Empty);
                    columns.Add(new FormBuilderExportColumn(FormBuilderExportColumnKind.GridRow, $"{label}: {rowLabel}", $"{alias}.{field.Rows.ToList().IndexOf(row) + 1}", alias, rowLabel.Length > 0 ? rowLabel : (field.Rows.ToList().IndexOf(row) + 1).ToString(CultureInfo.InvariantCulture)));
                    specs.Add(new ColumnSpec(FormBuilderExportColumnKind.GridRow, field.Key, Row: row.Key));
                }

                continue;
            }

            if (string.Equals(field.Type, "ranking", StringComparison.Ordinal) && await PositionsAsync(field, culture, cancellationToken) is > 0 and var positions)
            {
                for (var position = 1; position <= positions; position++)
                {
                    var place = position.ToString(CultureInfo.InvariantCulture);
                    columns.Add(new FormBuilderExportColumn(FormBuilderExportColumnKind.Position, $"{label}: {place}", $"{alias}.{place}", alias, place));
                    specs.Add(new ColumnSpec(FormBuilderExportColumnKind.Position, field.Key, Position: position));
                }

                continue;
            }

            columns.Add(new FormBuilderExportColumn(FormBuilderExportColumnKind.Answer, label, alias, alias));
            specs.Add(new ColumnSpec(FormBuilderExportColumnKind.Answer, field.Key));
            if (types.Find(field.Type) is { Storage: FieldStorageKind.File })
            {
                fileFields[field.Key] = alias;
            }
        }

        // A field only an older version had, of no repeatable group or grid, last, as the list shows it.
        foreach (var field in EntryLists.Fields(catalogue).Where(field => field.Removed && field.Parent is null && field.Kind != EntryListFieldKind.Group && !placed.Contains(field.Key)))
        {
            if (!Chosen(field.Key) || !Shows(field.Sensitive || Marked(field.Key)))
            {
                continue;
            }

            count++;
            columns.Add(new FormBuilderExportColumn(FormBuilderExportColumnKind.Answer, field.Label, field.Alias, field.Alias));
            specs.Add(new ColumnSpec(FormBuilderExportColumnKind.Answer, field.Key));
            if (field.Kind == EntryListFieldKind.File)
            {
                fileFields[field.Key] = field.Alias;
            }
        }

        return new Layout(columns, specs, tables, tableSpecs, fileFields, count);
    }

    // How many places a ranking's answer has: as many as its options.
    private async Task<int> PositionsAsync(FormField ranking, string culture, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await options.GetAsync(ranking, new FormBuilderOptionContext(culture), cancellationToken);
            return resolved.IsAvailable ? resolved.Options.Count : 0;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            LogNoPositions(logger, exception.GetType().Name);
            return 0;
        }
    }

    // The entries a page at a time, each page its own read, told to the progress after it is written.
    private async IAsyncEnumerable<FormBuilderExportEntry> ReadAsync(Prepared export, Func<int, Task> wrote, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (export.Selected is { } selected)
        {
            foreach (var chunk in selected.Chunk(PageSize))
            {
                foreach (var entry in await EntriesAsync(export, chunk, cancellationToken))
                {
                    yield return entry;
                }

                await wrote(chunk.Length);
            }

            yield break;
        }

        EntryCursor? after = null;
        do
        {
            var page = await queries.ListAsync(export.Query, after, PageSize, count: false, cancellationToken);
            foreach (var entry in await EntriesAsync(export, page.Entries, cancellationToken))
            {
                yield return entry;
            }

            await wrote(page.Entries.Count);
            after = page.Next;
        }
        while (after is not null);
    }

    // A page's entries as the export writes them: the answers read for the page at once.
    private async Task<IReadOnlyList<FormBuilderExportEntry>> EntriesAsync(Prepared export, IReadOnlyList<StoredEntry> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var read = await scopes.ScopesAsync(export.Form, page, cancellationToken);
        var zones = new Dictionary<Guid, TimeZoneInfo>();
        var files = export.Format.WritesFiles && export.FileFields.Count > 0
            ? (await entries.ListFilesAsync([.. page.Select(entry => entry.Id)], cancellationToken)).ToLookup(file => file.EntryId)
            : null;
        var made = new List<FormBuilderExportEntry>(page.Count);
        foreach (var entry in page)
        {
            IReadOnlyList<string>[] cells = new IReadOnlyList<string>[export.Specs.Count];
            IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>>[] tables = new IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>>[export.TableSpecs.Count];
            read.TryGetValue(entry.Id, out var scope);

            // In its site's time zone and its version's format, as its receipt and emails gave them; an entry whose version
            // is gone takes the form's.
            var zone = scope is null ? export.TimeZone : await ZoneAsync(zones, scope.Site, cancellationToken);
            var local = TimeZoneInfo.ConvertTime(entry.SubmittedUtc, zone);
            var reference = scope is null ? export.Reference : scope.Form.Settings.Submission.EntryReference;
            var fields = scope is null ? null : FieldsOf(scope.Form);
            var texts = scope is null ? null : await answers.TextsAsync(scope, Showing(export, fields!), cancellationToken);
            var ranked = new Dictionary<Guid, IReadOnlyList<string>>();
            for (var index = 0; index < export.Specs.Count; index++)
            {
                var spec = export.Specs[index];
                var field = fields?.GetValueOrDefault(spec.Field);
                cells[index] = spec.Kind switch
                {
                    FormBuilderExportColumnKind.Number => [entry.Number.ToString(CultureInfo.InvariantCulture)],
                    FormBuilderExportColumnKind.Reference => [EntryReferences.Format(reference, entry.Number, local.Year)],
                    FormBuilderExportColumnKind.State => [export.Words.StateOf(entry.State)],
                    FormBuilderExportColumnKind.Received => [local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)],
                    _ when scope is null || texts is null || field is null => [],
                    FormBuilderExportColumnKind.GridRow => field.Rows.FirstOrDefault(row => row.Key == spec.Row) is { } row ? answers.ExportGridRow(field, row, scope, texts) : [],
                    FormBuilderExportColumnKind.Position => Ranked(ranked, field, scope, texts) is { } order && spec.Position <= order.Count ? [order[spec.Position - 1]] : [],
                    _ => answers.ExportTexts(new AnswerField(field, null), scope, texts),
                };
            }

            for (var index = 0; index < export.TableSpecs.Count; index++)
            {
                var spec = export.TableSpecs[index];
                tables[index] = scope is not null && fields is not null && texts is not null && fields.TryGetValue(spec.Repeater, out var repeater)
                    ? answers.ExportRows(repeater, spec.Children, scope, texts)
                    : [];
            }

            made.Add(new FormBuilderExportEntry(entry.Key, entry.Number, entry.SubmittedUtc, cells, tables, FilesOf(export, files?[entry.Id])));
        }

        return made;
    }

    // A site's time zone, read once a page.
    private async Task<TimeZoneInfo> ZoneAsync(Dictionary<Guid, TimeZoneInfo> zones, Guid? site, CancellationToken cancellationToken)
    {
        var key = site ?? Guid.Empty;
        if (!zones.TryGetValue(key, out var zone))
        {
            zones[key] = zone = await timeZones.GetAsync(site, cancellationToken);
        }

        return zone;
    }

    // A ranking's texts in rank order, read once per entry.
    private IReadOnlyList<string> Ranked(Dictionary<Guid, IReadOnlyList<string>> ranked, FormField field, MergeTagScope scope, ScopeTexts texts)
    {
        if (!ranked.TryGetValue(field.Key, out var order))
        {
            ranked[field.Key] = order = answers.ExportTexts(new AnswerField(field, null), scope, texts);
        }

        return order;
    }

    // The files of an entry the export writes: of the fields it takes, not held back by the scanner.
    private static IReadOnlyList<FormBuilderExportFile> FilesOf(Prepared export, IEnumerable<StoredEntryFile>? files) =>
        files is null
            ? []
            : [.. files.Where(file => export.FileFields.ContainsKey(file.FieldKey) && file.ScanState != FileScanStates.Quarantined)
                .Select(file => new FormBuilderExportFile(file.Key, file.OriginalName, export.FileFields[file.FieldKey]))];

    // The fields of a version by key, a repeatable group's children among them.
    private static Dictionary<Guid, FormField> FieldsOf(FormDefinition definition)
    {
        var fields = new Dictionary<Guid, FormField>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            fields.TryAdd(field.Key, field);
            foreach (var child in field.Fields)
            {
                fields.TryAdd(child.Key, child);
            }
        }

        return fields;
    }

    // The fields whose options' labels the texts need: every field the export writes that the entry's version has.
    private static IEnumerable<AnswerField> Showing(Prepared export, Dictionary<Guid, FormField> fields)
    {
        foreach (var key in export.Specs.Where(spec => spec.Field != Guid.Empty).Select(spec => spec.Field).Distinct())
        {
            if (fields.TryGetValue(key, out var field))
            {
                yield return new AnswerField(field, null);
            }
        }

        foreach (var spec in export.TableSpecs)
        {
            if (fields.TryGetValue(spec.Repeater, out var repeater))
            {
                foreach (var child in repeater.Fields.Where(child => spec.Children.Contains(child.Key)))
                {
                    yield return new AnswerField(child, repeater);
                }
            }
        }
    }

    // A file's bytes for a format that writes files: the ones a download would give, null when they are gone.
    private async Task<Stream?> OpenFileAsync(FormBuilderExportFile file, CancellationToken cancellationToken)
    {
        if (await entries.GetFileAsync(file.Key, cancellationToken) is not { } stored || downloads.IsHeld(stored))
        {
            return null;
        }

        var content = await store.OpenReadAsync(stored.StoredName, cancellationToken);
        if (content is null)
        {
            LogBytesGone(logger, file.Key);
        }

        return content;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder left the file {FileKey} out of an export: its bytes are gone")]
    private static partial void LogBytesGone(ILogger logger, Guid fileKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder exports a ranking in one column: its options could not be read ({ExceptionType})")]
    private static partial void LogNoPositions(ILogger logger, string exceptionType);

    // A column's source: the entry's own, or a field, a grid's row or a ranking's position.
    private sealed record ColumnSpec(FormBuilderExportColumnKind Kind, Guid Field = default, Guid Row = default, int Position = 0);

    // A table's source: a repeatable group and the children it writes.
    private sealed record TableSpec(Guid Repeater, IReadOnlyList<Guid> Children);

    private sealed record Layout(
        IReadOnlyList<FormBuilderExportColumn> Columns,
        IReadOnlyList<ColumnSpec> Specs,
        IReadOnlyList<FormBuilderExportTable> Tables,
        IReadOnlyList<TableSpec> TableSpecs,
        IReadOnlyDictionary<Guid, string> FileFields,
        int FieldCount);

    private sealed record Prepared(
        StoredForm Form,
        IFormBuilderExportFormat Format,
        EntryQuery Query,
        IReadOnlyList<StoredEntry>? Selected,
        IReadOnlyList<FormBuilderExportColumn> Columns,
        IReadOnlyList<ColumnSpec> Specs,
        IReadOnlyList<FormBuilderExportTable> Tables,
        IReadOnlyList<TableSpec> TableSpecs,
        IReadOnlyDictionary<Guid, string> FileFields,
        int FieldCount,
        bool Sensitive,
        ExportWords Words,
        TimeZoneInfo TimeZone,
        EntryReferenceFormat? Reference);
}

/// <summary>The words an export writes itself, in English or German, by the exporting user's language.</summary>
/// <param name="Entry">The number's column.</param>
/// <param name="Reference">The reference's column.</param>
/// <param name="State">The state's column.</param>
/// <param name="Received">The received column.</param>
/// <param name="States">The states, as the lists name them (copy.md).</param>
internal sealed record ExportWords(string Entry, string Reference, string State, string Received, IReadOnlyDictionary<EntryState, string> States)
{
    private static readonly ExportWords English = new("Entry", "Reference", "State", "Received", new Dictionary<EntryState, string>
    {
        [EntryState.Draft] = "Draft",
        [EntryState.Unverified] = "Waiting for confirmation",
        [EntryState.Submitted] = "Waiting for approval",
        [EntryState.Approved] = "Approved",
        [EntryState.Rejected] = "Rejected",
        [EntryState.Spam] = "Spam",
    }.ToFrozenDictionary());

    private static readonly ExportWords German = new("Eintrag", "Referenz", "Status", "Eingegangen", new Dictionary<EntryState, string>
    {
        [EntryState.Draft] = "Entwurf",
        [EntryState.Unverified] = "Wartet auf Best\u00e4tigung",
        [EntryState.Submitted] = "Wartet auf Genehmigung",
        [EntryState.Approved] = "Genehmigt",
        [EntryState.Rejected] = "Abgelehnt",
        [EntryState.Spam] = "Spam",
    }.ToFrozenDictionary());

    /// <summary>The words of a language: German for German, English for every other.</summary>
    public static ExportWords Of(string culture) => culture.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? German : English;

    /// <summary>A state's name.</summary>
    public string StateOf(EntryState state) => States.TryGetValue(state, out var name) ? name : state.ToString();
}
