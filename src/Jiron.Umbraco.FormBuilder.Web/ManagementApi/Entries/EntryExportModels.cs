using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>An export of a form's entries (ADR-0210).</summary>
public sealed class EntryExportRequestModel
{
    /// <summary>The format's name, one of the export view's <c>formats</c>.</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>Which entries, as the form's list filters them; none for every entry a list holds. Ignored when <c>entries</c> names some.</summary>
    public EntryListFilterRequestModel? Filter { get; init; }

    /// <summary>The ticked entries of Export selected\u2026, at most 500; none for the filter's.</summary>
    public IReadOnlyList<Guid>? Entries { get; init; }

    /// <summary>The fields, in any order; none for every field.</summary>
    public IReadOnlyList<Guid>? Fields { get; init; }

    /// <summary>Whether sensitive fields go along, which needs <c>viewSensitive</c> on the form.</summary>
    public bool Sensitive { get; init; }

    /// <summary>The delimiter, the headers and the byte-order mark; none for the defaults.</summary>
    public EntryExportOptionsRequestModel? Options { get; init; }

    /// <summary>
    /// Whether the export dialog opens with these choices next time: the dialog's own exports say so, and the entry
    /// panel's Print does not.
    /// </summary>
    public bool Remember { get; init; }
}

/// <summary>What an export writes beside the format and the fields.</summary>
public sealed class EntryExportOptionsRequestModel
{
    /// <summary>A CSV file's delimiter: <c>comma</c>, <c>semicolon</c> or <c>tab</c>.</summary>
    public string Delimiter { get; init; } = "comma";

    /// <summary>How the columns are named: <c>labels</c> or <c>aliases</c>.</summary>
    public string Headers { get; init; } = "labels";

    /// <summary>Whether a CSV file starts with a byte-order mark, so spreadsheet programs read it as UTF-8.</summary>
    public bool ByteOrderMark { get; init; } = true;
}

/// <summary>What the export dialog opens with.</summary>
public sealed class EntryExportViewResponseModel
{
    /// <summary>The formats the site offers, in the dialog's order.</summary>
    public required IReadOnlyList<EntryExportFormatResponseModel> Formats { get; init; }

    /// <summary>What the user last exported of the form; none until they export it.</summary>
    public EntryExportChoicesResponseModel? Choices { get; init; }

    /// <summary>Whether the user may export sensitive fields: they hold <c>viewSensitive</c> on the form.</summary>
    public bool MaySeeSensitive { get; init; }

    /// <summary>The user's background exports of the form that are under way or ready, newest first.</summary>
    public required IReadOnlyList<EntryExportStatusResponseModel> Exports { get; init; }
}

/// <summary>A format the dialog offers.</summary>
public sealed class EntryExportFormatResponseModel
{
    /// <summary>The format's name, which an export names.</summary>
    public required string Name { get; init; }

    /// <summary>Its name in the dialog: a text, or <c>#area_key</c> for one of the backoffice's dictionaries.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Whether it writes the entries' files rather than their answers, so the dialog offers the file fields only.</summary>
    public bool WritesFiles { get; init; }

    /// <summary>Whether it takes the delimiter and the byte-order mark, as CSV does.</summary>
    public bool TakesDelimiter { get; init; }

    /// <summary>Whether it names its columns by labels or by aliases, as the user chooses.</summary>
    public bool TakesHeaders { get; init; }

    /// <summary>The name the form's file downloads under in this format.</summary>
    public required string FileName { get; init; }
}

/// <summary>What the user last exported of a form.</summary>
public sealed class EntryExportChoicesResponseModel
{
    /// <summary>The format's name.</summary>
    public required string Format { get; init; }

    /// <summary>The fields chosen; none for every field.</summary>
    public IReadOnlyList<Guid>? Fields { get; init; }

    /// <summary>Whether sensitive fields went along.</summary>
    public bool Sensitive { get; init; }

    /// <summary>The delimiter, the headers and the byte-order mark.</summary>
    public required EntryExportOptionsResponseModel Options { get; init; }
}

/// <summary>What the user last chose beside the format and the fields.</summary>
public sealed class EntryExportOptionsResponseModel
{
    /// <summary>A CSV file's delimiter: <c>comma</c>, <c>semicolon</c> or <c>tab</c>.</summary>
    public required string Delimiter { get; init; }

    /// <summary>How the columns are named: <c>labels</c> or <c>aliases</c>.</summary>
    public required string Headers { get; init; }

    /// <summary>Whether a CSV file starts with a byte-order mark.</summary>
    public bool ByteOrderMark { get; init; }
}

/// <summary>How many entries an export holds, and whether it runs in the background.</summary>
public sealed class EntryExportPlanResponseModel
{
    /// <summary>The number of entries; a lower bound when <c>more</c>.</summary>
    public int Entries { get; init; }

    /// <summary>Whether a search found more than it counts.</summary>
    public bool More { get; init; }

    /// <summary>Whether the export runs in the background: more than 10,000 entries, or a search that found more than it counts.</summary>
    public bool Background { get; init; }

