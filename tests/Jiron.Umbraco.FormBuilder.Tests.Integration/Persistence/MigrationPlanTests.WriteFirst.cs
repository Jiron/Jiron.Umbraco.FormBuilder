using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

// The data steps write first (27.1, the first site's finding 10): a step that fills a table does it in statements that
// write before anything is read, so beside a second process on the same SQLite file it waits its turn for the write lock,
// and running it again changes nothing.
public sealed partial class MigrationPlanTests
{
    [Test]
    public async Task TheDefaultSettings_AreOneStatement_ThatReadsNothingFirst_AndARerunLeavesOneRowEach()
    {
        await RunAsync(new TestPlan(1));
        var first = await RunAsync(new TestPlan(2, replaceLastStep: typeof(Recorded<CreateDefaultSettingsV1>)));
        var firstStatements = Statements.Take();
        var again = await RunAsync(new TestPlan(2, ignoreCurrentState: true, replaceLastStep: typeof(Recorded<CreateDefaultSettingsV1>)));
        var againStatements = Statements.Take();

        Assert.Multiple(() =>
        {
            Assert.That((first.Successful, again.Successful), Is.EqualTo((true, true)), first.Exception?.ToString() ?? again.Exception?.ToString());
            Assert.That(firstStatements, Has.Count.EqualTo(1), "one statement");
            Assert.That(firstStatements, Has.All.StartsWith("INSERT INTO"), "which writes, reading nothing before it");
            Assert.That(againStatements, Has.Count.EqualTo(1).And.All.StartsWith("INSERT INTO"));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count), "one row for each section, after two runs");
            Assert.That(Count(FormBuilderTables.Setting, $"{Q("json")} = @0 AND {Q("version")} = @1 AND {Q("schemaVersion")} = @1", "{}", 1), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count));
        });
    }

    [Test]
    public async Task TheSectionGrant_WritesFirst_AndARerunGrantsNothingAgain()
    {
        await RunAsync(new TestPlan(2));
        var first = await RunAsync(new TestPlan(3, replaceLastStep: typeof(Recorded<GrantFormsSectionV1>)));
        var firstStatements = Statements.Take();
        var again = await RunAsync(new TestPlan(3, ignoreCurrentState: true, replaceLastStep: typeof(Recorded<GrantFormsSectionV1>)));

        Assert.Multiple(() =>
        {
            Assert.That((first.Successful, again.Successful), Is.EqualTo((true, true)), first.Exception?.ToString() ?? again.Exception?.ToString());
            Assert.That(firstStatements.Take(2), Has.All.StartsWith("INSERT INTO"), "the grant and the flag come first, each a write");
            Assert.That(GroupsWithSection(), Is.EqualTo(Admins));
            Assert.That(Count(FormBuilderTables.KeyValue, $"{Q("key")} = @0 AND {Q("value")} = @1", GrantFormsSectionV1.GrantedFlag, Admins[0]), Is.EqualTo(1));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<StepStatements>();
    }

    private StepStatements Statements => GetRequiredService<StepStatements>();

    /// <summary>The statements a recorded step ran, each run's taken by the test.</summary>
    internal sealed class StepStatements
    {
        private readonly List<string> _texts = [];

        /// <summary>Adds a run's statements.</summary>
        public void Add(IEnumerable<string> texts) => _texts.AddRange(texts);

        /// <summary>The statements so far, which are forgotten.</summary>
        public List<string> Take()
        {
            List<string> taken = [.. _texts];
            _texts.Clear();
            return taken;
        }
    }

    /// <summary>Runs the real step with its statements recorded, from its first to its last.</summary>
    internal sealed class Recorded<TStep>(IMigrationContext context, IServiceProvider services) : AsyncMigrationBase(context)
        where TStep : AsyncMigrationBase
    {
        protected override async Task MigrateAsync()
        {
            var log = new CommandLog();
            Context.Database.Interceptors.Add(log);
            try
            {
                await ActivatorUtilities.CreateInstance<TStep>(services, Context).RunAsync();
            }
            finally
            {
                Context.Database.Interceptors.Remove(log);
                services.GetRequiredService<StepStatements>().Add(log.Texts);
            }
        }
    }
}
