using System.Globalization;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// A list read from an uploaded file (ADR-0076): its options are its items, as a manual list's, which
/// <see cref="OptionFileReader"/> read from the upload; it has no configuration and is no field's source (ADR-0073).
/// </summary>
internal sealed class FileOptionKind() : OptionKind<FileOptionListSettings>(OptionListKind.File, onField: false, hasItems: true)
{
    /// <inheritdoc />
    protected override ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(FileOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(OptionItems.ToOptions(request.Items, request.Texts, request.Source.Culture));
}

/// <summary>A file list's configuration (ADR-0076): none, as its options are its items.</summary>
public sealed class FileOptionListSettings;

/// <summary>The kinds of file an upload of options may be (ADR-0076).</summary>
internal enum OptionFileFormat
{
    /// <summary>Comma- or semicolon-separated values (<c>.csv</c>).</summary>
    Csv,

    /// <summary>Text with <c>|</c> between the value and the label (<c>.txt</c>).</summary>
    Txt,
}

/// <summary>An upload read: its options as the JSON a list's items are written in, or the problems that refuse it.</summary>
/// <param name="Items">The items; null when the upload is refused.</param>
/// <param name="Problems">Why it is refused, each at <c>$.file</c> with its line.</param>
internal sealed record OptionFileResult(string? Items, IReadOnlyList<DefinitionProblem> Problems)
{
    /// <summary>Whether the upload was read.</summary>
    public bool Succeeded => Items is not null;
}

/// <summary>
/// Reads an uploaded CSV or TXT file into a list's items (ADR-0076): UTF-8 (a byte order mark skipped), one option per
/// line as a value and an optional label, separated by <c>,</c> or <c>;</c> in CSV (whichever comes first outside quotes
/// on the first line) and by <c>|</c> in TXT. A part may be quoted, <c>""</c> standing for a quote, and then holds the
/// separator; spaces around a part are left out, empty lines are skipped, and a first line of <c>value</c> and
/// <c>label</c> is a header. Each value is cleaned as answers are and held to the checks of a list's items. An upload
/// with a problem, or with no options, is refused as a whole, every problem at <c>$.file</c> and naming its line.
/// </summary>
internal static class OptionFileReader
{
    /// <summary>The largest upload.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>Where an upload's problems are.</summary>
    public const string Path = "$.file";

    /// <summary>The format a file's name says, or null for a name that is no CSV or TXT file.</summary>
    public static OptionFileFormat? FormatOf(string fileName) => System.IO.Path.GetExtension(fileName ?? string.Empty).ToUpperInvariant() switch
    {
        ".CSV" => OptionFileFormat.Csv,
        ".TXT" => OptionFileFormat.Txt,
        _ => null,
    };

