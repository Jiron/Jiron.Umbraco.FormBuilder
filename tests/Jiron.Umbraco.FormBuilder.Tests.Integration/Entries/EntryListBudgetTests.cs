using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>
/// The list's budget (section 4.12, ADR-0207): the first page of a form with 100,000 entries, with its count, its titles
/// and two columns, loads through the list service in 300 ms at most on SQLite.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryListBudgetTests : ActionTestBase
{
    private const int Count = 100_000;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    private static readonly FormField Amount = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000201"),
        Type = "number",
        Alias = "amount",
        Label = new LocalizedText("Amount"),
    };

    // A time budget: scripts/test-integration.mjs runs the "Timed" tests alone, after the shares (ADR-0261).
    [Test]
    [Category("Timed")]
    public async Task TheFirstPage_OfAHundredThousandEntries_LoadsWithinItsBudget()
    {
        var form = await PublishAsync(
            name: "Budget",
            change: definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { EntryTitle = "{{ field.name }}" } } },
            fields: [NameField, Amount]);
        await SeedAsync(form);
        var lists = GetRequiredService<EntryLists>();
        var newest = new EntryListRequest(new EntryListFilter()) { Columns = [NameField.Key, Amount.Key], Count = true };
        var approved = newest with { Filter = new EntryListFilter { States = [EntryState.Approved], Sort = new EntryListSort { Field = Amount.Key } } };
        var searched = newest with { Filter = new EntryListFilter { Search = "person 4" } };

        var first = await lists.ListAsync(form, newest);
        var times = new Dictionary<string, List<double>> { ["newest"] = [], ["approved by amount"] = [], ["search"] = [] };
        for (var round = 0; round < 5; round++)
        {
            foreach (var (name, request) in new[] { ("newest", newest), ("approved by amount", approved), ("search", searched) })
            {
                var watch = Stopwatch.StartNew();
                var page = await lists.ListAsync(form, request);
                times[name].Add(watch.Elapsed.TotalMilliseconds);
                Assert.That(page!.Rows, Has.Count.EqualTo(EntryLists.PageSize));
            }
        }

        foreach (var (name, measured) in times)
        {
            await TestContext.Out.WriteLineAsync($"{name}: median {Median(measured):0.0} ms of {string.Join(", ", measured.Select(time => time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(first!.Count, Is.EqualTo(new EntryCount(Count, More: false)));
            Assert.That(first.Rows[0].Title, Is.EqualTo($"Person {Count}"), "the newest, named by its title");
            Assert.That(first.Rows[0].Cells.Select(cell => cell.Texts.Single()), Is.EqualTo(new[] { $"Person {Count}", (Count % 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            foreach (var (name, measured) in times)
            {
                Assert.That(TimeSpan.FromMilliseconds(Median(measured)), Is.LessThanOrEqualTo(Budget), name);
            }
        });
    }

    private static double Median(List<double> times) => times.Order().ElementAt(times.Count / 2);

    // The entries as a submission stores them, a second apart, a tenth of them approved: each with its name and amount.
    private async Task SeedAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        var catalogue = await FormRows.ListFieldsAsync(row.Id);
        var name = catalogue.Single(field => field.Values.FieldKey == NameField.Key).Id;
        var amount = catalogue.Single(field => field.Values.FieldKey == Amount.Key).Id;
        var start = Clock.GetUtcNow().UtcDateTime;
        await GetRequiredService<FormBuilderDatabase>().WriteAsync(
            async db =>
            {
                for (var i = 1; i <= Count; i++)
                {
                    var at = start.AddSeconds(i);
                    var text = $"Person {i}";
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
                        SearchText = EntrySearchText.For([text]),
                        SubmittedUtc = at,
                        UpdatedUtc = at,
                        StateChangedUtc = at,
                    };
                    await db.InsertAsync(entry);
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = name, Text = text, TextKey = EntryTextKey.For(text) });
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = amount, Text = (i % 1000).ToString(System.Globalization.CultureInfo.InvariantCulture), Number = i % 1000 });
                }

                var n = new SqlNames(db);
                await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.Form)} SET {n.C("lastEntryNumber")} = @0 WHERE {n.C("id")} = @1", Count, row.Id);
                return true;
            });
        Clock.Advance(TimeSpan.FromSeconds(Count + 1));
    }
}
