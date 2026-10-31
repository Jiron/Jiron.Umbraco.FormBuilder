using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Picker;

/// <summary>
/// The form picker's value converter (ADR-0127): only the picker's properties, the stored text read as a form's key or
/// nothing, a <see cref="FormBuilderFormReference"/> in Razor, and in the Content Delivery API a marker of the key and
/// whether the property is expanded, all cached with the element, since the marker is resolved per request.
/// </summary>
public sealed class FormPickerValueConverterTests
{
    private static readonly Guid Form = Guid.Parse("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084");

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
        Assert.That(_converter.ConvertSourceToIntermediate(_owner, _property, stored, preview: false), Is.EqualTo(Form));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("not a key")]
    [TestCase(42)]
    public void AnythingElse_IsNoForm(object? stored)
    {
        var intermediate = _converter.ConvertSourceToIntermediate(_owner, _property, stored, preview: false);

        Assert.Multiple(() =>
        {
            Assert.That(intermediate, Is.Null);
            Assert.That(_converter.ConvertIntermediateToObject(_owner, _property, PropertyCacheLevel.Element, intermediate, preview: false), Is.Null);
            Assert.That(_converter.ConvertIntermediateToDeliveryApiObject(_owner, _property, PropertyCacheLevel.Element, intermediate, preview: false, expanding: true), Is.Null);
        });
    }

    [Test]
    public void Razor_GetsAReferenceToTheForm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetPropertyValueType(_property), Is.EqualTo(typeof(FormBuilderFormReference)));
            Assert.That(_converter.ConvertIntermediateToObject(_owner, _property, PropertyCacheLevel.Element, Form, preview: false), Is.EqualTo(new FormBuilderFormReference(Form)));
        });
    }

    [TestCase(false, """{"jfbForm":"6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084","expand":false}""")]
    [TestCase(true, """{"jfbForm":"6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084","expand":true}""")]
    public void TheDeliveryApi_GetsTheMarker_WithWhetherThePropertyIsExpanded(bool expanding, string written)
    {
        var marker = _converter.ConvertIntermediateToDeliveryApiObject(_owner, _property, PropertyCacheLevel.Element, Form, preview: false, expanding);

        Assert.Multiple(() =>
        {
            Assert.That(_converter.GetDeliveryApiPropertyValueType(_property), Is.EqualTo(typeof(FormPickerDeliveryMarker)));
            Assert.That(marker, Is.EqualTo(new FormPickerDeliveryMarker(Form, expanding)));
            Assert.That(JsonSerializer.Serialize(marker), Is.EqualTo(written), "the bytes the middleware finds, in this order");
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

    private static IPublishedPropertyType Property(string editorAlias)
    {
        var property = Substitute.For<IPublishedPropertyType>();
        property.EditorAlias.Returns(editorAlias);
        return property;
    }
}
