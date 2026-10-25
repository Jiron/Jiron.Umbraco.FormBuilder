using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The due list and the claim of the action runs on SQL Server (ADR-0040, amended in 4.8b): both check the earlier runs
/// of the batch the same way, and only the claim holds their rows while it does.
/// </summary>
[TestFixture]
public sealed class ActionRunSqlTests
{
    private const string Check = "NOT EXISTS (SELECT 1 FROM [jfbActionRun] p";
    private const string Locks = " WITH (UPDLOCK, HOLDLOCK)";

    [Test]
    public void TheDueList_ChecksTheEarlierRunsWithoutLocks_AndTheClaimWithThem()
    {
        var n = TestSqlNames.Of(TestSqlNames.SqlServer);

        var due = ActionRunRepository.DueSql(n, 25);
        var claim = ActionRunRepository.ClaimSql(n);

        Assert.Multiple(() =>
        {
            Assert.That(n.LockingRead, Is.EqualTo(Locks), "SQL Server's locking read");
            Assert.That(due, Does.Contain(Check + " WHERE p.[entryId] = r.[entryId]"), "the due list checks the earlier runs");
            Assert.That(due, Does.Not.Contain("UPDLOCK").And.Not.Contain("HOLDLOCK"), "without a lock: it only proposes");
            Assert.That(due, Does.EndWith("ORDER BY r.[nextAttemptUtc], r.[id] OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY"));
            Assert.That(claim, Does.StartWith("UPDATE [jfbActionRun] SET [state] = 'running'"));
            Assert.That(claim, Does.Contain(Check + Locks + " WHERE p.[entryId] = [jfbActionRun].[entryId]"), "the claim holds the rows it checked");
            Assert.That(claim[claim.IndexOf(Check, StringComparison.Ordinal)..], Is.EqualTo(due[due.IndexOf(Check, StringComparison.Ordinal)..due.IndexOf(" ORDER BY", StringComparison.Ordinal)].Replace(Check, Check + Locks, StringComparison.Ordinal).Replace("r.[", "[jfbActionRun].[", StringComparison.Ordinal)), "the same check");
        });
    }
}
