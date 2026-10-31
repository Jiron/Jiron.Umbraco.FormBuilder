using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The form picker's endpoints (ROADMAP 6.7e, ADR-0013, ADR-0133), authorised by content access: an editor with the
/// Content section and without the Forms section lists, searches and looks up the published forms that are not
/// restricted, never an unpublished, trashed or restricted one, and the folders that lead to them; a user whose group
/// holds a grant on a restricted folder or form gets those too; a data type's allowed folders (and below) and allowed
/// forms limit every answer; the themes are those the data type's settings accept; and a user with none of Umbraco's
/// Content, Media, Members and Settings sections is refused.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PickerApiTests : ManagementApiTestBase
{
    // The editor: Umbraco's writers, with the Content section and without the Forms section.
    private const string Editor = "outsider";

    private Guid _events;
    private Guid _archive;
    private Guid _news;
    private Guid _hr;
    private Guid _contact;
    private Guid _spring;
    private Guid _autumn;
    private Guid _weekly;
    private Guid _payroll;
    private Guid _secret;
    private Guid _draft;
    private Guid _old;

    // Events (Archive below it), News and HR (restricted) at the root, and Planned, which holds only a draft. Published:
    // Contact and Secret (restricted) at the root, Spring in Events, Autumn in Archive, Weekly in News, Payroll in HR;
    // Draft at the root is never published, and Old went to the trash.
    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        _events = await FolderAsync("Events");
        _archive = await FolderAsync("Archive", _events);
        _news = await FolderAsync("News");
        _hr = await FolderAsync("HR");
        await RestrictAsync(_hr, "hr", FormBuilderPermissions.ViewEntries);
        var planned = await FolderAsync("Planned");
        _contact = await PublishedAsync("Contact");
        _spring = await PublishedAsync("Spring", _events);
        _autumn = await PublishedAsync("Autumn", _archive);
        _weekly = await PublishedAsync("Weekly", _news);
        _payroll = await PublishedAsync("Payroll", _hr);
        _secret = await PublishedAsync("Secret");
        var forms = GetRequiredService<IFormBuilderFormService>();
        Assert.That(await GetRequiredService<IFormBuilderAccessService>().RestrictFormAsync(_secret, (await forms.GetAsync(_secret))!.ConcurrencyVersion, true, null), Is.Not.Null);
        _draft = await FormAsync("Draft");
        await FormAsync("Unannounced", planned);
        _old = await PublishedAsync("Old");
        await TrashFormAsync(_old);
    }

    [Test]
    public async Task AnEditorWithoutTheFormsSection_GetsThePublishedFormsThatAreNotRestricted_AndTheFoldersThatLeadToThem()
    {
        var root = await CallAsync(Editor, HttpMethod.Get, "picker/children");
        var events = await CallAsync(Editor, HttpMethod.Get, $"picker/children?parentKey={_events}");
        var archive = await CallAsync(Editor, HttpMethod.Get, $"picker/children?parentKey={_archive}");
        var hr = await CallAsync(Editor, HttpMethod.Get, $"picker/children?parentKey={_hr}");
        var search = await CallAsync(Editor, HttpMethod.Get, "picker/search?text=o");
        var looked = await CallAsync(Editor, HttpMethod.Get, Keys(_payroll, _spring, _draft, _old, _secret, _contact, Guid.NewGuid()));
        var paged = await CallAsync(Editor, HttpMethod.Get, "picker/children?skip=1&take=1");

        Assert.Multiple(() =>
        {
            Assert.That(root.Status, Is.EqualTo(HttpStatusCode.OK), root.Text);
            Assert.That(Items(root), Is.EqualTo(new[] { "folder Events +", "folder News +", "form Contact" }), "HR restricted, Planned without a published form, Draft unpublished, Secret restricted, Old trashed");
            Assert.That(Items(events), Is.EqualTo(new[] { "folder Archive +", "form Spring" }));
            Assert.That(Items(archive), Is.EqualTo(new[] { "form Autumn" }));
            Assert.That(hr.Outcome, Is.EqualTo("404 folder_not_found"), "a restricted folder is none to this editor");
            Assert.That(Items(search), Is.EqualTo(new[] { "form Contact" }), "by name or alias, never Old, Draft, Payroll or Secret");
            Assert.That(Items(looked), Is.EqualTo(new[] { "form Spring", "form Contact" }), "in the order asked, the others left out");
            Assert.That((Items(paged), paged.Body!["total"]!.GetValue<long>()), Is.EqualTo((new[] { "folder News +" }, 3L)));
            Assert.That(looked.Body!.AsArray()[1]!["alias"]!.GetValue<string>(), Is.EqualTo("contact"), "a form's alias");
            Assert.That(root.Text, Does.Not.Contain("permissions").And.Not.Contain("entries"), "names and keys only");
        });
    }

    [Test]
    public async Task AUserWhoseGroupHoldsAGrant_AlsoGetsTheRestrictedFolderAndFormItCovers()
    {
        var group = await GroupAsync("jfbHrEditors", Constants.Applications.Content, AccessPersonas.Section);
        await GrantAsync(group, PermissionTarget.Folder, _hr, FormBuilderPermissions.ViewEntries);
        await GrantAsync(group, PermissionTarget.Form, _secret, FormBuilderPermissions.ViewEntries);
        var caller = new ManagementApiCaller(Client, Services, _ => ["jfbHrEditors"]);

        var root = await caller.CallAsync("hrEditor", HttpMethod.Get, "picker/children");
        var hr = await caller.CallAsync("hrEditor", HttpMethod.Get, $"picker/children?parentKey={_hr}");
        var looked = await caller.CallAsync("hrEditor", HttpMethod.Get, Keys(_payroll, _secret, _draft));

        Assert.Multiple(() =>
        {
            Assert.That(Items(root), Is.EqualTo(new[] { "folder Events +", "folder HR +", "folder News +", "form Contact", "form Secret" }), "in the tree's order");
            Assert.That(Items(hr), Is.EqualTo(new[] { "form Payroll" }));
            Assert.That(Items(looked), Is.EqualTo(new[] { "form Payroll", "form Secret" }), "still never a form without a published version");
        });
    }

    [Test]
    public async Task ADataTypesAllowedFoldersAndForms_LimitEveryAnswer()
    {
        var events = await PickerTypeAsync("Events picker", folders: [_events], forms: [_contact]);
        var archive = await PickerTypeAsync("Archive picker", folders: [_archive, _events]);
        var weekly = await PickerTypeAsync("Weekly picker", forms: [_weekly]);
        var text = (await GetRequiredService<IDataTypeService>().GetAsync(Constants.DataTypes.Guids.TextstringGuid))!.Key;

        var root = await CallAsync(Editor, HttpMethod.Get, $"picker/children?dataTypeKey={events}");
        var below = await CallAsync(Editor, HttpMethod.Get, $"picker/children?parentKey={_archive}&dataTypeKey={events}");
        var outside = await CallAsync(Editor, HttpMethod.Get, $"picker/children?parentKey={_news}&dataTypeKey={events}");
        var search = await CallAsync(Editor, HttpMethod.Get, $"picker/search?text=n&dataTypeKey={events}");
        var looked = await CallAsync(Editor, HttpMethod.Get, Keys(_weekly, _autumn, _contact) + $"&dataTypeKey={events}");
        var nested = await CallAsync(Editor, HttpMethod.Get, $"picker/children?dataTypeKey={archive}");
        var single = await CallAsync(Editor, HttpMethod.Get, $"picker/children?dataTypeKey={weekly}");
        var none = await CallAsync(Editor, HttpMethod.Get, $"picker/children?dataTypeKey={text}");
        var missing = await CallAsync(Editor, HttpMethod.Get, $"picker/search?text=e&dataTypeKey={Guid.NewGuid()}");

        Assert.Multiple(() =>
        {
            Assert.That(Items(root), Is.EqualTo(new[] { "folder Events +", "form Contact" }), "the allowed folder and the allowed form");
            Assert.That(Items(below), Is.EqualTo(new[] { "form Autumn" }), "below an allowed folder");
            Assert.That(outside.Outcome, Is.EqualTo("404 folder_not_found"), "a folder the settings do not allow");
            Assert.That(Items(search), Is.EqualTo(new[] { "form Autumn", "form Contact", "form Spring" }), "the allowed form and those in or below the allowed folder; Weekly is outside");
            Assert.That(Items(looked), Is.EqualTo(new[] { "form Autumn", "form Contact" }));
            Assert.That(Items(nested), Is.EqualTo(new[] { "folder Events +" }), "an allowed folder below another shows once, inside it");
            Assert.That(Items(single), Is.EqualTo(new[] { "form Weekly" }));
            Assert.That((none.Outcome, missing.Outcome), Is.EqualTo(("404 data_type_not_found", "404 data_type_not_found")), "a data type that is no picker's, and none at all");
        });
    }

    [Test]
    public async Task TheThemes_AreThoseThePickersSettingsAccept()
    {
        var answer = await CallAsync(Editor, HttpMethod.Get, "picker/themes");
        var themes = GetRequiredService<FormBuilderThemes>().All;
        var aliases = answer.Body!.AsArray().Select(theme => theme!["alias"]!.GetValue<string>()).ToList();
        var accepted = new List<string>();
        foreach (var alias in aliases)
        {
            var created = await GetRequiredService<IDataTypeService>().CreateAsync(NewPickerType($"Picker in {alias}", new Dictionary<string, object> { ["theme"] = alias }), Constants.Security.SuperUserKey);
            if (created.Success)
            {
                accepted.Add(alias);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.Text);
            Assert.That(aliases, Is.EqualTo(themes.Select(theme => theme.Name)), "every theme, in the backoffice's order");
            Assert.That(answer.Body!.AsArray().Select(theme => theme!["label"]!.GetValue<string>()), Is.EqualTo(themes.Select(theme => theme.Label)));
            Assert.That(accepted, Is.EqualTo(aliases), "each one accepted by the settings");
        });
    }

    [Test]
    public async Task AUserWithNoneOfTheFourSections_IsRefused_AndOneWithTheSettingsSectionOnlyIsAnswered()
    {
        await GroupAsync("jfbSettingsOnly", Constants.Applications.Settings);
        var settings = new ManagementApiCaller(Client, Services, _ => ["jfbSettingsOnly"]);
        string[] paths = ["picker/children", "picker/search?text=a", Keys(_contact), "picker/themes"];

        var refused = new List<string>();
        var answered = new List<string>();
        foreach (var path in paths)
        {
            var answer = await CallAsync("noAccess", HttpMethod.Get, path);
            refused.Add($"{path}: {answer.Outcome} {answer.Body?["section"]?.GetValue<string>()}");
            answered.Add($"{path}: {(await settings.CallAsync("settingsKeeper", HttpMethod.Get, path)).Outcome}");
        }

        var anonymous = await CallAsync(null, HttpMethod.Get, "picker/themes");

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.All.EndWith(": 403 forbidden content"), "the Forms section alone is not content access");
            Assert.That(answered, Is.All.EndWith(": 200"));
            Assert.That(anonymous.Status, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task ValuesTheEndpointsCannotRead_AreRefused()
    {
        var take = await CallAsync(Editor, HttpMethod.Get, "picker/children?take=0");
        var text = await CallAsync(Editor, HttpMethod.Get, "picker/search");
        var many = await CallAsync(Editor, HttpMethod.Get, Keys([.. Enumerable.Range(0, FormBuilderPickerService.MaxKeys + 1).Select(_ => Guid.NewGuid())]));
        var none = await CallAsync(Editor, HttpMethod.Get, "picker/forms");

        Assert.Multiple(() =>
        {
            Assert.That((take.Outcome, text.Outcome, many.Outcome), Is.EqualTo(("400 invalid_request", "400 invalid_request", "400 invalid_request")));
            Assert.That((none.Status, none.Text), Is.EqualTo((HttpStatusCode.OK, "[]")), "no keys, no forms");
        });
    }

    // A form created through the form service and published.
    private async Task<Guid> PublishedAsync(string name, Guid? folderKey = null)
    {
        var key = await FormAsync(name, folderKey);
        var forms = GetRequiredService<IFormBuilderFormService>();
        Assert.That(await forms.PublishAsync(key, (await forms.GetAsync(key))!.ConcurrencyVersion, null, null), Is.Not.Null);
        return key;
    }

    // A user group with these sections.
    private async Task<Guid> GroupAsync(string alias, params string[] sections)
    {
        await using var scope = Services.CreateAsyncScope();
        var wanted = new TestUserGroup { Alias = alias, Name = alias };
        foreach (var section in sections)
        {
            wanted.Sections.Add(section);
        }

        return (await scope.ServiceProvider.GetRequiredService<TestPeople>().EnsureUserGroupAsync(wanted)).Key;
    }

    private DataType NewPickerType(string name, Dictionary<string, object> settings) =>
        new(GetRequiredService<PropertyEditorCollection>()[FormPickerPropertyEditor.EditorAlias]!, GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Name = name,
            DatabaseType = ValueStorageType.Nvarchar,
            ConfigurationData = settings,
        };

    // A picker's data type that allows these folders and forms.
    private async Task<Guid> PickerTypeAsync(string name, Guid[]? folders = null, Guid[]? forms = null)
    {
        var settings = new Dictionary<string, object>();
        if (folders is not null)
        {
            settings["allowedFolders"] = folders.Select(key => key.ToString("D")).ToArray();
        }

        if (forms is not null)
        {
            settings["allowedForms"] = forms.Select(key => key.ToString("D")).ToArray();
        }

        var created = await GetRequiredService<IDataTypeService>().CreateAsync(NewPickerType(name, settings), Constants.Security.SuperUserKey);
        Assert.That(created.Success, Is.True, created.Status.ToString());
        return created.Result!.Key;
    }

    private static string Keys(params Guid[] keys) => "picker/forms?" + string.Join("&", keys.Select(key => $"keys={key}"));

    // Each item as "kind name", a folder that leads further marked " +".
    private static string[] Items(ApiAnswer answer)
    {
        var items = answer.Body is System.Text.Json.Nodes.JsonArray list ? list : answer.Body?["items"]?.AsArray();
        return [.. (items ?? []).Select(item => $"{item!["kind"]!.GetValue<string>()} {item["name"]!.GetValue<string>()}{(item["hasChildren"]!.GetValue<bool>() ? " +" : string.Empty)}")];
    }
}
