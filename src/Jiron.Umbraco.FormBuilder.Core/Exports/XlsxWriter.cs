using System.Globalization;
using System.IO.Compression;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Core.Exports;

/// <summary>A cell of a sheet: a number, or a text, or empty.</summary>
/// <param name="Number">Its number, written as one.</param>
/// <param name="Text">Its text, when it has no number.</param>
internal readonly record struct XlsxCell(double? Number, string? Text)
{
    /// <summary>A cell of a number, or else of a text.</summary>
    public static XlsxCell Of(int? number, string? text) => new(number, text);
}

/// <summary>
/// An Office Open XML workbook as the Excel export writes it (ADR-0210): a ZIP of its parts, each sheet's rows written as
/// they come, inline strings, a bold header row that stays in view. One sheet is open at a time, as a ZIP writes one part
/// at a time; a sheet written elsewhere joins as a finished part.
/// </summary>
internal sealed class XlsxWriter : IAsyncDisposable
{
    /// <summary>The longest text a cell holds; longer ones are cut there.</summary>
    public const int MaxCellText = 32767;

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private readonly HeldWritesStream _held;
    private readonly ZipArchive _archive;
    private readonly List<string> _sheets = [];
    private bool _closed;

    private XlsxWriter(HeldWritesStream held, ZipArchive archive)
    {
        _held = held;
        _archive = archive;
    }

    /// <summary>A workbook written to the output, which it leaves open.</summary>
    public static async Task<XlsxWriter> CreateAsync(Stream output, CancellationToken cancellationToken)
    {
        var held = new HeldWritesStream(output);
        var archive = await ZipArchive.CreateAsync(held, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        return new XlsxWriter(held, archive);
    }

    /// <summary>Begins the next sheet with its header row; it ends before another begins or joins.</summary>
    /// <param name="name">Its name, made one a workbook takes.</param>
    /// <param name="headers">Its columns' names.</param>
    /// <param name="cancellationToken">Cancels the writing.</param>
    public async Task<XlsxSheet> BeginSheetAsync(string name, IReadOnlyList<string> headers, CancellationToken cancellationToken)
    {
        var entry = _archive.CreateEntry(PartOf(_sheets.Count + 1), CompressionLevel.Fastest);
        _sheets.Add(SheetName(name, _sheets));
        var sheet = new XlsxSheet(await entry.OpenAsync(cancellationToken), ownsStream: true);
        await sheet.BeginAsync(headers, cancellationToken);
        return sheet;
    }

    /// <summary>Adds a sheet written elsewhere, whole, read from its start.</summary>
    /// <param name="name">Its name, made one a workbook takes.</param>
    /// <param name="sheet">Its XML.</param>
    /// <param name="cancellationToken">Cancels the writing.</param>
    public async Task AddSheetAsync(string name, Stream sheet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var entry = _archive.CreateEntry(PartOf(_sheets.Count + 1), CompressionLevel.Fastest);
        _sheets.Add(SheetName(name, _sheets));
        await using var into = await entry.OpenAsync(cancellationToken);
        await sheet.CopyToAsync(into, cancellationToken);
    }

    /// <summary>Writes the workbook's other parts and ends the file.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        await PartAsync("[Content_Types].xml", ContentTypes(), cancellationToken);
        await PartAsync("_rels/.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{Relationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>", cancellationToken);
        await PartAsync("xl/workbook.xml", Workbook(), cancellationToken);
        await PartAsync("xl/_rels/workbook.xml.rels", WorkbookRelationships(), cancellationToken);
        await PartAsync("xl/styles.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"{Main}\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>", cancellationToken);
        await _archive.DisposeAsync();
        await _held.FlushAsync(cancellationToken);
        _closed = true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_closed)
        {
            await _archive.DisposeAsync();
        }

        await _held.DisposeAsync();
    }

