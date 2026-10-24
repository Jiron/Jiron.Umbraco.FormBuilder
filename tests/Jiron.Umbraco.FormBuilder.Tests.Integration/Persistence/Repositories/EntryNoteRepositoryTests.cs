using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>The internal notes on entries (section 4.4, ADR-0038): added, read, changed and deleted, per entry.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EntryNoteRepositoryTests : RepositoryTestBase
{
    private static readonly Guid Colleague = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c002");

    [Test]
    public async Task ANote_IsAdded_Read_Changed_AndDeleted()
    {
        var entry = await AddEntryAsync();

        var added = await EntryNotes.AddAsync(entry, Editor, "Called back, waiting for the documents.");
        Clock.Advance(TimeSpan.FromMinutes(5));
        var changed = await EntryNotes.UpdateAsync(added!.Id, "Documents received.");

        Assert.Multiple(async () =>
        {
            Assert.That(added, Is.EqualTo(new StoredEntryNote(added.Id, entry, Editor, "Called back, waiting for the documents.", Now, Now)));
            Assert.That(changed, Is.EqualTo(added with { Body = "Documents received.", UpdatedUtc = Now.AddMinutes(5) }), "its author and creation stay");
            Assert.That(await EntryNotes.GetAsync(added.Id), Is.EqualTo(changed));
            Assert.That(await EntryNotes.DeleteAsync(added.Id), Is.True);
            Assert.That(await EntryNotes.DeleteAsync(added.Id), Is.False);
            Assert.That(await EntryNotes.GetAsync(added.Id), Is.Null);
        });
    }

    [Test]
    public async Task ANoteOfAMissingEntry_IsNull_AndAMissingNoteIsNotChanged()
    {
        var entry = await AddEntryAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await EntryNotes.AddAsync(entry + 1, Editor, "Nobody reads this."), Is.Null);
            Assert.That(await EntryNotes.UpdateAsync(1, "Nobody reads this either."), Is.Null);
            Assert.That(Count(FormBuilderTables.EntryNote), Is.Zero);
        });
    }

    [Test]
    public async Task Notes_AreListedOldestFirst_PerEntry()
    {
        var entry = await AddEntryAsync();
        var other = await AddEntryAsync();
        var first = await EntryNotes.AddAsync(entry, Editor, "First.");
        await EntryNotes.AddAsync(other, Editor, "Elsewhere.");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await EntryNotes.AddAsync(entry, Colleague, "Second.");
        var third = await EntryNotes.AddAsync(entry, Editor, "Third, at the same moment.");
        Clock.Advance(TimeSpan.FromMinutes(1));
        await EntryNotes.UpdateAsync(first!.Id, "First, edited later.");

        Assert.Multiple(async () =>
        {
            Assert.That((await EntryNotes.ListAsync(entry)).Select(note => note.Id), Is.EqualTo(new[] { first.Id, second!.Id, third!.Id }), "by when they were written");
            Assert.That(await EntryNotes.ListAsync(entry + other + 1), Is.Empty);
        });
    }

    [Test]
    public async Task Notes_GoByWhenTheyWereWritten_NotByWhenTheyWereStored()
    {
        var entry = await AddEntryAsync();
        var first = await EntryNotes.AddAsync(entry, Editor, "First.");
        var late = (await EntryNotes.AddAsync(entry, Colleague, "Written before, stored after."))!;

        // A server whose clock is behind stored a note written before the others, after them.
        Write(db => db.Execute($"UPDATE {T(FormBuilderTables.EntryNote)} SET {Q("createdUtc")} = @0 WHERE {Q("id")} = @1", Now.AddMinutes(-1).UtcDateTime, late.Id));

        Assert.That((await EntryNotes.ListAsync(entry)).Select(note => note.Id), Is.EqualTo(new[] { late.Id, first!.Id }));
    }

    private async Task<long> AddEntryAsync()
    {
        var form = await CreateFormAsync();
        return await AddEntryAsync(form.Id, form.DraftVersionId!.Value);
    }
}
