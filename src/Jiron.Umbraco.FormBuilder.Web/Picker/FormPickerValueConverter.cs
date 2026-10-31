using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// What the Content Delivery API's answer holds for a picked form until the package resolves it after the answer is made
/// (ADR-0127): the form's key, and whether the property is expanded. <see cref="FormBuilderDeliveryApiMiddleware"/>
/// replaces it with <c>{ key, alias, name }</c>, the form itself when expanded, or null, so it never leaves the site.
/// </summary>
/// <param name="Form">The picked form's key.</param>
/// <param name="Expand">Whether the request expands the property.</param>
public sealed record FormPickerDeliveryMarker(
    [property: JsonPropertyName(FormPickerDeliveryMarker.FormName)] Guid Form,
    [property: JsonPropertyName(FormPickerDeliveryMarker.ExpandName)] bool Expand)
{
    /// <summary>The marker's first property, which names it.</summary>
    public const string FormName = "jfbForm";

    /// <summary>The marker's second property.</summary>
    public const string ExpandName = "expand";
}

/// <summary>
/// The form picker's values (ADR-0127): its stored text read as the picked form's key, a
/// <see cref="FormBuilderFormReference"/> in Razor, and in the Content Delivery API a <see cref="FormPickerDeliveryMarker"/>,
/// which needs no lookup, so the synchronous converter never waits on the form's asynchronous cache.
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
    public override Type GetPropertyValueType(IPublishedPropertyType propertyType) => typeof(FormBuilderFormReference);

    /// <inheritdoc />
    public override PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.Element;

    /// <inheritdoc />
    public override object? ConvertSourceToIntermediate(IPublishedElement owner, IPublishedPropertyType propertyType, object? source, bool preview) =>
        source is string text && Guid.TryParse(text, out var key) ? key : null;

    /// <inheritdoc />
    public override object? ConvertIntermediateToObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object? inter, bool preview) =>
        inter is Guid key ? new FormBuilderFormReference(key) : null;

    /// <inheritdoc />
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.Element;

    /// <inheritdoc />
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevelForExpansion(IPublishedPropertyType propertyType) => PropertyCacheLevel.Element;

    /// <inheritdoc />
    public Type GetDeliveryApiPropertyValueType(IPublishedPropertyType propertyType) => typeof(FormPickerDeliveryMarker);

    /// <inheritdoc />
    public object? ConvertIntermediateToDeliveryApiObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object? inter, bool preview, bool expanding) =>
        inter is Guid key ? new FormPickerDeliveryMarker(key, expanding) : null;
}
