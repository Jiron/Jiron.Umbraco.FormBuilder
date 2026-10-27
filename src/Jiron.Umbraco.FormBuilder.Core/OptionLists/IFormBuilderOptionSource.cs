namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// A source of options from code (section 4.9, ADR-0073), for example a product catalogue, registered through
/// <c>builder.JironFormBuilder().OptionSources().Append&lt;ProductSource&gt;()</c>. A field or a library list of the
/// kind <c>code</c> names it by its name. A source is a singleton: it holds no state of its own and may take singleton
/// services in its constructor. The standard lists (months, weekdays, two scales and "yes, no, not sure") are sources
/// too, which a site may replace or remove.
/// </summary>
public interface IFormBuilderOptionSource
{
    /// <summary>
    /// The name fields and lists name the source by: a lowercase ASCII letter, then ASCII letters and digits, at most
    /// 64 characters. Start-up refuses a name that two sources share or that is outside the grammar.
    /// </summary>
    string Name { get; }

    /// <summary>What the backoffice calls the source: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    string Label { get; }

    /// <summary>
    /// The options for a request, in the order visitors see them. Each value is what entries store: what is blank,
    /// longer than 200 characters, changed when cleaned as answers are, or equal to an earlier value once letter case is
    /// ignored is left out, and so is everything past 10,000 options. An exception, or an answer that takes longer than
    /// 5 seconds, gives the field no options and is logged without values, so a source never stops a page or a
    /// submission.
    /// </summary>
    /// <param name="request">The culture, the parent's answers and the current page.</param>
    /// <param name="cancellationToken">Cancelled when the page or the submission is.</param>
    ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken);
}

/// <summary>What a source's options are asked for.</summary>
/// <param name="Culture">The culture the labels are in, such as <c>de-CH</c>.</param>
/// <param name="ParentValues">
/// The answers of the field whose answer filters the options (cascading), cleaned; empty without a parent or an answer.
/// A source may filter by them itself; an option that names parent values is kept only for one of them either way.
/// </param>
/// <param name="Page">The key of the content page the form is on, or null.</param>
public sealed record FormBuilderOptionRequest(string Culture, IReadOnlyList<string> ParentValues, Guid? Page);

/// <summary>One option a source gives (ADR-0073).</summary>
public sealed record FormBuilderOption
{
    /// <summary>An option of a value and its label.</summary>
    /// <param name="value">What entries store: 1 to 200 characters, the same once cleaned as answers are.</param>
    /// <param name="label">The label in the request's culture; the value shows when it is null or blank.</param>
    public FormBuilderOption(string value, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        Label = label;
    }

    /// <summary>What entries store.</summary>
    public string Value { get; }

    /// <summary>The label in the request's culture, or null for the value.</summary>
    public string? Label { get; init; }

    /// <summary>A text below the label, or null.</summary>
    public string? Description { get; init; }

    /// <summary>The key of a media item shown with the option, or null.</summary>
    public Guid? Image { get; init; }

    /// <summary>The number of places, for a type whose options fill up (section 4.4); null for no limit.</summary>
    public int? Places { get; init; }

    /// <summary>The parent's answers the option belongs to; empty for an option that belongs to every answer.</summary>
    public IReadOnlyList<string> ParentValues { get; init => field = value ?? []; } = [];
}
