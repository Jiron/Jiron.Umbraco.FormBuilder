using System.Text;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>The places a template renders in, each with its own encoder (ADR-0014, ADR-0064).</summary>
internal enum TemplateContext
{
    /// <summary>HTML: values escaped, tags only in text content.</summary>
    Html,

    /// <summary>Plain text: values as they are.</summary>
    Text,

    /// <summary>A header's value: line breaks and controls as spaces, trimmed.</summary>
    Header,

    /// <summary>An address: values percent-encoded, tags only after the path starts.</summary>
    Url,

    /// <summary>A JSON document: string content inside a string literal, a complete JSON value outside one.</summary>
    Json,

    /// <summary>The content of one JSON string.</summary>
    JsonString,

    /// <summary>A form body: values form-encoded.</summary>
    Form,

    /// <summary>XML: values escaped, tags only in text content.</summary>
    Xml,
}

/// <summary>How a tag's value is written where it stands: with a context's encoder, or as a complete JSON value.</summary>
internal enum TemplateWriter
{
    Html,
    Xml,
    Text,
    Header,
    Url,
    Form,
    JsonString,
    JsonValue,
}

/// <summary>A value's item: text, or text with the wire number or the wire date behind it (ADR-0064).</summary>
internal sealed record MergeItem(string Text, string? Number = null, string? Date = null);

/// <summary>What a source gives for a path: one item or a list of them.</summary>
internal sealed record MergeValue(IReadOnlyList<MergeItem> Items, bool IsList)
{
    /// <summary>One item of text.</summary>
    public static MergeValue Of(string text) => new([new MergeItem(text)], false);

    /// <summary>One item.</summary>
    public static MergeValue Of(MergeItem item) => new([item], false);

    /// <summary>A list.</summary>
    public static MergeValue ListOf(IReadOnlyList<MergeItem> items) => new(items, true);
}

/// <summary>A problem of a template and the UTF-16 index where it starts (ADR-0064).</summary>
internal readonly record struct TemplateProblem(string Code, int At);

/// <summary>The problems of a template, the same in TypeScript; the warnings are those after which a tag renders empty.</summary>
internal static class TemplateProblems
{
    /// <summary>The tag does not follow the grammar.</summary>
    public const string MalformedTag = "malformed_tag";

    /// <summary>No <c>}}</c> follows a <c>{{</c>.</summary>
    public const string UnclosedTag = "unclosed_tag";

    /// <summary>In HTML or XML, the tag is not in text content.</summary>
    public const string TagInMarkup = "tag_in_markup";

    /// <summary>In JSON, the tag follows a backslash inside a string literal.</summary>
    public const string TagInEscape = "tag_in_escape";

    /// <summary>In an address, the tag comes before the path.</summary>
    public const string TagBeforePath = "tag_before_path";

    /// <summary>A filter's arguments do not fit it.</summary>
    public const string InvalidArgument = "invalid_argument";

    /// <summary><c>html</c> outside the HTML context, or <c>json</c> inside a JSON string.</summary>
    public const string FilterNotForContext = "filter_not_for_context";

    /// <summary>A filter follows <c>html</c>, <c>url</c> or <c>json</c>.</summary>
    public const string FilterAfterEncoding = "filter_after_encoding";

    /// <summary>More than <see cref="MergeTags.MaxTags"/> tags.</summary>
    public const string TooManyTags = "too_many_tags";

    /// <summary>A tag with more than <see cref="MergeTags.MaxFilters"/> filters.</summary>
    public const string TooManyFilters = "too_many_filters";

    /// <summary>The output would pass <see cref="MergeTags.MaxOutput"/> code units.</summary>
    public const string TooLong = "too_long";

    /// <summary>No source knows the path (a warning).</summary>
    public const string UnknownPath = "unknown_path";

    /// <summary>No filter has the name (a warning).</summary>
    public const string UnknownFilter = "unknown_filter";

    /// <summary>Whether a tag with this problem renders empty, rather than as it is written.</summary>
    public static bool IsWarning(string code) => code is UnknownPath or UnknownFilter;
}

/// <summary>A rendered template: its text, and its problems in the order of the template.</summary>
internal sealed record RenderedTemplate(string Text, IReadOnlyList<TemplateProblem> Problems);

/// <summary>A filter's argument: a string's text, or a number as written.</summary>
internal sealed record TemplateArgument(bool IsString, string Text, int At);

