using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>Where the content action puts what it creates.</summary>
public enum ContentParentSource
{
    /// <summary>The picked content item.</summary>
    [FormBuilderChoice("#jfbActionChoices_contentParentPicked")]
    Picked,

    /// <summary>The page the form was sent from.</summary>
    [FormBuilderChoice("#jfbActionChoices_contentParentPage")]
    Page,

    /// <summary>The root of the site the form was sent from.</summary>
    [FormBuilderChoice("#jfbActionChoices_contentParentSiteRoot")]
    SiteRoot,
}

/// <summary>One property of a content action's mapping: a field's answer, or a fixed value that may hold merge tags.</summary>
/// <param name="Property">The property's alias.</param>
/// <param name="Field">The field whose answer it takes.</param>
/// <param name="Value">The fixed value when it names no field.</param>
internal sealed record ContentMapping(string Property, Guid? Field, string? Value);

/// <summary>The settings of the content action (section 4.4, ADR-0162).</summary>
public sealed class ContentActionSettings
{
    /// <summary>The alias of the document type it creates.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentDocumentType", Order = 1, Editor = "documentTypePicker")]
    public string? DocumentType { get; init; }

    /// <summary>Where the parent comes from.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentParentSource", Order = 2)]
    public ContentParentSource ParentSource { get; init; }

    /// <summary>
    /// The picked parent, empty for the content root. For the other sources it is required and marks how far a visitor's page can reach:
    /// a form's page comes from the request, so what it creates stays at this item or below it.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_contentParent", Order = 3, Kind = FieldSettingKind.Key, Editor = "documentPicker")]
    public Guid? Parent { get; init; }

    /// <summary>The name of the item, which may hold merge tags.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentName", Order = 4, SupportsMergeTags = true)]
    public string? Name { get; init; }

    /// <summary>The properties it sets, as <c>[{"property": "title", "field": "...", "value": "..."}]</c>.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentMapping", Order = 5, Editor = "contentMapping")]
    public JsonElement? Mapping { get; init; }

    /// <summary>Whether it publishes the item, or only saves it for an editor to look at first.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentPublish", Order = 6)]
    public bool Publish { get; init; }

    /// <summary>
    /// Whether a child of the same type and name under the parent is updated instead of a new one created. What a visitor typed can
    /// choose the item, so a site turns it on only where that is what it wants.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_contentUpdateExisting", Order = 7, Group = FieldSettingGroup.Advanced)]
    public bool UpdateExisting { get; init; }

    /// <summary>The media folder uploaded files go to when the mapping names a file field; empty for the media root.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentMediaFolder", Order = 8, Kind = FieldSettingKind.Key, Editor = "mediaPicker", Group = FieldSettingGroup.Advanced)]
    public Guid? MediaFolder { get; init; }

    /// <summary>The sensitive fields it may copy (ADR-0014); none by default.</summary>
    [FormBuilderSetting("#jfbActionSettings_contentIncludeSensitive", Order = 9, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.Sensitive, Group = FieldSettingGroup.Advanced)]
    public IReadOnlyList<Guid> IncludeSensitive { get; init; } = [];
}

