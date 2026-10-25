using System.Diagnostics;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The entries query builder on a real database (ADR-0003, ADR-0039): every filter kind with the correctness cases of
/// the 1.3 spike, text compared as rules compare it, the sorts and their keyset pages, the counts, and the time budget.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EntryQueryRepositoryTests : RepositoryTestBase
{
    // The spike's numbers (1.3): signs, fractions, near neighbours and fifteen significant digits.
    private static readonly double[] Numbers =
    [
        -10, -2.5, -2, -0.001, 0, 0.1, 0.5, 2, 9, 10, 10.25, 100, 1000, 12345.6789, 99999999999999.9, -123456789012.345, 0.000000001, 0.3,
        0.30000000000001,
    ];

    // The spike's dates (1.3): around the turn of years and days, with milliseconds SQL Server's datetime keeps.
    private static readonly DateTime[] Dates =
    [
        Utc(1999, 12, 31), Utc(2000, 1, 1), Utc(2009, 6, 15), Utc(2010, 1, 1), Utc(2024, 12, 31, 23, 59, 59), Utc(2025, 1, 1), Utc(2026, 3, 5),
        Utc(2026, 10, 22, 8, 0, 0), Utc(1965, 7, 9), Utc(2100, 1, 1), Utc(2025, 12, 31, 23, 59, 59), Utc(2025, 1, 1, 0, 0, 1),
        Utc(2025, 6, 30, 23, 59, 59, 500), Utc(2025, 6, 30, 23, 59, 59, 250), Utc(2025, 7, 1), Utc(1900, 1, 1),
    ];

    [Test]
    public async Task TextFilters_CompareAsRulesDo_AndMatchAnyAnswer()
    {
        var form = await CreateTestFormAsync("name", "other");
        var name = form["name"];
        var anna = await StoreAsync(form, Text(name, "Anna"));
        var spaced = await StoreAsync(form, Text(name, "  ANNA "));
        var anne = await StoreAsync(form, Text(name, "Anne"));
        var bertha = await StoreAsync(form, Text(name, "Bertha"));
        var none = await StoreAsync(form, Text(form["other"], "Anna"));
        var both = await StoreAsync(form, Text(name, "Cara"), Text(name, "anna", item: 1));
        var umlaut = await StoreAsync(form, Text(name, (char)0xE4 + "rger"));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(form, OneOf(name, "anna")), Is.EquivalentTo(new[] { anna, spaced, both }), "cleaned and folded");
            Assert.That(await IdsAsync(form, OneOf(name, "ANNA", " bertha")), Is.EquivalentTo(new[] { anna, spaced, bertha, both }));
            Assert.That(await IdsAsync(form, OneOf(name, "Ann")), Is.Empty, "one of compares whole texts");
            Assert.That(await IdsAsync(form, NoneOf(name, "anna")), Is.EquivalentTo(new[] { anne, bertha, none, umlaut }), "the exact opposite: an entry without the field matches");
            Assert.That(await IdsAsync(form, StartsWith(name, "AN")), Is.EquivalentTo(new[] { anna, spaced, anne, both }));
            Assert.That(await IdsAsync(form, StartsWith(name, "b")), Is.EquivalentTo(new[] { bertha }));
            Assert.That(await IdsAsync(form, StartsWith(name, " " + (char)0xC4 + "R")), Is.EqualTo(new[] { umlaut }), "a prefix is cleaned and folded past ASCII, as its keys are");
            Assert.That(await IdsAsync(form, OneOf(name, "anna"), StartsWith(name, "c")), Is.EquivalentTo(new[] { both }), "every filter, each by any answer");
        });
    }

    [Test]
    public async Task APrefix_IsLiteralText_EvenWithLikesWildcards()
    {
        var form = await CreateTestFormAsync("code");
        var code = form["code"];
        var percent = await StoreAsync(form, Text(code, "50%_off"));
        await StoreAsync(form, Text(code, "500 off"));
        await StoreAsync(form, Text(code, "50x"));
        var bracket = await StoreAsync(form, Text(code, "[a]b"));
        await StoreAsync(form, Text(code, "ab"));
        var slash = await StoreAsync(form, Text(code, @"\a"));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(form, StartsWith(code, "50%")), Is.EqualTo(new[] { percent }));
            Assert.That(await IdsAsync(form, StartsWith(code, "50_")), Is.Empty);
            Assert.That(await IdsAsync(form, StartsWith(code, "[a")), Is.EqualTo(new[] { bracket }));
            Assert.That(await IdsAsync(form, StartsWith(code, @"\")), Is.EqualTo(new[] { slash }));
        });
    }

    [Test]
    public async Task ASharpS_AndSs_StayApart_InFilters_Searches_AndSorts()
    {
        var form = await CreateTestFormAsync("street");
        var street = form["street"];
        var sharpS = "Stra" + (char)0xDF + "e";
        var sharp = await StoreAsync(form, Text(street, sharpS));
        var ss = await StoreAsync(form, Text(street, "Strasse"));
        var sorted = new EntryQuery(form.Id) { Sort = EntrySort.ByValue(street, EntrySortColumn.Text, descending: false) };

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(form, OneOf(street, sharpS.ToUpperInvariant())), Is.EqualTo(new[] { sharp }));
            Assert.That(await IdsAsync(form, OneOf(street, "STRASSE")), Is.EqualTo(new[] { ss }));
            Assert.That(await IdsAsync(form, NoneOf(street, sharpS)), Is.EqualTo(new[] { ss }));
            Assert.That(await IdsAsync(form, StartsWith(street, "STRA" + (char)0xDF)), Is.EqualTo(new[] { sharp }));
            Assert.That(await IdsAsync(form, StartsWith(street, "strass")), Is.EqualTo(new[] { ss }));
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = sharpS }), Is.EqualTo(new[] { sharp }));
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = "strasse" }), Is.EqualTo(new[] { ss }));
            Assert.That(await AllPagesAsync(sorted, take: 1), Is.EquivalentTo(new[] { sharp, ss }), "each once, wherever the collation puts them");
        });
    }

    [Test]
    public async Task ATextLongerThanItsKey_MatchesOnlyTheEntriesWithThatWholeText()
    {
        var form = await CreateTestFormAsync("note");
        var note = form["note"];
        var x = new string('x', 300);
        var alpha = await StoreAsync(form, Text(note, x + "Alpha"));
        await StoreAsync(form, Text(note, x + "Beta"));
        var shouted = await StoreAsync(form, Text(note, "  " + x.ToUpperInvariant() + "ALPHA "));
        var single = await StoreAsync(form, Text(note, "x"));
        var fits = await StoreAsync(form, Text(note, new string('y', EntryTextKey.MaxLength)));
        var cut = await StoreAsync(form, Text(note, new string('y', EntryTextKey.MaxLength + 1)));
        var edge = await StoreAsync(form, Text(note, new string('y', EntryTextKey.MaxLength - 1)));
        var padded = await StoreAsync(form, Text(note, new string('w', EntryTextKey.MaxLength - 2) + "  b"));
        var shorter = await StoreAsync(form, Text(note, new string('w', EntryTextKey.MaxLength - 2)));
        var face = char.ConvertFromUtf32(0x1F600);
        var plain = await StoreAsync(form, Text(note, new string('z', EntryTextKey.MaxLength - 1)));
        var pair = await StoreAsync(form, Text(note, new string('z', EntryTextKey.MaxLength - 1) + face));
        var all = await IdsAsync(new EntryQuery(form.Id));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(form, OneOf(note, x + "ALPHA")), Is.EquivalentTo(new[] { alpha, shouted }), "the whole text, folded");
            Assert.That(await IdsAsync(form, NoneOf(note, x + "alpha")), Is.EquivalentTo(all.Except([alpha, shouted])));
            Assert.That(await IdsAsync(form, OneOf(note, x + "Gamma")), Is.Empty, "the same key, another text");
            Assert.That(await IdsAsync(form, OneOf(note, x + "alpha", "X")), Is.EquivalentTo(new[] { alpha, shouted, single }));
            Assert.That(await IdsAsync(form, OneOf(note, new string('Y', EntryTextKey.MaxLength))), Is.EqualTo(new[] { fits }), "a text as long as its key");
            Assert.That(await IdsAsync(form, OneOf(note, new string('y', EntryTextKey.MaxLength + 1))), Is.EqualTo(new[] { cut }), "one unit past it");
            Assert.That(await IdsAsync(form, OneOf(note, new string('y', EntryTextKey.MaxLength - 1))), Is.EqualTo(new[] { edge }));
            Assert.That(await IdsAsync(form, OneOf(note, new string('Z', EntryTextKey.MaxLength - 1))), Is.EqualTo(new[] { plain }), "a key cut before a pair is one short");
            Assert.That(await IdsAsync(form, OneOf(note, new string('z', EntryTextKey.MaxLength - 1) + face)), Is.EqualTo(new[] { pair }));
            Assert.That(await IdsAsync(form, OneOf(note, new string('W', EntryTextKey.MaxLength - 2))), Is.EqualTo(new[] { shorter }), "a cut key ending in spaces is no shorter key");
            Assert.That(await IdsAsync(form, NoneOf(note, new string('w', EntryTextKey.MaxLength - 2))), Does.Contain(padded).And.Not.Contain(shorter));
        });
    }

    [Test]
    public async Task NumberRanges_MatchTheSpikesCases_AtTheirBounds()
    {
        var form = await CreateTestFormAsync("amount");
        var amount = form["amount"];
        var ids = new Dictionary<double, long>();
        foreach (var number in Numbers)
        {
            ids[number] = await StoreAsync(form, new NewEntryValue(amount, Invariant(number), Number: number));
        }

        await StoreAsync(form, Text(amount, "not a number"));
        (decimal? Min, bool MinInclusive, decimal? Max, bool MaxInclusive, double[] Expected)[] ranges =
        [
            (-3m, true, 10m, true, [-2.5, -2, -0.001, 0, 0.1, 0.5, 2, 9, 10, 0.000000001, 0.3, 0.30000000000001]),
            (9.5m, true, 1000m, true, [10, 10.25, 100, 1000]),
            (-11m, true, -2.25m, true, [-10, -2.5]),
            (9m, false, null, false, [10, 10.25, 100, 1000, 12345.6789, 99999999999999.9]),
            (0.3m, true, 0.3m, true, [0.3]),
            (0.30000000000001m, true, 1m, false, [0.5, 0.30000000000001]),
            (0m, false, 0.001m, false, [0.000000001]),
            (null, false, -2m, false, [-10, -2.5, -123456789012.345]),
            (99999999999999.9m, true, null, false, [99999999999999.9]),
        ];

        foreach (var range in ranges)
        {
            var filter = new EntryNumberFilter(amount, range.Min, range.MinInclusive, range.Max, range.MaxInclusive);
            Assert.That(await IdsAsync(form, filter), Is.EquivalentTo(range.Expected.Select(number => ids[number])), filter.ToString());
        }

        // More digits than a double holds: the bound is the double the same digits submitted are stored as.
        const string Digits = "318966.42879877258406151690183";
        var parsed = await StoreAsync(form, new NewEntryValue(amount, Digits, Number: double.Parse(Digits, CultureInfo.InvariantCulture)));
        var exact = decimal.Parse(Digits, CultureInfo.InvariantCulture);
        Assert.That(await IdsAsync(form, new EntryNumberFilter(amount, exact, true, exact, true)), Is.EqualTo(new[] { parsed }));
    }

    [Test]
    public async Task DateRanges_AreHalfOpen_ToTheMillisecond()
    {
        var form = await CreateTestFormAsync("arrival");
        var arrival = form["arrival"];
        var ids = new Dictionary<DateTime, long>();
        foreach (var date in Dates)
        {
            ids[date] = await StoreAsync(form, new NewEntryValue(arrival, date.ToString("O", CultureInfo.InvariantCulture), Date: date));
        }

        (DateTime? From, DateTime? Before, DateTime[] Expected)[] ranges =
        [
            (Utc(2009, 1, 1), Utc(2025, 1, 1), [Utc(2009, 6, 15), Utc(2010, 1, 1), Utc(2024, 12, 31, 23, 59, 59)]),
            (Utc(2025, 1, 1), Utc(2026, 1, 1), [Utc(2025, 1, 1), Utc(2025, 12, 31, 23, 59, 59), Utc(2025, 1, 1, 0, 0, 1), Utc(2025, 6, 30, 23, 59, 59, 500), Utc(2025, 6, 30, 23, 59, 59, 250), Utc(2025, 7, 1)]),
            (Utc(2025, 6, 30), Utc(2025, 7, 1), [Utc(2025, 6, 30, 23, 59, 59, 500), Utc(2025, 6, 30, 23, 59, 59, 250)]),
            (null, Utc(2000, 1, 1), [Utc(1999, 12, 31), Utc(1965, 7, 9), Utc(1900, 1, 1)]),
            (Utc(2100, 1, 1), null, [Utc(2100, 1, 1)]),
            (Utc(2025, 6, 30, 23, 59, 59, 250), Utc(2025, 6, 30, 23, 59, 59, 500), [Utc(2025, 6, 30, 23, 59, 59, 250)]),
        ];

        foreach (var range in ranges)
        {
            var filter = new EntryDateFilter(arrival, range.From, range.Before);
            Assert.That(await IdsAsync(form, filter), Is.EquivalentTo(range.Expected.Select(date => ids[date])), filter.ToString());
        }
    }

    [Test]
    public async Task CheckedAndAnswered_CountAMissingAnswerAsNo()
    {
        var form = await CreateTestFormAsync("agree", "name");
        var agree = form["agree"];
        var yes = await StoreAsync(form, new NewEntryValue(agree, "yes", Bool: true));
        var no = await StoreAsync(form, new NewEntryValue(agree, "no", Bool: false));
        var missing = await StoreAsync(form, Text(form["name"], "Anna"));
        var twice = await StoreAsync(form, new NewEntryValue(agree, "no", Bool: false), new NewEntryValue(agree, "yes", Bool: true, RowIndex: 1));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(form, new EntryCheckedFilter(agree, Checked: true)), Is.EquivalentTo(new[] { yes, twice }), "any answer checked");
            Assert.That(await IdsAsync(form, new EntryCheckedFilter(agree, Checked: false)), Is.EquivalentTo(new[] { no, missing }), "the exact opposite");
            Assert.That(await IdsAsync(form, new EntryAnsweredFilter(agree, Answered: true)), Is.EquivalentTo(new[] { yes, no, twice }));
            Assert.That(await IdsAsync(form, new EntryAnsweredFilter(agree, Answered: false)), Is.EquivalentTo(new[] { missing }));
        });
    }

    [Test]
    public async Task StatesTestEntriesAndASubmissionRange_NarrowTheList()
    {
        var form = await CreateTestFormAsync("name");
        var submitted = await StoreAsync(form);
        var from = Clock.GetUtcNow().UtcDateTime.AddMinutes(1);
        var approved = await StoreAsync(form, entry => entry with { State = EntryState.Approved });
        var spam = await StoreAsync(form, entry => entry with { State = EntryState.Spam });
        var before = Clock.GetUtcNow().UtcDateTime.AddMinutes(1);
        var test = await StoreAsync(form, entry => entry with { IsTest = true });
        var draft = await StoreAsync(form, entry => entry with { State = EntryState.Draft });
        var query = new EntryQuery(form.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(query), Is.EqualTo(new[] { draft, spam, approved, submitted }), "newest first, the test entries left out");
            Assert.That(await IdsAsync(query with { Tests = EntryTestFilter.Only }), Is.EqualTo(new[] { test }));
            Assert.That(await IdsAsync(query with { Tests = EntryTestFilter.Include }), Is.EqualTo(new[] { draft, test, spam, approved, submitted }));
            Assert.That(await IdsAsync(query with { States = [EntryState.Spam] }), Is.EqualTo(new[] { spam }));
            Assert.That(await IdsAsync(query with { States = [EntryState.Approved, EntryState.Submitted, EntryState.Approved] }), Is.EqualTo(new[] { approved, submitted }));
            Assert.That(await IdsAsync(query with { States = Enum.GetValues<EntryState>() }), Is.EqualTo(new[] { draft, spam, approved, submitted }), "every state");
            Assert.That(await IdsAsync(query with { SubmittedFrom = from, SubmittedBefore = before }), Is.EqualTo(new[] { spam, approved }), "from (inclusive) to before");
            Assert.That(await IdsAsync(query with { SubmittedFrom = before }), Is.EqualTo(new[] { draft }));
            Assert.That(await IdsAsync(query with { SubmittedBefore = from }), Is.EqualTo(new[] { submitted }));
        });
    }

    [Test]
    public async Task AQuery_ListsOnlyItsForm_EvenWithAFieldOfAnother()
    {
        var form = await CreateTestFormAsync("name");
        var other = await CreateTestFormAsync("name");
        var mine = await StoreAsync(form, Text(form["name"], "Anna"));
        await StoreAsync(other, Text(other["name"], "Anna"));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(new EntryQuery(form.Id)), Is.EqualTo(new[] { mine }));
            Assert.That(await IdsAsync(form, OneOf(other["name"], "anna")), Is.Empty, "a field of another form matches no entry");
            Assert.That(await IdsAsync(form, NoneOf(other["name"], "anna")), Is.EqualTo(new[] { mine }));
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Sort = EntrySort.ByValue(other["name"], EntrySortColumn.Text, false) }), Is.EqualTo(new[] { mine }), "in the tail");
        });
    }

    [Test]
    public async Task PagesBySubmission_GoOnFromTheLastEntry_ThroughEqualTimes()
    {
        var form = await CreateTestFormAsync("name");
        var stored = new List<(long Id, DateTimeOffset Submitted)>();
        for (var i = 0; i < 10; i++)
        {
            // Three entries at each moment: a page boundary falls among entries submitted together.
            var id = await StoreAsync(form, later: i % 3 == 0, change: null);
            stored.Add((id, Clock.GetUtcNow()));
        }

        var newest = stored.OrderByDescending(entry => entry.Submitted).ThenByDescending(entry => entry.Id).Select(entry => entry.Id).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(await AllPagesAsync(new EntryQuery(form.Id), take: 4), Is.EqualTo(newest));
            Assert.That(await AllPagesAsync(new EntryQuery(form.Id) { Sort = EntrySort.Oldest }, take: 3), Is.EqualTo(Enumerable.Reverse(newest)));
            Assert.That(await AllPagesAsync(new EntryQuery(form.Id), take: 10), Is.EqualTo(newest), "one full page");
        });
    }

    [Test]
    public async Task ATextSort_WalksTheKeys_ThenTheEntriesWithoutOne_NewestFirst()
    {
        var form = await CreateTestFormAsync("name", "other");
        var name = form["name"];
        var cara = await StoreAsync(form, Text(name, "cara"));
        var anna = await StoreAsync(form, Text(name, "Anna"));
        var older = await StoreAsync(form, Text(form["other"], "x"));
        var bob = await StoreAsync(form, Text(name, "bob"));
        var anna2 = await StoreAsync(form, Text(name, " anna"));
        var newer = await StoreAsync(form);
        var number = await StoreAsync(form, new NewEntryValue(name, "5", Number: 5));

        // A changed answer gets a new row: the older entry's value now comes after the newer one's.
        await Entries.ReplaceValuesAsync(anna, [name], [Text(name, "Anna")], "anna");
        var ascending = new EntryQuery(form.Id) { Sort = EntrySort.ByValue(name, EntrySortColumn.Text, descending: false) };
        var descending = ascending with { Sort = EntrySort.ByValue(name, EntrySortColumn.Text, descending: true) };
        long[] up = [anna, anna2, bob, cara, number, newer, older];
        long[] down = [cara, bob, anna2, anna, number, newer, older];

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(ascending), Is.EqualTo(up), "equal keys by entry; a value without a key is in the tail");
            Assert.That(await IdsAsync(descending), Is.EqualTo(down), "the tail stays newest first");
            for (var take = 1; take <= up.Length; take++)
            {
                Assert.That(await AllPagesAsync(ascending, take), Is.EqualTo(up), $"pages of {take}");
                Assert.That(await AllPagesAsync(descending, take), Is.EqualTo(down), $"pages of {take}, descending");
            }
        });
    }

    [Test]
    public async Task NumberAndDateSorts_OrderTheSpikesValues_PageByPage()
    {
        var form = await CreateTestFormAsync("amount", "arrival");
        var (amount, arrival) = (form["amount"], form["arrival"]);
        var numbers = new List<(long Id, double Value)>();
        foreach (var number in Numbers.Append(10))
        {
            numbers.Add((await StoreAsync(form, new NewEntryValue(amount, Invariant(number), Number: number)), number));
        }

        var dates = new List<(long Id, DateTime Value)>();
        foreach (var date in Dates.Append(Dates[5]))
        {
            dates.Add((await StoreAsync(form, new NewEntryValue(arrival, date.ToString("O", CultureInfo.InvariantCulture), Date: date)), date));
        }

        var neither = await StoreAsync(form);

        // The walk orders by the value, then the entry; the tail holds the entries without a value, newest first.
        static IEnumerable<long> Walk<T>(IEnumerable<(long Id, T Value)> rows, bool descending) => descending
            ? rows.OrderByDescending(row => row.Value).ThenByDescending(row => row.Id).Select(row => row.Id)
            : rows.OrderBy(row => row.Value).ThenBy(row => row.Id).Select(row => row.Id);
        var withoutNumber = dates.Select(row => row.Id).Append(neither).OrderByDescending(id => id).ToList();
        var withoutDate = numbers.Select(row => row.Id).Append(neither).OrderByDescending(id => id).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(await AllPagesAsync(Sorted(form, amount, EntrySortColumn.Number, descending: false), take: 4), Is.EqualTo(Walk(numbers, false).Concat(withoutNumber)));
            Assert.That(await AllPagesAsync(Sorted(form, amount, EntrySortColumn.Number, descending: true), take: 4), Is.EqualTo(Walk(numbers, true).Concat(withoutNumber)));
            Assert.That(await AllPagesAsync(Sorted(form, arrival, EntrySortColumn.Date, descending: false), take: 5), Is.EqualTo(Walk(dates, false).Concat(withoutDate)));
            Assert.That(await AllPagesAsync(Sorted(form, arrival, EntrySortColumn.Date, descending: true), take: 3), Is.EqualTo(Walk(dates, true).Concat(withoutDate)));
        });
    }

    [Test]
    public async Task ASort_ListsEveryEntryOnce_AtItsFirstAnswerWithAValue()
    {
        var form = await CreateTestFormAsync("name", "score");
        var (name, score) = (form["name"], form["score"]);
        var zed = await StoreAsync(form, Text(name, "zed"), Text(name, "aaa", item: 1));
        var bob = await StoreAsync(form, Text(score, "1", row: 0), Text(name, "bob", row: 1));
        var cat = await StoreAsync(form, Text(name, "cat"));
        var dan = await StoreAsync(form, new NewEntryValue(name, "1", Number: 1), Text(name, "dan", item: 1));
        var five = await StoreAsync(form, Text(score, "n/a"), new NewEntryValue(score, "5", Number: 5, RowIndex: 1));
        var seven = await StoreAsync(form, new NewEntryValue(score, "7", Number: 7), new NewEntryValue(score, "1", Number: 1, ValueIndex: 1));
        var six = await StoreAsync(form, new NewEntryValue(score, "6", Number: 6));
        var eve = await StoreAsync(form, new NewEntryValue(name, "2", Number: 2), Text(name, "eve", item: 1), Text(name, "abe", row: 1));
        var byName = Sorted(form, name, EntrySortColumn.Text, descending: false);
        var byScore = Sorted(form, score, EntrySortColumn.Number, descending: false);

        Assert.Multiple(async () =>
        {
            Assert.That(await AllPagesAsync(byName, take: 1), Is.EqualTo(new[] { bob, cat, dan, eve, zed, six, seven, five }), "zed at zed, not at its second item; eve at its first row");
            Assert.That(await AllPagesAsync(byScore, take: 1), Is.EqualTo(new[] { five, six, seven, eve, dan, cat, bob, zed }), "seven at 7, not at its second item");
        });
    }

    [Test]
    public async Task ACursor_GoesOnOnlyWithItsSort_AndPastItsDeletedEntry()
    {
        var form = await CreateTestFormAsync("name");
        var name = form["name"];
        var ids = new List<long>();
        foreach (var text in new[] { "a", "b", "c", "d", "e" })
        {
            ids.Add(await StoreAsync(form, Text(name, text)));
        }

        var query = new EntryQuery(form.Id);
        var byName = Sorted(form, name, EntrySortColumn.Text, descending: false);
        var first = await EntryQueries.ListAsync(query, null, 2, count: false);
        var walk = await EntryQueries.ListAsync(byName, null, 2, count: false);
        await Entries.DeleteAsync(first.Entries[^1].Id);
        await Entries.DeleteAsync(walk.Entries[^1].Id);

        Assert.Multiple(async () =>
        {
            Assert.That(() => EntryQueries.ListAsync(query with { Sort = EntrySort.Oldest }, first.Next, 2, count: false), Throws.ArgumentException);
            Assert.That(() => EntryQueries.ListAsync(byName, first.Next, 2, count: false), Throws.ArgumentException);
            Assert.That(walk.Next!.Value, Is.Not.Null, "a cursor in the walk holds its value");
            Assert.That((await EntryQueries.ListAsync(query, first.Next, 10, count: false)).Entries.Select(entry => entry.Id), Is.EqualTo(new[] { ids[2], ids[0] }));
            Assert.That((await EntryQueries.ListAsync(byName, walk.Next, 10, count: false)).Entries.Select(entry => entry.Id), Is.EqualTo(new[] { ids[2], ids[4] }));
        });
    }

    [Test]
    public async Task ACursor_PlacesAPage_OnlyByARowOfItsFormAndField()
    {
        var form = await CreateTestFormAsync("name");
        var other = await CreateTestFormAsync("name");
        var foreign = await StoreAsync(other, Text(other["name"], "c"));
        var ids = new List<long>();
        foreach (var text in new[] { "a", "b", "c", "d" })
        {
            ids.Add(await StoreAsync(form, Text(form["name"], text)));
        }

        var foreignValue = (await Entries.ListValuesAsync([foreign])).Single().Id;
        var byName = Sorted(form, form["name"], EntrySortColumn.Text, descending: false);
        var later = Clock.GetUtcNow().UtcDateTime.AddDays(1);

        // Each cursor stood somewhere of its own (at a, or after every entry) and names a row of the other form.
        var walk = await EntryQueries.ListAsync(byName, new EntryCursor(byName.Sort, ids[0], later, new EntryValuePosition(foreignValue, "a", null, null)), 10, count: false);
        var bySubmission = await EntryQueries.ListAsync(new EntryQuery(form.Id), new EntryCursor(EntrySort.Newest, foreign, later, null), 10, count: false);

        Assert.Multiple(() =>
        {
            Assert.That(walk.Entries.Select(entry => entry.Id), Is.EqualTo(ids.Skip(1)), "after a, not after the other form's c");
            Assert.That(bySubmission.Entries.Select(entry => entry.Id), Is.EqualTo(Enumerable.Reverse(ids)), "after its own time, not the other form's entry");
        });
    }

    [Test]
    public async Task APage_HoldsTheWholeEntries_InTheListsOrder()
    {
        var form = await CreateTestFormAsync("name");
        await StoreAsync(form, Text(form["name"], "Anna"));
        var second = await StoreAsync(form, entry => entry with { SearchText = "JFB_TEST", PageUrl = "/kontakt" });

        var page = await EntryQueries.ListAsync(new EntryQuery(form.Id), null, 1, count: true);

        Assert.Multiple(async () =>
        {
            Assert.That(page.Entries.Single(), Is.EqualTo(await Entries.GetByIdAsync(second)), "every column, as the entry repository reads it");
            Assert.That(page.Next, Is.EqualTo(new EntryCursor(EntrySort.Newest, second, page.Entries[0].SubmittedUtc.UtcDateTime, null)));
            Assert.That(page.Count, Is.EqualTo(new EntryCount(2, More: false)));
            Assert.That((await EntryQueries.ListAsync(new EntryQuery(form.Id), page.Next, 1, count: false)).Next, Is.Null, "the last page");
            Assert.That((await EntryQueries.ListAsync(new EntryQuery(form.Id + 1000), null, 10, count: true)).Count, Is.EqualTo(new EntryCount(0, More: false)));
        });
    }

    [Test]
    public async Task ASearch_FindsFoldedTextAnywhere_AsLiteralText_WithinOneAnswer()
    {
        var form = await CreateTestFormAsync("name", "other");
        var (name, other) = (form["name"], form["other"]);
        var hello = await StoreAsync(form, Text(name, "Hello Anna"));
        var percent = await StoreAsync(form, Text(name, "50% off"));
        var digits = await StoreAsync(form, Text(name, "500 off"));
        var two = await StoreAsync(form, Text(name, "anna"), Text(other, "bob"));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = " ANNA " }), Is.EqualTo(new[] { two, hello }));
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = "50%" }), Is.EqualTo(new[] { percent }));
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = "5_0" }), Is.Empty);
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = "anna bob" }), Is.Empty, "never across two answers");
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = "anna" + (char)10 + "bob" }), Is.Empty);
            Assert.That(await IdsAsync(new EntryQuery(form.Id) { Search = "  " }), Is.EqualTo(new[] { two, digits, percent, hello }), "a blank search is none");
        });
    }

    [Test]
    public async Task Counts_AreExact_AndASearchsCountStopsPastAThousand()
    {
        var form = await CreateTestFormAsync("name");
        Seed(form, 1000, "alpha needle");
        Seed(form, 1, "beta needle");
        Seed(form, 5, "gamma");
        var needle = new EntryQuery(form.Id) { Search = "NEEDLE" };

        var log = new CommandLog();
        var first = await WatchedReadAsync(log, () => EntryQueries.ListAsync(needle, null, 10, count: true));
        var second = await EntryQueries.ListAsync(needle, first.Next, 10, count: true);

        Assert.Multiple(async () =>
        {
            Assert.That(first.Count, Is.EqualTo(new EntryCount(1000, More: true)), "newest first: the rows and the count in one pass");
            Assert.That(first.Entries, Has.Count.EqualTo(10));
            Assert.That(log.Texts.Where(statement => statement.Contains("COUNT(", StringComparison.Ordinal)), Is.Empty, "no count of its own");
            Assert.That(second.Count, Is.EqualTo(new EntryCount(1000, More: true)), "a later page counts on its own");
            Assert.That(second.Entries.Select(entry => entry.Id), Is.All.LessThan(first.Entries[^1].Id));
            Assert.That((await EntryQueries.ListAsync(needle with { Sort = EntrySort.Oldest }, null, 10, count: true)).Count, Is.EqualTo(new EntryCount(1000, More: true)));
            Assert.That((await EntryQueries.ListAsync(needle with { Search = "alpha" }, null, 10, count: true)).Count, Is.EqualTo(new EntryCount(1000, More: false)), "exactly a thousand");
            Assert.That((await EntryQueries.ListAsync(needle with { Search = "beta" }, null, 10, count: true)).Count, Is.EqualTo(new EntryCount(1, More: false)));
            Assert.That((await EntryQueries.ListAsync(new EntryQuery(form.Id), null, 10, count: true)).Count, Is.EqualTo(new EntryCount(1006, More: false)), "without a search, exact");
            Assert.That((await EntryQueries.ListAsync(needle, null, 10, count: false)).Count, Is.Null, "only when asked");
        });
    }

    [Test]
    public async Task TheLargestQuery_RunsWithinTheParametersOfOneStatement()
    {
        var form = await CreateTestFormAsync("a", "b", "c", "d", "e", "f", "g", "h", "i", "j");
        var match = await StoreAsync(form, [.. form.Fields.Values.Select(id => Text(id, "value 49"))]);
        await StoreAsync(form, [.. form.Fields.Values.Select(id => Text(id, "value 50"))]);
        var filters = form.Fields.Values
            .Select(id => (EntryFieldFilter)new EntryTextFilter(id, EntryTextMatch.OneOf, [.. Enumerable.Range(0, EntryQuerySql.MaxValuesPerFilter).Select(i => "VALUE " + i)]))
            .ToList();
        var counts = new List<int>();

        var page = await WatchedReadAsync(new ParameterCounter(counts), () => EntryQueries.ListAsync(new EntryQuery(form.Id) { Filters = filters, Search = "value" }, null, 10, count: true));

        Assert.Multiple(() =>
        {
            Assert.That(page.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { match }));
            Assert.That(page.Count, Is.EqualTo(new EntryCount(1, More: false)));
            Assert.That(counts.Max(), Is.InRange(1000, 2100), "ten filters of fifty values in one statement SQL Server takes");
        });
    }

    [Test]
    public async Task AListPastItsBudget_StopsWithQueryTimeout_AndASubmissionMeanwhileSucceeds()
    {
        var form = await CreateTestFormAsync("name");
        await StoreAsync(form, Text(form["name"], "Anna"));
        var slow = IsSqlite
            ? "(WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 100000000) SELECT MAX(x) FROM c) > 0 AND "
            : "(SELECT COUNT_BIG(*) FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c CROSS JOIN sys.all_objects d) > 0 AND ";
        var where = T(FormBuilderTables.Entry) + " e WHERE ";
        var slowly = new CommandRewriter(where, where + slow);
        var budgeted = new EntryQueryRepository(Database, TimeSpan.FromMilliseconds(250));
        var watch = Stopwatch.StartNew();

        var list = WatchedReadAsync(slowly, () => budgeted.ListAsync(new EntryQuery(form.Id), null, 10, count: false));
        var submission = Task.Run(async () =>
        {
            await Task.Delay(50);
            return await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, Text(form["name"], "Bob")));
        });

        Assert.That(async () => await list, Throws.InstanceOf<FormBuilderQueryTimeoutException>());
        var stopped = watch.Elapsed;
        var stored = await submission;
        Assert.Multiple(async () =>
        {
            Assert.That(slowly.Rewritten, Is.EqualTo(1), "the list ran once, and never again");
            Assert.That(stopped, Is.LessThan(TimeSpan.FromSeconds(5)), "interrupted, not run to its end");
            Assert.That(stored, Is.Not.Null, "the submission made meanwhile is stored");
            Assert.That((await EntryQueries.ListAsync(new EntryQuery(form.Id), null, 10, count: true)).Count, Is.EqualTo(new EntryCount(2, More: false)), "the database serves the next unit");
        });
    }

    [Test]
    public async Task AListsBudget_EndsWithTheList()
    {
        Assume.That(IsSqlite, Is.True, "the progress handler is SQLite's");
        var form = await CreateTestFormAsync("name");
        await StoreAsync(form, Text(form["name"], "Anna"));
        var budgeted = new EntryQueryRepository(Database, TimeSpan.FromMilliseconds(50));

        // The lists join an outer unit, so the statement after them runs on the very connection they used.
        var sum = await Database.ReadAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                await budgeted.ListAsync(new EntryQuery(form.Id), null, 10, count: false);
            }

            await Task.Delay(200);

            // Far more steps than the handler counts between checks, past every deadline.
            return await db.ScalarAsync<long>("WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 200000) SELECT SUM(x) FROM c");
        });

        Assert.That(sum, Is.EqualTo(200000L * 200001 / 2), "no handler of a finished list stops it");
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int millisecond = 0) =>
        new(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);

    private static string Invariant(double number) => number.ToString("R", CultureInfo.InvariantCulture);

    private static NewEntryValue Text(int fieldId, string text, int row = 0, int item = 0) => new(fieldId, text, RowIndex: row, ValueIndex: item);

    private static EntryTextFilter OneOf(int fieldId, params string[] values) => new(fieldId, EntryTextMatch.OneOf, values);

    private static EntryTextFilter NoneOf(int fieldId, params string[] values) => new(fieldId, EntryTextMatch.NoneOf, values);

    private static EntryTextFilter StartsWith(int fieldId, string prefix) => new(fieldId, EntryTextMatch.StartsWith, [prefix]);

    private static EntryQuery Sorted(TestForm form, int fieldId, EntrySortColumn column, bool descending) =>
        new(form.Id) { Sort = EntrySort.ByValue(fieldId, column, descending) };

    /// <summary>Stores an entry of the form with these values a minute after the one before, its search text from them.</summary>
    private Task<long> StoreAsync(TestForm form, params NewEntryValue[] values) => StoreAsync(form, later: true, change: null, values);

    private Task<long> StoreAsync(TestForm form, Func<NewEntry, NewEntry> change, params NewEntryValue[] values) => StoreAsync(form, later: true, change, values);

    private async Task<long> StoreAsync(TestForm form, bool later, Func<NewEntry, NewEntry>? change, params NewEntryValue[] values)
    {
        if (later)
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var entry = NewEntry(form.Id, form.VersionId, values) with { SearchText = EntrySearchText.For(values.Select(value => value.Text ?? string.Empty)) };
        return (await Entries.CreateAsync(change is null ? entry : change(entry)))!.Id;
    }

    /// <summary>Stores many entries of the form at once, a second apart, with this search text and no values.</summary>
    private void Seed(TestForm form, int count, string searchText) =>
        Write(db =>
        {
            var number = db.ExecuteScalar<int>($"SELECT COALESCE(MAX({Q("number")}), 0) FROM {T(FormBuilderTables.Entry)} WHERE {Q("formId")} = @0", form.Id);
            for (var i = 1; i <= count; i++)
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                var now = Clock.GetUtcNow().UtcDateTime;
                db.Insert(new EntryDto
                {
                    Key = Guid.NewGuid(),
                    FormId = form.Id,
                    FormVersionId = form.VersionId,
                    Number = number + i,
                    State = StoredNames.Of(EntryState.Submitted),
                    SubmissionId = Guid.NewGuid(),
                    SubmissionHash = Hex(32),
                    Culture = "en-US",
                    SearchText = searchText,
                    SubmittedUtc = now,
                    UpdatedUtc = now,
                    StateChangedUtc = now,
                });
            }
        });

    private async Task<List<long>> IdsAsync(TestForm form, params EntryFieldFilter[] filters) => await IdsAsync(new EntryQuery(form.Id) { Filters = filters });

    private async Task<List<long>> IdsAsync(EntryQuery query) =>
        [.. (await EntryQueries.ListAsync(query, null, PageRequest.MaxTake, count: false)).Entries.Select(entry => entry.Id)];

    /// <summary>Every page of the list, each full but the last, the ids in their order.</summary>
    private async Task<List<long>> AllPagesAsync(EntryQuery query, int take)
    {
        var ids = new List<long>();
        EntryCursor? after = null;
        for (var pages = 1; pages <= 100; pages++)
        {
            var page = await EntryQueries.ListAsync(query, after, take, count: false);
            ids.AddRange(page.Entries.Select(entry => entry.Id));
            if (page.Next is null)
            {
                return ids;
            }

            Assert.That(page.Entries, Has.Count.EqualTo(take), "a page before the last is full");
            after = page.Next;
        }

        throw new AssertionException("The list did not end within 100 pages.");
    }

    /// <summary>Runs <paramref name="work"/> inside one read with <paramref name="interceptor"/> watching its statements.</summary>
    private Task<T> WatchedReadAsync<T>(NPoco.IInterceptor interceptor, Func<Task<T>> work) =>
        Database.ReadAsync(async db =>
        {
            db.Interceptors.Add(interceptor);
            try
            {
                return await work();
            }
            finally
            {
                db.Interceptors.Remove(interceptor);
            }
        });
}
