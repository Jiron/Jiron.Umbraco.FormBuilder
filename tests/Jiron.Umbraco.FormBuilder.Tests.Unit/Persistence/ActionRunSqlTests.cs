using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The due list and the claim of the action runs on SQL Server (ADR-0040, amended in 4.8b and 26.1): both check the
/// earlier runs of the batch the same way, reading the entry's runs no later than the run's own position; the claim
/// holds the rows it read with update locks, row by row, and the due list skips the rows other units hold.
/// </summary>
[TestFixture]
public sealed class ActionRunSqlTests
{
    private const string Check = "NOT EXISTS (SELECT 1 FROM [jfbActionRun] p";
    private const string Claims = " WITH (UPDLOCK, ROWLOCK, INDEX(IX_jfbActionRun_entryId_sortOrder), FORCESEEK)";
    private const string Skips = " WITH (READPAST, READCOMMITTEDLOCK)";

    [Test]
    public void TheDueList_SkipsWhatOthersHold_AndTheClaimHoldsWhatItChecked()
    {
        var n = TestSqlNames.Of(TestSqlNames.SqlServer);

        var due = ActionRunRepository.DueSql(n, 25);
        var claim = ActionRunRepository.ClaimSql(n);

        Assert.Multiple(() =>
        {
            Assert.That((n.QueueCheck(ActionRunRepository.EntryRunsIndex), n.QueueRead), Is.EqualTo((Claims, Skips)), "SQL Server's queue hints");
            Assert.That(n.LockingSeek(ActionRunRepository.EntryRunsIndex), Is.EqualTo(" WITH (UPDLOCK, HOLDLOCK, INDEX(IX_jfbActionRun_entryId_sortOrder), FORCESEEK)"));
            Assert.That(due, Does.StartWith("SELECT r.[id] FROM [jfbActionRun] r" + Skips + " WHERE"), "the due list skips the runs being claimed");
            Assert.That(due, Does.Contain(Check + Skips + " WHERE p.[entryId] = r.[entryId] AND p.[sortOrder] <= r.[sortOrder] AND p.[changeKey] = r.[changeKey]"), "it checks the earlier runs, seeking the entry's");
            Assert.That(due, Does.Not.Contain("UPDLOCK").And.Not.Contain("HOLDLOCK"), "without a lock: it only proposes");
            Assert.That(due, Does.EndWith("ORDER BY r.[nextAttemptUtc], r.[id] OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY"));
            Assert.That(claim, Does.StartWith("UPDATE [jfbActionRun] SET [state] = 'running'"));
            Assert.That(claim, Does.Contain(Check + Claims + " WHERE p.[entryId] = [jfbActionRun].[entryId] AND p.[sortOrder] <= [jfbActionRun].[sortOrder]"), "the claim holds the earlier rows it checked");
            Assert.That(claim, Does.Not.Contain("HOLDLOCK"), "no key-range lock, which reaches the next run's key");
            Assert.That(
                claim[claim.IndexOf(Check, StringComparison.Ordinal)..],
                Is.EqualTo(due[due.IndexOf(Check, StringComparison.Ordinal)..due.IndexOf(" ORDER BY", StringComparison.Ordinal)].Replace(Check + Skips, Check + Claims, StringComparison.Ordinal).Replace("r.[", "[jfbActionRun].[", StringComparison.Ordinal)),
                "the same check");
        });
    }

    [Test]
    public void OnSQLite_NeitherTakesAHint()
    {
        var n = TestSqlNames.Of(TestSqlNames.Sqlite);

        Assert.Multiple(() =>
        {
            Assert.That((n.QueueCheck(ActionRunRepository.EntryRunsIndex), n.QueueRead, n.LockingSeek(ActionRunRepository.EntryRunsIndex)), Is.EqualTo((string.Empty, string.Empty, string.Empty)));
            Assert.That(ActionRunRepository.ClaimSql(n) + ActionRunRepository.DueSql(n, 25), Does.Not.Contain("WITH ("));
        });
    }
}
