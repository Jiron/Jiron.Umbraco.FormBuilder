using Jiron.Umbraco.FormBuilder.Core.Persistence;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>The small persistence helpers (ADR-0036): SQL that differs per database, LIKE escaping, UTC times, paging.</summary>
[TestFixture]
public sealed class PersistenceHelperTests
{
    private const string Sqlite = "Microsoft.Data.Sqlite";
    private const string SqlServer = "Microsoft.Data.SqlClient";

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

    [TestCase(Sqlite, true, "", "WITH RECURSIVE")]
    [TestCase(SqlServer, false, " WITH (UPDLOCK, HOLDLOCK)", "WITH")]
    public void TheSqlThatDiffers_FollowsTheProvider(string provider, bool sqlite, string insertOnceHint, string withRecursive)
    {
        var names = Names(provider);
        Assert.Multiple(() =>
        {
            Assert.That(names.IsSqlite, Is.EqualTo(sqlite));
            Assert.That(names.InsertOnceHint, Is.EqualTo(insertOnceHint), "inserts that must happen once lock the checked range on SQL Server");
            Assert.That(names.LockingRead, Is.EqualTo(insertOnceHint), "so do the checks whose answer must hold until the unit ends (ADR-0038)");
            Assert.That(names.WithRecursive, Is.EqualTo(withRecursive));
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

    private static SqlNames Names(string provider)
    {
        var syntax = Substitute.For<ISqlSyntaxProvider>();
        syntax.ProviderName.Returns(provider);
        return new SqlNames(syntax);
    }
}
