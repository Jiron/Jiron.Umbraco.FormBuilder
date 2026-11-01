using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// Schema v1 and its migration plan (ROADMAP 4.1, ADR-0003, ADR-0033): a fresh install, an upgrade from every earlier
/// state, a rerun, a replay of every step, failing data steps and Umbraco's runner. SQLite here; the <c>sqlserver</c>
/// job in ci.yml runs the same tests against SQL Server.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed partial class MigrationPlanTests : FormBuilderMigrationTestBase
{
    private static readonly string[] Admins = [Constants.Security.AdminGroupAlias];

    [Test]
    public void Plan_HasThePackageNamesAndTheStepsInOrder_AndKeepsTheCurrentState()
    {
        var plan = new FormBuilderMigrationPlan();
        var walked = new List<(string State, Type Step)>();
        for (var state = string.Empty; plan.Transitions.TryGetValue(state, out var transition) && transition is not null; state = transition.TargetState)
        {
            walked.Add((transition.TargetState, transition.MigrationType));
        }

        Assert.Multiple(() =>
        {
            Assert.That(plan.Name, Is.EqualTo("Jiron.Umbraco.FormBuilder"), "the stored state's name never changes");
            Assert.That(plan.Name, Is.EqualTo(FormBuilderConstants.MigrationPlanName));
            Assert.That(plan.PackageId, Is.EqualTo(FormBuilderConstants.PackageId));
            Assert.That(plan.PackageName, Is.EqualTo(FormBuilderConstants.PackageName));
            Assert.That(plan.IgnoreCurrentState, Is.False);
            Assert.That(
                typeof(FormBuilderMigrationPlan).GetProperty(nameof(MigrationPlan.IgnoreCurrentState))!.DeclaringType,
                Is.EqualTo(typeof(FormBuilderMigrationPlan)),
                "the plan states IgnoreCurrentState itself (ADR-0003)");
            Assert.That(walked, Is.EqualTo(Steps), "the plan's transitions are its steps, in order");
            Assert.That(Steps.Select(s => s.Step), Is.EqualTo(new[] { typeof(CreateSchemaV1), typeof(CreateDefaultSettingsV1), typeof(GrantFormsSectionV1), typeof(RequestReferenceRebuildV1), typeof(AddFileScanIndexV1), typeof(ChangeFileScanIndexV1) }));
            Assert.That(plan.FinalState, Is.EqualTo(FinalState));
            Assert.That(new TestPlan().FinalState, Is.EqualTo(plan.FinalState), "the tests' copy follows the plan");
            Assert.That(CreateSchemaV1.Tables.Select(t => t.Name), Is.EqualTo(FormBuilderTables.All), "the step creates every table in order");
        });
    }

    [Test]
    public async Task FreshInstall_RunsEveryStepInOrder_AndCreatesEveryTableKeyIndexAndForeignKey()
    {
        var result = await RunAsync(new FormBuilderMigrationPlan());

        Assert.That(result.Successful, Is.True, result.Exception?.ToString());
        var schema = Inspect();
        Assert.Multiple(() =>
        {
            Assert.That(result.CompletedTransitions.Select(t => t.MigrationType), Is.EqualTo(Steps.Select(s => s.Step)));
            Assert.That(StoredState(), Is.EqualTo(FinalState));
            Assert.That(schema.Columns.Keys, Is.EquivalentTo(ExpectedSchemaV1.Columns.Keys), "tables");
            foreach (var (table, columns) in ExpectedSchemaV1.Columns)
            {
                Assert.That(schema.Columns.GetValueOrDefault(table), Is.EqualTo(columns), $"columns of {table}");
                Assert.That(schema.PrimaryKeys.GetValueOrDefault(table), Is.EqualTo(ExpectedSchemaV1.PrimaryKey(table)), $"primary key of {table}");
            }

            Assert.That(schema.Indexes, Is.EqualTo(ExpectedSchemaV1.Indexes.Append("jfbEntryFile.IX_jfbEntryFile_scanState_entryId_id(scanState,entryId,id)").Order(StringComparer.Ordinal)), "indexes, the filtered ones included");
            Assert.That(schema.ForeignKeys, Is.EqualTo(ExpectedSchemaV1.ForeignKeys), "foreign keys");
        });
    }

    [Test]
    public async Task FreshInstall_EntryAndValueIndexes_AreThoseOfTheDesign_AndValuesKeepTheirFieldWithoutACascade()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        var schema = Inspect();

        Assert.Multiple(() =>
        {
            Assert.That(
                schema.Indexes.Where(index => index.StartsWith("jfbEntry.", StringComparison.Ordinal) || index.StartsWith("jfbEntryValue.", StringComparison.Ordinal)),
                Is.EqualTo(ExpectedSchemaV1.IndexesOf("jfbEntry", "jfbEntryValue")));
            Assert.That(schema.ForeignKeys, Does.Contain("jfbEntryValue.fieldId -> jfbFormField on delete no action"));
            Assert.That(schema.ForeignKeys.Where(key => key.StartsWith("jfbEntryValue.fieldId ", StringComparison.Ordinal)).ToList(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Schema_NoNullableColumn_IsPartOfAPrimaryKeyOrAPlainUniqueIndex()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        var schema = Inspect();

        var offenders = new List<string>();
        foreach (var (table, key) in schema.PrimaryKeys)
        {
            offenders.AddRange(key.Split(',').Where(schema.NullableColumns(table).Contains).Select(column => $"{table} primary key: {column}"));
        }

        var unique = schema.Indexes.Select(ParseIndex).Where(index => index.Unique).ToList();
        foreach (var index in unique.Where(index => !index.Filtered))
        {
            offenders.AddRange(index.Columns.Where(schema.NullableColumns(index.Table).Contains).Select(column => $"{index.Name}: {column}"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty);
            Assert.That(schema.PrimaryKeys.Keys, Is.EquivalentTo(ExpectedSchemaV1.Columns.Keys), "every table has a primary key");

            // The check sees nullable columns: the filtered unique indexes are exactly the ones it would refuse.
            Assert.That(
                unique.Where(index => index.Columns.Any(schema.NullableColumns(index.Table).Contains)).Select(index => index.Name),
                Is.EquivalentTo(new[] { "IX_jfbEntry_formId_challengeId", "IX_jfbEntry_resumeTokenHash" }));
            Assert.That(unique.Where(index => index.Filtered).Select(index => index.Name), Is.EquivalentTo(new[] { "IX_jfbEntry_formId_challengeId", "IX_jfbEntry_resumeTokenHash" }));
        });
    }

    [Test]
    public async Task FreshInstall_WritesTheGlobalRowOfEverySettingsSection_WithNoValues()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        await RunAsync(new FormBuilderMigrationPlan());
        var after = DateTime.UtcNow.AddSeconds(1);

        var rows = Query(db => db.Fetch<CreateDefaultSettingsV1.SettingDto>($"SELECT * FROM {T(FormBuilderTables.Setting)}"));
        Assert.Multiple(() =>
        {
            // The sections of ADR-0008; access lives in jfbPermission.
            Assert.That(rows.Select(row => row.Section), Is.EquivalentTo(new[] { "general", "email", "protection", "files", "privacy", "integrations" }));
            Assert.That(rows.Select(row => row.Scope).Distinct(), Is.EqualTo(new[] { CreateDefaultSettingsV1.GlobalScope }));
            Assert.That(rows.Select(row => (row.SchemaVersion, row.Version, row.Json, row.UpdatedByUserKey)).Distinct(), Is.EqualTo(new[] { (1, 1, "{}", (Guid?)null) }));
            Assert.That(rows.Select(row => row.UpdatedUtc), Has.All.InRange(before, after));
        });
    }

    [Test]
    public async Task FreshInstall_GrantsFormsToTheAdministrators_AndRecordsTheGrant()
    {
        Assert.That(GroupsWithSection(), Is.Empty, "nothing before the plan");

        await RunAsync(new FormBuilderMigrationPlan());

        Assert.Multiple(() =>
        {
            Assert.That(GroupsWithSection(), Is.EqualTo(Admins));
            Assert.That(GrantFlag(), Is.EqualTo(Constants.Security.AdminGroupAlias));
        });
    }

    [Test]
    public async Task FreshInstall_AsksForAReferenceRebuild_AndAReplayLeavesARequestUnderWayAsItIs()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        var asked = RebuildRequest();
        Write(db => db.Execute($"UPDATE {T(FormBuilderTables.KeyValue)} SET {Q("value")} = @0 WHERE {Q("key")} = @1", "1234", RequestReferenceRebuildV1.RebuildKey));

        var replay = await RunAsync(new TestPlan(ignoreCurrentState: true));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(RequestReferenceRebuildV1.FromStart), "a walk from the first document");
            Assert.That(replay.Successful, Is.True, replay.Exception?.ToString());
            Assert.That(RebuildRequest(), Is.EqualTo("1234"), "the walk under way goes on where it was");
        });
    }

    [Test]
    public async Task FreshInstall_WithoutTheAdministratorsGroup_GrantsNothing_AndRecordsThatItDidNot()
    {
        Write(db => db.Execute(
            $"UPDATE {T(Constants.DatabaseSchema.Tables.UserGroup)} SET {Q("userGroupAlias")} = @0 WHERE {Q("userGroupAlias")} = @1",
            "jfbTestRenamedAdmins",
            Constants.Security.AdminGroupAlias));

        var result = await RunAsync(new FormBuilderMigrationPlan());

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True, result.Exception?.ToString());
            Assert.That(StoredState(), Is.EqualTo(FinalState));
            Assert.That(GroupsWithSection(), Is.Empty);
            Assert.That(GrantFlag(), Is.EqualTo(string.Empty), "recorded, so a later rerun does not grant either");
        });
    }

    private static IEnumerable<int> EarlierStates() => Enumerable.Range(1, Steps.Count - 1);

    [TestCaseSource(nameof(EarlierStates))]
    public async Task Upgrade_FromAnEarlierState_RunsOnlyTheRemainingSteps_AndKeepsTheData(int done)
    {
        await RunAsync(new TestPlan(done));
        Write(db => db.Insert(new CreateSchemaV1.KeyValueDto { Key = "jfb-test-kept", Value = "written before the upgrade", UpdatedUtc = DateTime.UtcNow }));

        var upgrade = await RunAsync(new FormBuilderMigrationPlan());

        Assert.Multiple(() =>
        {
            Assert.That(upgrade.Successful, Is.True, upgrade.Exception?.ToString());
            Assert.That(upgrade.InitialState, Is.EqualTo(Steps[done - 1].State));
            Assert.That(upgrade.CompletedTransitions.Select(t => t.MigrationType), Is.EqualTo(Steps.Skip(done).Select(s => s.Step)));
            Assert.That(StoredState(), Is.EqualTo(FinalState));
            Assert.That(Count(FormBuilderTables.KeyValue, $"{Q("key")} = @0", "jfb-test-kept"), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count));
            Assert.That(GroupsWithSection(), Is.EqualTo(Admins));
            Assert.That(Inspect().Indexes, Has.One.Contains(ChangeFileScanIndexV1.IndexName).And.None.Contains("IX_jfbEntryFile_scanState_id("), "the old index of the scans gives way");
        });
    }

    [Test]
    public async Task Rerun_AtTheFinalState_IsANoOp()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        var before = Inspect();

        var again = await RunAsync(new FormBuilderMigrationPlan());

        Assert.Multiple(() =>
        {
            Assert.That(again.Successful, Is.True, again.Exception?.ToString());
            Assert.That(again.CompletedTransitions, Is.Empty);
            Assert.That(again.FinalState, Is.EqualTo(FinalState));
            Assert.That(Inspect(), Is.EqualTo(before).Using<SchemaReport>(SameSchema));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count));
        });
    }

    [Test]
    public async Task Replay_OfEveryStep_ChangesNothing_BecauseEveryStepIsIdempotent()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        var before = Inspect();

        var replay = await RunAsync(new TestPlan(ignoreCurrentState: true));

        Assert.Multiple(() =>
        {
            Assert.That(replay.Successful, Is.True, replay.Exception?.ToString());
            Assert.That(replay.CompletedTransitions, Has.Count.EqualTo(Steps.Count), "every step ran again");
            Assert.That(Inspect(), Is.EqualTo(before).Using<SchemaReport>(SameSchema));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count));
            Assert.That(Count(FormBuilderTables.KeyValue), Is.EqualTo(2), "one grant flag and one rebuild request");
            Assert.That(GroupsWithSection(), Is.EqualTo(Admins));
        });
    }

    [Test]
    public async Task SettingsRow_ThatASiteChanged_SurvivesAReplay()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        Write(db => db.Execute(
            $"UPDATE {T(FormBuilderTables.Setting)} SET {Q("json")} = @0, {Q("version")} = @1 WHERE {Q("section")} = @2",
            "{\"siteName\":\"Ours\"}",
            2,
            "general"));

        var replay = await RunAsync(new TestPlan(ignoreCurrentState: true));

        Assert.Multiple(() =>
        {
            Assert.That(replay.Successful, Is.True, replay.Exception?.ToString());
            Assert.That(Count(FormBuilderTables.Setting, $"{Q("section")} = @0 AND {Q("version")} = @1 AND {Q("json")} = @2", "general", 2, "{\"siteName\":\"Ours\"}"), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count));
        });
    }

    [Test]
    public async Task SectionGrant_RemovedByAnAdministrator_IsNotGrantedAgainByAReplay()
    {
        await RunAsync(new FormBuilderMigrationPlan());
        Write(db => db.Execute($"DELETE FROM {T(Constants.DatabaseSchema.Tables.UserGroup2App)} WHERE {Q("app")} = @0", FormBuilderConstants.SectionAlias));

        var replay = await RunAsync(new TestPlan(ignoreCurrentState: true));

        Assert.That(replay.Successful, Is.True, replay.Exception?.ToString());
        Assert.That(GroupsWithSection(), Is.Empty);
    }

    [TestCase(2)]
    [TestCase(3)]
    public async Task FailingDataStep_RollsBackItsChanges_KeepsThePreviousState_AndCanRunAgain(int step)
    {
        await RunAsync(new TestPlan(step - 1));
        var failing = step == 2 ? typeof(Failing<CreateDefaultSettingsV1>) : typeof(Failing<GrantFormsSectionV1>);

        var failed = await RunAsync(new TestPlan(step, replaceLastStep: failing));

        Assert.Multiple(() =>
        {
            Assert.That(failed.Successful, Is.False);
            Assert.That(failed.Exception?.ToString(), Does.Contain("JFB_TEST: simulated failure"));
            Assert.That(StoredState(), Is.EqualTo(Steps[step - 2].State));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(step == 2 ? 0 : CreateDefaultSettingsV1.Sections.Count), "the settings step rolled back");
            Assert.That(GroupsWithSection(), Is.Empty, "the grant rolled back");
            Assert.That(Count(FormBuilderTables.KeyValue), Is.Zero, "the grant flag rolled back");
        });

        var fixedRun = await RunAsync(new FormBuilderMigrationPlan());

        Assert.Multiple(() =>
        {
            Assert.That(fixedRun.Successful, Is.True, fixedRun.Exception?.ToString());
            Assert.That(StoredState(), Is.EqualTo(FinalState));
            Assert.That(Count(FormBuilderTables.Setting), Is.EqualTo(CreateDefaultSettingsV1.Sections.Count));
            Assert.That(GroupsWithSection(), Is.EqualTo(Admins));
        });
    }

    [Test]
    public async Task PackagePlan_RunsThroughUmbracosRunner_AndTheStateAndTheUserGroupCacheFollow()
    {
        var plan = GetRequiredService<PackageMigrationPlanCollection>().Single(p => p.Name == FormBuilderConstants.MigrationPlanName);
        var state = GetRequiredService<IFormBuilderMigrationState>();
        var groups = GetRequiredService<IUserGroupService>();
        var pendingBefore = await state.IsPendingAsync();
        var sectionsBefore = (await groups.GetAsync(Constants.Security.AdminGroupAlias))!.AllowedSections.ToList();

        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);

        var pendingAfter = await state.IsPendingAsync();
        var sectionsAfter = (await groups.GetAsync(Constants.Security.AdminGroupAlias))!.AllowedSections.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(plan, Is.InstanceOf<FormBuilderMigrationPlan>());
            Assert.That(pendingBefore, Is.True, "pending before the run: unattended package migrations are off here");
            Assert.That(results.Single().Successful, Is.True);
            Assert.That(pendingAfter, Is.False, "current once the runner finished");
            Assert.That(sectionsBefore, Does.Not.Contain(FormBuilderConstants.SectionAlias));
            Assert.That(sectionsAfter, Does.Contain(FormBuilderConstants.SectionAlias), "the cached group was refreshed");
        });
    }

    private string? RebuildRequest() =>
        Query(db => db.ExecuteScalar<string?>($"SELECT {Q("value")} FROM {T(FormBuilderTables.KeyValue)} WHERE {Q("key")} = @0", RequestReferenceRebuildV1.RebuildKey));

    private string? GrantFlag() =>
        Query(db => db.ExecuteScalar<string?>($"SELECT {Q("value")} FROM {T(FormBuilderTables.KeyValue)} WHERE {Q("key")} = @0", GrantFormsSectionV1.GrantedFlag));

    private static bool SameSchema(SchemaReport a, SchemaReport b) =>
        a.Columns.OrderBy(c => c.Key, StringComparer.Ordinal).SequenceEqual(b.Columns.OrderBy(c => c.Key, StringComparer.Ordinal))
        && a.PrimaryKeys.OrderBy(k => k.Key, StringComparer.Ordinal).SequenceEqual(b.PrimaryKeys.OrderBy(k => k.Key, StringComparer.Ordinal))
        && a.Indexes.SequenceEqual(b.Indexes)
        && a.ForeignKeys.SequenceEqual(b.ForeignKeys);

    private static ParsedIndex ParseIndex(string index)
    {
        var match = IndexPattern().Match(index);
        Assert.That(match.Success, Is.True, $"an index as the inspector writes it: {index}");
        return new ParsedIndex(
            match.Groups["table"].Value,
            match.Groups["name"].Value,
            match.Groups["columns"].Value.Split(','),
            match.Groups["unique"].Success,
            match.Groups["where"].Success);
    }

    [GeneratedRegex(@"^(?<table>\w+)\.(?<name>\w+)\((?<columns>[\w,]+)\)(?<unique> unique)?(?<where> where .+)?$")]
    private static partial Regex IndexPattern();

    private sealed record ParsedIndex(string Table, string Name, IReadOnlyList<string> Columns, bool Unique, bool Filtered);
}
