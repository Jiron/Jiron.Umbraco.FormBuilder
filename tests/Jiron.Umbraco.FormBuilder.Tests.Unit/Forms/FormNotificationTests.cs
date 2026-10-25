using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The notifications of the form service (ADR-0044): what each carries, the arguments it refuses, how a handler cancels
/// a change, and the publish check that holds a draft to the structural validation (ADR-0035).
/// </summary>
[TestFixture]
public sealed class FormNotificationTests
{
    private static readonly Guid FormKey = Guid.Parse("60000000-0000-4000-8000-000000000001");
    private static readonly Guid User = Guid.Parse("60000000-0000-4000-8000-000000000002");
    private static readonly Guid Folder = Guid.Parse("60000000-0000-4000-8000-000000000003");
    private static readonly Guid Previous = Guid.Parse("60000000-0000-4000-8000-000000000004");

    [Test]
    public void TheNotificationsBeforeAChange_CarryWhatItWillBe()
    {
        var definition = new FormDefinition { Key = FormKey };
        var saving = new FormSavingNotification(definition, "contact", "Contact", isNew: true, User, new EventMessages());
        var publishing = new FormPublishingNotification(definition, "contact", "Contact", null, new EventMessages());
        var unpublishing = new FormUnpublishingNotification(FormKey, "contact", "Contact", User, new EventMessages());
        var renaming = new FormRenamingNotification(FormKey, "getInTouch", "Get in touch", "contact", "Contact", User, new EventMessages());
        var moving = new FormMovingNotification(FormKey, "contact", "Contact", Folder, Previous, User, new EventMessages());

        Assert.Multiple(() =>
        {
            Assert.That((saving.FormKey, saving.Alias, saving.Name, saving.IsNew, saving.UserKey), Is.EqualTo((FormKey, "contact", "Contact", true, (Guid?)User)));
            Assert.That(saving.Definition, Is.SameAs(definition));
            Assert.That((publishing.FormKey, publishing.UserKey), Is.EqualTo((FormKey, (Guid?)null)));
            Assert.That(publishing.Definition, Is.SameAs(definition));
            Assert.That((unpublishing.FormKey, unpublishing.Alias, unpublishing.Name), Is.EqualTo((FormKey, "contact", "Contact")));
            Assert.That((renaming.Alias, renaming.Name, renaming.PreviousAlias, renaming.PreviousName), Is.EqualTo(("getInTouch", "Get in touch", "contact", "Contact")));
            Assert.That((moving.FolderKey, moving.PreviousFolderKey), Is.EqualTo(((Guid?)Folder, (Guid?)Previous)));
            Assert.That(new[] { saving.Cancel, publishing.Cancel, unpublishing.Cancel, renaming.Cancel, moving.Cancel }, Is.All.False);
        });
    }

    [Test]
    public void TheNotificationsAfterAChange_CarryWhatItDid()
    {
        var saved = new FormSavedNotification(FormKey, "contact", "Contact", 4, isNew: false, User);
        var published = new FormPublishedNotification(FormKey, "contact", "Contact", 5, 2, revisionChanged: true, null);
        var unpublished = new FormUnpublishedNotification(FormKey, "contact", "Contact", User);
        var renamed = new FormRenamedNotification(FormKey, "getInTouch", "Get in touch", "contact", "Contact", User);
        var moved = new FormMovedNotification(FormKey, "contact", "Contact", null, Previous, User);

        Assert.Multiple(() =>
        {
            Assert.That((saved.FormKey, saved.Alias, saved.Name, saved.Version, saved.IsNew, saved.UserKey), Is.EqualTo((FormKey, "contact", "Contact", 4, false, (Guid?)User)));
            Assert.That((published.Version, published.PublicRevision, published.RevisionChanged, published.UserKey), Is.EqualTo((5, 2, true, (Guid?)null)));
            Assert.That((unpublished.FormKey, unpublished.UserKey), Is.EqualTo((FormKey, (Guid?)User)));
            Assert.That((renamed.Alias, renamed.Name, renamed.PreviousAlias, renamed.PreviousName), Is.EqualTo(("getInTouch", "Get in touch", "contact", "Contact")));
            Assert.That((moved.FolderKey, moved.PreviousFolderKey), Is.EqualTo(((Guid?)null, (Guid?)Previous)), "to the root");
        });
    }

