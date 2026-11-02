using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// The text version of a mail (ADR-0007, ADR-0159): what a reader would read of its rich text, in reading order. Blocks are
/// separated by a blank line, list items and table rows stand on lines of their own, and a link's address follows its words.
/// </summary>
internal static class EmailText
{
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "ul", "ol", "table", "div", "hr", "caption",
    };

    /// <summary>The text of these rich texts, in order, each only when it says something.</summary>
    public static string Of(HtmlParser parser, params string[] parts)
    {
        var said = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length > 0 && TextOf(parser.ParseDocument($"<!DOCTYPE html><body>{part}").Body!) is { Length: > 0 } text)
            {
                said.Add(text);
            }
        }

        return string.Join("\n\n", said);
    }

    private static string TextOf(IElement root)
    {
        var writer = new Writer();
        writer.Children(root);
        return writer.ToString();
    }

    // Writes nodes into lines, keeping runs of white space from becoming more than one space, as a browser would.
    private sealed class Writer
    {
        private readonly StringBuilder _text = new();
        private bool _pendingSpace;
        private bool _preformatted;

        public void Children(INode parent)
        {
            foreach (var child in parent.ChildNodes)
            {
                Node(child);
            }
        }

        public override string ToString() => _text.ToString().Trim();

        private void Node(INode node)
        {
            if (node is IText text)
            {
                Words(text.Data);
            }
            else if (node is IElement element)
            {
                Element(element);
            }
        }

        private void Element(IElement element)
        {
            switch (element.LocalName)
            {
                case "br":
                    Line();
                    return;
                case "hr":
                    Blank();
                    _text.Append("---");
                    Blank();
                    return;
                case "li":
                    Line();
                    _text.Append(element.ParentElement is { LocalName: "ol" } list ? $"{Array.IndexOf([.. list.Children], element) + 1}. " : "- ");
                    Children(element);
                    Line();
                    return;
                case "tr":
                    Line();
                    Row(element);
                    Line();
                    return;
                case "a":
                    Link(element);
                    return;
                case "pre":
                    Blank();
                    _preformatted = true;
                    Children(element);
                    _preformatted = false;
                    Blank();
                    return;
            }

            var block = Blocks.Contains(element.LocalName);
            if (block)
            {
                Blank();
            }

            Children(element);
            if (block)
            {
                Blank();
            }
        }

        private void Row(IElement row)
        {
            var first = true;
            foreach (var cell in row.Children)
            {
                if (!first)
                {
                    _text.Append(": ");
                    _pendingSpace = false;
                }

                Children(cell);
                first = false;
            }
        }

        private void Link(IElement link)
        {
            Children(link);
            var address = link.GetAttribute("href");
            if (!string.IsNullOrEmpty(address)
                && address.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(address.TrimEnd('/'), link.TextContent.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                Words($" ({address})");
            }
        }

        private void Words(string data)
        {
            if (_preformatted)
            {
                _text.Append(data);
                return;
            }

            foreach (var c in data)
            {
                if (char.IsWhiteSpace(c) && c != (char)0x00a0)
                {
                    _pendingSpace = true;
                    continue;
                }

                if (_pendingSpace && _text.Length > 0 && _text[^1] != '\n')
                {
                    _text.Append(' ');
                }

                _pendingSpace = false;
                _text.Append(c);
            }
        }

        private void Line()
        {
            _pendingSpace = false;
            if (_text.Length > 0 && _text[^1] != '\n')
            {
                _text.Append('\n');
            }
        }

        private void Blank()
        {
            Line();
            if (_text.Length > 1 && _text[^2] != '\n')
            {
                _text.Append('\n');
            }
        }
    }
}