/// <summary>A filter of a tag, with its arguments.</summary>
internal sealed record TemplateFilter(string Name, int At, IReadOnlyList<TemplateArgument> Arguments);

/// <summary>A part of a parsed template.</summary>
internal abstract record TemplatePart(int At);

/// <summary>The template's own text.</summary>
internal sealed record TextPart(int At, string Text) : TemplatePart(At);

/// <summary>A tag with an error of its own, which renders as it is written.</summary>
internal sealed record WrittenPart(int At, string Text, TemplateProblem Problem) : TemplatePart(At);

/// <summary>A well-formed tag in its place: a path or a string, its filters, how it is written, and its filters' problems.</summary>
internal sealed record TagPart(
    int At,
    string Text,
    string? Path,
    int PathAt,
    string? Literal,
    IReadOnlyList<TemplateFilter> Filters,
    TemplateWriter Writer,
    IReadOnlyList<TemplateProblem> Problems) : TemplatePart(At);

/// <summary>A limit: the output ends here.</summary>
internal sealed record StopPart(int At, TemplateProblem Problem) : TemplatePart(At);

/// <summary>A template parsed for its context, with the problems that do not depend on values.</summary>
internal sealed record ParsedTemplate(TemplateContext Context, IReadOnlyList<TemplatePart> Parts, IReadOnlyList<TemplateProblem> Problems);

/// <summary>
/// Merge tags (section 4.3.3, ADR-0014, ADR-0064), the same in TypeScript (<c>packages/logic/src/template.ts</c>): a
/// template's text with <c>{{ source.path | filter: arg }}</c> tags, parsed once for the context it renders in, then
/// rendered over the values its sources give. Nothing in a template runs as code, and neither parsing nor rendering
/// throws, whatever the template, the values or the culture data.
/// </summary>
internal static partial class MergeTags
{
    /// <summary>The most tags a template has.</summary>
    public const int MaxTags = 200;

    /// <summary>The most filters a tag has.</summary>
    public const int MaxFilters = 10;

    /// <summary>The longest output, in UTF-16 code units.</summary>
    public const int MaxOutput = 100_000;

    /// <summary>
    /// Parses a template for the context it renders in (ADR-0064): its text, each tag's path or string and filters, how
    /// each tag is written where it stands, and every problem that does not depend on values. Never throws for any
    /// template.
    /// </summary>
    public static ParsedTemplate Parse(string text, TemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        var scan = ScanFor(context);
        var parts = new List<TemplatePart>();
        var position = 0;
        var tags = 0;
        while (position < text.Length)
        {
            var start = text.IndexOf("{{", position, StringComparison.Ordinal);
            var until = start < 0 ? text.Length : start;
            if (until > position)
            {
                scan.Read(text, position, until);
                parts.Add(new TextPart(position, text[position..until]));
            }

            if (start < 0)
            {
                break;
            }

            if (++tags > MaxTags)
            {
                parts.Add(new StopPart(start, new TemplateProblem(TemplateProblems.TooManyTags, start)));
                break;
            }

            var close = ClosingOf(text, start);
            if (close < 0)
            {
                Written(start, text.Length, TemplateProblems.UnclosedTag, start);
                break;
            }

            position = close + 2;
            var (writer, misplaced) = scan.Place();
            if (misplaced is not null)
            {
                Written(start, position, misplaced, start);
                continue;
            }

            var reading = new TagReader(text, start + 2, close).Read();
            if (reading.Limit is { } limit)
            {
                parts.Add(new StopPart(start, new TemplateProblem(TemplateProblems.TooManyFilters, limit)));
                break;
            }

            if (reading.Fail is { } fail)
            {
                Written(start, position, TemplateProblems.MalformedTag, fail);
                continue;
            }

            var pathAt = start + 2;
            while (pathAt < text.Length && text[pathAt] is ' ' or Tab)
            {
                pathAt++;
            }

            var problems = FilterProblems(reading.Filters, writer);
            parts.Add(new TagPart(start, text[start..position], reading.Path, pathAt, reading.Literal, reading.Filters, writer, problems));
            if (problems.Any(problem => !TemplateProblems.IsWarning(problem.Code)))
            {
                scan.Read(text, start, position);
            }
        }

        return new ParsedTemplate(context, parts, Ordered(parts.SelectMany(ProblemsOf)));

        // A tag written out as it stands puts its own text in the output, so the scan reads it as the template's own.
        void Written(int at, int end, string code, int where)
        {
            parts.Add(new WrittenPart(at, text[at..end], new TemplateProblem(code, where)));
            scan.Read(text, at, end);
        }
    }

