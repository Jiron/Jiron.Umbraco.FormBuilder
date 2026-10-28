using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Entries;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The digest of a submission (section 4.4, ADR-0088), which tells a retry of the same answers from other answers sent
/// with the same submission id: the SHA-256, in lowercase hex, of the request as the pipeline reads it, written
/// canonically with every name sorted ordinally and strings escaped as <see cref="CanonicalJson"/> escapes them, so
/// neither the order of a body's members nor the transport changes it. The submission id, the challenge and the
/// captcha's token are left out: a retry may carry a fresh challenge.
/// </summary>
internal static class SubmissionDigest
{
    /// <summary>The digest of a request.</summary>
    /// <param name="request">The request.</param>
    public static string Of(SubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = new StringBuilder();
        text.Append("{\"revision\":").Append(request.Revision.ToString(CultureInfo.InvariantCulture));
        text.Append(",\"culture\":");
        Nullable(text, request.Culture);
        text.Append(",\"content\":");
        Nullable(text, request.ContentKey?.ToString("D"));
        text.Append(",\"fields\":");
        Answers(text, request.Fields);
        text.Append(",\"rows\":{");
        var first = true;
        foreach (var (alias, rows) in request.Rows.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Name(text, alias, ref first);
            text.Append('[');
            for (var i = 0; i < rows.Count; i++)
            {
                text.Append(i == 0 ? string.Empty : ",");
                Answers(text, rows[i]);
            }

            text.Append(']');
        }

        text.Append("},\"query\":{");
        first = true;
        foreach (var (name, values) in request.Query.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Name(text, name, ref first);
            Texts(text, values);
        }

        text.Append("},\"context\":{");
        first = true;
        foreach (var (name, value) in request.Context.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Name(text, name, ref first);
            CanonicalJson.WriteString(text, value);
        }

        text.Append("}}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // Answers by alias, each its values and its parts by name.
    private static void Answers(StringBuilder text, IReadOnlyDictionary<string, PostedAnswer>? answers)
    {
        text.Append('{');
        var first = true;
        foreach (var (alias, answer) in (answers ?? new Dictionary<string, PostedAnswer>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Name(text, alias, ref first);
            text.Append("{\"values\":");
            Texts(text, answer?.Values ?? []);
            text.Append(",\"parts\":{");
            var firstPart = true;
            foreach (var (part, values) in (answer?.Parts ?? new Dictionary<string, IReadOnlyList<string?>>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Name(text, part, ref firstPart);
                Texts(text, values ?? []);
            }

            text.Append("}}");
        }

        text.Append('}');
    }

    private static void Texts(StringBuilder text, IEnumerable<string?> values)
    {
        text.Append('[');
        var first = true;
        foreach (var value in values)
        {
            text.Append(first ? string.Empty : ",");
            first = false;
            Nullable(text, value);
        }

        text.Append(']');
    }

    private static void Name(StringBuilder text, string name, ref bool first)
    {
        text.Append(first ? string.Empty : ",");
        first = false;
        CanonicalJson.WriteString(text, name);
        text.Append(':');
    }

    private static void Nullable(StringBuilder text, string? value)
    {
        if (value is null)
        {
            text.Append("null");
        }
        else
        {
            CanonicalJson.WriteString(text, value);
        }
    }
}