    /// <summary>A sheet's name as a workbook takes it: at most 31 letters, none of <c>[]:*?/\</c>, and unique.</summary>
    public static string SheetName(string name, IReadOnlyCollection<string> taken)
    {
        var plain = new string([.. (name ?? string.Empty).Where(letter => letter is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\') && !char.IsControl(letter))]).Trim('\'', ' ');
        var stem = plain.Length == 0 ? "Sheet" : plain[..Math.Min(plain.Length, 31)];
        var made = stem;
        for (var number = 2; taken.Contains(made, StringComparer.OrdinalIgnoreCase); number++)
        {
            var suffix = $" ({number})";
            made = stem[..Math.Min(stem.Length, 31 - suffix.Length)] + suffix;
        }

        return made;
    }

    private static string PartOf(int sheet) => $"xl/worksheets/sheet{sheet.ToString(CultureInfo.InvariantCulture)}.xml";

    private async Task PartAsync(string name, string xml, CancellationToken cancellationToken)
    {
        var entry = _archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var into = await entry.OpenAsync(cancellationToken);
        await into.WriteAsync(Encoding.UTF8.GetBytes(xml), cancellationToken);
    }

    private string ContentTypes()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var sheet = 1; sheet <= _sheets.Count; sheet++)
        {
            xml.Append("<Override PartName=\"/").Append(PartOf(sheet)).Append("\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return xml.Append("</Types>").ToString();
    }

    private string Workbook()
    {
        var xml = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"{Main}\" xmlns:r=\"{Relationships}\"><sheets>");
        for (var sheet = 1; sheet <= _sheets.Count; sheet++)
        {
            xml.Append("<sheet name=\"").Append(XlsxSheet.Escape(_sheets[sheet - 1])).Append("\" sheetId=\"").Append(sheet).Append("\" r:id=\"rId").Append(sheet).Append("\"/>");
        }

        return xml.Append("</sheets></workbook>").ToString();
    }

    private string WorkbookRelationships()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var sheet = 1; sheet <= _sheets.Count; sheet++)
        {
            xml.Append("<Relationship Id=\"rId").Append(sheet).Append("\" Type=\"").Append(Relationships).Append("/worksheet\" Target=\"worksheets/sheet").Append(sheet).Append(".xml\"/>");
        }

        var styles = _sheets.Count + 1;
        return xml.Append("<Relationship Id=\"rId").Append(styles).Append("\" Type=\"").Append(Relationships).Append("/styles\" Target=\"styles.xml\"/></Relationships>").ToString();
    }
}

