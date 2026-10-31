using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The form picker in the Content Delivery API (ROADMAP 6.6c, ADR-0127): a page that picks a form answers its
/// <c>{ key, alias, name }</c>, and with the property expanded also the form exactly as the definition endpoint answers
/// it in the request's culture; a form that is not headless, unpublished or gone answers <c>null</c>; and no marker leaves the site
/// on the item and items routes (the query route answers from the search index, which the test host leaves empty, in
/// the items route's shape, which <c>FormBuilderDeliveryApiMiddlewareTests</c> reads).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPickerDeliveryApiTests : RenderingTestBase
{
    private const string Delivery = "/umbraco/delivery/api/v2/content";

    [Test]
    public async Task APageThatPicksAForm_AnswersItsKeyAliasAndName_AndExpandedTheFormAsTheDefinitionEndpointAnswersIt_InTheRequestsCulture()
    {
        var english = (await GetRequiredService<ILanguageService>().GetDefaultLanguageAsync())!;
        var german = new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode };
        Assert.That((await GetRequiredService<ILanguageService>().CreateAsync(german, Constants.Security.SuperUserKey)).Success, Is.True);
        var form = await PublishFormAsync("pickedForm", definition => Headless(definition), NameField);
        var page = await PickerPageAsync("picks", form);
        var item = await PropertyAsync($"{Delivery}/item/{page}");
        var expanded = await PropertyAsync($"{Delivery}/item/{page}?expand=properties[form]");
        var inGerman = await PropertyAsync($"{Delivery}/item/{page}?expand=properties[form]", "de-DE");

        Assert.Multiple(async () =>
        {
            Assert.That(item?.ToJsonString(), Is.EqualTo($$"""{"key":"{{form}}","alias":"pickedForm","name":"pickedForm"}"""), "the key, the alias and the name");
            Assert.That(expanded!["key"]!.GetValue<string>(), Is.EqualTo(form.ToString()));
            Assert.That(expanded["form"]!.ToJsonString(), Is.EqualTo(await EndpointFormAsync(form, "en-US", page)), "the form the definition endpoint answers with");
            Assert.That(inGerman!["form"]!.ToJsonString(), Is.EqualTo(await EndpointFormAsync(form, "de-DE", page)), "in the culture the request accepts");
            Assert.That(inGerman["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!["label"]!.GetValue<string>(), Is.EqualTo("Name (de)"));
            Assert.That(expanded["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!["label"]!.GetValue<string>(), Is.EqualTo("Name"));
        });
    }

    [Test]
    public async Task AFormThatIsNotHeadless_Unpublished_OrGone_AnswersNull()
    {
        var closed = await PublishFormAsync("closedForm", definition => Headless(definition, headless: false), NameField);
        var withdrawn = await PublishFormAsync("withdrawnForm", definition => Headless(definition), NameField);
        var stored = (await GetRequiredService<Core.Persistence.Repositories.IFormRepository>().GetAsync(withdrawn))!;
        Assert.That(await Forms.UnpublishAsync(withdrawn, stored.ConcurrencyVersion, null), Is.Not.Null);
        var gone = Guid.NewGuid();
        var pages = new[] { await PickerPageAsync("closed", closed), await PickerPageAsync("withdrawn", withdrawn), await PickerPageAsync("gone", gone) };

        Assert.Multiple(async () =>
        {
            foreach (var page in pages)
            {
                Assert.That(await PropertyAsync($"{Delivery}/item/{page}"), Is.Null);
                Assert.That(await PropertyAsync($"{Delivery}/item/{page}?expand=properties[form]"), Is.Null);
            }
        });
    }

    [Test]
    public async Task NoMarker_LeavesTheSite_OnTheItemAndItemsRoutes()
    {
        var form = await PublishFormAsync("listedForm", definition => Headless(definition), NameField);
        var page = await PickerPageAsync("listed", form);
        var other = await PickerPageAsync("listedToo", form);
        var answers = new List<string>();
        foreach (var path in new[]
        {
            $"{Delivery}/item/{page}",
            $"{Delivery}/items?id={page}&id={other}",
            $"{Delivery}/items?id={page}&id={other}&expand=properties[form]",
        })
        {
            using var response = await GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
            answers.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Multiple(() =>
        {
            foreach (var answer in answers)
            {
                Assert.That(answer, Does.Not.Contain(FormPickerDeliveryMarker.FormName));
                Assert.That(answer, Does.Contain("\"alias\":\"listedForm\""));
            }
        });
    }

    private const string PickerType = "jfbPickerPage";

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        configuration["Umbraco:CMS:DeliveryApi:Enabled"] = "true";
    }

    // The form property of the Delivery API's item at a path, as JSON, asked in a culture or none; null for null.
    private async Task<JsonNode?> PropertyAsync(string path, string? culture = null)
    {
        using var response = await GetAsync(path, request =>
        {
            if (culture is not null)
            {
                request.Headers.AcceptLanguage.ParseAdd(culture);
            }
        });
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{path}: {text}");
        return JsonNode.Parse(text)!["properties"]!["form"];
    }

    // The form the definition endpoint answers in a culture on a page, as JSON.
    private async Task<string> EndpointFormAsync(Guid form, string culture, Guid page)
    {
        using var response = await GetAsync($"{Root}/forms/{form}?culture={culture}&contentKey={page}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["form"]!.ToJsonString();
    }

    // A published page of the picker page type whose form property holds the form's key.
    private async Task<Guid> PickerPageAsync(string name, Guid form)
    {
        var shortStrings = GetRequiredService<IShortStringHelper>();
        var types = GetRequiredService<IContentTypeService>();
        if (types.Get(PickerType) is not IContentType type)
        {
            var editor = GetRequiredService<PropertyEditorCollection>()[FormPickerPropertyEditor.EditorAlias]!;
            var dataType = new DataType(editor, GetRequiredService<IConfigurationEditorJsonSerializer>()) { Name = "Form picker", DatabaseType = ValueStorageType.Nvarchar };
            Assert.That((await GetRequiredService<IDataTypeService>().CreateAsync(dataType, Constants.Security.SuperUserKey)).Success, Is.True);
            var created = new ContentType(shortStrings, -1) { Alias = PickerType, Name = "Picker page", AllowedAsRoot = true };
            created.AddPropertyType(new PropertyType(shortStrings, dataType, "form") { Name = "Form" });
            Assert.That((await types.CreateAsync(created, Constants.Security.SuperUserKey)).Success, Is.True);
            type = created;
        }

        var contents = GetRequiredService<IContentService>();
        var page = new Content(name, -1, type);
        page.SetValue("form", form.ToString("D"));
        contents.Save(page);
        contents.Publish(page, ["*"]);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        await GetRequiredService<IDocumentUrlService>().InitAsync(forceEmpty: false, CancellationToken.None);
        return page.Key;
    }
}
