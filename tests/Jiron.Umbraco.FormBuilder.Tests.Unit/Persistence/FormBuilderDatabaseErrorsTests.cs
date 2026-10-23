using System.Data.Common;
using Jiron.Umbraco.FormBuilder.Core.Persistence;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// What a database error means (ADR-0036): which ones a unit of work runs again after, which ones are a unique key, and
/// how a log names them. Core references neither database client, so the errors are read by their type's name and
/// their codes; these stand-ins carry both as the clients do.
/// </summary>
[TestFixture]
public sealed class FormBuilderDatabaseErrorsTests
{
    [TestCase(5, true, TestName = "SQLite busy")]
    [TestCase(6, true, TestName = "SQLite locked")]
    [TestCase(19, false, TestName = "SQLite constraint")]
    [TestCase(1, false, TestName = "SQLite error")]
    public void SQLite_LockConflictsAreTransient_ForReadsAndWrites(int code, bool transient)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new SqliteException(code), write: true), Is.EqualTo(transient));
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new SqliteException(code), write: false), Is.EqualTo(transient));
        });
    }

    [TestCase(1205, true, true, TestName = "SQL Server deadlock victim")]
    [TestCase(1222, true, true, TestName = "SQL Server lock timeout")]
    [TestCase(-2, false, true, TestName = "SQL Server command timeout")]
    [TestCase(10054, false, true, TestName = "SQL Server connection reset")]
    [TestCase(40613, false, true, TestName = "Azure SQL database unavailable")]
    [TestCase(2627, false, false, TestName = "SQL Server unique key")]
    [TestCase(547, false, false, TestName = "SQL Server foreign key")]
    public void SqlServer_LockConflictsAreTransient_LostConnectionsOnlyForReads(int number, bool forWrites, bool forReads)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new SqlException(number), write: true), Is.EqualTo(forWrites), "write");
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new SqlException(number), write: false), Is.EqualTo(forReads), "read");
        });
    }

    [Test]
    public void ADatabaseErrorInsideAnother_Counts_AndOtherErrorsDoNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new InvalidOperationException("wrapped", new SqliteException(5)), write: true), Is.True);
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new TimeoutException(), write: false), Is.False);
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new OtherDbException(), write: false), Is.False, "an unknown client");
            Assert.That(FormBuilderDatabaseErrors.IsTransient(new FormBuilderConcurrencyException("stale"), write: true), Is.False);
        });
    }

    [TestCase(2067, true, TestName = "SQLite unique index")]
    [TestCase(1555, true, TestName = "SQLite primary key")]
    [TestCase(787, false, TestName = "SQLite foreign key")]
    [TestCase(1299, false, TestName = "SQLite not null")]
    public void SQLite_UniqueViolations_AreTheConstraintErrorsOfAUniqueKey(int extended, bool unique)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new SqliteException(19, extended)), Is.EqualTo(unique));
            Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new SqliteException(5, extended)), Is.False, "only a constraint error");
        });
    }

    [Test]
    public void SQLite_AConstraintErrorWithoutAnExtendedCode_CountsAsUnique()
    {
        // Older clients carry no extended code; the statements that may meet one check uniqueness first anyway.
        Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new SqliteException(19)), Is.True);
    }

    [TestCase(2601, true, TestName = "SQL Server unique index")]
    [TestCase(2627, true, TestName = "SQL Server unique constraint")]
    [TestCase(547, false, TestName = "SQL Server constraint")]
    [TestCase(1205, false, TestName = "SQL Server deadlock")]
    public void SqlServer_UniqueViolations_AreDuplicateKeys(int number, bool unique)
    {
        Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new SqlException(number)), Is.EqualTo(unique));
    }

    [Test]
    public void AUniqueViolationInsideAnother_Counts_AndOtherErrorsDoNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new InvalidOperationException("wrapped", new SqlException(2601))), Is.True);
            Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new OtherDbException()), Is.False);
            Assert.That(FormBuilderDatabaseErrors.IsUniqueViolation(new InvalidOperationException("JFB_TEST")), Is.False);
        });
    }

    [Test]
    public void Describe_NamesTheErrorAndItsCode_NeverItsMessage()
    {
        // A database's message can quote the values of the statement, for example a duplicate alias.
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDatabaseErrors.Describe(new SqliteException(19, 2067)), Is.EqualTo("SqliteException 2067"));
            Assert.That(FormBuilderDatabaseErrors.Describe(new SqliteException(5)), Is.EqualTo("SqliteException 5"));
            Assert.That(FormBuilderDatabaseErrors.Describe(new SqlException(1205)), Is.EqualTo("SqlException 1205"));
            Assert.That(FormBuilderDatabaseErrors.Describe(new InvalidOperationException("JFB_TEST secret", new SqlException(2601))), Is.EqualTo("SqlException 2601"));
            Assert.That(FormBuilderDatabaseErrors.Describe(new OtherDbException()), Is.EqualTo(nameof(OtherDbException)));
            Assert.That(FormBuilderDatabaseErrors.Describe(new InvalidOperationException("JFB_TEST secret")), Is.EqualTo(nameof(InvalidOperationException)));
        });
    }

    /// <summary>Shaped like Microsoft.Data.Sqlite's exception.</summary>
    private sealed class SqliteException(int code, int? extended = null) : DbException("JFB_TEST secret value")
    {
        public int SqliteErrorCode { get; } = code;

        // The real client always has the property; null leaves it out, as an older client would.
        public int? SqliteExtendedErrorCode { get; } = extended;
    }

    /// <summary>Shaped like Microsoft.Data.SqlClient's exception.</summary>
    private sealed class SqlException(int number) : DbException("JFB_TEST secret value")
    {
        public int Number { get; } = number;
    }

    private sealed class OtherDbException() : DbException("JFB_TEST");
}
