using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.AI;
using Jiron.Umbraco.FormBuilder.AI.Health;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Umbraco.AI.Core.Connections;
using Umbraco.AI.Core.Profiles;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Settings;

/// <summary>
/// Settings > AI and the add-on's health (ROADMAP 15.3, ADR-0018): the profile, chosen by who may manage settings and
/// audited; the state every user of the section may read; and the health check, on Umbraco's dashboard and the
/// overview, which says what keeps the features from working.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AISettingsApiTests : AITestBase
{
    [Test]
    public async Task WhoMayManageSettings_ChoosesTheProfile_AndEachChoiceIsAudited()
    {
        var fake = await GetRequiredService<IAIProfileService>().GetProfileByAliasAsync(FakeAISetup.ProfileAlias);

        var read = await CallAsync("admin", HttpMethod.Get, "ai/settings");
        var cleared = await CallAsync("admin", HttpMethod.Put, "ai/settings", new { profileId = (Guid?)null });
        var withoutStatus = await CallAsync("viewer", HttpMethod.Get, "ai/status");
        var chosen = await CallAsync("admin", HttpMethod.Put, "ai/settings", new { profileId = fake!.Id });
        var withStatus = await CallAsync("viewer", HttpMethod.Get, "ai/status");

        Assert.Multiple(async () =>
        {
            Assert.That(read.Body!["profileId"]!.GetValue<Guid>(), Is.EqualTo(fake.Id));
            Assert.That(cleared.Status, Is.EqualTo(HttpStatusCode.OK), cleared.ToString());
            Assert.That(cleared.Body!["profileId"], Is.Null, "Umbraco.AI's default");
            Assert.That(withoutStatus.Body!["ready"]!.GetValue<bool>(), Is.False, "the test site has no default chat profile");
            Assert.That(withoutStatus.Body!["problem"]!.GetValue<string>(), Is.EqualTo("ai_no_profile"));
            Assert.That(chosen.Body!["profileId"]!.GetValue<Guid>(), Is.EqualTo(fake.Id));
            Assert.That(withStatus.Body!["ready"]!.GetValue<bool>(), Is.True);
            Assert.That(withStatus.Body!["profileName"]!.GetValue<string>(), Is.EqualTo(fake.Name));
            Assert.That(await AuditCountAsync(FormBuilderAI.ProfileChosenAction), Is.EqualTo(3), "the set-up's own choice, then the clearing and the choice");
        });

        // Placed as a settings section's save is, so the audit log says "Settings > AI", with the profiles as details.
        var entries = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = FormBuilderAI.ProfileChosenAction }, new PageRequest(0, 10))).Items;
        var clearing = entries.Single(entry => JsonNode.Parse(entry.Details)!["to"] is null);
        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => (entry.EntityType, entry.EntityKey)), Is.All.EqualTo(("settings", (Guid?)null)));
            Assert.That(entries.Select(entry => JsonNode.Parse(entry.Details)!["section"]!.GetValue<string>()), Is.All.EqualTo("ai"));
            Assert.That(JsonNode.Parse(clearing.Details)!["from"]!.GetValue<Guid>(), Is.EqualTo(fake.Id), "what the clearing took away");
        });
    }

    [Test]
    public async Task OnlyAChatProfile_MayBeChosen_AndOnlyByWhoMayManageSettings()
    {
        Assert.Multiple(async () =>
        {
            Assert.That((await CallAsync("admin", HttpMethod.Put, "ai/settings", new { profileId = Guid.NewGuid() })).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await CallAsync("designer", HttpMethod.Get, "ai/settings")).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That((await CallAsync("designer", HttpMethod.Put, "ai/settings", new { profileId = (Guid?)null })).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That((await CallAsync("outsider", HttpMethod.Get, "ai/status")).Status, Is.EqualTo(HttpStatusCode.Forbidden), "the section's users only");
            Assert.That((await CallAsync(null, HttpMethod.Get, "ai/status")).Status, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task AChosenProfileThatWasDeleted_IsSaid_AndTheFeaturesRefuse_NeverFallingBackToAnother()
    {
        var fake = await GetRequiredService<IAIProfileService>().GetProfileByAliasAsync(FakeAISetup.ProfileAlias);
        await GetRequiredService<IAIProfileService>().DeleteProfileAsync(fake!.Id);

        var status = await CallAsync("designer", HttpMethod.Get, "ai/status");
        var draft = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = "A contact form" });

        Assert.Multiple(() =>
        {
            Assert.That(status.Body!["problem"]!.GetValue<string>(), Is.EqualTo("ai_profile_missing"));
            Assert.That(draft.Outcome, Is.EqualTo("409 ai_profile_missing"), draft.ToString());
            Assert.That(Journal.Entries, Has.None.StartsWith("request:"));
        });
    }

    [Test]
    public async Task TheHealthCheck_PassesWithAProfileWhoseConnectionAnswers_AndSaysWhatIsWrongOtherwise()
    {
        var check = GetRequiredService<HealthCheckCollection>().OfType<AIHealthCheck>().Single();
        var passing = (await check.GetStatusAsync()).Single();

        var connections = GetRequiredService<IAIConnectionService>();
        var connection = await connections.GetConnectionByAliasAsync(FakeAISetup.ConnectionAlias);
        connection!.IsActive = false;
        await connections.SaveConnectionAsync(connection);
        var off = (await check.GetStatusAsync()).Single();
        await CallAsync("admin", HttpMethod.Put, "ai/settings", new { profileId = (Guid?)null });
        var none = await GetRequiredService<FormBuilderHealth>().FindAsync("de-DE", namesSites: true);

        Assert.Multiple(() =>
        {
            Assert.That(passing.ResultType, Is.EqualTo(StatusResultType.Success), passing.Message);
            Assert.That(off.ResultType, Is.EqualTo(StatusResultType.Warning));
            Assert.That(off.Message, Does.Contain("switched off"));
            Assert.That(none.Select(finding => finding.Code), Does.Contain("ai_no_profile"), "the overview reads the same probe");
            Assert.That(none.Single(finding => finding.Code == "ai_no_profile").Message, Does.StartWith("Das KI-Add-on hat kein Profil"));
        });
    }
}
