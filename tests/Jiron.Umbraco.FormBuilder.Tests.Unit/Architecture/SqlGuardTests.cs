using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Guards are locking reads (ADR-0036, ADR-0038): every <c>EXISTS</c> check in the repositories' SQL, and every count of
/// the ids a unit names or of the entries that count toward a form's limit (ADR-0088), reads with <c>SqlNames.InsertOnceHint</c> or <c>SqlNames.LockingRead</c>. On SQL Server under
/// read-committed snapshot a plain check reads a row's last committed version and misses a writer at the same moment;
/// on SQLite the hints are empty, so no database test here can tell. The test reads the repositories' source, the
/// strongest check that runs without a SQL Server.
/// </summary>
[TestFixture]
public sealed partial class SqlGuardTests
{
    // The checks that read without a lock on purpose: the file, a text of the check, and the reason.
    private static readonly (string File, string Check, string Reason)[] Unlocked =
    [
        ("ThemeRepository.cs", "themeAlias", "a name without a foreign key: ADR-0037 accepts a publish while a delete runs"),
        ("FormRepository.Versions.cs", "Entry)} e WHERE e.{n.C(\"formVersionId\")", "the cleanup's nominating read: each delete checks again with a locking read (ADR-0045)"),
        ("ActionRunRepository.cs", "{runs} p{(locking ? n.LockingRead : string.Empty)", "the due list only proposes; the claim makes the same check with a locking read (ADR-0040, ActionRunSqlTests)"),
        ("ActionRunRepository.cs", "FROM {entries} e WHERE e.{n.C(\"id\")", "an action's last run, which the Actions view shows: nothing is written after the read, and a lock would hold the queue's writers back (ADR-0195)"),
        ("EntryRepository.Unique.cs", "EXISTS (SELECT 1 FROM {held})", "the held answers read each table with a lock in the submission's unit; the read before it only proposes (ADR-0091, UniqueAnswerSqlTests)"),
        ("ReferenceRepository.cs", "SELECT 1 FROM {nodes} WHERE", "the rebuild's last part reads Umbraco's nodes: a page's rows are written after its node is committed, so a plain read sees every node a row names, and a lock would hold Umbraco's own saves (ADR-0131)"),
        ("FormRepository.Catalogue.cs", "FROM {n.T(FormBuilderTables.FormField)} g WHERE", "the field search leaves a grid's rows out of what it lists; nothing is written after it, and a lock would hold the designer's saves (ADR-0166)"),
    ];

    // Common table expressions a check may read: their own reads carry the hint.
    private static readonly string[] Expressions = ["up", "down"];