    /// <summary>The entries waiting for their email confirmation that the export leaves out, as its filter names no state.</summary>
    public int Unconfirmed { get; init; }
}

/// <summary>A link that downloads a background export's file.</summary>
public sealed class EntryExportTicketResponseModel
{
    /// <summary>The file's address on the site, with its ticket; a link to it downloads the file.</summary>
    public required string Url { get; init; }

    /// <summary>When the link stops working: a minute after it was asked for.</summary>
    public required DateTimeOffset ExpiresUtc { get; init; }
}

/// <summary>A background export, as its owner sees it.</summary>
public sealed class EntryExportStatusResponseModel
{
    /// <summary>The export.</summary>
    public Guid Key { get; init; }

    /// <summary><c>queued</c>, <c>running</c>, <c>ready</c> or <c>failed</c>.</summary>
    public required string State { get; init; }

    /// <summary>The entries the plan counted; a lower bound when <c>more</c>.</summary>
    public int Total { get; init; }

    /// <summary>Whether a search found more than it counts.</summary>
    public bool More { get; init; }

    /// <summary>The entries written so far, or in all once it is ready.</summary>
    public int Done { get; init; }

    /// <summary>The name the file downloads under, once it is ready.</summary>
    public string? FileName { get; init; }

    /// <summary>When the file and the export go, once it is ready or failed.</summary>
    public DateTimeOffset? ExpiresUtc { get; init; }

    /// <summary>Why it failed: <c>form_not_found</c>, <c>export_invalid</c> or <c>export_failed</c>.</summary>
    public string? Problem { get; init; }
}

/// <summary>Between the export's models and the service's.</summary>
internal static class EntryExportMapping
{
    /// <summary>A request as the service takes it; problems by their paths.</summary>
    public static EntryExportRequest Request(EntryExportRequestModel model, List<DefinitionProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(model);
        var filter = EntryListMapping.Filter(model.Filter, "filter", problems);
        var options = model.Options ?? new EntryExportOptionsRequestModel();
        var delimiter = options.Delimiter switch
        {
            "comma" => ',',
            "semicolon" => ';',
            "tab" => '\t',
            _ => default(char?),
        };
        if (delimiter is null)
        {
            problems.Add(new DefinitionProblem("invalid_value", "options.delimiter", "The delimiter is comma, semicolon or tab."));
        }

        var headers = options.Headers switch
        {
            "labels" => FormBuilderExportHeaders.Labels,
            "aliases" => FormBuilderExportHeaders.Aliases,
            _ => default(FormBuilderExportHeaders?),
        };
        if (headers is null)
        {
            problems.Add(new DefinitionProblem("invalid_value", "options.headers", "The headers are labels or aliases."));
        }

        return new EntryExportRequest
        {
            Format = model.Format,
            Filter = filter,
            Entries = model.Entries ?? [],
            Fields = model.Fields,
            Sensitive = model.Sensitive,
            Options = new FormBuilderExportOptions { Delimiter = delimiter ?? ',', Headers = headers ?? FormBuilderExportHeaders.Labels, ByteOrderMark = options.ByteOrderMark },
        };
    }

    /// <summary>The options as the dialog reads them.</summary>
    public static EntryExportOptionsResponseModel Model(FormBuilderExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new EntryExportOptionsResponseModel
        {
            Delimiter = options.Delimiter switch
            {
                ';' => "semicolon",
                '\t' => "tab",
                _ => "comma",
            },
            Headers = options.Headers == FormBuilderExportHeaders.Aliases ? "aliases" : "labels",
            ByteOrderMark = options.ByteOrderMark,
        };
    }

    /// <summary>A format as the dialog offers it for a form.</summary>
    public static EntryExportFormatResponseModel Model(IFormBuilderExportFormat format, string formAlias)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new EntryExportFormatResponseModel
        {
            Name = format.Name,
            DisplayName = format.DisplayName,
            WritesFiles = format.WritesFiles,
            TakesDelimiter = format.TakesDelimiter,
            TakesHeaders = format.TakesHeaders,
            FileName = EntryExports.FileName(formAlias, format),
        };
    }

    /// <summary>The choices as the dialog opens with them.</summary>
    public static EntryExportChoicesResponseModel Model(EntryExportChoices choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return new EntryExportChoicesResponseModel { Format = choices.Format, Fields = choices.Fields, Sensitive = choices.Sensitive, Options = Model(choices.Options) };
    }

    /// <summary>A background export as its owner sees it.</summary>
    public static EntryExportStatusResponseModel Model(EntryExportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new EntryExportStatusResponseModel
        {
            Key = record.Key,
            State = record.State switch
            {
                EntryExportState.Running => "running",
                EntryExportState.Ready => "ready",
                EntryExportState.Failed => "failed",
                _ => "queued",
            },
            Total = record.Total,
            More = record.More,
            Done = record.Done,
            FileName = record.FileName,
            ExpiresUtc = record.ExpiresUtc,
            Problem = record.Problem,
        };
    }
}
