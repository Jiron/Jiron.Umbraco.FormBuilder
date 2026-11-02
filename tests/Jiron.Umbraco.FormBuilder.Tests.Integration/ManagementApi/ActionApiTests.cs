using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The endpoints of the action framework (ROADMAP 8.1, section 4.4, section 4.7): the action types, an entry's runs, the retry, which
/// refuses an uncertain run without a confirmation, and test runs, of which a type that reaches what the action is set up to reach
/// needs a confirmation too.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ActionApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IActionRunRepository Runs => GetRequiredService<IActionRunRepository>();

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private ScriptedActionType Scripted => GetRequiredService<FormBuilderActionTypeCollection>().OfType<ScriptedActionType>().Single();

    [Test]
    public async Task TheActionTypes_AreServedToAUserOfTheSection_WithTheirSettings()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");

        var scripted = answer.Body!["types"]!.AsArray().Single(type => type!["alias"]!.GetValue<string>() == ScriptedActionType.Alias)!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(scripted["name"]!.GetValue<string>(), Is.EqualTo("Scripted"));
            Assert.That(scripted["icon"]!.GetValue<string>(), Is.EqualTo("icon-script"));
            Assert.That(scripted["test"]!.GetValue<string>(), Is.EqualTo("afterConfirmation"));
            Assert.That(scripted["settings"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("target"));
        });
    }

    [Test]
    public async Task TheBuiltInActionTypes_AreServedAsTheReviewedSnapshotHolds()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");
        var builtIn = new JsonArray([.. answer.Body!["types"]!.AsArray().Where(type => type!["alias"]!.GetValue<string>() is EmailActionType.Alias).Select(type => type!.DeepClone())]);
        var served = builtIn.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        var path = RepositoryRoot.Path(typeof(ActionApiTests).Assembly, "docs", "api", "action-types.json");
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(path, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {path}. Review the diff, check the backoffice's dictionaries, commit it, and run again without it.");
        }

        Assert.That((await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the action types changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task TheEmailType_OffersItsPresets()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");

        var email = answer.Body!["types"]!.AsArray().Single(type => type!["alias"]!.GetValue<string>() == EmailActionType.Alias)!;
        var presets = email["presets"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(email["test"]!.GetValue<string>(), Is.EqualTo("toCurrentUser"));
            Assert.That(presets.Select(preset => preset!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "#jfbActionPresets_emailNotifyTeam", "#jfbActionPresets_emailConfirmSender" }));
            Assert.That(presets.All(preset => JsonNode.Parse(preset!["settings"]!.GetValue<string>())!["subject"] is not null), Is.True);
        });
    }

    [Test]
    public async Task AnEntrysRuns_AreListedForAUserWhoSeesItsEntries_ForThatFormOnly()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Failed);
        var other = await FormAsync("Other");

        var answer = await CallAsync(Admin, HttpMethod.Get, Path(form, entry));
        var elsewhere = await CallAsync(Admin, HttpMethod.Get, Path(other, entry));
        var nothing = await CallAsync(Admin, HttpMethod.Get, Path(form, Guid.NewGuid()));

        var first = answer.Body!.AsArray()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(first["id"]!.GetValue<long>(), Is.EqualTo(run.Id));
            Assert.That(first["state"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(first["trigger"]!.GetValue<string>(), Is.EqualTo("submitted"));
            Assert.That(first["actionType"]!.GetValue<string>(), Is.EqualTo(ScriptedActionType.Alias));
            Assert.That(first["errorCode"]!.GetValue<string>(), Is.EqualTo("refused"));
            Assert.That(first["errorMessage"]!.GetValue<string>(), Is.EqualTo("It said no."));
            Assert.That(first["attempts"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(elsewhere.Outcome, Is.EqualTo("404 action_run_not_found"), elsewhere.ToString());
            Assert.That(nothing.Outcome, Is.EqualTo("404 action_run_not_found"), nothing.ToString());
        });
    }

    [Test]
    public async Task ARetryOfAFailedRun_QueuesItAgain_WithItsAttemptsStartingOver_AndIsAudited()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Failed);

        var answer = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { });

        var stored = await Runs.GetAsync(run.Id);
        var audited = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = ActionRunRetries.RetriedAction }, PageRequest.First(5));
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["state"]!.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(stored!.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(stored.Attempts, Is.Zero);
            Assert.That(audited.Items.Single().EntityKey, Is.EqualTo(form));
            Assert.That(audited.Items.Single().Details, Does.Contain($"\"run\":{run.Id}").And.Contain("\"uncertain\":false"));
        });
    }

    [Test]
    public async Task ARetryOfAnUncertainRun_IsRefusedWithoutItsConfirmation_AndGoesWithIt()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Uncertain);

        var without = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { });
        var saidNo = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { confirmed = false });
        var noBody = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry");
        var stillUncertain = await Runs.GetAsync(run.Id);
        var confirmed = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { confirmed = true });

        Assert.Multiple(async () =>
        {
            Assert.That(without.Outcome, Is.EqualTo("409 retry_needs_confirmation"), without.ToString());
            Assert.That(saidNo.Outcome, Is.EqualTo("409 retry_needs_confirmation"), saidNo.ToString());
            Assert.That(noBody.Outcome, Is.EqualTo("409 retry_needs_confirmation"), noBody.ToString());
            Assert.That(stillUncertain!.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(confirmed.Outcome, Is.EqualTo("200"), confirmed.ToString());
            Assert.That((await Runs.GetAsync(run.Id))!.State, Is.EqualTo(ActionRunState.Pending));
        });
    }

    [Test]
    public async Task ARetryOfARunThatDidNotFail_IsRefused_AndOneThatIsNotTheEntrysIsNotFound()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Succeeded);
        var (_, otherEntry, otherRun) = await RunInAsync(ActionRunState.Failed);

        var succeeded = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { confirmed = true });
        var notItsRun = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{otherRun.Id}/retry", new { confirmed = true });
        var notItsForm = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, otherEntry)}/{otherRun.Id}/retry", new { confirmed = true });
        var noRun = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/999999/retry", new { confirmed = true });

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Outcome, Is.EqualTo("409 run_not_retryable"), succeeded.ToString());
            Assert.That(notItsRun.Outcome, Is.EqualTo("404 action_run_not_found"), notItsRun.ToString());
            Assert.That(notItsForm.Outcome, Is.EqualTo("404 action_run_not_found"), notItsForm.ToString());
            Assert.That(noRun.Outcome, Is.EqualTo("404 action_run_not_found"), noRun.ToString());
        });
    }

    [Test]
    public async Task ATestRun_ThatReachesWhatTheActionIsSetUpToReach_AsksFirst_AndChangesNothing()
    {
        var action = Scripted0("target-of-the-test");
        var form = await PublishedAsync(action);

        var without = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{action.Key}/test", new { });
        var seenWithout = Scripted.Tests.Count;
        var confirmed = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{action.Key}/test", new { confirmed = true });

        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var context = Scripted.Tests.Single();
        Assert.Multiple(async () =>
        {
            Assert.That(without.Outcome, Is.EqualTo("409 test_needs_confirmation"), without.ToString());
            Assert.That(seenWithout, Is.Zero);
            Assert.That(confirmed.Outcome, Is.EqualTo("200"), confirmed.ToString());
            Assert.That(confirmed.Body!["outcome"]!.GetValue<string>(), Is.EqualTo("sent"));
            Assert.That(confirmed.Body!["message"]!.GetValue<string>(), Is.EqualTo("Sent to target-of-the-test"));
            Assert.That(confirmed.Body!["preview"]!.AsArray().Select(line => line!.GetValue<string>()), Is.EqualTo(new[] { "one line" }));
            Assert.That(context.Action.Key, Is.EqualTo(action.Key));
            Assert.That(context.Scope.Entry!.Number, Is.Zero, "a sample entry");
            Assert.That(context.User.Name, Is.Not.Empty);
            Assert.That(context.User.Email, Does.Contain("@"));
            Assert.That(await Entries.ListIdsAsync(row.Id, 5), Is.Empty, "nothing is stored");
        });
    }

    [Test]
    public async Task ATestRun_OfAnActionTheDraftLacks_IsNotFound()
    {
        var form = await PublishedAsync(Scripted0("target"));

        var answer = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{Guid.NewGuid()}/test", new { confirmed = true });

        Assert.That(answer.Outcome, Is.EqualTo("404 action_not_found"), answer.ToString());
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().ActionTypes().Append<ScriptedActionType>();
    }

    private static string Path(Guid form, Guid entry) => $"form/{form}/entry/{entry}/action-run";

    private static FormAction Scripted0(string target) => new()
    {
        Key = Guid.NewGuid(),
        Type = ScriptedActionType.Alias,
        Name = "Scripted",
        Settings = SettingsObject.Parse($$"""{"target":"{{target}}"}"""),
    };

    private async Task<Guid> PublishedAsync(params FormAction[] actions)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Form {Guid.NewGuid():N}", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, draft.Definition! with { Actions = actions }, null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    // A form with an entry whose one run is in the state.
    private async Task<(Guid Form, Guid Entry, StoredActionRun Run)> RunInAsync(ActionRunState state)
    {
        var action = Scripted0("target");
        var form = await PublishedAsync(action);
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
        var run = (await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, ActionTrigger.Submitted, 0, false)]))!.Single();
        var token = (await Runs.ClaimAsync(run.Id, "server-a", TimeSpan.FromMinutes(5)))!.LockedBy!;
        var error = new DeliveryError("refused", "It said no.");
        _ = state switch
        {
            ActionRunState.Failed => await Runs.FailAsync(run.Id, token, error),
            ActionRunState.Uncertain => await Runs.CutOffAsync(run.Id, token, error),
            _ => await Runs.SucceedAsync(run.Id, token),
        };
        return (form, entry.Key, (await Runs.GetAsync(run.Id))!);
    }
}
