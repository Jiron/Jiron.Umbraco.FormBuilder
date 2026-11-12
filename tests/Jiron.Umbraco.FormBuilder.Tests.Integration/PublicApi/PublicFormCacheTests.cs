using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The published forms the website serves, kept per server until they change (ADR-0044, ADR-0084): every change of the
/// form service and the folder service drops the form here and tells the other servers, another server's instruction
/// drops it here, and so does a refresh of everything; misses that come together share one read, a key or alias that
/// names no form is not kept, and a published version this server cannot read is answered as missing, logged once.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormCacheTests : PublicApiTestBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private RecordingServerMessenger Messenger => GetRequiredService<RecordingServerMessenger>();

    private IFormBuilderFolderService Folders => GetRequiredService<IFormBuilderFolderService>();

    [Test]
    public async Task EveryChangeOfTheForm_DropsItHere_AndTellsTheOtherServers()
    {
        var key = await PublishAsync();
        var sent = Change(key);
        var answers = new List<string> { await AnswerAsync("published", "contactUs") };

        await ChangeAsync("published again", "contactUs", () => SaveAndPublishAsync(key, definition => WithFields(definition, NameField with { Label = new LocalizedText("Full name") })));
        await ChangeAsync("renamed", "contact", async () => await Forms.RenameAsync(key, await VersionAsync(key), "Contact", "contact", Editor));
        answers.Add(await AnswerAsync("by its old alias", "contactUs"));
        await ChangeAsync("unpublished", "contact", async () => await Forms.UnpublishAsync(key, await VersionAsync(key), Editor));
        await ChangeAsync("published once more", "contact", async () => await Forms.PublishAsync(key, await VersionAsync(key), null, Editor));
        await ChangeAsync("trashed", "contact", async () => await Forms.TrashAsync(key, await VersionAsync(key), Editor));
        await ChangeAsync("restored", "contact", async () => await Forms.RestoreAsync(key, await VersionAsync(key), Editor));
        await Forms.TrashAsync(key, await VersionAsync(key), Editor);
        await ChangeAsync("deleted", "contact", async () => await Forms.DeleteAsync(key, await VersionAsync(key), Editor));

        Assert.That(answers, Is.EqualTo(new[]
        {
            "published: 200 Name",
            $"published again: 200 Full name, sent {sent}",
            $"renamed: 200 Full name, sent {sent}",
            "by its old alias: 404",
            $"unpublished: 404, sent {sent}",
            $"published once more: 200 Full name, sent {sent}",
            $"trashed: 404, sent {sent}",
            $"restored: 200 Full name, sent {sent}",
            $"deleted: 404, sent {sent}",
        }));

        async Task ChangeAsync(string what, string alias, Func<Task> change)
        {
            var before = Messenger.Refreshes.Count;
            await change();
            var instructions = Messenger.Refreshes.Skip(before).Where(refresh => refresh.Refresher == FormBuilderFormCacheRefresher.UniqueId).Select(refresh => refresh.Payload);
            answers.Add($"{await AnswerAsync(what, alias)}, sent {string.Join(" | ", instructions)}");
        }
    }

    [Test]
    public async Task AFolderTrashOrRestore_DropsEveryForm_AndItsDeleteEachFormItRemoves()
    {
        var folder = await Folders.CreateAsync(new NewFolderRequest("Archive", UserKey: Editor));
        var key = (await Forms.CreateAsync(new NewFormRequest("Inside", FolderKey: folder.Key, UserKey: Editor))).Key;
        await SaveAndPublishAsync(key, definition => Headless(WithFields(definition, NameField)));
        var answers = new List<string> { await AnswerAsync("published", "inside") };

        await ChangeAsync("folder trashed", () => Folders.TrashAsync(folder.Key, Editor));
        await ChangeAsync("folder restored", () => Folders.RestoreAsync(folder.Key, Editor));
        await Folders.TrashAsync(folder.Key, Editor);
        await ChangeAsync("folder deleted", () => Folders.DeleteAsync(folder.Key, Editor));

        Assert.That(answers, Is.EqualTo(new[]
        {
            "published: 200 Name",
            $"folder trashed: 404, sent {Change(Guid.Empty)}",
            $"folder restored: 200 Name, sent {Change(Guid.Empty)}",
            $"folder deleted: 404, sent {Change(key)}",
        }));

        async Task ChangeAsync(string what, Func<Task> change)
        {
            var before = Messenger.Refreshes.Count;
            await change();
            var instructions = Messenger.Refreshes.Skip(before).Where(refresh => refresh.Refresher == FormBuilderFormCacheRefresher.UniqueId).Select(refresh => refresh.Payload);
            answers.Add($"{await AnswerAsync(what, "inside")}, sent {string.Join(" | ", instructions)}");
        }
    }

    [Test]
    public async Task AnotherServersChange_ReachesThisOne_OnlyThroughItsInstruction()
    {
        var first = await PublishAsync("First");
        var second = await PublishAsync("Second");
        var answers = new List<string> { await AnswerAsync("first, published", "first"), await AnswerAsync("second, published", "second") };

        // Another server unpublishes both; only its cache instructions reach this one, as JSON.
        var repository = GetRequiredService<IFormRepository>();
        foreach (var key in new[] { first, second })
        {
            var stored = (await repository.GetAsync(key))!;
            _ = await repository.UnpublishAsync(stored.Id, stored.ConcurrencyVersion, Editor);
        }

        answers.Add(await AnswerAsync("first, unpublished elsewhere", "first"));
        var refresher = (IJsonCacheRefresher)GetRequiredService<CacheRefresherCollection>()[FormBuilderFormCacheRefresher.UniqueId]!;
        var json = GetRequiredService<IJsonSerializer>();
        refresher.Refresh(json.Serialize(new[] { new FormCacheChange(first) }));
        answers.Add(await AnswerAsync("first, told", "first"));
        answers.Add(await AnswerAsync("second, not yet told", "second"));
        refresher.Refresh(json.Serialize(new[] { new FormCacheChange(Guid.Empty) }));
        answers.Add(await AnswerAsync("second, told of every form", "second"));

        Assert.That(answers, Is.EqualTo(new[]
        {
            "first, published: 200 Name",
            "second, published: 200 Name",
            "first, unpublished elsewhere: 200 Name",
            "first, told: 404",
            "second, not yet told: 200 Name",
            "second, told of every form: 404",
        }));
    }

    [Test]
    public async Task ARefreshOfEverything_DropsEveryForm()
    {
        var key = await PublishAsync("First");
        var answers = new List<string> { await AnswerAsync("published", "first") };

        // Another server unpublishes it, and Umbraco then refreshes every cache, as after a rebuild.
        var repository = GetRequiredService<IFormRepository>();
        var stored = (await repository.GetAsync(key))!;
        _ = await repository.UnpublishAsync(stored.Id, stored.ConcurrencyVersion, Editor);
        answers.Add(await AnswerAsync("unpublished elsewhere", "first"));
        var refresher = GetRequiredService<CacheRefresherCollection>()[FormBuilderFormCacheRefresher.UniqueId]!;
        refresher.RefreshAll();
        answers.Add(await AnswerAsync("everything refreshed", "first"));

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new[] { "published: 200 Name", "unpublished elsewhere: 200 Name", "everything refreshed: 404" }));
            Assert.That(refresher.Name, Is.EqualTo("Jiron Form Builder forms"), "as Umbraco lists it");
        });
    }

    [Test]
    public async Task APublishedVersionThisServerCannotRead_IsAnsweredAsMissing_AndLoggedOncePerLoad()
    {
        var key = await PublishAsync("Broken");

        // A version another server wrote, which this one cannot read.
        var repository = GetRequiredService<IFormRepository>();
        var stored = (await repository.GetAsync(key))!;
        var unreadable = new VersionDocument(FormDefinition.CurrentSchemaVersion, """{"schemaVersion":1,"pages":"none"}""", new string('0', 64), new string('1', 64));
        _ = await repository.PublishAsync(stored.Id, stored.ConcurrencyVersion, unreadable, null, null, Editor);
        var answers = new List<string>();
        for (var time = 0; time < 2; time++)
        {
            using var response = await GetAsync($"{Root}/forms/{key}");
            answers.Add($"{await RefusalAsync(response)} after {Watched.PublishedReads} read(s)");
        }

        // A post reads the row, which says the form is published, but not the version again (16.5's review): it would read the same.
        for (var time = 0; time < 2; time++)
        {
            using var post = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent("""{"revision":1,"values":{"name":"Grace"}}""", System.Text.Encoding.UTF8, "application/json"));
            answers.Add($"post: {await RefusalAsync(post)} after {Watched.PublishedReads} read(s)");
        }

        // The pipeline's own read of the form, as a post without JavaScript takes it, holds the same.
        var submissions = GetRequiredService<Jiron.Umbraco.FormBuilder.Core.Submissions.FormSubmissions>();
        for (var time = 0; time < 2; time++)
        {
            var receipt = await submissions.ReceiptAsync(key, Guid.NewGuid(), member: null);
            answers.Add($"pipeline: {receipt.Refusal} after {Watched.PublishedReads} read(s)");
        }

        var logged = Logged();
        await SaveAndPublishAsync(key, definition => WithFields(definition, NameField with { Label = new LocalizedText("Full name") }));
        answers.Add(await AnswerAsync("published again", "broken"));

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new[] { "404 form_not_found after 1 read(s)", "404 form_not_found after 1 read(s)", "post: 404 form_not_found after 1 read(s)", "post: 404 form_not_found after 1 read(s)", "pipeline: FormNotFound after 1 read(s)", "pipeline: FormNotFound after 1 read(s)", "published again: 200 Full name" }), "kept as not served until it changes");
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error }), "once per load");
            Assert.That(logged.FirstOrDefault()?.Message, Does.StartWith($"The published version of the form {key} cannot be read, so the website answers it as missing until it is published again. Problems: "));
            Assert.That(Logged(), Has.Count.EqualTo(1), "a version that reads logs nothing");
        });
    }

    [Test]
    public async Task MissesThatComeTogether_ShareOneRead()
    {
        var key = await PublishAsync();
        var gate = new ReadGate();
        Watched.Gate = gate;

        var requests = Enumerable.Range(0, 5).Select(_ => GetAsync($"{Root}/forms/{key}")).ToList();
        await gate.Reached.WaitAsync(Patience);
        gate.Open();
        var responses = await Task.WhenAll(requests).WaitAsync(Patience);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(responses.Select(response => response.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
                Assert.That(responses.Select(response => response.Headers.ETag?.Tag).Distinct().Count(), Is.EqualTo(1));
                Assert.That(Watched.PublishedReads, Is.EqualTo(1));
            });
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task TheCache_KeepsWhatExists_AndNothingOfKeysAndAliasesThatNameNoForm()
    {
        var unpublished = (await Forms.CreateAsync(new NewFormRequest("Draft only", UserKey: Editor))).Key;
        var nothing = Guid.NewGuid();
        foreach (var path in new[] { $"forms/{unpublished}", $"forms/{nothing}", "forms/nothingHere", "forms/draftOnly" })
        {
            for (var time = 0; time < 2; time++)
            {
                using var response = await GetAsync($"{Root}/{path}");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), path);
            }
        }

        var cache = GetRequiredService<AppCaches>().RuntimeCache;
        Assert.Multiple(() =>
        {
            Assert.That(cache.Get(FormCacheKeys.Published(unpublished)), Is.Not.Null, "a form the website does not get is kept as such");
            Assert.That(cache.Get(FormCacheKeys.Alias("draftOnly")), Is.Not.Null);
            Assert.That(Watched.PublishedReads, Is.EqualTo(1), "so it is read once");
            Assert.That(cache.Get(FormCacheKeys.Published(nothing)), Is.Null, "a visitor's guess is not kept");
            Assert.That(cache.Get(FormCacheKeys.Alias("nothingHere")), Is.Null);
            Assert.That(cache.Get(FormCacheKeys.PublishedLoading(nothing)), Is.Null, "nor its load");
            Assert.That(cache.Get(FormCacheKeys.AliasLoading("nothingHere")), Is.Null);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<RecordingServerMessenger>();
        builder.Services.AddUnique<IServerMessenger>(provider => provider.GetRequiredService<RecordingServerMessenger>());
        builder.Services.AddLogCapture();
    }

    // What the published forms logged.
    private List<CapturedLog> Logged() =>
        GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(PublishedFormCache).FullName).ToList();

    // The instruction the cache sends for a form, or for every form.
    private static string Change(Guid key) => new FormCacheChange(key).ToString();

    // What the form endpoint answers for an alias: the status, and the first field's label when it serves the form.
    private async Task<string> AnswerAsync(string what, string alias)
    {
        using var response = await GetAsync($"{Root}/forms/{alias}");
        return Succeeded(response)
            ? $"{what}: 200 {(await JsonAsync(response))["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!["label"]!.GetValue<string>()}"
            : $"{what}: {(int)response.StatusCode}";
    }

    private async Task<int> VersionAsync(Guid key) => (await Forms.GetAsync(key))!.ConcurrencyVersion;
}
