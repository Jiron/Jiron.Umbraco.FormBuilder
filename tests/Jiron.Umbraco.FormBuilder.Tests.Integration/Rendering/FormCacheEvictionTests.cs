using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// Eviction (ROADMAP 6.7d, ADR-0004, ADR-0132) with Umbraco's output cache on: a page that picks a form is cached with
/// the form's tag from the "used on" index, so publishing the form re-renders it at once, while a cached page that picks
/// another form, and one that names the form in its view, are kept; an unpublish, a rename and a move to the trash evict
/// as a publish does, a change to a whole folder evicts every page that uses a form, and a page that uses none gets no
/// tag. The eviction follows the form cache refresher, so the host delivers cache messages on this server, as a site of
/// one server does (<see cref="LocalServerMessenger"/>).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormCacheEvictionTests : OutputCacheTestBase
{
    private const string Picking = "picker-cache";

    private const string PickingOther = "picker-cache-other";

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IServerMessenger, LocalServerMessenger>();
    }

    [Test]
    public async Task PublishingTheForm_ReRendersACachedPageThatPicksItAtOnce_AndKeepsACachedPageThatDoesNot()
    {
        var form = await PublishFormAsync("pickerForm");
        var other = await PublishFormAsync("otherForm");
        _ = await PublishFormAsync(CacheForm);
        var pages = await PagesAsync(Picking, PickingOther, CachePage);
        await PickAsync(pages[Picking], form);
        await PickAsync(pages[PickingOther], other);

        var before = await ServedAsync(Client, $"/{Picking}/");
        var cached = await ServedAsync(Visitor(), $"/{Picking}/");
        var otherBefore = await ServedAsync(Client, $"/{PickingOther}/");
        var namedBefore = await ServedAsync(Client, $"/{CachePage}/");
        await SaveAndPublishAsync(form, definition => WithFields(definition, RequiredName));
        var after = await ServedAsync(Visitor(), $"/{Picking}/");
        var otherAfter = await ServedAsync(Visitor(), $"/{PickingOther}/");
        var namedAfter = await ServedAsync(Visitor(), $"/{CachePage}/");

        Assert.Multiple(() =>
        {
            Assert.That((before.Status, after.Status), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That(before.Document.QuerySelector("form.jfb-form")?.GetAttribute("data-jfb-form"), Is.EqualTo(form.ToString("D")));
            Assert.That((cached.FromCache, cached.Stamp), Is.EqualTo((true, before.Stamp)), "cached before the publish");
            Assert.That(after.FromCache, Is.False, "rendered again at once");
            Assert.That(after.Stamp, Is.Not.EqualTo(before.Stamp));
            Assert.That(before.Document.QuerySelector("input[required]"), Is.Null, "the first version");
            Assert.That(after.Document.QuerySelector("input[required]"), Is.Not.Null, "the version just published");
            Assert.That((otherAfter.FromCache, otherAfter.Stamp), Is.EqualTo((true, otherBefore.Stamp)), "a page that picks another form kept");
            Assert.That((namedAfter.FromCache, namedAfter.Stamp), Is.EqualTo((true, namedBefore.Stamp)), "a form named in a view is not in the index; form_changed covers it");
        });
    }

    [Test]
    public async Task AnUnpublish_ARename_AndATrash_EvictAsAPublishDoes_AndAFoldersChangeEvictsEveryPageThatUsesAForm()
    {
        var form = await PublishFormAsync("pickerForm");
        var other = await PublishFormAsync("otherForm");
        var pages = await PagesAsync(Picking, PickingOther);
        await PickAsync(pages[Picking], form);
        await PickAsync(pages[PickingOther], other);
        var forms = GetRequiredService<IFormBuilderFormService>();

        var renamed = await EvictsAsync(Picking, async () => Assert.That(await forms.RenameAsync(form, (await forms.GetAsync(form))!.ConcurrencyVersion, "Renamed", null, null), Is.Not.Null));
        var unpublished = await EvictsAsync(Picking, async () => Assert.That(await forms.UnpublishAsync(form, (await forms.GetAsync(form))!.ConcurrencyVersion, null), Is.Not.Null));
        var trashed = await EvictsAsync(Picking, async () => Assert.That(await forms.TrashAsync(form, (await forms.GetAsync(form))!.ConcurrencyVersion, null), Is.Not.Null));
        var everything = await EvictsAsync(PickingOther, () =>
        {
            GetRequiredService<FormCacheInvalidator>().ChangedAll();
            return Task.CompletedTask;
        });

        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.True, "a rename");
            Assert.That(unpublished, Is.True, "an unpublish");
            Assert.That(trashed, Is.True, "a move to the trash");
            Assert.That(everything, Is.True, "a change to many forms at once");
        });
    }

    [Test]
    public async Task APageThatUsesNoForm_GetsNoTag_AndOneThatDoes_TheTagOfEachForm()
    {
        var form = await PublishFormAsync("pickerForm");
        var pages = await PagesAsync(Picking, PickingOther);
        await PickAsync(pages[Picking], form);
        var tags = Services.GetServices<IWebsiteOutputCacheTagProvider>().OfType<FormCacheTags>().Single();
        var published = GetRequiredService<global::Umbraco.Cms.Core.PublishedCache.IPublishedContentCache>();

        Assert.Multiple(async () =>
        {
            Assert.That(tags.GetTags((await published.GetByIdAsync(pages[Picking]))!), Is.EqualTo(new[] { "jfb-form", $"jfb-form-{form:D}" }));
            Assert.That(tags.GetTags((await published.GetByIdAsync(pages[PickingOther]))!), Is.Empty);
        });
    }

    // Whether a change evicts a page: cached before it, rendered again after it.
    private async Task<bool> EvictsAsync(string page, Func<Task> change)
    {
        var before = await ServedAsync(Client, $"/{page}/");
        var cached = await ServedAsync(Visitor(), $"/{page}/");
        Assert.That((cached.FromCache, cached.Stamp), Is.EqualTo((true, before.Stamp)), "cached before the change");
        await change();
        var after = await ServedAsync(Visitor(), $"/{page}/");
        return !after.FromCache && after.Stamp != before.Stamp;
    }

    // The page type's picker property "form", once, and a page's pick of a form, published.
    private async Task PickAsync(Guid page, Guid form)
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = types.Get(PageType)!;
        if (!type.PropertyTypeExists("form"))
        {
            var picker = new DataType(GetRequiredService<PropertyEditorCollection>()[FormPickerPropertyEditor.EditorAlias]!, GetRequiredService<IConfigurationEditorJsonSerializer>())
            {
                Name = "Cache picker",
                DatabaseType = ValueStorageType.Nvarchar,
            };
            var created = await GetRequiredService<IDataTypeService>().CreateAsync(picker, Constants.Security.SuperUserKey);
            Assert.That(created.Success, Is.True, created.Status.ToString());
            type.AddPropertyType(new PropertyType(GetRequiredService<IShortStringHelper>(), created.Result!, "form") { Name = "Form" });
            Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        }

        var content = Contents.GetById(page)!;
        content.SetValue("form", form.ToString("D"));
        Assert.That(Contents.Save(content).Success, Is.True);
        Assert.That(Contents.Publish(content, ["*"]).Success, Is.True);
    }
}
