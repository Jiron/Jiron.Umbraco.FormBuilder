using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.Exports;

/// <summary>
/// A format entries export to (section 4.9, ADR-0210), registered through <c>builder.JironFormBuilder().ExportFormats()</c>:
/// it writes an export's columns and entries, and may open their files. CSV, Excel, JSON, the printable view and the ZIP of
/// the files are built in; a site's formats follow them in the export dialog, and the last registration of a name wins,
/// so a site can replace one.
/// </summary>
public interface IFormBuilderExportFormat
{
    /// <summary>The format's name, unique among the formats, such as <c>csv</c>; lowercase letters, digits and dashes.</summary>
    string Name { get; }

    /// <summary>The format's name in the dialog: a text, or <c>#area_key</c> for one of the backoffice's dictionaries.</summary>
    string DisplayName { get; }

    /// <summary>The file's extension, with its dot, such as <c>.csv</c>.</summary>
    string FileExtension { get; }

    /// <summary>The file's media type, such as <c>text/csv</c>.</summary>
    string ContentType { get; }

    /// <summary>Whether the format writes the entries' files rather than their answers, as the ZIP of the files does.</summary>
    bool WritesFiles => false;

    /// <summary>Whether the format takes the delimiter and the byte-order mark the dialog offers, as CSV does; false by default.</summary>
    bool TakesDelimiter => false;

    /// <summary>Whether the format names its columns by labels or by aliases, as the user chooses; a format of files names none.</summary>
    bool TakesHeaders => !WritesFiles;

    /// <summary>
    /// Writes the export. The entries come a page at a time as the export reads them, so a format writes as they come and
    /// never holds them all.
    /// </summary>
    /// <param name="content">The columns, the entries and the choices.</param>
    /// <param name="output">Where the file goes; left open.</param>
    /// <param name="cancellationToken">Cancelled when the export is.</param>
    Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken);
}

/// <summary>How an export names its columns (ADR-0210).</summary>
public enum FormBuilderExportHeaders
{
    /// <summary>By the fields' labels.</summary>
    Labels,

    /// <summary>By the fields' aliases.</summary>
    Aliases,
}

/// <summary>What the user chose beside the format and the fields (ADR-0210).</summary>
public sealed record FormBuilderExportOptions
{
    /// <summary>The delimiter of a CSV file: a comma, a semicolon or a tab.</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>How the columns are named.</summary>
    public FormBuilderExportHeaders Headers { get; init; } = FormBuilderExportHeaders.Labels;

    /// <summary>Whether a CSV file starts with a byte-order mark, so spreadsheet programs read it as UTF-8.</summary>
    public bool ByteOrderMark { get; init; } = true;
}

/// <summary>What a column of an export holds.</summary>
public enum FormBuilderExportColumnKind
{
    /// <summary>The entry's number.</summary>
    Number,

    /// <summary>The entry's reference, such as <c>REQ-2026-00042</c>.</summary>
    Reference,

    /// <summary>The entry's state, in the backoffice's words.</summary>
    State,

    /// <summary>When the entry was received, in the site's time zone.</summary>
    Received,

    /// <summary>A field's answer.</summary>
    Answer,

    /// <summary>One row of a grid's answer.</summary>
    GridRow,

    /// <summary>One position of a ranking's answer.</summary>
    Position,

    /// <summary>The language the entry was sent in, such as <c>de-DE</c>, in which its answers are written (ADR-0228); empty for none.</summary>
    Language,
}

/// <summary>A column of an export, or of a repeatable group's table (ADR-0210).</summary>
/// <param name="Kind">What it holds.</param>
/// <param name="Label">Its name by the field's label, as <see cref="FormBuilderExportHeaders.Labels"/> names it.</param>
/// <param name="Alias">Its name by the field's alias, as <see cref="FormBuilderExportHeaders.Aliases"/> names it.</param>
/// <param name="Field">The field's alias; null for a column of the entry itself.</param>
/// <param name="Part">A grid row's alias or a ranking's position, from 1; null otherwise.</param>
public sealed record FormBuilderExportColumn(FormBuilderExportColumnKind Kind, string Label, string Alias, string? Field = null, string? Part = null)
{
    /// <summary>Its name as the export's headers ask for.</summary>
    /// <param name="headers">How the export names its columns.</param>
    public string Header(FormBuilderExportHeaders headers) => headers == FormBuilderExportHeaders.Aliases ? Alias : Label;
}

