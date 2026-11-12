using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// Content pages (ADR-0075): the children of a page, or every page below it, that are published in the culture and need
/// no member to sign in, of the chosen document types, in the tree's order or by their labels. An option's value is the
/// page's key, its name or a property's value; its label the page's name or a property's value. The options are kept
/// until the site's content changes.
/// </summary>
/// <param name="umbraco">The content, document types and public access.</param>
internal sealed partial class ContentOptionKind(OptionListUmbraco umbraco) : OptionKind<ContentOptionListSettings>(OptionListKind.Content, onField: true, keeping: OptionKeeping.UntilSiteChanges)
{
    /// <summary>The word for the page's key as an option's value.</summary>
    public const string PageKey = "key";

    /// <summary>The word for the page's name as an option's value or label.</summary>
    public const string PageName = "name";

    /// <inheritdoc />
    /// <remarks>The document types a page may have, by name, as <c>documentTypes</c> offers them.</remarks>
    public override ValueTask<IReadOnlyDictionary<string, IReadOnlyList<FieldSettingChoice>>> SiteChoicesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<FieldSettingChoice> types = [.. umbraco.PageTypes().Select(type => new FieldSettingChoice(type.Key.ToString(), type.Name ?? type.Alias))];
        return ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<FieldSettingChoice>>>(
            new Dictionary<string, IReadOnlyList<FieldSettingChoice>>(StringComparer.Ordinal) { ["documentTypes"] = types });
    }

    /// <inheritdoc />
    protected override void CheckSettings(ContentOptionListSettings settings, OptionKindCheck check)
    {
        if (settings.ValueFrom != PageKey && settings.ValueFrom != PageName && !PropertyAlias().IsMatch(settings.ValueFrom))
        {
            check.Invalid("valueFrom", $"An option's value is the page's 'key', its 'name' or a property's alias, not '{settings.ValueFrom}'.");
        }

        if (settings.LabelFrom == PageKey)
        {
            check.Invalid("labelFrom", "An option's label is the page's 'name' or a property's alias; a key is no label.");
        }
        else if (settings.LabelFrom != PageName && !PropertyAlias().IsMatch(settings.LabelFrom))
        {
            check.Invalid("labelFrom", $"An option's label is the page's 'name' or a property's alias, not '{settings.LabelFrom}'.");
        }
    }

    /// <inheritdoc />
    /// <remarks>A property named for the value or the label is on every document type named, compositions included.</remarks>
    protected override ValueTask CheckReferencesAsync(ContentOptionListSettings settings, OptionKindCheck check, CancellationToken cancellationToken)
    {
        if (settings.Root is { } root && !umbraco.HasPage(root))
        {
            check.Invalid("root", $"The site has no page with the key {root} outside the recycle bin.");
        }

        for (var i = 0; i < settings.DocumentTypes.Count; i++)
        {
            var key = settings.DocumentTypes[i];
            if (umbraco.DocumentType(key) is not { } type)
            {
                check.Invalid(BuiltInChecks.At("documentTypes", i), $"The site has no document type with the key {key}.");
                continue;
            }

            foreach (var (setting, alias) in new[] { ("valueFrom", settings.ValueFrom), ("labelFrom", settings.LabelFrom) })
            {
                if (alias != PageKey && alias != PageName && !type.CompositionPropertyTypes.Any(property => string.Equals(property.Alias, alias, StringComparison.OrdinalIgnoreCase)))
                {
                    check.Invalid(setting, $"The document type '{type.Name}' has no property '{alias}'.");
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Without a root of its own and without the page the form is on, there are no pages, and so no options. A root that
    /// is gone makes the source fail.
    /// </remarks>
    protected override async ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(ContentOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken)
    {
        if ((settings.Root ?? request.Source.Page) is not { } root)
        {
            return [];
        }

        var culture = await umbraco.CultureAsync(request.Source.Culture);
        var key = OptionSiteCacheKeys.Content(settings, root, culture);
        return await RuntimeCacheLoads.GetAsync(umbraco.Cache, key, OptionSiteCacheKeys.Loading(key), () => ReadAsync(settings, root, culture), OptionSiteCacheKeys.Duration, cancellationToken);
    }

    // The options of the pages below the root, depth first in the tree's order, at most one more than a field offers: a
    // page that is not on the site in the culture is left out with every page below it.
    private async Task<IReadOnlyList<FormBuilderOption>> ReadAsync(ContentOptionListSettings settings, Guid root, string culture)
    {
        var children = umbraco.Children(root) ?? throw new InvalidOperationException("The root page is gone.");
        var types = settings.DocumentTypes.Count > 0 ? settings.DocumentTypes.ToHashSet() : null;
        var formats = umbraco.Cultures.For(culture);
        var options = new List<FormBuilderOption>();
        await VisitAsync(children);
        if (settings.Order == ContentOptionOrder.Label)
        {
            var labels = StringComparer.Create(MergeTagCultures.Culture(culture), ignoreCase: false);
            return [.. options.OrderBy(option => option.Label, labels).ThenBy(option => option.Value, StringComparer.Ordinal)];
        }

        return options;

        async Task VisitAsync(IReadOnlyList<Guid> keys)
        {
            foreach (var key in keys)
            {
                if (options.Count > OptionListService.MaxOptions)
                {
                    return;
                }

                if (await umbraco.PublishedAsync(key) is not { } page || MergeTagPropertyValues.Name(page, culture) is not { Length: > 0 } name || umbraco.IsProtected(page))
                {
                    continue;
                }

                if ((types is null || types.Contains(page.ContentType.Key)) && Option(page, name, settings, culture, formats) is { } option)
                {
                    options.Add(option);
                }

                if (settings.Descendants && umbraco.Children(page.Key) is { Count: > 0 } below)
                {
                    await VisitAsync(below);
                }
            }
        }
    }

    // A page's option: no value leaves the page out; a label without text shows the page's name. A value is one item,
    // a number or a date in its wire form; a label shows every item as merge tags show it.
    private static FormBuilderOption? Option(IPublishedContent page, string name, ContentOptionListSettings settings, string culture, CultureFormats formats)
    {
        var value = settings.ValueFrom switch
        {
            PageKey => page.Key.ToString("D"),
            PageName => name,
            var alias => MergeTagPropertyValues.Of(page, alias, culture, formats) is { IsList: false, Items: [var item] } ? item.Number ?? item.Date ?? item.Text : null,
        };

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var label = settings.LabelFrom == PageName
            ? name
            : MergeTagPropertyValues.Of(page, settings.LabelFrom, culture, formats) is { } found && string.Join(", ", found.Items.Select(item => item.Text)) is { Length: > 0 } text ? text : name;
        return new FormBuilderOption(value, label);
    }

    // A property's alias as Umbraco writes one.
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyAlias();
}

/// <summary>The order of a content list's options (ADR-0075).</summary>
internal enum ContentOptionOrder
{
    /// <summary>As the content tree shows the pages, each page's own pages after it (<c>tree</c>).</summary>
    [FormBuilderChoice("#jfbOptionListChoices_contentOrderTree")]
    Tree,

    /// <summary>By the labels, in the culture's alphabetical order (<c>label</c>).</summary>
    [FormBuilderChoice("#jfbOptionListChoices_contentOrderLabel")]
    Label,
}

/// <summary>The configuration of a content list (ADR-0075).</summary>
internal sealed class ContentOptionListSettings
{
    /// <summary>The page whose children, or every page below it, are the options; the page the form is on by default.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_contentRoot", Description = "#jfbOptionListSettings_contentRootHelp", Editor = "contentPicker")]
    public Guid? Root { get; init; }

    /// <summary>Whether every page below the root is an option, not only its children.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_contentDescendants", Order = 1)]
    public bool Descendants { get; init; }

    /// <summary>The document types whose pages are options, by their keys; every type by default.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_contentDocumentTypes", Description = "#jfbOptionListSettings_contentDocumentTypesHelp", Editor = "documentTypePicker", Order = 2)]
    public IReadOnlyList<Guid> DocumentTypes { get; init; } = [];

    /// <summary>An option's value: the page's <c>key</c> (by default), its <c>name</c>, or a property's alias.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_contentValueFrom", Description = "#jfbOptionListSettings_contentValueFromHelp", Order = 3)]
    public string ValueFrom { get; init; } = ContentOptionKind.PageKey;

    /// <summary>An option's label: the page's <c>name</c> (by default) or a property's alias.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_contentLabelFrom", Description = "#jfbOptionListSettings_contentLabelFromHelp", Order = 4)]
    public string LabelFrom { get; init; } = ContentOptionKind.PageName;

    /// <summary>The order of the options; the tree's by default.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_contentOrder", Order = 5)]
    public ContentOptionOrder Order { get; init; }
}
