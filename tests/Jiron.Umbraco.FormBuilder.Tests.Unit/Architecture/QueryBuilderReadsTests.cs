using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// The entries query builder only reads, and always within its time budget (ADR-0039): its source in Core's
/// <c>Persistence/Queries</c> reaches the database through <c>FormBuilderDatabase.QueryAsync</c> alone and writes no
/// statement that changes a row. A read without the budget would still pass every other test that finishes in time.
/// </summary>
[TestFixture]
public sealed partial class QueryBuilderReadsTests
{
    [Test]
    public void TheQueryBuilder_ReadsOnly_InOneBudgetedRead()
    {
        var sources = Sources().ToList();
        var units = sources.SelectMany(source => Unit().Matches(source.Text).Select(match => $"{source.File}: {match.Value}")).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sources.Select(source => source.File), Is.SupersetOf(new[] { "EntryQueryRepository.cs", "EntryQuerySql.cs" }), "the scan read the query builder");
            Assert.That(units, Is.EqualTo(new[] { "EntryQueryRepository.cs: .QueryAsync(" }), "one budgeted read, and no other unit of work");
            Assert.That(sources.Where(source => Change().IsMatch(source.Text)).Select(source => source.File), Is.Empty, "no statement that changes a row");
        });
    }

    [TestCase("_database.ReadAsync(db => ListAsync(db))", true)]
    [TestCase("database.WriteAsync(", true)]
    [TestCase("db.ExecAsync(", true)]
    [TestCase("db.FetchListAsync<T>(", false)]
    public void TheScan_FindsEveryUnitOfWorkAndEveryWrite(string code, bool found) =>
        Assert.That(Unit().IsMatch(code), Is.EqualTo(found));

    [TestCase("$\"DELETE FROM {n.T(FormBuilderTables.Entry)}\"", true)]
    [TestCase("$\"UPDATE {entries} SET\"", true)]
    [TestCase("$\"SELECT COUNT(*) FROM {_entry} e\"", false)]
    public void TheScan_FindsEveryChange(string code, bool found) =>
        Assert.That(Change().IsMatch(code), Is.EqualTo(found));

    private static IEnumerable<(string File, string Text)> Sources()
    {
        var folder = RepositoryRoot.Path(typeof(QueryBuilderReadsTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Core", "Persistence", "Queries");
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
    }

    // A unit of work, or a statement that may write, called on the database.
    [GeneratedRegex(@"\.(?:ReadAsync|WriteAsync|QueryAsync|ExecAsync|InsertRowAsync)\(")]
    private static partial Regex Unit();

    // SQL that changes rows.
    [GeneratedRegex(@"\b(?:INSERT|UPDATE|DELETE|MERGE)\s")]
    private static partial Regex Change();
}
