using System.Text;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Logic;

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

/// <summary>A rendered template: its text, and its problems in the order of the template.</summary>
internal sealed record RenderedTemplate(string Text, IReadOnlyList<MergeTagProblem> Problems);

/// <summary>A filter's argument: a string's text, or a number as written.</summary>
internal sealed record TemplateArgument(bool IsString, string Text, int At);

/// <summary>A filter of a tag, with its arguments, and the site's own filter of that name when no built-in one has it.</summary>
internal sealed record TemplateFilter(string Name, int At, IReadOnlyList<TemplateArgument> Arguments, SiteFilter? Site = null);

/// <summary>
/// A site's own filter as the engine runs it (ADR-0065): whether its arguments fit, and what it does to a value. It never
/// encodes, so the context's encoder still runs on what it gives.
/// </summary>
internal sealed record SiteFilter(Func<IReadOnlyList<TemplateArgument>, bool> Accepts, Func<MergeTagValue, IReadOnlyList<TemplateArgument>, MergeTagValue> Apply);

/// <summary>
/// What the place a template renders in adds to the language (ADR-0065): the site's own filters by name, whether the
/// place takes them, and the problem a path's source has there (null for none). A tag with such a problem renders as it
/// is written, like any tag with an error.
/// </summary>
internal sealed record TemplateRules(Func<string, SiteFilter?> SiteFilters, bool TakesSiteFilters, Func<string, string?> PathProblem);

/// <summary>A part of a parsed template.</summary>
internal abstract record TemplatePart(int At);

/// <summary>The template's own text.</summary>
internal sealed record TextPart(int At, string Text) : TemplatePart(At);

/// <summary>A tag with an error of its own, which renders as it is written.</summary>
internal sealed record WrittenPart(int At, string Text, MergeTagProblem Problem) : TemplatePart(At);

/// <summary>A well-formed tag in its place: a path or a string, its filters, how it is written, and its filters' problems.</summary>
internal sealed record TagPart(
    int At,
    string Text,
    string? Path,
    int PathAt,
    string? Literal,
    IReadOnlyList<TemplateFilter> Filters,
    TemplateWriter Writer,
    IReadOnlyList<MergeTagProblem> Problems) : TemplatePart(At);

/// <summary>A limit: the output ends here.</summary>
internal sealed record StopPart(int At, MergeTagProblem Problem) : TemplatePart(At);

/// <summary>A template parsed for its context, with the problems that do not depend on values.</summary>
internal sealed record ParsedTemplate(MergeTagContext Context, IReadOnlyList<TemplatePart> Parts, IReadOnlyList<MergeTagProblem> Problems);

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
    /// template. <paramref name="rules"/> adds what the place adds (ADR-0065): without them, no path is refused and no
    /// site's filter is known.
    /// </summary>
    public static ParsedTemplate Parse(string text, MergeTagContext context, TemplateRules? rules = null)
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
                parts.Add(new StopPart(start, new MergeTagProblem(MergeTagProblems.TooManyTags, start)));
                break;
            }

            var close = ClosingOf(text, start);
            if (close < 0)
            {
                Written(start, text.Length, MergeTagProblems.UnclosedTag, start);
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
                parts.Add(new StopPart(start, new MergeTagProblem(MergeTagProblems.TooManyFilters, limit)));
                break;
            }

            if (reading.Fail is { } fail)
            {
                Written(start, position, MergeTagProblems.MalformedTag, fail);
                continue;
            }

            var pathAt = start + 2;
            while (pathAt < text.Length && text[pathAt] is ' ' or Tab)
            {
                pathAt++;
            }

            if (reading.Path is { } path && rules?.PathProblem(path) is { } refused)
            {
                Written(start, position, refused, pathAt);
                continue;
            }

            var filters = rules is null
                ? reading.Filters
                : [.. reading.Filters.Select(filter => Filters.ContainsKey(filter.Name) ? filter : filter with { Site = rules.SiteFilters(filter.Name) })];
            var problems = FilterProblems(filters, writer, rules?.TakesSiteFilters ?? false);
            parts.Add(new TagPart(start, text[start..position], reading.Path, pathAt, reading.Literal, filters, writer, problems));
            if (problems.Any(problem => !MergeTagProblems.IsWarning(problem.Code)))
            {
                scan.Read(text, start, position);
            }
        }

        return new ParsedTemplate(context, parts, Ordered(parts.SelectMany(ProblemsOf)));

        // A tag written out as it stands puts its own text in the output, so the scan reads it as the template's own.
        void Written(int at, int end, string code, int where)
        {
            parts.Add(new WrittenPart(at, text[at..end], new MergeTagProblem(code, where)));
            scan.Read(text, at, end);
        }
    }

    /// <summary>
    /// Renders a parsed template over its sources' values and a culture's formats (ADR-0064): a tag with an error as it
    /// is written, one with warnings only as nothing, and the output at most <see cref="MaxOutput"/> code units long.
    /// A path no source knows has no value (null). Never throws for any template, value or culture.
    /// </summary>
    public static RenderedTemplate Render(ParsedTemplate template, Func<string, MergeTagValue?> values, CultureFormats culture)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(culture);
        var problems = new List<MergeTagProblem>();
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
            problems.Add(new MergeTagProblem(MergeTagProblems.TooLong, at));
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
                    var value = tag.Literal is { } literal ? MergeTagValue.Of(literal) : values(tag.Path ?? string.Empty);
                    if (value is null)
                    {
                        problems.Add(new MergeTagProblem(MergeTagProblems.UnknownPath, tag.PathAt));
                    }

                    problems.AddRange(tag.Problems);
                    if (tag.Problems.Any(problem => !MergeTagProblems.IsWarning(problem.Code)))
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

    private static IEnumerable<MergeTagProblem> ProblemsOf(TemplatePart part) => part switch
    {
        TagPart tag => tag.Problems,
        WrittenPart written => [written.Problem],
        StopPart stop => [stop.Problem],
        _ => [],
    };

    /// <summary>Problems in the order of the template; those at one place keep their order.</summary>
    private static List<MergeTagProblem> Ordered(IEnumerable<MergeTagProblem> problems) => [.. problems.OrderBy(problem => problem.At)];

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