/// <summary>
/// Creates or updates a content item from an entry (ROADMAP 8.5, section 4.4, ADR-0162). A field goes only to a property whose editor can
/// hold its answer, which publishing checks and the run converts. An uploaded file goes to media only when the mapping names it, with the
/// same checks as a mail's attachment. The keys of what it creates come from the run's delivery key, so a retry finds what an attempt
/// before it created.
/// </summary>
internal sealed partial class ContentActionType(
    IContentService content,
    IContentTypeService contentTypes,
    IMediaService media,
    MediaFileManager mediaFiles,
    MediaUrlGeneratorCollection urlGenerators,
    IShortStringHelper shortStrings,
    IContentTypeBaseServiceProvider typeProvider,
    ILanguageService languages,
    IOptionsMonitor<ContentSettings> uploads,
    IFormBuilderMergeTagService tags,
    IEntryRepository entries,
    IFormBuilderFileStore files,
    IEnumerable<IFormBuilderFileScanner> scanners,
    IJsonSerializer json,
    ILogger<ContentActionType> logger)
    : FormBuilderActionTypeBase<ContentActionSettings>(Declared)
{
    /// <summary>The type's alias.</summary>
    public const string Alias = "content";

    /// <summary>The code of a run whose document type or parent is gone.</summary>
    public const string TargetMissing = "target_missing";

    /// <summary>The code of a run whose name rendered blank.</summary>
    public const string NoName = "no_name";

    /// <summary>The code of a run Umbraco refused to save or publish.</summary>
    public const string Refused = "content_refused";

    /// <summary>The code of a run that waits for a mapped file's scan.</summary>
    public const string ScanPending = "scan_pending";

    private const string FileFieldType = "file";

    private const int MaxNameLength = 255;

    private static readonly ActionTypeDeclaration Declared = new()
    {
        Alias = Alias,
        Name = "#jfbActionTypes_content",
        Icon = "icon-document",
        Test = ActionTestKind.DryRun,
    };

    private readonly bool _scanned = scanners.Any();

    /// <summary>Whether a property with this editor can hold an answer of this field type.</summary>
    /// <param name="fieldType">The field's type.</param>
    /// <param name="editor">The property's editor alias.</param>
    internal static bool Fits(string fieldType, string editor) => fieldType switch
    {
        FileFieldType => editor == Constants.PropertyEditors.Aliases.MediaPicker3,
        "number" or "rating" or "scale" or "slider" or "calculation" => editor is Constants.PropertyEditors.Aliases.Integer or Constants.PropertyEditors.Aliases.Decimal or Constants.PropertyEditors.Aliases.TextBox or Constants.PropertyEditors.Aliases.Label,
        "date" or "datetime" => editor is Constants.PropertyEditors.Aliases.DateTime or Constants.PropertyEditors.Aliases.TextBox or Constants.PropertyEditors.Aliases.Label,
        "checkbox" or "consent" or "yesno" => editor is Constants.PropertyEditors.Aliases.Boolean or Constants.PropertyEditors.Aliases.TextBox or Constants.PropertyEditors.Aliases.Label,
        "divider" or "heading" or "paragraph" or "captcha" or "repeater" or "image" => false,
        _ => IsText(editor),
    };

    /// <summary>What an editor stores for an answer as it is read, or the text itself where the editor holds text; false when it cannot be.</summary>
    /// <param name="editor">The property's editor alias.</param>
    /// <param name="text">The answer as it is read.</param>
    /// <param name="value">What to store.</param>
    internal static bool TryConvert(string editor, string text, out object? value)
    {
        value = null;
        switch (editor)
        {
            case Constants.PropertyEditors.Aliases.Boolean:
                var isTrue = text is "true" or "1" or "yes";
                value = isTrue ? 1 : 0;
                return isTrue || text is "false" or "0" or "no" or "";
            case Constants.PropertyEditors.Aliases.Integer when text.Length > 0:
                var fits = int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole);
                value = whole;
                return fits;
            case Constants.PropertyEditors.Aliases.Decimal when text.Length > 0:
                var isNumber = decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number);
                value = number;
                return isNumber;
            case Constants.PropertyEditors.Aliases.DateTime when text.Length > 0:
                var isDate = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
                value = date;
                return isDate;
            case Constants.PropertyEditors.Aliases.Integer or Constants.PropertyEditors.Aliases.Decimal or Constants.PropertyEditors.Aliases.DateTime:
                return true;
            default:
                value = text;
                return true;
        }
    }

    /// <inheritdoc />
    protected override void CheckSettings(ContentActionSettings settings, ActionSettingsCheck check)
    {
        var type = string.IsNullOrWhiteSpace(settings.DocumentType) ? null : contentTypes.Get(settings.DocumentType);
        if (type is null)
        {
            check.Invalid("documentType", "This site has no such document type.");
        }

        if (settings.ParentSource != ContentParentSource.Picked && settings.Parent is null)
        {
            check.Invalid("parent", "A parent taken from the visitor's page needs an item it must stay under.");
        }

        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            check.Invalid("name", "An item needs a name.");
        }
        else
        {
            foreach (var problem in tags.Check(settings.Name, MergeTagPlace.Action, MergeTagContext.Text, check.Definition))
            {
                // A tag that names nothing renders empty: the form check warns (ADR-0177).
                if (problem.IsWarning)
                {
                    check.Warn("name", problem.Code, MergeTagFormCheck.Sentence(problem));
                }
                else
                {
                    check.Invalid("name", $"A merge tag at {problem.At} cannot be used here: {problem.Code}.");
                }
            }
        }

        var known = DefinitionKeys.Fields(check.Definition).ToDictionary(field => field.Key);
        if (settings.IncludeSensitive.Any(field => !known.ContainsKey(field)))
        {
            check.Invalid("includeSensitive", "Each must be a field of this form.");
        }

        if (MappingOf(settings.Mapping) is not { } mapping)
        {
            check.Invalid("mapping", "The mapping is a list of properties, each with a field or a value.");
            return;
        }

        var properties = type?.CompositionPropertyTypes.ToDictionary(property => property.Alias, StringComparer.Ordinal);
        foreach (var item in mapping)
        {
            CheckItem(item, properties, known, check);
        }
    }

    /// <inheritdoc />
    protected override async Task<ActionResult> RunAsync(ContentActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.DocumentType) || contentTypes.Get(settings.DocumentType) is null)
        {
            return ActionResult.Failed(TargetMissing);
        }

        var scope = context.Scope with { IncludedSensitive = settings.IncludeSensitive.ToHashSet() };
        var name = Cut((await tags.RenderAsync(settings.Name ?? string.Empty, MergeTagPlace.Action, MergeTagContext.Text, scope, cancellationToken)).Text.Trim());
        if (name.Length == 0)
        {
            return ActionResult.Failed(NoName);
        }

        if (Parent(settings, context.Scope) is not { } parent)
        {
            return ActionResult.Failed(TargetMissing);
        }

        var mappings = MappingOf(settings.Mapping) ?? [];
        if (await WaitsForScanAsync(mappings, settings, context, cancellationToken))
        {
            return ActionResult.TryAgain(ScanPending);
        }

        var item = Find(settings, context, parent, name) ?? content.Create(name, parent == Guid.Empty ? Constants.System.Root : content.GetById(parent)!.Id, settings.DocumentType);
        item.Key = item.HasIdentity ? item.Key : KeyOf(context.DeliveryKey);
        item.Name = name;
        var culture = item.ContentType.VariesByCulture() ? await CultureAsync(scope.Culture) : null;
        if (culture is not null)
        {
            item.SetCultureName(name, culture);
        }

        // Files last: an answer that fails the run should leave nothing in the media library.
        foreach (var mapping in mappings.OrderBy(mapping => IsFileMapping(mapping, scope.Form)))
        {
            if (!await SetAsync(item, mapping, culture, settings, context, scope, cancellationToken))
            {
                return ActionResult.Failed(Refused);
            }
        }

        if (!content.Save(item).Success)
        {
            return ActionResult.Failed(Refused);
        }

        return settings.Publish && !content.Publish(item, [culture ?? "*"]).Success ? ActionResult.Failed(Refused) : ActionResult.Succeeded;
    }

    /// <inheritdoc />
    protected override Task<ActionTestResult> TestAsync(ContentActionSettings settings, ActionTestContext context, CancellationToken cancellationToken)
    {
        var type = string.IsNullOrWhiteSpace(settings.DocumentType) ? null : contentTypes.Get(settings.DocumentType);
        return Task.FromResult(type is null
            ? new ActionTestResult(ActionTestOutcome.Failed, "This site has no such document type.")
            : new ActionTestResult(ActionTestOutcome.Sent, $"An entry would {(settings.Publish ? "publish" : "save")} a {type.Name}. Nothing was created."));
    }

    // The key of what a run creates: the same for every attempt of the run, and for no other run.
    // It is laid out as a version 4 key, as Umbraco's media paths refuse the time-ordered kind, which any 16 bytes might read as.
    private static Guid KeyOf(string deliveryKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("jfb.content." + deliveryKey)).AsSpan(0, 16);
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static string Cut(string name) =>
        name.Length <= MaxNameLength ? name : name[..(char.IsHighSurrogate(name[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength)];

    private static bool IsFileMapping(ContentMapping mapping, FormDefinition form) =>
        mapping.Field is { } key && DefinitionKeys.Fields(form).Any(field => field.Key == key && field.Type == FileFieldType);

    // Whether the action may copy a field: not a sensitive one, or a sensitive repeatable group's, unless it names the one or the other (ADR-0014).
    private static bool MayCopy(FormField field, FormDefinition form, ContentActionSettings settings)
    {
        var group = form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).FirstOrDefault(each => each.Fields.Any(child => child.Key == field.Key));
        return (!field.Sensitive && group?.Sensitive != true)
            || settings.IncludeSensitive.Contains(field.Key)
            || (group is not null && settings.IncludeSensitive.Contains(group.Key));
    }

    private static bool IsText(string editor) =>
        editor is Constants.PropertyEditors.Aliases.TextBox or Constants.PropertyEditors.Aliases.TextArea or Constants.PropertyEditors.Aliases.Label;

    private static List<ContentMapping>? MappingOf(JsonElement? element)
    {
        if (element is not { ValueKind: not JsonValueKind.Null } given)
        {
            return [];
        }

        if (given.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var mapping = new List<ContentMapping>();
        foreach (var item in given.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("property", out var property) || property.ValueKind != JsonValueKind.String || property.GetString() is not { Length: > 0 } alias)
            {
                return null;
            }

            Guid? field = null;
            if (item.TryGetProperty("field", out var named) && named.ValueKind != JsonValueKind.Null)
            {
                if (named.ValueKind != JsonValueKind.String || !named.TryGetGuid(out var key))
                {
                    return null;
                }

                field = key;
            }

            string? value = null;
            if (item.TryGetProperty("value", out var written) && written.ValueKind != JsonValueKind.Null)
            {
                if (written.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                value = written.GetString();
            }

            if (field is null == value is null)
            {
                return null;
            }

            mapping.Add(new ContentMapping(alias, field, value));
        }

        return mapping;
    }

    private void CheckItem(ContentMapping item, Dictionary<string, IPropertyType>? properties, Dictionary<Guid, FormField> fields, ActionSettingsCheck check)
    {
        if (item.Value is { } written)
        {
            foreach (var problem in tags.Check(written, MergeTagPlace.Action, MergeTagContext.Text, check.Definition))
            {
                if (problem.IsWarning)
                {
                    check.Warn("mapping", problem.Code, $"In the value of '{item.Property}': {MergeTagFormCheck.Sentence(problem)}");
                }
                else
                {
                    check.Invalid("mapping", $"A merge tag at {problem.At} in the value of '{item.Property}' cannot be used here: {problem.Code}.");
                }
            }
        }

        if (properties is null)
        {
            return;
        }

        if (!properties.TryGetValue(item.Property, out var property))
        {
            check.Invalid("mapping", $"The document type has no property '{item.Property}'.");
            return;
        }

        if (item.Field is not { } key)
        {
            // Text can go anywhere text goes; anything else has to be what the editor stores, known now, which no tag is.
            if (!IsText(property.PropertyEditorAlias) && (!IsConvertible(property.PropertyEditorAlias) || !TryConvert(property.PropertyEditorAlias, item.Value!, out _)))
            {
                check.Invalid("mapping", $"The property '{item.Property}' cannot hold this value.");
            }

            return;
        }

        if (!fields.TryGetValue(key, out var field))
        {
            check.Invalid("mapping", $"The field mapped to '{item.Property}' is not a field of this form.");
        }
        else if (!Fits(field.Type, property.PropertyEditorAlias))
        {
            check.Invalid("mapping", $"The property '{item.Property}' cannot hold an answer of a {field.Type} field.");
        }
    }

    private static bool IsConvertible(string editor) =>
        editor is Constants.PropertyEditors.Aliases.Boolean or Constants.PropertyEditors.Aliases.Integer or Constants.PropertyEditors.Aliases.Decimal or Constants.PropertyEditors.Aliases.DateTime;

    // A variant document needs one of the site's languages: the entry's, or the default where the site does not have that.
    private async Task<string?> CultureAsync(string culture) =>
        (await languages.GetAsync(culture))?.IsoCode ?? await languages.GetDefaultIsoCodeAsync();

    private IContent? Find(ContentActionSettings settings, ActionRunContext context, Guid parent, string name)
    {
        if (content.GetById(KeyOf(context.DeliveryKey)) is { } created)
        {
            return created;
        }

        if (!settings.UpdateExisting)
        {
            return null;
        }

        bool IsSame(IContent child) => child.ContentType.Alias == settings.DocumentType && string.Equals(child.Name, name, StringComparison.Ordinal);
        if (parent == Guid.Empty)
        {
            return content.GetRootContent().FirstOrDefault(IsSame);
        }

        const int PageSize = 500;
        var parentId = content.GetById(parent)!.Id;
        long total;
        var page = 0;
        do
        {
            var children = content.GetPagedChildren(parentId, page, PageSize, out total, null, null, null).ToList();
            if (children.FirstOrDefault(IsSame) is { } found)
            {
                return found;
            }

            page++;
        }
        while (page * (long)PageSize < total);
        return null;
    }

    // The parent's key; empty for the content root, null when the one it names is gone, or lies outside where the action may reach. The
    // visitor's page comes from the request, so nothing it names is taken on its word.
    private Guid? Parent(ContentActionSettings settings, MergeTagScope scope) => settings.ParentSource switch
    {
        ContentParentSource.Page => WithinReach(scope.Page, settings.Parent),
        ContentParentSource.SiteRoot => WithinReach(scope.Site, settings.Parent),
        _ when settings.Parent is { } picked => content.GetById(picked) is null or { Trashed: true } ? null : picked,
        _ => Guid.Empty,
    };

    private Guid? WithinReach(Guid? key, Guid? limit)
    {
        if (key is not { } target || limit is not { } top || content.GetById(target) is not { Trashed: false } item || content.GetById(top) is not { } reach)
        {
            return null;
        }

        return item.Id == reach.Id || item.Path.Split(',').Contains(reach.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal) ? target : null;
    }

    private async Task<bool> WaitsForScanAsync(List<ContentMapping> mappings, ContentActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        if (!_scanned || context.EntryId is not { } entry)
        {
            return false;
        }

        var mapped = DefinitionKeys.Fields(context.Scope.Form)
            .Where(field => field.Type == FileFieldType && MayCopy(field, context.Scope.Form, settings) && mappings.Any(mapping => mapping.Field == field.Key))
            .Select(field => field.Key)
            .ToHashSet();
        return mapped.Count > 0 && (await entries.ListFilesAsync(entry, cancellationToken)).Any(file => mapped.Contains(file.FieldKey) && file.ScanState == FileScanStates.Pending);
    }

    // False when the answer is not what the property's editor stores.
    private async Task<bool> SetAsync(IContent item, ContentMapping mapping, string? itemCulture, ContentActionSettings settings, ActionRunContext context, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var type = contentTypes.Get(item.ContentType.Alias)?.CompositionPropertyTypes.FirstOrDefault(property => property.Alias == mapping.Property);
        if (type is null)
        {
            LogNoProperty(logger, mapping.Property);
            return true;
        }

        var culture = type.VariesByCulture() ? itemCulture : null;
        var editor = type.PropertyEditorAlias;
        string text;
        if (mapping.Field is { } key && DefinitionKeys.Fields(scope.Form).FirstOrDefault(field => field.Key == key) is { } field)
        {
            // A sensitive answer that nobody named stays out altogether: no mask in the item, and no file in the media library.
            if (!MayCopy(field, scope.Form, settings))
            {
                return true;
            }

            if (field.Type == FileFieldType)
            {
                item.SetValue(mapping.Property, await MediaValueAsync(key, settings, context, cancellationToken), culture);
                return true;
            }

            text = IsText(editor)
                ? (await tags.RenderAsync($"{{{{ field.{field.Alias ?? string.Empty} }}}}", MergeTagPlace.Action, MergeTagContext.Text, scope, cancellationToken)).Text
                : WireTextOf(field, scope);
        }
        else
        {
            text = (await tags.RenderAsync(mapping.Value ?? string.Empty, MergeTagPlace.Action, MergeTagContext.Text, scope, cancellationToken)).Text;
        }

        if (!TryConvert(editor, text, out var value))
        {
            return false;
        }

        try
        {
            item.SetValue(mapping.Property, value, culture);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
        {
            return false;
        }

        return true;
    }

    // An answer as the field reads it, not as it shows: a consent reads as true, whatever it stores.
    private static string WireTextOf(FormField field, MergeTagScope scope)
    {
        if (scope.Answers.Hidden.Contains(field.Key) || scope.Answers.Find(field.Key) is not { } answer)
        {
            return string.Empty;
        }

        return answer.Values[0].Text ?? string.Empty;
    }

    // The field's files as the value of a media picker. They are copied only here, where the mapping names them, and held to what a
    // mail's attachments are: not quarantined, and scanned on a site that has a scanner (ADR-0144).
    private async Task<string?> MediaValueAsync(Guid field, ContentActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        if (context.EntryId is not { } entry)
        {
            return null;
        }

        var picked = new List<object>();
        var folder = settings.MediaFolder is { } key && media.GetById(key) is { } found ? found.Id : Constants.System.Root;
        foreach (var file in (await entries.ListFilesAsync(entry, cancellationToken)).Where(file => file.FieldKey == field && !IsHeldBack(file)))
        {
            await using var bytes = await files.OpenReadAsync(file.StoredName, cancellationToken);
            if (bytes is null)
            {
                continue;
            }

            var name = Cut(Path.GetFileName(file.OriginalName));
            if (!uploads.CurrentValue.IsFileAllowedForUpload(Path.GetExtension(name).TrimStart('.')))
            {
                LogMediaRefused(logger, file.Key);
                continue;
            }

            var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            var type = isImage ? Constants.Conventions.MediaTypes.Image : Constants.Conventions.MediaTypes.File;
            var mediaKey = KeyOf(context.DeliveryKey + "." + file.Key);
            if (media.GetById(mediaKey) is null)
            {
                var item = media.CreateMedia(name, folder, type);
                item.Key = mediaKey;
                item.SetValue(mediaFiles, urlGenerators, shortStrings, typeProvider, Constants.Conventions.Media.File, name, bytes);
                if (!media.Save(item).Success)
                {
                    LogMediaRefused(logger, file.Key);
                    continue;
                }
            }

            picked.Add(new { key = KeyOf(context.DeliveryKey + ".picked." + file.Key), mediaKey, mediaTypeAlias = type });
        }

        return picked.Count == 0 ? null : json.Serialize(picked);
    }

    private bool IsHeldBack(StoredEntryFile file) => file.ScanState switch
    {
        FileScanStates.Quarantined => true,
        FileScanStates.Pending => _scanned,
        _ => false,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The document type has no property {Property} any more, so the content action left it out")]
    private static partial void LogNoProperty(ILogger logger, string property);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The uploaded file {File} is not one Umbraco allows or saves as media, so the content action left it out")]
    private static partial void LogMediaRefused(ILogger logger, Guid file);
}
