namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>The places a template renders in, each with its own encoder (ADR-0014, ADR-0064).</summary>
public enum MergeTagContext
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

/// <summary>
/// One item of a merge tag's value (ADR-0064): its text, and the wire number or the wire date behind it, which the
/// <c>number</c>, <c>currency</c>, <c>date</c> and <c>json</c> filters read. A number or a date a filter cannot read
/// leaves the item as it is.
/// </summary>
/// <param name="Text">The text, as the value is displayed.</param>
/// <param name="Number">The number behind the text, in the wire form of ADR-0005; null for none.</param>
/// <param name="Date">The date, time or date-time behind the text, in the wire form of ADR-0005; null for none.</param>
public sealed record MergeTagItem(string Text, string? Number = null, string? Date = null)
{
    /// <summary>The text, as the value is displayed.</summary>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    public string Text { get; init; } = Text ?? throw new ArgumentNullException(nameof(Text));
}

/// <summary>
/// What a source gives for a path (ADR-0064): one item, or a list of them, which a filter applies to item by item
/// unless it takes the list as a whole, and which reaches its place joined by ", ".
/// </summary>
/// <param name="Items">The items: one, or for a list any number.</param>
/// <param name="IsList">Whether the value is a list.</param>
public sealed record MergeTagValue(IReadOnlyList<MergeTagItem> Items, bool IsList)
{
    /// <summary>The items: one, or for a list any number.</summary>
    /// <exception cref="ArgumentException">The items are null or hold a null.</exception>
    public IReadOnlyList<MergeTagItem> Items { get; init; } = Items is not null && Items.All(item => item is not null)
        ? Items
        : throw new ArgumentException("A value's items are never null.", nameof(Items));

    /// <summary>
    /// The value as markup, written in place of its items only in the HTML context and only by a tag without filters; the
    /// built-in sources' own (the answers' table of <c>entry.answers</c>, ADR-0066), whose texts they escaped
    /// themselves. A site's source gives text, never markup, so the property is not public.
    /// </summary>
    internal string? Html { get; init; }

    /// <summary>The empty text: a known path without an answer.</summary>
    public static MergeTagValue Empty { get; } = Of(string.Empty);

    /// <summary>One item of text.</summary>
    /// <param name="text">The text.</param>
    public static MergeTagValue Of(string text) => new([new MergeTagItem(text)], false);

    /// <summary>One item.</summary>
    /// <param name="item">The item.</param>
    public static MergeTagValue Of(MergeTagItem item) => new([item], false);

    /// <summary>A list.</summary>
    /// <param name="items">The list's items, any number.</param>
    public static MergeTagValue ListOf(IReadOnlyList<MergeTagItem> items) => new(items, true);
}

/// <summary>A problem of a template and the UTF-16 index where it starts (ADR-0064).</summary>
/// <param name="Code">One of the codes of <see cref="MergeTagProblems"/>.</param>
/// <param name="At">The UTF-16 index in the template where the problem starts.</param>
public readonly record struct MergeTagProblem(string Code, int At)
{
    /// <summary>Whether the problem is a warning, after which the tag renders empty rather than as it is written.</summary>
    public bool IsWarning => MergeTagProblems.IsWarning(Code);
}

/// <summary>
/// The problems of a template (ADR-0064, ADR-0065). Those of the language are the same in TypeScript; those of a
/// place come from the server's check of where a template is used.
/// </summary>
public static class MergeTagProblems
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

    /// <summary>More than 200 tags.</summary>
    public const string TooManyTags = "too_many_tags";

    /// <summary>A tag with more than 10 filters.</summary>
    public const string TooManyFilters = "too_many_filters";

    /// <summary>The output would pass 100,000 UTF-16 code units.</summary>
    public const string TooLong = "too_long";

    /// <summary>No source knows the path (a warning).</summary>
    public const string UnknownPath = "unknown_path";

    /// <summary>No filter has the name (a warning).</summary>
    public const string UnknownFilter = "unknown_filter";

    /// <summary>The place does not take the path's source (ADR-0014, ADR-0065), at the path.</summary>
    public const string SourceNotForPlace = "source_not_for_place";

    /// <summary>The place does not take a site's own filter, since the browser renders it too (ADR-0065), at the filter.</summary>
    public const string FilterNotForPlace = "filter_not_for_place";

    /// <summary>Whether a tag with this problem renders empty, rather than as it is written.</summary>
    /// <param name="code">The problem's code.</param>
    public static bool IsWarning(string code) => code is UnknownPath or UnknownFilter;
}
