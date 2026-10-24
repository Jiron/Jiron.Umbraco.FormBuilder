using System.Data;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The query builder's shapes stay on their indexes (ADR-0003, ADR-0039): SQLite's <c>EXPLAIN QUERY PLAN</c> of every
/// statement it writes, for every sort, filter kind and cursor, with and without planner statistics, scans neither
/// entries nor values, and a sort by a value never sorts in a temporary b-tree. SQL Server's optimizer picks its own
/// order (research note, section 10); the correctness tests run there.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed partial class EntryQueryPlanTests : RepositoryTestBase
{
    private const int EntriesPerForm = 3000;
    private static readonly DateTime At = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task EveryShape_StaysOnItsIndexes_WithoutPlannerStatistics()
    {
        var form = await SeedAsync(analyze: false);

        Assert.That(Query(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'sqlite_stat1'")), Is.Zero, "no statistics");
        AssertPlans(form);
    }

    [Test]
    public async Task EveryShape_StaysOnItsIndexes_WithPlannerStatistics()
    {
        var form = await SeedAsync(analyze: true);

        Assert.That(
            Query(db => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM sqlite_stat1 WHERE tbl = '{FormBuilderTables.EntryValue}'")),
            Is.GreaterThanOrEqualTo(4),
            "ANALYZE measured the value indexes");
        AssertPlans(form);
    }

    // The check is not blind: without the unary plus that keeps the join from seeking the values by entry (ADR-0039),
    // SQLite carries an entry filter over to the values, and a filtered walk sorts in a temporary b-tree.
    [Test]
    public async Task ThePlanCheck_FindsAWalkThatSorts_WithoutTheUnaryPlus()
    {
        var form = await SeedAsync(analyze: true);

        var sorting = Query(db => Statements(new SqlNames(db), form).Count(shape => shape.Walk && Sorts(Explain(db, WithoutThePlus(shape.Statement)))));

        Assert.That(sorting, Is.GreaterThan(0));
    }

    private static SqlStatement WithoutThePlus(SqlStatement walk)
    {
        Assert.That(walk.Text, Does.Contain(" = +v."), "the walk joins through the unary plus");
        return walk with { Text = walk.Text.Replace(" = +v.", " = v.", StringComparison.Ordinal) };
    }

    private static bool Sorts(List<string> plan) => plan.Any(line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));

    // Every statement of every query below, explained; the failures are listed together, each with its plan.
    private void AssertPlans(TestForm form)
    {
        var failures = Query(db =>
        {
            var n = new SqlNames(db);
            var found = new List<string>();
            var count = 0;
            foreach (var (name, statement, walk) in Statements(n, form))
            {
                count++;
                var plan = Explain(db, statement);
                if (plan.Any(line => Scan().IsMatch(line)) || (walk && Sorts(plan)))
                {
                    found.Add($"{name}:{Environment.NewLine}    {string.Join(Environment.NewLine + "    ", plan)}{Environment.NewLine}    -- {statement.Text}");
                }
            }

            Assert.That(count, Is.GreaterThan(300), "the shapes explained");
            return found;
        });

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    // Each sort with each filter kind alone, all of them, a search, states, the test entries and a range, from the
    // start and after a cursor; then the reads around them.
    private static IEnumerable<(string Name, SqlStatement Statement, bool Walk)> Statements(SqlNames n, TestForm form)
    {
        var (name, amount, arrival, agree) = (form["name"], form["amount"], form["arrival"], form["agree"]);
        var longName = new string('n', 300);
        var texts = new Dictionary<(int Filter, int Value), IReadOnlyList<string>> { [(0, 1)] = [longName, longName.ToUpperInvariant()] };
        EntryFieldFilter[] filters =
        [
            new EntryTextFilter(name, EntryTextMatch.OneOf, ["name 1", longName]),
            new EntryTextFilter(name, EntryTextMatch.NoneOf, ["name 2"]),
            new EntryTextFilter(name, EntryTextMatch.StartsWith, ["name 3"]),
            new EntryNumberFilter(amount, 10m, true, 20m, false),
            new EntryDateFilter(arrival, At.AddYears(-6), At.AddYears(-5)),
            new EntryCheckedFilter(agree, true),
            new EntryCheckedFilter(agree, false),
            new EntryAnsweredFilter(amount, true),
            new EntryAnsweredFilter(amount, false),
        ];

        var plain = new EntryQuery(form.Id);
        var variations = new List<(string Name, EntryQuery Query)> { ("no filter", plain), ("every filter", plain with { Filters = filters }) };
        variations.AddRange(filters.Select(filter => (Describe(filter), plain with { Filters = [filter] })));
        variations.Add(("a search", plain with { Search = "entry 12" }));
        variations.Add(("a search and a filter", plain with { Search = "entry 12", Filters = [filters[3]] }));
        variations.Add(("one state", plain with { States = [EntryState.Approved] }));
        variations.Add(("two states", plain with { States = [EntryState.Approved, EntryState.Spam] }));
        variations.Add(("the test entries", plain with { Tests = EntryTestFilter.Only }));
        variations.Add(("every entry", plain with { Tests = EntryTestFilter.Include }));
        variations.Add(("a submission range", plain with { SubmittedFrom = At.AddYears(-1), SubmittedBefore = At }));

        EntrySort[] sorts =
        [
            EntrySort.Newest,
            EntrySort.Oldest,
            EntrySort.ByValue(name, EntrySortColumn.Text, descending: false),
            EntrySort.ByValue(name, EntrySortColumn.Text, descending: true),
            EntrySort.ByValue(amount, EntrySortColumn.Number, descending: false),
            EntrySort.ByValue(amount, EntrySortColumn.Number, descending: true),
            EntrySort.ByValue(arrival, EntrySortColumn.Date, descending: false),
            EntrySort.ByValue(arrival, EntrySortColumn.Date, descending: true),
        ];

        foreach (var (variation, query) in variations)
        {
            var sql = new EntryQuerySql(n, query, texts);
            yield return ($"count, {variation}", sql.Count(), false);
            foreach (var sort in sorts)
            {
                var sorted = new EntryQuerySql(n, query with { Sort = sort }, texts);
                var label = $"{sort.Column} {(sort.Descending ? "descending" : "ascending")}, {variation}";
                if (!sort.ByField)
                {
                    yield return (label, sorted.BySubmission(null, 51), false);
                    yield return (label + ", after", sorted.BySubmission(new EntryCursor(sort, 100, At, null), 51), false);
                    continue;
                }

                var position = new EntryValuePosition(100, "name 7", 50, At.AddYears(-3));
                yield return ("walk " + label, sorted.Walk(null, 51), true);
                yield return ("walk " + label + ", after", sorted.Walk(new EntryCursor(sort, 100, At, position), 51), true);
                yield return ("tail " + label, sorted.Tail(null, 51), false);
                yield return ("tail " + label + ", after", sorted.Tail(new EntryCursor(sort, 100, At, null), 51), false);
            }
        }

        yield return ("long texts", EntryQuerySql.LongTexts(n, name, new string('n', EntryTextKey.MaxLength)), false);
        yield return ("load", EntryQuerySql.Load(n, [.. Enumerable.Range(1, 50).Select(i => (long)i)]), false);
    }

    private static string Describe(EntryFieldFilter filter) => filter switch
    {
        EntryTextFilter text => "text " + text.Match,
        EntryNumberFilter => "a number range",
        EntryDateFilter => "a date range",
        EntryCheckedFilter check => check.Checked ? "checked" : "not checked",
        EntryAnsweredFilter answered => answered.Answered ? "answered" : "not answered",
        _ => filter.ToString(),
    };

    private static List<string> Explain(IUmbracoDatabase db, SqlStatement statement)
    {
        db.ExecuteScalar<int>("SELECT 1");
        using var command = db.CreateCommand(db.Connection!, CommandType.Text, "EXPLAIN QUERY PLAN " + statement.Text, statement.Args!);
        using var reader = command.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return plan;
    }

    // Two forms of 3,000 entries each, with a text, a number, a date and a checkbox, some with a second answer.
    private async Task<TestForm> SeedAsync(bool analyze)
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's; SQL Server's optimizer picks its own order");
        var form = await CreateTestFormAsync("name", "amount", "arrival", "agree");
        var other = await CreateTestFormAsync("name", "amount", "arrival", "agree");
        Write(db =>
        {
            foreach (var target in new[] { form, other })
            {
                Seed(db, target);
            }

            if (analyze)
            {
                db.Execute("ANALYZE");
            }
        });

        return form;
    }

    private void Seed(IUmbracoDatabase db, TestForm form)
    {
        var (entry, value) = (T(FormBuilderTables.Entry), T(FormBuilderTables.EntryValue));
        var time = $"strftime('%Y-%m-%d %H:%M:%f', '2026-01-01', '+' || i || ' minutes')";
        db.Execute(
            $"""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {EntriesPerForm})
            INSERT INTO {entry} ({Q("key")}, {Q("formId")}, {Q("formVersionId")}, {Q("number")}, {Q("state")}, {Q("isTest")}, {Q("submissionId")}, {Q("digest")}, {Q("culture")}, {Q("spamScore")}, {Q("spamReasons")}, {Q("searchText")}, {Q("context")}, {Q("submittedUtc")}, {Q("updatedUtc")}, {Q("stateChangedUtc")})
            SELECT lower(hex(randomblob(16))), @0, @1, i, CASE i % 5 WHEN 0 THEN 'Approved' WHEN 1 THEN 'Spam' ELSE 'Submitted' END, i % 50 = 0, lower(hex(randomblob(16))), lower(hex(randomblob(32))), 'en-US', 0, '', 'entry ' || i, @2, {time}, {time}, {time}
            FROM n
            """,
            form.Id,
            form.VersionId,
            "{}");

        string Values(string columns, string select, int fieldId, string where = "1 = 1") =>
            $"INSERT INTO {value} ({Q("entryId")}, {Q("fieldId")}, {Q("rowIndex")}, {Q("valueIndex")}, {columns}) SELECT {Q("id")}, {fieldId}, 0, 0, {select} FROM {entry} WHERE {Q("formId")} = @0 AND {where}";

        db.Execute(Values($"{Q("text")}, {Q("textKey")}", $"'name ' || ({Q("number")} % 40), 'name ' || ({Q("number")} % 40)", form["name"]), form.Id);
        db.Execute(Values($"{Q("text")}, {Q("number")}", $"'n', {Q("number")} % 100", form["amount"], $"{Q("number")} % 7 <> 0"), form.Id);
        db.Execute(Values($"{Q("text")}, {Q("date")}", $"'d', strftime('%Y-%m-%d %H:%M:%f', '2020-01-01', '+' || {Q("number")} || ' days')", form["arrival"]), form.Id);
        db.Execute(Values($"{Q("text")}, {Q("bool")}", $"'b', {Q("number")} % 2", form["agree"]), form.Id);
        db.Execute(
            $"INSERT INTO {value} ({Q("entryId")}, {Q("fieldId")}, {Q("rowIndex")}, {Q("valueIndex")}, {Q("text")}, {Q("textKey")}) SELECT {Q("id")}, {form["name"]}, 0, 1, 'second', 'second' FROM {entry} WHERE {Q("formId")} = @0 AND {Q("number")} % 10 = 0",
            form.Id);
    }

    // A plan line that reads a whole table or index of entries or values: every shape must seek instead.
    [GeneratedRegex(@"^SCAN (v|vf|vw|vc|vt|e|ec|jfbEntry|jfbEntryValue)( |$)")]
    private static partial Regex Scan();
}
