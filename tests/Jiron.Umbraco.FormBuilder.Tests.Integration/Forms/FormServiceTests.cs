using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// The form service in a real site (section 4.5, ADR-0044): a form from blank with the new-form defaults, aliases from
/// names, draft saves, publishes with the field catalogue and the public revision, unpublish, rename and move; the
/// refusals with their codes, the trash, the notifications before and after each change, and the audit entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormServiceTests : RepositoryTestBase
{
    private static readonly FormField Email = new() { Key = Key(1), Type = "email", Alias = "email", Label = new LocalizedText("Email address") };
    private static readonly FormField GuestName = new() { Key = Key(2), Type = "text", Alias = "guestName", Label = new LocalizedText("Name") };
    private static readonly FormField Guests = new() { Key = Key(3), Type = "repeater", Alias = "guests", Label = new LocalizedText("Guests"), Fields = [GuestName] };
    private static readonly FormField Age = new() { Key = Key(4), Type = "number", Alias = "age", Label = new LocalizedText("Age") };

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private IFormBuilderSettingsService SettingsService => GetRequiredService<IFormBuilderSettingsService>();

    private FormNotificationLog Notifications => GetRequiredService<FormNotificationLog>();

    private TestPublishCheck Check => GetRequiredService<TestPublishCheck>();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    [Test]
    public async Task ACreate_FromBlank_HasOnePageGroupAndMessage_AsItsFirstDraft()
    {
        var created = await CreateAsync("  Contact us  ");
        var draft = (await Service.GetDraftAsync(created.Key))!;
        var row = await RowAsync(created);

        Assert.Multiple(async () =>
        {
            Assert.That(created, Is.EqualTo(new FormInfo(row.Key, "contactUs", "Contact us", null, row.ConcurrencyVersion, false, 0, false, null, Now, Now, Editor, Editor)));
            Assert.That(await Service.GetAsync(created.Key), Is.EqualTo(created));
            Assert.That(await Service.GetByAliasAsync("CONTACTUS"), Is.EqualTo(created), "in any letter case");
            Assert.That(draft.Form, Is.EqualTo(created));
            Assert.That(draft.Definition!.Key, Is.EqualTo(created.Key), "the definition's key is the form's");
            Assert.That(draft.Definition.Pages.Single().Groups.Single().Fields, Is.Empty, "no consent default is set");
            Assert.That(draft.Definition.Outcomes.Single().Kind, Is.EqualTo(OutcomeKind.Message));
            Assert.That(draft.Definition.Outcomes.Single().Message, Is.Null, "the built-in thank-you message");
            Assert.That(draft.Problems, Is.Empty);
            Assert.That((draft.DraftVersion, draft.PublishedVersion, draft.HasUnpublishedChanges), Is.EqualTo((1, (int?)null, true)));
            Assert.That(await Service.GetAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await Service.GetDraftAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await Service.GetByAliasAsync("missing"), Is.Null);
        });
    }

    [Test]
    public async Task ACreate_TakesTheNextFreeAlias_InAnyLetterCase_TrashedFormsIncluded()
    {
        var first = await CreateAsync("Contact");
        var second = await CreateAsync("contact!");
        var row = await RowAsync(second);
        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var third = await CreateAsync("CONTACT");
        var given = await Service.CreateAsync(new NewFormRequest("Anything", Alias: "newsletter"));

        Assert.That(new[] { first.Alias, second.Alias, third.Alias, given.Alias }, Is.EqualTo(new[] { "contact", "contact2", "contact3", "newsletter" }));
    }

    [Test]
    public async Task ACreate_IsRefused_ForATakenOrInvalidAlias_AndForANameThatIsNone_WritingNothing()
    {
        await CreateAsync("Contact");

        Assert.Multiple(() =>
        {
            AssertCode(async () => await Service.CreateAsync(new NewFormRequest("Other", Alias: "contacT")), "alias_taken");
            AssertCode(async () => await Service.CreateAsync(new NewFormRequest("Other", Alias: "Contact")), "alias_invalid", "alias");
            AssertCode(async () => await Service.CreateAsync(new NewFormRequest("   ")), "invalid_value", "name");
            AssertCode(async () => await Service.CreateAsync(new NewFormRequest(new string('a', 256))), "limit_exceeded", "name");
            AssertCode(async () => await Service.CreateAsync(new NewFormRequest("Two" + (char)10 + "lines")), "invalid_value", "name");
            AssertCode(async () => await Service.CreateAsync(new NewFormRequest("Lone " + (char)0xD800)), "invalid_value", "name");
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ACreate_GoesIntoAFolderOutsideTheTrash()
    {
        var folder = await CreateFolderAsync("Live");
        var doomed = await CreateFolderAsync("Doomed");
        await Folders.TrashAsync(doomed.Id, Editor);

        var inFolder = await CreateAsync("In a folder", folder.Key);

        Assert.Multiple(async () =>
        {
            Assert.That(inFolder.FolderKey, Is.EqualTo(folder.Key));
            Assert.That((await RowAsync(inFolder)).FolderId, Is.EqualTo(folder.Id));
            AssertCode(async () => await CreateAsync("Lost", Guid.NewGuid()), "folder_missing");
            AssertCode(async () => await CreateAsync("Lost", doomed.Key), "folder_missing");
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(1));
            Assert.That(Notifications.Seen.OfType<FormSavingNotification>().Count(), Is.EqualTo(1), "refused before handlers hear of it");
        });
    }

    [Test]
    public async Task ACreate_StartsWithTheConsentField_OfTheNewFormDefaults()
    {
        var consent = new NewFormConsent
        {
            Label = new Dictionary<string, string> { [string.Empty] = "I agree to the terms", ["de"] = "Ich stimme den Bedingungen zu" },
            Text = new Dictionary<string, string> { [string.Empty] = "<p>I agree to the <a href='/terms'>terms</a>.</p>" },
        };
        await SettingsService.SaveAsync(new GeneralSettings { NewForms = new NewFormDefaults { Consent = consent } }, 1, Editor);

        var created = await CreateAsync("Contact");
        var draft = (await Service.GetDraftAsync(created.Key))!;
        var field = draft.Definition!.Pages[0].Groups[0].Fields.Single();

        Assert.Multiple(async () =>
        {
            Assert.That((field.Type, field.Alias, field.Required), Is.EqualTo(("consent", "consent", true)));
            Assert.That(field.Label, Is.EqualTo(new LocalizedText(consent.Label)));
            Assert.That(field.Settings!.TryGetProperty("text", out var text) ? text.GetProperty(string.Empty).GetString() : null, Is.EqualTo(consent.Text[string.Empty]));
            Assert.That(draft.Problems, Is.Empty);
            Assert.That((await PublishAsync(created)).Version, Is.EqualTo(2), "a form with the consent field publishes");
        });
    }

    [Test]
    public async Task AConsentDefault_ADefinitionWouldRefuse_IsLeftOut_AndLogged()
    {
        // Rows written past the settings service, which would refuse them: a label on two lines, a label in a culture that
        // is none, a wording in a culture that is none, a wording too long, and a blank wording.
        var refused = new (string Name, Dictionary<string, string> Label, Dictionary<string, string> Text)[]
        {
            ("Two lines", Texts("Two" + (char)10 + "lines"), Texts("<p>Yes.</p>")),
            ("No culture", Texts("Yes", "not a culture", "Ja"), Texts("<p>Yes.</p>")),
            ("No wording culture", Texts("Yes"), Texts("<p>Yes.</p>", "not a culture", "<p>Ja.</p>")),
            ("Long wording", Texts("Yes"), Texts(new string('a', 50001))),
            ("Blank wording", Texts("Yes"), Texts(" ")),
        };
        var created = new List<FormInfo>();
        for (var i = 0; i < refused.Length; i++)
        {
            await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, GeneralSettings.Alias, 1, i + 1, ConsentRow(refused[i].Label, refused[i].Text), null));
            created.Add(await CreateAsync(refused[i].Name));
        }

        var warnings = Logs.Entries.Where(entry => entry.Category == typeof(FormBuilderFormService).FullName && entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();
        Assert.Multiple(async () =>
        {
            for (var i = 0; i < refused.Length; i++)
            {
                Assert.That((await DraftAsync(created[i])).Pages[0].Groups[0].Fields, Is.Empty, refused[i].Name);
                Assert.That(warnings.ElementAtOrDefault(i), Does.Contain(created[i].Key.ToString()).And.Contain("save the general settings again"), refused[i].Name);
            }

            Assert.That(warnings, Has.Count.EqualTo(refused.Length));
        });
    }

    [Test]
    public async Task ASave_AppendsADraftVersion_AndTheSameDefinitionAgainWritesNothing()
    {
        var created = await CreateAsync("Contact");
        var changed = WithFields(await DraftAsync(created), Email);
        Clock.Advance(TimeSpan.FromMinutes(1));

        var saved = (await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, changed, "Added the email", Editor))!;
        var again = (await Service.SaveDraftAsync(created.Key, saved.Form.ConcurrencyVersion, changed, null, Editor))!;
        var reopened = (await Service.GetDraftAsync(created.Key))!;
        var row = await RowAsync(created);

        Assert.Multiple(async () =>
        {
            Assert.That(saved.Version, Is.EqualTo(2));
            Assert.That(saved.Form, Is.EqualTo(created with { ConcurrencyVersion = created.ConcurrencyVersion + 1, UpdatedUtc = Now.AddMinutes(1) }));
            Assert.That(again, Is.EqualTo(new FormVersionResult(saved.Form, null)), "the same definition writes nothing");
            Assert.That(reopened.DraftVersion, Is.EqualTo(2));
            Assert.That(Hash(reopened.Definition!), Is.EqualTo(Hash(changed)));
            Assert.That((await Forms.GetVersionByIdAsync(row.DraftVersionId!.Value))!.Info.Comment, Is.EqualTo("Added the email"));
            Assert.That(await VersionsAsync(created), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AStaleChange_ReturnsConcurrencyConflict_AndWritesNothing()
    {
        var created = await CreateAsync("Contact");
        var definition = await DraftAsync(created);
        await SaveAsync(created, WithFields(definition, Email));
        var row = await RowAsync(created);

        Assert.Multiple(async () =>
        {
            AssertCode(async () => await SaveAsync(created, WithFields(definition, Age)), "concurrency_conflict");
            AssertCode(async () => await PublishAsync(created), "concurrency_conflict");
            AssertCode(async () => await Service.UnpublishAsync(created.Key, created.ConcurrencyVersion, Editor), "concurrency_conflict");
            AssertCode(async () => await Service.RenameAsync(created.Key, created.ConcurrencyVersion, "Renamed", null, Editor), "concurrency_conflict");
            AssertCode(async () => await Service.MoveAsync(created.Key, created.ConcurrencyVersion, null, Editor), "concurrency_conflict");
            Assert.That(await RowAsync(created), Is.EqualTo(row));
            Assert.That(Notifications.Seen.OfType<FormCancelableNotification>().Count(), Is.EqualTo(2), "a stale change is refused before handlers hear of it");
            Assert.That(await VersionsAsync(created), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AChangeMeanwhile_BetweenTheChecksAndTheWrite_IsRefusedByTheWrite()
    {
        var created = await CreateAsync("Contact");
        var definition = WithFields(await DraftAsync(created), Email);
        Notifications.Before = async _ =>
        {
            // Another editor renames the form while a handler looks at the save.
            Notifications.Before = null;
            var row = await RowAsync(created);
            await Forms.UpdateAsync(new FormUpdate(row.Id, row.ConcurrencyVersion, row.Alias, "Renamed meanwhile", row.FolderId, row.IsRestricted, null));
        };

        Assert.Multiple(async () =>
        {
            AssertCode(async () => await SaveAsync(created, definition), "concurrency_conflict");
            Assert.That((await RowAsync(created)).Name, Is.EqualTo("Renamed meanwhile"));
            Assert.That(await VersionsAsync(created), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ACreateThatLosesItsAliasMeanwhile_TakesTheNextFreeOne_ButNeverAGivenAlias()
    {
        var steals = 1;
        Notifications.Before = async notification =>
        {
            // A create at the same moment takes the alias this one chose.
            if (notification is FormSavingNotification { IsNew: true } saving && steals-- > 0)
            {
                await Forms.CreateAsync(NewForm(saving.Alias));
            }
        };

        var created = await CreateAsync("Contact");
        var announced = Notifications.Seen.OfType<FormSavingNotification>().Select(notification => notification.Alias).ToList();
        steals = FormBuilderFormService.AliasAttempts;
        AssertCode(async () => await CreateAsync("Contact"), "alias_taken");
        steals = 1;
        AssertCode(async () => await Service.CreateAsync(new NewFormRequest("Newsletter", Alias: "newsletter")), "alias_taken");

        Assert.Multiple(() =>
        {
            Assert.That(created.Alias, Is.EqualTo("contact2"));
            Assert.That(announced, Is.EqualTo(new[] { "contact", "contact2" }), "announced again with the next alias");
            Assert.That(Notifications.Seen.OfType<FormSavingNotification>().Select(notification => notification.Alias).Skip(2), Is.EqualTo(new[] { "contact3", "contact4", "contact5", "newsletter" }), "three attempts, and none for a given alias");
        });
    }

    [Test]
    public async Task ASave_RefusesWhatASaveRefuses()
    {
        var created = await CreateAsync("Contact");
        var definition = await DraftAsync(created);
        var long501 = Email with { Label = new LocalizedText(new string('a', 501)) };

        Assert.Multiple(async () =>
        {
            AssertCode(async () => await SaveAsync(created, definition with { Key = Guid.NewGuid() }), "invalid_value", "$.key");
            AssertCode(async () => await SaveAsync(created, WithFields(definition, long501)), "limit_exceeded");
            AssertCode(async () => await SaveAsync(created, WithFields(definition, Email), new string('c', 501)), "limit_exceeded", "comment");
            Assert.That(await Service.SaveDraftAsync(Guid.NewGuid(), 1, definition, null, Editor), Is.Null);
            Assert.That(await VersionsAsync(created), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task APublish_PutsTheDraftLive_WithItsTheme_AndItsFieldCatalogue()
    {
        var created = await CreateAsync("Contact");
        var definition = WithFields(await DraftAsync(created), Email, Guests, Age);
        definition = definition with { Settings = definition.Settings with { Appearance = new FormAppearanceSettings { Theme = "calm" } } };
        var saved = await SaveAsync(created, definition);

        var published = (await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, "Live", Editor))!;
        var row = await RowAsync(created);
        var draft = (await Service.GetDraftAsync(created.Key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(published.Version, Is.EqualTo(3));
            Assert.That((published.Form.IsPublished, published.Form.PublicRevision), Is.EqualTo((true, 1)));
            Assert.That(row.ThemeAlias, Is.EqualTo("calm"));
            Assert.That((draft.PublishedVersion, draft.HasUnpublishedChanges), Is.EqualTo(((int?)3, false)));
            Assert.That(await CatalogueAsync(created), Is.EqualTo(new[]
            {
                ("email", "email", "text", (Guid?)null, 0, (int?)3, (int?)null),
                ("guests", "repeater", "none", null, 1, 3, null),
                ("guestName", "text", "text", Guests.Key, 2, 3, null),
                ("age", "number", "number", null, 3, 3, null),
            }));
        });
    }

    [Test]
    public async Task ARepublish_RaisesTheRevision_OnlyWhenVisitorsSeeAChange()
    {
        var created = await CreateAsync("Contact");
        var definition = WithFields(await DraftAsync(created), Email, Age);
        var first = await PublishAsync((await SaveAsync(created, definition)).Form);
        var same = await PublishAsync(first.Form);
        var hidden = await PublishAsync((await SaveAsync(first.Form, WithFields(definition, Email with { Sensitive = true }, Age))).Form);
        var relabelled = await PublishAsync((await SaveAsync(hidden.Form, WithFields(definition, Email with { Sensitive = true, Label = new LocalizedText("Your email") }, Age))).Form);
        var dropped = await PublishAsync((await SaveAsync(relabelled.Form, WithFields(definition, Email with { Sensitive = true, Label = new LocalizedText("Your email") }))).Form);

        var publishes = Notifications.Seen.OfType<FormPublishedNotification>().Select(notification => (notification.Version, notification.PublicRevision, notification.RevisionChanged));
        Assert.Multiple(async () =>
        {
            Assert.That((first.Version, first.Form.PublicRevision), Is.EqualTo(((int?)3, 1)));
            Assert.That(same, Is.EqualTo(new FormVersionResult(first.Form, null)), "the live version again writes nothing");
            Assert.That((hidden.Version, hidden.Form.PublicRevision), Is.EqualTo(((int?)5, 1)), "a sensitive field looks the same to visitors");
            Assert.That((relabelled.Version, relabelled.Form.PublicRevision), Is.EqualTo(((int?)7, 2)));
            Assert.That(publishes, Is.EqualTo(new[] { (3, 1, true), (5, 1, false), (7, 2, true), (9, 3, true) }));
            Assert.That(await CatalogueAsync(created), Is.EqualTo(new[]
            {
                ("email", "email", "text", (Guid?)null, 0, (int?)3, (int?)null),
                ("age", "number", "number", null, 1, 3, 7),
            }), "the age field ends at the last publish that had it");
            Assert.That(
                (await Audit.QueryAsync(new AuditQuery { EntityKey = created.Key, Action = FormBuilderFormService.PublishedAction }, PageRequest.First(10))).Items.OrderBy(entry => entry.Id).Select(entry => entry.Details),
                Is.EqualTo(new[]
                {
                    """{"version":3,"revision":1,"revisionChanged":true}""",
                    """{"version":5,"revision":1,"revisionChanged":false}""",
                    """{"version":7,"revision":2,"revisionChanged":true}""",
                    """{"version":9,"revision":3,"revisionChanged":true}""",
                }));
            Assert.That((await Forms.ListFieldsAsync((await RowAsync(created)).Id)).Select(field => (field.Values.Label, field.Values.IsSensitive)).First(), Is.EqualTo(("Your email", true)));
        });
    }

    [Test]
    public async Task APublish_IsRefusedByEveryCheckAtOnce_AndWritesNothing()
    {
        var created = await CreateAsync("Contact");
        var saved = await SaveAsync(created, WithFields(await DraftAsync(created), Email, Email with { Key = Key(9) }));
        Check.Problems = [new DefinitionProblem("test_refusal", "$", "The test check refuses.")];

        var refused = AssertCode(async () => await PublishAsync(saved.Form), "alias_duplicate");
        Check.Problems = [];
        var fixedUp = await SaveAsync(saved.Form, WithFields(await DraftAsync(saved.Form), Email));

        Assert.Multiple(async () =>
        {
            Assert.That((refused as FormBuilderValidationException)?.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { "alias_duplicate", "test_refusal" }));
            Assert.That(Notifications.Seen.OfType<FormPublishingNotification>(), Is.Empty, "refused before handlers hear of it");
            Assert.That((await PublishAsync(fixedUp.Form)).Version, Is.EqualTo(4), "nothing was appended by the refusal");
        });
    }

    [Test]
    public async Task Unpublishing_TakesTheFormOffline_AndAPublishPutsTheDraftBack()
    {
        var created = await CreateAsync("Contact");
        var published = await PublishAsync((await SaveAsync(created, WithFields(await DraftAsync(created), Email) with { Settings = new FormSettings { Appearance = new FormAppearanceSettings { Theme = "calm" } } })).Form);

        var offline = (await Service.UnpublishAsync(created.Key, published.Form.ConcurrencyVersion, Editor))!;
        var again = (await Service.UnpublishAsync(created.Key, offline.ConcurrencyVersion, Editor))!;
        var back = await PublishAsync(offline);

        Assert.Multiple(async () =>
        {
            Assert.That(offline, Is.EqualTo(published.Form with { IsPublished = false, ConcurrencyVersion = published.Form.ConcurrencyVersion + 1 }));
            Assert.That(again, Is.EqualTo(offline), "an unpublished form stays as it is");
            Assert.That(Notifications.Seen.OfType<FormUnpublishedNotification>().Count(), Is.EqualTo(1));
            Assert.That((back.Version, back.Form.IsPublished, back.Form.PublicRevision), Is.EqualTo(((int?)4, true, 1)), "the same projection keeps its revision");
            Assert.That((await RowAsync(created)).ThemeAlias, Is.EqualTo("calm"));
            Assert.That(await Service.UnpublishAsync(Guid.NewGuid(), 1, Editor), Is.Null);
        });
    }

    [Test]
    public async Task ARename_ChangesTheName_AndTheAliasWhenOneIsGiven()
    {
        var created = await CreateAsync("Contact");
        await CreateAsync("Newsletter");

        var renamed = (await Service.RenameAsync(created.Key, created.ConcurrencyVersion, "  Get in touch ", null, Editor))!;
        var realiased = (await Service.RenameAsync(created.Key, renamed.ConcurrencyVersion, "Get in touch", "getInTouch", Editor))!;
        var same = (await Service.RenameAsync(created.Key, realiased.ConcurrencyVersion, "Get in touch", "getInTouch", Editor))!;

        Assert.Multiple(async () =>
        {
            Assert.That((renamed.Name, renamed.Alias), Is.EqualTo(("Get in touch", "contact")));
            Assert.That((realiased.Alias, realiased.ConcurrencyVersion), Is.EqualTo(("getInTouch", renamed.ConcurrencyVersion + 1)));
            Assert.That(same, Is.EqualTo(realiased), "the same name and alias change nothing");
            Assert.That((await Service.GetByAliasAsync("GETINTOUCH"))?.Key, Is.EqualTo(created.Key));
            Assert.That(await Service.GetByAliasAsync("contact"), Is.Null);
            Assert.That(
                Notifications.Seen.OfType<FormRenamedNotification>().Select(notification => (notification.Alias, notification.Name, notification.PreviousAlias, notification.PreviousName)),
                Is.EqualTo(new[] { ("contact", "Get in touch", "contact", "Contact"), ("getInTouch", "Get in touch", "contact", "Get in touch") }));
            AssertCode(async () => await Service.RenameAsync(created.Key, same.ConcurrencyVersion, "Get in touch", "NEWSLETTER", Editor), "alias_invalid", "alias");
            AssertCode(async () => await Service.RenameAsync(created.Key, same.ConcurrencyVersion, "Get in touch", "newsLetter", Editor), "alias_taken");
            AssertCode(async () => await Service.RenameAsync(created.Key, same.ConcurrencyVersion, " ", null, Editor), "invalid_value", "name");
            Assert.That(await Service.RenameAsync(Guid.NewGuid(), 1, "Missing", null, Editor), Is.Null);
        });
    }

    [Test]
    public async Task AMove_GoesToAFolderOutsideTheTrash_OrToTheRoot()
    {
        var folder = await CreateFolderAsync("Live");
        var doomed = await CreateFolderAsync("Doomed");
        await Folders.TrashAsync(doomed.Id, Editor);
        var created = await CreateAsync("Contact");

        var moved = (await Service.MoveAsync(created.Key, created.ConcurrencyVersion, folder.Key, Editor))!;
        var same = (await Service.MoveAsync(created.Key, moved.ConcurrencyVersion, folder.Key, Editor))!;
        var root = (await Service.MoveAsync(created.Key, same.ConcurrencyVersion, null, Editor))!;

        Assert.Multiple(async () =>
        {
            Assert.That(moved, Is.EqualTo(created with { FolderKey = folder.Key, ConcurrencyVersion = created.ConcurrencyVersion + 1 }));
            Assert.That(same, Is.EqualTo(moved), "the same folder changes nothing");
            Assert.That((root.FolderKey, (await RowAsync(created)).FolderId), Is.EqualTo(((Guid?)null, (int?)null)));
            Assert.That(
                Notifications.Seen.OfType<FormMovedNotification>().Select(notification => (notification.FolderKey, notification.PreviousFolderKey)),
                Is.EqualTo(new[] { ((Guid?)folder.Key, (Guid?)null), (null, folder.Key) }));
            AssertCode(async () => await Service.MoveAsync(created.Key, root.ConcurrencyVersion, doomed.Key, Editor), "folder_missing");
            AssertCode(async () => await Service.MoveAsync(created.Key, root.ConcurrencyVersion, Guid.NewGuid(), Editor), "folder_missing");
            Assert.That(Notifications.Seen.OfType<FormMovingNotification>().Count(), Is.EqualTo(2), "a move to the same folder and a refused one are announced to nobody");
            Assert.That(await Service.MoveAsync(Guid.NewGuid(), 1, null, Editor), Is.Null);
        });
    }

    [Test]
    public async Task AFormInTheTrash_StillOpens_AndTakesNoChangeButBeingTakenOffline()
    {
        var created = await CreateAsync("Contact");
        var definition = WithFields(await DraftAsync(created), Email);
        await PublishAsync((await SaveAsync(created, definition)).Form);
        var row = await RowAsync(created);
        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var trashed = (await Service.GetAsync(created.Key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.TrashedUtc, Is.EqualTo(Now));
            Assert.That((await Service.GetDraftAsync(created.Key))?.Definition, Is.Not.Null, "it opens");
            AssertCode(async () => await SaveAsync(trashed, WithFields(definition, Age)), "trashed");
            AssertCode(async () => await PublishAsync(trashed), "trashed");
            AssertCode(async () => await Service.RenameAsync(created.Key, trashed.ConcurrencyVersion, "Renamed", null, Editor), "trashed");
            AssertCode(async () => await Service.MoveAsync(created.Key, trashed.ConcurrencyVersion, null, Editor), "trashed");
            Assert.That((await Service.UnpublishAsync(created.Key, trashed.ConcurrencyVersion, Editor))?.IsPublished, Is.False, "which lets go of its theme");
            Assert.That(Notifications.Seen.OfType<FormCancelableNotification>().Select(notification => notification.GetType().Name), Is.EqualTo(new[] { "FormSavingNotification", "FormSavingNotification", "FormPublishingNotification", "FormUnpublishingNotification" }), "the refused changes reach no handler");
        });
    }

    [Test]
    public async Task AHandlerThatCancels_StopsTheChange_WithItsMessage_AndNothingIsWritten()
    {
        var folder = await CreateFolderAsync();
        var created = await CreateAsync("Contact");
        var published = await PublishAsync((await SaveAsync(created, WithFields(await DraftAsync(created), Email))).Form);
        var changed = await SaveAsync(published.Form, WithFields(await DraftAsync(created), Email, Age));
        var current = changed.Form;
        var row = await RowAsync(created);
        var versions = await VersionsAsync(created);
        var audited = Count(FormBuilderTables.AuditEntry);
        var seen = Notifications.Seen.Count;
        Notifications.Cancel = true;

        Assert.Multiple(async () =>
        {
            var refused = AssertCode(async () => await CreateAsync("Another"), "cancelled");
            Assert.That((refused as FormBuilderCancelledException)?.Messages, Is.EqualTo(new[] { new CancelMessage(FormNotificationRecorder<INotification>.Category, FormNotificationRecorder<INotification>.Message) }));
            AssertCode(async () => await SaveAsync(current, WithFields(await DraftAsync(created), Age)), "cancelled");
            AssertCode(async () => await PublishAsync(current), "cancelled");
            AssertCode(async () => await Service.UnpublishAsync(created.Key, current.ConcurrencyVersion, Editor), "cancelled");
            AssertCode(async () => await Service.RenameAsync(created.Key, current.ConcurrencyVersion, "Renamed", null, Editor), "cancelled");
            AssertCode(async () => await Service.MoveAsync(created.Key, current.ConcurrencyVersion, folder.Key, Editor), "cancelled");
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(1));
            Assert.That(await RowAsync(created), Is.EqualTo(row));
            Assert.That(await VersionsAsync(created), Is.EqualTo(versions));
            Assert.That(Count(FormBuilderTables.AuditEntry), Is.EqualTo(audited));
            Assert.That(Notifications.Seen.Skip(seen), Has.Count.EqualTo(6).And.All.InstanceOf<FormCancelableNotification>(), "no notification that a change happened");
        });
    }

    [Test]
    public async Task EveryChange_IsAnnouncedBefore_AndAfterItCommitted()
    {
        var folder = await CreateFolderAsync();
        var created = await CreateAsync("Contact");
        var saved = await SaveAsync(created, WithFields(await DraftAsync(created), Email));
        var published = await PublishAsync(saved.Form);
        var offline = (await Service.UnpublishAsync(created.Key, published.Form.ConcurrencyVersion, Editor))!;
        var renamed = (await Service.RenameAsync(created.Key, offline.ConcurrencyVersion, "Get in touch", "getInTouch", Editor))!;
        await Service.MoveAsync(created.Key, renamed.ConcurrencyVersion, folder.Key, Editor);

        var seen = Notifications.Seen;
        Assert.Multiple(() =>
        {
            Assert.That(seen.Select(notification => notification.GetType()), Is.EqualTo(new[]
            {
                typeof(FormSavingNotification), typeof(FormSavedNotification),
                typeof(FormSavingNotification), typeof(FormSavedNotification),
                typeof(FormPublishingNotification), typeof(FormPublishedNotification),
                typeof(FormUnpublishingNotification), typeof(FormUnpublishedNotification),
                typeof(FormRenamingNotification), typeof(FormRenamedNotification),
                typeof(FormMovingNotification), typeof(FormMovedNotification),
            }));
            Assert.That(Notifications.AnyInUnitOfWork, Is.False, "before any write, and after the commit");
            Assert.That(seen.OfType<FormNotification>().Select(notification => notification.FormKey), Is.All.EqualTo(created.Key));
            Assert.That(seen.OfType<FormCancelableNotification>().Select(notification => notification.FormKey), Is.All.EqualTo(created.Key));
            Assert.That(seen.OfType<FormSavedNotification>().Select(notification => (notification.Version, notification.IsNew, notification.UserKey)), Is.EqualTo(new[] { (1, true, (Guid?)Editor), (2, false, (Guid?)Editor) }));
            Assert.That(seen.OfType<FormSavingNotification>().Select(notification => notification.IsNew), Is.EqualTo(new[] { true, false }));
            Assert.That(Hash(seen.OfType<FormPublishingNotification>().Single().Definition), Is.EqualTo(Hash(seen.OfType<FormSavingNotification>().Last().Definition)), "the draft that goes live");
            Assert.That(seen.OfType<FormRenamingNotification>().Select(notification => (notification.Alias, notification.PreviousAlias)).Single(), Is.EqualTo(("getInTouch", "contact")));
            Assert.That(seen.OfType<FormMovingNotification>().Select(notification => (notification.FolderKey, notification.PreviousFolderKey)).Single(), Is.EqualTo(((Guid?)folder.Key, (Guid?)null)));
        });
    }

    [Test]
    public async Task AFailingHandler_IsLogged_AndTheChangeStands()
    {
        Notifications.Fail = true;

        var created = await CreateAsync("Contact");

        var errors = Logs.Entries.Where(entry => entry.Category == typeof(FormBuilderFormService).FullName && entry.Level == LogLevel.Error).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(await Service.GetAsync(created.Key), Is.EqualTo(created));
            Assert.That(errors.Single().Message, Does.Contain(nameof(FormSavedNotification)).And.Contain(created.Key.ToString()).And.Contain("the change stands"));
            Assert.That(errors.Single().Exception?.Message, Is.EqualTo("JFB_TEST: a handler that fails"));
        });
    }

    [Test]
    public async Task AChange_InsideAUnitOfWorkOrAnUmbracoScope_IsRefused()
    {
        var created = await CreateAsync("Contact");
        var definition = WithFields(await DraftAsync(created), Email);
        var row = await RowAsync(created);
        var changes = new (string Name, Func<Task> Change)[]
        {
            ("create", () => CreateAsync("Inside")),
            ("save", () => SaveAsync(created, definition)),
            ("publish", () => PublishAsync(created)),
            ("unpublish", () => Service.UnpublishAsync(created.Key, created.ConcurrencyVersion, Editor)),
            ("rename", () => Service.RenameAsync(created.Key, created.ConcurrencyVersion, "Renamed", null, Editor)),
            ("move", () => Service.MoveAsync(created.Key, created.ConcurrencyVersion, null, Editor)),
        };

        Assert.Multiple(async () =>
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
            Assert.That(await RowAsync(created), Is.EqualTo(row));
            Assert.That(await VersionsAsync(created), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task EveryChange_IsAudited_WithItsVersion_NeverTheDefinition()
    {
        var folder = await CreateFolderAsync();
        var created = await CreateAsync("Contact");
        var saved = await SaveAsync(created, WithFields(await DraftAsync(created), Email));
        var published = await PublishAsync(saved.Form);
        var offline = (await Service.UnpublishAsync(created.Key, published.Form.ConcurrencyVersion, Editor))!;
        var renamed = (await Service.RenameAsync(created.Key, offline.ConcurrencyVersion, "Get in touch", "getInTouch", Editor))!;
        await Service.MoveAsync(created.Key, renamed.ConcurrencyVersion, folder.Key, Editor);

        var entries = (await Audit.QueryAsync(new AuditQuery { EntityKey = created.Key }, PageRequest.First(50))).Items.OrderBy(entry => entry.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => (entry.Action, entry.EntityType, entry.UserKey, entry.Details)), Is.EqualTo(new[]
            {
                ("form.created", "form", (Guid?)Editor, """{"alias":"contact","version":1}"""),
                ("form.saved", "form", Editor, """{"version":2}"""),
                ("form.published", "form", Editor, """{"version":3,"revision":1,"revisionChanged":true}"""),
                ("form.unpublished", "form", Editor, "{}"),
                ("form.renamed", "form", Editor, """{"alias":"getInTouch","previousAlias":"contact","nameChanged":true}"""),
                ("form.moved", "form", Editor, $$"""{"folder":"{{folder.Key}}"}"""),
            }));
            Assert.That(entries.Select(entry => entry.Details), Has.None.Contains("Email address"), "no text of the definition");
        });
    }

    [Test]
    public void TheService_AndItsPublishChecks_AreRegistered()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<IFormBuilderFormService>(), Is.InstanceOf<FormBuilderFormService>());
            Assert.That(GetRequiredService<IEnumerable<IFormPublishCheck>>().OfType<StructuralPublishCheck>().Count(), Is.EqualTo(1));
            Assert.That(GetRequiredService<FieldTypeCatalogue>(), Is.SameAs(FieldTypeCatalogue.BuiltIn));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.Services.AddSingleton<FormNotificationLog>();
        builder.Services.AddSingleton<TestPublishCheck>();
        builder.Services.AddSingleton<IFormPublishCheck>(provider => provider.GetRequiredService<TestPublishCheck>());
        builder.AddNotificationAsyncHandler<FormSavingNotification, FormNotificationRecorder<FormSavingNotification>>();
        builder.AddNotificationAsyncHandler<FormSavedNotification, FormNotificationRecorder<FormSavedNotification>>();
        builder.AddNotificationAsyncHandler<FormPublishingNotification, FormNotificationRecorder<FormPublishingNotification>>();
        builder.AddNotificationAsyncHandler<FormPublishedNotification, FormNotificationRecorder<FormPublishedNotification>>();
        builder.AddNotificationAsyncHandler<FormUnpublishingNotification, FormNotificationRecorder<FormUnpublishingNotification>>();
        builder.AddNotificationAsyncHandler<FormUnpublishedNotification, FormNotificationRecorder<FormUnpublishedNotification>>();
        builder.AddNotificationAsyncHandler<FormRenamingNotification, FormNotificationRecorder<FormRenamingNotification>>();
        builder.AddNotificationAsyncHandler<FormRenamedNotification, FormNotificationRecorder<FormRenamedNotification>>();
        builder.AddNotificationAsyncHandler<FormMovingNotification, FormNotificationRecorder<FormMovingNotification>>();
        builder.AddNotificationAsyncHandler<FormMovedNotification, FormNotificationRecorder<FormMovedNotification>>();
    }

    private static Guid Key(int number) => Guid.Parse($"70000000-0000-4000-8000-{number:D12}");

    private static string ConsentRow(Dictionary<string, string> label, Dictionary<string, string> text) =>
        SettingsJson.Global(new GeneralSettings { NewForms = new NewFormDefaults { Consent = new NewFormConsent { Label = label, Text = text } } });

    // A default text and pairs of a culture and its text.
    private static Dictionary<string, string> Texts(string fallback, params string[] cultures)
    {
        var texts = new Dictionary<string, string> { [string.Empty] = fallback };
        for (var i = 0; i < cultures.Length; i += 2)
        {
            texts[cultures[i]] = cultures[i + 1];
        }

        return texts;
    }

    private Task<FormInfo> CreateAsync(string name, Guid? folderKey = null) => Service.CreateAsync(new NewFormRequest(name, FolderKey: folderKey, UserKey: Editor));

    private async Task<FormDefinition> DraftAsync(FormInfo form) => (await Service.GetDraftAsync(form.Key))!.Definition!;

    private async Task<FormVersionResult> SaveAsync(FormInfo form, FormDefinition definition, string? comment = null) =>
        (await Service.SaveDraftAsync(form.Key, form.ConcurrencyVersion, definition, comment, Editor))!;

    private async Task<FormVersionResult> PublishAsync(FormInfo form) => (await Service.PublishAsync(form.Key, form.ConcurrencyVersion, null, Editor))!;

    private async Task<StoredForm> RowAsync(FormInfo form) => (await Forms.GetAsync(form.Key))!;

    private async Task<int> VersionsAsync(FormInfo form) => Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", (await RowAsync(form)).Id);

    // The catalogue rows by sort order: alias, type, storage, parent, place, first and last version.
    private async Task<IReadOnlyList<(string, string, string, Guid?, int, int?, int?)>> CatalogueAsync(FormInfo form) =>
        (await Forms.ListFieldsAsync((await RowAsync(form)).Id))
            .Select(field => field.Values)
            .OrderBy(values => values.SortOrder)
            .Select(values => (values.Alias, values.Type, StoredNames.Of(values.StorageKind), values.ParentFieldKey, values.SortOrder, values.FirstVersionNumber, values.LastVersionNumber))
            .ToList();
}
