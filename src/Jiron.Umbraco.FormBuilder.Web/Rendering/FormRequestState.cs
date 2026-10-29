using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The instance ids of the forms a request renders (ADR-0004): <c>jfb-1</c> for the first, <c>jfb-2</c> for the
/// next, in the order they render, so the same page gives the same ids to every visitor.
/// </summary>
internal static class FormInstances
{
    // The key under which a request counts the forms it rendered.
    private const string Count = "Jiron.FormBuilder.Rendering.Instances";

    /// <summary>The prefix of every instance id.</summary>
    public const string Prefix = "jfb-";

    /// <summary>The next instance id of the request.</summary>
    /// <param name="httpContext">The request.</param>
    public static string Next(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var next = (httpContext.Items[Count] as int? ?? 0) + 1;
        httpContext.Items[Count] = next;
        return Prefix + next.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>A notice above the form after a post, such as "the form has changed".</summary>
/// <param name="Heading">Its heading, or null.</param>
/// <param name="Text">Its text.</param>
/// <param name="Code">
/// What happened (<c>form_changed</c>, <c>not_sent</c>), which makes the notice an alert in the error summary's place
/// (ADR-0100); null for a notice that is none.
/// </param>
internal sealed record FormPostNotice(string? Heading, string Text, string? Code = null);

/// <summary>The outcome of a post that sent the form.</summary>
/// <param name="Html">The message, sanitised.</param>
/// <param name="Reference">The entry's reference, or null.</param>
/// <param name="IsPending">Whether the visitor must still confirm their email address.</param>
/// <param name="ContinueUrl">The redirect after the message, which the visitor follows by a link (ADR-0100); null for none.</param>
internal sealed record FormPostOutcome(string Html, string? Reference, bool IsPending, string? ContinueUrl = null);

/// <summary>Where a post moves from its page (ADR-0100); the form's render works out the page with the rules.</summary>
internal enum FormPostMove
{
    /// <summary>The posted page again, or the first page the rules show when they hide it.</summary>
    Stay,

    /// <summary>The next page after the posted one that the rules show.</summary>
    Next,

    /// <summary>The last page before the posted one that the rules show.</summary>
    Back,
}

/// <summary>
/// An error a post left for a field (ADR-0099), as the pipeline reports it; the page shows the editor's message for it,
/// else the package's default.
/// </summary>
/// <param name="Code">
/// The field's kind of error as its <c>messages</c> name it (<c>required</c>, <c>maxLength</c>, ...), <c>check</c> for one
/// of its own checks or <c>rule</c> for a validation rule.
/// </param>
/// <param name="Check">For <c>check</c>, the index of the field's check that fails.</param>
/// <param name="Rule">For <c>rule</c>, the key of the validation rule whose message shows.</param>
/// <param name="GridRow">For a grid, the key of the row the error is at; null for the grid's own.</param>
internal sealed record FormPostError(string Code, int? Check = null, Guid? Rule = null, Guid? GridRow = null);

/// <summary>
/// What a post without JavaScript leaves for the form it renders again (ADR-0004, ADR-0093, ADR-0100): the answers, the
/// errors by field, each repeatable group's rows, the page it came from and where it moves, the query values it carried,
/// a notice above the form, or the outcome. The surface action fills it, and so does a page after the redirect to an
/// outcome.
/// </summary>
internal sealed record FormPostState
{
    /// <summary>The visitor's answers by the field's key.</summary>
    public IReadOnlyDictionary<Guid, FieldAnswers> Answers { get; init; } = new Dictionary<Guid, FieldAnswers>();

    /// <summary>The errors by the field's key; a repeatable group's own error, such as too few rows, among them.</summary>
    public IReadOnlyDictionary<Guid, FormPostError> Errors { get; init; } = new Dictionary<Guid, FormPostError>();

    /// <summary>Whether the post held no answer at all (ADR-0099): the error summary says so, linked to the page's first field.</summary>
    public bool IsEmpty { get; init; }

    /// <summary>
    /// Each repeatable group's rows by the group's key, in the order the visitor saw them, each with its child fields'
    /// answers and errors (ADR-0098); a group without rows has none.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<FormPostRow>> Rows { get; init; } = new Dictionary<Guid, IReadOnlyList<FormPostRow>>();

    /// <summary>The page the post came from, which <see cref="Move"/> moves from; null for the first page the rules show.</summary>
    public Guid? Page { get; init; }

    /// <summary>Where the page to show is from <see cref="Page"/>.</summary>
    public FormPostMove Move { get; init; }

    /// <summary>The query values the post carried, of the names the form lists, which the page carries on (ADR-0100).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Query { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>A notice above the form, or null.</summary>
    public FormPostNotice? Notice { get; init; }

    /// <summary>The outcome, in place of the form; null while the form shows.</summary>
    public FormPostOutcome? Outcome { get; init; }
}

/// <summary>One row of a repeatable group that a post left: its child fields' answers and errors by their keys (ADR-0098).</summary>
internal sealed record FormPostRow
{
    /// <summary>The visitor's answers by the child field's key.</summary>
    public IReadOnlyDictionary<Guid, FieldAnswers> Answers { get; init; } = new Dictionary<Guid, FieldAnswers>();

    /// <summary>The errors by the child field's key, each the error of its field in this row.</summary>
    public IReadOnlyDictionary<Guid, FormPostError> Errors { get; init; } = new Dictionary<Guid, FormPostError>();
}

/// <summary>Where a post's state waits for the form it renders again: the request's items, by the form's key and the instance.</summary>
internal static class FormPosts
{
    // The prefix of the keys under which a request holds the posts' states.
    private const string Prefix = "Jiron.FormBuilder.Rendering.Post:";

    /// <summary>Leaves a post's state for the form and the instance.</summary>
    public static void Set(HttpContext httpContext, Guid form, string instance, FormPostState state)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(state);
        httpContext.Items[Key(form, instance)] = state;
    }

    /// <summary>The state a post left for the form and the instance, or null.</summary>
    public static FormPostState? Get(HttpContext httpContext, Guid form, string instance)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(Key(form, instance), out var state) ? state as FormPostState : null;
    }

    private static string Key(Guid form, string instance) => Prefix + form.ToString("N") + ":" + instance;
}
