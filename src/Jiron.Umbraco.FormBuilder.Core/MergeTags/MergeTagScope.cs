using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// What a template is rendered for (ADR-0065): the form, the culture its texts and formats are in, the site, the page
/// and the member, the entry once there is one, its answers, and the query and context values. The sources read what
/// their paths need from it.
/// </summary>
public sealed record MergeTagScope
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoQuery = FrozenDictionary<string, IReadOnlyList<string>>.Empty;
    private static readonly IReadOnlyDictionary<string, string> NoContext = FrozenDictionary<string, string>.Empty;
    private static readonly IReadOnlySet<Guid> NoFields = FrozenSet<Guid>.Empty;

    /// <summary>The form's definition: the version the entry was submitted with, or the one the page shows.</summary>
    public required FormDefinition Form { get; init; }

    /// <summary>The form's name, which lives on the form rather than in a version (ADR-0002).</summary>
    public required string FormName { get; init; }

    /// <summary>The form's alias.</summary>
    public required string FormAlias { get; init; }

    /// <summary>
    /// The culture the texts and formats are in, a name .NET knows such as <c>de-CH</c>: the page's, or the entry's once
    /// there is one. A name .NET does not know formats as the invariant culture.
    /// </summary>
    public required string Culture { get; init; }

    /// <summary>
    /// The key of the site's root content, whose settings (its time zone, its base URL) apply and whose name
    /// <c>site.name</c> shows; null for the global settings.
    /// </summary>
    public Guid? Site { get; init; }

    /// <summary>
    /// The key of the content page the form is shown on or was submitted from, which <c>page.*</c> reads (ADR-0067); null
    /// without one, such as a form rendered headless.
    /// </summary>
    public Guid? Page { get; init; }

    /// <summary>The key of the member who is signed in, which <c>member.*</c> reads (ADR-0067); null when no one is.</summary>
    public Guid? Member { get; init; }

    /// <summary>The entry, once the visitor has submitted it; null before.</summary>
    public MergeTagEntry? Entry { get; init; }

    /// <summary>The page's query values by name, as posted or in the address; the form's allowed names are read from it.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Query { get; init => field = value ?? NoQuery; } = NoQuery;

    /// <summary>The values the developer passed when rendering or submitting (section 4.9), by name.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init => field = value ?? NoContext; } = NoContext;

    /// <summary>The answers <c>field.*</c> and <c>entry.answers</c> show (ADR-0066), and the fields the rules hid; none by default.</summary>
    public MergeTagAnswers Answers { get; init => field = value ?? MergeTagAnswers.None; } = MergeTagAnswers.None;

    /// <summary>
    /// The sensitive fields an action names to include (ADR-0014): only mail and the other actions show them, in their
    /// tags and in <c>entry.answers</c>; anywhere else a sensitive field stays masked or left out. None by default.
    /// </summary>
    public IReadOnlySet<Guid> IncludedSensitive { get; init => field = value ?? NoFields; } = NoFields;
}

/// <summary>An entry as merge tags see it (ADR-0014): its key, number, state and when it was submitted.</summary>
public sealed record MergeTagEntry
{
    /// <summary>The entry's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The entry's number within its form, from 1.</summary>
    public required int Number { get; init; }

    /// <summary>The entry's state as entries store it, for example <c>submitted</c> or <c>approved</c>.</summary>
    public required string State { get; init; }

    /// <summary>When the entry was submitted.</summary>
    public required DateTimeOffset Submitted { get; init; }
}

/// <summary>A rendered template: its text, and its problems in the order of the template (ADR-0064).</summary>
/// <param name="Text">The text, at most 100,000 UTF-16 code units long.</param>
/// <param name="Problems">The problems, in the order of the template.</param>
public sealed record MergeTagRendering(string Text, IReadOnlyList<MergeTagProblem> Problems);

/// <summary>A path a source offers in the picker, with what the picker shows for it.</summary>
/// <param name="Path">The whole path, the source's name first, for example <c>entry.reference</c>.</param>
/// <param name="Label">What the picker shows, such as a field's label; null to show the path.</param>
public sealed record MergeTagPathInfo(string Path, string? Label = null);