/// <summary>A repeatable group's answers, a table of its own with a row per answered row (ADR-0210).</summary>
/// <param name="Label">The group's label.</param>
/// <param name="Alias">The group's alias.</param>
/// <param name="Columns">Its children's columns.</param>
public sealed record FormBuilderExportTable(string Label, string Alias, IReadOnlyList<FormBuilderExportColumn> Columns);

/// <summary>A file of an entry, which a format that writes files opens through <see cref="FormBuilderExport.OpenFileAsync"/>.</summary>
/// <param name="Key">The file's key.</param>
/// <param name="Name">The name the visitor gave it.</param>
/// <param name="Field">The alias of the field it was uploaded to.</param>
public sealed record FormBuilderExportFile(Guid Key, string Name, string Field);

/// <summary>An exported entry: a cell per column, a row list per table, and its files (ADR-0210).</summary>
/// <param name="Key">The entry's key.</param>
/// <param name="Number">Its number within the form.</param>
/// <param name="ReceivedUtc">When it was received.</param>
/// <param name="Cells">A cell per column of the export, each the texts of its values: none when unanswered.</param>
/// <param name="Tables">For each table of the export, the entry's rows, each a cell per column of the table.</param>
/// <param name="Files">Its files the user may have, for a format that writes files; none otherwise.</param>
public sealed record FormBuilderExportEntry(
    Guid Key,
    int Number,
    DateTimeOffset ReceivedUtc,
    IReadOnlyList<IReadOnlyList<string>> Cells,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>>> Tables,
    IReadOnlyList<FormBuilderExportFile> Files);

/// <summary>An export as a format writes it (ADR-0210).</summary>
public sealed class FormBuilderExport
{
    /// <summary>The form's name.</summary>
    public required string FormName { get; init; }

    /// <summary>The form's alias.</summary>
    public required string FormAlias { get; init; }

    /// <summary>The language of the export's own words (its columns' names, the states), the exporting user's, such as <c>en-US</c>.</summary>
    public required string Culture { get; init; }

    /// <summary>The columns, the entry's own first.</summary>
    public required IReadOnlyList<FormBuilderExportColumn> Columns { get; init; }

    /// <summary>The repeatable groups' tables, in the order of the form.</summary>
    public required IReadOnlyList<FormBuilderExportTable> Tables { get; init; }

    /// <summary>What the user chose.</summary>
    public required FormBuilderExportOptions Options { get; init; }

    /// <summary>The entries, read a page at a time as they are asked for; read once.</summary>
    public required IAsyncEnumerable<FormBuilderExportEntry> Entries { get; init; }

    /// <summary>Opens a file of an entry, or null when its bytes are gone; the caller closes it.</summary>
    public required Func<FormBuilderExportFile, CancellationToken, Task<Stream?>> OpenFileAsync { get; init; }
}

/// <summary>The export formats a site has, in the order they are registered (ADR-0210).</summary>
public sealed class FormBuilderExportFormatCollection : BuilderCollectionBase<IFormBuilderExportFormat>
{
    /// <summary>A collection of the formats the builder creates.</summary>
    public FormBuilderExportFormatCollection(Func<IEnumerable<IFormBuilderExportFormat>> items)
        : base(items)
    {
    }

    /// <summary>The format of a name, the last registered when there are several; null when the site has none.</summary>
    /// <param name="name">The name.</param>
    public IFormBuilderExportFormat? Find(string? name) => this.LastOrDefault(format => string.Equals(format.Name, name, StringComparison.Ordinal));

    /// <summary>The formats the dialog offers, one per name, the last registered of each, in the order they came.</summary>
    public IReadOnlyList<IFormBuilderExportFormat> Offered() => [.. this.Where(format => ReferenceEquals(Find(format.Name), format))];
}

/// <summary>
/// The ordered collection of export formats (ADR-0210), reached through <c>builder.JironFormBuilder().ExportFormats()</c>. It
/// holds the built-in ones from the moment it is made.
/// </summary>
public sealed class FormBuilderExportFormatCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderExportFormatCollectionBuilder, FormBuilderExportFormatCollection, IFormBuilderExportFormat>
{
    /// <summary>A builder that holds the built-in formats.</summary>
    public FormBuilderExportFormatCollectionBuilder() =>
        Append([typeof(CsvExportFormat), typeof(ExcelExportFormat), typeof(JsonExportFormat), typeof(PrintExportFormat), typeof(FilesExportFormat)]);

    /// <inheritdoc />
    protected override FormBuilderExportFormatCollectionBuilder This => this;
}
