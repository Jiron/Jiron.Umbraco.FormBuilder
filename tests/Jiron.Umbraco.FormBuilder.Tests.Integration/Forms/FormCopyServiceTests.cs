using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// Copies, templates, imports and exports through the form service (section 4.5, ADR-0046): a duplicate with new keys
/// that its references follow, actions copied to another form with their fields mapped by alias, forms from the
/// built-in Contact template and from saved templates, and export files that import as new forms; with their
/// refusals, notifications and audit entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormCopyServiceTests : RepositoryTestBase
{
    private static readonly Guid ListKey = Guid.Parse("72000000-0000-4000-8000-000000000001");

    private static readonly FormField Email = new() { Key = Key(1), Type = "email", Alias = "email", Label = new LocalizedText("Email address"), Required = true };

    private static readonly FormField Topic = new()
    {
        Key = Key(2),
        Type = "radio",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "sales", Label = new LocalizedText("Sales") }, new FieldOption { Value = "support", Label = new LocalizedText("Support") }] },
        Rules = [new FormRule { Key = Key(3), Effect = RuleEffect.Show, Conditions = [new RuleCondition { Field = Key(1), Operator = "isNotEmpty" }] }],
    };

    private static readonly FormField Confirm = new()
    {
        Key = Key(4),
        Type = "email",
        Alias = "confirmEmail",
        Label = new LocalizedText("Email again"),
        Validation = [new MatchesFieldCheck { Field = Key(1), Message = new LocalizedText("The addresses differ.") }],
    };

    private static readonly FormField Session = new()
    {
        Key = Key(5),
        Type = "dropdown",
        Alias = "session",
        Label = new LocalizedText("Session"),
        Options = new FieldOptions { Source = OptionSource.List, OptionList = ListKey },
    };

    private static readonly FormAction Notify = new()
    {
        Key = Key(6),
        Type = "email",
        Name = "Notify sales",
        Rules = [new FormRule { Key = Key(7), Effect = RuleEffect.Run, Conditions = [new RuleCondition { Field = Key(2), Operator = "equals", Value = new ConditionOperand.Text("sales") }] }],
        Settings = SettingsObject.Parse($$"""{"replyTo":"{{Key(1)}}","to":["sales@example.com"]}"""),
    };

    private static readonly FormAction Hook = new() { Key = Key(8), Type = "webhook", Name = "CRM", Enabled = false };

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private IFormBuilderFormExchangeService Exchange => GetRequiredService<IFormBuilderFormExchangeService>();

    private IFormBuilderSettingsService SettingsService => GetRequiredService<IFormBuilderSettingsService>();

    private FormNotificationLog Notifications => GetRequiredService<FormNotificationLog>();

    [Test]
    public async Task ADuplicate_RemapsRuleReferences_ToItsOwnElements_AndCopiesTheDraft()
    {
        var folder = await CreateFolderAsync();
        var (source, designed) = await DesignedAsync();
        Clock.Advance(TimeSpan.FromMinutes(1));

        var copy = (await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Contact (copy)", IncludeActions: true, FolderKey: folder.Key, UserKey: Editor)))!;
        var draft = (await Service.GetDraftAsync(copy.Key))!;
        var definition = draft.Definition!;
        var fields = DefinitionKeys.Fields(definition).ToDictionary(field => field.Alias!, field => field.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(copy, Is.EqualTo(new FormInfo(copy.Key, "contactCopy", "Contact (copy)", folder.Key, copy.ConcurrencyVersion, false, 0, false, null, Now.AddMinutes(1), Now.AddMinutes(1), Editor, Editor)), "a new form, unpublished");
            Assert.That((draft.DraftVersion, draft.PublishedVersion), Is.EqualTo((1, (int?)null)));
            Assert.That(DefinitionKeys.Of(definition).Intersect(DefinitionKeys.Of(designed)), Is.Empty, "every element has a new key");
            Assert.That(definition.Key, Is.EqualTo(copy.Key), "the definition's key is the copy's");
            Assert.That(fields.Keys, Is.EqualTo(new[] { "email", "topic", "confirmEmail", "session" }), "the draft, which has the field the published version lacks");
            Assert.That(Condition(definition.Pages[0].Groups[0].Fields[1].Rules[0]).Field, Is.EqualTo(fields["email"]), "a field's rule names the copy's field");
            Assert.That(Condition(definition.Actions[0].Rules[0]).Field, Is.EqualTo(fields["topic"]), "an action's rule too");
            Assert.That(definition.ValidationRules[0].Field, Is.EqualTo(fields["confirmEmail"]), "a validation rule");
            Assert.That(Condition(definition.ValidationRules[0]).Field, Is.EqualTo(fields["email"]));
            Assert.That(((MatchesFieldCheck)definition.Pages[0].Groups[0].Fields[2].Validation[0]).Field, Is.EqualTo(fields["email"]), "a check");
            Assert.That(definition.Settings.Submission.Verification!.EmailField, Is.EqualTo(fields["email"]), "the verification field");
            Assert.That(definition.Actions[0].Settings!.TryGetProperty("replyTo", out var replyTo) ? replyTo.GetString() : null, Is.EqualTo(fields["email"].ToString("D")), "an action's setting");
            Assert.That(definition.Pages[0].Groups[0].Fields[3].Options!.OptionList, Is.EqualTo(ListKey), "an option list of the library stays");
            Assert.That(Hash(Remapped(definition, designed)), Is.EqualTo(Hash(designed)), "nothing else changed");
            Assert.That((await Service.GetDraftAsync(source.Key))!.Definition, Is.Not.Null.And.Property(nameof(FormDefinition.Key)).EqualTo(source.Key), "the source stays as it was");
            Assert.That(await Service.PublishAsync(copy.Key, copy.ConcurrencyVersion, null, Editor), Has.Property(nameof(FormVersionResult.Version)).EqualTo(2), "the copy publishes on its own");
        });
    }

    [Test]
    public async Task ADuplicate_WithoutActions_HasNone_AndKeepsNoAccessOfItsSource()
    {
        var (source, _) = await DesignedAsync();
        var row = await RowAsync(source);
        var restricted = (await Forms.UpdateAsync(new FormUpdate(row.Id, row.ConcurrencyVersion, row.Alias, row.Name, row.FolderId, IsRestricted: true, Editor)))!;

        var copy = (await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Contact", IncludeActions: false, UserKey: Editor)))!;
        var definition = (await Service.GetDraftAsync(copy.Key))!.Definition!;

        Assert.Multiple(() =>
        {
            Assert.That(restricted.IsRestricted, Is.True);
            Assert.That(definition.Actions, Is.Empty);
            Assert.That(DefinitionKeys.Fields(definition).Select(field => field.Alias), Is.EqualTo(new[] { "email", "topic", "confirmEmail", "session" }));
            Assert.That((copy.Alias, copy.IsRestricted, copy.FolderKey), Is.EqualTo(("contact2", false, (Guid?)null)), "the copy takes its folder's access, as a new form does");
        });
    }

    [Test]
    public async Task ADuplicate_IsAnnouncedAsANewFormFromItsSource_AndAudited()
    {
        var (source, _) = await DesignedAsync();
        var before = Notifications.Seen.Count;

        var copy = (await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", IncludeActions: true, UserKey: Editor)))!;
        var seen = Notifications.Seen.Skip(before).ToList();
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = copy.Key }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormSavingNotification), typeof(FormSavedNotification) }));
            var saving = (FormSavingNotification)seen[0];
            var saved = (FormSavedNotification)seen[1];
            Assert.That((saving.FormKey, saving.Alias, saving.IsNew, saving.CreatedFrom, saving.SourceKey, saving.ActionsCopiedFrom), Is.EqualTo((copy.Key, "copy", true, (FormSource?)FormSource.Duplicate, (Guid?)source.Key, (Guid?)null)));
            Assert.That((saved.FormKey, saved.Version, saved.IsNew, saved.CreatedFrom, saved.SourceKey), Is.EqualTo((copy.Key, 1, true, (FormSource?)FormSource.Duplicate, (Guid?)source.Key)));
            Assert.That(Notifications.AnyInUnitOfWork, Is.False);
            Assert.That((entry.Action, entry.UserKey, entry.Details), Is.EqualTo((FormBuilderFormExchangeService.DuplicatedAction, (Guid?)Editor, $$"""{"alias":"copy","version":1,"source":"{{source.Key}}","sourceVersion":4,"actions":true}""")));
        });
    }

    [Test]
    public async Task ADuplicate_IsRefused_ForATrashedForm_ATakenAlias_AMissingFolder_OrAHandler_WritingNothing()
    {
        var (source, _) = await DesignedAsync();
        var trashed = await Service.CreateAsync(new NewFormRequest("Old", UserKey: Editor));
        var row = await RowAsync(trashed);
        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var forms = Count(FormBuilderTables.Form);

        Assert.Multiple(async () =>
        {
            Assert.That(await Exchange.DuplicateAsync(Guid.NewGuid(), new DuplicateFormRequest("Copy", true)), Is.Null, "a form that does not exist");
            AssertCode(async () => await Exchange.DuplicateAsync(trashed.Key, new DuplicateFormRequest("Copy", true)), "trashed");
            AssertCode(async () => await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", true, Alias: "contact")), "alias_taken");
            AssertCode(async () => await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", true, Alias: "Not valid")), "alias_invalid", "alias");
            AssertCode(async () => await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest(" ", true)), "invalid_value", "name");
            AssertCode(async () => await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", true, FolderKey: Guid.NewGuid())), "folder_missing");
            Notifications.Cancel = true;
            AssertCode(async () => await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", true)), "cancelled");
            Notifications.Cancel = false;
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(forms), "no form was written");
        });
    }

    [Test]
    public async Task ADuplicate_OfADraftThatDoesNotRead_IsRefusedWithItsProblems()
    {
        var (source, _) = await DesignedAsync();
        var row = await RowAsync(source);
        await Forms.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Document("""{"schemaVersion":1,"key":"x"}"""), null, Editor);

        AssertCode(async () => await Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", true)), "invalid_value", "$.key");
    }

    [Test]
    public async Task CopiedActions_MapTheFieldsTheyName_ByAlias_AndReportTheRest()
    {
        var (source, _) = await DesignedAsync();
        var target = await Service.CreateAsync(new NewFormRequest("Newsletter", UserKey: Editor));
        var email = Email with { Key = Guid.NewGuid(), Alias = "eMail" };
        var saved = await SaveAsync(target, WithFields(await DraftAsync(target), email));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var before = Notifications.Seen.Count;

        var copied = (await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, target.Key, saved.Form.ConcurrencyVersion, UserKey: Editor)))!;
        var draft = (await Service.GetDraftAsync(target.Key))!;
        var notify = draft.Definition!.Actions[0];
        var seen = Notifications.Seen.Skip(before).ToList();
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = target.Key, Action = FormBuilderFormExchangeService.ActionsCopiedAction }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(copied.Target.Version, Is.EqualTo(3), "a draft save of the target");
            Assert.That(copied.Target.Form, Is.EqualTo(saved.Form with { ConcurrencyVersion = saved.Form.ConcurrencyVersion + 1, UpdatedUtc = Now.AddMinutes(1) }));
            Assert.That(draft.Definition.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Notify sales", "CRM" }), "all the source's actions, in its order");
            Assert.That(copied.Actions.Select(action => (action.SourceKey, action.Key)), Is.EqualTo(new[] { (Notify.Key, notify.Key), (Hook.Key, draft.Definition.Actions[1].Key) }));
            Assert.That(notify.Key, Is.Not.EqualTo(Notify.Key));
            Assert.That(notify.Settings!.TryGetProperty("replyTo", out var replyTo) ? replyTo.GetString() : null, Is.EqualTo(email.Key.ToString("D")), "email maps to eMail");
            Assert.That(copied.Actions[0].Unmapped, Is.EqualTo(new[] { new UnmappedReference("$.actions[0].rules[0].conditions[0].field", Topic.Key, "topic") }), "the target has no topic");
            Assert.That(Condition(notify.Rules[0]).Field, Is.EqualTo(Topic.Key), "kept until the editor fixes it");
            Assert.That(copied.Actions[1].Unmapped, Is.Empty);
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormSavingNotification), typeof(FormSavedNotification) }));
            Assert.That(seen.OfType<FormSavingNotification>().Single() is { IsNew: false, ActionsCopiedFrom: { } from, CreatedFrom: null } && from == source.Key, Is.True);
            Assert.That(seen.OfType<FormSavedNotification>().Single() is { IsNew: false, Version: 3, ActionsCopiedFrom: { } to } && to == source.Key, Is.True);
            Assert.That((entry.UserKey, entry.Details), Is.EqualTo(((Guid?)Editor, $$"""{"version":3,"source":"{{source.Key}}","actions":2,"unmapped":1}""")));
        });

        AssertCode(async () => await Service.PublishAsync(target.Key, copied.Target.Form.ConcurrencyVersion, null, Editor), "unknown_field", "$.actions[0].rules[0].conditions[0].field");
    }

    [Test]
    public async Task CopiedActions_ThatMapEveryReference_Publish_AndChosenActionsComeInTheSourcesOrder()
    {
        var (source, _) = await DesignedAsync();
        var target = await Service.CreateAsync(new NewFormRequest("Newsletter", UserKey: Editor));
        var saved = await SaveAsync(target, WithFields(await DraftAsync(target), Email with { Key = Guid.NewGuid() }, Topic with { Key = Guid.NewGuid(), Rules = [] }));

        var copied = (await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, target.Key, saved.Form.ConcurrencyVersion, [Hook.Key, Notify.Key], Editor)))!;

        Assert.Multiple(async () =>
        {
            Assert.That(copied.Actions.Select(action => action.SourceKey), Is.EqualTo(new[] { Notify.Key, Hook.Key }), "in the source's order, whatever the request's");
            Assert.That(copied.Actions.SelectMany(action => action.Unmapped), Is.Empty);
            Assert.That(await Service.PublishAsync(target.Key, copied.Target.Form.ConcurrencyVersion, null, Editor), Has.Property(nameof(FormVersionResult.Version)).EqualTo(4));
        });
    }

    [Test]
    public async Task CopyingActions_IsRefused_ForAStaleOrTrashedTarget_ATrashedSource_OrAnUnknownAction_AndNothingToCopyWritesNothing()
    {
        var (source, _) = await DesignedAsync();
        var target = await Service.CreateAsync(new NewFormRequest("Newsletter", UserKey: Editor));
        var empty = await Service.CreateAsync(new NewFormRequest("Empty", UserKey: Editor));
        var trashed = await Service.CreateAsync(new NewFormRequest("Old", UserKey: Editor));
        var row = await RowAsync(trashed);
        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        trashed = (await Service.GetAsync(trashed.Key))!;
        var versions = Count(FormBuilderTables.FormVersion);
        var audited = Count(FormBuilderTables.AuditEntry);
        var before = Notifications.Seen.Count;

        Assert.Multiple(async () =>
        {
            Assert.That(await Exchange.CopyActionsAsync(new CopyActionsRequest(Guid.NewGuid(), target.Key, target.ConcurrencyVersion)), Is.Null, "no source");
            Assert.That(await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, Guid.NewGuid(), 1)), Is.Null, "no target");
            AssertCode(async () => await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, target.Key, target.ConcurrencyVersion - 1)), "concurrency_conflict");
            AssertCode(async () => await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, trashed.Key, trashed.ConcurrencyVersion)), "trashed");
            AssertCode(async () => await Exchange.CopyActionsAsync(new CopyActionsRequest(trashed.Key, target.Key, target.ConcurrencyVersion)), "trashed");
            AssertCode(async () => await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, target.Key, target.ConcurrencyVersion, [Notify.Key, Guid.NewGuid()])), "invalid_value", "actionKeys[1]");
            var none = (await Exchange.CopyActionsAsync(new CopyActionsRequest(empty.Key, target.Key, target.ConcurrencyVersion)))!;
            var chosenNone = (await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, target.Key, target.ConcurrencyVersion, [])))!;
            Assert.That((none.Target, none.Actions), Is.EqualTo((new FormVersionResult(target, null), (IReadOnlyList<CopiedAction>)[])).Using<(FormVersionResult, IReadOnlyList<CopiedAction>)>((x, y) => x.Item1 == y.Item1 && x.Item2.Count == y.Item2.Count), "a source without actions");
            Assert.That(chosenNone.Target, Is.EqualTo(new FormVersionResult(target, null)), "an empty choice");
            Assert.That(Notifications.Seen.Skip(before), Is.Empty, "a refusal, and nothing to copy, announce nothing");
            Notifications.Cancel = true;
            AssertCode(async () => await Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, target.Key, target.ConcurrencyVersion)), "cancelled");
            Notifications.Cancel = false;
            Assert.That((Count(FormBuilderTables.FormVersion), Count(FormBuilderTables.AuditEntry)), Is.EqualTo((versions, audited)), "nothing written");
            Assert.That(Notifications.Seen.Skip(before).OfType<FormSavedNotification>(), Is.Empty);
        });
    }

    [Test]
    public async Task AFormFromTheContactTemplate_HasItsFields_WithNewKeys_AndPublishes()
    {
        var folder = await CreateFolderAsync();
        var before = Notifications.Seen.Count;

        var created = (await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(BuiltInFormTemplates.ContactKey, FolderKey: folder.Key, UserKey: Editor)))!;
        var definition = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var template = FormExportFormat.Read(BuiltInFormTemplates.Find(BuiltInFormTemplates.ContactKey)!.Envelope, DefinitionFixtures()).Envelope!.Definition;
        var seen = Notifications.Seen.Skip(before).ToList();
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = created.Key }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(async () =>
        {
            Assert.That((created.Name, created.Alias, created.FolderKey, created.IsPublished), Is.EqualTo(("Contact", "contact", (Guid?)folder.Key, false)), "the template's name");
            Assert.That(DefinitionKeys.Fields(definition).Select(field => (field.Alias, field.Type, field.Required)), Is.EqualTo(new[] { ("name", "text", true), ("email", "email", true), ("message", "textarea", true) }));
            Assert.That(DefinitionKeys.Of(definition).Intersect(DefinitionKeys.Of(template)), Is.Empty, "new keys, so every form from it is its own");
            Assert.That(definition.Key, Is.EqualTo(created.Key));
            Assert.That(DefinitionKeys.Fields(definition).Select(field => field.Label!.Resolve("de")), Is.EqualTo(new[] { "Name", "E-Mail-Adresse", "Nachricht" }));
            Assert.That(seen.OfType<FormSavingNotification>().Single() is { IsNew: true, CreatedFrom: FormSource.Template, SourceKey: { } key } && key == BuiltInFormTemplates.ContactKey, Is.True);
            Assert.That(seen.OfType<FormSavedNotification>().Single() is { IsNew: true, CreatedFrom: FormSource.Template, Version: 1 }, Is.True);
            Assert.That((entry.Action, entry.Details), Is.EqualTo((FormBuilderFormExchangeService.CreatedFromTemplateAction, $$"""{"alias":"contact","version":1,"folder":"{{folder.Key}}","template":"{{BuiltInFormTemplates.ContactKey}}","builtIn":true}""")));
            Assert.That(await Service.PublishAsync(created.Key, created.ConcurrencyVersion, null, Editor), Has.Property(nameof(FormVersionResult.Version)).EqualTo(2));
        });

        var second = (await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(BuiltInFormTemplates.ContactKey, "Kontakt", UserKey: Editor)))!;
        Assert.That((second.Name, second.Alias), Is.EqualTo(("Kontakt", "kontakt")), "a name the request gives");
    }

    [Test]
    public async Task AFormFromATemplate_GetsTheConsentDefault_AtTheEndOfItsLastGroup_UnlessItHasConsentAlready()
    {
        var consent = new NewFormConsent { Label = new Dictionary<string, string> { [string.Empty] = "Privacy" }, Text = new Dictionary<string, string> { [string.Empty] = "<p>I agree.</p>" } };
        await SettingsService.SaveAsync(new GeneralSettings { NewForms = new NewFormDefaults { Consent = consent } }, 1, Editor);
        var withConsent = await SavedTemplateAsync(FormTemplateKind.Form, field: new FormField { Key = Guid.NewGuid(), Type = "consent", Alias = "agree", Label = new LocalizedText("Agree"), Required = true });
        var withAlias = await SavedTemplateAsync(FormTemplateKind.Form, field: new FormField { Key = Guid.NewGuid(), Type = "checkbox", Alias = "Consent", Label = new LocalizedText("Consent") });

        var contact = (await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(BuiltInFormTemplates.ContactKey, UserKey: Editor)))!;
        var own = (await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(withConsent.Key, UserKey: Editor)))!;
        var aliased = (await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(withAlias.Key, UserKey: Editor)))!;

        Assert.Multiple(async () =>
        {
            Assert.That(await AliasesAsync(contact), Is.EqualTo(new[] { "name", "email", "message", "consent" }), "the consent field goes last");
            Assert.That(await AliasesAsync(own), Is.EqualTo(new[] { "email", "agree" }), "a consent field of its own");
            Assert.That(await AliasesAsync(aliased), Is.EqualTo(new[] { "email", "Consent" }), "a field with the alias in another letter case");
            Assert.That(await Service.PublishAsync(contact.Key, contact.ConcurrencyVersion, null, Editor), Has.Property(nameof(FormVersionResult.Version)).EqualTo(2), "it publishes with the consent field");
        });
    }

    [Test]
    public async Task ASavedFormTemplate_CreatesAForm_AndAGroupTemplateOrAnUnknownKey_GivesNull()
    {
        var saved = await SavedTemplateAsync(FormTemplateKind.Form);
        var group = await SavedTemplateAsync(FormTemplateKind.Group);

        var created = (await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(saved.Key, UserKey: Editor)))!;
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = created.Key }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(async () =>
        {
            Assert.That((created.Name, created.Alias), Is.EqualTo(("Saved template", "savedTemplate")));
            Assert.That(await AliasesAsync(created), Is.EqualTo(new[] { "email" }));
            Assert.That(entry.Details, Is.EqualTo($$"""{"alias":"savedTemplate","version":1,"template":"{{saved.Key}}","builtIn":false}"""));
            Assert.That(await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(group.Key)), Is.Null, "a group template makes no form");
            Assert.That(await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(Guid.NewGuid())), Is.Null);
            AssertCode(async () => await Exchange.CreateFromTemplateAsync(new TemplateFormRequest(saved.Key, Alias: "savedTemplate")), "alias_taken");
        });
    }

    [Test]
    public async Task AnExport_ImportsAsANewForm_WithNewKeys_AndTheSameDefinition()
    {
        await CreateListAsync(ListKey, "sessions");
        var (source, designed) = await DesignedAsync();
        var folder = await CreateFolderAsync();
        var file = (await Exchange.ExportAsync(source.Key))!;
        var before = Notifications.Seen.Count;

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.Json, FolderKey: folder.Key, UserKey: Editor));
        var definition = (await Service.GetDraftAsync(imported.Form.Key))!.Definition!;
        var seen = Notifications.Seen.Skip(before).ToList();
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = imported.Form.Key }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That((imported.Form.Name, imported.Form.Alias, imported.Form.FolderKey, imported.Form.IsPublished), Is.EqualTo(("Contact", "contact2", (Guid?)folder.Key, false)), "the file's name and alias, with the next free number");
            Assert.That(imported.Notes, Is.Empty, "everything came along");
            Assert.That(DefinitionKeys.Of(definition).Intersect(DefinitionKeys.Of(designed)), Is.Empty, "new keys");
            Assert.That(Hash(Remapped(definition, designed)), Is.EqualTo(Hash(designed)), "the same definition otherwise");
            Assert.That(seen.OfType<FormSavingNotification>().Single() is { IsNew: true, CreatedFrom: FormSource.Import, SourceKey: { } key } && key == source.Key, Is.True);
            Assert.That(seen.OfType<FormSavedNotification>().Single() is { CreatedFrom: FormSource.Import, Version: 1 }, Is.True);
            Assert.That((entry.Action, entry.Details), Is.EqualTo((FormBuilderFormExchangeService.ImportedAction, $$"""{"alias":"contact2","version":1,"folder":"{{folder.Key}}","source":"{{source.Key}}","sourceAlias":"contact","packageVersion":"{{FormBuilderVersion.Package}}","notes":[],"noteCounts":[]}""")));
        });

        var named = await Exchange.ImportAsync(new ImportFormRequest(file.Json, "Kontakt", "kontaktformular", UserKey: Editor));
        Assert.That((named.Form.Name, named.Form.Alias), Is.EqualTo(("Kontakt", "kontaktformular")), "a name and an alias the request gives");
    }

    [Test]
    public async Task AnExport_HoldsTheDraftOrAVersion_WithTheOptionListsItNames_AndThePackageVersion()
    {
        var list = await CreateListAsync(ListKey, "sessions");
        var (source, designed) = await DesignedAsync();
        Clock.Advance(TimeSpan.FromMinutes(5));

        var draft = (await Exchange.ExportAsync(source.Key))!;
        var version = (await Exchange.ExportAsync(source.Key, 3))!;
        using var document = JsonDocument.Parse(draft.Json);
        var root = document.RootElement;
        var read = FormExportFormat.Read(version.Json, DefinitionFixtures()).Envelope!;

        Assert.Multiple(async () =>
        {
            Assert.That(draft.FileName, Is.EqualTo("contact.json"));
            Assert.That(root.GetProperty("format").GetString(), Is.EqualTo(FormExportFormat.Format));
            Assert.That(root.GetProperty("packageVersion").GetString(), Is.EqualTo(FormBuilderVersion.Package));
            Assert.That(root.GetProperty("exportedUtc").GetDateTimeOffset(), Is.EqualTo(Now.AddMinutes(5)));
            Assert.That(root.GetProperty("form").ToString(), Is.EqualTo($$"""{"key":"{{source.Key}}","alias":"contact","name":"Contact"}""").Using<string>((x, y) => JsonNode.DeepEquals(JsonNode.Parse(x), JsonNode.Parse(y)) ? 0 : 1));
            Assert.That(Hash(FormExportFormat.Read(draft.Json, DefinitionFixtures()).Envelope!.Definition), Is.EqualTo(Hash(designed)), "the draft");
            Assert.That(DefinitionKeys.Fields(read.Definition).Select(field => field.Alias), Is.EqualTo(new[] { "email", "topic", "confirmEmail" }), "the published version 3, without the field added since");
            Assert.That(root.GetProperty("optionLists").EnumerateArray().Select(item => (item.GetProperty("key").GetGuid(), item.GetProperty("alias").GetString(), item.GetProperty("kind").GetString(), item.GetProperty("cacheSeconds").GetInt32())), Is.EqualTo(new[] { (list.Key, "sessions", "Manual", 0) }));
            Assert.That(read.OptionLists, Is.Empty, "version 3 names no list");
            Assert.That(await Exchange.ExportAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await Exchange.ExportAsync(source.Key, 99), Is.Null);
        });

        await OptionLists.DeleteAsync(list.Id, list.ConcurrencyVersion);
        var row = await RowAsync(source);
        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var withoutList = (await Exchange.ExportAsync(source.Key))!;
        Assert.That(JsonDocument.Parse(withoutList.Json).RootElement.GetProperty("optionLists").GetArrayLength(), Is.Zero, "a list the site lacks is left out, and a form in the trash exports");
    }

    [Test]
    public async Task AnImport_ClearsAThemeTheSiteLacks_AndKeepsOneItHas()
    {
        await Themes.CreateAsync(new NewTheme(Guid.NewGuid(), "brand", "Brand", "default"));
        var (source, designed) = await DesignedAsync();
        var file = (await Exchange.ExportAsync(source.Key))!;

        var kept = await Exchange.ImportAsync(new ImportFormRequest(Themed(file.Json, "brand"), UserKey: Editor));
        var cleared = await Exchange.ImportAsync(new ImportFormRequest(Themed(file.Json, "gone"), UserKey: Editor));
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = cleared.Form.Key }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(async () =>
        {
            Assert.That((await Service.GetDraftAsync(kept.Form.Key))!.Definition!.Settings.Appearance.Theme, Is.EqualTo("brand"));
            Assert.That(kept.Notes.Where(note => note.Code == FormImportNotes.ThemeMissing), Is.Empty);
            Assert.That((await Service.GetDraftAsync(cleared.Form.Key))!.Definition!.Settings.Appearance.Theme, Is.Null, "the default theme");
            Assert.That(cleared.Notes[0], Is.EqualTo(new FormImportNote(FormImportNotes.ThemeMissing, "$.definition.settings.appearance.theme", "gone")));
            Assert.That(designed.Settings.Appearance.Theme, Is.Null);
            Assert.That(entry.Details, Does.EndWith(""","notes":["theme_missing","option_list_missing"],"noteCounts":[1,1]}"""), "the audit entry names the notes: the theme, and the session list this site lacks");
        });
    }

    [Test]
    public async Task AnImport_UsesTheSitesOptionList_ByKey_OrByAliasWithTheSameOptions_AndReportsTheRest()
    {
        var byKey = Guid.Parse("72000000-0000-4000-8000-000000000011");
        var sameAlias = Guid.Parse("72000000-0000-4000-8000-000000000012");
        var otherItems = Guid.Parse("72000000-0000-4000-8000-000000000013");
        var notCarried = Guid.Parse("72000000-0000-4000-8000-000000000014");
        var otherKind = Guid.Parse("72000000-0000-4000-8000-000000000015");
        var otherConfig = Guid.Parse("72000000-0000-4000-8000-000000000016");
        var inChild = Guid.Parse("72000000-0000-4000-8000-000000000017");
        await CreateListAsync(byKey, "rooms");
        var here = await CreateListAsync(Guid.NewGuid(), "SESSIONS", """[{"value":"a"}]""");
        await CreateListAsync(Guid.NewGuid(), "meals");
        await CreateListAsync(Guid.NewGuid(), "kinds");
        await CreateListAsync(Guid.NewGuid(), "configs");
        var created = await Service.CreateAsync(new NewFormRequest("Event", UserKey: Editor));
        FormField Choice(Guid list, string alias) => Session with { Key = Guid.NewGuid(), Alias = alias, Options = new FieldOptions { Source = OptionSource.List, OptionList = list } };
        var fields = new[] { (byKey, "room"), (sameAlias, "session"), (otherItems, "meal"), (notCarried, "extra"), (byKey, "room2"), (notCarried, "extra2"), (otherKind, "kind"), (otherConfig, "config") }
            .Select(pair => Choice(pair.Item1, pair.Item2))
            .Append(new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "guests", Label = new LocalizedText("Guests"), Fields = [Choice(inChild, "guestSession")] })
            .ToArray();
        await SaveAsync(created, WithFields(await DraftAsync(created), fields));
        var envelope = JsonNode.Parse((await Exchange.ExportAsync(created.Key))!.Json)!.AsObject();
        var exported = envelope["optionLists"]!.AsArray().Select(list => list!["key"]!.GetValue<string>()).ToList();
        envelope["optionLists"] = new JsonArray(
            ListJson(byKey, "rooms", "[]"),
            ListJson(sameAlias, "sessions", """[{"value":"a"}]"""),
            ListJson(otherItems, "meals", """[{"value":"b"}]"""),
            ListJson(otherKind, "kinds", "[]", kind: "Countries"),
            ListJson(otherConfig, "configs", "[]", config: """{"sort":"label"}"""));

        var imported = await Exchange.ImportAsync(new ImportFormRequest(envelope.ToJsonString(), UserKey: Editor));
        var lists = DefinitionKeys.Fields((await Service.GetDraftAsync(imported.Form.Key))!.Definition!).Select(field => field.Options?.OptionList).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(exported, Is.EqualTo(new[] { byKey.ToString("D") }), "the export carries the one list the site has, once for the two fields that name it");
            Assert.That(lists, Is.EqualTo(new Guid?[] { byKey, here.Key, otherItems, notCarried, byKey, notCarried, otherKind, otherConfig, null, inChild }), "by key; moved to the same list by alias; kept");
            Assert.That(imported.Notes, Is.EqualTo(new[]
            {
                new FormImportNote(FormImportNotes.OptionListReused, "$.definition.pages[0].groups[0].fields[1].options.list", "SESSIONS"),
                new FormImportNote(FormImportNotes.OptionListMissing, "$.definition.pages[0].groups[0].fields[2].options.list", "meals"),
                new FormImportNote(FormImportNotes.OptionListMissing, "$.definition.pages[0].groups[0].fields[3].options.list", null),
                new FormImportNote(FormImportNotes.OptionListMissing, "$.definition.pages[0].groups[0].fields[6].options.list", "kinds"),
                new FormImportNote(FormImportNotes.OptionListMissing, "$.definition.pages[0].groups[0].fields[7].options.list", "configs"),
                new FormImportNote(FormImportNotes.OptionListMissing, "$.definition.pages[0].groups[0].fields[8].fields[0].options.list", null),
            }), "once per list, at the first field that names it; another kind or configuration is another list");
        });
    }

    [Test]
    public async Task AnImportWithMoreNotesThanAnAuditEntryLists_NamesEachCodeOnce_WithItsCount()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Many", UserKey: Editor));
        var fields = Enumerable.Range(1, AuditDetails.MaxListLength + 1)
            .Select(i => Session with { Key = Guid.NewGuid(), Alias = "choice" + i.ToString(CultureInfo.InvariantCulture), Options = new FieldOptions { Source = OptionSource.List, OptionList = Guid.NewGuid() } })
            .ToArray();
        await SaveAsync(created, WithFields(await DraftAsync(created), fields));
        var file = (await Exchange.ExportAsync(created.Key))!;

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file.Json, UserKey: Editor));
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = imported.Form.Key }, PageRequest.First(10))).Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(imported.Notes, Has.Count.EqualTo(AuditDetails.MaxListLength + 1), "a note for each list this site lacks");
            Assert.That(entry.Details, Does.EndWith($$""","notes":["option_list_missing"],"noteCounts":[{{AuditDetails.MaxListLength + 1}}]}"""));
        });
    }

    [Test]
    public async Task AReimportOfTheLongestAlias_GetsItsNumberWithinTheLongestAlias()
    {
        var longest = "a" + new string('b', DefinitionStructureChecks.MaxAliasLength - 2) + "c";
        var created = await Service.CreateAsync(new NewFormRequest("Long", longest, UserKey: Editor));
        var file = (await Exchange.ExportAsync(created.Key))!;

        var first = await Exchange.ImportAsync(new ImportFormRequest(file.Json, UserKey: Editor));
        var second = await Exchange.ImportAsync(new ImportFormRequest(file.Json, UserKey: Editor));

        Assert.Multiple(() =>
        {
            Assert.That(first.Form.Alias, Is.EqualTo(longest[..^1] + "2"), "cut by one for its number");
            Assert.That(second.Form.Alias, Is.EqualTo(longest[..^1] + "3"));
            Assert.That(second.Form.Alias, Has.Length.EqualTo(DefinitionStructureChecks.MaxAliasLength));
        });
    }

    [Test]
    public async Task AnImport_IsRefused_WithTheFilesProblems_ANameOfTheFileThatIsNone_OrAHandler()
    {
        var (source, _) = await DesignedAsync();
        var file = (await Exchange.ExportAsync(source.Key))!;
        var envelope = JsonNode.Parse(file.Json)!.AsObject();
        var forms = Count(FormBuilderTables.Form);

        Assert.Multiple(async () =>
        {
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest("{}")), "missing_property", "$.format");
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(file.Json.Replace("jfb-form-export/1", "jfb-form-export/2", StringComparison.Ordinal))), "unsupported_format", "$.format");
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(Changed(envelope, e => e["definition"]!["pages"]![0]!["colour"] = "red"))), "unknown_property", "$.definition.pages[0].colour");
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(Changed(envelope, e => e["form"]!["name"] = "  "))), "invalid_value", "$.form.name");
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(file.Json, Name: " ")), "invalid_value", "name");
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(file.Json, FolderKey: Guid.NewGuid())), "folder_missing");
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(file.Json, Alias: "contact")), "alias_taken");
            Notifications.Cancel = true;
            AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(file.Json)), "cancelled");
            Notifications.Cancel = false;
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(forms), "nothing written");
            var renamed = await Exchange.ImportAsync(new ImportFormRequest(Changed(envelope, e => e["form"]!["alias"] = "Not an alias"), UserKey: Editor));
            Assert.That(renamed.Form.Alias, Is.EqualTo("contact2"), "an alias that is not valid gives way to one from the name");
        });
    }

    [Test]
    public async Task EveryOperation_InsideAUnitOfWork_IsRefused()
    {
        var (source, _) = await DesignedAsync();
        var file = (await Exchange.ExportAsync(source.Key))!;
        var changes = new (string Name, Func<Task> Change)[]
        {
            ("duplicate", () => Exchange.DuplicateAsync(source.Key, new DuplicateFormRequest("Copy", true))),
            ("copy actions", () => Exchange.CopyActionsAsync(new CopyActionsRequest(source.Key, source.Key, source.ConcurrencyVersion))),
            ("template", () => Exchange.CreateFromTemplateAsync(new TemplateFormRequest(BuiltInFormTemplates.ContactKey))),
            ("import", () => Exchange.ImportAsync(new ImportFormRequest(file.Json))),
        };

        Assert.Multiple(() =>
        {
            foreach (var (name, change) in changes)
            {
                Assert.That(() => Database.WriteAsync(async _ => { await change(); return true; }), Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"), name);
                using (ScopeProvider.CreateScope(autoComplete: true))
                {
                    Assert.That(() => change(), Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"), name + " in a scope");
                }
            }

            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ABlankForm_IsAnnouncedAsBlank()
    {
        var before = Notifications.Seen.Count;

        await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));

        Assert.That(Notifications.Seen.Skip(before).Select(notification => notification switch
        {
            FormSavingNotification saving => (saving.CreatedFrom, saving.SourceKey),
            FormSavedNotification saved => (saved.CreatedFrom, saved.SourceKey),
            _ => default,
        }), Is.EqualTo(new[] { ((FormSource?)FormSource.Blank, (Guid?)null), (FormSource.Blank, null) }));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<FormNotificationLog>();
        builder.AddNotificationAsyncHandler<FormSavingNotification, FormNotificationRecorder<FormSavingNotification>>();
        builder.AddNotificationAsyncHandler<FormSavedNotification, FormNotificationRecorder<FormSavedNotification>>();
    }

    private static Guid Key(int number) => Guid.Parse($"75000000-0000-4000-8000-{number:D12}");

    private static RuleCondition Condition(FormRule rule) => (RuleCondition)rule.Conditions[0];

    private static RuleCondition Condition(FormValidationRule rule) => (RuleCondition)rule.Conditions[0];

    private static FormDefinitionSerializer DefinitionFixtures() => new(new Core.Definitions.Upgrades.FormDefinitionUpgrades([]), new DefinitionLimits());

    // The copy with the source's keys put back where the copy has its own, which the two share in document order.
    private static FormDefinition Remapped(FormDefinition copy, FormDefinition source)
    {
        var map = DefinitionKeys.Of(copy).Zip(DefinitionKeys.Of(source)).ToDictionary(pair => pair.First, pair => pair.Second);
        return DefinitionKeys.Remap(copy, map, DefinitionFixtures());
    }

    private static string Themed(string json, string theme)
    {
        var envelope = JsonNode.Parse(json)!.AsObject();
        var settings = envelope["definition"]!["settings"]!.AsObject();
        settings["appearance"]!["theme"] = theme;
        return envelope.ToJsonString();
    }

    private static string Changed(JsonObject envelope, Action<JsonObject> change)
    {
        var copy = envelope.DeepClone().AsObject();
        change(copy);
        return copy.ToJsonString();
    }

    private static JsonObject ListJson(Guid key, string alias, string items, string kind = "Manual", string config = "{}") => new()
    {
        ["key"] = key.ToString("D"),
        ["alias"] = alias,
        ["name"] = alias,
        ["kind"] = kind,
        ["config"] = JsonNode.Parse(config),
        ["items"] = JsonNode.Parse(items),
        ["cacheSeconds"] = 0,
    };

    // A form "Contact" published with three fields and both actions, then saved with a fourth field: every save and
    // publish adds a version, so version 3 is the published one and version 4 the draft. Its draft definition comes with it.
    private async Task<(FormInfo Form, FormDefinition Draft)> DesignedAsync()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        var blank = await DraftAsync(created);
        var published = blank with
        {
            Settings = blank.Settings with { Submission = blank.Settings.Submission with { Verification = new EmailVerification { EmailField = Email.Key } } },
            Pages = [blank.Pages[0] with { Groups = [blank.Pages[0].Groups[0] with { Fields = [Email, Topic, Confirm] }] }],
            ValidationRules = [new FormValidationRule { Key = Key(9), Field = Confirm.Key, Message = new LocalizedText("Fill in both."), Conditions = [new RuleCondition { Field = Email.Key, Operator = "isEmpty" }] }],
            Actions = [Notify, Hook],
        };
        var saved = await SaveAsync(created, published);
        var live = (await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor))!;
        Assert.That(live.Version, Is.EqualTo(3), "the designed form publishes");
        var draft = published with { Pages = [published.Pages[0] with { Groups = [published.Pages[0].Groups[0] with { Fields = [Email, Topic, Confirm, Session] }] }] };
        var current = await SaveAsync(live.Form, draft);
        return (current.Form, draft);
    }

    private async Task<FormDefinition> DraftAsync(FormInfo form) => (await Service.GetDraftAsync(form.Key))!.Definition!;

    private async Task<FormVersionResult> SaveAsync(FormInfo form, FormDefinition definition) =>
        (await Service.SaveDraftAsync(form.Key, form.ConcurrencyVersion, definition, null, Editor))!;

    private async Task<StoredForm> RowAsync(FormInfo form) => (await Forms.GetAsync(form.Key))!;

    private async Task<IReadOnlyList<string?>> AliasesAsync(FormInfo form) =>
        DefinitionKeys.Fields((await Service.GetDraftAsync(form.Key))!.Definition!).Select(field => field.Alias).ToList();

    private Task<StoredOptionList> CreateListAsync(Guid key, string alias, string items = "[]") =>
        OptionLists.CreateAsync(new NewOptionList(key, alias, alias, "Manual", Items: items));

    // A saved template of one group with an email field, and one more field when given.
    private async Task<StoredFormTemplate> SavedTemplateAsync(FormTemplateKind kind, FormField? field = null)
    {
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages = [new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = field is null ? [Email] : [Email, field] }] }],
            Outcomes = [new FormOutcome { Key = Guid.NewGuid() }],
        };
        return await Templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), kind, "Saved template", FormDefinitionSerializer.WriteString(definition), UserKey: Editor));
    }
}
