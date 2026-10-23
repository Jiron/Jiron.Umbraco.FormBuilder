using System.Globalization;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>Reads a problem's JSON path (<c>$.pages[0].title['de-CH']</c>) back into its steps: names and indexes.</summary>
internal static class JsonPathSteps
{
    private const char Backslash = (char)92;

    public static List<object> Parse(string path)
    {
        if (!path.StartsWith('$'))
        {
            throw new FormatException($"A path starts with $: {path}");
        }

        var steps = new List<object>();
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var start = ++i;
                while (i < path.Length && path[i] is not ('.' or '['))
                {
                    i++;
                }

                steps.Add(path[start..i]);
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                i += 2;
                var name = new StringBuilder();
                while (path[i] != '\'')
                {
                    if (path[i] != Backslash)
                    {
                        name.Append(path[i++]);
                    }
                    else if (path[i + 1] == 'u')
                    {
                        name.Append((char)int.Parse(path.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 6;
                    }
                    else
                    {
                        name.Append(path[i + 1]);
                        i += 2;
                    }
                }

                i += 2;
                steps.Add(name.ToString());
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                steps.Add(int.Parse(path.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture));
                i = end + 1;
            }
            else
            {
                throw new FormatException($"Not a path: {path}");
            }
        }

        return steps;
    }
}
