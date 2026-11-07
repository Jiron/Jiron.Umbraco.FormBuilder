using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Insights;

/// <summary>Which entries' answers to summarise (ADR-0212).</summary>
public sealed class InsightAnswersRequestModel
{
    /// <summary>The first UTC day they were received on, <c>yyyy-MM-dd</c>; with <c>to</c>, or neither for the last 30 days.</summary>
    public string? From { get; init; }

    /// <summary>The last UTC day.</summary>
    public string? To { get; init; }

    /// <summary>The entries list's filter; its <c>sort</c> is not read; none for every entry the list holds.</summary>
    public EntryListFilterRequestModel? Filter { get; init; }
}

/// <summary>Which entries' free-text answers to read on, and from where.</summary>
public sealed class InsightAnswerTextsRequestModel
{
    /// <summary>The first UTC day, as the summary had it.</summary>
    public string? From { get; init; }

    /// <summary>The last UTC day.</summary>
    public string? To { get; init; }

    /// <summary>The filter, as the summary had it.</summary>
    public EntryListFilterRequestModel? Filter { get; init; }

    /// <summary>The <c>more</c> of the summary or of the page before.</summary>
    public required string After { get; init; }
}

/// <summary>The answers of a form's entries, summarised per field.</summary>
public sealed class InsightAnswersResponseModel
{
    /// <summary>How many entries the summaries are of.</summary>
    public required long Entries { get; init; }

    /// <summary>Each field's summary, in the form's order: the live version's fields, then those it no longer has.</summary>
    public required IReadOnlyList<InsightAnswerResponseModel> Fields { get; init; }

    internal static InsightAnswersResponseModel Of(AnswerReport report) => new()
    {
        Entries = report.Entries,
        Fields = [.. report.Fields.Select(InsightAnswerResponseModel.Of)],
    };
}

/// <summary>A field's summary.</summary>
public sealed class InsightAnswerResponseModel
{
    /// <summary>The field's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its label, or its alias without one.</summary>
    public required string Label { get; init; }

    /// <summary>What its summary is: <c>options</c>, <c>ranking</c>, <c>grid</c>, <c>number</c>, <c>checkbox</c>, <c>texts</c> or <c>answered</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>How many answered it: entries, or rows of its repeatable group.</summary>
    public required long Answered { get; init; }

    /// <summary>The repeatable group it is a child of, whose rows its answers are; none outside one.</summary>
    public Guid? Parent { get; init; }

    /// <summary>That group's label.</summary>
    public string? ParentLabel { get; init; }

    /// <summary>Whether the live version no longer has it.</summary>
    public required bool Removed { get; init; }

    /// <summary>Whether an answer may choose several options, so the shares add up past the whole.</summary>
    public required bool Several { get; init; }

    /// <summary>For options and a ranking, the lines.</summary>
    public required IReadOnlyList<InsightAnswerOptionResponseModel> Options { get; init; }

    /// <summary>For a grid, its rows.</summary>
    public required IReadOnlyList<InsightAnswerGridRowResponseModel> Rows { get; init; }

    /// <summary>For a number with answers, its figures.</summary>
    public InsightAnswerNumbersResponseModel? Numbers { get; init; }

    /// <summary>For a checkbox, how many ticked it.</summary>
    public required long Ticked { get; init; }

    /// <summary>For free text, the newest answers.</summary>
    public required IReadOnlyList<InsightAnswerTextResponseModel> Texts { get; init; }

    /// <summary>For free text, the <c>after</c> of the next answers; none when these are all.</summary>
    public string? More { get; init; }

