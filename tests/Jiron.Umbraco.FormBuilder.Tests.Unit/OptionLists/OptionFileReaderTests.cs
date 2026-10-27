using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// An upload of options (ADR-0076): CSV with <c>,</c> or <c>;</c> and TXT with <c>|</c>, quoted parts, a byte order mark,
/// empty lines and a header skipped, and each line held to the checks of a list's items, the whole file refused with
/// every problem at <c>$.file</c> and its line.
/// </summary>
[TestFixture]
public sealed class OptionFileReaderTests
{
    private static readonly Guid ListKey = Guid.Parse("73000000-0000-4000-8000-000000000050");

    [Test]
    public void ACsvFile_HoldsAValueAndALabel_PerLine_SeparatedByAComma()
    {
        var read = Read("s,Small\r\nm,Medium\nl\rxl,\n", OptionFileFormat.Csv);

        Assert.That(Pairs(read), Is.EqualTo(new[] { ("s", "Small"), ("m", "Medium"), ("l", (string?)null), ("xl", null) }), "every kind of line end, and a label left out or empty");
    }

    [Test]
    public void ACsvFile_SeparatedBySemicolons_SaysSoOnItsFirstLine()
    {
        var semicolons = Read("\"a,b\";First, of all\nc;Second, too", OptionFileFormat.Csv);
        var noneOnTheFirstLine = Read("plain\nm,Medium", OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(semicolons), Is.EqualTo(new[] { ("a,b", "First, of all"), ("c", "Second, too") }), "the first separator outside quotes");
            Assert.That(Pairs(noneOnTheFirstLine), Is.EqualTo(new[] { ("plain", (string?)null), ("m", "Medium") }), "a comma when the first line has none");
        });
    }

    [Test]
    public void ATxtFile_SeparatesTheValueAndTheLabelWithABar()
    {
        var read = Read("s|Small, or tiny; really\n\"a|b\"|Quoted\nm", OptionFileFormat.Txt);

        Assert.That(Pairs(read), Is.EqualTo(new[] { ("s", "Small, or tiny; really"), ("a|b", "Quoted"), ("m", (string?)null) }));
    }

    [Test]
    public void AQuotedPart_HoldsTheSeparatorAndQuotesWrittenAsTwo_AndSpacesAroundPartsAreLeftOut()
    {
        var read = Read("\"Say \"\"hi\"\"\",\"A, B\"\n  spaced  ,  Label  \n\t\"tab\"\t,\"\"\n\"\",Empty value", OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(read.Succeeded, Is.False, "the last line's value is empty");
            Assert.That(read.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 4: An option's value has at least one character.") }), "once");
            Assert.That(Pairs(Read("\"Say \"\"hi\"\"\",\"A, B\"\n  spaced  ,  Label  \n\t\"tab\"\t,\"\"", OptionFileFormat.Csv)), Is.EqualTo(new[] { ("Say \"hi\"", "A, B"), ("spaced", "Label"), ("tab", (string?)null) }));
        });
    }

    [Test]
    public void AQuoteLeftOpen_OrTextAfterAQuotedPart_IsRefusedAtItsLine()
    {
        var read = Read("ok,Fine\n\"open,Label\n\"closed\"x,Label\n\"closed\" ,Label", OptionFileFormat.Csv);

        Assert.That(read.Problems, Is.EqualTo(new[]
        {
            new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 2: A quote is left open: close it, and write a quote inside quotes as two."),
            new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 3: Text follows a quoted part: put the whole part in quotes."),
        }));
    }

    [Test]
    public void AByteOrderMark_IsSkipped_AndAnyOtherEncodingRefused()
    {
        var marked = OptionFileReader.Read([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("value,label\ns,Small")], OptionFileFormat.Csv);
        var latin1 = OptionFileReader.Read([(byte)'s', 0xFC, (byte)',', (byte)'S'], OptionFileFormat.Csv);
        var utf16 = OptionFileReader.Read([0xFF, 0xFE, (byte)'s', 0, (byte)',', 0, (byte)'S', 0], OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(marked), Is.EqualTo(new[] { ("s", "Small") }), "the header after the mark is a header");
            Assert.That(latin1.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "The file is not UTF-8 text: save it as UTF-8 (a spreadsheet calls it CSV UTF-8).") }));
            Assert.That(utf16.Succeeded, Is.False);
        });
    }

    [Test]
    public void EmptyLines_AreSkipped_AndCountedInTheLineNumbers()
    {
        var read = Read("\n\ns,Small\n   \n\nm,Medium\nM,Again\n", OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(read.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.OptionValueDuplicate, "$.file", "Line 7: The value repeats that of line 6 once letter case is ignored.") }));
            Assert.That(Pairs(Read("\n\ns,Small\n   \n\nm,Medium\n\n", OptionFileFormat.Csv)), Is.EqualTo(new[] { ("s", "Small"), ("m", "Medium") }));
        });
    }

    [Test]
    public void AFirstLineOfValueAndLabel_IsAHeader_AndOnlyTheFirst()
    {
        var header = Read("Value;LABEL\ns;Small", OptionFileFormat.Csv);
        var later = Read("s,Small\nvalue,label", OptionFileFormat.Csv);
        var txt = Read("value|label\ns|Small", OptionFileFormat.Txt);
        var notAHeader = Read("value,Value", OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(header), Is.EqualTo(new[] { ("s", "Small") }));
            Assert.That(Pairs(later), Is.EqualTo(new[] { ("s", "Small"), ("value", "label") }));
            Assert.That(Pairs(txt), Is.EqualTo(new[] { ("s", "Small") }));
            Assert.That(Pairs(notAHeader), Is.EqualTo(new[] { ("value", "Value") }));
        });
    }

    [TestCase("", Description = "nothing")]
    [TestCase("\n \n\t\n", Description = "empty lines")]
    [TestCase("value,label\n", Description = "a header")]
    public void AFileWithNoOptions_IsRefused(string text) =>
        Assert.That(Read(text, OptionFileFormat.Csv).Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "The file holds no options: write one option per line, its value and then its label.") }));

    [Test]
    public void ARepeatedValue_IsRefused_NamingBothLines()
    {
        var read = Read("a,One\nb,Two\nA,Again\n\"b\",Twice", OptionFileFormat.Csv);

        Assert.That(read.Problems, Is.EqualTo(new[]
        {
            new DefinitionProblem(DefinitionErrors.OptionValueDuplicate, "$.file", "Line 3: The value repeats that of line 1 once letter case is ignored."),
            new DefinitionProblem(DefinitionErrors.OptionValueDuplicate, "$.file", "Line 4: The value repeats that of line 2 once letter case is ignored."),
        }));
    }

    [Test]
    public void AFile_HoldsAtMost10000Options()
    {
        var most = string.Join('\n', Enumerable.Range(1, OptionListService.MaxOptions).Select(i => $"v{i},Option {i}"));

        var fits = Read(most, OptionFileFormat.Csv);
        var over = Read(most + "\n\nv10001,One more\nv10002,And another", OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(fits), Has.Count.EqualTo(10_000));
            Assert.That(over.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.LimitExceeded, "$.file", "A file holds at most 10,000 options; line 10002 is one more.") }));
        });
    }

    [Test]
    public void AValueOrLabel_IsHeldToTheChecksOfAListsItems()
    {
        var longest = new string('v', DefinitionSaveChecks.OptionValueLength);

        var fits = Read($"{longest},Label", OptionFileFormat.Csv);
        var read = Read($"{longest}v,Too long\nfine,Label\n\"two{(char)0x2028}lines\",Label\nok,\"A{(char)0x2028}label\"", OptionFileFormat.Csv);

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(fits), Is.EqualTo(new[] { (longest, "Label") }), "200 characters");
            Assert.That(read.Problems, Is.EqualTo(new[]
            {
                new DefinitionProblem(DefinitionErrors.LimitExceeded, "$.file", "Line 1: The text has at most 200 characters."),
                new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 3: The text has no line break."),
                new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 4: The text has no line break."),
            }));
            Assert.That(read.Problems, Has.None.Matches<DefinitionProblem>(problem => problem.Message.Contains("$.items", StringComparison.Ordinal)), "a line's problem names no path of the items");
        });
    }

    [Test]
    public void AValue_IsCleanedAsAnswersAre()
    {
        var read = Read("\" padded \",\" Label \"", OptionFileFormat.Csv);

        Assert.That(Pairs(read), Is.EqualTo(new[] { ("padded", " Label ") }), "a label is written as it is");
    }

    [Test]
    public void ALine_HoldsAValueAndALabel_NoMore_ThoughEmptyColumnsAfterThemAreLeftOut()
    {
        var read = Read("a,b,c\nd,,f\ng,h,,\n", OptionFileFormat.Csv);
        var txt = Read("a|b|c", OptionFileFormat.Txt);

        Assert.Multiple(() =>
        {
            Assert.That(read.Problems, Is.EqualTo(new[]
            {
                new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 1: A line holds a value and a label, no more: quote a part that holds ','."),
                new DefinitionProblem(DefinitionErrors.InvalidValue, "$.file", "Line 2: A line holds a value and a label, no more: quote a part that holds ','."),
            }));
            Assert.That(Pairs(Read("g,h,,\n", OptionFileFormat.Csv)), Is.EqualTo(new[] { ("g", "h") }), "as a spreadsheet writes empty columns");
            Assert.That(txt.Problems.Single().Message, Is.EqualTo("Line 1: A line holds a value and a label, no more: quote a part that holds '|'."));
        });
    }

    [Test]
    public void AFile_HoldsAtMost4MiB()
    {
        var most = new byte[OptionFileReader.MaxBytes];
        Array.Fill(most, (byte)'\n');
        "a,A"u8.CopyTo(most);

        Assert.Multiple(() =>
        {
            Assert.That(OptionFileReader.MaxBytes, Is.EqualTo(4 * 1024 * 1024));
            Assert.That(Pairs(OptionFileReader.Read(most, OptionFileFormat.Csv)), Is.EqualTo(new[] { ("a", "A") }));
            Assert.That(OptionFileReader.Read(new byte[OptionFileReader.MaxBytes + 1], OptionFileFormat.Csv).Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.LimitExceeded, "$.file", "A file of options holds at most 4 MiB.") }));
        });
    }

    [TestCase("options.csv", "Csv")]
    [TestCase("Options.CSV", "Csv")]
    [TestCase("lists/options.txt", "Txt")]
    [TestCase("options.TXT", "Txt")]
    [TestCase("options.json", null)]
    [TestCase("options.csv.exe", null)]
    [TestCase("options", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void AFilesName_SaysItsFormat(string? name, string? format) =>
        Assert.That(OptionFileReader.FormatOf(name!)?.ToString(), Is.EqualTo(format));

    [Test]
    public async Task AFileRead_IsAFileListsItems_WithItsOptionsInOrder()
    {
        var read = Read("value;label\nch;Switzerland\nde;Germany\nat", OptionFileFormat.Csv);
        var site = new OptionListTestSite();

        var created = await site.Lists.CreateAsync(ListKey, new OptionListInput("countries", "Countries", OptionListKind.File, Items: read.Items!));
        var resolved = await site.Options().GetListAsync(ListKey, new FormBuilderOptionContext("en"));

        Assert.Multiple(() =>
        {
            Assert.That(read.Items, Is.EqualTo("""[{"value":"ch","label":{"":"Switzerland"},"parentValues":[]},{"value":"de","label":{"":"Germany"},"parentValues":[]},{"value":"at","parentValues":[]}]"""), "as a list's items are written");
            Assert.That(created.Problems, Is.Empty);
            Assert.That(OptionListTesting.Pairs(resolved), Is.EqualTo(new[] { ("ch", "Switzerland"), ("de", "Germany"), ("at", (string?)null) }));
        });
    }

    private static OptionFileResult Read(string text, OptionFileFormat format) => OptionFileReader.Read(Encoding.UTF8.GetBytes(text), format);

    private static IReadOnlyList<(string Value, string? Label)> Pairs(OptionFileResult read)
    {
        Assert.That(read.Problems, Is.Empty);
        using var items = JsonDocument.Parse(read.Items!);
        return [.. items.RootElement.EnumerateArray().Select(item => (item.GetProperty("value").GetString()!, item.TryGetProperty("label", out var label) ? label.GetProperty(string.Empty).GetString() : null))];
    }
}
