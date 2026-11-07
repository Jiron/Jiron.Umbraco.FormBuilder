using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Core.Exports;

/// <summary>What the built-in formats share: a cell's texts as one text, and a repeatable group's rows as lines.</summary>
internal static class ExportTexts
{
    /// <summary>The texts of a cell's values as one text: apart by ", ".</summary>
    public static string Joined(IReadOnlyList<string> texts) => texts.Count switch
    {
        0 => string.Empty,
        1 => texts[0],
        _ => string.Join(", ", texts),
    };

    /// <summary>A repeatable group's row as one line: its answered columns as <c>Label: answer</c>, apart by "; ".</summary>
    public static string Row(FormBuilderExportTable table, IReadOnlyList<IReadOnlyList<string>> row, FormBuilderExportHeaders headers) =>
        string.Join("; ", table.Columns.Select((column, index) => (column, text: Joined(row[index])))
            .Where(each => each.text.Length > 0)
            .Select(each => each.column.Header(headers) + ": " + each.text));

    /// <summary>A repeatable group's rows as lines, one per row.</summary>
    public static string Rows(FormBuilderExportTable table, IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> rows, FormBuilderExportHeaders headers) =>
        string.Join("\n", rows.Select(row => Row(table, row, headers)));
}

/// <summary>
/// CSV (ADR-0210): a header row and a row per entry, with the chosen delimiter and a byte-order mark when asked for; every
/// cell that a spreadsheet program would read as a formula gets a leading <c>'</c>, the formula guard (ADR-0003).
/// </summary>
internal sealed class CsvExportFormat : IFormBuilderExportFormat
{
    /// <inheritdoc />
    public string Name => "csv";

    /// <inheritdoc />
    public string DisplayName => "#jfbExport_formatCsv";

    /// <inheritdoc />
    public string FileExtension => ".csv";

    /// <inheritdoc />
    public string ContentType => "text/csv";

    /// <inheritdoc />
    public bool TakesDelimiter => true;

    /// <inheritdoc />
    public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var options = content.Options;
        await using var writer = new StreamWriter(output, new UTF8Encoding(options.ByteOrderMark), 1 << 16, leaveOpen: true) { NewLine = "\r\n" };
        await WriteRowAsync(writer, [.. content.Columns.Select(column => column.Header(options.Headers)), .. content.Tables.Select(table => options.Headers == FormBuilderExportHeaders.Aliases ? table.Alias : table.Label)], options.Delimiter);
        await foreach (var entry in content.Entries.WithCancellation(cancellationToken))
        {
            var cells = new List<string>(content.Columns.Count + content.Tables.Count);
            cells.AddRange(entry.Cells.Select(ExportTexts.Joined));
            cells.AddRange(content.Tables.Select((table, index) => ExportTexts.Rows(table, entry.Tables[index], options.Headers)));
            await WriteRowAsync(writer, cells, options.Delimiter);
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>A cell as CSV writes it: guarded against formulas, and quoted where it holds the delimiter, a quote or a line break.</summary>
    /// <param name="text">The cell's text.</param>
    /// <param name="delimiter">The delimiter.</param>
    public static string Cell(string text, char delimiter)
    {
        var guarded = text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
        return guarded.IndexOfAny([delimiter, '"', '\r', '\n']) < 0 ? guarded : "\"" + guarded.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static Task WriteRowAsync(StreamWriter writer, IReadOnlyList<string> cells, char delimiter) =>
        writer.WriteLineAsync(string.Join(delimiter, cells.Select(cell => Cell(cell, delimiter))));
}

/// <summary>
/// Excel (ADR-0210): an Office Open XML workbook from Jiron Form Builder's own small writer, a sheet of the entries and a
/// sheet for each repeatable group, with the entry's number on each of its rows.
/// </summary>
internal sealed class ExcelExportFormat : IFormBuilderExportFormat
{
    /// <inheritdoc />
    public string Name => "xlsx";

    /// <inheritdoc />
    public string DisplayName => "#jfbExport_formatExcel";

    /// <inheritdoc />
    public string FileExtension => ".xlsx";

    /// <inheritdoc />
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <inheritdoc />
    public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var headers = content.Options.Headers;
        var number = content.Columns.FirstOrDefault(column => column.Kind == FormBuilderExportColumnKind.Number);
        await using var workbook = await XlsxWriter.CreateAsync(output, cancellationToken);
        await using var tables = new XlsxTableSheets(content.Tables.Count);
        var entries = await workbook.BeginSheetAsync(content.FormName, [.. content.Columns.Select(column => column.Header(headers))], cancellationToken);
        for (var index = 0; index < content.Tables.Count; index++)
        {
            await tables.BeginAsync(index, [number?.Header(headers) ?? "#", "#", .. content.Tables[index].Columns.Select(column => column.Header(headers))], cancellationToken);
        }

        await foreach (var entry in content.Entries.WithCancellation(cancellationToken))
        {
            await entries.WriteRowAsync([.. content.Columns.Select((column, index) => XlsxCell.Of(column.Kind == FormBuilderExportColumnKind.Number ? entry.Number : null, ExportTexts.Joined(entry.Cells[index])))], cancellationToken);
            for (var index = 0; index < content.Tables.Count; index++)
            {
                var rows = entry.Tables[index];
                for (var row = 0; row < rows.Count; row++)
                {
                    await tables.WriteRowAsync(index, [XlsxCell.Of(entry.Number, null), XlsxCell.Of(row + 1, null), .. rows[row].Select(cell => XlsxCell.Of(null, ExportTexts.Joined(cell)))], cancellationToken);
                }
            }
        }

        await entries.EndAsync(cancellationToken);
        for (var index = 0; index < content.Tables.Count; index++)
        {
            await workbook.AddSheetAsync(content.Tables[index].Label, await tables.EndAsync(index, cancellationToken), cancellationToken);
        }

        await workbook.CloseAsync(cancellationToken);
    }
}

/// <summary>
/// JSON (ADR-0210): an array of entries, each its number, reference, state, when it was received (ISO 8601 UTC) and its
/// answers by alias: a text, an array of texts where a field takes several values, an object for a grid, an array in rank
/// order for a ranking, and an array of rows for a repeatable group. An unanswered field is left out.
/// </summary>
internal sealed class JsonExportFormat : IFormBuilderExportFormat
{
    /// <inheritdoc />
    public string Name => "json";

