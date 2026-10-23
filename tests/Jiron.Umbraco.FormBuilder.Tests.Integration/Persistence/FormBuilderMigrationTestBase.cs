using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// Runs Jiron Form Builder's migration plan, or copies of it, against the test database (ADR-0033). The test host keeps
/// unattended package migrations off (appsettings.Tests.json), so the plan stays pending at boot and the tests run it.
/// </summary>
public abstract class FormBuilderMigrationTestBase : UmbracoIntegrationTest
{
    /// <summary>The plan's steps in order, read from the plan itself.</summary>
    internal static IReadOnlyList<(string State, Type Step)> Steps => FormBuilderMigrationPlan.Steps;

    /// <summary>The state of a database on which every step has run.</summary>
    internal static string FinalState => FormBuilderMigrationState.FinalState;

    private protected static string StateKey => FormBuilderMigrationState.StateKey;

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => builder.AddComposers();

    private protected async Task<ExecutedMigrationPlan> RunAsync(MigrationPlan plan) =>
        await new Upgrader(plan).ExecuteAsync(
            GetRequiredService<IMigrationPlanExecutor>(),
            ScopeProvider,
            GetRequiredService<IKeyValueService>());

    private protected string? StoredState()
    {
        using var scope = ScopeProvider.CreateScope(autoComplete: true);
        return GetRequiredService<IKeyValueService>().GetValue(StateKey);
    }

    private protected T Query<T>(Func<IUmbracoDatabase, T> read)
    {
        using var scope = ScopeProvider.CreateScope(autoComplete: true);
        return read(scope.Database);
    }

    private protected void Write(Action<IUmbracoDatabase> write)
    {
        using var scope = ScopeProvider.CreateScope();
        write(scope.Database);
        scope.Complete();
    }

    private protected SchemaReport Inspect() => Query(SchemaInspector.Inspect);

    private protected bool IsSqlite => Query(SchemaInspector.IsSqlite);

    private protected int Count(string table, string where = "1 = 1", params object[] args) =>
        Query(db => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {where}", args));

    /// <summary>The aliases of the user groups that may open the Forms section.</summary>
    private protected IReadOnlyList<string> GroupsWithSection() =>
        Query(db => db.Fetch<string>(
                $"SELECT g.{Q("userGroupAlias")} FROM {T(Constants.DatabaseSchema.Tables.UserGroup2App)} a JOIN {T(Constants.DatabaseSchema.Tables.UserGroup)} g ON g.{Q("id")} = a.{Q("userGroupId")} WHERE a.{Q("app")} = @0",
                FormBuilderConstants.SectionAlias))
            .Order(StringComparer.Ordinal)
            .ToList();

    private protected string T(string table) => Syntax.GetQuotedTableName(table);

    private protected string Q(string column) => Syntax.GetQuotedColumnName(column);

    private ISqlSyntaxProvider Syntax => GetRequiredService<IUmbracoDatabaseFactory>().SqlContext.SqlSyntax;

    /// <summary>A copy of the plan with its first steps (all by default), optionally replaying them all or replacing the last.</summary>
    internal sealed class TestPlan : MigrationPlan
    {
        private readonly bool _ignoreCurrentState;

        public TestPlan(int? steps = null, bool ignoreCurrentState = false, Type? replaceLastStep = null)
            : base(FormBuilderConstants.MigrationPlanName)
        {
            _ignoreCurrentState = ignoreCurrentState;
            From(string.Empty);
            var count = steps ?? Steps.Count;
            for (var i = 0; i < count; i++)
            {
                To(Steps[i].State, i == count - 1 && replaceLastStep is not null ? replaceLastStep : Steps[i].Step);
            }
        }

        public override bool IgnoreCurrentState => _ignoreCurrentState;
    }

    /// <summary>Runs the real step, then fails, so the test sees whether the step's changes roll back with it.</summary>
    internal sealed class Failing<TStep>(IMigrationContext context, IServiceProvider services) : AsyncMigrationBase(context)
        where TStep : AsyncMigrationBase
    {
        protected override async Task MigrateAsync()
        {
            await ActivatorUtilities.CreateInstance<TStep>(services, Context).RunAsync();
            throw new InvalidOperationException("JFB_TEST: simulated failure after the step's changes.");
        }
    }
}
