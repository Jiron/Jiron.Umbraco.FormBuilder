using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Setup;

/// <summary>
/// What knows when each step of the setup checklist is done (section 4.13, ADR-0165), and the checklist that asks them: who may do
/// each step, and that a complete setup stays complete.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SetupDetectorsTests
{
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IFormBuilderSettingsService _settings = Substitute.For<IFormBuilderSettingsService>();
    private readonly IOptionsMonitor<GlobalSettings> _global = Substitute.For<IOptionsMonitor<GlobalSettings>>();
    private readonly IFormRepository _forms = Substitute.For<IFormRepository>();
    private readonly IReferenceRepository _references = Substitute.For<IReferenceRepository>();
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly IFormBuilderAccessService _access = Substitute.For<IFormBuilderAccessService>();
    private readonly IKeyValueRepository _values = Substitute.For<IKeyValueRepository>();
    private readonly IDocumentNavigationQueryService _navigation = Substitute.For<IDocumentNavigationQueryService>();
    private readonly IUserGroupService _groups = Substitute.For<IUserGroupService>();

    /// <summary>Nothing is set up to begin with: NSubstitute would answer with a made-up type and an empty row.</summary>
    public SetupDetectorsTests()
    {
        _contentTypes.Get(Arg.Any<string>()).Returns((IContentType?)null);
        _values.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
    }

    private SetupDetectors Detectors => new(new EmailHealth(_sender, _settings, _global, _navigation), _forms, _references, _contentTypes, _access, _groups);

    private SetupChecklist Checklist => new(Detectors, _access, _forms, _values);

    private void SiteAddress(string? settingsAddress, string? umbracoAddress = null)
    {
        _settings.GetAsync<EmailSettings>(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            new SettingsSnapshot<EmailSettings>(new EmailSettings { SenderAddress = settingsAddress }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        _global.CurrentValue.Returns(new GlobalSettings { Smtp = new SmtpSettings { From = umbracoAddress! } });
    }

    [TestCase(true, null, "Forms <forms@jfb.test>", false, TestName = "The email step is open with a display name in Umbraco's address, which mail refuses")]
    [TestCase(true, "forms@jfb.test", null, true, TestName = "The email step is done with a mail server and the settings' address")]
    [TestCase(true, null, "site@jfb.test", true, TestName = "The email step is done with a mail server and Umbraco's own address")]
    [TestCase(true, null, null, false, TestName = "The email step is open without an address, which Umbraco's gate does not check")]
    [TestCase(true, " ", " ", false, TestName = "The email step is open with a blank address")]
    [TestCase(false, "forms@jfb.test", null, false, TestName = "The email step is open without a mail server")]
    public async Task TheEmailStep(bool canSend, string? settingsAddress, string? umbracoAddress, bool done)
    {
        _sender.CanSendRequiredEmail().Returns(canSend);
        SiteAddress(settingsAddress, umbracoAddress);

        Assert.That(await Detectors.EmailAsync(), Is.EqualTo(done));
    }

    [TestCase(true, false, true, false)]
    [TestCase(false, false, false, false)]
    [TestCase(false, true, false, true)]
    [TestCase(true, true, true, true)]
    public async Task TheFormStep_ReadsAFormOutsideTheTrash_AndTheEntryStep_AnEntryEver(bool anyForm, bool anyEntry, bool formDone, bool entryDone)
    {
        _forms.GetSetupFactsAsync(Arg.Any<CancellationToken>()).Returns(new FormSetupFacts(anyForm, anyEntry));

        Assert.Multiple(async () =>
        {
            Assert.That(await Detectors.FormAsync(), Is.EqualTo(formDone));
            Assert.That(await Detectors.TestEntryAsync(), Is.EqualTo(entryDone));
        });
    }

    [Test]
    public async Task TheBlockStep_IsOpen_WithNoElementTypeAndNoPageUsingAForm()
    {
        Assert.That(await Detectors.FormBlockAsync(), Is.False);
    }

    [Test]
    public async Task TheBlockStep_IsDone_OnceTheElementTypeExists()
    {
        _contentTypes.Get(FormBlockTypes.ElementTypeAlias).Returns(Substitute.For<IContentType>());

        Assert.That(await Detectors.FormBlockAsync(), Is.True);
    }

    [Test]
    public async Task TheBlockStep_IsDone_WhenAPageUsesAFormThroughASitesOwnType()
    {
        _references.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);

        Assert.That(await Detectors.FormBlockAsync(), Is.True);
    }

    [Test]
    public async Task TheEmailStep_IsDone_WithAnAddressForOneSite()
    {
        var site = Guid.NewGuid();
        _sender.CanSendRequiredEmail().Returns(true);
        SiteAddress(null);
        _settings.GetAsync<EmailSettings>(site, Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            new SettingsSnapshot<EmailSettings>(new EmailSettings { SenderAddress = "site@jfb.test" }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        _navigation.TryGetRootKeys(out Arg.Any<IEnumerable<Guid>>()).Returns(call =>
        {
            call[0] = new[] { site };
            return true;
        });

        Assert.That(await Detectors.EmailAsync(), Is.True);
    }

    private void GroupHoldsViewEntries(params string[] sections)
    {
        var key = Guid.NewGuid();
        _access.ListGroupsHoldingAsync(FormBuilderPermissions.ViewEntries, Arg.Any<CancellationToken>()).Returns([key]);
        var group = Substitute.For<IUserGroup>();
        group.AllowedSections.Returns(sections);
        _groups.GetAsync(key).Returns(group);
    }

    [Test]
    public async Task TheAccessStep_IsOpen_WithNoGroupThatViewsEntries()
    {
        _access.ListGroupsHoldingAsync(FormBuilderPermissions.ViewEntries, Arg.Any<CancellationToken>()).Returns([]);

        Assert.That(await Detectors.AccessAsync(), Is.False);
    }

    [Test]
    public async Task TheAccessStep_IsDone_WithAGroupThatCanReachTheSection()
    {
        GroupHoldsViewEntries("content", FormBuilderConstants.SectionAlias);

        Assert.That(await Detectors.AccessAsync(), Is.True);
    }

    [Test]
    public async Task TheAccessStep_StaysOpen_ForAGroupWithoutTheSection_WhoseGrantGivesItsUsersNothing()
    {
        GroupHoldsViewEntries("content");

        Assert.That(await Detectors.AccessAsync(), Is.False);
    }

    private static readonly AccessUser Someone = new(Guid.NewGuid(), new HashSet<Guid>(), false, true);

    private void Holds(FormBuilderPermissions global, FormBuilderPermissions anywhere, StoredForm? preview = null, FormBuilderPermissions? onFolders = null)
    {
        _access.GetGlobalAsync(Someone, Arg.Any<CancellationToken>()).Returns(global);
        _access.GetOnFoldersAsync(Someone, Arg.Any<CancellationToken>()).Returns(onFolders ?? anywhere);
        _access.GetFormVisibilityAsync(Someone, FormBuilderPermissions.Design, Arg.Any<CancellationToken>())
            .Returns(anywhere.HasFlag(FormBuilderPermissions.Design) ? FormVisibility.Everything : FormVisibility.Nothing);
        _forms.GetNewestAsync(FormVisibility.Everything, Arg.Any<CancellationToken>()).Returns(preview);
        _forms.GetSetupFactsAsync(Arg.Any<CancellationToken>()).Returns(new FormSetupFacts(false, false));
        SiteAddress(null);
    }

    private static bool CanDo(Checklist checklist, ChecklistStep step) => checklist.Items.Single(item => item.Step == step).CanDo;

    [Test]
    public async Task EachStep_SaysWhoMayDoIt()
    {
        Holds(FormBuilderPermissions.ManageSettings, FormBuilderPermissions.ManageSettings);
        var settings = await Checklist.GetAsync(Someone, hasSettingsSection: true);
        var noSection = await Checklist.GetAsync(Someone, hasSettingsSection: false);
        Holds(FormBuilderPermissions.None, FormBuilderPermissions.Design);
        var folderDesigner = await Checklist.GetAsync(Someone, hasSettingsSection: true);
        Holds(FormBuilderPermissions.ManagePermissions, FormBuilderPermissions.ManagePermissions);
        var permissions = await Checklist.GetAsync(Someone, hasSettingsSection: true);
        Holds(FormBuilderPermissions.None, FormBuilderPermissions.Design, onFolders: FormBuilderPermissions.None);
        var formDesigner = await Checklist.GetAsync(Someone, hasSettingsSection: true);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Items.Select(item => item.Step), Is.EqualTo(Enum.GetValues<ChecklistStep>()), "in their order");
            Assert.That(CanDo(settings, ChecklistStep.Email), Is.True);
            Assert.That(CanDo(settings, ChecklistStep.FormBlock), Is.True);
            Assert.That(CanDo(settings, ChecklistStep.Form), Is.False);
            Assert.That(CanDo(settings, ChecklistStep.Access), Is.False);
            Assert.That(CanDo(noSection, ChecklistStep.FormBlock), Is.False, "creating Umbraco's types needs Umbraco's Settings section");
            Assert.That(CanDo(folderDesigner, ChecklistStep.Form), Is.True, "on one folder is enough");
            Assert.That(CanDo(folderDesigner, ChecklistStep.Email), Is.False, "settings are granted globally only");
            Assert.That(CanDo(permissions, ChecklistStep.Access), Is.True);
            Assert.That(CanDo(formDesigner, ChecklistStep.Form), Is.False, "a grant on one form creates nothing");
        });
    }

    [Test]
    public async Task ThePreviewStep_NamesTheNewestFormTheUserMayDesign_AndNeedsOne()
    {
        var form = TestForm();
        Holds(FormBuilderPermissions.None, FormBuilderPermissions.Design, form);
        var designer = await Checklist.GetAsync(Someone, hasSettingsSection: false);
        Holds(FormBuilderPermissions.None, FormBuilderPermissions.Design);
        var noForm = await Checklist.GetAsync(Someone, hasSettingsSection: false);
        Holds(FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, form);
        var viewer = await Checklist.GetAsync(Someone, hasSettingsSection: false);

        Assert.Multiple(() =>
        {
            Assert.That(designer.PreviewForm, Is.EqualTo(form.Key));
            Assert.That(CanDo(designer, ChecklistStep.TestEntry), Is.True);
            Assert.That(noForm.PreviewForm, Is.Null);
            Assert.That(CanDo(noForm, ChecklistStep.TestEntry), Is.False);
            Assert.That(viewer.PreviewForm, Is.Null, "a form the user may not design stays unnamed");
            Assert.That(CanDo(viewer, ChecklistStep.TestEntry), Is.False);
        });
    }

    private void EveryStepDone()
    {
        _sender.CanSendRequiredEmail().Returns(true);
        SiteAddress("forms@jfb.test");
        _forms.GetSetupFactsAsync(Arg.Any<CancellationToken>()).Returns(new FormSetupFacts(true, true));
        _references.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        GroupHoldsViewEntries(FormBuilderConstants.SectionAlias);
    }

    [Test]
    public async Task ACompleteSetup_IsWrittenDown_AndStaysComplete_WhenAStepOpensAgain()
    {
        _access.GetFormVisibilityAsync(Someone, FormBuilderPermissions.Design, Arg.Any<CancellationToken>()).Returns(FormVisibility.Nothing);
        Holds(FormBuilderPermissions.None, FormBuilderPermissions.None);
        var open = await Checklist.GetAsync(Someone, false);
        await _values.DidNotReceiveWithAnyArgs().TryAddAsync(default!, default);

        EveryStepDone();
        var complete = await Checklist.GetAsync(Someone, false);
        await _values.Received(1).TryAddAsync(SetupChecklist.CompleteKey, "1", Arg.Any<CancellationToken>());

        _values.GetAsync(SetupChecklist.CompleteKey, Arg.Any<CancellationToken>()).Returns("1");
        _forms.GetSetupFactsAsync(Arg.Any<CancellationToken>()).Returns(new FormSetupFacts(false, true));
        var later = await Checklist.GetAsync(Someone, false);

        Assert.Multiple(() =>
        {
            Assert.That(open.Complete, Is.False);
            Assert.That(complete.Complete, Is.True);
            Assert.That(complete.Items.All(item => item.Done), Is.True);
            Assert.That(later.Complete, Is.True, "the last form deleted brings no checklist back");
            Assert.That(later.Items.Single(item => item.Step == ChecklistStep.Form).Done, Is.False, "shown on request, it says how things are");
        });
    }

    private static StoredForm TestForm() => SetupTestForms.Form();
}
