using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Actions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.AuthorizationStatus;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Actions;

/// <summary>
/// What a content action needs of the user's own Umbraco rights beyond the Content section (ADR-0235), each gap the
/// management API answers as <c>content_rights</c>: create, update and publish where it writes, below the visitor's page
/// as well, every language for a document type that varies by culture, and the Media section and folder for files
/// (16.5's review: the integration tests held only the section and create and publish on a picked parent).
/// </summary>
[TestFixture]
public sealed class ContentActionRightsTests
{
    private static readonly Guid UserKey = Guid.Parse("40000000-0000-4000-8000-000000000001");
    private static readonly Guid Top = Guid.Parse("40000000-0000-4000-8000-000000000002");
    private static readonly Guid Folder = Guid.Parse("40000000-0000-4000-8000-000000000003");
    private static readonly Guid Upload = Guid.Parse("40000000-0000-4000-8000-000000000004");

    private IUserService _users = null!;
    private IContentPermissionService _content = null!;
    private IMediaPermissionService _media = null!;
    private IContentTypeService _types = null!;
    private ILanguageService _languages = null!;
    private IUser _user = null!;

    [SetUp]
    public void SetUp()
    {
        _user = Substitute.For<IUser>();
        _user.Key.Returns(UserKey);
        _user.AllowedSections.Returns([Constants.Applications.Content, Constants.Applications.Media]);
        _users = Substitute.For<IUserService>();
        _users.GetAsync(UserKey).Returns(_user);
        _content = Substitute.For<IContentPermissionService>();
        Content(ContentAuthorizationStatus.Success);
        _content.AuthorizeCultureAccessAsync(Arg.Any<IUser>(), Arg.Any<ISet<string>>()).Returns(ContentAuthorizationStatus.Success);
        _media = Substitute.For<IMediaPermissionService>();
        Media(MediaAuthorizationStatus.Success);
        _types = Substitute.For<IContentTypeService>();
        _languages = Substitute.For<ILanguageService>();
        var english = Substitute.For<ILanguage>();
        english.IsoCode.Returns("en-US");
        var german = Substitute.For<ILanguage>();
        german.IsoCode.Returns("de-DE");
        _languages.GetAllAsync().Returns([english, german]);
    }

    [Test]
    public async Task UpdatingAnItemOfTheSameName_NeedsUmbracosUpdate_Where_ItCreates()
    {
        _content.AuthorizeAccessAsync(Arg.Any<IUser>(), Arg.Any<IEnumerable<Guid>>(), Arg.Is<ISet<string>>(needed => needed.Contains(ActionUpdate.ActionLetter)))
            .Returns(ContentAuthorizationStatus.UnauthorizedMissingPermissionAccess);

        var creating = await MissingAsync(Settings(parent: Top));
        var updating = await MissingAsync(Settings(parent: Top, update: true));

        Assert.Multiple(() =>
        {
            Assert.That(creating, Is.Null);
            Assert.That(updating, Is.EqualTo(ContentRightsGap.Content));
            _content.Received().AuthorizeAccessAsync(_user, Arg.Is<IEnumerable<Guid>>(keys => keys.SequenceEqual(new[] { Top })), Arg.Is<ISet<string>>(needed => needed.SetEquals(new[] { ActionNew.ActionLetter, ActionUpdate.ActionLetter })));
        });
    }

    [Test]
    public async Task WithoutAPickedParent_TheContentRootIsWhereItCreates()
    {
        _content.AuthorizeRootAccessAsync(Arg.Any<IUser>(), Arg.Any<ISet<string>>()).Returns(ContentAuthorizationStatus.UnauthorizedMissingRootAccess);

        Assert.That(await MissingAsync(Settings(parent: null)), Is.EqualTo(ContentRightsGap.Content));
    }

    [TestCase(nameof(ContentParentSource.Page))]
    [TestCase(nameof(ContentParentSource.SiteRoot))]
    public async Task ForTheVisitorsPageOrSite_EveryItemBelowWhereTheReachStarts_CountsAsWell(string reach)
    {
        var source = Enum.Parse<ContentParentSource>(reach);
        _content.AuthorizeDescendantsAccessAsync(Arg.Any<IUser>(), Top, Arg.Any<ISet<string>>()).Returns(ContentAuthorizationStatus.UnauthorizedMissingDescendantAccess);

        var below = await MissingAsync(Settings(parent: Top, source: source));
        _content.AuthorizeDescendantsAccessAsync(Arg.Any<IUser>(), Top, Arg.Any<ISet<string>>()).Returns(ContentAuthorizationStatus.Success);
        var everywhere = await MissingAsync(Settings(parent: Top, source: source));
        var nowhere = await MissingAsync(Settings(parent: null, source: source));

        Assert.Multiple(() =>
        {
            Assert.That(below, Is.EqualTo(ContentRightsGap.Content), "one item below the start the user may not write to");
            Assert.That(everywhere, Is.Null);
            Assert.That(nowhere, Is.Null, "without the item its reach starts at, a run creates nothing");
        });
    }

