using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The "who may see this form" clause (ADR-0048): none for every form, one that holds nothing for no form, the folders
/// as literals with their unrestricted forms, and the forms granted on their own through the groups as parameters.
/// </summary>
[TestFixture]
public sealed class AccessSqlTests
{
    private static readonly Guid Designers = Guid.Parse("5c0e9f4a-1d2b-4c3e-8f7a-6b5d4c3b2a10");
    private static readonly Guid Handlers = Guid.Parse("5c0e9f4a-1d2b-4c3e-8f7a-6b5d4c3b2a11");

    private const string Granted = "IN (SELECT p.[targetKey] FROM [jfbPermission] p WHERE p.[targetType] = 'form' AND p.[userGroupKey] IN (@0, @1) AND (p.[permissions] & @2) <> 0)";

    [Test]
    public void EveryForm_AddsNoClause_AndNoForm_OneThatHoldsNothing()
    {
        var args = new List<object?>();

        Assert.Multiple(() =>
        {
            Assert.That(AccessSql.FormClause(null, Names(), "f.", Parameters(args)), Is.Null, "no filter");
            Assert.That(AccessSql.FormClause(AccessScope.Everything, Names(), "f.", Parameters(args)), Is.Null);
            Assert.That(AccessSql.FormClause(AccessScope.Everything with { FolderIds = [1], GroupKeys = [Designers] }, Names(), "f.", Parameters(args)), Is.Null, "whatever the rest says");
            Assert.That(AccessSql.FormClause(AccessScope.Nothing, Names(), "f.", Parameters(args)), Is.EqualTo("1 = 0"));
            Assert.That(args, Is.Empty);
        });
    }

    [Test]
    public void FoldersAndTheRoot_KeepTheirUnrestrictedForms_NamingTheFoldersAsLiterals()
    {
        var args = new List<object?>();

        Assert.Multiple(() =>
        {
            Assert.That(
                AccessSql.FormClause(new AccessScope { FolderIds = [12, 7], Root = true }, Names(), "f.", Parameters(args)),
                Is.EqualTo("(f.[isRestricted] = 0 AND (f.[folderId] IN (12, 7) OR f.[folderId] IS NULL))"));
            Assert.That(AccessSql.FormClause(new AccessScope { FolderIds = [3] }, Names(), "f.", Parameters(args)), Is.EqualTo("(f.[isRestricted] = 0 AND (f.[folderId] IN (3)))"));
            Assert.That(AccessSql.FormClause(new AccessScope { Root = true }, Names(), string.Empty, Parameters(args)), Is.EqualTo("([isRestricted] = 0 AND ([folderId] IS NULL))"));
            Assert.That(args, Is.Empty, "no parameter for a number the snapshot read");
        });
    }

    [Test]
    public void FormsGrantedOnTheirOwn_AreFoundThroughTheGroups_WithOneOfThePermissions()
    {
        var args = new List<object?>();

        var clause = AccessSql.FormClause(new AccessScope { GroupKeys = [Designers, Handlers], AnyOf = FormBuilderPermissions.ViewEntries }, Names(), "f.", Parameters(args));

        Assert.Multiple(() =>
        {
            Assert.That(clause, Is.EqualTo("f.[key] " + Granted));
            Assert.That(args, Is.EqualTo(new object?[] { Designers, Handlers, 4 }));
        });
    }

    [Test]
    public void FoldersAndGrantedForms_AreEitherOr()
    {
        var args = new List<object?>();

        var clause = AccessSql.FormClause(new AccessScope { FolderIds = [5], GroupKeys = [Designers, Handlers] }, Names(), "f.", Parameters(args));

        Assert.Multiple(() =>
        {
            Assert.That(clause, Is.EqualTo("((f.[isRestricted] = 0 AND (f.[folderId] IN (5))) OR f.[key] " + Granted + ")"));
            Assert.That(args[2], Is.EqualTo((int)PermissionRules.FolderLevel), "any permission by default");
        });
    }

    [Test]
    public void AScopeOfMoreGroupsThanAStatementTakes_IsRefused()
    {
        var most = Enumerable.Range(0, PermissionRepository.GroupsPerStatement).Select(_ => Guid.NewGuid()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(AccessSql.FormClause(new AccessScope { GroupKeys = most }, Names(), "f.", Parameters([])), Does.Contain("@1000"));
            Assert.That(
                () => AccessSql.FormClause(new AccessScope { GroupKeys = [.. most, Guid.NewGuid()] }, Names(), "f.", Parameters([])),
                Throws.ArgumentException.With.Message.StartsWith("A scope names at most 1000 groups"));
            Assert.That(() => AccessSql.FormClause(AccessScope.Nothing, Names(), null!, Parameters([])), Throws.ArgumentNullException);
            Assert.That(() => AccessSql.FormClause(AccessScope.Nothing, Names(), "f.", null!), Throws.ArgumentNullException);
        });
    }

    // Names each parameter by its position, as a repository's statement does.
    private static Func<object?, string> Parameters(List<object?> args) => value =>
    {
        args.Add(value);
        return "@" + (args.Count - 1).ToString(CultureInfo.InvariantCulture);
    };

    private static SqlNames Names()
    {
        var syntax = Substitute.For<ISqlSyntaxProvider>();
        syntax.ProviderName.Returns("Microsoft.Data.SqlClient");
        syntax.GetQuotedTableName(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        syntax.GetQuotedColumnName(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        return new SqlNames(syntax);
    }
}
