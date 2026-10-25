using Jiron.Umbraco.FormBuilder.Core.Persistence;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The small persistence helpers (ADR-0036): SQL that differs per database, LIKE escaping, UTC times, paging, and the
/// arguments of a statement.
/// </summary>
[TestFixture]
public sealed class PersistenceHelperTests
{
    private const string Sqlite = TestSqlNames.Sqlite;
    private const string SqlServer = TestSqlNames.SqlServer;

    [TestCase("anna", "anna")]
    [TestCase("100%", @"100\%")]
    [TestCase("a_b", @"a\_b")]
    [TestCase("[x]", @"\[x]")]
    [TestCase(@"back\slash", @"back\\slash")]
    [TestCase(@"\%_[", @"\\\%\_\[")]
    public void Escape_EscapesEveryWildcard(string text, string escaped) =>
        Assert.That(SqlNames.Escape(text), Is.EqualTo(escaped));

    [Test]
    public void Escape_GoesWithItsClause() =>
        Assert.That(SqlNames.EscapeClause, Is.EqualTo("ESCAPE '" + SqlNames.EscapeCharacter + "'"));

    [TestCase(Sqlite, 0, 25, "LIMIT 25 OFFSET 0")]
    [TestCase(Sqlite, 1000, 500, "LIMIT 500 OFFSET 1000")]
    [TestCase(SqlServer, 0, 25, "OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY")]
    [TestCase(SqlServer, 1000, 500, "OFFSET 1000 ROWS FETCH NEXT 500 ROWS ONLY")]
    public void Page_IsTheDatabasesPagingClause(string provider, long skip, int take, string clause) =>
        Assert.That(Names(provider).Page(skip, take), Is.EqualTo(clause));

    [Test]
    public void Page_RefusesNumbersThatAreNoPage()
    {
        var names = Names(Sqlite);
        Assert.Multiple(() =>
        {
            Assert.That(() => names.Page(-1, 10), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => names.Page(0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [TestCase(Sqlite, true, "", "WITH RECURSIVE", " COLLATE BINARY", "IFNULL")]
    [TestCase(SqlServer, false, " WITH (UPDLOCK, HOLDLOCK)", "WITH", " COLLATE Latin1_General_BIN2", "ISNULL")]
    public void TheSqlThatDiffers_FollowsTheProvider(string provider, bool sqlite, string insertOnceHint, string withRecursive, string binaryCollation, string ifNull)
    {
        var names = Names(provider);
        Assert.Multiple(() =>
        {
            Assert.That(names.IsSqlite, Is.EqualTo(sqlite));
            Assert.That(names.InsertOnceHint, Is.EqualTo(insertOnceHint), "inserts that must happen once lock the checked range on SQL Server");
            Assert.That(names.LockingRead, Is.EqualTo(insertOnceHint), "so do the checks whose answer must hold until the unit ends (ADR-0038)");
            Assert.That(names.WithRecursive, Is.EqualTo(withRecursive));
            Assert.That(names.BinaryCollation, Is.EqualTo(binaryCollation), "the query builder compares text again by its code units (ADR-0039)");
            Assert.That(names.IfNull, Is.EqualTo(ifNull), "a fallback that runs its subquery once");
        });
    }

    [Test]
    public void Names_AreQuotedByTheProvider()
    {
        var syntax = Substitute.For<ISqlSyntaxProvider>();
        syntax.GetQuotedTableName("jfbForm").Returns("[jfbForm]");
        syntax.GetQuotedColumnName("alias").Returns("[alias]");
        var names = new SqlNames(syntax);

        Assert.Multiple(() =>
        {
            Assert.That(names.T("jfbForm"), Is.EqualTo("[jfbForm]"));
            Assert.That(names.C("alias"), Is.EqualTo("[alias]"));
        });
    }

    [Test]
    public void Times_AreStoredAsUtc_AndReadBackAsUtcInstants()
    {
        var local = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(2));

        var stored = FormBuilderDbTime.Write(local);
        var read = FormBuilderDbTime.Read(DateTime.SpecifyKind(stored, DateTimeKind.Unspecified));

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(stored.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(read, Is.EqualTo(local));
            Assert.That(read.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(FormBuilderDbTime.Read((DateTime?)null), Is.Null);
            Assert.That(FormBuilderDbTime.Write((DateTimeOffset?)null), Is.Null);
            Assert.That(FormBuilderDbTime.Write((DateTimeOffset?)local), Is.EqualTo(stored));
        });
    }

    [Test]
    public void APageRequest_HoldsOneToFiveHundredRows_AfterAnyNumberOfRows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PageRequest.First(20), Is.EqualTo(new PageRequest(0, 20)));
            Assert.That(new PageRequest(long.MaxValue, PageRequest.MaxTake).Take, Is.EqualTo(500));
            Assert.That(() => new PageRequest(-1, 20), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PageRequest(0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PageRequest(0, PageRequest.MaxTake + 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void Arguments_AreNumberedInTheOrderTheyAreAdded_EachWithItsValue()
    {
        var args = new SqlArgs();

        var first = args.Add("contact");
        var list = args.List([3, null, 5]);
        var last = args.Add(Guid.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("@0"));
            Assert.That(list, Is.EqualTo("@1, @2, @3"));
            Assert.That(last, Is.EqualTo("@4"));
            Assert.That(args.Count, Is.EqualTo(5));
            Assert.That(args.Values, Is.EqualTo(new object?[] { "contact", 3, null, 5, Guid.Empty }));
            Assert.That(args.Values, Is.Not.SameAs(args.Values), "a copy each time");
            Assert.That(new SqlArgs().Values, Is.Empty);
        });
    }

    [Test]
    public void Placeholders_NumberTheValuesOfAnArray_FromTheFirstGiven()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SqlArgs.Placeholders(0, 3), Is.EqualTo("@0, @1, @2"));
            Assert.That(SqlArgs.Placeholders(2, 1), Is.EqualTo("@2"));
            Assert.That(SqlArgs.Placeholders(9, 2), Is.EqualTo("@9, @10"));
            Assert.That(() => SqlArgs.Placeholders(-1, 2), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => SqlArgs.Placeholders(0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>(), "an empty IN list is no statement");
            Assert.That(SqlArgs.MaxListLength, Is.EqualTo(1000), "well inside SQL Server's 2,100 parameters");
        });
    }

    private static SqlNames Names(string provider) => TestSqlNames.Of(provider);
}
