using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Webhooks;

/// <summary>
/// A Slack or Teams message (ADR-0191): the default words, a test's first line, and the JSON each chat reads. The message is the
/// editor's text in the chat's own formatting; each answer in it is kept as text by the scope's guard (<see cref="WebhookChatText"/>).
/// </summary>
internal static class WebhookChat
{
    /// <summary>A Slack message without words of its own: the form's name in bold, then the answers.</summary>
    public const string SlackMessage = "*New entry in {{ form.name }}*\n{{ entry.answers }}";

    /// <summary>A Teams message without words of its own: the form's name in bold, then the answers.</summary>
    public const string TeamsMessage = "**New entry in {{ form.name }}**\n{{ entry.answers }}";

    /// <summary>A test message's first line, in the tester's backoffice language (English or German, as the test mail's).</summary>
    /// <param name="culture">The tester's backoffice language; null for English.</param>
    public static string TestLine(string? culture) =>
        string.Equals(culture?.Split('-')[0], "de", StringComparison.OrdinalIgnoreCase) ? "Dies ist ein Test." : "This is a test.";

    /// <summary>
    /// The body a chat reads: for Slack, its <c>text</c>; for Teams, an Adaptive Card with one wrapping text block per line, as
    /// a card shows a line break only between blocks, and a blank line as the space before the next.
    /// </summary>
    /// <param name="target">Slack or Teams.</param>
    /// <param name="message">The message, its merge tags rendered.</param>
    public static byte[] Body(WebhookTarget target, string message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (target == WebhookTarget.Slack)
            {
                writer.WriteString("text", message);
            }
            else
            {
                writer.WriteString("type", "message");
                writer.WriteStartArray("attachments");
                writer.WriteStartObject();
                writer.WriteString("contentType", "application/vnd.microsoft.card.adaptive");
                writer.WriteStartObject("content");
                writer.WriteString("type", "AdaptiveCard");
                writer.WriteString("version", "1.4");
                writer.WriteStartArray("body");
                var apart = false;
                foreach (var line in message.Split('\n').Select(line => line.TrimEnd('\r')))
                {
                    if (line.Length == 0)
                    {
                        apart = true;
                        continue;
                    }

                    writer.WriteStartObject();
                    writer.WriteString("type", "TextBlock");
                    writer.WriteString("text", line);
                    writer.WriteBoolean("wrap", true);
                    writer.WriteString("spacing", apart ? "Medium" : "None");
                    writer.WriteEndObject();
                    apart = false;
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
