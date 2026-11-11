using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Privacy;

/// <summary>
/// A member deleted in Umbraco (ROADMAP 14.3, ADR-0231): their entries are anonymised by default, keeping their place
/// but nothing of the member, or deleted where Settings > Privacy says so; an entry a legal hold keeps stays as it is,
/// and another visitor's entry is untouched.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class MemberDeletionTests : RenderingTestBase
{
    private const string Entry = """{"revision":1,"values":{"name":"Ada"}}""";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    [Test]
    public async Task ADeletedMembersEntries_AreAnonymised_ButOneALegalHoldKeeps_AndAnotherVisitorsStayAsTheyAre()
    {
        var (key, member, theirs) = await TwoEntriesOfAMemberAndOneOfAVisitorAsync();
        var held = theirs[1];
        Assert.That(await GetRequiredService<EntryHolds>().HoldAsync(held.Key, DateTimeOffset.UtcNow.AddDays(30), "A complaint is open.", Editor), Is.EqualTo(EntryHoldRefusal.None));

        GetRequiredService<IMemberService>().Delete(GetRequiredService<IMemberService>().GetById(member)!);
        var after = await EntriesAsync(key);
        var values = await Entries.ListValuesAsync([.. after.Select(entry => entry.Id)]);

        Assert.Multiple(() =>
        {
            Assert.That(after, Has.Count.EqualTo(3), "anonymised entries keep their place");
            Assert.That(after.Single(entry => entry.Id == theirs[0].Id).MemberKey, Is.Null, "nothing names the member any more");
            Assert.That(values.Where(value => value.EntryId == theirs[0].Id), Is.Empty, "and no answer is left");
            Assert.That(after.Single(entry => entry.Id == held.Id).MemberKey, Is.EqualTo(member), "a legal hold keeps its entry as it is");
            Assert.That(values.Where(value => value.EntryId == held.Id), Is.Not.Empty);
            Assert.That(values.Where(value => value.EntryId == after.Single(entry => entry.MemberKey is null && entry.Id != theirs[0].Id).Id), Is.Not.Empty, "another visitor's entry is untouched");
        });
    }

    [Test]
    public async Task ADeletedMembersEntries_AreDeleted_WhereSettingsPrivacySaysSo()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<PrivacySettings>();
        await settings.SaveAsync(current.Value with { MemberDeleted = MemberDeletion.Delete }, current.Version, Editor);
        var (key, member, theirs) = await TwoEntriesOfAMemberAndOneOfAVisitorAsync();

        GetRequiredService<IMemberService>().Delete(GetRequiredService<IMemberService>().GetById(member)!);
        var after = await EntriesAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(after.Select(entry => entry.Id), Has.None.AnyOf(theirs.Select(entry => entry.Id).ToArray()), "the member's entries go");
            Assert.That(after, Has.Count.EqualTo(1), "another visitor's stays");
            Assert.That(after[0].MemberKey, Is.Null);
        });
    }

    // A form for anyone with one entry of a visitor, then two of a member signed in; the member's key and entries.
    private async Task<(Guid Form, Guid Member, List<StoredEntry> Theirs)> TwoEntriesOfAMemberAndOneOfAVisitorAsync()
    {
        var key = await PublishFormAsync("forAnyone", null, NameField);
        _ = await PagesAsync("open");
        using (var visitor = await PostAsync(key))
        {
            Assert.That(visitor.StatusCode, Is.EqualTo(HttpStatusCode.OK), await visitor.Content.ReadAsStringAsync());
        }

        await SignInMemberAsync("member@example.com");
        var member = GetRequiredService<IMemberService>().GetByUsername(MemberName)!.Key;
        for (var i = 0; i < 2; i++)
        {
            using var sent = await PostAsync(key);
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
        }

        return (key, member, [.. (await EntriesAsync(key)).Where(entry => entry.MemberKey == member)]);
    }

    private async Task<HttpResponseMessage> PostAsync(Guid key)
    {
        var challenge = await ChallengeAsync(key);
        return await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
            request.Content = new StringContent(Entry.Replace("{\"revision\"", $"{{\"challenge\":\"{challenge}\",\"revision\"", StringComparison.Ordinal), Encoding.UTF8, "application/json"));
    }

    // The form's entries, oldest first.
    private async Task<List<StoredEntry>> EntriesAsync(Guid key)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(key))!;
        var stored = new List<StoredEntry>();
        foreach (var id in (await Entries.ListIdsAsync(row.Id, 10)).Order())
        {
            stored.Add((await Entries.GetByIdAsync(id))!);
        }

        return stored;
    }
}