    /// <inheritdoc />
    public string DisplayName => "#jfbExport_formatJson";

    /// <inheritdoc />
    public string FileExtension => ".json";

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public bool TakesHeaders => false;

    /// <inheritdoc />
    public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartArray();
        await foreach (var entry in content.Entries.WithCancellation(cancellationToken))
        {
            Write(json, content, entry);
            if (json.BytesPending > 1 << 16)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
    }

    private static void Write(Utf8JsonWriter json, FormBuilderExport content, FormBuilderExportEntry entry)
    {
        json.WriteStartObject();
        json.WriteNumber("number", entry.Number);
        for (var index = 0; index < content.Columns.Count; index++)
        {
            var column = content.Columns[index];
            if (column.Kind == FormBuilderExportColumnKind.Reference)
            {
                json.WriteString("reference", ExportTexts.Joined(entry.Cells[index]));
            }
            else if (column.Kind == FormBuilderExportColumnKind.State)
            {
                json.WriteString("state", ExportTexts.Joined(entry.Cells[index]));
            }
        }

        json.WriteString("received", entry.ReceivedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteStartObject("answers");
        var columns = content.Columns.Select((column, index) => (column, cell: entry.Cells[index])).Where(each => each.column.Field is not null).ToList();
        foreach (var field in columns.GroupBy(each => each.column.Field!))
        {
            var answered = field.Where(each => each.cell.Count > 0).ToList();
            if (answered.Count == 0)
            {
                continue;
            }

            switch (field.First().column.Kind)
            {
                case FormBuilderExportColumnKind.GridRow:
                    json.WriteStartObject(field.Key);
                    foreach (var (column, cell) in answered)
                    {
                        WriteValue(json, column.Part!, cell);
                    }

                    json.WriteEndObject();
                    break;
                case FormBuilderExportColumnKind.Position:
                    json.WriteStartArray(field.Key);
                    foreach (var (_, cell) in answered)
                    {
                        json.WriteStringValue(ExportTexts.Joined(cell));
                    }

                    json.WriteEndArray();
                    break;
                default:
                    WriteValue(json, field.Key, answered[0].cell);
                    break;
            }
        }

        for (var index = 0; index < content.Tables.Count; index++)
        {
            var rows = entry.Tables[index];
            if (rows.Count == 0)
            {
                continue;
            }

            var table = content.Tables[index];
            json.WriteStartArray(table.Alias);
            foreach (var row in rows)
            {
                json.WriteStartObject();
                for (var column = 0; column < table.Columns.Count; column++)
                {
                    if (row[column].Count > 0)
                    {
                        WriteValue(json, table.Columns[column].Field ?? table.Columns[column].Alias, row[column]);
                    }
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    // A text, or an array of texts where there are several.
    private static void WriteValue(Utf8JsonWriter json, string name, IReadOnlyList<string> texts)
    {
        if (texts.Count == 1)
        {
            json.WriteString(name, texts[0]);
            return;
        }

        json.WriteStartArray(name);
        foreach (var text in texts)
        {
            json.WriteStringValue(text);
        }

        json.WriteEndArray();
    }
}

/// <summary>
/// The printable view (ADR-0210): one HTML document without scripts, its texts escaped, each entry a section with its
/// heading, its state and when it was received, and its answers as a table; a repeatable group's rows as a list.
/// </summary>
internal sealed class PrintExportFormat : IFormBuilderExportFormat
{
    private const string Style =
        "body{margin:2rem;font:15px/1.5 system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;color:#1c1f24}"
        + "h1{font-size:1.5rem;margin:0 0 1.5rem}h2{font-size:1.15rem;margin:0}"
        + "section{break-inside:avoid;page-break-inside:avoid;margin:0 0 2rem;padding:0 0 1.5rem;border-bottom:1px solid #dfe2e6}"
        + "p{margin:.25rem 0 .75rem;color:#4b5563}table{border-collapse:collapse;width:100%}"
        + "th,td{text-align:start;vertical-align:top;padding:.35rem .75rem .35rem 0;border-top:1px solid #eceef1}"
        + "th{width:35%;font-weight:600}ol{margin:0;padding-inline-start:1.25rem}"
        + "@media print{body{margin:0}section{border:0}}";

    /// <inheritdoc />
    public string Name => "print";

    /// <inheritdoc />
    public string DisplayName => "#jfbExport_formatPrint";

    /// <inheritdoc />
    public string FileExtension => ".html";

    /// <inheritdoc />
    public string ContentType => "text/html";

    /// <inheritdoc />
    public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), 1 << 16, leaveOpen: true);
        await writer.WriteAsync($"<!DOCTYPE html><html lang=\"{Encode(content.Culture)}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{Encode(content.FormName)}</title><style>{Style}</style></head><body><h1>{Encode(content.FormName)}</h1>\n");
        var headers = content.Options.Headers;
        await foreach (var entry in content.Entries.WithCancellation(cancellationToken))
        {
            var html = new StringBuilder("<section>");
            var heading = new List<string>();
            var details = new List<string>();
            var rows = new StringBuilder();
            for (var index = 0; index < content.Columns.Count; index++)
            {
                var column = content.Columns[index];
                var text = ExportTexts.Joined(entry.Cells[index]);
                switch (column.Kind)
                {
                    case FormBuilderExportColumnKind.Number:
                        heading.Add(column.Label + " #" + text);
                        break;
                    case FormBuilderExportColumnKind.Reference:
                        heading.Add(text);
                        break;
                    case FormBuilderExportColumnKind.State or FormBuilderExportColumnKind.Received:
                        details.Add(text);
                        break;
                    default:
                        if (text.Length > 0)
                        {
                            rows.Append("<tr><th scope=\"row\">").Append(Encode(column.Header(headers))).Append("</th><td>").Append(Encode(text)).Append("</td></tr>");
                        }

                        break;
                }
            }

            for (var index = 0; index < content.Tables.Count; index++)
            {
                var table = content.Tables[index];
                if (entry.Tables[index].Count == 0)
                {
                    continue;
                }

                rows.Append("<tr><th scope=\"row\">").Append(Encode(headers == FormBuilderExportHeaders.Aliases ? table.Alias : table.Label)).Append("</th><td><ol>");
                foreach (var row in entry.Tables[index])
                {
                    rows.Append("<li>").Append(Encode(ExportTexts.Row(table, row, headers))).Append("</li>");
                }

                rows.Append("</ol></td></tr>");
            }

            html.Append("<h2>").Append(Encode(string.Join(" \u00b7 ", heading.Where(part => part.Length > 0)))).Append("</h2>");
            html.Append("<p>").Append(Encode(string.Join(" \u00b7 ", details.Where(part => part.Length > 0)))).Append("</p>");
            if (rows.Length > 0)
            {
                html.Append("<table><tbody>").Append(rows).Append("</tbody></table>");
            }

            html.Append("</section>\n");
            await writer.WriteAsync(html.ToString().AsMemory(), cancellationToken);
        }

        await writer.WriteAsync("</body></html>\n".AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}

/// <summary>
/// The ZIP of the files (ADR-0210): a folder per entry, named by its number, each file under a name that stays in it
/// (ADR-0145); a file whose bytes are gone is left out.
/// </summary>
internal sealed class FilesExportFormat : IFormBuilderExportFormat
{
    /// <inheritdoc />
    public string Name => "files";

    /// <inheritdoc />
    public string DisplayName => "#jfbExport_formatFiles";

    /// <inheritdoc />
    public string FileExtension => ".zip";

    /// <inheritdoc />
    public string ContentType => "application/zip";

    /// <inheritdoc />
    public bool WritesFiles => true;

    /// <inheritdoc />
    public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var names = new FileArchiveNames();
        await using var held = new HeldWritesStream(output);
        await using (var archive = await ZipArchive.CreateAsync(held, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
        {
            await foreach (var entry in content.Entries.WithCancellation(cancellationToken))
            {
                foreach (var file in entry.Files)
                {
                    if (await content.OpenFileAsync(file, cancellationToken) is not { } bytes)
                    {
                        continue;
                    }

                    await using (bytes)
                    {
                        var item = archive.CreateEntry(names.NextIn("entry-" + entry.Number.ToString(CultureInfo.InvariantCulture), file.Name), CompressionLevel.Fastest);
                        await using var into = await item.OpenAsync(cancellationToken);
                        await bytes.CopyToAsync(into, cancellationToken);
                    }
                }
            }
        }

        await held.FlushAsync(cancellationToken);
    }
}