/// <summary>A sheet's XML as its rows come: the header row bold and kept in view, then a row per call.</summary>
/// <param name="output">Where the sheet's XML goes.</param>
/// <param name="ownsStream">Whether ending the sheet closes the stream.</param>
internal sealed class XlsxSheet(Stream output, bool ownsStream) : IAsyncDisposable
{
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(false), 1 << 16, leaveOpen: !ownsStream);
    private int _row;
    private bool _ended;

    /// <summary>The start of the sheet and its header row.</summary>
    public async Task BeginAsync(IReadOnlyList<string> headers, CancellationToken cancellationToken)
    {
        await _writer.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetData>".AsMemory(), cancellationToken);
        await WriteAsync([.. headers.Select(header => new XlsxCell(null, header))], bold: true, cancellationToken);
    }

    /// <summary>A row of cells.</summary>
    public Task WriteRowAsync(IReadOnlyList<XlsxCell> cells, CancellationToken cancellationToken) => WriteAsync(cells, bold: false, cancellationToken);

    /// <summary>The end of the sheet; the stream is closed when the sheet owns it, and flushed otherwise.</summary>
    public async Task EndAsync(CancellationToken cancellationToken)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        await _writer.WriteAsync("</sheetData></worksheet>".AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
        await _writer.DisposeAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_ended)
        {
            _ended = true;
            await _writer.DisposeAsync();
        }
    }

    /// <summary>A column's letters, such as <c>A</c>, <c>Z</c>, <c>AA</c>, from 0.</summary>
    public static string Column(int index)
    {
        var letters = new StringBuilder();
        for (var left = index + 1; left > 0; left = (left - 1) / 26)
        {
            letters.Insert(0, (char)('A' + ((left - 1) % 26)));
        }

        return letters.ToString();
    }

    /// <summary>A text as XML takes it: escaped, without the characters XML 1.0 refuses, and cut at a cell's limit.</summary>
    public static string Escape(string text)
    {
        var xml = new StringBuilder(Math.Min(text.Length, XlsxWriter.MaxCellText) + 16);
        var kept = 0;
        for (var index = 0; index < text.Length && kept < XlsxWriter.MaxCellText; index++)
        {
            var letter = text[index];
            if (char.IsHighSurrogate(letter) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                xml.Append(letter).Append(text[++index]);
                kept += 2;
                continue;
            }

            if (char.IsSurrogate(letter) || (letter < ' ' && letter is not ('\t' or '\n' or '\r')) || letter is '\ufffe' or '\uffff')
            {
                continue;
            }

            kept++;
            _ = letter switch
            {
                '&' => xml.Append("&amp;"),
                '<' => xml.Append("&lt;"),
                '>' => xml.Append("&gt;"),
                '"' => xml.Append("&quot;"),
                _ => xml.Append(letter),
            };
        }

        return xml.ToString();
    }

    private async Task WriteAsync(IReadOnlyList<XlsxCell> cells, bool bold, CancellationToken cancellationToken)
    {
        var row = ++_row;
        var xml = new StringBuilder("<row r=\"").Append(row).Append("\">");
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            var place = Column(index) + row.ToString(CultureInfo.InvariantCulture);
            if (cell.Number is { } number)
            {
                xml.Append("<c r=\"").Append(place).Append("\"><v>").Append(number.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
            }
            else if (!string.IsNullOrEmpty(cell.Text))
            {
                xml.Append("<c r=\"").Append(place).Append(bold ? "\" s=\"1\"" : "\"").Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Escape(cell.Text)).Append("</t></is></c>");
            }
        }

        xml.Append("</row>");
        await _writer.WriteAsync(xml.ToString().AsMemory(), cancellationToken);
    }
}

/// <summary>
/// The sheets of an export's repeatable groups while the entries' sheet is written: each in a temporary file of its own,
/// deleted when closed, joined to the workbook after the entries' sheet ends.
/// </summary>
/// <param name="count">How many sheets.</param>
internal sealed class XlsxTableSheets(int count) : IAsyncDisposable
{
    private readonly FileStream?[] _files = new FileStream?[count];
    private readonly XlsxSheet?[] _sheets = new XlsxSheet?[count];

    /// <summary>The folder of the temporary files.</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "jfb-exports");

    /// <summary>Begins a group's sheet with its header row.</summary>
    public async Task BeginAsync(int index, IReadOnlyList<string> headers, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var file = new FileStream(Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".xml"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        _files[index] = file;
        _sheets[index] = new XlsxSheet(file, ownsStream: false);
        await _sheets[index]!.BeginAsync(headers, cancellationToken);
    }

    /// <summary>A row of a group's sheet.</summary>
    public Task WriteRowAsync(int index, IReadOnlyList<XlsxCell> cells, CancellationToken cancellationToken) =>
        _sheets[index]!.WriteRowAsync(cells, cancellationToken);

    /// <summary>Ends a group's sheet and gives its XML from its start; the file goes with this instance.</summary>
    public async Task<Stream> EndAsync(int index, CancellationToken cancellationToken)
    {
        await _sheets[index]!.EndAsync(cancellationToken);
        var file = _files[index]!;
        file.Position = 0;
        return file;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var sheet in _sheets.OfType<XlsxSheet>())
        {
            await sheet.DisposeAsync();
        }

        foreach (var file in _files.OfType<FileStream>())
        {
            await file.DisposeAsync();
        }
    }
}
