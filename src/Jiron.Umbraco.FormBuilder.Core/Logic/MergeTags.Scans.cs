using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>Where a tag may stand, read from the template's own text, never from a value (ADR-0064).</summary>
internal static partial class MergeTags
{
    /// <summary>The HTML elements whose content is not markup, where an escaped value is no safer than a raw one.</summary>
    private static readonly FrozenSet<string> RawElements = new[]
    {
        "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes", "noscript", "plaintext",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The elements after which a browser may read a raw element's content as markup: inside svg and math it is foreign
    /// content, and select and frameset may drop a raw element's start tag. Which one a browser does depends on the
    /// whole document, which the scan does not build.
    /// </summary>
    private static readonly FrozenSet<string> DoubtAfter =
        new[] { "svg", "math", "select", "frameset" }.ToFrozenSet(StringComparer.Ordinal);

    private static IScan ScanFor(MergeTagContext context) => context switch
    {
        MergeTagContext.Html => new MarkupScan(html: true),
        MergeTagContext.Xml => new MarkupScan(html: false),
        MergeTagContext.Json => new JsonScan(inString: false),
        MergeTagContext.JsonString => new JsonScan(inString: true),
        MergeTagContext.Url => new UrlScan(),
        MergeTagContext.Header => new AnywhereScan(TemplateWriter.Header),
        MergeTagContext.Form => new AnywhereScan(TemplateWriter.Form),
        _ => new AnywhereScan(TemplateWriter.Text),
    };

    /// <summary>A path, query or fragment starts: <c>/</c>, <c>\</c>, <c>?</c> or <c>#</c>.</summary>
    private static bool StartsPath(int c) => c is '/' or Backslash or '?' or '#';

    /// <summary>
    /// Reads the template's own text, never a value, to know how a tag there is written, or that none may stand there.
    /// A tag that writes a value leaves the scan where it was, since each encoder keeps its place; a tag written out as
    /// it stands is read like the rest of the template's text, because that is what the output then holds.
    /// </summary>
    private interface IScan
    {
        void Read(string text, int from, int to);

        /// <summary>How a tag here is written, or the problem that refuses it.</summary>
        (TemplateWriter Writer, string? Misplaced) Place();
    }

    /// <summary>A context without places to refuse.</summary>
    private sealed class AnywhereScan(TemplateWriter writer) : IScan
    {
        public void Read(string text, int from, int to)
        {
        }

        public (TemplateWriter Writer, string? Misplaced) Place() => (writer, null);
    }

    /// <summary>
    /// HTML and XML: a tag may stand only in text content. The scan reads the template as the HTML tokenizer does (XML's
    /// well-formed markup reads alike), and refuses a doubtful place: a <c>&lt;</c> before a letter, <c>/</c>, <c>!</c>
    /// or <c>?</c>, or right before a tag. Where it cannot know how a browser reads on, it refuses every tag that follows
    /// (<c>Endless</c>).
    /// </summary>
    private sealed class MarkupScan(bool html) : IScan
    {
        private const string Dashes = "--";
        private const string CdataStart = "[CDATA[";

        /// <summary>In a script, the start of an escape that the script's end tag may not end.</summary>
        private const string ScriptEscape = "<!--";

        /// <summary>In an HTML comment after <c>--!</c>, where a <c>&gt;</c> ends it.</summary>
        private const int DashesAndBang = -1;

        /// <summary>The most of an element's name kept: more than any raw element's, so a longer name is none of them.</summary>
        private const int NameKept = 10;

        private MarkupState _state = MarkupState.Text;

        /// <summary>In a tag: where in it the scan is.</summary>
        private TagPart _part = TagPart.Name;

        /// <summary>In a quoted attribute value or an XML declaration's literal: its quote, 0 outside a literal.</summary>
        private int _quote;

        /// <summary>In a tag: the start of its element's name, in small letters.</summary>
        private string _name = string.Empty;

        private bool _closing;

        /// <summary>After <c>&lt;!</c>: what follows, until it is a comment, a CDATA section or a declaration.</summary>
        private string _bang = string.Empty;

        /// <summary>
        /// Dashes or brackets seen (<see cref="DashesAndBang"/> after <c>--!</c> in an HTML comment), a question mark
        /// seen, or in a raw element the characters of its end tag matched.
        /// </summary>
        private int _run;

        /// <summary>In a raw element: its name.</summary>
        private string _raw = string.Empty;

        /// <summary>In a script: the characters of <see cref="ScriptEscape"/> matched.</summary>
        private int _escape;

        /// <summary>Whether an element of <see cref="DoubtAfter"/> has started.</summary>
        private bool _doubt;

        /// <summary>In a raw element: whether a browser may read its content as markup.</summary>
        private bool _doubtful;

        private enum MarkupState
        {
            Text,
            Open,
            EndOpen,
            Bang,
            InTag,
            CommentStart,
            Comment,
            Cdata,
            Declaration,
            Doctype,
            Instruction,
            Raw,
            RawEnd,
            Endless,
        }

        /// <summary>
        /// Where in a tag the scan is, as the HTML tokenizer reads a tag: a quote opens a value only after <c>=</c>. The
        /// scan need not know where an attribute's name ends or the next one starts, nor whether the tag closes itself,
        /// since neither changes where the tag ends: so a <c>/</c>, and the end of a quoted value, leave it before an
        /// attribute.
        /// </summary>
        private enum TagPart
        {
            Name,
            BeforeAttribute,
            Attribute,
            BeforeValue,
            Quoted,
            Unquoted,
        }

        public void Read(string text, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                Step(text[i]);
            }
        }

        public (TemplateWriter Writer, string? Misplaced) Place() =>
            _state != MarkupState.Text
                ? (TemplateWriter.Text, MergeTagProblems.TagInMarkup)
                : (html ? TemplateWriter.Html : TemplateWriter.Xml, null);

        /// <summary>The spaces of the HTML tokenizer, carriage return included (the parser reads it as a line feed).</summary>
        private static bool IsSpace(int c) => c is 9 or 10 or 12 or 13 or ' ';

        private void StartTag(bool closing, int first)
        {
            _state = MarkupState.InTag;
            _part = TagPart.Name;
            _closing = closing;
            _name = first < 0 ? string.Empty : new string((char)Small(first), 1);
        }

        private void EndTag()
        {
            if (html && !_closing)
            {
                if (DoubtAfter.Contains(_name))
                {
                    _doubt = true;
                }

                if (RawElements.Contains(_name))
                {
                    // Nothing ends a plaintext element; noscript is raw only while scripts run.
                    _state = _name == "plaintext" ? MarkupState.Endless : MarkupState.Raw;
                    _raw = _name;
                    _doubtful = _doubt || _name == "noscript";
                    _run = 0;
                    return;
                }
            }

            _state = MarkupState.Text;
        }

        private void Step(int c)
        {
            switch (_state)
            {
                case MarkupState.Text:
                    if (c == '<')
                    {
                        _state = MarkupState.Open;
                    }

                    break;
                case MarkupState.Open:
                    if (c == '!')
                    {
                        _state = MarkupState.Bang;
                        _bang = string.Empty;
                    }
                    else if (c == '/')
                    {
                        _state = MarkupState.EndOpen;
                    }
                    else if (c == '?')
                    {
                        _state = html ? MarkupState.Declaration : MarkupState.Instruction;
                        _run = 0;
                    }
                    else if (IsLetter(c) || !html)
                    {
                        // In XML any other '<' opens a tag; in HTML only one before a letter does.
                        StartTag(closing: false, c);
                    }
                    else
                    {
                        _state = MarkupState.Text;
                        Step(c);
                    }

                    break;
                case MarkupState.EndOpen:
                    // A letter opens an end tag, "</>" is nothing, and anything else opens a comment that ends at '>'.
                    if (IsLetter(c))
                    {
                        StartTag(closing: true, c);
                    }
                    else
                    {
                        _state = c == '>' ? MarkupState.Text : MarkupState.Declaration;
                    }

                    break;
                case MarkupState.Bang:
                    AfterBang(c);
                    break;
                case MarkupState.InTag:
                    InTag(c);
                    break;
                case MarkupState.CommentStart:
                    // The HTML tokenizer ends a comment at a '>' right after "<!--" or "<!---".
                    if (c == '>')
                    {
                        _state = MarkupState.Text;
                    }
                    else if (c == '-' && _run == 0)
                    {
                        _run = 1;
                    }
                    else
                    {
                        _state = MarkupState.Comment;
                        _run = c == '-' ? 2 : 0;
                    }

                    break;
                case MarkupState.Comment:
                case MarkupState.Cdata:
                    if (c == (_state == MarkupState.Comment ? '-' : ']'))
                    {
                        _run = _run < 0 ? 1 : _run + 1;
                    }
                    else if (c == '>' && (_run >= 2 || _run == DashesAndBang))
                    {
                        _state = MarkupState.Text;
                    }
                    else
                    {
                        // An HTML comment ends at "--!>" too.
                        _run = html && c == '!' && _run >= 2 ? DashesAndBang : 0;
                    }

                    break;
                case MarkupState.Declaration:
                    if (c == '>')
                    {
                        _state = MarkupState.Text;
                    }

                    break;
                case MarkupState.Doctype:
                    // XML's: its literals may hold a '>', and its internal subset markup declarations the scan does not read.
                    if (_quote != 0)
                    {
                        if (c == _quote)
                        {
                            _quote = 0;
                        }
                    }
                    else if (c is '"' or (char)39)
                    {
                        _quote = c;
                    }
                    else if (c == '[')
                    {
                        _state = MarkupState.Endless;
                    }
                    else if (c == '>')
                    {
                        _state = MarkupState.Text;
                    }

                    break;
                case MarkupState.Instruction:
                    if (c == '>' && _run == 1)
                    {
                        _state = MarkupState.Text;
                    }
                    else
                    {
                        _run = c == '?' ? 1 : 0;
                    }

                    break;
                case MarkupState.Raw:
                    InRaw(c);
                    break;
                case MarkupState.RawEnd:
                    // The end tag's name is complete if a space, '/' or '>' follows.
                    if (IsSpace(c) || c == '/')
                    {
                        StartTag(closing: true, -1);
                        _part = TagPart.BeforeAttribute;
                    }
                    else if (c == '>')
                    {
                        _state = MarkupState.Text;
                    }
                    else if (_doubtful)
                    {
                        _state = MarkupState.Endless;
                    }
                    else
                    {
                        _state = MarkupState.Raw;
                        _run = 0;
                        InRaw(c);
                    }

                    break;
                default:
                    // Endless stays.
                    break;
            }
        }

        private void AfterBang(int c)
        {
            _bang += (char)c;
            if (Dashes.StartsWith(_bang, StringComparison.Ordinal))
            {
                if (_bang.Length == Dashes.Length)
                {
                    _state = html ? MarkupState.CommentStart : MarkupState.Comment;
                    _run = 0;
                }
            }
            else if ((!html || _doubt) && CdataStart.StartsWith(_bang, StringComparison.Ordinal))
            {
                // XML's CDATA section; in HTML one after svg or math, which a browser ends at "]]>" or at the first '>'.
                if (_bang.Length == CdataStart.Length)
                {
                    _state = html ? MarkupState.Endless : MarkupState.Cdata;
                    _run = 0;
                }
            }
            else if (html)
            {
                // A comment that ends at the first '>', as a document type declaration does.
                _state = c == '>' ? MarkupState.Text : MarkupState.Declaration;
            }
            else
            {
                _state = MarkupState.Doctype;
                _quote = 0;
                Step(c);
            }
        }

        /// <summary>
        /// A tag, read as the HTML tokenizer reads one, so the scan leaves it where a browser does: a quote in a name or
        /// an unquoted value is a character like any other.
        /// </summary>
        private void InTag(int c)
        {
            switch (_part)
            {
                case TagPart.Name:
                    if (IsSpace(c) || c == '/')
                    {
                        _part = TagPart.BeforeAttribute;
                    }
                    else if (c == '>')
                    {
                        EndTag();
                    }
                    else if (_name.Length < NameKept)
                    {
                        _name += (char)Small(c);
                    }

                    break;
                case TagPart.BeforeAttribute:
                    if (c == '>')
                    {
                        EndTag();
                    }
                    else if (!IsSpace(c) && c != '/')
                    {
                        _part = TagPart.Attribute;
                    }

                    break;
                case TagPart.Attribute:
                    if (c == '/')
                    {
                        _part = TagPart.BeforeAttribute;
                    }
                    else if (c == '=')
                    {
                        _part = TagPart.BeforeValue;
                    }
                    else if (c == '>')
                    {
                        EndTag();
                    }

                    break;
                case TagPart.BeforeValue:
                    if (c is '"' or (char)39)
                    {
                        _part = TagPart.Quoted;
                        _quote = c;
                    }
                    else if (c == '>')
                    {
                        EndTag();
                    }
                    else if (!IsSpace(c))
                    {
                        _part = TagPart.Unquoted;
                    }

                    break;
                case TagPart.Quoted:
                    if (c == _quote)
                    {
                        _part = TagPart.BeforeAttribute;
                    }

                    break;
                default:
                    // Unquoted
                    if (IsSpace(c))
                    {
                        _part = TagPart.BeforeAttribute;
                    }
                    else if (c == '>')
                    {
                        EndTag();
                    }

                    break;
            }
        }

        /// <summary>
        /// A raw element's content, which ends at <c>&lt;/</c>, the element's name without regard to case, and a space,
        /// <c>/</c> or <c>&gt;</c>. In a script, <c>&lt;!--</c> may start an escape that the end tag does not end, so
        /// nothing ends the script after it. Where a browser may read the content as markup instead, it may hold no
        /// other <c>&lt;</c>, so that both readings end alike.
        /// </summary>
        private void InRaw(int c)
        {
            if (_raw == "script")
            {
                _escape = c == ScriptEscape[_escape] ? _escape + 1 : c == '<' ? 1 : 0;
                if (_escape == ScriptEscape.Length)
                {
                    _state = MarkupState.Endless;
                    return;
                }
            }

            var end = "</" + _raw;
            if (Small(c) == end[_run])
            {
                _run++;
                if (_run == end.Length)
                {
                    _state = MarkupState.RawEnd;
                }
            }
            else if (_run > 0 && _doubtful)
            {
                _state = MarkupState.Endless;
            }
            else
            {
                // A mismatch starts again, at 1 if it is another '<'.
                _run = c == '<' ? 1 : 0;
            }
        }
    }

