using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// The dates of the list of the answers (27.1, the first site's finding 9): <c>{{ entry.table }}</c> and
/// <c>{{ entry.answers }}</c> write a date with its month's name and no weekday, the culture's medium format, so
/// 4 March never reads as 3 April; a date-time as that date and the time; a time in the time format. A tag that names
/// the field keeps the short format. The cultures' formats are written here, so no system's culture data decides it
/// (ADR-0263).
/// </summary>
public sealed class EntryTableDatesTests
{
    private static readonly string[] EnglishMonths = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] EnglishShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] GermanMonths = ["Januar", "Februar", "M" + (char)0xE4 + "rz", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
    private static readonly string[] GermanShort = ["Jan.", "Feb.", "M" + (char)0xE4 + "rz", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez."];

    private static readonly MergeTagCultures Cultures = new(
    [
        KeyValuePair.Create("en-US", Formats(new DatePatterns("M/d/yyyy", "MMM d, yyyy", "dddd, MMMM d, yyyy", "h:mm tt", "M/d/yyyy h:mm tt"), EnglishMonths, EnglishShort, "AM", "PM")),
        KeyValuePair.Create("de-DE", Formats(new DatePatterns("dd.MM.yyyy", "d. MMM yyyy", "dddd, d. MMMM yyyy", "HH:mm", "dd.MM.yyyy HH:mm"), GermanMonths, GermanShort, string.Empty, string.Empty)),
    ]);

    [TestCase("en-US", "Visit: Mar 4, 2026\nArrival: Mar 4, 2026 2:30 PM\nCall at: 2:30 PM\nDeparture:\nGuests:\n1. Born: Dec 24, 1990")]
    [TestCase("de-DE", "Visit: 4. M\u00e4rz 2026\nArrival: 4. M\u00e4rz 2026 14:30\nCall at: 14:30\nDeparture:\nGuests:\n1. Born: 24. Dez. 1990")]
    public async Task TheTable_WritesADateWithItsMonthsName_InTheEntrysCulture(string culture, string expected)
    {
        var (form, answers) = Visit();

        var table = await MergeTagTestSite.Create(cultures: Cultures).RenderAsync("{{ entry.table }}", Scope(form, answers, culture));

        Assert.That(table.Text, Is.EqualTo(expected));
    }

    [TestCase("en-US", "Visit: Mar 4, 2026\nArrival: Mar 4, 2026 2:30 PM\nCall at: 2:30 PM\nGuests:\n1. Born: Dec 24, 1990")]
    [TestCase("de-DE", "Visit: 4. M\u00e4rz 2026\nArrival: 4. M\u00e4rz 2026 14:30\nCall at: 14:30\nGuests:\n1. Born: 24. Dez. 1990")]
    public async Task TheAnswers_AndARowsDates_AreWrittenSoToo(string culture, string expected)
    {
        var (form, answers) = Visit();

        var list = await MergeTagTestSite.Create(cultures: Cultures).RenderAsync("{{ entry.answers }}", Scope(form, answers, culture));

        Assert.That(list.Text, Is.EqualTo(expected));
    }

    [TestCase("en-US", "3/4/2026 | 3/4/2026 2:30 PM")]
    [TestCase("de-DE", "04.03.2026 | 04.03.2026 14:30")]
    public async Task ATagThatNamesTheField_KeepsTheShortFormat(string culture, string expected)
    {
        var (form, answers) = Visit();

        var tags = await MergeTagTestSite.Create(cultures: Cultures).RenderAsync("{{ field.visit }} | {{ field.arrival }}", Scope(form, answers, culture));

        Assert.That(tags.Text, Is.EqualTo(expected));
    }

    // A visit on 4 March 2026, which in numbers reads as 3 April where the day comes first, an arrival that day, a time,
    // a date left unanswered, and a guest's birthday in a row.
    private static (Core.Definitions.FormDefinition Form, MergeTagAnswers Answers) Visit()
    {
        var visit = Field("date", "visit", "Visit");
        var arrival = Field("datetime", "arrival", "Arrival");
        var call = Field("time", "call", "Call at");
        var departure = Field("date", "departure", "Departure");
        var born = Field("date", "born", "Born");
        var guests = Field("repeater", "guests", "Guests") with { Fields = [born] };
        var form = Form(visit, arrival, call, departure, guests);
        return (form, Answers(Answer(visit, "2026-03-04"), Answer(arrival, "2026-03-04T14:30"), Answer(call, "14:30"), InRow(born, 0, "1990-12-24")));
    }

    private static CultureFormats Formats(DatePatterns dates, string[] months, string[] monthsShort, string am, string pm) => new(
        dates,
        months,
        monthsShort,
        months,
        monthsShort,
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        am,
        pm,
        "-",
        new NumberFormat(".", ",", [3], "-n"),
        new CurrencyFormat("USD", "$", ".", ",", [3], "$n", "-$n"));
}
