using System.Collections.Frozen;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The element endpoint's answer (ADR-0128): the view model of a form's first render, which the headless element writes
/// the built-in views from, with the stylesheets and the runtime the views need.
/// </summary>
/// <param name="Contract">The version of this answer's shape, <see cref="Version"/>; an element of another one shows nothing.</param>
/// <param name="Form">The form's view model, as the Razor views get it, without what only a post without JavaScript uses.</param>
/// <param name="Assets">The stylesheets and the script the form's page loads.</param>
internal sealed record ElementAnswer(int Contract, FormViewModel Form, ElementAssets Assets)
{
    /// <summary>The version of the answer's shape the element of this install reads.</summary>
    public const int Version = 1;
}

/// <summary>The addresses below the site's path base of what a form's page loads (ADR-0092, ADR-0105).</summary>
/// <param name="Styles">The stylesheets, the base rules first, then the theme's.</param>
/// <param name="Script">The website runtime's module.</param>
internal sealed record ElementAssets(IReadOnlyList<string> Styles, string Script);

/// <summary>
/// How the view models are written for the element (ADR-0128): their properties in camel case, an enum as its name in
/// camel case, a rich text as its HTML, nothing for null, a control as an object with its <c>kind</c> first; without
/// the view paths, the classes but once at the form, and what only a post without JavaScript uses: the form's action,
/// encoding and post values, and a button's <c>formaction</c>.
/// </summary>
internal static class ElementJson
{
    /// <summary>The name of the property that says which control model an object is.</summary>
    public const string KindName = "kind";

    /// <summary>Each control model's kind: its name without <c>ControlModel</c>, in camel case.</summary>
    public static readonly FrozenDictionary<Type, string> Kinds = new[]
    {
        typeof(TextControlModel),
        typeof(TextAreaControlModel),
        typeof(NumberControlModel),
        typeof(HeadingControlModel),
        typeof(TextBlockControlModel),
        typeof(ImageControlModel),
        typeof(ChoicesControlModel),
        typeof(SelectControlModel),
        typeof(GridControlModel),
        typeof(RankingControlModel),
        typeof(SingleChoiceControlModel),
        typeof(DateControlModel),
        typeof(SliderControlModel),
        typeof(CalculationControlModel),
        typeof(PartsControlModel),
        typeof(FileControlModel),
        typeof(SignatureControlModel),
        typeof(CaptchaControlModel),
        typeof(CustomControlModel),
        typeof(RepeaterControlModel),
    }.ToFrozenDictionary(type => type, type => JsonNamingPolicy.CamelCase.ConvertName(type.Name[..^"ControlModel".Length]));

    // The properties only a post without JavaScript uses, by the type that has them and their written name, the view
    // paths a field and a repeatable group name, and the address the element writes from its own API's (ADR-0211).
    private static readonly FrozenSet<(Type Type, string Name)> Left = new[]
    {
        (typeof(FormViewModel), "action"),
        (typeof(FormViewModel), "encodingType"),
        (typeof(FormViewModel), "postValues"),
        (typeof(FormViewModel), "insightsUrl"),
        (typeof(RankingMoveModel), "action"),
        (typeof(RowButtonModel), "action"),
        (typeof(SummaryChangeModel), "action"),
        (typeof(FieldViewModel), "controlView"),
        (typeof(RepeaterControlModel), "fieldView"),
    }.ToFrozenSet();

    /// <summary>The options the answer is written with.</summary>
    public static readonly JsonSerializerOptions Options = FrozenOptions.Of(new(ElementJsonContext.Default.Options)
    {
        TypeInfoResolver = ElementJsonContext.Default.WithAddedModifier(Shape),
    });

    /// <summary>The answer as UTF-8 JSON.</summary>
    /// <param name="answer">The answer.</param>
    public static byte[] Write(ElementAnswer answer) => JsonSerializer.SerializeToUtf8Bytes(answer, Options.GetTypeInfo(typeof(ElementAnswer)));

    // Leaves out the view paths, the classes below the form and the post-only properties, and writes a field's control
    // with its kind.
    private static void Shape(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var index = info.Properties.Count - 1; index >= 0; index--)
        {
            var property = info.Properties[index];
            if (property.PropertyType == typeof(FormViewPaths)
                || (property.PropertyType == typeof(ThemeClassesModel) && info.Type != typeof(FormViewModel))
                || Left.Contains((info.Type, property.Name)))
            {
                info.Properties.RemoveAt(index);
            }
            else if (info.Type == typeof(FieldViewModel) && property.Name == "control")
            {
                property.CustomConverter = new ControlConverter();
            }
        }
    }

    // A control model as an object with its kind first, then its properties as its own metadata writes them.
    private sealed class ControlConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("The element's answer is written only.");

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(options);
            var type = value.GetType();
            if (!Kinds.TryGetValue(type, out var kind))
            {
                throw new JsonException($"The element has no control of type {type.FullName}; add it to ElementJson.Kinds.");
            }

            using var written = JsonSerializer.SerializeToDocument(value, options.GetTypeInfo(type));
            writer.WriteStartObject();
            writer.WriteString(KindName, kind);
            foreach (var property in written.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}

/// <summary>A rich text as the HTML a view writes for it.</summary>
internal sealed class HtmlContentConverter : JsonConverter<IHtmlContent>
{
    /// <inheritdoc />
    public override IHtmlContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The element's answer is written only.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IHtmlContent value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        using var text = new StringWriter();
        value.WriteTo(text, HtmlEncoder.Default);
        writer.WriteStringValue(text.ToString());
    }
}

/// <summary>An enum as its name in camel case.</summary>
/// <typeparam name="T">The enum.</typeparam>
internal sealed class CamelCaseEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where T : struct, Enum;

/// <summary>The source-generated metadata of the element's answer (ADR-0128).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    MaxDepth = 64,
    Converters =
    [
        typeof(HtmlContentConverter),
        typeof(CamelCaseEnumConverter<FormDisplay>),
        typeof(CamelCaseEnumConverter<FieldLabelElement>),
        typeof(CamelCaseEnumConverter<FieldMark>),
        typeof(CamelCaseEnumConverter<ProgressStyle>),
        typeof(CamelCaseEnumConverter<NoticeKind>),
    ])]
[JsonSerializable(typeof(ElementAnswer))]
[JsonSerializable(typeof(TextControlModel))]
[JsonSerializable(typeof(TextAreaControlModel))]
[JsonSerializable(typeof(NumberControlModel))]
[JsonSerializable(typeof(HeadingControlModel))]
[JsonSerializable(typeof(TextBlockControlModel))]
[JsonSerializable(typeof(ImageControlModel))]
[JsonSerializable(typeof(ChoicesControlModel))]
[JsonSerializable(typeof(SelectControlModel))]
[JsonSerializable(typeof(GridControlModel))]
[JsonSerializable(typeof(RankingControlModel))]
[JsonSerializable(typeof(SingleChoiceControlModel))]
[JsonSerializable(typeof(DateControlModel))]
[JsonSerializable(typeof(SliderControlModel))]
[JsonSerializable(typeof(CalculationControlModel))]
[JsonSerializable(typeof(PartsControlModel))]
[JsonSerializable(typeof(FileControlModel))]
[JsonSerializable(typeof(SignatureControlModel))]
[JsonSerializable(typeof(CaptchaControlModel))]
[JsonSerializable(typeof(CustomControlModel))]
[JsonSerializable(typeof(RepeaterControlModel))]
internal sealed partial class ElementJsonContext : JsonSerializerContext;
