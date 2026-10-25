using Jiron.Umbraco.FormBuilder.Core.Persistence;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>The names of a database, for the tests of statement text: its provider, and every name in brackets.</summary>
internal static class TestSqlNames
{
    /// <summary>The provider name of the SQLite client.</summary>
    public const string Sqlite = "Microsoft.Data.Sqlite";

    /// <summary>The provider name of the SQL Server client.</summary>
    public const string SqlServer = "Microsoft.Data.SqlClient";

    /// <summary>The names of a database of <paramref name="provider"/>.</summary>
    public static SqlNames Of(string provider)
    {
        var syntax = Substitute.For<ISqlSyntaxProvider>();
        syntax.ProviderName.Returns(provider);
        syntax.GetQuotedTableName(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        syntax.GetQuotedColumnName(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        return new SqlNames(syntax);
    }
}