    /// <summary>
    /// Renders a parsed template over its sources' values and a culture's formats (ADR-0064): a tag with an error as it
    /// is written, one with warnings only as nothing, and the output at most <see cref="MaxOutput"/> code units long.
    /// A path no source knows has no value (null). Never throws for any template, value or culture.
    /// </summary>
    public static RenderedTemplate Render(ParsedTemplate template, Func<string, MergeValue?> values, CultureFormats culture)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(culture);
        var problems = new List<TemplateProblem>();
        var output = new StringBuilder();
        var stopped = false;

        void Add(string piece, int at)
        {
            if (output.Length + piece.Length <= MaxOutput)
            {
                output.Append(piece);
                return;
            }

            // Up to the limit, but never between the two halves of a surrogate pair.
            var room = MaxOutput - output.Length;
            if (room > 0 && char.IsHighSurrogate(piece[room - 1]) && char.IsLowSurrogate(piece[room]))
            {
                room--;
            }

            output.Append(piece, 0, room);
            problems.Add(new TemplateProblem(TemplateProblems.TooLong, at));
            stopped = true;
        }

        foreach (var part in template.Parts)
        {
            if (stopped)
            {
                break;
            }

            switch (part)
            {
                case TextPart text:
                    Add(text.Text, text.At);
                    break;
                case WrittenPart written:
                    problems.Add(written.Problem);
                    Add(written.Text, written.At);
                    break;
                case StopPart stop:
                    problems.Add(stop.Problem);
                    stopped = true;
                    break;
                case TagPart tag:
                    var value = tag.Literal is { } literal ? MergeValue.Of(literal) : values(tag.Path ?? string.Empty);
                    if (value is null)
                    {
                        problems.Add(new TemplateProblem(TemplateProblems.UnknownPath, tag.PathAt));
                    }

                    problems.AddRange(tag.Problems);
                    if (tag.Problems.Any(problem => !TemplateProblems.IsWarning(problem.Code)))
                    {
                        Add(tag.Text, tag.At);
                    }
                    else if (value is not null && tag.Problems.Count == 0)
                    {
                        Add(Write(value, tag, culture), tag.At);
                    }

                    break;
            }
        }

