using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The opening steps of every reader of a document from outside, the definition's and the export file's (ADR-0034,
/// ADR-0046), written once: text becomes UTF-8 within the reader's limit and a lone surrogate is refused rather than
/// becoming U+FFFD; bytes are within the limit and valid UTF-8 throughout, which the JSON reader checks only inside the
/// strings it reads; and a document that is only too deep is told from one that is not JSON.
/// </summary>
internal static class StrictText
{
    /// <summary>UTF-8 that throws for what it cannot encode or decode, and writes no byte order mark.</summary>
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The problem of a document larger than its reader's limit.</summary>
    /// <param name="document">What the document is, as the message begins: "A definition", "An export file".</param>
    /// <param name="maxBytes">The most bytes the reader takes.</param>
    public static DefinitionProblem TooLarge(string document, int maxBytes) =>
        new(DefinitionErrors.LimitExceeded, JsonPaths.Root, string.Create(CultureInfo.InvariantCulture, $"{document} has at most {maxBytes} bytes in UTF-8."));

    /// <summary>The text as UTF-8 bytes, or null with the problem: a lone surrogate, or more bytes than the limit.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxBytes">The most bytes the reader takes.</param>
    /// <param name="document">What the document is, for the problem of one that is too large.</param>
    /// <param name="problem">The problem, when the text was refused.</param>
    public static byte[]? ToUtf8(string text, int maxBytes, string document, out DefinitionProblem? problem)
    {
        try
        {
            if (Utf8.GetByteCount(text) > maxBytes)
            {
                problem = TooLarge(document, maxBytes);
                return null;
            }

            problem = null;
            return Utf8.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            problem = new DefinitionProblem(DefinitionErrors.InvalidJson, JsonPaths.Root, "The text holds a lone surrogate.");
            return null;
        }
    }

    /// <summary>The problem with the bytes, or null: more bytes than the limit, or bytes that are not valid UTF-8.</summary>
    /// <param name="utf8">The bytes.</param>
    /// <param name="maxBytes">The most bytes the reader takes.</param>
    /// <param name="document">What the document is, for the problem of one that is too large.</param>
    public static DefinitionProblem? Check(ReadOnlySpan<byte> utf8, int maxBytes, string document)
    {
        if (utf8.Length > maxBytes)
        {
            return TooLarge(document, maxBytes);
        }

        return System.Text.Unicode.Utf8.IsValid(utf8) ? null : new DefinitionProblem(DefinitionErrors.InvalidJson, JsonPaths.Root, "The bytes are not valid UTF-8.");
    }

    /// <summary>
    /// The bytes, checked already, as a JSON document read to <paramref name="maxDepth"/> levels; or null with the
    /// problem: <c>limit_exceeded</c> for a document that is JSON as far as it goes but nests deeper, otherwise
    /// <c>invalid_json</c>.
    /// </summary>
    public static JsonDocument? Parse(ReadOnlySpan<byte> utf8, int maxDepth, out DefinitionProblem? problem)
    {
        problem = null;
        try
        {
            return JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = maxDepth });
        }
        catch (JsonException exception)
        {
            var position = string.Create(CultureInfo.InvariantCulture, $"line {exception.LineNumber + 1}, byte {exception.BytePositionInLine + 1}");
            problem = NestsDeeperThan(utf8, maxDepth)
                ? new DefinitionProblem(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"The document nests deeper than {maxDepth} levels ({position}).")
                : new DefinitionProblem(DefinitionErrors.InvalidJson, JsonPaths.Root, $"The text is not JSON ({position}).");
            return null;
        }
    }

    /// <summary>
    /// Whether the text opens more than <paramref name="maxDepth"/> nested objects and arrays before it ends or stops
    /// being JSON: this tells a document that is only too deep from one that is not JSON, whatever its depth, without
    /// parsing it.
    /// </summary>
    public static bool NestsDeeperThan(ReadOnlySpan<byte> utf8, int maxDepth)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = int.MaxValue });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth >= maxDepth)
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON before it was too deep.
        }

        return false;
    }
}
