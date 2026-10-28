using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The entries endpoint against what the store holds a moment later, or holds another way (ADR-0036, ADR-0088): a
/// submission another request stored a moment earlier is answered as its retry, or refused for other answers; the last
/// place or an upload another entry took a moment earlier is refused inside the unit, storing nothing; a form trashed or
/// gone a moment earlier takes nothing; a retry is answered from the stored entry, with its context, and with the default
/// message for an entry stored without its event; an older revision that cannot be read answers <c>form_changed</c>; and
/// the pipeline called without a token reader takes no upload. The site's entry, form and event repositories are wrapped
/// so a test can answer one call as the store would a moment later or earlier.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormEntryStoreTests : PublicApiTestBase
{
    private static readonly FormOutcome Thanks = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000388"),
        Message = new LocalizedHtml("<p>Thanks from {{ context.campaign }}.</p>"),
    };

    private string _storeRoot = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [TearDown]
    public void DeleteTheStore()
    {
        if (Directory.Exists(_storeRoot))
        {
            Directory.Delete(_storeRoot, recursive: true);
        }
    }

    [Test]
    public async Task ASubmissionStoredAMomentEarlier_IsAnsweredAsItsRetry_AndAsADuplicateForOtherAnswersOrOnceItIsGone()
    {
        var key = await PublishAsync();
        var submissionId = Guid.NewGuid();
        var ada = $$$"""{"submissionId":"{{{submissionId}}}","revision":1,"values":{"name":"Ada"}}""";
        using var first = await PostAsync(key, ada);

        // The first read of the id misses the entry, as a read does while another request stores it; the unit then finds it.
        var misses = 0;
        Hooks<IEntryRepository>().On(nameof(IEntryRepository.GetBySubmissionAsync), (_, call) => misses-- > 0 ? Task.FromResult<StoredEntry?>(null) : call());
        misses = 1;
        using var retry = await PostAsync(key, ada);
        misses = 1;
        using var other = await PostAsync(key, $$$"""{"submissionId":"{{{submissionId}}}","revision":1,"values":{"name":"Grace"}}""");
        misses = 2;
        using var gone = await PostAsync(key, ada);
        Hooks<IEntryRepository>().Off(nameof(IEntryRepository.GetBySubmissionAsync));

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()), "the receipt and outcome of the entry the unit found");
            Assert.That(await AnswerAsync(other), Is.EqualTo("409 duplicate_submission"), "other answers under its id");
            Assert.That(await AnswerAsync(gone), Is.EqualTo("409 duplicate_submission"), "an entry the unit found but that is gone by the next read");
            Assert.That(await StoredAsync(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheLastPlaceTakenAMomentEarlier_IsRefusedInsideTheUnit_AndTakesNoNumber()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { MaxEntries = 1 } },
        });
        using var first = await PostAsync(key, """{"revision":1,"values":{"name":"Ada"}}""");

        // The early count sees the form as it was before the first entry, as two submissions at once both do.
        Hooks<IEntryRepository>().On(nameof(IEntryRepository.CountTowardsLimitAsync), (_, _) => Task.FromResult(0));
        using var second = await PostAsync(key, """{"revision":1,"values":{"name":"Grace"}}""");
        Hooks<IEntryRepository>().Off(nameof(IEntryRepository.CountTowardsLimitAsync));

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(await AnswerAsync(second), Is.EqualTo("403 entry_limit_reached"));
            Assert.That(await StoredAsync(key), Is.EqualTo(1));
            Assert.That((await FormRows.GetAsync(key))!.LastEntryNumber, Is.EqualTo(1), "the refusal rolled the form's counter back");
        });
    }

    [Test]
    public async Task AnUploadAnotherEntryTookAMomentEarlier_FailsAtItsField_AndStoresNothing()
    {
        var attachment = Field("attachment", "file", """{"types":["text"]}""");
        var key = await PublishAsync(fields: [NameField, attachment]);
        var token = await UploadAsync(key, attachment.Key, "notes.txt", "Some notes"u8.ToArray());
        HttpResponseMessage? earlier = null;

        // Between reading the upload and storing the entry, another submission with the same token stores its entry.
        var forms = Hooks<IFormRepository>();
        forms.On(nameof(IFormRepository.ListFieldsAsync), (_, call) =>
        {
            forms.Off(nameof(IFormRepository.ListFieldsAsync));
            return TakeFirstAsync(call);
        });
        async Task<IReadOnlyList<CatalogueField>> TakeFirstAsync(Func<object?> call)
        {
            earlier = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Grace","attachment":["{{{token}}}"]}}""");
            return await (Task<IReadOnlyList<CatalogueField>>)call()!;
        }

        using var late = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Ada","attachment":["{{{token}}}"]}}""");
        using var taken = earlier!;
        var row = (await FormRows.GetAsync(key))!;
        var ids = await Entries.ListIdsAsync(row.Id, 10);

        Assert.Multiple(async () =>
        {
            Assert.That(taken.StatusCode, Is.EqualTo(HttpStatusCode.OK), await taken.Content.ReadAsStringAsync());
            Assert.That(await AnswerAsync(late), Is.EqualTo("422 validation_failed invalid at attachment"));
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That((await Entries.ListFilesAsync(ids.Single())).Single().OriginalName, Is.EqualTo("notes.txt"), "the upload is the earlier entry's");
        });
    }

    [Test]
    public async Task AFormTrashedOrGoneAMomentEarlier_TakesNothing()
    {
        var trashed = await PublishAsync("Trashed");
        var gone = await PublishAsync("Gone");

        // The site still serves the form when the submission comes: it read the form before the trash, which went past
        // the form service and its cache.
        using var served = await GetAsync($"{Root}/forms/{trashed}");
        Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var row = (await FormRows.GetAsync(trashed))!;
        _ = await FormRows.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        using var toTrashed = await PostAsync(trashed, """{"revision":1,"values":{"name":"Ada"}}""");

        // The form's row is gone by the time the unit stores the entry.
        Hooks<IEntryRepository>().On(nameof(IEntryRepository.CreateAsync), (_, _) => Task.FromResult<StoredEntry?>(null));
        using var toGone = await PostAsync(gone, """{"revision":1,"values":{"name":"Ada"}}""");
        Hooks<IEntryRepository>().Off(nameof(IEntryRepository.CreateAsync));

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(toTrashed), Is.EqualTo("404 form_not_found"));
            Assert.That(await AnswerAsync(toGone), Is.EqualTo("404 form_not_found"));
            Assert.That(await StoredAsync(trashed) + await StoredAsync(gone), Is.Zero);
        });
    }

    [Test]
    public async Task ARetry_IsAnsweredFromTheStoredEntry_WithItsContext_OrWithTheDefaultMessageWithoutItsEvent()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Outcomes = [Thanks],
            Settings = definition.Settings with { Developer = definition.Settings.Developer with { ContextValues = ["campaign"] } },
        });
        var ada = $$$"""{"submissionId":"{{{Guid.NewGuid()}}}","revision":1,"values":{"name":"Ada"},"context":{"campaign":"autumn"}}""";
        using var first = await PostAsync(key, ada);
        using var retry = await PostAsync(key, ada);

        // An entry stored another way, without the event that names its outcome.
        Hooks<IEntryEventRepository>().On(nameof(IEntryEventRepository.ListForEntryAsync), (_, _) => Task.FromResult<IReadOnlyList<StoredEntryEvent>>([]));
        using var withoutEvent = await PostAsync(key, ada);
        Hooks<IEntryEventRepository>().Off(nameof(IEntryEventRepository.ListForEntryAsync));

        Assert.Multiple(async () =>
        {
            Assert.That((await JsonAsync(first))["outcome"]!["html"]!.GetValue<string>(), Is.EqualTo("<p>Thanks from autumn.</p>"));
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()), "rendered again from the stored context");
            Assert.That((await JsonAsync(withoutEvent))["outcome"]!.ToJsonString(), Is.EqualTo("""{"kind":"message"}"""));
            Assert.That(await StoredAsync(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnOlderRevisionThatCannotBeRead_IsAnsweredFormChanged()
    {
        var email = Field("email", "email");
        var key = await PublishAsync();
        await SaveAndPublishAsync(key, definition => WithFields(definition, NameField, email));
        var row = (await FormRows.GetAsync(key))!;
        var first = (await FormRows.GetRevisionAsync(row.Id, 1))!;

        // A version a newer schema wrote, or that something else changed: this server cannot read it.
        await GetRequiredService<FormBuilderDatabase>().WriteAsync(db =>
        {
            var n = new SqlNames(db);
            return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.FormVersion)} SET {n.C("definition")} = @0 WHERE {n.C("id")} = @1", """{"schemaVersion":999}""", first.Info.Id);
        });
        using var older = await PostAsync(key, """{"revision":1,"values":{"name":"Ada"}}""");
        using var current = await PostAsync(key, """{"revision":2,"values":{"name":"Ada"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(older), Is.EqualTo("409 form_changed"));
            Assert.That(current.StatusCode, Is.EqualTo(HttpStatusCode.OK), await current.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task ThePipelineCalledWithoutATokenReader_TakesNoUpload()
    {
        var attachment = Field("attachment", "file", """{"types":["text"]}""");
        var key = await PublishAsync(fields: [NameField, attachment]);
        var token = await UploadAsync(key, attachment.Key, "notes.txt", "Some notes"u8.ToArray());

        var result = await GetRequiredService<FormSubmissions>().SubmitAsync(new SubmissionRequest
        {
            Form = key,
            Revision = 1,
            Fields = new Dictionary<string, PostedAnswer> { ["name"] = new(["Ada"]), ["attachment"] = new([token]) },
        });

        Assert.Multiple(async () =>
        {
            Assert.That(result.Refusal, Is.EqualTo(SubmissionRefusal.ValidationFailed));
            Assert.That(result.Problems, Is.EqualTo(new[] { new SubmissionProblem("invalid", "attachment") }));
            Assert.That(await StoredAsync(key), Is.Zero);
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _storeRoot = Path.Combine(Path.GetTempPath(), $"jfb-entries-{Guid.NewGuid():N}");
        configuration[FormBuilderFilesOptions.RootKey] = _storeRoot;
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(provider => Intercepted<IEntryRepository>.Over(ActivatorUtilities.CreateInstance<EntryRepository>(provider)));
        builder.Services.AddUnique(provider => Intercepted<IFormRepository>.Over(ActivatorUtilities.CreateInstance<FormRepository>(provider)));
        builder.Services.AddUnique(provider => Intercepted<IEntryEventRepository>.Over(ActivatorUtilities.CreateInstance<EntryEventRepository>(provider)));
    }

    private static FormField Field(string alias, string type, string? settings = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    // The status, the code and each error with where it is, such as "422 validation_failed required at name"; "200" for an entry.
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return "200";
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}";
    }

    private Intercepted<T> Hooks<T>()
        where T : class => Intercepted<T>.Of(GetRequiredService<T>());

    private async Task<HttpResponseMessage> PostAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<int> StoredAsync(Guid form) => (await Entries.ListIdsAsync((await FormRows.GetAsync(form))!.Id, 10)).Count;

    // Uploads a file to the field and gives its token.
    private async Task<string> UploadAsync(Guid form, Guid field, string name, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", name);
        using var response = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/uploads?fieldKey={field}", request => request.Content = content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response))["token"]!.GetValue<string>();
    }
}