    /// <summary>The items of an upload, or why it is refused.</summary>
    public static OptionFileResult Read(ReadOnlySpan<byte> bytes, OptionFileFormat format)
    {
        var problems = new DefinitionProblems();
        if (bytes.Length > MaxBytes)
        {
            problems.Add(DefinitionErrors.LimitExceeded, Path, string.Create(CultureInfo.InvariantCulture, $"A file of options holds at most {MaxBytes / (1024 * 1024)} MiB."));
            return new OptionFileResult(null, problems.ToReadOnlyList());
        }

        if (bytes.StartsWith(ByteOrderMark))
        {
            bytes = bytes[ByteOrderMark.Length..];
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            problems.Add(DefinitionErrors.InvalidValue, Path, "The file is not UTF-8 text: save it as UTF-8 (a spreadsheet calls it CSV UTF-8).");
            return new OptionFileResult(null, problems.ToReadOnlyList());
        }

        var options = new List<FieldOption>();
        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        var separator = format == OptionFileFormat.Txt ? '|' : (char?)null;
        var first = true;
        var number = 0;
        foreach (var line in text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            separator ??= CsvSeparator(line);
            var parts = Split(line, separator.Value, out var problem);
            var header = first && parts is [var v, var l] && v.Equals("value", StringComparison.OrdinalIgnoreCase) && l.Equals("label", StringComparison.OrdinalIgnoreCase);
            first = false;
            if (parts is null)
            {
                Fail(problems, DefinitionErrors.InvalidValue, number, problem!);
                continue;
            }

            if (header)
            {
                continue;
            }

            if (parts.Count > 2)
            {
                Fail(problems, DefinitionErrors.InvalidValue, number, $"A line holds a value and a label, no more: quote a part that holds '{separator}'.");
                continue;
            }

            if (options.Count == OptionListService.MaxOptions)
            {
                problems.Add(DefinitionErrors.LimitExceeded, Path, string.Create(CultureInfo.InvariantCulture, $"A file holds at most {OptionListService.MaxOptions:N0} options; line {number} is one more."));
                break;
            }

            var option = new FieldOption { Value = TextSemantics.Clean(parts[0]), Label = parts is [_, { Length: > 0 } label] ? new LocalizedText(label) : null };
            // The checks of a list's items, then, for a line that passes them, those of its value, so a value is refused once.
            var refused = DefinitionSaveChecks.CheckOptionItems([option], OptionListService.MaxOptions, "$.items");
            if (refused.Count == 0)
            {
                refused = DefinitionStructureChecks.CheckOptionValues([option], "$.items");
            }

            foreach (var each in refused)
            {
                Fail(problems, each.Code, number, each.Message);
            }

            if (refused.Count > 0)
            {
                continue;
            }

            if (!lines.TryAdd(TextSemantics.Fold(option.Value), number))
            {
                Fail(problems, DefinitionErrors.OptionValueDuplicate, number, string.Create(CultureInfo.InvariantCulture, $"The value repeats that of line {lines[TextSemantics.Fold(option.Value)]} once letter case is ignored."));
                continue;
            }

            options.Add(option);
        }

        if (problems.Found == 0 && options.Count == 0)
        {
            problems.Add(DefinitionErrors.InvalidValue, Path, "The file holds no options: write one option per line, its value and then its label.");
        }

        if (problems.Found > 0)
        {
            return new OptionFileResult(null, problems.ToReadOnlyList());
        }

        // The items as a field's own options are written, which a list's items are (ADR-0073).
        using var written = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new FieldOptions { Items = options }, DefinitionJsonContext.Default.FieldOptions));
        return new OptionFileResult(written.RootElement.TryGetProperty("items", out var items) ? items.GetRawText() : "[]", []);
    }

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private static void Fail(DefinitionProblems problems, string code, int line, string message) =>
        problems.Add(code, Path, string.Create(CultureInfo.InvariantCulture, $"Line {line}: {message}"));

    // A CSV file's separator: the first comma or semicolon outside quotes on its first line, a comma when there is none.
    private static char CsvSeparator(string line)
    {
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is ',' or ';')
            {
                return c;
            }
        }

        return ',';
    }

    // A line's parts: each without the spaces around it, a quoted one without its quotes, and empty ones past the second
    // left out, as spreadsheets write empty columns. Null, with the problem, for a line that does not read.
    private static List<string>? Split(string line, char separator, out string? problem)
    {
        problem = null;
        var parts = new List<string>();
        var i = 0;
        while (true)
        {
            while (i < line.Length && line[i] is ' ' or '\t')
            {
                i++;
            }

            if (i < line.Length && line[i] == '"')
            {
                var part = new StringBuilder();
                i++;
                while (true)
                {
                    if (i == line.Length)
                    {
                        problem = "A quote is left open: close it, and write a quote inside quotes as two.";
                        return null;
                    }

                    if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        part.Append('"');
                        i += 2;
                    }
                    else if (line[i] == '"')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        part.Append(line[i++]);
                    }
                }

                while (i < line.Length && line[i] is ' ' or '\t')
                {
                    i++;
                }

                if (i < line.Length && line[i] != separator)
                {
                    problem = "Text follows a quoted part: put the whole part in quotes.";
                    return null;
                }

                parts.Add(part.ToString());
            }
            else
            {
                var start = i;
                while (i < line.Length && line[i] != separator)
                {
                    i++;
                }

                parts.Add(line[start..i].Trim());
            }

            if (i == line.Length)
            {
                break;
            }

            i++;
        }

        while (parts.Count > 2 && parts[^1].Length == 0)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return parts;
    }
}
