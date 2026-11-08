using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Exports;

/// <summary>
/// The pieces of the exports (ADR-0210) that need no site: the formula guard and CSV's quoting, the workbook's column
/// letters, escaping and sheet names, the file's name, the formats' collection, a background export's stored record,
/// and the management API's words for the delimiter and the headers.
/// </summary>
[TestFixture]
public sealed class ExportFormatTests
{
    [TestCase("=SUM(A1)", "'=SUM(A1)")]
    [TestCase("+31 20 123", "'+31 20 123")]
    [TestCase("-5", "'-5")]
    [TestCase("@cmd", "'@cmd")]
    [TestCase("\tTab", "\"'\tTab\"")]
    [TestCase("\rReturn", "\"'\rReturn\"")]
    [TestCase("Ada", "Ada")]
    [TestCase("a,b", "\"a,b\"")]
    [TestCase("a;b", "\"a;b\"")]
    [TestCase("a\tb", "\"a\tb\"")]
    [TestCase("x;=1+1", "\"x;=1+1\"")]
    [TestCase("x,=1+1", "\"x,=1+1\"")]
    [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [TestCase("two\nlines", "\"two\nlines\"")]
    [TestCase("", "")]
    public void ACsvCell_IsGuardedAgainstFormulas_AndQuotedWhereAnyDelimiterWouldSplitIt(string text, string written) =>
        Assert.That(CsvExportFormat.Cell(text), Is.EqualTo(written));

    [TestCase(0, "A")]
    [TestCase(25, "Z")]
    [TestCase(26, "AA")]
    [TestCase(701, "ZZ")]
    [TestCase(702, "AAA")]
    public void AColumn_IsNamedByItsLetters(int index, string letters) => Assert.That(XlsxSheet.Column(index), Is.EqualTo(letters));

    [Test]
    public void ACellsText_IsEscaped_LosesWhatXmlRefuses_AndStopsAtItsLimit()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        Assert.Multiple(() =>
        {
            Assert.That(XlsxSheet.Escape("<a href=\"x\">&</a>"), Is.EqualTo("&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;"));
            Assert.That(XlsxSheet.Escape("bell\u0007 and \uffff"), Is.EqualTo("bell and "), "characters XML 1.0 refuses");
            Assert.That(XlsxSheet.Escape("lone \ud800 half"), Is.EqualTo("lone  half"), "half a surrogate pair");
            Assert.That(XlsxSheet.Escape("tab\tline\n"), Is.EqualTo("tab\tline\n"));
            Assert.That(XlsxSheet.Escape(emoji), Is.EqualTo(emoji), "a whole pair stays");
            Assert.That(XlsxSheet.Escape(new string('x', XlsxWriter.MaxCellText + 10)), Has.Length.EqualTo(XlsxWriter.MaxCellText));
        });
    }

