using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Models.ContentPublishing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>
/// Seeds the demo site once it has started (ROADMAP 3.3): the German language, the dictionary items of the views, the
/// templates, a block list with two element types, the document types, the English and German pages with their
/// domains, the members area (<see cref="DemoMemberSeeder"/>), the picture the specimen form shows
/// (<see cref="DemoMediaSeeder"/>) and the demo forms (<see cref="DemoFormSeeder"/>).
/// Idempotent: every item is created only when it is missing, so a restart changes nothing, and a later step adds a
/// page by adding it to <c>DemoContent.json</c>. The feature steps add their demo forms as the features arrive.
/// </summary>
internal sealed partial class DemoSeeder(
    ILanguageService languages,
    IDictionaryItemService dictionary,
    ITemplateService templates,
    IContentTypeService contentTypes,
    IDataTypeService dataTypes,
    PropertyEditorCollection propertyEditors,
    IConfigurationEditorJsonSerializer configurationSerializer,
    IShortStringHelper shortStrings,
    IContentEditingService contentEditing,
    IContentPublishingService publishing,
    IDomainService domains,
    IWebHostEnvironment environment,
    DemoMemberSeeder members,
    DemoMediaSeeder media,
    DemoFormSeeder demoForms,
    ILogger<DemoSeeder> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string English = "en-US";
    public const string German = "de-DE";
    private static readonly Guid User = Constants.Security.SuperUserKey;

    /// <summary>The block list's data type, found again by this key.</summary>
    private static readonly Guid BlocksDataTypeKey = new("5c0f3a27-9d3e-4c6b-8a1e-2f7b6d4e9a10");

    /// <inheritdoc />
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var content = DemoContent.Load();
            var created = await SeedAsync(content);
            created |= await members.SeedAsync(content);
            created |= media.Seed();
            created |= await demoForms.SeedAsync(cancellationToken);
            LogSeeded(logger, created);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
        }
    }

    private async Task<bool> SeedAsync(DemoContent content)
    {
        var english = await languages.GetAsync(English)
            ?? throw new InvalidOperationException($"Demo seeding: the default language {English} is missing.");
        var german = await languages.GetAsync(German);
        if (german is null)
        {
            german = new Language(German, "Deutsch (Deutschland)");
            Check(await languages.CreateAsync(german, User), "the language de-DE");
        }

        await EnsureDictionaryAsync(content, english, german);

        var homeTemplate = await EnsureTemplateAsync(DemoContent.HomeType, "Demo home");
        var pageTemplate = await EnsureTemplateAsync(DemoContent.PageType, "Demo page");
        var membersTemplate = await EnsureTemplateAsync(DemoContent.MembersType, "Demo members area");
        var loginTemplate = await EnsureTemplateAsync(DemoContent.LoginType, "Demo sign-in");

        var textBox = await DataTypeAsync(Constants.PropertyEditors.Aliases.TextBox);
        var textArea = await DataTypeAsync(Constants.PropertyEditors.Aliases.TextArea);
        var richText = await DataTypeAsync(Constants.PropertyEditors.Aliases.RichText);

        var textBlock = await EnsureElementTypeAsync(DemoContent.TextBlockType, "Demo text block", "icon-text", textBox, richText);
        var highlightBlock = await EnsureElementTypeAsync(DemoContent.HighlightBlockType, "Demo highlight block", "icon-lightbulb", textBox, textArea);
        var blocks = await EnsureBlockListAsync(textBlock, highlightBlock);
        var properties = new PageProperties(textBox, textArea, richText, blocks);

        var page = await EnsureDocumentTypeAsync(DemoContent.PageType, "Demo page", "icon-document", [pageTemplate], properties);
        var legal = await EnsureDocumentTypeAsync(DemoContent.LegalType, "Demo legal page", "icon-script", [pageTemplate], properties);
        var specimen = await EnsureDocumentTypeAsync(DemoContent.SpecimenType, "Demo specimen", "icon-layout", [pageTemplate], properties);
        var membersArea = await EnsureDocumentTypeAsync(DemoContent.MembersType, "Demo members area", "icon-lock", [membersTemplate], properties);
        var login = await EnsureDocumentTypeAsync(DemoContent.LoginType, "Demo sign-in page", "icon-key", [loginTemplate], properties);
        var home = await EnsureDocumentTypeAsync(DemoContent.HomeType, "Demo home", "icon-home", [homeTemplate], properties, isRoot: true);
        await EnsureAllowedChildrenAsync(home, [page, membersArea, login, legal, specimen]);

        var types = new Dictionary<string, (IContentType Type, ITemplate Template)>
        {
            [DemoContent.HomeType] = (home, homeTemplate),
            [DemoContent.PageType] = (page, pageTemplate),
            [DemoContent.LegalType] = (legal, pageTemplate),
            [DemoContent.SpecimenType] = (specimen, pageTemplate),
            [DemoContent.MembersType] = (membersArea, membersTemplate),
            [DemoContent.LoginType] = (login, loginTemplate),
        };
        var elementTypes = new Dictionary<string, IContentType>
        {
            [DemoContent.TextBlockType] = textBlock,
            [DemoContent.HighlightBlockType] = highlightBlock,
        };

        // Every missing page first, then the domains, then publishing (home first), so no page is published in two
        // cultures while its root has no domains.
        var created = new List<DemoPage>();
        if (await CreatePageAsync(content.Home, types, elementTypes, parentKey: null))
        {
            created.Add(content.Home);
        }

        foreach (var child in content.Pages)
        {
            if (await CreatePageAsync(child, types, elementTypes, content.Home.Key))
            {
                created.Add(child);
            }
        }

        await EnsureDomainsAsync(content.Home.Key);
        foreach (var newPage in created)
        {
            await PublishAsync(newPage);
        }

        return created.Count > 0;
    }

    private async Task EnsureDictionaryAsync(DemoContent content, ILanguage english, ILanguage german)
    {
        foreach (var (key, text) in content.Dictionary)
        {
            if (await dictionary.ExistsAsync(key))
            {
                continue;
            }

            var item = new DictionaryItem(key)
            {
                Translations = [new DictionaryTranslation(english, text.En), new DictionaryTranslation(german, text.De)],
            };
            Check(await dictionary.CreateAsync(item, User), $"the dictionary item {key}");
        }
    }

    private async Task<ITemplate> EnsureTemplateAsync(string alias, string name)
    {
        var existing = await templates.GetAsync(alias);
        if (existing is not null)
        {
            return existing;
        }

        // The view is committed in Views/. A TemplateOnDisk makes Umbraco read it, but Umbraco writes it back with a
        // byte-order mark, which the repository's text check refuses; removing it leaves the committed file as it was.
        var created = await templates.CreateAsync(new TemplateOnDisk(shortStrings, name, alias), User);
        if (!created.Success)
        {
            throw new InvalidOperationException($"Demo seeding: the template {alias} failed ({created.Status}).");
        }

        await RemoveByteOrderMarkAsync(Path.Combine(environment.ContentRootPath, "Views", $"{alias}.cshtml"));
        return created.Result;
    }

    private static async Task RemoveByteOrderMarkAsync(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return;
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            await System.IO.File.WriteAllBytesAsync(path, bytes[3..]);
        }
    }

    private async Task<IDataType> DataTypeAsync(string editorAlias) =>
        (await dataTypes.GetByEditorAliasAsync(editorAlias)).FirstOrDefault()
        ?? throw new InvalidOperationException($"Demo seeding: Umbraco's {editorAlias} data type is missing.");

    private async Task<IContentType> EnsureElementTypeAsync(string alias, string name, string icon, IDataType heading, IDataType text)
    {
        var existing = contentTypes.Get(alias);
        if (existing is not null)
        {
            return existing;
        }

        // Invariant: the block list property itself varies by culture, so each language has its own blocks.
        var type = new ContentType(shortStrings, -1) { Alias = alias, Name = name, Icon = icon, IsElement = true };
        type.AddPropertyType(new PropertyType(shortStrings, heading, "heading") { Name = "Heading" }, "content", "Content");
        type.AddPropertyType(new PropertyType(shortStrings, text, "text") { Name = "Text" }, "content", "Content");
        Check(await contentTypes.CreateAsync(type, User), $"the element type {alias}");
        return type;
    }

    private async Task<IDataType> EnsureBlockListAsync(IContentType textBlock, IContentType highlightBlock)
    {
        var existing = await dataTypes.GetAsync(BlocksDataTypeKey);
        if (existing is not null)
        {
            return existing;
        }

        if (!propertyEditors.TryGet(Constants.PropertyEditors.Aliases.BlockList, out var editor))
        {
            throw new InvalidOperationException("Demo seeding: Umbraco's block list editor is missing.");
        }

        var configuration = new BlockListConfiguration
        {
            Blocks =
            [
                new BlockListConfiguration.BlockConfiguration { ContentElementTypeKey = textBlock.Key },
                new BlockListConfiguration.BlockConfiguration { ContentElementTypeKey = highlightBlock.Key },
            ],
        };
        var dataType = new DataType(editor, configurationSerializer)
        {
            Key = BlocksDataTypeKey,
            Name = "Demo blocks",
            EditorUiAlias = "Umb.PropertyEditorUi.BlockList",
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = editor.GetConfigurationEditor().FromConfigurationObject(configuration, configurationSerializer),
        };
        Check(await dataTypes.CreateAsync(dataType, User), "the block list data type");
        return dataType;
    }

    private async Task<IContentType> EnsureDocumentTypeAsync(
        string alias,
        string name,
        string icon,
        IReadOnlyList<ITemplate> allowedTemplates,
        PageProperties properties,
        bool isRoot = false)
    {
        var existing = contentTypes.Get(alias);
        if (existing is not null)
        {
            return existing;
        }

        var type = new ContentType(shortStrings, -1)
        {
            Alias = alias,
            Name = name,
            Icon = icon,
            AllowedAsRoot = isRoot,
            Variations = ContentVariation.Culture,
        };
        AddProperty(type, properties.TextBox, "heading", "Heading", "content", "Content");
        AddProperty(type, properties.TextArea, "lede", "Introduction", "content", "Content");
        AddProperty(type, properties.RichText, "bodyText", "Body text", "content", "Content");
        AddProperty(type, properties.Blocks, "blocks", "Blocks", "content", "Content");

        // The form below the content, by its alias, the same in both languages (the form picker replaces it with 6.7).
        type.AddPropertyType(new PropertyType(shortStrings, properties.TextBox, "form") { Name = "Form", Description = "The alias of a form to show below the content." }, "content", "Content");

        // What the page is about, which its form may take as the context value topic (ADR-0124).
        type.AddPropertyType(new PropertyType(shortStrings, properties.TextBox, "formTopic") { Name = "Form topic", Description = "What the page is about; a form that lists the context value topic may take it." }, "content", "Content");
        if (isRoot)
        {
            // The footer's newsletter slot: its texts now, the newsletter form once a step brings it.
            AddProperty(type, properties.TextBox, "newsletterHeading", "Newsletter heading", "footer", "Footer");
            AddProperty(type, properties.TextArea, "newsletterText", "Newsletter text", "footer", "Footer");
        }

        type.AllowedTemplates = allowedTemplates;
        type.SetDefaultTemplate(allowedTemplates[0]);
        Check(await contentTypes.CreateAsync(type, User), $"the document type {alias}");
        return type;
    }

    private void AddProperty(ContentType type, IDataType dataType, string alias, string name, string groupAlias, string groupName) =>
        type.AddPropertyType(
            new PropertyType(shortStrings, dataType, alias) { Name = name, Variations = ContentVariation.Culture },
            groupAlias,
            groupName);

    private async Task EnsureAllowedChildrenAsync(IContentType home, IReadOnlyList<IContentType> children)
    {
        var allowed = home.AllowedContentTypes?.Select(sort => sort.Key).ToHashSet() ?? [];
        if (children.All(child => allowed.Contains(child.Key)))
        {
            return;
        }

        home.AllowedContentTypes = children.Select((child, index) => new ContentTypeSort(child.Key, index, child.Alias)).ToList();
        Check(await contentTypes.UpdateAsync(home, User), $"the allowed children of {home.Alias}");
    }

    /// <summary>Creates the page, unpublished, when it is missing; true when it was created.</summary>
    private async Task<bool> CreatePageAsync(
        DemoPage page,
        Dictionary<string, (IContentType Type, ITemplate Template)> types,
        Dictionary<string, IContentType> elementTypes,
        Guid? parentKey)
    {
        if (await contentEditing.GetAsync(page.Key) is not null)
        {
            return false;
        }

        var (type, template) = types[page.Type];
        var properties = new List<PropertyValueModel>();
        if (page.Form is not null)
        {
            properties.Add(new PropertyValueModel { Alias = "form", Value = page.Form });
        }

        if (page.FormTopic is not null)
        {
            properties.Add(new PropertyValueModel { Alias = "formTopic", Value = page.FormTopic });
        }

        foreach (var (culture, german) in new[] { (English, false), (German, true) })
        {
            var texts = page.Texts(german);
            properties.Add(new PropertyValueModel { Alias = "heading", Value = texts.Heading, Culture = culture });
            properties.Add(new PropertyValueModel { Alias = "lede", Value = texts.Lede, Culture = culture });
            properties.Add(new PropertyValueModel { Alias = "bodyText", Value = texts.Body, Culture = culture });
            properties.Add(new PropertyValueModel { Alias = "blocks", Value = BlockListValue(page.Blocks, german, elementTypes), Culture = culture });
            if (texts.NewsletterHeading is not null)
            {
                properties.Add(new PropertyValueModel { Alias = "newsletterHeading", Value = texts.NewsletterHeading, Culture = culture });
                properties.Add(new PropertyValueModel { Alias = "newsletterText", Value = texts.NewsletterText, Culture = culture });
            }
        }

        var model = new ContentCreateModel
        {
            Key = page.Key,
            ContentTypeKey = type.Key,
            ParentKey = parentKey,
            TemplateKey = template.Key,
            Variants =
            [
                new VariantModel { Culture = English, Name = page.En.Name },
                new VariantModel { Culture = German, Name = page.De.Name },
            ],
            Properties = properties,
        };
        var created = await contentEditing.CreateAsync(model, User);
        if (!created.Success || created.Result.Content is null)
        {
            throw new InvalidOperationException($"Demo seeding: the page {page.En.Name} failed ({created.Status}).");
        }

        return true;
    }

    /// <summary>Publishes a page in English and German.</summary>
    private async Task PublishAsync(DemoPage page)
    {
        var published = await publishing.PublishAsync(
            page.Key,
            [new CulturePublishScheduleModel { Culture = English }, new CulturePublishScheduleModel { Culture = German }],
            User);
        if (!published.Success)
        {
            throw new InvalidOperationException($"Demo seeding: publishing {page.En.Name} failed ({published.Status}).");
        }
    }

    /// <summary>
    /// A block list value in Umbraco 17's format: the layout, the blocks' data and their exposure. The element types
    /// are invariant, so each block is exposed without a culture inside the culture's own value.
    /// </summary>
    private static string? BlockListValue(IReadOnlyList<DemoBlock> blocks, bool german, Dictionary<string, IContentType> elementTypes)
    {
        if (blocks.Count == 0)
        {
            return null;
        }

        var items = blocks.Select(block => (Key: Guid.NewGuid(), Block: block, Texts: block.Texts(german))).ToList();
        var value = new
        {
            layout = new Dictionary<string, object>
            {
                [Constants.PropertyEditors.Aliases.BlockList] = items.Select(item => new { contentKey = item.Key }).ToArray(),
            },
            contentData = items.Select(item => new
            {
                key = item.Key,
                contentTypeKey = elementTypes[item.Block.Type].Key,
                values = new object[]
                {
                    new { alias = "heading", value = (object)item.Texts.Heading },
                    new
                    {
                        alias = "text",
                        value = item.Block.Type == DemoContent.TextBlockType
                            ? new { markup = item.Texts.Text, blocks = (object?)null }
                            : (object)item.Texts.Text,
                    },
                },
            }).ToArray(),
            settingsData = Array.Empty<object>(),
            expose = items.Select(item => new { contentKey = item.Key, culture = (string?)null, segment = (string?)null }).ToArray(),
        };
        return JsonSerializer.Serialize(value);
    }

    /// <summary>The home page's domains: English under <c>/en</c> and German under <c>/de</c>, on any host.</summary>
    private async Task EnsureDomainsAsync(Guid home)
    {
        DomainModel[] wanted =
        [
            new() { DomainName = "/en", IsoCode = English },
            new() { DomainName = "/de", IsoCode = German },
        ];
        var assigned = (await domains.GetAssignedDomainsAsync(home, true))
            .OrderBy(domain => domain.SortOrder)
            .Select(domain => domain.DomainName);
        if (assigned.SequenceEqual(wanted.Select(domain => domain.DomainName), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var update = await domains.UpdateDomainsAsync(home, new DomainsUpdateModel { Domains = wanted });
        if (!update.Success)
        {
            throw new InvalidOperationException($"Demo seeding: the domains failed ({update.Status}).");
        }
    }

    internal static void Check<TResult, TStatus>(Attempt<TResult, TStatus> attempt, string what)
    {
        if (!attempt.Success)
        {
            throw new InvalidOperationException($"Demo seeding: {what} failed ({attempt.Status}).");
        }
    }

    internal static void Check<TStatus>(Attempt<TStatus> attempt, string what)
    {
        if (!attempt.Success)
        {
            throw new InvalidOperationException($"Demo seeding: {what} failed ({attempt.Result}).");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo seeding finished (content created: {Created})")]
    private static partial void LogSeeded(ILogger logger, bool created);

    [LoggerMessage(Level = LogLevel.Error, Message = "Demo seeding failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    /// <summary>The data types of the pages' properties.</summary>
    private sealed record PageProperties(IDataType TextBox, IDataType TextArea, IDataType RichText, IDataType Blocks);
}
