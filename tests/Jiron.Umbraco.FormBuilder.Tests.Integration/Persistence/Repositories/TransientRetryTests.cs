using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Microsoft.Data.Sqlite;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// A unit of work that the database turns away with a transient error runs again where that is safe (ADR-0036): the
/// failed attempt rolled back, so the next one starts from its first statement, and its writes commit once. Other
/// errors, and units inside other units, are not run again.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class TransientRetryTests : RepositoryTestBase
{
    /// <summary>SQLite's "database is locked", as Microsoft.Data.Sqlite raises it.</summary>
    private static SqliteException Busy() => new("JFB_TEST: database is locked", 5);

    [Test]
    public async Task ALockConflict_RollsTheAttemptBack_AndTheUnitRunsAgain_CommittingOnce()
    {
        var attempts = 0;

        await Database.WriteAsync(async _ =>
        {
            attempts++;
            await KeyValues.SetAsync($"attempt:{attempts}", "written");
            return attempts == 1 ? throw Busy() : true;
        });

        Assert.Multiple(async () =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(await KeyValues.GetAsync("attempt:1"), Is.Null, "the first attempt's write rolled back");
            Assert.That(await KeyValues.GetAsync("attempt:2"), Is.EqualTo("written"));
        });
    }

    [Test]
    public async Task ALockConflictThatLasts_GivesUpAfterThreeAttempts_WithNothingWritten()
    {
        var attempts = 0;

        Assert.That(
            async () => await Database.WriteAsync<bool>(async _ =>
            {
                attempts++;
                await KeyValues.SetAsync($"attempt:{attempts}", "written");
                throw Busy();
            }),
            Throws.InstanceOf<SqliteException>());

        Assert.Multiple(async () =>
        {
            Assert.That(attempts, Is.EqualTo(FormBuilderDatabase.Attempts));
            Assert.That(await KeyValues.ListAsync("attempt:"), Is.Empty);
        });
    }

    [Test]
    public void AnOrdinaryFailure_IsNotRunAgain()
    {
        var attempts = 0;

        Assert.That(
            async () => await Database.WriteAsync<bool>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("JFB_TEST: not a database error");
            }),
            Throws.InvalidOperationException);
        Assert.That(
            async () => await Database.WriteAsync<bool>(_ =>
            {
                attempts++;
                throw new SqliteException("JFB_TEST: constraint", 19);
            }),
            Throws.InstanceOf<SqliteException>(),
            "a constraint violation is no lock conflict");

        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task AUnitInsideAnother_IsNotRunAgainOnItsOwn_TheOutermostRunsAgainWhole()
    {
        var outer = 0;
        var inner = 0;

        await Database.WriteAsync(async _ =>
        {
            outer++;
            await KeyValues.SetAsync($"outer:{outer}", "written");

            // The inner unit shares the outer transaction, so its lock conflict ends the outer attempt, which runs again.
            await Database.WriteAsync(_ =>
            {
                inner++;
                return outer == 1 ? throw Busy() : Task.FromResult(true);
            });
            return true;
        });

        Assert.Multiple(async () =>
        {
            Assert.That(outer, Is.EqualTo(2), "the outermost unit ran again");
            Assert.That(inner, Is.EqualTo(2), "once per outer attempt, never again on its own");
            Assert.That((await KeyValues.ListAsync("outer:")).Select(pair => pair.Key), Is.EqualTo(new[] { "outer:2" }));
        });
    }

    [Test]
    public async Task ARead_RunsAgainAfterALockConflict()
    {
        await KeyValues.SetAsync("read:me", "found");
        var attempts = 0;

        var found = await Database.ReadAsync(async _ =>
        {
            if (++attempts == 1)
            {
                throw Busy();
            }

            return await KeyValues.GetAsync("read:me");
        });

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(found, Is.EqualTo("found"));
        });
    }

    [Test]
    public void ACancelledRequest_IsNotRunAgain()
    {
        using var cancel = new CancellationTokenSource();
        var attempts = 0;

        Assert.That(
            async () => await Database.WriteAsync<bool>(
                async _ =>
                {
                    attempts++;
                    await cancel.CancelAsync();
                    throw Busy();
                },
                cancel.Token),
            Throws.InstanceOf<SqliteException>());
        Assert.That(attempts, Is.EqualTo(1));
    }
}
