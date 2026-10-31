using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// What the Content Delivery API's answer holds for a picked form until the package resolves it after the answer is made
/// (ADR-0127): the form's key, whether the property is expanded, and the theme and the page after a send the picker's
/// settings give (ADR-0129). <see cref="FormBuilderDeliveryApiMiddleware"/> replaces it with <c>{ key, alias, name }</c>,
/// the form itself when expanded, or null, so it never leaves the site.
/// </summary>
/// <param name="Form">The picked form's key.</param>
/// <param name="Expand">Whether the request expands the property.</param>
/// <param name="Theme">The theme the picker's settings name, or null.</param>
/// <param name="Redirect">The page after a send the picker's settings name, or null.</param>
public sealed record FormPickerDeliveryMarker(
    [property: JsonPropertyName(FormPickerDeliveryMarker.FormName)] Guid Form,
    [property: JsonPropertyName(FormPickerDeliveryMarker.ExpandName)] bool Expand,
    [property: JsonPropertyName(FormPickerDeliveryMarker.ThemeName)] string? Theme = null,
    [property: JsonPropertyName(FormPickerDeliveryMarker.RedirectName)] Guid? Redirect = null)
{
    /// <summary>The marker's first property, which names it.</summary>
    public const string FormName = "jfbForm";

    /// <summary>The marker's second property.</summary>
    public const string ExpandName = "expand";

    /// <summary>The marker's theme, which the answer keeps as <c>theme</c>.</summary>
    public const string ThemeName = "theme";

    /// <summary>The marker's page after a send, which the answer keeps as <c>redirect</c>.</summary>
    public const string RedirectName = "redirect";
}

/// <summary>
/// The form picker's values (ADR-0127, ADR-0129): its stored text read as the picked forms' keys; in Razor a
/// <see cref="FormBuilderFormReference"/> for a single picker and a list of them for a multiple one, each with the theme
/// and the page after a send the settings give; in the Content Delivery API a <see cref="FormPickerDeliveryMarker"/> or a
/// list of them, which need no lookup, so the synchronous converter never waits on the forms' asynchronous cache.
/// </summary>
public sealed class FormPickerValueConverter : PropertyValueConverterBase, IDeliveryApiPropertyValueConverter
{
    /// <inheritdoc />
    public override bool IsConverter(IPublishedPropertyType propertyType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        return propertyType.EditorAlias == FormPickerPropertyEditor.EditorAlias;
    }

    /// <inheritdoc />
    public override Type GetPropertyValueType(IPublishedPropertyType propertyType) =>
        Settings(propertyType).Multiple ? typeof(IReadOnlyList<FormBuilderFormReference>) : typeof(FormBuilderFormReference);

    /// <inheritdoc />
    public override PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.Element;

    /// <inheritdoc />
    public override object? ConvertSourceToIntermediate(IPublishedElement owner, IPublishedPropertyType propertyType, object? source, bool preview) =>
        FormPickerValues.Read(source) is { Count: > 0 } keys ? keys : null;

    /// <inheritdoc />
    public override object? ConvertIntermediateToObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object? inter, bool preview)
    {
        var settings = Settings(propertyType);
        var references = Keys(inter).Select(key => new FormBuilderFormReference(key) { Theme = settings.ThemeAlias, Redirect = settings.RedirectKey }).ToList();
        return settings.Multiple ? references : references.FirstOrDefault();
    }

    /// <inheritdoc />
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.Element;

    /// <inheritdoc />
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevelForExpansion(IPublishedPropertyType propertyType) => PropertyCacheLevel.Element;

    /// <inheritdoc />
    public Type GetDeliveryApiPropertyValueType(IPublishedPropertyType propertyType) =>
        Settings(propertyType).Multiple ? typeof(IReadOnlyList<FormPickerDeliveryMarker>) : typeof(FormPickerDeliveryMarker);

    /// <inheritdoc />
    public object? ConvertIntermediateToDeliveryApiObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object? inter, bool preview, bool expanding)
    {
        var settings = Settings(propertyType);
        var markers = Keys(inter).Select(key => new FormPickerDeliveryMarker(key, expanding, settings.ThemeAlias, settings.RedirectKey)).ToList();
        return settings.Multiple ? markers : markers.FirstOrDefault();
    }

    private static FormPickerConfiguration Settings(IPublishedPropertyType propertyType) =>
        propertyType.DataType.ConfigurationAs<FormPickerConfiguration>() ?? new FormPickerConfiguration();

    private static IReadOnlyList<Guid> Keys(object? inter) => inter as IReadOnlyList<Guid> ?? [];
}