        return new RenderedTemplate(output.ToString(), Ordered(problems));
    }

    private static IEnumerable<TemplateProblem> ProblemsOf(TemplatePart part) => part switch
    {
        TagPart tag => tag.Problems,
        WrittenPart written => [written.Problem],
        StopPart stop => [stop.Problem],
        _ => [],
    };

    /// <summary>Problems in the order of the template; those at one place keep their order.</summary>
    private static List<TemplateProblem> Ordered(IEnumerable<TemplateProblem> problems) => [.. problems.OrderBy(problem => problem.At)];

    /// <summary>
    /// Where the tag that starts at <paramref name="start"/> ends: the first <c>}}</c> outside a string literal; when a
    /// string does not close, the first <c>}}</c> after the start. -1 when there is none.
    /// </summary>
    private static int ClosingOf(string text, int start)
    {
        var inString = false;
        var first = -1;
        for (var i = start + 2; i < text.Length; i++)
        {
            var c = text[i];
            var closes = c == '}' && i + 1 < text.Length && text[i + 1] == '}';
            if (closes && first < 0)
            {
                first = i;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (closes)
            {
                return i;
            }
        }

        return inString ? first : -1;
    }

    private static bool IsDigit(int c) => c is >= '0' and <= '9';

    private static bool IsLowerLetter(int c) => c is >= 'a' and <= 'z';

    private static bool IsLetter(int c) => c is >= 'A' and <= 'Z' || IsLowerLetter(c);

    /// <summary>An ASCII capital as its small letter; any other code unchanged.</summary>
    private static int Small(int c) => c is >= 'A' and <= 'Z' ? c + 32 : c;

    /// <summary>What a tag's text holds, or where its grammar fails, or the eleventh filter's <c>|</c>.</summary>
    private sealed record TagReading(string? Path, string? Literal, IReadOnlyList<TemplateFilter> Filters, int? Fail = null, int? Limit = null);

    /// <summary>Reads a tag's text between <c>{{</c> and <c>}}</c> by the grammar of ADR-0014.</summary>
    private sealed class TagReader(string text, int from, int end)
    {
        private int _position = from;
        private int _failure = -1;

        public TagReading Read()
        {
            Spaces();
            var start = _position;
            string? path = null;
            string? literal = null;
            if (Code() == '"')
            {
                literal = String();
                if (literal is null)
                {
                    return Failed(_failure);
                }
            }
            else
            {
                if (!Name())
                {
                    return Failed(_position);
                }

                while (Code() == '.')
                {
                    _position++;
                    if (!Segment())
                    {
                        return Failed(_position);
                    }
                }

                path = text[start.._position];
            }

            var filters = new List<TemplateFilter>();
            while (true)
            {
                Spaces();
                if (_position == end)
                {
                    return new TagReading(path, literal, filters);
                }

                if (Code() != '|')
                {
                    return Failed(_position);
                }

                if (filters.Count == MaxFilters)
                {
                    return new TagReading(null, null, [], Limit: _position);
                }

                _position++;
                Spaces();
                var at = _position;
                if (!Name())
                {
                    return Failed(_position);
                }

                var name = text[at.._position];
                var arguments = new List<TemplateArgument>();
                var back = _position;
                Spaces();
                if (Code() == ':')
                {
                    _position++;
                    while (true)
                    {
                        Spaces();
                        if (Argument() is not { } argument)
                        {
                            return Failed(_failure);
                        }

                        arguments.Add(argument);
                        var mark = _position;
                        Spaces();
                        if (Code() != ',')
                        {
                            _position = mark;
                            break;
                        }

                        _position++;
                    }
                }
                else
                {
                    _position = back;
                }

                filters.Add(new TemplateFilter(name, at, arguments));
            }
        }

        private static TagReading Failed(int at) => new(null, null, [], Fail: at);

        /// <summary>The code unit at the position, or -1 at the tag's end.</summary>
        private int Code(int offset = 0) => _position + offset < end ? text[_position + offset] : -1;

        private void Spaces()
        {
            while (Code() is ' ' or 9)
            {
                _position++;
            }
        }

        /// <summary>A source's or filter's name: a small letter, then letters and digits.</summary>
        private bool Name()
        {
            if (!IsLowerLetter(Code()))
            {
                return false;
            }

            while (IsLetter(Code()) || IsDigit(Code()))
            {
                _position++;
            }

            return true;
        }

        /// <summary>A path's segment: letters, digits, <c>_</c> and <c>-</c>.</summary>
        private bool Segment()
        {
            var start = _position;
            while (Code() is var c && (IsLetter(c) || IsDigit(c) || c is '_' or '-'))
            {
                _position++;
            }

            return _position > start;
        }

        /// <summary>A string's text; its only escapes are a quote and a backslash after a backslash.</summary>
        private string? String()
        {
            var start = _position++;
            var builder = new StringBuilder();
            while (_position < end)
            {
                var c = Code();
                if (c == '"')
                {
                    _position++;
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    var next = Code(1);
                    if (next is not ('"' or '\\'))
                    {
                        _failure = _position;
                        return null;
                    }

                    builder.Append((char)next);
                    _position += 2;
                }
                else
                {
                    builder.Append(text[_position++]);
                }
            }

            _failure = start;
            return null;
        }

        private TemplateArgument? Argument()
        {
            var at = _position;
            if (Code() == '"')
            {
                return String() is { } value ? new TemplateArgument(true, value, at) : null;
            }

            if (Code() == '-')
            {
                _position++;
            }

            // The wire form of ADR-0005: no leading zero, and at least one digit after a point.
            if (!IsDigit(Code()) || (Code() == '0' && IsDigit(Code(1))))
            {
                _failure = _position;
                return null;
            }

            while (IsDigit(Code()))
            {
                _position++;
            }

            if (Code() == '.')
            {
                _position++;
                if (!IsDigit(Code()))
                {
                    _failure = _position;
                    return null;
                }

                while (IsDigit(Code()))
                {
                    _position++;
                }
            }

            return new TemplateArgument(false, text[at.._position], at);
        }
    }
}
