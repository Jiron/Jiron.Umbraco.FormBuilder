using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// What the section's shell reads (ROADMAP 9.1, ADR-0165): what a user holds, the checklist as the site really stands, health, the
/// checks Umbraco finds and the "used on" check's action.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class SetupApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private static IEnumerable<string> Strings(System.Text.Json.Nodes.JsonNode? list) => list!.AsArray().Select(item => item!.GetValue<string>());

    private async Task<Dictionary<string, (bool Done, bool CanDo)>> StepsAsync(string role)
    {
        var answer = await CallAsync(role, HttpMethod.Get, "setup/checklist");
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        return answer.Body!["items"]!.AsArray().ToDictionary(
            item => item!["step"]!.GetValue<string>(),
            item => (item!["done"]!.GetValue<bool>(), item!["canDo"]!.GetValue<bool>()));
    }

    [Test]
    public async Task Me_SaysWhatAUserHoldsGloballyAndAnywhere()
    {
        var folder = await FolderAsync("HR");
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);

        var privacy = await CallAsync("privacyOfficer", HttpMethod.Get, "me");
        var hr = await CallAsync("hr", HttpMethod.Get, "me");
        var nobody = await CallAsync("noAccess", HttpMethod.Get, "me");
        var admin = await CallAsync("admin", HttpMethod.Get, "me");

        Assert.Multiple(() =>
        {
            Assert.That(Strings(privacy.Body!["global"]), Is.EqualTo(new[] { "viewEntries", "viewSensitive", "deleteEntries", "privacyTools" }));
            Assert.That(Strings(hr.Body!["global"]), Is.Empty);
            Assert.That(Strings(hr.Body!["anywhere"]), Is.EqualTo(new[] { "viewEntries" }), "one folder is enough for the Inbox");
            Assert.That(Strings(nobody.Body!["anywhere"]), Is.Empty);
            Assert.That(Strings(admin.Body!["global"]), Has.Member("managePermissions").And.Member("privacyTools"));
        });
    }

    [Test]
    public async Task TheChecklist_FollowsTheSite_AndWhoMayDoEachStep()
    {
        var before = await StepsAsync("admin");
        var form = await FormAsync("Contact");
        await GetRequiredService<FormBlockTypes>().CreateAsync(global::Umbraco.Cms.Core.Constants.Security.SuperUserKey);
        var after = await StepsAsync("admin");
        var designer = await StepsAsync("designer");
        var viewer = await StepsAsync("viewer");
        var answer = await CallAsync("designer", HttpMethod.Get, "setup/checklist");

        Assert.Multiple(() =>
        {
            Assert.That(before["form"].Done, Is.False);
            Assert.That(before["formBlock"].Done, Is.False);
            Assert.That(after["form"].Done, Is.True);
            Assert.That(after["formBlock"].Done, Is.True);
            Assert.That(after["testEntry"].Done, Is.False);
            Assert.That(after["access"].Done, Is.True, "the personas' groups view entries");
            Assert.That(designer["form"].CanDo, Is.True);
            Assert.That(designer["access"].CanDo, Is.False);
            Assert.That(viewer["testEntry"].CanDo, Is.False);
            Assert.That(answer.Body!["previewForm"]!.GetValue<Guid>(), Is.EqualTo(form));
        });
    }

    [Test]
    public async Task TheFormStep_ReadsAFormOutsideTheTrash_AndNamesTheNewest()
    {
        var older = await FormAsync("Older");
        var newer = await FormAsync("Newer");
        var forms = GetRequiredService<IFormBuilderFormService>();
        var rows = GetRequiredService<IFormRepository>();
        var newest = await CallAsync("designer", HttpMethod.Get, "setup/checklist");

        await forms.TrashAsync(newer, (await rows.GetAsync(newer))!.ConcurrencyVersion, null);
        var afterOne = await CallAsync("designer", HttpMethod.Get, "setup/checklist");
        var one = await StepsAsync("designer");
        await forms.TrashAsync(older, (await rows.GetAsync(older))!.ConcurrencyVersion, null);
        var none = await StepsAsync("designer");

        Assert.Multiple(() =>
        {
            Assert.That(newest.Body!["previewForm"]!.GetValue<Guid>(), Is.EqualTo(newer), "the newest of the two");
            Assert.That(afterOne.Body!["previewForm"]!.GetValue<Guid>(), Is.EqualTo(older), "the trash doesn't count");
            Assert.That(one["form"].Done, Is.True);
            Assert.That(none["form"].Done, Is.False, "only forms in the trash");
        });
    }

    [Test]
    public async Task TheEntryStep_IsDone_WithAnEntry_AndStaysDone_OnceTheEntryIsGone()
    {
        var form = await FormAsync("Contact");
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(form))!;
        await forms.PublishAsync(form, draft.Form.ConcurrencyVersion, null, null);
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var entries = GetRequiredService<IEntryRepository>();
        var before = await StepsAsync(Admin);

        var entry = (await entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            IsTest = true,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
        var after = await StepsAsync(Admin);
        await entries.DeleteAsync(entry.Id);
        var gone = await StepsAsync(Admin);

        Assert.Multiple(() =>
        {
            Assert.That(before["testEntry"].Done, Is.False);
            Assert.That(after["testEntry"].Done, Is.True, "a test entry is what the step asks for");
            Assert.That(gone["testEntry"].Done, Is.True, "deleting it takes nothing back");
        });
    }

    [Test]
    public async Task Health_ListsWhatIsWrong_ForAnyUserOfTheSection_InTheirWords()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "setup/health");
        var sectionless = await CallAsync("outsider", HttpMethod.Get, "setup/health");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["findings"]!.AsArray().Select(finding => finding!["message"]!.GetValue<string>()), Is.All.Not.Empty);
            Assert.That(sectionless.Outcome, Is.EqualTo("403 no_section"));
        });
    }

    [Test]
    public void Umbraco_FindsEveryCheck_UnderOneGroup_WithItsOwnKey()
    {
        var checks = GetRequiredService<HealthCheckCollection>().Where(check => check.Group == FormBuilderHealthCheck.GroupName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(checks.Select(check => check.GetType().Name).Order(), Is.EqualTo(new[]
            {
                nameof(BaseUrlHealthCheck), nameof(DeliveriesHealthCheck), nameof(EmailHealthCheck), nameof(JobsHealthCheck),
                nameof(KeyRingHealthCheck), nameof(MigrationsHealthCheck), nameof(UploadsHealthCheck), nameof(UsedOnHealthCheck),
            }));
            Assert.That(checks.Select(check => check.Id).Distinct().Count(), Is.EqualTo(checks.Count));
        });
    }

    [Test]
    public async Task TheUsedOnCheck_AsksForARebuildFromTheFirstDocument_WhichTheJobFinishes_AndSaysWhileOneIsUnderWay()
    {
        var check = GetRequiredService<HealthCheckCollection>().OfType<UsedOnHealthCheck>().Single();
        var rebuild = GetRequiredService<FormReferenceRebuild>();
        var job = GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().OfType<FormReferenceRebuildJob>().Single();
        await RunRebuildAsync(job);
        var quiet = (await check.GetStatusAsync()).Single();

        var status = await check.ExecuteActionAsync(quiet.Actions.Single());
        var requested = await GetRequiredService<IKeyValueRepository>().GetAsync(Core.Persistence.Migrations.RequestReferenceRebuildV1.RebuildKey);
        var underWay = (await check.GetStatusAsync()).Single();
        await RunRebuildAsync(job);

        Assert.Multiple(async () =>
        {
            Assert.That(quiet.ResultType, Is.EqualTo(StatusResultType.Success));
            Assert.That(quiet.Actions.Single().Alias, Is.EqualTo(UsedOnHealthCheck.RebuildAction));
            Assert.That(status.ResultType, Is.EqualTo(StatusResultType.Info));
            Assert.That(requested, Does.StartWith(Core.Persistence.Migrations.RequestReferenceRebuildV1.FromStart), "from the first document");
            Assert.That(underWay.ResultType, Is.EqualTo(StatusResultType.Info));
            Assert.That(underWay.Actions, Is.Empty, "one is under way");
            Assert.That(await rebuild.IsDueAsync(), Is.False, "the job finished it");
            Assert.That((await check.GetStatusAsync()).Single().ResultType, Is.EqualTo(StatusResultType.Success));
        });
    }

    // The job holds a request just made for one run, so that every server has heard of what asked; the next run does it.
    private static async Task RunRebuildAsync(IDistributedBackgroundJob job)
    {
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);
    }

    [Test]
    public async Task TheJobs_LeaveAHeartbeat()
    {
        var job = GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().OfType<FormBuilderOutboxJob>().Single();

        await job.ExecuteAsync(CancellationToken.None);

        Assert.That(await GetRequiredService<IKeyValueRepository>().GetAsync(JobHeartbeat.Key), Is.Not.Null);
    }
}
