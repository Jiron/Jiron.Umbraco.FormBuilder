using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Picker;

/// <summary>
/// The form picker's value converter (ADR-0127, ADR-0129): only the picker's properties, the stored text read as the
/// picked forms' keys in order (one key as 6.6c stored it) or nothing, a <see cref="FormBuilderFormReference"/> in Razor
/// for a single picker and a list of them for a multiple one, each with the theme and the page after a send of the
/// settings, and in the Content Delivery API a marker (or a list of them) of the key, whether the property is expanded
/// and the settings' theme and page, all cached with the element, since the marker is resolved per request.
/// </summary>
public sealed class FormPickerValueConverterTests
{
    private static readonly Guid Form = Guid.Parse("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084");

    private static readonly Guid Other = Guid.Parse("2b9e4c10-7d3a-4f5e-8a61-0c4d2e9b7f13");

    private static readonly Guid Thanks = Guid.Parse("9a3f6e21-5c8b-4d07-b1e4-7f2a0d6c3b58");

    private readonly FormPickerValueConverter _converter = new();

    private readonly IPublishedPropertyType _property = Property(FormPickerPropertyEditor.EditorAlias);

    private readonly IPublishedElement _owner = Substitute.For<IPublishedElement>();

    [Test]
    public void OnlyThePickersProperties_AreConverted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_converter.IsConverter(_property), Is.True);
            Assert.That(_converter.IsConverter(Property("Umbraco.TextBox")), Is.False);
            Assert.That(_converter.IsConverter(Property(FormPickerPropertyEditor.EditorAlias.ToUpperInvariant())), Is.False, "an alias is matched exactly");
        });
    }

    [TestCase("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084")]
    [TestCase("6F1C3A52-31F4-4E57-9D2E-61B0F6A1C084")]
    [TestCase("6f1c3a5231f44e579d2e61b0f6a1c084")]
    public void TheStoredText_IsTheFormsKey(string stored) =>
        Assert.That(_converter.ConvertSourceToIntermediate(_owner, _property, stored, preview: false), Is.EqualTo(new[] { Form }));

    [Test]
    public void SeveralKeys_AreTheFormsInOrder_EachOnce() =>
        Assert.That(
            _converter.ConvertSourceToIntermediate(_owner, _property, $"{Other:D}, {Form:D},{Other:D}", preview: false),
            Is.EqualTo(new[] { Other, Form }));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("not a key")]
    [TestCase("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084,not a key")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase(42)]
    public void AnythingElse_IsNoForm(object? stored)
    {
        var intermediate = _converter.ConvertSourceToIntermediate(_owner, _property, stored, preview: false);
        var many = Property(FormPickerPropertyEditor.EditorAlias, new FormPickerConfiguration { Multiple = true });

        Assert.Multiple(() =>
        {
            Assert.That(intermediate, Is.Null);
            Assert.That(_converter.ConvertIntermediateToObject(_owner, _property, PropertyCacheLevel.Element, intermediate, preview: false), Is.Null);
            Assert.That(_converter.ConvertIntermediateToDeliveryApiObject(_owner, _property, PropertyCacheLevel.Element, intermediate, preview: false, expanding: true), Is.Null);
            Assert.That(_converter.ConvertIntermediateToObject(_owner, many, PropertyCacheLevel.Element, intermediate, preview: false), Is.Empty, "a multiple picker's empty list");
            Assert.That(_converter.ConvertIntermediateToDeliveryApiObject(_owner, many, PropertyCacheLevel.Element, intermediate, preview: false, expanding: false), Is.Empty);
        });
    }

    [Test]
    public void Razor_GetsAReferenceToTheForm_WithTheSettingsThemeAndPage()
    {
        var themed = Property(FormPickerPropertyEditor.EditorAlias, new FormPickerConfiguration { Theme = " bootstrap5 ", Redirect = Thanks.ToString("D") });

        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetPropertyValueType(_property), Is.EqualTo(typeof(FormBuilderFormReference)));
            Assert.That(_converter.ConvertIntermediateToObject(_owner, _property, PropertyCacheLevel.Element, new[] { Form }, preview: false), Is.EqualTo(new FormBuilderFormReference(Form)));
            Assert.That(
                _converter.ConvertIntermediateToObject(_owner, themed, PropertyCacheLevel.Element, new[] { Form }, preview: false),
                Is.EqualTo(new FormBuilderFormReference(Form) { Theme = "bootstrap5", Redirect = Thanks }));
        });
    }

    [Test]
    public void Razor_GetsAListOfReferences_FromAMultiplePicker()
    {
        var many = Property(FormPickerPropertyEditor.EditorAlias, new FormPickerConfiguration { Multiple = true, Theme = "unstyled" });

        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetPropertyValueType(many), Is.EqualTo(typeof(IReadOnlyList<FormBuilderFormReference>)));
            Assert.That(
                _converter.ConvertIntermediateToObject(_owner, many, PropertyCacheLevel.Element, new[] { Other, Form }, preview: false),
                Is.EqualTo(new[] { new FormBuilderFormReference(Other) { Theme = "unstyled" }, new FormBuilderFormReference(Form) { Theme = "unstyled" } }));
        });
    }

    [TestCase(false, """{"jfbForm":"6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084","expand":false,"theme":null,"redirect":null}""")]
    [TestCase(true, """{"jfbForm":"6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084","expand":true,"theme":null,"redirect":null}""")]
    public void TheDeliveryApi_GetsTheMarker_WithWhetherThePropertyIsExpanded(bool expanding, string written)
    {
        var marker = _converter.ConvertIntermediateToDeliveryApiObject(_owner, _property, PropertyCacheLevel.Element, new[] { Form }, preview: false, expanding);

        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetDeliveryApiPropertyValueType(_property), Is.EqualTo(typeof(FormPickerDeliveryMarker)));
            Assert.That(marker, Is.EqualTo(new FormPickerDeliveryMarker(Form, expanding)));
            Assert.That(JsonSerializer.Serialize(marker), Is.EqualTo(written), "the bytes the middleware finds, in this order");
        });
    }

    [Test]
    public void TheDeliveryApi_GetsAListOfMarkers_WithTheSettingsThemeAndPage_FromAMultiplePicker()
    {
        var many = Property(FormPickerPropertyEditor.EditorAlias, new FormPickerConfiguration { Multiple = true, Theme = "bootstrap5", Redirect = Thanks.ToString("D") });
        var markers = _converter.ConvertIntermediateToDeliveryApiObject(_owner, many, PropertyCacheLevel.Element, new[] { Other, Form }, preview: false, expanding: false);

        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetDeliveryApiPropertyValueType(many), Is.EqualTo(typeof(IReadOnlyList<FormPickerDeliveryMarker>)));
            Assert.That(markers, Is.EqualTo(new[] { new FormPickerDeliveryMarker(Other, false, "bootstrap5", Thanks), new FormPickerDeliveryMarker(Form, false, "bootstrap5", Thanks) }));
        });
    }

    [Test]
    public void EveryCacheLevel_IsTheElements()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetPropertyCacheLevel(_property), Is.EqualTo(PropertyCacheLevel.Element));
            Assert.That(_converter.GetDeliveryApiPropertyCacheLevel(_property), Is.EqualTo(PropertyCacheLevel.Element));
            Assert.That(_converter.GetDeliveryApiPropertyCacheLevelForExpansion(_property), Is.EqualTo(PropertyCacheLevel.Element));
        });
    }

    private static IPublishedPropertyType Property(string editorAlias, FormPickerConfiguration? settings = null)
    {
        var property = Substitute.For<IPublishedPropertyType>();
        property.EditorAlias.Returns(editorAlias);
        property.DataType.Returns(new PublishedDataType(1, editorAlias, editorAlias, new Lazy<object?>(() => settings ?? new FormPickerConfiguration())));
        return property;
    }
}
