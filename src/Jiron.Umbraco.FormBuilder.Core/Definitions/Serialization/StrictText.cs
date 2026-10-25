using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The opening steps of every reader of a document from outside, the definition's and the export file's (ADR-0034,
/// ADR-0046), written once: text becomes UTF-8 within the reader's limit and a lone surrogate is refused rather than
/// becoming U+FFFD; bytes are within the limit and valid UTF-8 throughout, which the JSON reader checks only inside the
/// strings it reads.
/// </summary>
internal static class StrictText
{
    /// <summary>UTF-8 that throws for what it cannot encode or decode, and writes no byte order mark.</summary>
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The text as UTF-8 bytes, or null with the problem: a lone surrogate, or more bytes than the limit.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxBytes">The most bytes the reader takes.</param>
    /// <param name="tooLarge">The problem the reader reports for more bytes than its limit.</param>
    /// <param name="problem">The problem, when the text was refused.</param>
    public static byte[]? ToUtf8(string text, int maxBytes, DefinitionProblem tooLarge, out DefinitionProblem? problem)
    {
        try
        {
            if (Utf8.GetByteCount(text) > maxBytes)
            {
                problem = tooLarge;
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
    /// <param name="tooLarge">The problem the reader reports for more bytes than its limit.</param>
    public static DefinitionProblem? Check(ReadOnlySpan<byte> utf8, int maxBytes, DefinitionProblem tooLarge)
    {
        if (utf8.Length > maxBytes)
        {
            return tooLarge;
        }

        return System.Text.Unicode.Utf8.IsValid(utf8) ? null : new DefinitionProblem(DefinitionErrors.InvalidJson, JsonPaths.Root, "The bytes are not valid UTF-8.");
    }
}
