using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Sync;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;

/// <summary>
/// Content and data type lists over a real site (ADR-0075): an events page in English and German with events below it,
/// a draft, a page only members may see, a folder of older events and a dropdown data type; options read through the
/// published content in the culture, kept until the site changes, and checked against the site when a list is saved
/// and a form is published. Content changes reach the caches through Umbraco's own cache refreshers on this server.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class ContentOptionListTests : RepositoryTestBase
{
    private const string English = "en-US";
    private const string German = "de-DE";

    // The old fair in German: German sorts the A with umlaut before the K of Konzert, its code point after it.
    private const string OldFairInGerman = "\u00C4ltere Messe";

    private IFormBuilderOptionService Options => GetRequiredService<IFormBuilderOptionService>();

    private IContentService Contents => GetRequiredService<IContentService>();

    private IAppPolicyCache RuntimeCache => GetRequiredService<AppCaches>().RuntimeCache;

    [Test]
    public async Task AContentSource_OffersThePublishedPagesBelowItsRoot_InTheCulture_WithoutDraftsOrMembersPages()
    {
        var site = await SiteAsync();

        var english = await Labels(Content($$"""{"root":"{{site.Events}}"}"""), English);
        var german = await Labels(Content($$"""{"root":"{{site.Events}}"}"""), German);
        var values = await Options.GetAsync(Content($$"""{"root":"{{site.Events}}"}"""), new FormBuilderOptionContext(English));
        var current = await Labels(Content(null), English, page: site.Events);
        var nowhere = await Options.GetAsync(Content(null), new FormBuilderOptionContext(English));

        Assert.Multiple(() =>
        {
            Assert.That(english, Is.EqualTo(new[] { "Concert", "Workshop", "Archive" }), "the tree's order; the draft and the members' page are left out");
            Assert.That(german, Is.EqualTo(new[] { "Konzert", "Archive" }), "the workshop is not published in German");
            Assert.That(values.Options.Select(option => option.Value), Is.EqualTo(new[] { site.Concert.ToString("D"), site.Workshop.ToString("D"), site.Archive.ToString("D") }), "each page's key");
            Assert.That(current, Is.EqualTo(english), "the page the form is on");
            Assert.That((nowhere.IsAvailable, nowhere.Options.Count), Is.EqualTo((true, 0)), "no page, so no pages below it");
        });
    }

    [Test]
    public async Task AContentSource_FiltersByDocumentType_GoesDeep_TakesItsValuesAndLabelsFromProperties_AndOrdersByLabel()
    {
        var site = await SiteAsync();
        var events = $$"""{"root":"{{site.Events}}","documentTypes":["{{site.EventType}}"]""";

        var typed = await Labels(Content(events + "}"), English);
        var deep = await Labels(Content(events + ""","descendants":true}"""), English);
        var byLabel = await Labels(Content(events + ""","descendants":true,"order":"label"}"""), German);
        var properties = await Pairs(Content(events + ""","descendants":true,"valueFrom":"code","labelFrom":"title"}"""), German);
        var numbers = await Pairs(Content(events + ""","descendants":true,"valueFrom":"seats","labelFrom":"name"}"""), English);
        var names = await Pairs(Content(events + ""","valueFrom":"name"}"""), English);

        Assert.Multiple(() =>
        {
            Assert.That(typed, Is.EqualTo(new[] { "Concert", "Workshop" }), "the archive folder is no event");
            Assert.That(deep, Is.EqualTo(new[] { "Concert", "Workshop", "Old fair" }), "every page below, depth first, through the folder");
            Assert.That(byLabel, Is.EqualTo(new[] { OldFairInGerman, "Konzert" }), "in German's order, not by code point");
            Assert.That(properties, Is.EqualTo(new[] { ("C1", "Konzertabend"), ("F1", OldFairInGerman) }), "a property without a title shows the page's name");
            Assert.That(numbers, Is.EqualTo(new[] { ("1200", "Concert") }), "a number in its wire form, not as English writes it; a page without the value is left out");
            Assert.That(names, Is.EqualTo(new[] { ("Concert", "Concert"), ("Workshop", "Workshop") }));
        });
    }

    [Test]
    public async Task APublishedChange_ShowsAtOnce_ForAFieldsSourceAndALibraryList_AndSoDoesASavedDataType()
    {
        var site = await SiteAsync();
        var lists = GetRequiredService<OptionListService>();
        var listKey = Guid.NewGuid();
        Assert.That((await lists.CreateAsync(listKey, new OptionListInput("events", "Events", OptionListKind.Content, Config: $$"""{"root":"{{site.Events}}"}"""))).Succeeded, Is.True);
        var field = Content($$"""{"root":"{{site.Events}}"}""");
        var sizes = DataType($$"""{"dataType":"{{site.Sizes}}"}""");

        var before = await Labels(field, English);
        var listBefore = await ListLabels(listKey);
        var sizesBefore = await Labels(sizes, English);
        var kept = RuntimeCache.SearchByKey(OptionSiteCacheKeys.ContentPrefix).Count();

        var talk = new Content("Talk", Contents.GetById(site.Events)!, ContentType("jfbEvent"));
        talk.SetCultureName("Talk", English);
        Contents.Save(talk);
        Contents.Publish(talk, [English]);
        var published = await Labels(field, English);
        var listPublished = await ListLabels(listKey);

        Contents.Unpublish(Contents.GetById(site.Concert)!);
        var unpublished = await Labels(field, English);

        await ProtectAsync(site.Workshop, site);
        var protectedPage = await Labels(field, English);

        var dataTypes = GetRequiredService<IDataTypeService>();
        var dataType = (await dataTypes.GetAsync(site.Sizes))!;
        dataType.ConfigurationData = new Dictionary<string, object> { ["items"] = new[] { "XS", "S", "M", "L" } };
        await dataTypes.UpdateAsync(dataType, Constants.Security.SuperUserKey);
        var sizesAfter = await Labels(sizes, English);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new[] { "Concert", "Workshop", "Archive" }));
            Assert.That(listBefore, Is.EqualTo(before), "a library list reads the same pages");
            Assert.That(sizesBefore, Is.EqualTo(new[] { "S", "M", "L" }));
            Assert.That(kept, Is.GreaterThan(0), "the options were kept");
            Assert.That(published, Is.EqualTo(new[] { "Concert", "Workshop", "Archive", "Talk" }), "a published page shows at once");
            Assert.That(listPublished, Is.EqualTo(published), "for the library list too");
            Assert.That(unpublished, Is.EqualTo(new[] { "Workshop", "Archive", "Talk" }), "an unpublished page goes at once");
            Assert.That(protectedPage, Is.EqualTo(new[] { "Archive", "Talk" }), "a page that members only may see goes at once");
            Assert.That(sizesAfter, Is.EqualTo(new[] { "XS", "S", "M", "L" }), "a saved data type shows at once");
        });
    }

    [Test]
    public async Task ADataTypeSource_OffersADropdownsACheckboxListsOrARadioButtonListsValues_InTheirOrder_TheSameInEveryCulture()
    {
        var site = await SiteAsync();
        var checkboxes = await ValueListAsync(Constants.PropertyEditors.Aliases.CheckBoxList, "Days", "Mon", "Tue", "Wed");
        var radios = await ValueListAsync(Constants.PropertyEditors.Aliases.RadioButtonList, "Answer", "Yes", "No");

        var dropdown = await Pairs(DataType($$"""{"dataType":"{{site.Sizes}}"}"""), German);
        var days = await Labels(DataType($$"""{"dataType":"{{checkboxes}}"}"""), English);
        var answers = await Labels(DataType($$"""{"dataType":"{{radios}}"}"""), German);
        await GetRequiredService<IDataTypeService>().DeleteAsync(radios, Constants.Security.SuperUserKey);
        var deleted = await Options.GetAsync(DataType($$"""{"dataType":"{{radios}}"}"""), new FormBuilderOptionContext(English));

        Assert.Multiple(() =>
        {
            Assert.That(dropdown, Is.EqualTo(new[] { ("S", "S"), ("M", "M"), ("L", "L") }), "each value its own label");
            Assert.That(days, Is.EqualTo(new[] { "Mon", "Tue", "Wed" }));
            Assert.That(answers, Is.EqualTo(new[] { "Yes", "No" }));
            Assert.That((deleted.IsAvailable, deleted.Options.Count), Is.EqualTo((false, 0)), "a data type that is gone makes the source fail");
        });
    }

    [Test]
    public async Task WhatEachServersRefreshersTell_DropsWhatThisServerKept()
    {
        var site = await SiteAsync();
        var events = GetRequiredService<IEventAggregator>();
        await Labels(Content($$"""{"root":"{{site.Events}}"}"""), English);
        await Labels(DataType($$"""{"dataType":"{{site.Sizes}}"}"""), English);

        var keptBefore = (Kept(OptionSiteCacheKeys.ContentPrefix), Kept(OptionSiteCacheKeys.DataTypePrefix));
        events.Publish(new ContentCacheRefresherNotification(Array.Empty<ContentCacheRefresher.JsonPayload>(), MessageType.RefreshByPayload));
        var afterContent = (Kept(OptionSiteCacheKeys.ContentPrefix), Kept(OptionSiteCacheKeys.DataTypePrefix));
        await Labels(Content($$"""{"root":"{{site.Events}}"}"""), English);
        events.Publish(new DataTypeCacheRefresherNotification(Array.Empty<DataTypeCacheRefresher.JsonPayload>(), MessageType.RefreshByPayload));
        var afterDataType = (Kept(OptionSiteCacheKeys.ContentPrefix), Kept(OptionSiteCacheKeys.DataTypePrefix));
        var afterOthers = new List<bool>();
        foreach (var notification in new INotification[]
        {
            new ContentTypeCacheRefresherNotification(Array.Empty<ContentTypeCacheRefresher.JsonPayload>(), MessageType.RefreshByPayload),
            new PublicAccessCacheRefresherNotification(Array.Empty<object>(), MessageType.RefreshAll),
            new LanguageCacheRefresherNotification(Array.Empty<LanguageCacheRefresher.JsonPayload>(), MessageType.RefreshByPayload),
        })
        {
            await Labels(Content($$"""{"root":"{{site.Events}}"}"""), English);
            events.Publish(notification);
            afterOthers.Add(Kept(OptionSiteCacheKeys.ContentPrefix));
        }

        Assert.Multiple(() =>
        {
            Assert.That(keptBefore, Is.EqualTo((true, true)));
            Assert.That(afterContent, Is.EqualTo((false, true)), "a content change drops the content options only");
            Assert.That(afterDataType, Is.EqualTo((false, false)), "a data type's change drops both, as pages read their values through data types");
            Assert.That(afterOthers, Is.EqualTo(new[] { false, false, false }), "document types, public access and languages");
        });
    }

    [Test]
    public async Task SavingAListAndPublishingAForm_HoldWhatTheConfigurationNames_ToTheSite()
    {
        var site = await SiteAsync();
        var lists = GetRequiredService<OptionListService>();
        var service = GetRequiredService<IFormBuilderFormService>();
        var gone = Guid.Parse("75000000-0000-4000-8000-0000000000ff");
        var textstring = Constants.DataTypes.Guids.TextstringGuid;

        var listProblems = await lists.CheckAsync(new OptionListInput("events", "Events", OptionListKind.Content, Config: $$"""{"root":"{{gone}}","documentTypes":["{{site.EventType}}","{{gone}}"],"valueFrom":"number","labelFrom":"subtitle"}""", CacheSeconds: 60));
        var ownProblems = await lists.CheckAsync(new OptionListInput("events", "Events", OptionListKind.Content, Config: """{"valueFrom":"a-b","labelFrom":"key"}"""));
        var dataTypeProblems = await lists.CheckAsync(new OptionListInput("sizes", "Sizes", OptionListKind.DataType, Config: $$"""{"dataType":"{{textstring}}"}"""));
        // An alias in any case, as Umbraco reads a property.
        var good = await lists.CheckAsync(new OptionListInput("events", "Events", OptionListKind.Content, Config: $$"""{"root":"{{site.Events}}","documentTypes":["{{site.EventType}}"],"valueFrom":"code","labelFrom":"Title"}"""));
        Contents.MoveToRecycleBin(Contents.GetById(site.Archive)!);
        var binned = await lists.CheckAsync(new OptionListInput("old", "Old", OptionListKind.Content, Config: $$"""{"root":"{{site.Archive}}"}"""));

        var form = await service.CreateAsync(new NewFormRequest("Booking", UserKey: Editor));
        var blank = (await service.GetDraftAsync(form.Key))!.Definition!;
        var saved = await service.SaveDraftAsync(form.Key, form.ConcurrencyVersion, WithFields(blank, Content($$"""{"root":"{{gone}}"}"""), DataType($$"""{"dataType":"{{gone}}"}""")), null, Editor);
        var refusal = AssertCode(async () => await service.PublishAsync(form.Key, saved!.Form.ConcurrencyVersion, null, Editor), "setting_invalid") as FormBuilderValidationException;
        var fixedDraft = await service.SaveDraftAsync(form.Key, saved!.Form.ConcurrencyVersion, WithFields(blank, Content($$"""{"root":"{{site.Events}}","documentTypes":["{{site.EventType}}"]}"""), DataType($$"""{"dataType":"{{site.Sizes}}"}""")), null, Editor);
        var published = await service.PublishAsync(form.Key, fixedDraft!.Form.ConcurrencyVersion, null, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(listProblems.Select(problem => (problem.Path, problem.Message)), Is.EqualTo(new[]
            {
                ("$.config.root", $"The site has no page with the key {gone} outside the recycle bin."),
                ("$.config.valueFrom", "The document type 'Event' has no property 'number'."),
                ("$.config.labelFrom", "The document type 'Event' has no property 'subtitle'."),
                ("$.config.documentTypes[1]", $"The site has no document type with the key {gone}."),
                ("$.cacheSeconds", "A content list keeps nothing for a time, so its cacheSeconds is 0."),
            }));
            Assert.That(ownProblems.Select(problem => (problem.Path, problem.Message)), Is.EqualTo(new[]
            {
                ("$.config.valueFrom", "An option's value is the page's 'key', its 'name' or a property's alias, not 'a-b'."),
                ("$.config.labelFrom", "An option's label is the page's 'name' or a property's alias; a key is no label."),
            }));
            Assert.That(dataTypeProblems.Select(problem => (problem.Path, problem.Message)), Is.EqualTo(new[] { ("$.config.dataType", "The data type 'Textstring' is no dropdown, checkbox list or radio button list.") }));
            Assert.That(good, Is.Empty);
            Assert.That(binned.Select(problem => (problem.Path, problem.Message)), Is.EqualTo(new[] { ("$.config.root", $"The site has no page with the key {site.Archive} outside the recycle bin.") }), "a page in the recycle bin");
            Assert.That(refusal?.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[]
            {
                (DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[0].options.config.root"),
                (DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[1].options.config.dataType"),
            }));
            Assert.That(published!.Version, Is.EqualTo(4), "once fixed it goes live");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));

        // The changes these tests make tell the cache refreshers as on a site, and this server runs them at once.
        builder.AddNotificationHandler<ContentTreeChangeNotification, ContentTreeChangeDistributedCacheNotificationHandler>();
        builder.AddNotificationHandler<DataTypeSavedNotification, DataTypeSavedDistributedCacheNotificationHandler>();
        builder.AddNotificationHandler<DataTypeDeletedNotification, DataTypeDeletedDistributedCacheNotificationHandler>();
        builder.AddNotificationHandler<PublicAccessEntrySavedNotification, PublicAccessEntrySavedDistributedCacheNotificationHandler>();
        builder.Services.AddUnique<IServerMessenger, LocalServerMessenger>();
    }

    private static FormField Content(string? config) => new()
    {
        Key = Guid.Parse("75000000-0000-4000-8000-000000000010"),
        Type = "dropdown",
        Alias = "event",
        Label = new LocalizedText("Event"),
        Options = new FieldOptions { Source = OptionSource.Content, Config = config is null ? null : SettingsObject.Parse(config) },
    };

    private static FormField DataType(string config) => new()
    {
        Key = Guid.Parse("75000000-0000-4000-8000-000000000011"),
        Type = "dropdown",
        Alias = "sizes",
        Label = new LocalizedText("Size"),
        Options = new FieldOptions { Source = OptionSource.DataType, Config = SettingsObject.Parse(config) },
    };

    private bool Kept(string prefix) => RuntimeCache.SearchByKey(prefix).Any();

    private async Task<string?[]> Labels(FormField field, string culture, Guid? page = null) =>
        [.. (await Options.GetAsync(field, new FormBuilderOptionContext(culture) { Page = page })).Options.Select(option => option.Label)];

    private async Task<(string Value, string? Label)[]> Pairs(FormField field, string culture) =>
        [.. (await Options.GetAsync(field, new FormBuilderOptionContext(culture))).Options.Select(option => (option.Value, option.Label))];

    private async Task<string?[]> ListLabels(Guid list) =>
        [.. (await Options.GetListAsync(list, new FormBuilderOptionContext(English))).Options.Select(option => option.Label)];

    private IContentType ContentType(string alias) => GetRequiredService<IContentTypeService>().Get(alias)!;

    private async Task<Guid> ValueListAsync(string editorAlias, string name, params string[] items)
    {
        Assert.That(GetRequiredService<PropertyEditorCollection>().TryGet(editorAlias, out var editor), Is.True);
        var dataType = new DataType(editor!, GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Name = name,
            ConfigurationData = new Dictionary<string, object> { ["items"] = items },
        };
        Succeeded(await GetRequiredService<IDataTypeService>().CreateAsync(dataType, Constants.Security.SuperUserKey));
        return dataType.Key;
    }

    private async Task ProtectAsync(Guid page, (Guid Events, Guid Concert, Guid Workshop, Guid Archive, Guid EventType, Guid Sizes, Guid Login) site)
    {
        var access = await GetRequiredService<IPublicAccessService>().CreateAsync(new PublicAccessEntrySlim
        {
            ContentId = page,
            MemberGroupNames = ["Members"],
            MemberUserNames = [],
            LoginPageId = site.Login,
            ErrorPageId = site.Login,
        });
        Assert.That(access.Success, Is.True, access.Status.ToString());
    }

    /// <summary>
    /// English and German; an invariant folder type and an event type named per culture with an invariant code, a title
    /// per culture and seats; the events page with a concert in both languages, a workshop in English only, a draft, a
    /// members' night only members may see and an archive folder with an old fair; a login page; and a dropdown of sizes.
    /// </summary>
    private async Task<(Guid Events, Guid Concert, Guid Workshop, Guid Archive, Guid EventType, Guid Sizes, Guid Login)> SiteAsync()
    {
        var languages = GetRequiredService<ILanguageService>();
        var english = await languages.GetDefaultLanguageAsync() ?? throw new InvalidOperationException("No default language.");
        Assert.That(english.IsoCode, Is.EqualTo(English));
        await languages.CreateAsync(new Language(German, "German") { FallbackIsoCode = english.IsoCode }, Constants.Security.SuperUserKey);

        var strings = GetRequiredService<IShortStringHelper>();
        var dataTypes = GetRequiredService<IDataTypeService>();
        var textstring = await dataTypes.GetAsync(Constants.DataTypes.Guids.TextstringGuid) ?? throw new InvalidOperationException("No textstring.");
        var numeric = await dataTypes.GetAsync(Constants.DataTypes.Guids.NumericGuid) ?? throw new InvalidOperationException("No numeric.");
        var contentTypes = GetRequiredService<IContentTypeService>();
        var folderType = new ContentType(strings, -1) { Alias = "jfbFolder", Name = "Folder", AllowedAsRoot = true };
        var eventType = new ContentType(strings, -1) { Alias = "jfbEvent", Name = "Event", Variations = ContentVariation.Culture };
        eventType.AddPropertyType(new PropertyType(strings, textstring, "code") { Name = "Code", Variations = ContentVariation.Nothing });
        eventType.AddPropertyType(new PropertyType(strings, textstring, "title") { Name = "Title", Variations = ContentVariation.Culture });
        eventType.AddPropertyType(new PropertyType(strings, numeric, "seats") { Name = "Seats", Variations = ContentVariation.Nothing });
        Succeeded(await contentTypes.CreateAsync(folderType, Constants.Security.SuperUserKey));
        Succeeded(await contentTypes.CreateAsync(eventType, Constants.Security.SuperUserKey));
        folderType.AllowedContentTypes = [new ContentTypeSort(eventType.Key, 0, eventType.Alias), new ContentTypeSort(folderType.Key, 1, folderType.Alias)];
        Succeeded(await contentTypes.UpdateAsync(folderType, Constants.Security.SuperUserKey));

        var events = Folder("Events", null, folderType);
        var concert = Event("Concert", "Konzert", events, eventType, publish: [English, German]);
        concert.SetValue("code", "C1");
        concert.SetValue("title", "Concert night", English);
        concert.SetValue("title", "Konzertabend", German);
        concert.SetValue("seats", 1200);
        Contents.Save(concert);
        Contents.Publish(concert, [English, German]);
        var workshop = Event("Workshop", null, events, eventType, publish: [English]);
        workshop.SetValue("code", "W1");
        Contents.Save(workshop);
        Contents.Publish(workshop, [English]);
        Event("Draft talk", "Entwurf", events, eventType, publish: []);
        var members = Event("Members night", "Mitgliederabend", events, eventType, publish: [English, German]);
        var archive = Folder("Archive", events, folderType);
        var fair = Event("Old fair", OldFairInGerman, archive, eventType, publish: [English, German]);
        fair.SetValue("code", "F1");
        Contents.Save(fair);
        Contents.Publish(fair, [English, German]);
        var login = Folder("Login", null, folderType);

        var editors = GetRequiredService<PropertyEditorCollection>();
        Assert.That(editors.TryGet(Constants.PropertyEditors.Aliases.DropDownListFlexible, out var dropdown), Is.True);
        var sizes = new DataType(dropdown!, GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Name = "Sizes",
            ConfigurationData = new Dictionary<string, object> { ["items"] = new[] { "S", "M", "L" } },
        };
        Succeeded(await dataTypes.CreateAsync(sizes, Constants.Security.SuperUserKey));

        await GetRequiredService<IMemberGroupService>().CreateAsync(new MemberGroup { Name = "Members" });
        var site = (events.Key, concert.Key, workshop.Key, archive.Key, eventType.Key, sizes.Key, login.Key);
        await ProtectAsync(members.Key, site);
        return site;
    }

    private Content Folder(string name, Content? parent, IContentType type)
    {
        var folder = parent is null ? new Content(name, -1, type) : new Content(name, parent, type);
        Contents.Save(folder);
        Contents.Publish(folder, ["*"]);
        return folder;
    }

    private Content Event(string name, string? germanName, Content parent, IContentType type, string[] publish)
    {
        var page = new Content(name, parent, type);
        page.SetCultureName(name, English);
        if (germanName is not null)
        {
            page.SetCultureName(germanName, German);
        }

        Contents.Save(page);
        if (publish.Length > 0)
        {
            Contents.Publish(page, publish);
        }

        return page;
    }

    private static void Succeeded<TStatus>(Attempt<TStatus> attempt) => Assert.That(attempt.Success, Is.True, attempt.Result?.ToString());

    private static void Succeeded<TResult, TStatus>(Attempt<TResult, TStatus> attempt) => Assert.That(attempt.Success, Is.True, attempt.Status?.ToString());

    /// <summary>Delivers every cache refresh on this server at once, as a site on one server does.</summary>
    private sealed class LocalServerMessenger(IJsonSerializer serializer) : ServerMessengerBase(false, serializer)
    {
        public override void SendMessages()
        {
        }

        public override void Sync()
        {
        }

        protected override void DeliverRemote(ICacheRefresher refresher, MessageType messageType, IEnumerable<object>? ids = null, string? json = null)
        {
        }
    }
}
