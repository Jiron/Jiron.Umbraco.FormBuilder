using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Where a template is used (ADR-0014, ADR-0065), which decides the contexts it renders in, the sources it may name and
/// whether a site's own filters may run in it.
/// </summary>
public enum MergeTagPlace
{
    /// <summary>Labels, help texts, placeholders, page and group titles and text blocks, which the browser renders too.</summary>
    VisitorText,

    /// <summary>A consent field's text, stored as the visitor saw it, so the same for every visitor (ADR-0009).</summary>
    ConsentText,

    /// <summary>A field's default value, which the browser fills in on a cached page.</summary>
    DefaultValue,

    /// <summary>A hidden field's value, resolved when the entry is submitted and never rendered into the page.</summary>
    HiddenValue,

    /// <summary>An outcome's message and the copy of the answers.</summary>
    Outcome,

    /// <summary>A redirect's address, after the host only.</summary>
    Redirect,

    /// <summary>The line that names each entry in the lists.</summary>
    EntryTitle,

    /// <summary>A mail's subject or body.</summary>
    Mail,

    /// <summary>The texts of the other actions: webhooks, chat messages, the content action.</summary>
    Action,
}

/// <summary>
/// What a place takes (ADR-0014, ADR-0065): its contexts, the built-in sources it may name (null for all of them),
/// whether a site's source may be used and one whose values are per visitor, and whether a site's own filters may run.
/// </summary>
internal sealed record PlaceRules(
    FrozenSet<MergeTagContext> Contexts,
    FrozenSet<string>? Sources,
    bool TakesSiteSources,
    bool TakesPerVisitorSources,
    bool TakesSiteFilters);

/// <summary>The rules of every place, ADR-0014's table of where each source may be used.</summary>
internal static class MergeTagPlaces
{
    /// <summary>The built-in sources, each with whether its values are per visitor.</summary>
    public static readonly FrozenDictionary<string, bool> BuiltInSources = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["field"] = true,
        ["entry"] = true,
        ["form"] = false,
        ["page"] = false,
        ["member"] = true,
        ["query"] = true,
        ["context"] = true,
        ["site"] = false,
        ["date"] = false,
        ["dictionary"] = false,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<MergeTagPlace, PlaceRules> Rules = new Dictionary<MergeTagPlace, PlaceRules>
    {
        // The browser renders visitor texts and default values too, so they take only the built-in filters, and the page
        // may be cached, so nothing per visitor beyond answer recall and the allowed query values.
        [MergeTagPlace.VisitorText] = new(Contexts(MergeTagContext.Html, MergeTagContext.Text), Sources("field", "form", "page", "site", "date", "dictionary"), true, false, false),
        [MergeTagPlace.ConsentText] = new(Contexts(MergeTagContext.Html), Sources("form", "page", "site", "dictionary"), false, false, false),
        [MergeTagPlace.DefaultValue] = new(Contexts(MergeTagContext.Text), Sources("form", "page", "site", "date", "dictionary", "query"), true, false, false),
        [MergeTagPlace.HiddenValue] = new(Contexts(MergeTagContext.Text), null, true, true, true),
        [MergeTagPlace.Outcome] = new(Contexts(MergeTagContext.Html), null, true, true, true),
        [MergeTagPlace.Redirect] = new(Contexts(MergeTagContext.Url), null, true, true, true),
        [MergeTagPlace.EntryTitle] = new(Contexts(MergeTagContext.Text), Sources("field", "entry", "form"), false, false, true),
        [MergeTagPlace.Mail] = new(Contexts(MergeTagContext.Header, MergeTagContext.Html, MergeTagContext.Text), null, true, true, true),
        [MergeTagPlace.Action] = new(Enum.GetValues<MergeTagContext>().ToFrozenSet(), null, true, true, true),
    }.ToFrozenDictionary();

    /// <summary>The rules of a place.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The place is not one of <see cref="MergeTagPlace"/>.</exception>
    public static PlaceRules For(MergeTagPlace place) =>
        Rules.TryGetValue(place, out var rules) ? rules : throw new ArgumentOutOfRangeException(nameof(place), place, "Not a merge-tag place.");

    /// <summary>Whether a place takes a site's source with the given traits, as the registry read them once.</summary>
    public static bool TakesSiteSource(PlaceRules rules, MergeTagPlace place, MergeTagSourceTraits traits) =>
        rules.TakesSiteSources && (!traits.IsPerVisitor || rules.TakesPerVisitorSources) && (traits.Places is null || traits.Places.Contains(place));

    private static FrozenSet<MergeTagContext> Contexts(params MergeTagContext[] contexts) => contexts.ToFrozenSet();

    private static FrozenSet<string> Sources(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);
}
