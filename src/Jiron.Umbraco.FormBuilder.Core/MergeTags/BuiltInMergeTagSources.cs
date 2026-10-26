using System.Collections.Immutable;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// A built-in source whose paths are all known without values, so that a check of a template warns of any other path
/// before it renders (ADR-0065).
/// </summary>
internal interface IFiniteMergeTagSource : IFormBuilderMergeTagSource
{
    /// <summary>Whether a whole path is one of the source's for a form; by default, one of those the picker offers.</summary>
    /// <param name="path">The whole path, the source's name first.</param>
    /// <param name="form">The form whose template is checked.</param>
    bool Knows(string path, FormDefinition form) => Paths(form).Any(known => known.Path == path);
}

/// <summary>The site's time and time zone, in which entry dates and <c>date.now</c> are written (ADR-0005, ADR-0065).</summary>
/// <param name="settings">The general settings, which hold each site's time zone.</param>
/// <param name="time">The clock.</param>
internal sealed class MergeTagClock(IFormBuilderSettingsService settings, TimeProvider time)
{
    /// <summary>The current instant.</summary>
    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>The site's time zone, or UTC when the server cannot resolve the one stored (ADR-0043).</summary>
    public async Task<TimeZoneInfo> ZoneAsync(Guid? site, CancellationToken cancellationToken)
    {
        var general = await settings.GetAsync<GeneralSettings>(site, cancellationToken);
        return CalendarValues.FindTimeZone(general.Value.TimeZone) ?? TimeZoneInfo.Utc;
    }

    /// <summary>An instant as wall-clock time in a zone, in the wire form with seconds (ADR-0005).</summary>
    public static string Wire(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);

    /// <summary>A wire date-time as an item: written in the culture's <c>dateTime</c> format, with the wire form behind it.</summary>
    public static MergeTagItem DateTimeItem(string wire, CultureFormats culture) =>
        new(CultureFormatting.FormatCalendar(wire, "dateTime", culture) ?? wire, Date: wire);
}

/// <summary>Values made from texts: one text as an item, several as a list, none as the empty text.</summary>
internal static class MergeTagTexts
{
    /// <summary>The value of a list of texts.</summary>
    public static MergeTagValue Of(IReadOnlyList<string> texts) => texts.Count switch
    {
        0 => MergeTagValue.Empty,
        1 => MergeTagValue.Of(texts[0]),
        _ => MergeTagValue.ListOf([.. texts.Select(text => new MergeTagItem(text))]),
    };
}

/// <summary>The form an entry reference is written in (section 4.3.4): the prefix, the year and a dash, and the padded number.</summary>
internal static class EntryReferences
{
    /// <summary>The reference of an entry, for example <c>REQ-2026-00042</c>; without a format, the year and five digits.</summary>
    /// <param name="format">The form's format, or null for the default.</param>
    /// <param name="number">The entry's number.</param>
    /// <param name="year">The year the entry was submitted in, in the site's time zone.</param>
    public static string Format(EntryReferenceFormat? format, int number, int year)
    {
        format ??= new EntryReferenceFormat();
        var digits = number.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Clamp(format.Digits, 1, 10), '0');
        var dated = format.IncludeYear ? year.ToString("0000", CultureInfo.InvariantCulture) + "-" + digits : digits;
        return (format.Prefix ?? string.Empty) + dated;
    }
}

/// <summary><c>form.name</c> and <c>form.alias</c>.</summary>
internal sealed class FormMergeTagSource : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "form";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [new("form.name"), new("form.alias")];

    /// <inheritdoc />
    public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Path switch
        {
            "name" => MergeTagValue.Of(request.Scope.FormName),
            "alias" => MergeTagValue.Of(request.Scope.FormAlias),
            _ => (MergeTagValue?)null,
        });
}

/// <summary>
/// <c>entry.number</c>, <c>entry.reference</c>, <c>entry.date</c>, <c>entry.state</c> and <c>entry.url</c>, empty
/// before the visitor submits, the date and the reference's year in the site's time zone, the link to the entry in the
/// backoffice from the site's base URL (ADR-0067); and <c>entry.answers</c>, the list of the answers the scope holds
/// (ADR-0066).
/// </summary>
/// <param name="clock">The site's time zone.</param>
/// <param name="cultures">The culture formats.</param>
/// <param name="answers">The answers as merge tags show them.</param>
/// <param name="baseUrls">The sites' base URLs.</param>
internal sealed class EntryMergeTagSource(MergeTagClock clock, MergeTagCultures cultures, MergeTagAnswerTexts answers, SiteBaseUrl baseUrls) : IFiniteMergeTagSource
{
    private static readonly ImmutableArray<string> Known = ["number", "reference", "date", "state", "url", "answers"];

