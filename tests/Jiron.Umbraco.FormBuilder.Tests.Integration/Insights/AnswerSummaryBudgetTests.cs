using System.Diagnostics;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Insights;

/// <summary>
/// The answer summaries' budget (ROADMAP 11.5b, ADR-0212): the summaries of a form with 100,000 entries, an option, two
/// choices of several, a number, a scale from 0 to 10, a paragraph and a checkbox each, load in under a second on SQLite,
/// for every entry and for those a filter holds. On SQL Server, which runs only on the workflow's hosted runner, the
/// budget is that runner's (ADR-0264); the medians are written to the test's output, which the run's log shows.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AnswerSummaryBudgetTests : ActionTestBase
{
    private const int Count = 100_000;
    private static readonly TimeSpan SqliteBudget = TimeSpan.FromSeconds(1);

    // SQL Server in a container on a hosted runner, beside the tests: 3.95 s, 4.02 s and 5.28 s measured (ADR-0264).
    private static readonly TimeSpan SqlServerBudget = TimeSpan.FromSeconds(12);

    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("c6000000-0000-4000-8000-000000000001"),
        Type = "radio",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
        Options = new FieldOptions { Items = [Item("sales"), Item("support"), Item("delivery"), Item("press")] },
    };

    private static readonly FormField Interests = new()
    {
        Key = Guid.Parse("c6000000-0000-4000-8000-000000000002"),
        Type = "checkboxes",
        Alias = "interests",
        Label = new LocalizedText("Interests"),
        Options = new FieldOptions { Items = [Item("a"), Item("b"), Item("c")] },
    };

    private static readonly FormField Amount = new() { Key = Guid.Parse("c6000000-0000-4000-8000-000000000003"), Type = "number", Alias = "amount", Label = new LocalizedText("Amount") };

    private static readonly FormField Recommend = new() { Key = Guid.Parse("c6000000-0000-4000-8000-000000000004"), Type = "scale", Alias = "recommend", Label = new LocalizedText("Recommend") };

    private static readonly FormField Message = new() { Key = Guid.Parse("c6000000-0000-4000-8000-000000000005"), Type = "textarea", Alias = "message", Label = new LocalizedText("Message") };

    private static readonly FormField Agreed = new() { Key = Guid.Parse("c6000000-0000-4000-8000-000000000006"), Type = "checkbox", Alias = "agreed", Label = new LocalizedText("Agreed") };

    // A time budget: scripts/test-integration.mjs runs the "Timed" tests alone, after the shares (ADR-0261).
    [Test]
    [Category("Timed")]
    public async Task TheSummaries_OfAHundredThousandEntries_LoadWithinTheirBudget()
    {
        var form = await PublishAsync(name: "Budget", fields: [NameField, Topic, Interests, Amount, Recommend, Message, Agreed]);
        var start = Clock.GetUtcNow();
        await SeedAsync(form);
        var span = new InsightSpan(DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(start.UtcDateTime.AddSeconds(Count + 1)));
        var every = new AnswerRequest(span, new EntryListFilter());
        var filtered = new AnswerRequest(span, new EntryListFilter { States = [EntryState.Approved], Fields = [new EntryFieldCondition { Field = Topic.Key, Test = EntryFieldTest.OneOf, Values = ["sales"] }] });
        var summaries = GetRequiredService<AnswerSummaries>();
        var sqlite = await GetRequiredService<FormBuilderDatabase>().ReadAsync(db => Task.FromResult(new SqlNames(db).IsSqlite));
        var budget = sqlite ? SqliteBudget : SqlServerBudget;

        var first = (await summaries.SummarizeAsync(form, every))!;
        var times = new Dictionary<string, List<double>> { ["every entry"] = [], ["approved about sales"] = [] };
        for (var round = 0; round < 5; round++)
        {
            foreach (var (name, request) in new[] { ("every entry", every), ("approved about sales", filtered) })
            {
                var watch = Stopwatch.StartNew();
                _ = await summaries.SummarizeAsync(form, request);
                times[name].Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        foreach (var (name, measured) in times)
        {
            // scripts/test-integration.mjs prints the timed tests' output to the run's log, where a hosted runner's times are read (ADR-0264).
            var line = $"answer summaries, {(sqlite ? "SQLite" : "SQL Server")}, {name}: median {Median(measured).ToString("0.0", CultureInfo.InvariantCulture)} ms of {string.Join(", ", measured.Select(time => time.ToString("0.0", CultureInfo.InvariantCulture)))} (budget {budget.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms)";
            await TestContext.Out.WriteLineAsync(line);
        }

        AnswerSummary Of(FormField field) => first.Fields.Single(summary => summary.Key == field.Key);
        Assert.Multiple(() =>
        {
            Assert.That(first.Entries, Is.EqualTo(Count));
            Assert.That(Of(Topic).Options.Select(line => line.Count), Is.EqualTo(new[] { 25_000L, 25_000L, 25_000L, 25_000L }));
            Assert.That(Of(Interests).Options.Select(line => line.Count), Is.EqualTo(new[] { (long)Count, Count / 3L, 0L }));
            Assert.That(Of(Amount).Numbers!.Highest, Is.EqualTo(999));
            Assert.That(Of(Recommend).Numbers!.Groups, Is.Not.Null);
            Assert.That(Of(Message).Texts, Has.Count.EqualTo(AnswerSummaries.TextsShown));
            Assert.That(Of(Agreed).Ticked, Is.EqualTo(Count / 2));
            foreach (var (name, measured) in times)
            {
                Assert.That(TimeSpan.FromMilliseconds(Median(measured)), Is.LessThanOrEqualTo(budget), name);
            }
        });
    }

    private static FieldOption Item(string value) => new() { Value = value, Label = new LocalizedText(value) };

    private static double Median(List<double> times) => times.Order().ElementAt(times.Count / 2);

    // The entries as a submission stores them, a second apart, a tenth of them approved: each with its topic, its
    // interests, an amount, a score, a message in every tenth, and the checkbox in every other.
    private async Task SeedAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        var catalogue = (await FormRows.ListFieldsAsync(row.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        string[] topics = ["sales", "support", "delivery", "press"];
        var start = Clock.GetUtcNow().UtcDateTime;
        await GetRequiredService<FormBuilderDatabase>().WriteAsync(
            async db =>
            {
                for (var i = 1; i <= Count; i++)
                {
                    var at = start.AddSeconds(i);
                    var entry = new EntryDto
                    {
                        Key = Guid.NewGuid(),
                        FormId = row.Id,
                        FormVersionId = row.PublishedVersionId!.Value,
                        Number = i,
                        State = StoredNames.Of(i % 10 == 0 ? EntryState.Approved : EntryState.Submitted),
                        SubmissionId = Guid.NewGuid(),
                        SubmissionHash = new string('a', 64),
                        Culture = "en-US",
                        SubmittedUtc = at,
                        UpdatedUtc = at,
                        StateChangedUtc = at,
                    };
                    await db.InsertAsync(entry);
                    var topic = topics[i % 4];
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Topic.Key], Text = topic, TextKey = EntryTextKey.For(topic) });
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Interests.Key], Text = "a", TextKey = "a" });
                    if (i % 3 == 0)
                    {
                        await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Interests.Key], Text = "b", TextKey = "b", ValueIndex = 1 });
                    }

                    var amount = i % 1000;
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Amount.Key], Text = amount.ToString(CultureInfo.InvariantCulture), Number = amount });
                    var score = i % 11;
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Recommend.Key], Text = score.ToString(CultureInfo.InvariantCulture), Number = score });
                    if (i % 10 == 0)
                    {
                        var message = $"Message {i}";
                        await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Message.Key], Text = message, TextKey = EntryTextKey.For(message) });
                    }

                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = catalogue[Agreed.Key], Text = "true", Bool = i % 2 == 0 });
                }

                var n = new SqlNames(db);
                await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.Form)} SET {n.C("lastEntryNumber")} = @0 WHERE {n.C("id")} = @1", Count, row.Id);
                return true;
            });
        Clock.Advance(TimeSpan.FromSeconds(Count + 1));
    }
}
