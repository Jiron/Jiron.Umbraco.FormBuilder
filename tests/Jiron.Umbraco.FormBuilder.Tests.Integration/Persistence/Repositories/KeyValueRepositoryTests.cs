using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>The one-time flags and small values of <c>jfbKeyValue</c> (section 4.5, ADR-0036).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class KeyValueRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AValue_IsStored_Replaced_AndDeleted()
    {
        await KeyValues.SetAsync("checklist.email", "done");
        var stored = await KeyValues.GetAsync("checklist.email");
        Clock.Advance(TimeSpan.FromMinutes(1));
        await KeyValues.SetAsync("checklist.email", null);
        var replaced = await KeyValues.GetAsync("checklist.email");
        var row = Query(db => db.Single<KeyValueDto>($"SELECT * FROM {T(FormBuilderTables.KeyValue)} WHERE {Q("key")} = @0", "checklist.email"));

        Assert.Multiple(async () =>
        {
            Assert.That(stored, Is.EqualTo("done"));
            Assert.That(replaced, Is.Null, "a null value is stored as null");
            Assert.That(FormBuilderDbTime.Read(row.UpdatedUtc), Is.EqualTo(Now.AddMinutes(1)), "stamped by the clock");
            Assert.That(await KeyValues.DeleteAsync("checklist.email"), Is.True);
            Assert.That(await KeyValues.DeleteAsync("checklist.email"), Is.False, "deleted already");
            Assert.That(await KeyValues.GetAsync("checklist.email"), Is.Null);
        });
    }

    [Test]
    public async Task TryReplaceAndTryDelete_HoldOnlyWhileTheKeyHoldsTheValueRead()
    {
        await KeyValues.SetAsync("walk", "Place");
        var otherCase = await KeyValues.TryReplaceAsync("walk", "place", "next");
        var otherValue = await KeyValues.TryReplaceAsync("walk", "elsewhere", "next");
        var missing = await KeyValues.TryReplaceAsync("none", "Place", "next");
        var kept = await KeyValues.GetAsync("walk");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var replaced = await KeyValues.TryReplaceAsync("walk", "Place", "Next");
        var row = Query(db => db.Single<KeyValueDto>($"SELECT * FROM {T(FormBuilderTables.KeyValue)} WHERE {Q("key")} = @0", "walk"));
        var deleteOther = await KeyValues.TryDeleteAsync("walk", "Place");
        var afterOther = await KeyValues.GetAsync("walk");
        var deleted = await KeyValues.TryDeleteAsync("walk", "Next");

        Assert.Multiple(async () =>
        {
            Assert.That((otherCase, otherValue, missing), Is.EqualTo((false, false, false)), "another case, another value, no key");
            Assert.That(kept, Is.EqualTo("Place"));
            Assert.That(await KeyValues.GetAsync("none"), Is.Null, "a replace adds no key");
            Assert.That(replaced, Is.True);
            Assert.That((row.Value, FormBuilderDbTime.Read(row.UpdatedUtc)), Is.EqualTo(("Next", Now.AddMinutes(1))));
            Assert.That((deleteOther, afterOther), Is.EqualTo((false, "Next")));
            Assert.That(deleted, Is.True);
            Assert.That(await KeyValues.GetAsync("walk"), Is.Null);
        });
    }

    [Test]
    public async Task TryAdd_StoresAKeyOnce_TheFirstWriterWins()
    {
        var first = await KeyValues.TryAddAsync("mirror:forms/contact.json", "hash-1");
        var second = await KeyValues.TryAddAsync("mirror:forms/contact.json", "hash-2");

        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(await KeyValues.GetAsync("mirror:forms/contact.json"), Is.EqualTo("hash-1"));
        });
    }

    [Test]
    public async Task TryAdd_FromManyWritersAtOnce_StoresTheKeyOnce()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => KeyValues.TryAddAsync("setup:once", $"writer {i}"))));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(added => added), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.KeyValue, $"{Q("key")} = @0", "setup:once"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task List_FindsTheKeysWithAPrefix_InKeyOrder_WithItsWildcardsTakenLiterally()
    {
        await KeyValues.SetAsync("mirror:b", "2");
        await KeyValues.SetAsync("mirror:a", "1");
        await KeyValues.SetAsync("mirrors", "not this");
        await KeyValues.SetAsync("a_b:1", "underscore");
        await KeyValues.SetAsync("axb:1", "any character");

        var mirror = await KeyValues.ListAsync("mirror:");
        var underscore = await KeyValues.ListAsync("a_b");

        Assert.Multiple(() =>
        {
            Assert.That(mirror.Select(pair => pair.Key), Is.EqualTo(new[] { "mirror:a", "mirror:b" }));
            Assert.That(mirror.Select(pair => pair.Value), Is.EqualTo(new[] { "1", "2" }));
            Assert.That(underscore.Select(pair => pair.Key), Is.EqualTo(new[] { "a_b:1" }), "_ is no wildcard");
        });
    }

    [Test]
    public async Task DeleteOlder_RemovesTheKeysWithAPrefixStoredBeforeATime_AndLeavesTheRest()
    {
        await KeyValues.TryAddAsync("sent-copy:old", null);
        await KeyValues.SetAsync("sent-copyist", "another prefix");
        await KeyValues.SetAsync("other:old", "another prefix too");
        Clock.Advance(TimeSpan.FromMinutes(31));
        await KeyValues.TryAddAsync("sent-copy:new", null);

        var removed = await KeyValues.DeleteOlderAsync("sent-copy:", Now.AddMinutes(30).UtcDateTime);
        var again = await KeyValues.DeleteOlderAsync("sent-copy:", Now.AddMinutes(30).UtcDateTime);

        Assert.Multiple(async () =>
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(again, Is.Zero, "removed already");
            Assert.That((await KeyValues.ListAsync("sent-copy")).Select(pair => pair.Key), Is.EqualTo(new[] { "sent-copy:new", "sent-copyist" }), "a newer key, and an older one under another prefix, stay");
            Assert.That(await KeyValues.GetAsync("other:old"), Is.EqualTo("another prefix too"));
        });
    }

    [Test]
    public async Task TheMigrationsFlag_IsReadable()
    {
        // The section grant of migration step 3 writes its flag through the step's own frozen row.
        Assert.Multiple(async () =>
        {
            Assert.That(await KeyValues.ListAsync("forms-section"), Has.Some.Matches<KeyValuePair<string, string?>>(pair => pair.Key == GrantFormsSectionV1.GrantedFlag));
            Assert.That(await KeyValues.GetAsync(GrantFormsSectionV1.GrantedFlag), Is.Not.Null);
        });
    }
}
