using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The timeline of entries (section 4.4, ADR-0038): events are added, stamped with the clock, read per entry and per
/// change, and go only with their entry.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EntryEventRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task Events_AreAdded_AndListedOldestFirst()
    {
        var entry = await AddEntryAsync();
        var change = Guid.NewGuid();

        var submitted = await EntryEvents.AddAsync(new NewEntryEvent(entry, "submitted", Details: """{"source":"api"}"""));
        Clock.Advance(TimeSpan.FromMinutes(5));
        var approved = await EntryEvents.AddAsync(new NewEntryEvent(entry, "stateChanged", Editor, change, """{"from":"Submitted","to":"Approved"}"""));
        var mailed = await EntryEvents.AddAsync(new NewEntryEvent(entry, "action.email-sent"));

        Assert.Multiple(async () =>
        {
            Assert.That(submitted, Is.EqualTo(new StoredEntryEvent(submitted!.Id, entry, Now, "submitted", null, null, """{"source":"api"}""")));
            Assert.That(approved, Is.EqualTo(new StoredEntryEvent(approved!.Id, entry, Now.AddMinutes(5), "stateChanged", Editor, change, """{"from":"Submitted","to":"Approved"}""")));
            Assert.That(mailed?.Details, Is.EqualTo("{}"));
            Assert.That(await EntryEvents.ListForEntryAsync(entry), Is.EqualTo(new[] { submitted, approved, mailed }), "by time, then in the order they were added");
            Assert.That(await EntryEvents.ListForEntryAsync(entry + 1), Is.Empty);
        });
    }

    [Test]
    public async Task ATimeline_GoesByWhenThingsHappened_NotByWhenTheyWereStored()
    {
        var entry = await AddEntryAsync();
        var first = await EntryEvents.AddAsync(new NewEntryEvent(entry, "submitted"));
        var late = (await EntryEvents.AddAsync(new NewEntryEvent(entry, "action.email-sent")))!;

        // A server whose clock is behind stored an event that happened before the others, after them.
        Write(db => db.Execute($"UPDATE {T(FormBuilderTables.EntryEvent)} SET {Q("occurredUtc")} = @0 WHERE {Q("id")} = @1", Now.AddMinutes(-1).UtcDateTime, late.Id));

        Assert.That((await EntryEvents.ListForEntryAsync(entry)).Select(entryEvent => entryEvent.Id), Is.EqualTo(new[] { late.Id, first!.Id }));
    }

    [Test]
    public async Task AnEventOfAMissingEntry_IsNull()
    {
        var entry = await AddEntryAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await EntryEvents.AddAsync(new NewEntryEvent(entry + 1, "submitted")), Is.Null);
            Assert.That(Count(FormBuilderTables.EntryEvent), Is.Zero);
        });
    }

    [Test]
    public async Task TheEventsOfOneChange_AreFoundTogether_AcrossEntries()
    {
        var first = await AddEntryAsync();
        var second = await AddEntryAsync();
        var bulk = Guid.NewGuid();

        var a = await EntryEvents.AddAsync(new NewEntryEvent(first, "stateChanged", Editor, bulk));
        await EntryEvents.AddAsync(new NewEntryEvent(first, "stateChanged", Editor, Guid.NewGuid()));
        var b = await EntryEvents.AddAsync(new NewEntryEvent(second, "stateChanged", Editor, bulk));
        await EntryEvents.AddAsync(new NewEntryEvent(second, "noteAdded", Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(await EntryEvents.ListForChangeAsync(bulk), Is.EqualTo(new[] { a, b }));
            Assert.That(await EntryEvents.ListForChangeAsync(Guid.NewGuid()), Is.Empty);
        });
    }

    [Test]
    public async Task Events_GoWithTheirEntry_Only()
    {
        var gone = await AddEntryAsync();
        var kept = await AddEntryAsync();
        await EntryEvents.AddAsync(new NewEntryEvent(gone, "submitted"));
        var stays = await EntryEvents.AddAsync(new NewEntryEvent(kept, "submitted"));

        await Entries.DeleteAsync(gone);

        Assert.Multiple(async () =>
        {
            Assert.That(await EntryEvents.ListForEntryAsync(gone), Is.Empty);
            Assert.That(await EntryEvents.ListForEntryAsync(kept), Is.EqualTo(new[] { stays }));
        });
    }

    private async Task<long> AddEntryAsync()
    {
        var form = await CreateFormAsync();
        return await AddEntryAsync(form.Id, form.DraftVersionId!.Value);
    }
}
