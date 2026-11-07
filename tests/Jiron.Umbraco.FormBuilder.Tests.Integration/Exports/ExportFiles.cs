using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Exports;

/// <summary>A sheet of a workbook as a test reads it: its name and its rows of texts, empty cells as "".</summary>
/// <param name="Name">The sheet's name.</param>
/// <param name="Rows">Its rows, each as long as its last cell.</param>
internal sealed record TestSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Reads the files the exports write, for tests only (ADR-0210): a CSV parser that knows quotes, and a reader of the
/// workbook parts the Excel export writes, by their own relationships, so a file that only looks right is refused.
/// </summary>
internal static class ExportFiles
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>The rows of a CSV text: fields apart by the delimiter, quoted ones with doubled quotes and line breaks.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Csv(string text, char delimiter = ',')
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var letter = text[index];
            if (quoted)
            {
                if (letter == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (letter == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(letter);
                }

                continue;
            }

            if (letter == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (letter == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (letter == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
                index++;
            }
            else
            {
                field.Append(letter);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The sheets of a workbook, in the workbook's order, as its relationships lead to them.</summary>
    public static async Task<IReadOnlyList<TestSheet>> XlsxAsync(byte[] bytes)
    {
        using var read = new MemoryStream(bytes);
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);
        var types = await XmlAsync(archive, "[Content_Types].xml");
        var root = await XmlAsync(archive, "_rels/.rels");
        var office = root.Root!.Elements(Package + "Relationship").Single(relation => ((string)relation.Attribute("Type")!).EndsWith("/officeDocument", StringComparison.Ordinal));
        var workbook = await XmlAsync(archive, (string)office.Attribute("Target")!);
        var relations = (await XmlAsync(archive, "xl/_rels/workbook.xml.rels")).Root!.Elements(Package + "Relationship").ToDictionary(relation => (string)relation.Attribute("Id")!, relation => (string)relation.Attribute("Target")!);
        var sheets = new List<TestSheet>();
        foreach (var sheet in workbook.Root!.Element(Main + "sheets")!.Elements(Main + "sheet"))
        {
            var part = "xl/" + relations[(string)sheet.Attribute(Relations + "id")!];
            if (!types.Root!.Elements().Any(type => (string?)type.Attribute("PartName") == "/" + part))
            {
                throw new InvalidDataException($"The content types do not name {part}.");
            }

            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in (await XmlAsync(archive, part)).Root!.Element(Main + "sheetData")!.Elements(Main + "row"))
            {
                var cells = new List<string>();
                foreach (var cell in row.Elements(Main + "c"))
                {
                    var column = ColumnOf((string)cell.Attribute("r")!);
                    while (cells.Count < column)
                    {
                        cells.Add(string.Empty);
                    }

                    cells.Add((string?)cell.Attribute("t") == "inlineStr" ? string.Concat(cell.Descendants(Main + "t").Select(text => text.Value)) : (string?)cell.Element(Main + "v") ?? string.Empty);
                }

                rows.Add(cells);
            }

            sheets.Add(new TestSheet((string)sheet.Attribute("name")!, rows));
        }

        return sheets;
    }

    /// <summary>The names of a ZIP's entries, in their order.</summary>
    public static async Task<IReadOnlyList<string>> ZipAsync(byte[] bytes)
    {
        using var read = new MemoryStream(bytes);
        await using var archive = await ZipArchive.CreateAsync(read, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null);
        return [.. archive.Entries.Select(entry => entry.FullName)];
    }

    // A cell's column from its place, A1 as 0.
    private static int ColumnOf(string place)
    {
        var column = 0;
        foreach (var letter in place.TakeWhile(char.IsAsciiLetterUpper))
        {
            column = (column * 26) + (letter - 'A' + 1);
        }

        return column - 1;
    }

    private static async Task<XDocument> XmlAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"The workbook has no part {name}.");
        await using var stream = await entry.OpenAsync();
        return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }
}
