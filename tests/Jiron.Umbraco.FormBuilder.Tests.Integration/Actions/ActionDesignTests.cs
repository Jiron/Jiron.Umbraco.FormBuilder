using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>
/// What publishing, saving and importing do with actions (ROADMAP 8.1, ADR-0035, ADR-0046, ADR-0060, ADR-0158): an action type the
/// site lacks is refused with <c>unknown_type</c>, each type's settings are held to its schema and its own check, a designer lock
/// refuses a save with another value and wins at run time, and an imported action whose site configuration is missing arrives
/// switched off with its note.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ActionDesignTests : ActionTestBase
{
    private const string Locks = FormBuilderOptions.SectionName + ":Designer:SettingLocks:";

    private IFormBuilderFormExchangeService Exchange => GetRequiredService<IFormBuilderFormExchangeService>();

    [Test]
    public void Publishing_RefusesAnActionTypeTheSiteLacks_SwitchedOffOrNot()
    {
        var unknown = new FormAction { Key = Guid.NewGuid(), Type = "acme.nothing", Name = "Gone" };

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await ActionFormAsync(Action("known"), unknown, unknown with { Key = Guid.NewGuid(), Enabled = false }));

        Assert.That(refused!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[]
        {
            (DefinitionErrors.UnknownType, "$.actions[1].type"),
            (DefinitionErrors.UnknownType, "$.actions[2].type"),
        }));
    }

    [Test]
    public void Publishing_HoldsTheSettingsToTheTypesSchema()
    {
        var action = Action("known") with { Settings = SettingsObject.Parse("""{"nothing":"at all"}""") };

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await ActionFormAsync(action));

        Assert.That(refused!.Problems.Single(), Has.Property(nameof(DefinitionProblem.Code)).EqualTo(DefinitionErrors.SettingUnknown).And.Property(nameof(DefinitionProblem.Path)).EqualTo("$.actions[0].settings.nothing"));
    }

    [Test]
    public void Publishing_RunsEachTypesOwnCheck_NamingTheValue()
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await ActionFormAsync(Action("known", target: "refused")));

        Assert.That(refused!.Problems.Single(), Has.Property(nameof(DefinitionProblem.Code)).EqualTo(DefinitionErrors.SettingInvalid).And.Property(nameof(DefinitionProblem.Path)).EqualTo("$.actions[0].settings.target"));
    }

    [Test]
    public void ATypesOwnCheck_RunsOnlyOnSettingsThatKeepTheSchema()
    {
        var action = Action("known") with { Settings = SettingsObject.Parse("""{"target":"refused","nothing":"at all"}""") };

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await ActionFormAsync(action));

        Assert.That(refused!.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { DefinitionErrors.SettingUnknown }));
    }

    [Test]
    public void Publishing_ChecksTheActionAsTheSiteWillRunIt_WithTheLockApplied()
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await ActionFormAsync(Action("known", target: "not-with-the-lock")));

        Assert.That(refused!.Problems.Single().Path, Is.EqualTo("$.actions[0].settings.target"));
    }

    [Test]
    public async Task ALockedSetting_WinsAtRunTime_WhateverTheFormSays()
    {
        // An import keeps what a form brings, as a save would refuse it; published, the lock still decides what runs.
        var created = await ActionFormAsync(Action("other"));
        var file = JsonNode.Parse((await Exchange.ExportAsync(created))!.Json)!;
        var brought = file["definition"]!["actions"]![0]!;
        brought["settings"] = new JsonObject { ["held"] = "from-the-form" };
        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.ToJsonString(), "Brought", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(imported.Form.Key))!;
        await Forms.PublishAsync(imported.Form.Key, draft.Form.ConcurrencyVersion, null, Editor);
        var form = imported.Form.Key;

        var entry = await SubmitAsync(form);
        await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(Scripted.HeldSeen, Is.EqualTo(new[] { "locked-value" }));
            Assert.That(draft.Definition!.Actions.Single().Settings!.Value.GetProperty("held").GetString(), Is.EqualTo("from-the-form"), "what is stored is the form's");
        });
    }

    [Test]
    public async Task ASaveThatGivesALockedSettingAnotherValue_IsRefused_WithItsPathAndKey()
    {
        var created = await Forms.CreateAsync(new NewFormRequest("Locked", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(created.Key))!;

        var refused = Assert.ThrowsAsync<FormBuilderSettingLockedException>(async () =>
            await Forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, draft.Definition! with { Actions = [Held("mine", "elsewhere")] }, null, Editor));
        var same = await Forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, draft.Definition! with { Actions = [Held("mine", "locked-value")] }, null, Editor);
        var leftOut = await Forms.SaveDraftAsync(created.Key, same!.Form.ConcurrencyVersion, draft.Definition! with { Actions = [Action("mine")] }, null, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Field, Is.EqualTo("$.actions[0].settings.held"));
            Assert.That(refused!.ConfigurationKey, Is.EqualTo(Locks + "0"));
            Assert.That(same, Is.Not.Null, "the lock's own value is fine");
            Assert.That(leftOut, Is.Not.Null, "and so is leaving it out");
        });
    }

    [Test]
    public async Task AValueFromBeforeTheLock_StaysSaveable()
    {
        var created = await Forms.CreateAsync(new NewFormRequest("Kept", UserKey: Editor));

        // Stored as an import does, which brings a whole form from elsewhere and is not a value an editor gives.
        var file = JsonNode.Parse((await Exchange.ExportAsync(created.Key))!.Json)!;
        file["definition"]!["actions"] = new JsonArray(new JsonObject
        {
            ["key"] = Guid.NewGuid().ToString(),
            ["type"] = ScriptedActionType.Alias,
            ["settings"] = new JsonObject { ["held"] = "from-before" },
        });
        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.ToJsonString(), UserKey: Editor));
        var stored = (await Forms.GetDraftAsync(imported.Form.Key))!;

        var renamed = stored.Definition! with { Actions = [stored.Definition.Actions[0] with { Name = "Renamed" }] };
        var saved = await Forms.SaveDraftAsync(imported.Form.Key, stored.Form.ConcurrencyVersion, renamed, null, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(stored.Definition!.Actions.Single().Settings!.Value.GetProperty("held").GetString(), Is.EqualTo("from-before"));
            Assert.That(saved, Is.Not.Null);
        });
    }

    [Test]
    public async Task AnImportedAction_WhoseSiteConfigurationIsMissing_ArrivesSwitchedOff_WithItsNote()
    {
        var created = await ActionFormAsync(Action("fine"), Action("alsoFine"));
        var file = (await Exchange.ExportAsync(created))!;
        Scripted.Missing = ["JironFormBuilder:Secrets:Slack"];

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.Json, "Copy", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(imported.Form.Key))!;

        Assert.Multiple(() =>
        {
            Assert.That(draft.Definition!.Actions.Select(action => action.Enabled), Is.All.False);
            Assert.That(imported.Notes, Is.EqualTo(new[]
            {
                new FormImportNote(FormImportNotes.ActionConfigurationMissing, "$.definition.actions[0]", "JironFormBuilder:Secrets:Slack"),
                new FormImportNote(FormImportNotes.ActionConfigurationMissing, "$.definition.actions[1]", "JironFormBuilder:Secrets:Slack"),
            }));
        });
    }

    [Test]
    public async Task AnImportedAction_WhoseSettingsDoNotFit_Arrives_AndPublishingRefusesIt()
    {
        var created = await ActionFormAsync(Action("fine"));
        var file = JsonNode.Parse((await Exchange.ExportAsync(created))!.Json)!;
        file["definition"]!["actions"]![0]!["settings"] = new JsonObject { ["target"] = new JsonObject { ["not"] = "a text" } };

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.ToJsonString(), "Copy", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(imported.Form.Key))!;

        Assert.Multiple(() =>
        {
            Assert.That(imported.Notes, Is.Empty);
            Assert.That(async () => await Forms.PublishAsync(imported.Form.Key, draft.Form.ConcurrencyVersion, null, Editor), Throws.TypeOf<FormBuilderValidationException>());
        });
    }

    [Test]
    public async Task AnImportedAction_WhoseSiteConfigurationIsThere_StaysSwitchedOn()
    {
        var created = await ActionFormAsync(Action("fine"));
        var file = (await Exchange.ExportAsync(created))!;

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.Json, "Copy", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(imported.Form.Key))!;

        Assert.Multiple(() =>
        {
            Assert.That(draft.Definition!.Actions.Single().Enabled, Is.True);
            Assert.That(imported.Notes, Is.Empty);
        });
    }

    [Test]
    public async Task AnImportedAction_OfATypeTheSiteLacks_ArrivesSwitchedOff_AndPublishingStillRefusesIt()
    {
        var created = await ActionFormAsync(Action("fine"));
        var file = (await Exchange.ExportAsync(created))!.Json.Replace(ScriptedActionType.Alias, "acme.nothing", StringComparison.Ordinal);

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file, "Copy", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(imported.Form.Key))!;

        Assert.Multiple(() =>
        {
            Assert.That(draft.Definition!.Actions.Single().Enabled, Is.False);
            Assert.That(imported.Notes, Is.EqualTo(new[] { new FormImportNote(FormImportNotes.ActionTypeMissing, "$.definition.actions[0]", "acme.nothing") }));
            Assert.That(async () => await Forms.PublishAsync(imported.Form.Key, draft.Form.ConcurrencyVersion, null, Editor), Throws.TypeOf<FormBuilderValidationException>());
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        configuration[Locks + "0:Kind"] = "action";
        configuration[Locks + "0:Type"] = ScriptedActionType.Alias;
        configuration[Locks + "0:Setting"] = "held";
        configuration[Locks + "0:Value"] = "locked-value";
    }

    private static FormAction Held(string name, string value) => Action(name) with { Settings = SettingsObject.Parse($$"""{"held":"{{value}}"}""") };
}
