using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The general settings through the management API (ROADMAP 10.3b, ADR-0198): how long a form's History keeps draft
/// saves, with their limits and the host's locks; a save keeps the rest of the section, refuses a value out of range and
/// a save made meanwhile, needs <c>manageSettings</c>; and the draft cleanup keeps draft saves as the saved values say.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class GeneralSettingsApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private const string Path = "settings/general";

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    [Test]
    public async Task TheGeneralSettings_AnswerHowLongDraftSavesStay_WithTheirLimits_AndNoLocks()
    {
        var answer = await Api.CallAsync(Admin, HttpMethod.Get, Path);
        var version = await VersionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["version"]!.GetValue<int>(), Is.EqualTo(version), "the stored row's, which a save sends back");
            Assert.That(JsonNode.DeepEquals(answer.Body["history"], JsonNode.Parse("""{"draftDays":90,"draftCount":50}""")), Is.True, answer.Text);
            Assert.That(JsonNode.DeepEquals(answer.Body["historyLimits"], JsonNode.Parse("""{"minDraftDays":1,"maxDraftDays":3650,"maxDraftCount":1000}""")), Is.True, answer.Text);
            Assert.That(answer.Body["locks"]!.AsArray(), Is.Empty);
        });
    }

    [Test]
    public async Task ASave_ChangesTheHistory_AndKeepsTheRestOfTheSection()
    {
        var before = await Settings.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, await VersionAsync(), null);

        var saved = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version = before.Version, history = new { draftDays = 30, draftCount = 5 } });
        var read = await Api.CallAsync(Admin, HttpMethod.Get, Path);

        var stored = await Settings.GetAsync<GeneralSettings>();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["version"]!.GetValue<int>(), Is.EqualTo(before.Version + 1));
            Assert.That(JsonNode.DeepEquals(read.Body!["history"], JsonNode.Parse("""{"draftDays":30,"draftCount":5}""")), Is.True, read.Text);
            Assert.That((stored.Value.TimeZone, stored.Value.History.DraftDays, stored.Value.History.DraftCount), Is.EqualTo(("Europe/Berlin", 30, 5)));
        });
    }

    [Test]
    public async Task ASaveWithoutTheHistory_LeavesItAsItWas()
    {
        var before = await Settings.SaveAsync(new GeneralSettings { History = new HistorySettings { DraftDays = 7, DraftCount = 3 } }, await VersionAsync(), null);

        var saved = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version = before.Version });

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(JsonNode.DeepEquals(saved.Body!["history"], JsonNode.Parse("""{"draftDays":7,"draftCount":3}""")), Is.True, saved.Text);
        });
    }

    [Test]
    public async Task AValueOutOfRange_IsRefused_WithItsField_AndNothingIsSaved()
    {
        var version = await VersionAsync();
        var days = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version, history = new { draftDays = 0, draftCount = 5 } });
        var count = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version, history = new { draftDays = 30, draftCount = HistorySettings.MaxDraftCount + 1 } });

        Assert.Multiple(async () =>
        {
            Assert.That(days.Outcome, Is.EqualTo("400 validation_failed"), days.ToString());
            Assert.That(Fields(days), Is.EqualTo(new[] { ("general.history.draftDays", "range") }));
            Assert.That(count.Outcome, Is.EqualTo("400 validation_failed"), count.ToString());
            Assert.That(Fields(count), Is.EqualTo(new[] { ("general.history.draftCount", "range") }));
            Assert.That(await VersionAsync(), Is.EqualTo(version), "nothing saved");
        });
    }

    [Test]
    public async Task ASaveMadeMeanwhile_IsNotOverwritten()
    {
        var read = await VersionAsync();
        await Settings.SaveAsync(new GeneralSettings { History = new HistorySettings { DraftDays = 7, DraftCount = 3 } }, read, null);

        var answer = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version = read, history = new { draftDays = 30, draftCount = 5 } });

        Assert.Multiple(async () =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("409 concurrency_conflict"), answer.ToString());
            Assert.That((await Settings.GetAsync<GeneralSettings>()).Value.History.DraftDays, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task TheGeneralSettings_NeedTheRightToManageSettings()
    {
        var version = await VersionAsync();
        var read = await Api.CallAsync("designer", HttpMethod.Get, Path);
        var save = await Api.CallAsync("designer", HttpMethod.Put, Path, new { version, history = new { draftDays = 30, draftCount = 5 } });

        Assert.Multiple(async () =>
        {
            Assert.That(read.Outcome, Is.EqualTo("403 forbidden"), read.ToString());
            Assert.That(save.Outcome, Is.EqualTo("403 forbidden"), save.ToString());
            Assert.That(await VersionAsync(), Is.EqualTo(version));
        });
    }

    [Test]
    public async Task TheDraftCleanup_KeepsDraftSaves_AsTheSavedSettingsSay()
    {
        var form = await FormAsync("History kept");
        await SaveDraftsAsync(form, 5);

        // Every draft save is older than a day once the cleanup runs two days on; the newest are kept by number.
        var version = await VersionAsync();
        var four = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version, history = new { draftDays = 1, draftCount = 4 } });
        await CleanUpAsync();
        var afterFour = await NumbersAsync(form);
        var two = await Api.CallAsync(Admin, HttpMethod.Put, Path, new { version = version + 1, history = new { draftDays = 1, draftCount = 2 } });
        await CleanUpAsync();
        var afterTwo = await NumbersAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That((four.Outcome, two.Outcome), Is.EqualTo(("200", "200")), $"{four} / {two}");
            Assert.That(afterFour, Is.EqualTo(new[] { 6, 5, 4, 3 }), "the four newest of six");
            Assert.That(afterTwo, Is.EqualTo(new[] { 6, 5 }), "the two newest, as the changed setting says");
        });
    }

    // The version of the stored general settings, which a save sends back.
    private async Task<int> VersionAsync() => (await Settings.GetAsync<GeneralSettings>()).Version;

    // The fields and codes of a refusal's errors.
    private static (string Field, string Code)[] Fields(ApiAnswer answer) =>
        [.. answer.Body!["errors"]!.AsArray().Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()))];

    // Saves the form's draft this many times, each with another label, so each is a draft save of its own.
    private async Task SaveDraftsAsync(Guid formKey, int saves)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        for (var save = 1; save <= saves; save++)
        {
            var draft = (await forms.GetDraftAsync(formKey))!;
            var field = NameField with { Label = new LocalizedText([new(string.Empty, $"Name {save}")]) };
            var definition = draft.Definition! with { Pages = [draft.Definition!.Pages[0] with { Groups = [draft.Definition.Pages[0].Groups[0] with { Fields = [field] }] }] };
            _ = (await forms.SaveDraftAsync(formKey, draft.Form.ConcurrencyVersion, definition, null, null))!;
        }
    }

    // Runs the draft cleanup as the retention job would, two days on.
    private async Task CleanUpAsync()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow.AddDays(2));
        await using var scope = Services.CreateAsyncScope();
        var cleanup = ActivatorUtilities.CreateInstance<DraftVersionCleanup>(scope.ServiceProvider, (TimeProvider)clock);
        await cleanup.RunAsync(clock.GetUtcNow() + FormBuilderRetentionJob.Budget, CancellationToken.None);
    }

    // The form's versions, newest first, as the History lists them with the draft saves.
    private async Task<int[]> NumbersAsync(Guid formKey)
    {
        var answer = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{formKey}/versions?includeDrafts=true&take=50");
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        return [.. answer.Body!["items"]!.AsArray().Select(item => item!["version"]!["number"]!.GetValue<int>())];
    }
}

