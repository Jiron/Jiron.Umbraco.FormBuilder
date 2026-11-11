using System.Buffers;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// Resolves the form picker's markers in the Content Delivery API's answers (ADR-0127), after the answer is made, so
/// nothing waits on the form's asynchronous cache inside Umbraco's synchronous converters: each marker becomes the
/// picked form's <c>{ key, alias, name }</c>, with <c>form</c> as the definition endpoint answers it in the request's
/// culture when the property is expanded, or <c>null</c> for a form that is missing, not published or not headless.
/// Every other byte of the answer stays as Umbraco wrote it, and an answer without a marker passes untouched.
/// </summary>
internal sealed class FormBuilderDeliveryApiMiddleware : IMiddleware
{
    /// <summary>The Content Delivery API's paths.</summary>
    public const string Root = "/umbraco/delivery/api";

    // The marker's name (FormPickerDeliveryMarker.FormName) as it stands quoted in an answer.
    private static ReadOnlySpan<byte> Marker => "\"jfbForm\""u8;

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!context.Request.Path.StartsWithSegments(Root, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var response = context.Response;
        var original = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            response.Body = original;
        }

        var answer = buffer.ToArray();
        var written = response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true && answer.AsSpan().IndexOf(Marker) >= 0
            ? await ResolveAsync(context, answer)
            : answer;
        if (!ReferenceEquals(written, answer))
        {
            response.ContentLength = written.Length;
        }

