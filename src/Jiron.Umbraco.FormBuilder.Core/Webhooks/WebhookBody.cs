using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.Webhooks;

/// <summary>
/// Writes a webhook's body (ROADMAP 8.4, ADR-0161): a template in its format's own language, or the standard metadata with the fields.
/// Each value is a merge tag's text for an action: a sensitive field only where the action names it, a field the rules hid never.
/// </summary>
/// <param name="tags">The merge-tag service.</param>
internal sealed class WebhookBody(IFormBuilderMergeTagService tags)
{
    /// <summary>The body as it is sent and signed; null when a text would be cut at the merge tags' limit, as a body cut short is no body.</summary>
    /// <param name="action">The action's settings.</param>
    /// <param name="scope">The entry and what the action may show of it.</param>
    /// <param name="trigger">What happened.</param>
    /// <param name="delivery">The run's delivery key.</param>
    /// <param name="testLine">A test's first line for a chat's message; null for a run.</param>
    /// <param name="cancellationToken">Cancels the rendering.</param>
    public async Task<byte[]?> WriteAsync(WebhookActionSettings action, MergeTagScope scope, string trigger, string delivery, string? testLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(scope);
        if (action.Template is { } template)
        {
            var context = action.Format switch
            {
                WebhookFormat.Form => MergeTagContext.Form,
                WebhookFormat.Xml => MergeTagContext.Xml,
                _ => MergeTagContext.Json,
            };
            return await RenderAsync(template, context, scope, cancellationToken) is { } text ? Encoding.UTF8.GetBytes(text) : null;
        }

        // A chat's message, written as the chat reads it (ADR-0191); the scope keeps each answer in it as text.
        if (action.Target != WebhookTarget.Url)
        {
            var message = action.Message ?? (action.Target == WebhookTarget.Slack ? WebhookChat.SlackMessage : WebhookChat.TeamsMessage);
            return await RenderAsync(message, MergeTagContext.Text, scope, cancellationToken) is { } text
                ? WebhookChat.Body(action.Target, testLine is null ? text : testLine + "\n\n" + text)
                : null;
        }

        if (await FieldsAsync(action, scope, cancellationToken) is not { } fields)
        {
            return null;
        }

        // Without answers the metadata is all that says what happened, so it goes whatever its switch says (ADR-0191).
        var withMetadata = action.Metadata || action.WithoutAnswers;
        var metadata = withMetadata ? Metadata(scope, trigger, delivery) : [];
        return action.Format switch
        {
            WebhookFormat.Form => FormOf(metadata, fields, withMetadata),
            WebhookFormat.Xml => XmlOf(metadata, fields),
            _ => JsonOf(metadata, fields, withMetadata),
        };
    }

    private static List<KeyValuePair<string, string>> Metadata(MergeTagScope scope, string trigger, string delivery) =>
    [
        new("event", JsonNamingPolicy.CamelCase.ConvertName(trigger)),
        new("delivery", delivery),
        new("form.alias", scope.FormAlias),
        new("form.name", scope.FormName),
        new("entry.key", (scope.Entry?.Key ?? Guid.Empty).ToString("D")),
        new("entry.number", (scope.Entry?.Number ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("entry.state", scope.Entry?.State ?? string.Empty),
        new("entry.submitted", (scope.Entry?.Submitted ?? default).ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        new("entry.culture", scope.Culture),
    ];

    private static byte[] JsonOf(List<KeyValuePair<string, string>> metadata, List<KeyValuePair<string, string>> fields, bool withMetadata)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (withMetadata)
            {
                foreach (var group in metadata.GroupBy(item => item.Key.Split('.')[0]))
                {
                    if (group.Count() == 1 && !group.First().Key.Contains('.', StringComparison.Ordinal))
                    {
                        writer.WriteString(group.Key, group.First().Value);
                        continue;
                    }

                    writer.WriteStartObject(group.Key);
                    foreach (var item in group)
                    {
                        writer.WriteString(item.Key.Split('.')[1], item.Value);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteStartObject("fields");
            }

            foreach (var (name, value) in fields)
            {
                writer.WriteString(name, value);
            }

            if (withMetadata)
            {
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] FormOf(List<KeyValuePair<string, string>> metadata, List<KeyValuePair<string, string>> fields, bool withMetadata)
    {
        var named = metadata.Concat(fields.Select(field => new KeyValuePair<string, string>(withMetadata ? "fields." + field.Key : field.Key, field.Value)));
        return Encoding.UTF8.GetBytes(string.Join('&', named.Select(item => $"{WebUtility.UrlEncode(item.Key)}={WebUtility.UrlEncode(item.Value)}")));
    }

    private static byte[] XmlOf(List<KeyValuePair<string, string>> metadata, List<KeyValuePair<string, string>> fields)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false, CheckCharacters = false }))
        {
            writer.WriteStartElement("submission");
            foreach (var (name, value) in metadata)
            {
                writer.WriteStartElement("meta");
                writer.WriteAttributeString("name", name);
                writer.WriteString(XmlSafe(value));
                writer.WriteEndElement();
            }

            foreach (var (name, value) in fields)
            {
                writer.WriteStartElement("field");
                writer.WriteAttributeString("name", name);
                writer.WriteString(XmlSafe(value));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        return stream.ToArray();
    }

    // What XML can hold of a text: what it cannot is left out, and a pair of surrogates is one character it can.
    private static string XmlSafe(string text)
    {
        var kept = new StringBuilder(text.Length);
        for (var at = 0; at < text.Length; at++)
        {
            if (XmlConvert.IsXmlChar(text[at]))
            {
                kept.Append(text[at]);
            }
            else if (at + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[at + 1], text[at]))
            {
                kept.Append(text, at, 2);
                at++;
            }
        }

        return kept.ToString();
    }

    private async Task<string?> RenderAsync(string template, MergeTagContext context, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var rendered = await tags.RenderAsync(template, MergeTagPlace.Action, context, scope, cancellationToken);
        return rendered.Problems.Any(problem => problem.Code == MergeTagProblems.TooLong) ? null : rendered.Text;
    }

    // The mapping's templates, or every field with an answer it may send, by its alias; null when one is too long.
    private async Task<List<KeyValuePair<string, string>>?> FieldsAsync(WebhookActionSettings action, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (action.WithoutAnswers)
        {
            return fields;
        }

        if (WebhookMaps.Read(action.Fields, WebhookMaps.IsFieldName) is { Count: > 0 } mapping)
        {
            foreach (var (name, template) in mapping)
            {
                if (await RenderAsync(template, MergeTagContext.Text, scope, cancellationToken) is not { } text)
                {
                    return null;
                }

                fields.Add(new(name, text));
            }

            return fields;
        }

        foreach (var field in scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            // An unnamed sensitive field goes as no field at all: its mask would say that it was answered.
            if (field.Alias is { Length: > 0 } alias && (!field.Sensitive || scope.IncludedSensitive.Contains(field.Key)))
            {
                if (await RenderAsync($"{{{{ field.{alias} }}}}", MergeTagContext.Text, scope, cancellationToken) is not { } text)
                {
                    return null;
                }

                if (text.Length > 0)
                {
                    fields.Add(new(alias, text));
                }
            }
        }

        return fields;
    }
}
