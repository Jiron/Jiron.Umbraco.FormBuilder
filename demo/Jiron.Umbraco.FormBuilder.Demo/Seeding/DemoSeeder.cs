using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.Options;
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
/// templates, a block list with two element types and the form block of "Create a form block for me" (ADR-0136), the
/// document types, the English and German pages with their domains, the members area (<see cref="DemoMemberSeeder"/>),
/// the picture the specimen form shows (<see cref="DemoMediaSeeder"/>), the demo forms (<see cref="DemoFormSeeder"/>)
/// and then the pages whose blocks pick a form, which need the forms' keys.
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
    FormBlockTypes formBlocks,
    IOptions<DemoOptions> options,
    ILogger<DemoSeeder> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string English = "en-US";
    public const string German = "de-DE";
    private static readonly Guid User = Constants.Security.SuperUserKey;

    /// <summary>The block list's data type, found again by this key.</summary>
    private static readonly Guid BlocksDataTypeKey = new("5c0f3a27-9d3e-4c6b-8a1e-2f7b6d4e9a10");

    /// <summary>No form keys, for the pages without form blocks.</summary>
    private static readonly IReadOnlyDictionary<string, Guid> NoForms = new Dictionary<string, Guid>();

    /// <inheritdoc />
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (!options.Value.Seed)
        {
            LogSeeded(logger, false);
            return;
        }

        try
        {
            var content = DemoContent.Load();
            var (created, pageTypes) = await SeedAsync(content);
            created |= await members.SeedAsync(content);
            created |= media.Seed();
            created |= await demoForms.SeedAsync(cancellationToken);
            created |= await SeedPagesWithFormBlocksAsync(content, pageTypes, cancellationToken);
            LogSeeded(logger, created);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
        }
    }

    private async Task<(bool Created, PageTypes Types)> SeedAsync(DemoContent content)
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
        var formBlock = await EnsureFormBlockAsync(content.PageOfType(DemoContent.ThanksType).Key);
        var blocks = await EnsureBlockListAsync(textBlock, highlightBlock, formBlock);
        var properties = new PageProperties(textBox, textArea, richText, blocks);

        var page = await EnsureDocumentTypeAsync(DemoContent.PageType, "Demo page", "icon-document", [pageTemplate], properties);
        var legal = await EnsureDocumentTypeAsync(DemoContent.LegalType, "Demo legal page", "icon-script", [pageTemplate], properties);
        var specimen = await EnsureDocumentTypeAsync(DemoContent.SpecimenType, "Demo specimen", "icon-layout", [pageTemplate], properties);
        var thanks = await EnsureDocumentTypeAsync(DemoContent.ThanksType, "Demo thank-you page", "icon-message", [pageTemplate], properties);
        var membersArea = await EnsureDocumentTypeAsync(DemoContent.MembersType, "Demo members area", "icon-lock", [membersTemplate], properties);
        var login = await EnsureDocumentTypeAsync(DemoContent.LoginType, "Demo sign-in page", "icon-key", [loginTemplate], properties);
        var home = await EnsureDocumentTypeAsync(DemoContent.HomeType, "Demo home", "icon-home", [homeTemplate], properties, isRoot: true);
        await EnsureAllowedChildrenAsync(home, [page, membersArea, login, legal, specimen, thanks]);

        var types = new Dictionary<string, (IContentType Type, ITemplate Template)>
        {
            [DemoContent.HomeType] = (home, homeTemplate),
            [DemoContent.PageType] = (page, pageTemplate),
            [DemoContent.LegalType] = (legal, pageTemplate),
            [DemoContent.SpecimenType] = (specimen, pageTemplate),
            [DemoContent.ThanksType] = (thanks, pageTemplate),
            [DemoContent.MembersType] = (membersArea, membersTemplate),
            [DemoContent.LoginType] = (login, loginTemplate),
        };
        var elementTypes = new Dictionary<string, IContentType>
        {
            [DemoContent.TextBlockType] = textBlock,
            [DemoContent.HighlightBlockType] = highlightBlock,
            [FormBlockTypes.ElementTypeAlias] = formBlock,
        };
        var pageTypes = new PageTypes(types, elementTypes);

        // Every missing page first, then the domains, then publishing (home first), so no page is published in two
        // cultures while its root has no domains. A page whose blocks pick a form waits for the forms.
        var created = new List<DemoPage>();
        if (await CreatePageAsync(content.Home, pageTypes, NoForms, parentKey: null))
        {
            created.Add(content.Home);
        }

        foreach (var child in content.Pages.Where(child => !child.HoldsFormBlocks))
        {
            if (await CreatePageAsync(child, pageTypes, NoForms, content.Home.Key))
            {
                created.Add(child);
            }
        }

        await EnsureDomainsAsync(content.Home.Key);
        foreach (var newPage in created)
        {
            await PublishAsync(newPage);
        }

        return (created.Count > 0, pageTypes);
    }

    /// <summary>Creates and publishes the missing pages whose blocks pick a form, once the forms exist.</summary>
    private async Task<bool> SeedPagesWithFormBlocksAsync(DemoContent content, PageTypes pageTypes, CancellationToken cancellationToken)
    {
        var created = new List<DemoPage>();
        foreach (var page in content.Pages.Where(page => page.HoldsFormBlocks))
        {
            var formKeys = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var alias in page.Blocks.Select(block => block.Form).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                formKeys[alias] = await demoForms.KeyOfAsync(alias, cancellationToken);
            }

            if (await CreatePageAsync(page, pageTypes, formKeys, content.Home.Key))
            {
                created.Add(page);
            }
        }

        foreach (var page in created)
        {
            await PublishAsync(page);
        }

        return created.Count > 0;
    }

    /// <summary>
    /// "Create a form block for me", as a site builder presses it (ADR-0136); when that made the picker's data type, the
    /// page after a send is set to the thanks page, as the site builder sets it next in the data type's settings.
    /// </summary>
    private async Task<IContentType> EnsureFormBlockAsync(Guid thanksPage)
    {
        var made = await formBlocks.CreateAsync(User);
        if (made.DataType.Created)
        {
            var dataType = await dataTypes.GetAsync(made.DataType.Key)
                ?? throw new InvalidOperationException("Demo seeding: the form picker's data type is missing.");
            dataType.ConfigurationData = new Dictionary<string, object>(dataType.ConfigurationData) { ["redirect"] = thanksPage.ToString() };
            Check(await dataTypes.UpdateAsync(dataType, User), "the form picker's page after a send");
        }

        return await contentTypes.GetAsync(made.ElementType.Key)
            ?? throw new InvalidOperationException("Demo seeding: the form block's element type is missing.");
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

    private async Task<IDataType> EnsureBlockListAsync(IContentType textBlock, IContentType highlightBlock, IContentType formBlock)
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

        // The blocks as the backoffice writes them: Umbraco's server-side model of a block has no label, which the
        // form block takes so that its row names the picked form (the picker's block label component, ADR-0134).
        var dataType = new DataType(editor, configurationSerializer)
        {
            Key = BlocksDataTypeKey,
            Name = "Demo blocks",
            EditorUiAlias = "Umb.PropertyEditorUi.BlockList",
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = new Dictionary<string, object>
            {
                ["blocks"] = new object[]
                {
                    new { contentElementTypeKey = textBlock.Key },
                    new { contentElementTypeKey = highlightBlock.Key },
                    new { contentElementTypeKey = formBlock.Key, label = "{jfbFormName: form}" },
                },
            },
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

        // The form below the content, by its alias, the same in both languages: a developer naming a form in a view; the
        // form block shows an editor picking one (ADR-0136).
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
        PageTypes pageTypes,
        IReadOnlyDictionary<string, Guid> formKeys,
        Guid? parentKey)
    {
        if (await contentEditing.GetAsync(page.Key) is not null)
        {
            return false;
        }

        var (type, template) = pageTypes.Types[page.Type];
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
            properties.Add(new PropertyValueModel { Alias = "blocks", Value = BlockListValue(page.Blocks, german, pageTypes.ElementTypes, formKeys), Culture = culture });
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
    private static string? BlockListValue(
        IReadOnlyList<DemoBlock> blocks,
        bool german,
        IReadOnlyDictionary<string, IContentType> elementTypes,
        IReadOnlyDictionary<string, Guid> formKeys)
    {
        if (blocks.Count == 0)
        {
            return null;
        }

        var items = blocks.Select(block => (Key: Guid.NewGuid(), Block: block)).ToList();
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
                values = BlockValues(item.Block, german, formKeys),
            }).ToArray(),
            settingsData = Array.Empty<object>(),
            expose = items.Select(item => new { contentKey = item.Key, culture = (string?)null, segment = (string?)null }).ToArray(),
        };
        return JsonSerializer.Serialize(value);
    }

    /// <summary>A block's values: the form's key for a form block, else its heading and text.</summary>
    private static object[] BlockValues(DemoBlock block, bool german, IReadOnlyDictionary<string, Guid> formKeys)
    {
        if (block.Form is { } form)
        {
            return [new { alias = FormBlockTypes.PropertyAlias, value = formKeys[form].ToString() }];
        }

        var texts = block.Texts(german);
        return
        [
            new { alias = "heading", value = (object)texts.Heading },
            new
            {
                alias = "text",
                value = block.Type == DemoContent.TextBlockType ? new { markup = texts.Text, blocks = (object?)null } : (object)texts.Text,
            },
        ];
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

    /// <summary>The pages' document types with their templates, and the blocks' element types, by alias.</summary>
    private sealed record PageTypes(
        IReadOnlyDictionary<string, (IContentType Type, ITemplate Template)> Types,
        IReadOnlyDictionary<string, IContentType> ElementTypes);
}
