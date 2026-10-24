using System.Globalization;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The entries query builder's statements and what it refuses (ADR-0039), without a database: the integration tests
/// run the statements on SQLite and in the SQL Server job, and look at SQLite's plans.
/// </summary>
[TestFixture]
public sealed partial class EntryQuerySqlTests
{
    private const string Sqlite = "Microsoft.Data.Sqlite";
    private const string SqlServer = "Microsoft.Data.SqlClient";
    private const int Form = 7;
    private const int Field = 11;
    private static readonly DateTime Utc = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly EntrySort ByName = EntrySort.ByValue(Field, EntrySortColumn.Text, descending: false);

    public static IEnumerable<TestCaseData> Refused()
    {
        static TestCaseData Case(string name, EntryQuery query, EntryCursor? after = null, int take = 10) =>
            new TestCaseData(new TestDelegate(() => EntryQuerySql.Check(query, after, take))).SetName("Refuses(" + name + ")");

        var query = new EntryQuery(Form);
        yield return Case("an empty page", query, take: 0);
        yield return Case("a page past 500", query, take: 501);
        yield return Case("no form", new EntryQuery(0));
        yield return Case("an unknown state", query with { States = [(EntryState)99] });
        yield return Case("unknown test entries", query with { Tests = (EntryTestFilter)9 });
        yield return Case("a local submission time", query with { SubmittedFrom = DateTime.SpecifyKind(Utc, DateTimeKind.Local) });
        yield return Case("an unspecified submission time", query with { SubmittedBefore = DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified) });
        yield return Case("a submission range that ends where it starts", query with { SubmittedFrom = Utc, SubmittedBefore = Utc });
        yield return Case("eleven filters", query with { Filters = [.. Enumerable.Range(1, 11).Select(id => new EntryAnsweredFilter(id, true))] });
        yield return Case("a filter without a field", query with { Filters = [new EntryAnsweredFilter(0, true)] });
        yield return Case("a null filter", query with { Filters = [null!] });
        yield return Case("a text filter without values", query with { Filters = [new EntryTextFilter(Field, EntryTextMatch.OneOf, [])] });
        yield return Case("a text filter with 51 values", query with { Filters = [new EntryTextFilter(Field, EntryTextMatch.OneOf, [.. Enumerable.Range(0, 51).Select(i => "v" + i)])] });
        yield return Case("a text value that cleans to nothing", query with { Filters = [new EntryTextFilter(Field, EntryTextMatch.OneOf, ["anna", "  "])] });
        yield return Case("a null text value", query with { Filters = [new EntryTextFilter(Field, EntryTextMatch.NoneOf, [null!])] });
        yield return Case("an unknown text match", query with { Filters = [new EntryTextFilter(Field, (EntryTextMatch)7, ["anna"])] });
        yield return Case("two prefixes", query with { Filters = [new EntryTextFilter(Field, EntryTextMatch.StartsWith, ["a", "b"])] });
        yield return Case("a prefix the key may cut", query with { Filters = [new EntryTextFilter(Field, EntryTextMatch.StartsWith, [new string('a', EntryTextKey.MaxLength - 1)])] });
        yield return Case("a number range without bounds", query with { Filters = [new EntryNumberFilter(Field, null, true, null, true)] });
        yield return Case("a number range that ends below its start", query with { Filters = [new EntryNumberFilter(Field, 2m, true, 1m, true)] });
        yield return Case("a date range without bounds", query with { Filters = [new EntryDateFilter(Field, null, null)] });
        yield return Case("a local date", query with { Filters = [new EntryDateFilter(Field, DateTime.SpecifyKind(Utc, DateTimeKind.Local), null)] });
        yield return Case("a date range that ends where it starts", query with { Filters = [new EntryDateFilter(Field, Utc, Utc)] });
        yield return Case("an unknown filter", query with { Filters = [new OtherFilter(Field)] });
        yield return Case("a search past 200 characters", query with { Search = new string('a', EntrySearchText.MaxTermLength + 1) });
        yield return Case("an unknown sort column", query with { Sort = new EntrySort((EntrySortColumn)8, false, Field) });
        yield return Case("a value sort without a field", query with { Sort = new EntrySort(EntrySortColumn.Number, false) });
        yield return Case("a sort by submission with a field", query with { Sort = new EntrySort(EntrySortColumn.Submitted, true, Field) });
        yield return Case("a null sort", query with { Sort = null! });
        yield return Case("a cursor of another sort", query, new EntryCursor(EntrySort.Oldest, 1, Utc, null));
        yield return Case("a cursor of another field", query with { Sort = ByName }, new EntryCursor(EntrySort.ByValue(Field + 1, EntrySortColumn.Text, false), 1, Utc, null));
        yield return Case("a cursor without an entry", query, new EntryCursor(EntrySort.Newest, 0, Utc, null));
        yield return Case("a cursor's local time", query, new EntryCursor(EntrySort.Newest, 1, DateTime.SpecifyKind(Utc, DateTimeKind.Local), null));
        yield return Case("a value position by submission", query, new EntryCursor(EntrySort.Newest, 1, Utc, new EntryValuePosition(1, "a", null, null)));
        yield return Case("a value position without a row", query with { Sort = ByName }, new EntryCursor(ByName, 1, Utc, new EntryValuePosition(0, "a", null, null)));
        yield return Case("a text position without text", query with { Sort = ByName }, new EntryCursor(ByName, 1, Utc, new EntryValuePosition(1, null, 1, null)));
        var byNumber = EntrySort.ByValue(Field, EntrySortColumn.Number, true);
        yield return Case("a number position that is no number", query with { Sort = byNumber }, new EntryCursor(byNumber, 1, Utc, new EntryValuePosition(1, null, double.NaN, null)));
        var byDate = EntrySort.ByValue(Field, EntrySortColumn.Date, true);
        yield return Case("a local date position", query with { Sort = byDate }, new EntryCursor(byDate, 1, Utc, new EntryValuePosition(1, null, null, DateTime.SpecifyKind(Utc, DateTimeKind.Local))));
    }

    [TestCaseSource(nameof(Refused))]
    public void Check_RefusesWhatTheBuilderCannotAnswerAsAsked(TestDelegate check) =>
        Assert.That(check, Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Check_AcceptsAQueryWithEveryPart()
    {
        var query = Everything() with { Sort = ByName };

        Assert.Multiple(() =>
        {
            Assert.That(() => EntryQuerySql.Check(query, null, 500), Throws.Nothing);
            Assert.That(() => EntryQuerySql.Check(query, new EntryCursor(ByName, 1, Utc, new EntryValuePosition(1, "anna", null, null)), 1), Throws.Nothing);
            Assert.That(() => EntryQuerySql.Check(query, new EntryCursor(ByName, 1, Utc, null), 1), Throws.Nothing, "a cursor in the tail");
            Assert.That(() => EntryQuerySql.Check(new EntryQuery(Form) { Search = "   " }, null, 1), Throws.Nothing, "a blank search is none");
        });
    }

    [Test]
    public void EveryStatement_HasOneArgumentPerPlaceholder_InOrder()
    {
        foreach (var provider in new[] { Sqlite, SqlServer })
        {
            foreach (var (name, statement) in Statements(Names(provider)))
            {
                var placeholders = Placeholder().Matches(statement.Text).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
                Assert.That(placeholders, Is.EqualTo(Enumerable.Range(0, statement.Args.Length)), $"{provider}: {name}");
            }
        }
    }

    [Test]
    public void TheLargestQuery_FitsTheParametersOfOneStatement()
    {
        var filters = Enumerable.Range(1, EntryQuerySql.MaxFilters)
            .Select(id => (EntryFieldFilter)new EntryTextFilter(id, EntryTextMatch.OneOf, [.. Enumerable.Range(0, EntryQuerySql.MaxValuesPerFilter).Select(i => "value " + i)]))
            .ToList();
        var query = new EntryQuery(Form) { Filters = filters, Search = "anna", States = [EntryState.Submitted, EntryState.Approved], SubmittedFrom = Utc };
        EntryQuerySql.Check(query, null, 500);

        var sql = new EntryQuerySql(Names(SqlServer), query);

        Assert.Multiple(() =>
        {
            Assert.That(sql.BySubmission(new EntryCursor(EntrySort.Newest, 1, Utc, null), 501).Args, Has.Length.LessThanOrEqualTo(EntryQuerySql.MaxParameters));
            Assert.That(sql.Count().Args, Has.Length.LessThanOrEqualTo(EntryQuerySql.MaxParameters));
        });
    }

    [Test]
    public void LongTextsPastTheParameters_AreRefused()
    {
        var value = new string('x', 300);
        var query = new EntryQuery(Form) { Filters = [new EntryTextFilter(Field, EntryTextMatch.OneOf, [value])] };
        var texts = new Dictionary<(int Filter, int Value), IReadOnlyList<string>> { [(0, 0)] = [.. Enumerable.Range(0, EntryQuerySql.MaxParameters).Select(i => value + i)] };

        var sql = new EntryQuerySql(Names(Sqlite), query, texts);

        Assert.That(() => sql.BySubmission(null, 10), Throws.ArgumentException.With.Message.Contains("more values than one statement takes"));
    }

    [Test]
    public void LongValues_AreTheEqualityValuesWhoseKeyMayBeCut()
    {
        var shortValue = new string('a', EntryTextKey.MaxLength - 2);
        var edge = new string('b', EntryTextKey.MaxLength - 1);
        var longValue = "  " + new string('C', EntryTextKey.MaxLength + 10);
        var query = new EntryQuery(Form)
        {
            Filters =
            [
                new EntryTextFilter(Field, EntryTextMatch.OneOf, [shortValue, longValue]),
                new EntryTextFilter(Field + 1, EntryTextMatch.StartsWith, ["c"]),
                new EntryTextFilter(Field + 2, EntryTextMatch.NoneOf, [edge]),
            ],
        };

        Assert.That(
            EntryQuerySql.LongValues(query).ToList(),
            Is.EqualTo(new[]
            {
                (0, 1, Field, new string('c', EntryTextKey.MaxLength), new string('c', EntryTextKey.MaxLength + 10)),
                (2, 0, Field + 2, edge, edge),
            }));
    }

    [Test]
    public void ALongValueWithoutStoredTexts_MatchesNothing()
    {
        var query = new EntryQuery(Form) { Filters = [new EntryTextFilter(Field, EntryTextMatch.OneOf, [new string('x', 300)])] };

        var statement = new EntryQuerySql(Names(Sqlite), query).BySubmission(null, 10);

        Assert.That(statement.Text, Does.Contain("AND 1 = 0)"));
    }

    [TestCase(Sqlite, " COLLATE BINARY", "LIMIT 11 OFFSET 0")]
    [TestCase(SqlServer, " COLLATE Latin1_General_BIN2", "OFFSET 0 ROWS FETCH NEXT 11 ROWS ONLY")]
    public void TextFilters_SeekWithTheColumnsCollation_ThenCompareByCodeUnits(string provider, string binary, string page)
    {
        var query = new EntryQuery(Form) { Filters = [new EntryTextFilter(Field, EntryTextMatch.OneOf, ["Anna_1"])], Search = "x" };

        var statement = new EntryQuerySql(Names(provider), query).BySubmission(null, 11);

        Assert.Multiple(() =>
        {
            Assert.That(statement.Text, Does.Contain("vf.[fieldId] = @3 AND (vf.[textKey] IN (@4) AND (vf.[textKey]" + binary + " LIKE @5 " + SqlNames.EscapeClause + "))"));
            Assert.That(statement.Text, Does.Contain("e.[searchText]" + binary + " LIKE @2"));
            Assert.That(statement.Text, Does.EndWith(page));
            Assert.That(statement.Args[4], Is.EqualTo("anna_1"), "the key, cleaned and folded");
            Assert.That(statement.Args[5], Is.EqualTo(@"anna\_1"), "the same key for a LIKE without wildcards, which pads nothing");
        });
    }

    [TestCase(Sqlite, "vf.[textKey] LIKE @")]
    [TestCase(SqlServer, "vf.[textKey] COLLATE Latin1_General_BIN2 LIKE @")]
    public void PrefixesAndSearches_AreEscapedForLike_AndAPrefixSeeksOnlyOnSqlite(string provider, string prefix)
    {
        var query = new EntryQuery(Form) { Filters = [new EntryTextFilter(Field, EntryTextMatch.StartsWith, ["50%_[x"])], Search = " 100% " };

        var statement = new EntryQuerySql(Names(provider), query).BySubmission(null, 10);

        Assert.Multiple(() =>
        {
            Assert.That(statement.Args, Does.Contain(@"%100\%%"), "the search, folded and escaped");
            Assert.That(statement.Args, Does.Contain(@"50\%\_\[x%"), "the prefix, escaped");
            Assert.That(Regex.Matches(statement.Text, Regex.Escape(SqlNames.EscapeClause)), Has.Count.EqualTo(2), "one LIKE for the prefix, one for the search");
            Assert.That(statement.Text, Does.Contain(prefix), "SQLite seeks on NOCASE; on SQL Server, where a collation with contractions decides nothing, the binary LIKE alone");
        });
    }

    [TestCase(Sqlite, "IFNULL")]
    [TestCase(SqlServer, "ISNULL")]
    public void ACursorsRow_IsReadOnce_AndOnlyFromItsFormAndField(string provider, string ifNull)
    {
        var walk = new EntryQuerySql(Names(provider), new EntryQuery(Form) { Sort = ByName })
            .Walk(new EntryCursor(ByName, 5, Utc, new EntryValuePosition(9, "anna", null, null)), 10);
        var bySubmission = new EntryQuerySql(Names(provider), new EntryQuery(Form))
            .BySubmission(new EntryCursor(EntrySort.Newest, 5, Utc, null), 10);

        Assert.Multiple(() =>
        {
            Assert.That(walk.Text, Does.Contain(ifNull + "((SELECT vc.[textKey] FROM [jfbEntryValue] vc WHERE vc.[id] = @"));
            Assert.That(bySubmission.Text, Does.Contain(ifNull + "((SELECT ec.[submittedUtc] FROM [jfbEntry] ec WHERE ec.[id] = @"));
            Assert.That(walk.Text + bySubmission.Text, Does.Not.Contain("COALESCE"), "which runs a subquery twice on SQL Server");
            Assert.That(ArgumentsOf(walk, @"vc\.\[fieldId\] = @(\d+)"), Is.EqualTo(new object[] { Field, Field }), "the sort's field");
            Assert.That(ArgumentsOf(bySubmission, @"ec\.\[formId\] = @(\d+)"), Is.EqualTo(new object[] { Form, Form }), "the form");
        });
    }

    [Test]
    public void NumberBounds_AreTheDoublesTheirDigitsParseTo()
    {
        const string Digits = "318966.42879877258406151690183";
        var bound = decimal.Parse(Digits, CultureInfo.InvariantCulture);
        var query = new EntryQuery(Form) { Filters = [new EntryNumberFilter(Field, bound, true, bound, true)] };

        var statement = new EntryQuerySql(Names(Sqlite), query).BySubmission(null, 10);

        var parsed = double.Parse(Digits, CultureInfo.InvariantCulture);
        Assert.Multiple(() =>
        {
            Assert.That(statement.Args.OfType<double>(), Is.EqualTo(new[] { parsed, parsed }), "as a submitted number with these digits is stored");
            Assert.That((double)bound, Is.Not.EqualTo(parsed), "a conversion from decimal misses by one unit in the last place");
        });
    }

    [Test]
    public void FieldFilters_AreInSubqueries_AndTheWalkKeepsAnEntryAtItsFirstAnswer()
    {
        var sql = new EntryQuerySql(Names(Sqlite), Everything() with { Sort = ByName });

        var bySubmission = new EntryQuerySql(Names(Sqlite), Everything()).BySubmission(null, 10).Text;
        var walk = sql.Walk(null, 10).Text;

        Assert.Multiple(() =>
        {
            Assert.That(bySubmission, Does.Not.Contain("EXISTS"), "a filter is an IN subquery (ADR-0003)");
            Assert.That(Regex.Matches(walk, "NOT EXISTS"), Has.Count.EqualTo(1), "the walk's check for an earlier answer");
            Assert.That(walk, Does.Contain("FROM [jfbEntryValue] v CROSS JOIN [jfbEntry] e WHERE e.[id] = +v.[entryId]"), "the join lends no entry ids to the values");
            Assert.That(walk, Does.Contain("ORDER BY v.[textKey] ASC, v.[entryId] ASC"));
        });
    }

    [Test]
    public void ACountOfASearch_StopsPastAThousand_AndAnyOtherCountsAll()
    {
        var names = Names(Sqlite);

        Assert.Multiple(() =>
        {
            Assert.That(new EntryQuerySql(names, new EntryQuery(Form) { Search = "anna" }).Count().Text, Does.Contain("LIMIT 1001 OFFSET 0) c"));
            Assert.That(new EntryQuerySql(names, new EntryQuery(Form)).Count().Text, Does.Not.Contain("LIMIT"));
            Assert.That(new EntryQuerySql(names, new EntryQuery(Form) { Search = " " }).Searches, Is.False, "a blank search is none");
        });
    }

    [Test]
    public void ALoad_TakesOnePagesIds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EntryQuerySql.Load(Names(Sqlite), [3, 1, 2]).Args, Is.EqualTo(new object[] { 3L, 1L, 2L }));
            Assert.That(() => EntryQuerySql.Load(Names(Sqlite), [.. Enumerable.Range(1, PageRequest.MaxTake + 1).Select(i => (long)i)]), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void TheSearchText_HoldsTheAnswersCleanedAndFolded_OnePerLine()
    {
        var text = EntrySearchText.For(["  Anna ", string.Empty, "  ", "BOB", "two" + (char)10 + "lines"]);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("anna" + (char)10 + "bob" + (char)10 + "two lines"));
            Assert.That(EntrySearchText.Normalize(" ANNA" + (char)10 + "Bob "), Is.EqualTo("anna bob"), "a term never holds the separator");
            Assert.That(text, Does.Not.Contain(EntrySearchText.Normalize("anna" + (char)10 + "bob")), "so it never matches across two answers");
            Assert.That(() => EntrySearchText.For(null!), Throws.ArgumentNullException);
            Assert.That(() => EntrySearchText.Normalize(null!), Throws.ArgumentNullException);
        });
    }

    // A query with every part: each kind of filter, a long text, a search, states, the test entries and a range.
    private static EntryQuery Everything() => new(Form)
    {
        States = [EntryState.Submitted, EntryState.Approved],
        Tests = EntryTestFilter.Only,
        SubmittedFrom = Utc.AddDays(-7),
        SubmittedBefore = Utc,
        Search = "anna",
        Filters =
        [
            new EntryTextFilter(Field, EntryTextMatch.OneOf, ["anna", "bob", new string('x', 300)]),
            new EntryTextFilter(Field, EntryTextMatch.NoneOf, ["cara"]),
            new EntryTextFilter(Field, EntryTextMatch.StartsWith, ["an"]),
            new EntryNumberFilter(Field + 1, 1m, true, 10m, false),
            new EntryNumberFilter(Field + 1, null, false, 0m, true),
            new EntryDateFilter(Field + 2, Utc.AddYears(-1), Utc),
            new EntryCheckedFilter(Field + 3, true),
            new EntryCheckedFilter(Field + 3, false),
            new EntryAnsweredFilter(Field + 4, true),
            new EntryAnsweredFilter(Field + 4, false),
        ],
    };

    // Every statement the builder writes, by name.
    private static IEnumerable<(string Name, SqlStatement Statement)> Statements(SqlNames names)
    {
        var texts = new Dictionary<(int Filter, int Value), IReadOnlyList<string>> { [(0, 2)] = [new string('x', 300), new string('X', 300)] };
        var everything = Everything();
        yield return ("by submission", new EntryQuerySql(names, everything, texts).BySubmission(null, 10));
        yield return ("by submission, after", new EntryQuerySql(names, everything, texts).BySubmission(new EntryCursor(EntrySort.Newest, 5, Utc, null), 10));
        yield return ("count", new EntryQuerySql(names, everything, texts).Count());
        yield return ("count without a search", new EntryQuerySql(names, everything with { Search = null }, texts).Count());
        foreach (var column in new[] { EntrySortColumn.Text, EntrySortColumn.Number, EntrySortColumn.Date })
        {
            var sort = EntrySort.ByValue(Field, column, descending: column == EntrySortColumn.Number);
            var sql = new EntryQuerySql(names, everything with { Sort = sort }, texts);
            var position = new EntryValuePosition(9, "anna", 2.5, Utc);
            yield return ($"walk by {column}", sql.Walk(null, 10));
            yield return ($"walk by {column}, after", sql.Walk(new EntryCursor(sort, 5, Utc, position), 10));
            yield return ($"tail by {column}", sql.Tail(null, 10));
            yield return ($"tail by {column}, after", sql.Tail(new EntryCursor(sort, 5, Utc, null), 10));
        }

        yield return ("long texts", EntryQuerySql.LongTexts(names, Field, new string('x', EntryTextKey.MaxLength)));
        yield return ("load", EntryQuerySql.Load(names, [1, 2, 3]));
    }

    // The arguments of the placeholders a pattern finds, the placeholder's number in its first group.
    private static object?[] ArgumentsOf(SqlStatement statement, string pattern) =>
        [.. Regex.Matches(statement.Text, pattern).Select(match => statement.Args[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)])];

    private static SqlNames Names(string provider)
    {
        var syntax = Substitute.For<ISqlSyntaxProvider>();
        syntax.ProviderName.Returns(provider);
        syntax.GetQuotedTableName(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        syntax.GetQuotedColumnName(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        return new SqlNames(syntax);
    }

    [GeneratedRegex(@"@(\d+)")]
    private static partial Regex Placeholder();

    private sealed record OtherFilter(int FieldId) : EntryFieldFilter(FieldId);
}
