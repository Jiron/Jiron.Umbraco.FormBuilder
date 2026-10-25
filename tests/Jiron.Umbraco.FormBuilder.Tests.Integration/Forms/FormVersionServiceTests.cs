using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// The versions of a form through the form service (section 4.5, ADR-0045): the history with what changed in each
/// version, one version, two compared, a restore that saves an older definition as a new draft, and the pins and comments
/// that leave the form as it is; with their refusals, notifications and audit entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormVersionServiceTests : RepositoryTestBase
{
    private static readonly FormField Email = new() { Key = Key(1), Type = "email", Alias = "email", Label = new LocalizedText("Email address") };
    private static readonly FormField Relabelled = Email with { Label = new LocalizedText("Your email") };
    private static readonly FormField Age = new() { Key = Key(2), Type = "number", Alias = "age", Label = new LocalizedText("Age") };

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private IFormBuilderFormVersionService Versions => GetRequiredService<IFormBuilderFormVersionService>();

    private FormNotificationLog Notifications => GetRequiredService<FormNotificationLog>();

    [Test]
    public async Task ARestore_CreatesANewVersion_ThatIsTheDraft_WithTheOlderDefinition()
    {
        var (form, blank) = await HistoryAsync();
        Clock.Advance(TimeSpan.FromMinutes(1));

        var restored = (await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 2, Editor))!;
        var draft = (await Service.GetDraftAsync(form.Key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(restored.Version, Is.EqualTo(7), "a new version after the six");
            Assert.That(restored.Form, Is.EqualTo(form with { ConcurrencyVersion = form.ConcurrencyVersion + 1, UpdatedUtc = Now.AddMinutes(6) }));
            Assert.That((draft.DraftVersion, draft.PublishedVersion, draft.HasUnpublishedChanges), Is.EqualTo((7, (int?)6, true)), "the live version stays live");
            Assert.That(Hash(draft.Definition!), Is.EqualTo(Hash(WithFields(blank, Email))), "version 2's definition");
            Assert.That((await Versions.GetVersionAsync(form.Key, 7))!.Version, Is.EqualTo(new FormVersion(7, FormVersionKind.Draft, null, null, false, Now.AddMinutes(6), Editor, null, null, true, false)));
            Assert.That(Hash((await Versions.GetVersionAsync(form.Key, 2))!.Definition!), Is.EqualTo(Hash(draft.Definition!)), "version 2 stays as it was");
            Assert.That(await VersionsAsync(form), Is.EqualTo(7));
        });
    }

    [Test]
    public async Task ARestore_IsAnnouncedAsADraftSave_FromItsVersion_AndAudited()
    {
        var (form, blank) = await HistoryAsync();
        var before = Notifications.Seen;

        await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 2, Editor);

        var restore = Notifications.Seen.Skip(before.Count).ToList();
        var saving = restore.OfType<FormSavingNotification>().Single();
        var saved = restore.OfType<FormSavedNotification>().Single();
        var audited = (await Audit.QueryAsync(new AuditQuery { EntityKey = form.Key }, PageRequest.First(50))).Items.MaxBy(entry => entry.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(restore.Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormSavingNotification), typeof(FormSavedNotification) }));
            Assert.That((saving.FormKey, saving.RestoredFromVersion, saving.IsNew, saving.UserKey), Is.EqualTo((form.Key, (int?)2, false, (Guid?)Editor)));
            Assert.That(Hash(saving.Definition), Is.EqualTo(Hash(WithFields(blank, Email))), "the definition the draft gets");
            Assert.That((saved.FormKey, saved.Version, saved.RestoredFromVersion, saved.IsNew, saved.UserKey), Is.EqualTo((form.Key, 7, (int?)2, false, (Guid?)Editor)));
            Assert.That(before.OfType<FormSavingNotification>().Select(notification => notification.RestoredFromVersion), Is.All.Null, "a save is no restore");
            Assert.That(before.OfType<FormSavedNotification>().Select(notification => notification.RestoredFromVersion), Is.All.Null);
            Assert.That(Notifications.AnyInUnitOfWork, Is.False, "before any write, and after the commit");
            Assert.That((audited.Action, audited.EntityType, audited.UserKey, audited.Details), Is.EqualTo(("form.versionRestored", "form", (Guid?)Editor, """{"version":7,"from":2}""")));
        });
    }

    [Test]
    public async Task ARestoreOfWhatTheDraftHolds_WritesNothing()
    {
        var (form, _) = await HistoryAsync();
        var seen = Notifications.Seen.Count;
        var audited = Count(FormBuilderTables.AuditEntry);

        var draft = await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 5, Editor);
        var live = await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 6, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(draft, Is.EqualTo(new FormVersionResult(form, null)), "the draft itself");
            Assert.That(live, Is.EqualTo(new FormVersionResult(form, null)), "the live version, which holds the same");
            Assert.That(await VersionsAsync(form), Is.EqualTo(6));
            Assert.That(Notifications.Seen, Has.Count.EqualTo(seen));
            Assert.That(Count(FormBuilderTables.AuditEntry), Is.EqualTo(audited));
        });
    }

    [Test]
    public async Task ARestore_IsRefused_WhenStale_Cancelled_OrInTheTrash_WritingNothing()
    {
        var (form, _) = await HistoryAsync();
        var row = await RowAsync(form);
        var audited = Count(FormBuilderTables.AuditEntry);
        var seen = Notifications.Seen.Count;

        AssertCode(async () => await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion - 1, 2, Editor), "concurrency_conflict");
        Assert.That(await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 99, Editor), Is.Null, "a version it does not have");
        Assert.That(await Versions.RestoreVersionAsync(Guid.NewGuid(), 1, 1, Editor), Is.Null, "a form that is none");
        Notifications.Cancel = true;
        var cancelled = AssertCode(async () => await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 2, Editor), "cancelled");
        Notifications.Cancel = false;
        Assert.Multiple(async () =>
        {
            Assert.That((cancelled as FormBuilderCancelledException)?.Messages, Is.EqualTo(new[] { new CancelMessage(FormNotificationRecorder<INotification>.Category, FormNotificationRecorder<INotification>.Message) }));
            Assert.That(await RowAsync(form), Is.EqualTo(row));
            Assert.That(await VersionsAsync(form), Is.EqualTo(6));
            Assert.That(Count(FormBuilderTables.AuditEntry), Is.EqualTo(audited));
        });

        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var trashed = (await Service.GetAsync(form.Key))!;

        AssertCode(async () => await Versions.RestoreVersionAsync(form.Key, trashed.ConcurrencyVersion, 2, Editor), "trashed");
        Assert.Multiple(async () =>
        {
            Assert.That(await VersionsAsync(form), Is.EqualTo(6));
            Assert.That(Notifications.Seen.Skip(seen).Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormSavingNotification) }), "only the cancelled restore reached a handler");
        });
    }

    [Test]
    public async Task AVersionOfAnotherForm_OrOneThatCannotBeRead_IsNotRestored_NorCompared()
    {
        var created = await CreateAsync("Contact");
        var blank = await DraftAsync(created);
        var row = await RowAsync(created);
        // Versions written past the service: another form's definition, and one that is no definition at all.
        var other = (await Forms.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Stored(WithFields(blank, Email) with { Key = Guid.NewGuid() }), null, Editor))!;
        await Forms.SaveDraftAsync(row.Id, other.Form.ConcurrencyVersion, Document("""{"pages":"none"}"""), null, Editor);
        var form = (await Service.GetAsync(created.Key))!;

        var opened = (await Versions.GetVersionAsync(form.Key, 3))!;
        var listed = (await Versions.ListVersionsAsync(form.Key, includeDrafts: true, PageRequest.First(10)))!;

        Assert.Multiple(() =>
        {
            AssertCode(async () => await Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 2, Editor), "invalid_value", "$.key");
            Assert.That(() => Versions.RestoreVersionAsync(form.Key, form.ConcurrencyVersion, 3, Editor), Throws.InstanceOf<FormBuilderValidationException>(), "a restore");
            Assert.That(() => Versions.CompareAsync(form.Key, 1, 3), Throws.InstanceOf<FormBuilderValidationException>(), "a comparison");
            Assert.That(() => Versions.CompareAsync(form.Key, 3, 1), Throws.InstanceOf<FormBuilderValidationException>(), "a comparison the other way");
            Assert.That((opened.Definition, opened.Problems.Count > 0), Is.EqualTo(((FormDefinition?)null, true)), "it opens with what is wrong with it");
            Assert.That(listed.Items.Select(entry => entry.Changes is null), Is.EqualTo(new[] { true, false, true }), "no changes into it, and none for the first");
        });
    }

    [Test]
    public async Task TheHistory_ListsThePublishes_NewestFirst_EachWithWhatChangedSinceThePublishBefore()
    {
        var (form, _) = await HistoryAsync();

        var listed = (await Versions.ListVersionsAsync(form.Key, includeDrafts: false, PageRequest.First(10)))!;

        Assert.Multiple(() =>
        {
            Assert.That(listed.Total, Is.EqualTo(2));
            Assert.That(listed.Items.Select(entry => entry.Version), Is.EqualTo(new[]
            {
                new FormVersion(6, FormVersionKind.Published, 2, null, false, Now.AddMinutes(5), Editor, Now.AddMinutes(5), Editor, false, true),
                new FormVersion(3, FormVersionKind.Published, 1, null, false, Now.AddMinutes(2), Editor, Now.AddMinutes(2), Editor, false, false),
            }));
            Assert.That(listed.Items.Select(Described), Is.EqualTo(new[] { "Field email Changed, Field age Added", "none before it" }));
        });
    }

    [Test]
    public async Task TheHistory_WithTheDraftSaves_ComparesEachSaveWithTheVersionBefore_AndEachPublishWithThePublishBefore()
    {
        var (form, _) = await HistoryAsync();

        var all = (await Versions.ListVersionsAsync(form.Key, includeDrafts: true, PageRequest.First(10)))!;
        var first = (await Versions.ListVersionsAsync(form.Key, includeDrafts: true, new PageRequest(0, 2)))!;
        var last = (await Versions.ListVersionsAsync(form.Key, includeDrafts: true, new PageRequest(4, 2)))!;

        Assert.Multiple(() =>
        {
            Assert.That(all.Items.Select(entry => (entry.Version.Number, entry.Version.Kind, entry.Version.IsDraft, entry.Version.IsLive)), Is.EqualTo(new[]
            {
                (6, FormVersionKind.Published, false, true),
                (5, FormVersionKind.Draft, true, false),
                (4, FormVersionKind.Draft, false, false),
                (3, FormVersionKind.Published, false, false),
                (2, FormVersionKind.Draft, false, false),
                (1, FormVersionKind.Draft, false, false),
            }));
            Assert.That(all.Items.Select(Described), Is.EqualTo(new[]
            {
                "Field email Changed, Field age Added",
                "Field email Changed",
                "Field age Added",
                "none before it",
                "Field email Added",
                "none before it",
            }));
            Assert.That(first.Items.Select(Described), Is.EqualTo(all.Items.Take(2).Select(Described)), "a page compares with versions past it");
            Assert.That(last.Items.Select(Described), Is.EqualTo(all.Items.Skip(4).Select(Described)));
            Assert.That(new[] { all.Total, first.Total, last.Total }, Is.All.EqualTo(6));
        });
    }

    [Test]
    public async Task TheHistory_TakesAtMostFiftyAPage_AndIsNullForAFormThatIsNone()
    {
        var created = await CreateAsync("Contact");

        Assert.Multiple(async () =>
        {
            Assert.That((await Versions.ListVersionsAsync(created.Key, includeDrafts: true, PageRequest.First(50)))!.Items, Has.Count.EqualTo(1));
            Assert.That(() => Versions.ListVersionsAsync(created.Key, includeDrafts: true, PageRequest.First(51)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(await Versions.ListVersionsAsync(Guid.NewGuid(), includeDrafts: true, PageRequest.First(10)), Is.Null);
        });
    }

    [Test]
    public async Task OneVersion_Opens_WithWhatASaveWouldFindInIt()
    {
        var (form, blank) = await HistoryAsync();

        var opened = (await Versions.GetVersionAsync(form.Key, 4))!;

        Assert.Multiple(async () =>
        {
            Assert.That(opened.Version, Is.EqualTo(new FormVersion(4, FormVersionKind.Draft, null, null, false, Now.AddMinutes(3), Editor, null, null, false, false)));
            Assert.That(Hash(opened.Definition!), Is.EqualTo(Hash(WithFields(blank, Email, Age))));
            Assert.That(opened.Problems, Is.Empty);
            Assert.That(await Versions.GetVersionAsync(form.Key, 7), Is.Null);
            Assert.That(await Versions.GetVersionAsync(Guid.NewGuid(), 1), Is.Null);
        });
    }

    [Test]
    public async Task TwoVersions_AreCompared_InEitherDirection()
    {
        var (form, _) = await HistoryAsync();

        var forward = (await Versions.CompareAsync(form.Key, 2, 5))!;
        var backward = (await Versions.CompareAsync(form.Key, 5, 2))!;

        Assert.Multiple(async () =>
        {
            Assert.That((forward.FromVersion, forward.ToVersion, backward.FromVersion, backward.ToVersion), Is.EqualTo((2, 5, 5, 2)));
            Assert.That(forward.Changes.Select(change => (change.Kind, change.Key, change.Change)), Is.EqualTo(new[]
            {
                (DefinitionElementKind.Field, (Guid?)Key(1), DefinitionChangeKind.Changed),
                (DefinitionElementKind.Field, Key(2), DefinitionChangeKind.Added),
            }));
            Assert.That(forward.Changes[0].Properties.Select(property => (string.Join("/", property.Path), property.From?.GetValue<string>(), property.To?.GetValue<string>())), Is.EqualTo(new[] { ("label/", "Email address", "Your email") }));
            Assert.That(backward.Changes.Select(change => (change.Key, change.Change)), Is.EqualTo(new[] { ((Guid?)Key(1), DefinitionChangeKind.Changed), (Key(2), DefinitionChangeKind.Removed) }));
            Assert.That((await Versions.CompareAsync(form.Key, 2, 3))!.Changes, Is.Empty, "a publish of the same definition");
            Assert.That(await Versions.CompareAsync(form.Key, 2, 7), Is.Null);
            Assert.That(await Versions.CompareAsync(form.Key, 7, 2), Is.Null);
            Assert.That(await Versions.CompareAsync(Guid.NewGuid(), 1, 2), Is.Null);
        });
    }

    [Test]
    public async Task APinAndAComment_ChangeTheVersionOnly_EachAnnouncedAndAudited()
    {
        var created = await CreateAsync("Contact");
        var saved = await SaveAsync(created, WithFields(await DraftAsync(created), Email));
        var seen = Notifications.Seen.Count;
        Clock.Advance(TimeSpan.FromMinutes(1));

        var pinned = (await Versions.PinVersionAsync(created.Key, 1, true, Editor))!;
        var commented = (await Versions.CommentVersionAsync(created.Key, 1, "Approved by legal", Editor))!;
        var unpinned = (await Versions.PinVersionAsync(created.Key, 1, false, Editor))!;
        var cleared = (await Versions.CommentVersionAsync(created.Key, 1, "   ", Editor))!;
        var after = (await Service.GetAsync(created.Key))!;
        var announced = Notifications.Seen.Skip(seen).ToList();
        var audited = (await Audit.QueryAsync(new AuditQuery { EntityKey = created.Key }, PageRequest.First(50))).Items.OrderBy(entry => entry.Id).Skip(2).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(pinned, Is.EqualTo(new FormVersion(1, FormVersionKind.Draft, null, null, true, Now, Editor, null, null, false, false)));
            Assert.That((commented.IsPinned, commented.Comment), Is.EqualTo((true, "Approved by legal")), "a comment keeps the pin");
            Assert.That((unpinned.IsPinned, unpinned.Comment), Is.EqualTo((false, "Approved by legal")), "a pin keeps the comment");
            Assert.That((cleared.IsPinned, cleared.Comment), Is.EqualTo((false, (string?)null)), "a blank comment clears it");
            Assert.That(after, Is.EqualTo(saved.Form), "the form stays as it is");
            Assert.That(announced.Select(Announced), Is.EqualTo(new[]
            {
                "updating 1 pinned=True comment=", "updated 1 pinned=True comment=",
                "updating 1 pinned=True comment=Approved by legal", "updated 1 pinned=True comment=Approved by legal",
                "updating 1 pinned=False comment=Approved by legal", "updated 1 pinned=False comment=Approved by legal",
                "updating 1 pinned=False comment=", "updated 1 pinned=False comment=",
            }));
            Assert.That(announced.OfType<FormVersionUpdatingNotification>().Select(notification => (notification.FormKey, notification.Alias, notification.UserKey)), Is.All.EqualTo((created.Key, "contact", (Guid?)Editor)));
            Assert.That(announced.OfType<FormVersionUpdatedNotification>().Select(notification => (notification.FormKey, notification.Alias, notification.UserKey)), Is.All.EqualTo((created.Key, "contact", (Guid?)Editor)));
            Assert.That(Notifications.AnyInUnitOfWork, Is.False);
            Assert.That(audited.Select(entry => (entry.Action, entry.UserKey, entry.Details)), Is.EqualTo(new[]
            {
                ("form.versionPinned", (Guid?)Editor, """{"version":1,"hasComment":false}"""),
                ("form.versionCommented", Editor, """{"version":1,"hasComment":true}"""),
                ("form.versionUnpinned", Editor, """{"version":1,"hasComment":true}"""),
                ("form.versionCommented", Editor, """{"version":1,"hasComment":false}"""),
            }));
            Assert.That((await SaveAsync(after, WithFields(await DraftAsync(created), Email, Age))).Version, Is.EqualTo(3), "an editor's open draft is not stale");
        });
    }

    [Test]
    public async Task APinOrACommentThatChangesNothing_WritesNothing_AndIsNotAnnounced()
    {
        var created = await CreateAsync("Contact");
        var kept = (await Versions.CommentVersionAsync(created.Key, 1, "Kept", Editor))!;
        var seen = Notifications.Seen.Count;
        var audited = Count(FormBuilderTables.AuditEntry);

        var unpinned = await Versions.PinVersionAsync(created.Key, 1, false, Editor);
        var same = await Versions.CommentVersionAsync(created.Key, 1, "Kept", Editor);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { unpinned, same }, Is.All.EqualTo(kept));
            Assert.That(Notifications.Seen, Has.Count.EqualTo(seen));
            Assert.That(Count(FormBuilderTables.AuditEntry), Is.EqualTo(audited));
        });
    }

    [Test]
    public async Task APinOrAComment_IsRefused_ForWhatAVersionCannotHold_Cancelled_OrInTheTrash()
    {
        var created = await CreateAsync("Contact");
        var audited = Count(FormBuilderTables.AuditEntry);
        var longest = new string('c', FormRepository.MaxCommentLength);

        AssertCode(async () => await Versions.CommentVersionAsync(created.Key, 1, longest + "c", Editor), "limit_exceeded", "comment");
        AssertCode(async () => await Versions.CommentVersionAsync(created.Key, 1, "Lone " + (char)0xD800, Editor), "invalid_value", "comment");
        Assert.Multiple(async () =>
        {
            Assert.That(await Versions.PinVersionAsync(created.Key, 9, true, Editor), Is.Null, "a version it does not have");
            Assert.That(await Versions.CommentVersionAsync(created.Key, 9, "x", Editor), Is.Null);
            Assert.That(await Versions.PinVersionAsync(Guid.NewGuid(), 1, true, Editor), Is.Null, "a form that is none");
            Assert.That(await Versions.CommentVersionAsync(Guid.NewGuid(), 1, "x", Editor), Is.Null);
            Assert.That(Count(FormBuilderTables.AuditEntry), Is.EqualTo(audited));
        });

        var seen = Notifications.Seen.Count;
        Notifications.Cancel = true;
        AssertCode(async () => await Versions.PinVersionAsync(created.Key, 1, true, Editor), "cancelled");
        AssertCode(async () => await Versions.CommentVersionAsync(created.Key, 1, "x", Editor), "cancelled");
        Notifications.Cancel = false;
        var row = await RowAsync(created);
        await Forms.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        AssertCode(async () => await Versions.PinVersionAsync(created.Key, 1, true, Editor), "trashed");
        AssertCode(async () => await Versions.CommentVersionAsync(created.Key, 1, "x", Editor), "trashed");

        Assert.Multiple(async () =>
        {
            Assert.That((await Forms.GetVersionAsync(row.Id, 1))!.Info, Has.Property(nameof(FormVersionInfo.IsPinned)).False.And.Property(nameof(FormVersionInfo.Comment)).Null);
            Assert.That(Notifications.Seen.Skip(seen).Select(notification => notification.GetType()), Is.EqualTo(new[] { typeof(FormVersionUpdatingNotification), typeof(FormVersionUpdatingNotification) }), "only the cancelled changes reached a handler");
            Assert.That(Count(FormBuilderTables.AuditEntry), Is.EqualTo(audited));
        });

        await Forms.RestoreAsync(row.Id, (await RowAsync(created)).ConcurrencyVersion, row.FolderId, Editor);
        Assert.That((await Versions.CommentVersionAsync(created.Key, 1, longest, Editor))?.Comment, Is.EqualTo(longest), "the longest comment a version holds");
    }

    [Test]
    public async Task AVersionChange_InsideAUnitOfWork_IsRefused()
    {
        var created = await CreateAsync("Contact");
        var row = await RowAsync(created);
        var version = (await Forms.GetVersionAsync(row.Id, 1))!;
        var changes = new (string Name, Func<Task> Change)[]
        {
            ("restore", () => Versions.RestoreVersionAsync(created.Key, created.ConcurrencyVersion, 1, Editor)),
            ("pin", () => Versions.PinVersionAsync(created.Key, 1, true, Editor)),
            ("comment", () => Versions.CommentVersionAsync(created.Key, 1, "x", Editor)),
        };

        Assert.Multiple(async () =>
        {
            foreach (var (name, change) in changes)
            {
                Assert.That(() => Database.WriteAsync(async _ => { await change(); return true; }), Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"), name);
            }

            Assert.That(await Forms.GetVersionAsync(row.Id, 1), Is.EqualTo(version));
            Assert.That(await RowAsync(created), Is.EqualTo(row));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<FormNotificationLog>();
        builder.AddNotificationAsyncHandler<FormSavingNotification, FormNotificationRecorder<FormSavingNotification>>();
        builder.AddNotificationAsyncHandler<FormSavedNotification, FormNotificationRecorder<FormSavedNotification>>();
        builder.AddNotificationAsyncHandler<FormVersionUpdatingNotification, FormNotificationRecorder<FormVersionUpdatingNotification>>();
        builder.AddNotificationAsyncHandler<FormVersionUpdatedNotification, FormNotificationRecorder<FormVersionUpdatedNotification>>();
    }

    private static Guid Key(int number) => Guid.Parse($"71000000-0000-4000-8000-{number:D12}");

    // What changed into a version, as kind, alias and change, or that nothing came before it.
    private static string Described(FormVersionEntry entry) =>
        entry.Changes is null ? "none before it" : string.Join(", ", entry.Changes.Select(change => $"{change.Kind} {change.Alias} {change.Change}"));

    private static string Announced(INotification notification) => notification switch
    {
        FormVersionUpdatingNotification before => $"updating {before.Version} pinned={before.IsPinned} comment={before.Comment}",
        FormVersionUpdatedNotification after => $"updated {after.Version} pinned={after.IsPinned} comment={after.Comment}",
        _ => notification.GetType().Name,
    };

    // Version 1 blank, 2 with the email field, 3 its publish, 4 with the age field too, 5 with the email relabelled, 6 its
    // publish; a minute apart. The form after them, and its blank definition.
    private async Task<(FormInfo Form, FormDefinition Blank)> HistoryAsync()
    {
        var created = await CreateAsync("Contact");
        var blank = await DraftAsync(created);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var saved = await SaveAsync(created, WithFields(blank, Email));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var published = await PublishAsync(saved.Form);
        Clock.Advance(TimeSpan.FromMinutes(1));
        saved = await SaveAsync(published.Form, WithFields(blank, Email, Age));
        Clock.Advance(TimeSpan.FromMinutes(1));
        saved = await SaveAsync(saved.Form, WithFields(blank, Relabelled, Age));
        Clock.Advance(TimeSpan.FromMinutes(1));
        published = await PublishAsync(saved.Form);
        return (published.Form, blank);
    }

    private Task<FormInfo> CreateAsync(string name) => Service.CreateAsync(new NewFormRequest(name, UserKey: Editor));

    private async Task<FormDefinition> DraftAsync(FormInfo form) => (await Service.GetDraftAsync(form.Key))!.Definition!;

    private async Task<FormVersionResult> SaveAsync(FormInfo form, FormDefinition definition) =>
        (await Service.SaveDraftAsync(form.Key, form.ConcurrencyVersion, definition, null, Editor))!;

    private async Task<FormVersionResult> PublishAsync(FormInfo form) => (await Service.PublishAsync(form.Key, form.ConcurrencyVersion, null, Editor))!;

    private async Task<StoredForm> RowAsync(FormInfo form) => (await Forms.GetAsync(form.Key))!;

    private async Task<int> VersionsAsync(FormInfo form) => Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", (await RowAsync(form)).Id);
}