        await original.WriteAsync(written, context.RequestAborted);
    }

    /// <summary>
    /// The markers of an answer, in order: each one's bytes, the form, whether it is expanded, and the page it is on, the
    /// nearest item around it that has a route (a block's element has none).
    /// </summary>
    internal static List<FoundMarker> Find(byte[] json)
    {
        var found = new List<FoundMarker>();
        var frames = new Stack<Frame>();
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 256 });
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    if (frames.TryPeek(out var parent))
                    {
                        parent.TakeContainer();
                    }

                    frames.Push(new Frame((int)reader.TokenStartIndex, reader.TokenType == JsonTokenType.StartObject));
                    break;
                case JsonTokenType.PropertyName:
                    frames.Peek().Pending = reader.GetString();
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    var frame = frames.Pop();
                    if (frame.IsMarker)
                    {
                        found.Add(new FoundMarker(frame.Start, (int)reader.BytesConsumed, frame.Form!.Value, frame.Expand!.Value, frames.FirstOrDefault(each => each.Route && each.Id is not null)?.Id, frame.Theme, frame.Redirect));
                    }

                    break;
                default:
                    if (frames.TryPeek(out var owner))
                    {
                        owner.TakeValue(ref reader);
                    }

                    break;
            }
        }

        return found;
    }

    // Each marker replaced by what it stands for, each distinct one worked out once.
    private static async Task<byte[]> ResolveAsync(HttpContext context, byte[] answer)
    {
        var markers = Find(answer);
        if (markers.Count == 0)
        {
            return answer;
        }

        var services = context.RequestServices;
        var forms = services.GetRequiredService<PublishedFormCache>();
        var requests = services.GetRequiredService<FormRequests>();
        var pages = services.GetRequiredService<FormPages>();
        var cancellationToken = context.RequestAborted;
        var culture = await pages.CultureAsync(RequestedCulture(context.Request));
        var values = new Dictionary<(Guid Form, bool Expand, Guid? Page, string? Theme, Guid? Redirect), byte[]>();
        var output = new ArrayBufferWriter<byte>(answer.Length);
        var at = 0;
        foreach (var marker in markers)
        {
            output.Write(answer.AsSpan(at, marker.Start - at));
            var known = (marker.Form, marker.Expand, marker.Page, marker.Theme, marker.Redirect);
            if (!values.TryGetValue(known, out var value))
            {
                value = await ValueAsync(marker, forms, requests, pages, culture, context.Request.Host.Value, cancellationToken);
                values[known] = value;
            }

            output.Write(value.AsSpan());
            at = marker.End;
        }

        output.Write(answer.AsSpan(at));
        return output.WrittenSpan.ToArray();
    }

    // What a marker stands for: the form's key, alias and name, the theme and the page after a send the picker's settings
    // give (ADR-0129), and the definition endpoint's form when expanded.
    private static async Task<byte[]> ValueAsync(FoundMarker marker, PublishedFormCache forms, FormRequests requests, FormPages pages, string culture, string? host, CancellationToken cancellationToken)
    {
        if (await forms.GetAsync(marker.Form, cancellationToken) is not { } form || !form.Published.Definition.Settings.Developer.Headless)
        {
            return "null"u8.ToArray();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("key", marker.Form);
            writer.WriteString("alias", form.Published.Form.Alias);
            writer.WriteString("name", form.Published.Form.Name);
            if (marker.Theme is { } theme)
            {
                writer.WriteString(FormPickerDeliveryMarker.ThemeName, theme);
            }

            if (marker.Redirect is { } redirect)
            {
                writer.WriteString(FormPickerDeliveryMarker.RedirectName, redirect);
            }

            if (marker.Expand)
            {
                var definition = await requests.DefinitionAsync(form, culture, await pages.PageAsync(marker.Page, host), cancellationToken);
                using var document = JsonDocument.Parse(definition.Body);
                writer.WritePropertyName("form");
                writer.WriteRawValue(document.RootElement.GetProperty("form").GetRawText(), skipInputValidation: true);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    // The culture the Delivery API serves: the first language the request accepts, when it reads as one.
    private static string? RequestedCulture(HttpRequest request)
    {
        var first = request.Headers.AcceptLanguage.ToString().Split(',')[0].Split(';')[0].Trim();
        return first.Length > 0 && FormPages.IsCulture(first) ? first : null;
    }

    /// <summary>A marker found in an answer.</summary>
    /// <param name="Start">Where its object starts.</param>
    /// <param name="End">Where its object ends.</param>
    /// <param name="Form">The picked form's key.</param>
    /// <param name="Expand">Whether the property is expanded.</param>
    /// <param name="Page">The key of the page it is on, or null.</param>
    /// <param name="Theme">The theme the picker's settings name, or null.</param>
    /// <param name="Redirect">The page after a send the picker's settings name, or null.</param>
    internal sealed record FoundMarker(int Start, int End, Guid Form, bool Expand, Guid? Page, string? Theme = null, Guid? Redirect = null);

    // An object or an array being read: an object's properties as far as they tell a marker or an item with a route.
    private sealed class Frame(int start, bool isObject)
    {
        private int _properties;

        // Whether the marker's optional properties came, each in its place after the first two.
        private bool _theme;

        private bool _redirect;

        public int Start => start;

        public string? Pending { get; set; }

        public bool Route { get; private set; }

        public Guid? Id { get; private set; }

        public Guid? Form { get; private set; }

        public bool? Expand { get; private set; }

        public string? Theme { get; private set; }

        public Guid? Redirect { get; private set; }

        public bool IsMarker => isObject && Form is not null && Expand is not null && _properties == 2 + (_theme ? 1 : 0) + (_redirect ? 1 : 0);

        // An object or an array as the value of the property just named: a route, or any other property.
        public void TakeContainer()
        {
            if (Next() is "route")
            {
                Route = true;
            }
        }

        // A text, a number, a boolean or null as the value of the property just named.
        public void TakeValue(ref Utf8JsonReader reader)
        {
            switch (Next())
            {
                case "route":
                    Route = true;
                    break;
                case "id" when reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var id):
                    Id = id;
                    break;
                case FormPickerDeliveryMarker.FormName when _properties == 1 && reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var form):
                    Form = form;
                    break;
                case FormPickerDeliveryMarker.ExpandName when _properties == 2 && reader.TokenType is JsonTokenType.True or JsonTokenType.False:
                    Expand = reader.TokenType == JsonTokenType.True;
                    break;
                case FormPickerDeliveryMarker.ThemeName when _properties == 3 && Expand is not null && reader.TokenType is JsonTokenType.String or JsonTokenType.Null:
                    _theme = true;
                    Theme = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case FormPickerDeliveryMarker.RedirectName when _properties == (_theme ? 4 : 3) && Expand is not null && reader.TokenType is JsonTokenType.String or JsonTokenType.Null:
                    _redirect = true;
                    Redirect = reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var redirect) ? redirect : null;
                    break;
            }
        }

        // The name of the property whose value comes now, counted; null in an array.
        private string? Next()
        {
            if (!isObject || Pending is not { } name)
            {
                return null;
            }

            _properties++;
            Pending = null;
            return name;
        }
    }
}
