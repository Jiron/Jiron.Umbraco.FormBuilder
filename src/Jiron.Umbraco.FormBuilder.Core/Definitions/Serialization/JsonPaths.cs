using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>JSON paths for problems: <c>$.pages[0].title['de-CH']</c>, as System.Text.Json writes them.</summary>
internal static class JsonPaths
{
    public const string Root = "$";

    /// <summary>The path of a property: dotted when the name is an identifier, bracketed otherwise.</summary>
    public static string Property(string path, string name) => AppendProperty(new StringBuilder(path), name).ToString();

    /// <summary>The path of an array item.</summary>
    public static string Index(string path, int index) => path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>The name of a property, or null when it holds a lone surrogate, which .NET cannot read.</summary>
    public static string? TryGetName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

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

    private static StringBuilder AppendProperty(StringBuilder text, string name)
    {
        if (name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return text.Append('.').Append(name);
        }

        text.Append("['");
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

        return text.Append("']");
    }

    /// <summary>
    /// A place in a document that becomes a path only when a problem names it. Walking a document with these costs one
    /// small object per value; building each value's path as text would copy its parent's path every time, which a
    /// long property name over a long array makes quadratic.
    /// </summary>
    internal sealed class Location
    {
        private readonly Location? _parent;
        private readonly string? _name;
        private readonly int _index;

        private Location(Location? parent, string? name, int index)
        {
            _parent = parent;
            _name = name;
            _index = index;
        }

        /// <summary>The place a path already names.</summary>
        public static Location At(string path) => new(null, path, -1);

        /// <summary>A property of the value here.</summary>
        public Location Property(string name) => new(this, name, -1);

        /// <summary>An item of the array here.</summary>
        public Location Item(int index) => new(this, null, index);

        /// <summary>The path, such as <c>$.pages[0].title['de-CH']</c>.</summary>
        public override string ToString()
        {
            var steps = new Stack<Location>();
            var location = this;
            for (; location._parent is not null; location = location._parent)
            {
                steps.Push(location);
            }

            var text = new StringBuilder(location._name);
            foreach (var step in steps)
            {
                if (step._name is null)
                {
                    text.Append('[').Append(step._index.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
                else
                {
                    AppendProperty(text, step._name);
                }
            }

            return text.ToString();
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
