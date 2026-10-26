namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// A filter of a site's own (section 4.9, ADR-0014, ADR-0065), for example <c>{{ field.name | initials }}</c>,
/// registered through <c>builder.JironFormBuilder().MergeTagFilters().Append&lt;InitialsFilter&gt;()</c>. A filter is a
/// singleton without state of its own. It runs only on the server, so the places the browser renders too (visitor texts,
/// consent texts, default values) refuse it; and it never encodes: the place's context escapes what it gives, and like
/// every filter it may not follow <c>html</c>, <c>url</c> or <c>json</c>.
/// </summary>
public interface IFormBuilderMergeTagFilter
{
    /// <summary>
    /// The filter's name: a lowercase ASCII letter, then ASCII letters and digits, and none of the built-in filters'
    /// names. Start-up refuses a name that is taken or outside the grammar.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether the arguments fit the filter; a tag whose arguments do not fit is <c>invalid_argument</c> at the filter
    /// and renders as it is written. An exception counts as arguments that do not fit, and is logged.
    /// </summary>
    /// <param name="arguments">The arguments as written, strings with their quotes removed and numbers in the wire form.</param>
    bool Accepts(IReadOnlyList<MergeTagArgument> arguments);

    /// <summary>
    /// The value after the filter: each item, or the list as a whole, as the filter means to. An exception renders the
    /// tag empty and is logged without values.
    /// </summary>
    /// <param name="request">The value, the arguments and the culture.</param>
    MergeTagValue Apply(MergeTagFilterRequest request);
}

/// <summary>A filter's argument as written: a string's text without its quotes and escapes, or a number in the wire form.</summary>
/// <param name="Text">The string's text, or the number as written.</param>
/// <param name="IsString">Whether the argument is a string.</param>
public sealed record MergeTagArgument(string Text, bool IsString);

/// <summary>What a filter applies to.</summary>
/// <param name="Value">The value so far.</param>
/// <param name="Arguments">The filter's arguments, which <see cref="IFormBuilderMergeTagFilter.Accepts"/> accepted.</param>
/// <param name="Culture">The culture the template renders in, a name such as <c>de-CH</c>.</param>
public sealed record MergeTagFilterRequest(MergeTagValue Value, IReadOnlyList<MergeTagArgument> Arguments, string Culture);
