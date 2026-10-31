using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// "Create a form block type for me" (ROADMAP 6.7b, section 4.7): <c>POST setup/form-block</c> creates the element type
/// <c>Form</c> with its form picker property and the form picker's data type on the first call, and changes nothing on a
/// second; an element type or a data type that is there stays as it is; and a user with <c>manageSettings</c> and the
/// Forms section but without Umbraco's Settings section is refused with <c>403</c>, while one with all three is answered.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormBlockApiTests : ManagementApiTestBase
{
    private const string Path = "setup/form-block";

    [Test]
    public async Task TheFirstCall_CreatesTheElementTypeAndTheDataType_AndASecondChangesNothing()
    {
        var first = await CallAsync("admin", HttpMethod.Post, Path);
        var type = GetRequiredService<IContentTypeService>().Get(FormBlockTypes.ElementTypeAlias)!;
        var updated = type.UpdateDate;
        var second = await CallAsync("admin", HttpMethod.Post, Path);
        var pickers = (await GetRequiredService<IDataTypeService>().GetAllAsync()).Count(dataType => dataType.EditorAlias == FormPickerPropertyEditor.EditorAlias);
        var dataType = await GetRequiredService<IDataTypeService>().GetAsync(FormBlockTypes.DataTypeKey);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(HttpStatusCode.OK), first.Text);
            Assert.That(Part(first, "elementType"), Is.EqualTo((type.Key.ToString(), FormBlockTypes.ElementTypeAlias, true)));
            Assert.That(Part(first, "dataType"), Is.EqualTo((FormBlockTypes.DataTypeKey.ToString(), FormBlockTypes.DataTypeName, true)));
            Assert.That((type.IsElement, type.Name), Is.EqualTo((true, FormBlockTypes.ElementTypeName)));
            var property = type.PropertyTypes.Single();
            Assert.That((property.Alias, property.PropertyEditorAlias, property.DataTypeKey, property.Mandatory), Is.EqualTo((FormBlockTypes.PropertyAlias, FormPickerPropertyEditor.EditorAlias, FormBlockTypes.DataTypeKey, true)));
            Assert.That(second.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Part(second, "elementType"), Is.EqualTo((type.Key.ToString(), FormBlockTypes.ElementTypeAlias, false)));
            Assert.That(Part(second, "dataType"), Is.EqualTo((FormBlockTypes.DataTypeKey.ToString(), FormBlockTypes.DataTypeName, false)));
            Assert.That(GetRequiredService<IContentTypeService>().Get(FormBlockTypes.ElementTypeAlias)!.UpdateDate, Is.EqualTo(updated), "the element type untouched");
            Assert.That(pickers, Is.EqualTo(1), "one data type");
            Assert.That(dataType?.EditorUiAlias, Is.EqualTo(FormPickerPropertyEditor.EditorUiAlias), "the backoffice shows it with the picker (ADR-0134)");
        });
    }

    [Test]
    public async Task ATypeOrADataTypeThatIsThere_StaysAsItIs()
    {
        var shortStrings = GetRequiredService<IShortStringHelper>();
        var dataTypes = GetRequiredService<IDataTypeService>();
        var editor = GetRequiredService<PropertyEditorCollection>()[FormPickerPropertyEditor.EditorAlias]!;
        var changed = new DataType(editor, GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Key = FormBlockTypes.DataTypeKey,
            Name = "Our form picker",
            DatabaseType = ValueStorageType.Nvarchar,
            ConfigurationData = new Dictionary<string, object> { ["multiple"] = true },
        };
        Assert.That((await dataTypes.CreateAsync(changed, Constants.Security.SuperUserKey)).Success, Is.True);
        var text = (await dataTypes.GetAsync(Constants.DataTypes.Guids.TextstringGuid))!;
        var page = new ContentType(shortStrings, -1) { Alias = FormBlockTypes.ElementTypeAlias, Name = "A page of ours" };
        page.AddPropertyType(new PropertyType(shortStrings, text, "title") { Name = "Title" });
        Assert.That((await GetRequiredService<IContentTypeService>().CreateAsync(page, Constants.Security.SuperUserKey)).Success, Is.True);

        var answer = await CallAsync("admin", HttpMethod.Post, Path);
        var kept = GetRequiredService<IContentTypeService>().Get(FormBlockTypes.ElementTypeAlias)!;
        var keptData = (await dataTypes.GetAsync(FormBlockTypes.DataTypeKey))!;

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.Text);
            Assert.That(Part(answer, "elementType"), Is.EqualTo((page.Key.ToString(), FormBlockTypes.ElementTypeAlias, false)));
            Assert.That(Part(answer, "dataType"), Is.EqualTo((FormBlockTypes.DataTypeKey.ToString(), "Our form picker", false)));
            Assert.That((kept.Name, kept.IsElement, string.Join(",", kept.PropertyTypes.Select(property => property.Alias))), Is.EqualTo(("A page of ours", false, "title")));
            Assert.That((keptData.Name, keptData.ConfigurationObject is FormPickerConfiguration { Multiple: true }), Is.EqualTo(("Our form picker", true)));
        });
    }

    [Test]
    public async Task AUserWithManageSettingsAndTheFormsSection_ButNotUmbracosSettingsSection_IsRefused()
    {
        await using var scope = Services.CreateAsyncScope();
        var people = scope.ServiceProvider.GetRequiredService<TestPeople>();
        var access = scope.ServiceProvider.GetRequiredService<IFormBuilderAccessService>();
        foreach (var (alias, sections) in new[] { ("jfbSettingsKeepers", new[] { AccessPersonas.Section }), ("jfbSiteKeepers", new[] { AccessPersonas.Section, Constants.Applications.Settings }) })
        {
            var wanted = new TestUserGroup { Alias = alias, Name = alias };
            foreach (var section in sections)
            {
                wanted.Sections.Add(section);
            }

            var group = await people.EnsureUserGroupAsync(wanted);

            await access.ChangeGrantAsync(new AccessGrantChange(group.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ManageSettings, null));
        }

        var caller = new ManagementApiCaller(Client, Services, role => [role == "keeper" ? "jfbSettingsKeepers" : "jfbSiteKeepers"]);
        var refused = await caller.CallAsync("keeper", HttpMethod.Post, Path);
        var nothing = GetRequiredService<IContentTypeService>().Get(FormBlockTypes.ElementTypeAlias);
        var answered = await caller.CallAsync("siteKeeper", HttpMethod.Post, Path);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Outcome, Is.EqualTo("403 forbidden"), refused.Text);
            Assert.That(refused.Body?["section"]?.GetValue<string>(), Is.EqualTo(Constants.Applications.Settings), "the answer names the section");
            Assert.That(nothing, Is.Null, "nothing created");
            Assert.That(answered.Status, Is.EqualTo(HttpStatusCode.OK), answered.Text);
            Assert.That(Part(answered, "elementType").Created, Is.True);
        });
    }

    // A part of the answer: its key, its name and whether this call created it.
    private static (string Key, string Name, bool Created) Part(ApiAnswer answer, string name) =>
        (answer.Body![name]!["key"]!.GetValue<string>(), answer.Body[name]!["name"]!.GetValue<string>(), answer.Body[name]!["created"]!.GetValue<bool>());
}
