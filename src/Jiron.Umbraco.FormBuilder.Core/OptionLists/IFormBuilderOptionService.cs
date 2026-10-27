using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// The options of a choice field or a library option list, resolved from wherever they come from (section 4.9,
/// ADR-0073): the field's own options, a library list, or a source configured on the field, in one culture and, for a
/// cascading field, for the parent's answers.
/// </summary>
public interface IFormBuilderOptionService
{
    /// <summary>The options of a field; none, but available, for a field without options.</summary>
    /// <param name="field">The field, as the version holds it.</param>
    /// <param name="context">The culture, the parent's answers and the current page.</param>
    /// <param name="cancellationToken">Cancelled when the page or the submission is.</param>
    Task<FormBuilderResolvedOptions> GetAsync(FormField field, FormBuilderOptionContext context, CancellationToken cancellationToken = default);

    /// <summary>The options of a library list; unavailable for a list the site lacks.</summary>
    /// <param name="list">The list's key.</param>
    /// <param name="context">The culture, the parent's answers and the current page.</param>
    /// <param name="cancellationToken">Cancelled when the page or the request is.</param>
    Task<FormBuilderResolvedOptions> GetListAsync(Guid list, FormBuilderOptionContext context, CancellationToken cancellationToken = default);
}

/// <summary>What options are resolved for.</summary>
/// <param name="Culture">The culture the labels are in, such as <c>de-CH</c>.</param>
public sealed record FormBuilderOptionContext(string Culture)
{
    /// <summary>The parent's answers, for a field whose options a parent field filters; ignored for any other.</summary>
    public IReadOnlyList<string> ParentValues { get; init => field = value ?? []; } = [];

    /// <summary>The key of the content page the form is on, or null.</summary>
    public Guid? Page { get; init; }
}

/// <summary>The options resolved for a field or a list (ADR-0073).</summary>
/// <param name="Options">The options, in the order visitors see them, each label in the context's culture.</param>
/// <param name="IsAvailable">
/// False when the source could not give its options: a list or source the site lacks, a stored list that no longer
/// reads, or a source that failed or took longer than 5 seconds. There are no options then.
/// </param>
public sealed record FormBuilderResolvedOptions(IReadOnlyList<FormBuilderOption> Options, bool IsAvailable)
{
    /// <summary>No options, from a source that could not give any.</summary>
    public static FormBuilderResolvedOptions Unavailable { get; } = new([], IsAvailable: false);

    /// <summary>No options, from a source that has none.</summary>
    public static FormBuilderResolvedOptions None { get; } = new([], IsAvailable: true);
}