    [Test]
    public void TheNotificationsOfTheTrashAndTheDelete_CarryWhatTheyName()
    {
        var trashing = new FormTrashingNotification(FormKey, "contact", "Contact", User, new EventMessages());
        var trashed = new FormTrashedNotification(FormKey, "contact", "Contact", User);
        var restoring = new FormRestoringNotification(FormKey, "contact", "Contact", Folder, User, new EventMessages());
        var restored = new FormRestoredNotification(FormKey, "contact", "Contact", null, User);
        var deleting = new FormDeletingNotification(FormKey, "contact", "Contact", null, new EventMessages());
        var deleted = new FormDeletedNotification(FormKey, "contact", "Contact", 12, 3, User);

        Assert.Multiple(() =>
        {
            Assert.That((trashing.FormKey, trashing.Alias, trashing.Name, trashing.UserKey), Is.EqualTo((FormKey, "contact", "Contact", (Guid?)User)));
            Assert.That((trashed.FormKey, trashed.Alias, trashed.Name, trashed.UserKey), Is.EqualTo((FormKey, "contact", "Contact", (Guid?)User)));
            Assert.That((restoring.FolderKey, restored.FolderKey), Is.EqualTo(((Guid?)Folder, (Guid?)null)), "the folder it goes back into, or the root");
            Assert.That((deleting.FormKey, deleting.UserKey), Is.EqualTo((FormKey, (Guid?)null)));
            Assert.That((deleted.EntryCount, deleted.FileCount, deleted.UserKey), Is.EqualTo((12L, 3L, (Guid?)User)));
            Assert.That(new[] { trashing.Cancel, restoring.Cancel, deleting.Cancel }, Is.All.False);
            Assert.That(() => new FormDeletedNotification(FormKey, "contact", "Contact", -1, 0, null), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new FormDeletedNotification(FormKey, "contact", "Contact", 0, -1, null), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new FormRestoredNotification(FormKey, string.Empty, "Contact", null, null), Throws.ArgumentException);
            Assert.That(() => new FormTrashingNotification(FormKey, "contact", string.Empty, null, new EventMessages()), Throws.ArgumentException);
        });
    }

    [Test]
    public void ANotification_NeedsTheFormsAliasAndName()
    {
        var messages = new EventMessages();

        Assert.Multiple(() =>
        {
            Assert.That(() => new FormSavingNotification(null!, "contact", "Contact", false, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FormPublishingNotification(null!, "contact", "Contact", null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FormUnpublishingNotification(FormKey, string.Empty, "Contact", null, messages), Throws.ArgumentException);
            Assert.That(() => new FormUnpublishingNotification(FormKey, "contact", null!, null, messages), Throws.ArgumentNullException);
            Assert.That(() => new FormRenamingNotification(FormKey, "a", "A", string.Empty, "B", null, messages), Throws.ArgumentException);
            Assert.That(() => new FormRenamingNotification(FormKey, "a", "A", "b", string.Empty, null, messages), Throws.ArgumentException);
            Assert.That(() => new FormSavedNotification(FormKey, string.Empty, "Contact", 1, false, null), Throws.ArgumentException);
            Assert.That(() => new FormPublishedNotification(FormKey, "contact", string.Empty, 1, 1, false, null), Throws.ArgumentException);
            Assert.That(() => new FormRenamedNotification(FormKey, "a", "A", null!, "B", null), Throws.ArgumentNullException);
            Assert.That(() => new FormRenamedNotification(FormKey, "a", "A", "b", string.Empty, null), Throws.ArgumentException);
            Assert.That(() => new FormMovedNotification(FormKey, "contact", string.Empty, null, null, null), Throws.ArgumentException);
        });
    }

    [Test]
    public void AHandler_CancelsAChange_WithItsMessage()
    {
        var messages = new EventMessages();
        var publishing = new FormPublishingNotification(new FormDefinition { Key = FormKey }, "contact", "Contact", User, messages);

        publishing.CancelOperation(new EventMessage("Review", "Not on Fridays.", EventMessageType.Error));

        Assert.Multiple(() =>
        {
            Assert.That(publishing.Cancel, Is.True);
            Assert.That(publishing.Messages, Is.SameAs(messages));
            Assert.That(new FormBuilderCancelledException(publishing.Messages).Messages, Is.EqualTo(new[] { new CancelMessage("Review", "Not on Fridays.") }));
        });
    }

    [Test]
    public async Task TheStructuralCheck_RefusesWhatPublishingRefuses_WithTheSitesLimits()
    {
        var field = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "email", Label = new LocalizedText("Email") };
        var definition = new FormDefinition
        {
            Key = FormKey,
            Pages =
            [
                new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [field, field with { Key = Guid.NewGuid() }] }] },
                new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] },
            ],
            Outcomes = [new FormOutcome { Key = Guid.NewGuid() }],
        };
        var lenient = new StructuralPublishCheck(DefinitionFixtures.Serializer(), TestFieldTypes.Registry);
        var strict = new StructuralPublishCheck(DefinitionFixtures.Serializer(new DefinitionLimits { MaxPages = 1 }), TestFieldTypes.Registry);

        Assert.Multiple(async () =>
        {
            Assert.That((await lenient.CheckAsync(definition, CancellationToken.None)).Select(problem => $"{problem.Code} at {problem.Path}"), Is.EqualTo(new[] { "alias_duplicate at $.pages[0].groups[0].fields[1].alias" }));
            Assert.That((await strict.CheckAsync(definition, CancellationToken.None)).Select(problem => problem.Code), Does.Contain(DefinitionErrors.LimitExceeded));
            Assert.That((await lenient.CheckAsync(definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [field] }] }] }, CancellationToken.None)), Is.Empty);
            Assert.That(async () => await lenient.CheckAsync(null!, CancellationToken.None), Throws.ArgumentNullException);
        });
    }
}