    internal static InsightAnswerResponseModel Of(AnswerSummary summary) => new()
    {
        Key = summary.Key,
        Label = summary.Label,
        Kind = EntryListMapping.NameOf(summary.Kind),
        Answered = summary.Answered,
        Parent = summary.Parent,
        ParentLabel = summary.ParentLabel,
        Removed = summary.Removed,
        Several = summary.Several,
        Options = [.. summary.Options.Select(InsightAnswerOptionResponseModel.Of)],
        Rows = [.. summary.Rows.Select(row => new InsightAnswerGridRowResponseModel { Key = row.Key, Label = row.Label, Answered = row.Answered, Options = [.. row.Options.Select(InsightAnswerOptionResponseModel.Of)], Removed = row.Removed })],
        Numbers = summary.Numbers is { } numbers
            ? new InsightAnswerNumbersResponseModel
            {
                Average = numbers.Average,
                Median = numbers.Median,
                Lowest = numbers.Lowest,
                Highest = numbers.Highest,
                Groups = numbers.Groups is { } groups ? new InsightAnswerScaleResponseModel { Low = groups.Low, Middle = groups.Middle, High = groups.High, Score = groups.Score } : null,
            }
            : null,
        Ticked = summary.Ticked,
        Texts = [.. summary.Texts.Select(InsightAnswerTextResponseModel.Of)],
        More = summary.More,
    };
}

/// <summary>An option's line.</summary>
public sealed class InsightAnswerOptionResponseModel
{
    /// <summary>What it shows as; empty for the line of the visitors' own "Other" texts.</summary>
    public required string Label { get; init; }

    /// <summary>How many answers chose it.</summary>
    public required long Count { get; init; }

    /// <summary>For a ranking, its average position, 1 first.</summary>
    public double? Position { get; init; }

    /// <summary>Whether no live option has it: one an older version offered.</summary>
    public required bool Older { get; init; }

    /// <summary>Whether it stands for the visitors' own "Other" texts.</summary>
    public required bool Other { get; init; }

    internal static InsightAnswerOptionResponseModel Of(AnswerOption option) => new()
    {
        Label = option.Label,
        Count = option.Count,
        Position = option.Position,
        Older = option.Older,
        Other = option.Other,
    };
}

/// <summary>A grid's row.</summary>
public sealed class InsightAnswerGridRowResponseModel
{
    /// <summary>The row's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its label.</summary>
    public required string Label { get; init; }

    /// <summary>How many answered it.</summary>
    public required long Answered { get; init; }

    /// <summary>Its count per column.</summary>
    public required IReadOnlyList<InsightAnswerOptionResponseModel> Options { get; init; }

    /// <summary>Whether the live version's grid no longer has the row: one an older version asked.</summary>
    public required bool Removed { get; init; }
}

/// <summary>A number field's figures.</summary>
public sealed class InsightAnswerNumbersResponseModel
{
    /// <summary>The mean.</summary>
    public required double Average { get; init; }

    /// <summary>The middle answer, or the mean of the two in the middle.</summary>
    public required double Median { get; init; }

    /// <summary>The lowest.</summary>
    public required double Lowest { get; init; }

    /// <summary>The highest.</summary>
    public required double Highest { get; init; }

    /// <summary>For a scale from 0 to 10, its groups and its recommendation score; none otherwise.</summary>
    public InsightAnswerScaleResponseModel? Groups { get; init; }
}

/// <summary>The groups of a scale from 0 to 10.</summary>
public sealed class InsightAnswerScaleResponseModel
{
    /// <summary>How many answered 0 to 6.</summary>
    public required long Low { get; init; }

    /// <summary>How many answered 7 or 8.</summary>
    public required long Middle { get; init; }

    /// <summary>How many answered 9 or 10.</summary>
    public required long High { get; init; }

    /// <summary>The recommendation score: the share of 9 and 10 less that of 0 to 6, in whole points from -100 to 100.</summary>
    public required int Score { get; init; }
}

/// <summary>A free-text answer.</summary>
public sealed class InsightAnswerTextResponseModel
{
    /// <summary>Its entry's key, which opens the entry.</summary>
    public required Guid Entry { get; init; }

    /// <summary>When its entry was received.</summary>
    public required DateTimeOffset Received { get; init; }

    /// <summary>The text, at most 500 characters; the entry holds the whole.</summary>
    public required string Text { get; init; }

    internal static InsightAnswerTextResponseModel Of(AnswerText text) => new() { Entry = text.Entry, Received = text.Received, Text = text.Text };
}

/// <summary>More free-text answers.</summary>
public sealed class InsightAnswerTextsResponseModel
{
    /// <summary>The answers, newest entry first.</summary>
    public required IReadOnlyList<InsightAnswerTextResponseModel> Texts { get; init; }

    /// <summary>The <c>after</c> of the next answers; none when these are all.</summary>
    public string? More { get; init; }
}
