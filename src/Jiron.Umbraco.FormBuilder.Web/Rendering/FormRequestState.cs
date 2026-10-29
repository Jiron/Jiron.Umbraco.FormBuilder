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
internal sealed record FormPostNotice(string? Heading, string Text);

/// <summary>The outcome of a post that sent the form.</summary>
/// <param name="Html">The message, sanitised.</param>
/// <param name="Reference">The entry's reference, or null.</param>
/// <param name="IsPending">Whether the visitor must still confirm their email address.</param>
internal sealed record FormPostOutcome(string Html, string? Reference, bool IsPending);

/// <summary>
/// What a post without JavaScript leaves for the form it renders again (ADR-0004, ADR-0093): the answers, the errors by
/// field, each repeatable group's rows, the page to show, a notice above the form, or the outcome. The surface action
/// of 6.2g fills it.
/// </summary>
internal sealed record FormPostState
{
    /// <summary>The visitor's answers by the field's key.</summary>
    public IReadOnlyDictionary<Guid, FieldAnswers> Answers { get; init; } = new Dictionary<Guid, FieldAnswers>();

    /// <summary>The errors by the field's key; a repeatable group's own error, such as too few rows, among them.</summary>
    public IReadOnlyDictionary<Guid, string> Errors { get; init; } = new Dictionary<Guid, string>();

    /// <summary>
    /// Each repeatable group's rows by the group's key, in the order the visitor saw them, each with its child fields'
    /// answers and errors (ADR-0098); a group without rows has none.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<FormPostRow>> Rows { get; init; } = new Dictionary<Guid, IReadOnlyList<FormPostRow>>();

    /// <summary>The page to show; null for the first page the rules show.</summary>
    public Guid? Page { get; init; }

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
    public IReadOnlyDictionary<Guid, string> Errors { get; init; } = new Dictionary<Guid, string>();
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
