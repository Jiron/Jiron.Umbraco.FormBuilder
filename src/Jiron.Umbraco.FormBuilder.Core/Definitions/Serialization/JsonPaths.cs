using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>JSON paths for problems: <c>$.pages[0].title['de-CH']</c>, as System.Text.Json writes them.</summary>
internal static class JsonPaths
{
    public const string Root = "$";

    /// <summary>The path of a property: dotted when the name is an identifier, bracketed otherwise.</summary>
    public static string Property(string path, string name)
    {
        if (name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return path + "." + name;
        }

        var text = new StringBuilder(path).Append("['");
        foreach (var c in name)
        {
            text.Append(c switch
            {
                '\'' => "\\'",
                '\\' => "\\\\",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return text.Append("']").ToString();
    }

    /// <summary>The path of an array item.</summary>
    public static string Index(string path, int index) => path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The string of a JSON string value, or null when it holds a lone surrogate, which .NET cannot read.</summary>
    public static string? TryGetString(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The JSON type of a value, for messages.</summary>
    public static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null",
    };
}