    [Test]
    public async Task ADocumentTypeThatVariesByCulture_NeedsEveryLanguageOfTheSite()
    {
        var varying = Substitute.For<IContentType>();
        varying.Variations.Returns(ContentVariation.Culture);
        _types.Get("jfbStory").Returns(varying);
        var invariant = Substitute.For<IContentType>();
        invariant.Variations.Returns(ContentVariation.Nothing);
        _types.Get("jfbNote").Returns(invariant);
        _content.AuthorizeCultureAccessAsync(Arg.Any<IUser>(), Arg.Any<ISet<string>>()).Returns(ContentAuthorizationStatus.UnauthorizedMissingCulture);

        var story = await MissingAsync(Settings(parent: Top));
        var note = await MissingAsync(Settings(parent: Top, type: "jfbNote"));

        Assert.Multiple(() =>
        {
            Assert.That(story, Is.EqualTo(ContentRightsGap.Languages));
            Assert.That(note, Is.Null, "an invariant type asks for no language");
            _content.Received(1).AuthorizeCultureAccessAsync(_user, Arg.Is<ISet<string>>(cultures => cultures.SetEquals(new[] { "en-US", "de-DE" })));
        });
    }

    [Test]
    public async Task StoringUploadedFiles_NeedsTheMediaSection_AndTheFolder_OrTheMediaRootWithoutOne()
    {
        var mapping = JsonDocument.Parse($$"""[{"property":"photo","field":"{{Upload}}"}]""").RootElement;
        var withFiles = Settings(parent: Top, mapping: mapping);
        var noFiles = Settings(parent: Top);

        var plain = await MissingAsync(noFiles);
        _user.AllowedSections.Returns([Constants.Applications.Content]);
        var withoutSection = await MissingAsync(withFiles);
        var folderWithoutSection = await MissingAsync(Settings(parent: Top, folder: Folder));
        _user.AllowedSections.Returns([Constants.Applications.Content, Constants.Applications.Media]);
        Media(MediaAuthorizationStatus.UnauthorizedMissingPathAccess);
        _media.AuthorizeRootAccessAsync(Arg.Any<IUser>()).Returns(MediaAuthorizationStatus.UnauthorizedMissingRootAccess);
        var folderRefused = await MissingAsync(Settings(parent: Top, folder: Folder, mapping: mapping));
        var rootRefused = await MissingAsync(withFiles);
        Media(MediaAuthorizationStatus.NotFound);
        var folderGone = await MissingAsync(Settings(parent: Top, folder: Folder, mapping: mapping));

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.Null, "an action that stores no file asks nothing of media");
            Assert.That(withoutSection, Is.EqualTo(ContentRightsGap.MediaSection));
            Assert.That(folderWithoutSection, Is.EqualTo(ContentRightsGap.MediaSection), "a picked folder counts as storing files");
            Assert.That(folderRefused, Is.EqualTo(ContentRightsGap.Media));
            Assert.That(rootRefused, Is.EqualTo(ContentRightsGap.Media), "without a folder, the media root");
            Assert.That(folderGone, Is.Null, "a folder that is gone fails where the action runs, and its form check says so");
        });
    }

    private async Task<ContentRightsGap?> MissingAsync(ContentActionSettings settings) =>
        await new ContentActionRights(_users, _content, _media, _types, _languages).MissingAsync(UserKey, settings, Form);

    private void Content(ContentAuthorizationStatus status)
    {
        _content.AuthorizeAccessAsync(Arg.Any<IUser>(), Arg.Any<IEnumerable<Guid>>(), Arg.Any<ISet<string>>()).Returns(status);
        _content.AuthorizeRootAccessAsync(Arg.Any<IUser>(), Arg.Any<ISet<string>>()).Returns(status);
        _content.AuthorizeDescendantsAccessAsync(Arg.Any<IUser>(), Arg.Any<Guid>(), Arg.Any<ISet<string>>()).Returns(status);
    }

    // Both of Umbraco's ways to ask about one media item, whichever the checker takes.
    private void Media(MediaAuthorizationStatus status)
    {
        _media.AuthorizeAccessAsync(Arg.Any<IUser>(), Arg.Any<Guid>()).Returns(status);
        _media.AuthorizeAccessAsync(Arg.Any<IUser>(), Arg.Any<IEnumerable<Guid>>()).Returns(status);
        _media.AuthorizeRootAccessAsync(Arg.Any<IUser>()).Returns(status);
    }

    private static ContentActionSettings Settings(Guid? parent, ContentParentSource source = ContentParentSource.Picked, bool update = false, string type = "jfbStory", Guid? folder = null, JsonElement? mapping = null) => new()
    {
        DocumentType = type,
        ParentSource = source,
        Parent = parent,
        Name = "Story",
        UpdateExisting = update,
        MediaFolder = folder,
        Mapping = mapping,
    };

    // A form with a file field, which a mapping to it stores in the media library.
    private static FormDefinition Form { get; } = new()
    {
        Key = Guid.NewGuid(),
        Pages =
        [
            new FormPage
            {
                Key = Guid.NewGuid(),
                Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [new FormField { Key = Upload, Type = "file", Alias = "photo" }] }],
            },
        ],
    };
}