    [Test]
    public void EveryGuard_ReadsWithALock_OnSqlServer()
    {
        var guards = Matches(Guard()).ToList();
        var unlocked = guards
            .Where(guard => !guard.Match.Groups["hint"].Success)
            .Where(guard => !Unlocked.Any(known => known.File == guard.File && guard.Match.Value.Contains(known.Check, StringComparison.Ordinal)))
            .Select(guard => $"{guard.File}: {guard.Match.Value}")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(guards, Has.Count.GreaterThan(25), "the scan read the repositories' checks");
            Assert.That(unlocked, Is.Empty, "a check without SqlNames.LockingRead or SqlNames.InsertOnceHint");
        });
    }

    [Test]
    public void TheRebuildsWrite_ReadsTheDocumentsVersionWithALock_OnSqlServer()
    {
        // Without it a save can commit, and its handler write, between this read and the rebuild's own write (ADR-0131).
        var source = Sources().Single(source => source.File == "ReferenceRepository.cs").Text;

        Assert.That(source, Does.Contain("FROM {n.T(ContentVersionTable)}{n.LockingRead} WHERE"));
    }

    [Test]
    public void TheMailLimits_CountWithALock_OnSqlServer()
    {
        // Without it two requests count the same mail and pass the limit together (ADR-0012).
        var source = Sources().Single(source => source.File == "EmailRepository.cs").Text;

        Assert.That(source.Split("COUNT(*) AS {n.C(\"sent\")} FROM {emails}{n.LockingRead} WHERE").Length - 1, Is.EqualTo(2));
    }

    [Test]
    public void EveryExistsCheck_IsOneTheScanReads()
    {
        var unread = Matches(Exists())
            .Where(check => !Guard().IsMatch(check.Match.Value))
            .Where(check => !Expressions.Any(name => check.Match.Value.EndsWith(" " + name, StringComparison.Ordinal)))
            .Select(check => $"{check.File}: {check.Match.Value}")
            .ToList();

        Assert.That(unread, Is.Empty, "an EXISTS check written another way: write it as EXISTS (SELECT 1 FROM {table} alias{hint} ...)");
    }

    [Test]
    public void EveryCountOfNamedIds_ReadsWithALock_OnSqlServer()
    {
        var counts = Matches(Count()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(counts.Select(count => count.File), Is.EqualTo(new[] { "EntryRepository.Values.cs", "EntryRepository.Values.cs", "EntryRepository.cs" }), "the fields and the files an entry names, and the entries that count toward a limit");
            Assert.That(counts.Where(count => !count.Match.Groups["hint"].Success).Select(count => $"{count.File}: {count.Match.Value}"), Is.Empty);
        });
    }

    [Test]
    public void TheChecksWithoutALock_StillExist()
    {
        var sources = Sources().ToList();

        Assert.That(
            Unlocked.Where(known => !sources.Any(source => source.File == known.File && source.Text.Contains(known.Check, StringComparison.Ordinal))),
            Is.Empty,
            "a reason for a check that is gone");
    }

    [TestCase("WHERE EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)} WHERE {n.C(\"id\")} = @1)", false)]
    [TestCase("WHERE EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)}{n.LockingRead} WHERE {n.C(\"id\")} = @1)", true)]
    [TestCase("NOT EXISTS (SELECT 1 FROM {table} c WHERE c.{n.C(\"parentId\")} = @0)", false)]
    [TestCase("NOT EXISTS (SELECT 1 FROM {table} c{n.LockingRead} WHERE c.{n.C(\"parentId\")} = @0)", true)]
    [TestCase("NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C(\"key\")} = @0)", true)]
    public void TheScan_TellsALockingCheck_FromAPlainOne(string sql, bool locked)
    {
        var match = Guard().Match(sql);

        Assert.Multiple(() =>
        {
            Assert.That(match.Success, Is.True);
            Assert.That(match.Groups["hint"].Success, Is.EqualTo(locked));
        });
    }

    [TestCase("EXISTS (SELECT 1 FROM up WHERE id = @1)", true)]
    [TestCase("EXISTS (SELECT 1 FROM {table} WHERE {n.C(\"id\")} = @1)", true)]
    [TestCase("EXISTS (SELECT * FROM {table} WHERE {n.C(\"id\")} = @1)", false)]
    public void TheScan_FindsEveryExistsCheck_AndReadsTheUsualOnes(string sql, bool read)
    {
        var match = Exists().Match(sql);

        Assert.Multiple(() =>
        {
            Assert.That(match.Success, Is.True);
            Assert.That(Guard().IsMatch(match.Value) || Expressions.Any(name => match.Value.EndsWith(" " + name, StringComparison.Ordinal)), Is.EqualTo(read));
        });
    }

    [TestCase("SELECT COUNT(*) FROM {n.T(FormBuilderTables.FormField)} WHERE {n.C(\"formId\")} = @0 AND {n.C(\"id\")} IN ({SqlArgs.Placeholders(1, chunk.Length)})", false)]
    [TestCase("SELECT COUNT(*) FROM {n.T(FormBuilderTables.FormField)}{n.LockingRead} WHERE {n.C(\"formId\")} = @0 AND {n.C(\"id\")} IN ({SqlArgs.Placeholders(1, chunk.Length)})", true)]
    public void TheScan_FindsACountOfNamedIds(string sql, bool locked)
    {
        var match = Count().Match(sql);

        Assert.Multiple(() =>
        {
            Assert.That(match.Success, Is.True);
            Assert.That(match.Groups["hint"].Success, Is.EqualTo(locked));
        });
    }

    private static IEnumerable<(string File, Match Match)> Matches(Regex pattern) =>
        Sources().SelectMany(source => pattern.Matches(source.Text).Select(match => (source.File, match)));

    private static IEnumerable<(string File, string Text)> Sources()
    {
        var folder = RepositoryRoot.Path(typeof(SqlGuardTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Core", "Persistence", "Repositories");
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
    }

    // EXISTS (SELECT 1 FROM {table}, an optional alias, the hint if there is one, and the check up to a parenthesis.
    [GeneratedRegex(@"EXISTS \(SELECT 1 FROM \{[^}]+\}(?: (?!WHERE\b)\w+)?(?<hint>\{n\.(?:InsertOnceHint|LockingRead)\})?[^)\n]*\)?")]
    private static partial Regex Guard();

    // Every EXISTS check with the table it reads, however it is written.
    [GeneratedRegex(@"EXISTS \(SELECT [^)\n]*?FROM (?:\{[^}]+\}|\w+)")]
    private static partial Regex Exists();

    // A count of the ids a unit names: SELECT COUNT(*) FROM {table}{hint} WHERE ... IN ({SqlArgs.Placeholders(...)}).
    [GeneratedRegex(@"SELECT COUNT\(\*\) FROM \{[^}]+\}(?<hint>\{n\.LockingRead\})?[^\n]*? IN \(\{SqlArgs\.Placeholders\(")]
    private static partial Regex Count();
}