    /// <inheritdoc />
    public string Name => "entry";

    /// <inheritdoc />
    public bool IsPerVisitor => true;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => Known.Select(path => new MergeTagPathInfo("entry." + path));

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        if (!Known.Contains(request.Path, StringComparer.Ordinal))
        {
            return null;
        }

        if (request.Path == "answers")
        {
            return answers.Answers(request.Scope, request.Place, await answers.DictionaryAsync(request.Scope));
        }

        if (request.Scope.Entry is not { } entry)
        {
            return MergeTagValue.Empty;
        }

        switch (request.Path)
        {
            case "number":
                var number = entry.Number.ToString(CultureInfo.InvariantCulture);
                return MergeTagValue.Of(new MergeTagItem(number, Number: number));
            case "state":
                return MergeTagValue.Of(entry.State);
            case "url":
                return await baseUrls.GetAsync(request.Scope.Site, cancellationToken) is { } baseUrl
                    ? MergeTagValue.Of(baseUrl + BackofficeLinks.Entry(entry.Key))
                    : MergeTagValue.Empty;
        }

        var zone = await clock.ZoneAsync(request.Scope.Site, cancellationToken);
        if (request.Path == "reference")
        {
            var year = TimeZoneInfo.ConvertTime(entry.Submitted, zone).Year;
            return MergeTagValue.Of(EntryReferences.Format(request.Scope.Form.Settings.Submission.EntryReference, entry.Number, year));
        }

        return MergeTagValue.Of(MergeTagClock.DateTimeItem(MergeTagClock.Wire(entry.Submitted, zone), cultures.For(request.Scope.Culture)));
    }
}

/// <summary><c>query.&lt;name&gt;</c>: a query value, only of a name the form allows (<c>developer.queryValues</c>).</summary>
internal sealed class QueryMergeTagSource : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "query";

    /// <inheritdoc />
    public bool IsPerVisitor => true;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => form.Settings.Developer.QueryValues.Select(name => new MergeTagPathInfo("query." + name));

    /// <inheritdoc />
    public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        var scope = request.Scope;
        if (!scope.Form.Settings.Developer.QueryValues.Contains(request.Path, StringComparer.Ordinal))
        {
            return ValueTask.FromResult<MergeTagValue?>(null);
        }

        return ValueTask.FromResult<MergeTagValue?>(MergeTagTexts.Of(scope.Query.TryGetValue(request.Path, out var values) ? values : []));
    }
}

/// <summary>
/// <c>context.&lt;key&gt;</c>: a value the developer passed. A key the form lists among its context values is known
/// without a value; any other is known only when passed.
/// </summary>
internal sealed class ContextMergeTagSource : IFormBuilderMergeTagSource
{
    /// <inheritdoc />
    public string Name => "context";

    /// <inheritdoc />
    public bool IsPerVisitor => true;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => form.Settings.Developer.ContextValues.Select(name => new MergeTagPathInfo("context." + name));

    /// <inheritdoc />
    public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        var scope = request.Scope;
        if (scope.Context.TryGetValue(request.Path, out var value))
        {
            return ValueTask.FromResult<MergeTagValue?>(MergeTagValue.Of(value));
        }

        return ValueTask.FromResult<MergeTagValue?>(scope.Form.Settings.Developer.ContextValues.Contains(request.Path, StringComparer.Ordinal) ? MergeTagValue.Empty : null);
    }
}

/// <summary><c>date.now</c>: the current time in the site's time zone, written in the culture's <c>dateTime</c> format.</summary>
/// <param name="clock">The clock and the site's time zone.</param>
/// <param name="cultures">The culture formats.</param>
internal sealed class DateMergeTagSource(MergeTagClock clock, MergeTagCultures cultures) : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "date";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [new("date.now")];

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        if (request.Path != "now")
        {
            return null;
        }

        var zone = await clock.ZoneAsync(request.Scope.Site, cancellationToken);
        return MergeTagValue.Of(MergeTagClock.DateTimeItem(MergeTagClock.Wire(clock.Now, zone), cultures.For(request.Scope.Culture)));
    }
}
