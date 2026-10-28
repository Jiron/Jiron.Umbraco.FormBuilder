using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The answers that hold a unique value on SQL Server (ADR-0091): the read while the answers are read takes no lock, and
/// the read in the submission's unit holds every row it reads; both compare a value by its column, or a text by its key,
/// and count only the entries that hold their answers.
/// </summary>
[TestFixture]
public sealed class UniqueAnswerSqlTests
{
    private const string Locks = " WITH (UPDLOCK, HOLDLOCK)";
    private const int FormId = 7;
    private const char Escape = SqlNames.EscapeCharacter;
    private static readonly Guid Field = new("6f1c2a5e-0b7d-4c3e-9a1f-2d4e6b8c0a12");

    [Test]
    public void TheUnitsRead_LocksEveryTable_AndTheEarlyReadNone()
    {
        var n = TestSqlNames.Of(TestSqlNames.SqlServer);
        var value = new AnswerValue(Field, "ada@example.com", null, null);

        var early = EntryRepository.EarlyHolders(n, new SqlArgs(), FormId, value, "ada@example.com");
        var unit = EntryRepository.UnitHolders(n, new SqlArgs(), FormId, 42, value, "ada@example.com");

        Assert.Multiple(() =>
        {
            Assert.That(n.LockingRead, Is.EqualTo(Locks), "SQL Server's locking read");
            Assert.That(early, Does.StartWith("[jfbEntryValue] o INNER JOIN [jfbFormField] f ON f.[id] = o.[fieldId] INNER JOIN [jfbEntry] e ON e.[id] = o.[entryId] WHERE "));
            Assert.That(early, Does.Not.Contain("UPDLOCK").And.Not.Contain("HOLDLOCK"), "without a lock: the unit asks again");
            Assert.That(unit, Does.StartWith($"[jfbEntryValue] o{Locks} INNER JOIN [jfbFormField] f{Locks} ON f.[id] = o.[fieldId] INNER JOIN [jfbEntry] e{Locks} ON e.[id] = o.[entryId] WHERE "));
            Assert.That(unit, Does.EndWith(" AND e.[id] <> @8"), "the unit leaves its own entry out");
            Assert.That(unit.Replace(Locks, string.Empty, StringComparison.Ordinal), Is.EqualTo(early + " AND e.[id] <> @8"), "the same check");
        });
    }

    [Test]
    public void AText_ComparesByItsKey_SoughtOnTheIndex_AndKeptInABinaryCollation()
    {
        var n = TestSqlNames.Of(TestSqlNames.SqlServer);
        var a = new SqlArgs();
        var key = "50%_off";

        var sql = EntryRepository.EarlyHolders(n, a, FormId, new AnswerValue(Field, "50%_off", null, null), key);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain($"WHERE f.[formId] = @0 AND f.[fieldKey] = @1 AND o.[textKey] = @2 AND o.[textKey] COLLATE Latin1_General_BIN2 LIKE @3 {SqlNames.EscapeClause} AND "));
            Assert.That(a.Values[..4], Is.EqualTo(new object?[] { FormId, Field, key, string.Concat("50", Escape, "%", Escape, "_off") }), "the wildcards match only themselves");
        });
    }

    [Test]
    public void ANumberOrADate_ComparesByItsColumn()
    {
        var n = TestSqlNames.Of(TestSqlNames.SqlServer);
        var numbers = new SqlArgs();
        var dates = new SqlArgs();
        var date = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);

        var number = EntryRepository.EarlyHolders(n, numbers, FormId, new AnswerValue(Field, null, 12.5, null), null);
        var day = EntryRepository.UnitHolders(n, dates, FormId, 42, new AnswerValue(Field, null, null, date), null);

        Assert.Multiple(() =>
        {
            Assert.That(number, Does.Contain("AND f.[fieldKey] = @1 AND o.[number] = @2 AND e.").And.Not.Contain("textKey"));
            Assert.That(numbers.Values[2], Is.EqualTo(12.5));
            Assert.That(day, Does.Contain("AND f.[fieldKey] = @1 AND o.[date] = @2 AND e.").And.Not.Contain("textKey"), "in the unit too");
            Assert.That(dates.Values[2], Is.EqualTo(date));
        });
    }

    [Test]
    public void OnlyTheEntriesThatCountTowardTheLimit_HoldTheirAnswers()
    {
        var n = TestSqlNames.Of(TestSqlNames.SqlServer);
        var a = new SqlArgs();

        var sql = EntryRepository.EarlyHolders(n, a, FormId, new AnswerValue(Field, null, 1, null), null);

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.EndWith("AND e.[isTest] = @3 AND e.[state] IN (@4, @5, @6)"));
            Assert.That(a.Values[3..], Is.EqualTo(new object?[] { false, "unverified", "submitted", "approved" }), "no test, draft, spam or rejected entry");
        });
    }
}