/// <summary>A site whose host locks the general section (ADR-0008, ADR-0198): the endpoint names the key, and refuses a save.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class LockedGeneralSettingsApiTests : ManagementApiTestBase
{
    /// <summary>Locks the general section.</summary>
    public LockedGeneralSettingsApiTests() => InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:0"] = "general";

    [Test]
    public async Task ALockedSection_IsAnsweredWithItsKey_AndASaveIsRefused()
    {
        var read = await Api.CallAsync("admin", HttpMethod.Get, "settings/general");
        var save = await Api.CallAsync("admin", HttpMethod.Put, "settings/general", new { version = 0, history = new { draftDays = 30, draftCount = 5 } });

        Assert.Multiple(() =>
        {
            Assert.That(read.Outcome, Is.EqualTo("200"), read.ToString());
            Assert.That(JsonNode.DeepEquals(read.Body!["locks"], JsonNode.Parse("""[{"field":"*","kind":"locked","configurationKey":"JironFormBuilder:LockedSections:0"}]""")), Is.True, read.Text);
            Assert.That(save.Outcome, Is.EqualTo("409 setting_locked"), save.ToString());
            Assert.That(save.Body!["configurationKey"]!.GetValue<string>(), Is.EqualTo("JironFormBuilder:LockedSections:0"));
        });
    }
}