    [Test]
    public void ASheetsName_IsOneAWorkbookTakes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(XlsxWriter.SheetName("Guests [2026]: a/b?", []), Is.EqualTo("Guests 2026 ab"));
            Assert.That(XlsxWriter.SheetName("'quoted'", []), Is.EqualTo("quoted"));
            Assert.That(XlsxWriter.SheetName("", []), Is.EqualTo("Sheet"));
            Assert.That(XlsxWriter.SheetName(new string('n', 40), []), Has.Length.EqualTo(31));
            Assert.That(XlsxWriter.SheetName("Guests", ["guests"]), Is.EqualTo("Guests (2)"), "unique, whatever the case");
            Assert.That(XlsxWriter.SheetName(new string('n', 40), [new string('n', 31)]), Is.EqualTo(new string('n', 27) + " (2)"));
        });
    }

    [Test]
    public void AnExportsFile_IsNamedByItsFormsAlias()
    {
        var csv = new CsvExportFormat();
        Assert.Multiple(() =>
        {
            Assert.That(EntryExports.FileName("contactUs", csv), Is.EqualTo("contactUs-entries.csv"));
            Assert.That(EntryExports.FileName("../con", new FilesExportFormat()), Is.EqualTo(".._con-entries.zip").Or.EqualTo("_con-entries.zip"));
            Assert.That(EntryExports.FileName(" ", new ExcelExportFormat()), Is.EqualTo("entries-entries.xlsx"));
        });
    }

    [Test]
    public void TheFormats_OfferOneOfEachName_TheLastRegistered_InTheirOrder()
    {
        var mine = new OwnCsv();
        var formats = new FormBuilderExportFormatCollection(() => [new CsvExportFormat(), new ExcelExportFormat(), new JsonExportFormat(), mine]);

        Assert.Multiple(() =>
        {
            Assert.That(formats.Offered().Select(format => format.Name), Is.EqualTo(new[] { "xlsx", "json", "csv" }));
            Assert.That(formats.Find("csv"), Is.SameAs(mine), "a site's own replaces the built-in one");
            Assert.That(formats.Find("CSV"), Is.Null, "names are exact");
            Assert.That(formats.Find(null), Is.Null);
        });
    }

    [Test]
    public void ABackgroundExportsRecord_ReadsBackAsWritten_AndOneThatCannotBeReadIsNone()
    {
        var record = new EntryExportRecord
        {
            Key = Guid.NewGuid(),
            Owner = Guid.NewGuid(),
            Form = Guid.NewGuid(),
            Request = new EntryExportRequest
            {
                Format = "csv",
                Entries = [Guid.NewGuid()],
                Fields = [Guid.NewGuid()],
                Sensitive = true,
                Options = new FormBuilderExportOptions { Delimiter = '\t', Headers = FormBuilderExportHeaders.Aliases, ByteOrderMark = false },
                Filter = new Core.Entries.EntryListFilter { Search = "ada", States = [Core.Persistence.Repositories.EntryState.Approved] },
            },
            Culture = "de-DE",
            State = EntryExportState.Ready,
            Total = 12,
            Done = 12,
            File = new string('a', 32),
            QueuedUtc = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero),
        };

        var read = EntryExportJobs.Read(EntryExportJobs.Write(record))!;

        Assert.Multiple(() =>
        {
            Assert.That(read with { Request = record.Request }, Is.EqualTo(record));
            Assert.That(read.Request.Options, Is.EqualTo(record.Request.Options), "a tab survives");
            Assert.That(read.Request.Entries, Is.EqualTo(record.Request.Entries));
            Assert.That(read.Request.Filter.Search, Is.EqualTo("ada"));
            Assert.That(read.Request.Filter.States, Is.EqualTo(new[] { Core.Persistence.Repositories.EntryState.Approved }));
            Assert.That(EntryExports.ExportedAction, Is.EqualTo("entries.exported"));
            Assert.That(EntryExportJobs.Read("{not json"), Is.Null);
            Assert.That(EntryExportJobs.Read("""{"key":"x"}"""), Is.Null);
            Assert.That(EntryExportJobs.KeyOf(record.Key), Is.EqualTo("jfb.export." + record.Key.ToString("N")));
        });
    }

    [TestCase("comma", "labels", ',', FormBuilderExportHeaders.Labels)]
    [TestCase("semicolon", "aliases", ';', FormBuilderExportHeaders.Aliases)]
    [TestCase("tab", "labels", '\t', FormBuilderExportHeaders.Labels)]
    public void TheApisWords_ForTheDelimiterAndTheHeaders_GoBothWays(string delimiter, string headers, char character, FormBuilderExportHeaders named)
    {
        var problems = new List<DefinitionProblem>();
        var request = EntryExportMapping.Request(new EntryExportRequestModel { Format = "csv", Options = new EntryExportOptionsRequestModel { Delimiter = delimiter, Headers = headers, ByteOrderMark = false } }, problems);
        var back = EntryExportMapping.Model(request.Options);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That((request.Options.Delimiter, request.Options.Headers, request.Options.ByteOrderMark), Is.EqualTo((character, named, false)));
            Assert.That((back.Delimiter, back.Headers, back.ByteOrderMark), Is.EqualTo((delimiter, headers, false)));
        });
    }

    [Test]
    public void WordsTheApiDoesNotKnow_AreRefusedByTheirPaths()
    {
        var problems = new List<DefinitionProblem>();
        _ = EntryExportMapping.Request(new EntryExportRequestModel { Format = "csv", Options = new EntryExportOptionsRequestModel { Delimiter = ",", Headers = "Labels" } }, problems);

        Assert.That(problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("invalid_value", "options.delimiter"), ("invalid_value", "options.headers") }));
    }

    // A site's own CSV, under the built-in one's name.
    private sealed class OwnCsv : IFormBuilderExportFormat
    {
        public string Name => "csv";

        public string DisplayName => "My CSV";

        public string FileExtension => ".csv";

        public string ContentType => "text/csv";

        public Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