    /// <summary>JSON: inside a string literal a value is string content, outside it a complete JSON value.</summary>
    private sealed class JsonScan(bool inString) : IScan
    {
        private JsonState _state = inString ? JsonState.InString : JsonState.Outside;

        private enum JsonState
        {
            Outside,
            InString,
            Escaped,
        }

        public void Read(string text, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                var c = text[i];
                if (_state == JsonState.Outside)
                {
                    if (c == '"')
                    {
                        _state = JsonState.InString;
                    }
                }
                else if (_state == JsonState.InString)
                {
                    if (c == Backslash)
                    {
                        _state = JsonState.Escaped;
                    }
                    else if (c == '"')
                    {
                        _state = JsonState.Outside;
                    }
                }
                else
                {
                    _state = JsonState.InString;
                }
            }
        }

        public (TemplateWriter Writer, string? Misplaced) Place() => _state switch
        {
            JsonState.Escaped => (TemplateWriter.JsonString, MergeTagProblems.TagInEscape),
            JsonState.InString => (TemplateWriter.JsonString, null),
            _ => (TemplateWriter.JsonValue, null),
        };

    }

    /// <summary>
    /// An address: a tag may stand only after its path starts, so no value chooses its scheme or host. It reads the
    /// address as browsers do: tabs and line breaks dropped, leading spaces and controls skipped, <c>\</c> as <c>/</c>.
    /// </summary>
    private sealed class UrlScan : IScan
    {
        /// <summary>Only "http" and "https" are allowed, so a longer scheme need not be kept.</summary>
        private const int LongestScheme = 6;

        private UrlState _state = UrlState.Lead;
        private string _scheme = string.Empty;

        private enum UrlState
        {
            Lead,
            Scheme,
            Colon,
            Slashes,
            Slash,
            Authority,
            Relative,
            Path,
            Never,
        }

        public void Read(string text, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                var c = text[i];
                if (c is not ((char)9 or (char)10 or (char)13))
                {
                    Step(c);
                }
            }
        }

        public (TemplateWriter Writer, string? Misplaced) Place() =>
            _state == UrlState.Path ? (TemplateWriter.Url, null) : (TemplateWriter.Url, MergeTagProblems.TagBeforePath);

        private static bool IsSlash(int c) => c is '/' or Backslash;

        private void Step(int c)
        {
            switch (_state)
            {
                case UrlState.Lead:
                    if (c <= ' ')
                    {
                        return;
                    }

                    if (IsSlash(c))
                    {
                        _state = UrlState.Slash;
                    }
                    else if (c is '?' or '#')
                    {
                        _state = UrlState.Path;
                    }
                    else if (IsLetter(c))
                    {
                        _state = UrlState.Scheme;
                        _scheme = new string((char)Small(c), 1);
                    }
                    else
                    {
                        _state = UrlState.Relative;
                    }

                    break;
                case UrlState.Scheme:
                    if (IsLetter(c) || IsDigit(c) || c is '+' or '-' or '.')
                    {
                        if (_scheme.Length < LongestScheme)
                        {
                            _scheme += (char)Small(c);
                        }
                    }
                    else if (c == ':')
                    {
                        _state = _scheme is "http" or "https" ? UrlState.Colon : UrlState.Never;
                    }
                    else
                    {
                        _state = StartsPath(c) ? UrlState.Path : UrlState.Relative;
                    }

                    break;
                case UrlState.Colon:
                    _state = IsSlash(c) ? UrlState.Slashes : UrlState.Never;
                    break;
                case UrlState.Slashes:
                    if (!IsSlash(c))
                    {
                        _state = c is '?' or '#' ? UrlState.Path : UrlState.Authority;
                    }

                    break;
                case UrlState.Slash:
                    _state = IsSlash(c) ? UrlState.Slashes : UrlState.Path;
                    break;
                case UrlState.Authority:
                case UrlState.Relative:
                    if (StartsPath(c))
                    {
                        _state = UrlState.Path;
                    }

                    break;
                default:
                    // Path and Never stay.
                    break;
            }
        }
    }
}
