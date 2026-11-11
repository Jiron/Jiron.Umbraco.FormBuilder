using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
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

    // The longest instance id a post names: the prefix and a count no page reaches.
    private const int LongestId = 16;

    /// <summary>The next instance id of the request.</summary>
    /// <param name="httpContext">The request.</param>
    public static string Next(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var next = (httpContext.Items[Count] as int? ?? 0) + 1;
        httpContext.Items[Count] = next;
        return Prefix + next.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether a text is an instance id as the render gives it: the prefix and a whole number from 1 without a leading
    /// zero, no longer than a page reaches.
    /// </summary>
    /// <param name="instance">The text.</param>
    public static bool IsId(string instance) =>
        instance.Length <= LongestId && instance.StartsWith(Prefix, StringComparison.Ordinal) && instance.Length > Prefix.Length
        && instance[Prefix.Length] != '0' && !instance.AsSpan(Prefix.Length).ContainsAnyExceptInRange('0', '9');
}

/// <summary>
/// Where the render endpoint renders a form (ADR-0104): the page's instance id, its path, its content and its culture, in
/// place of the request's, so the markup is the page's.
/// </summary>
/// <param name="Instance">The instance id the page gave the form.</param>
/// <param name="Page">The page's path as the browser has it, which the form posts to without JavaScript.</param>
/// <param name="Content">The page's content, or null.</param>
/// <param name="Culture">The page's culture as asked for, or null for the default language.</param>
/// <param name="Theme">The theme the page's placement names, or null for the form's own (ADR-0105).</param>
/// <param name="Redirect">The placement's page after a send, from the page's token (ADR-0129), or null.</param>
/// <param name="Preview">
/// The version of the form the preview renders, as though it were published (ADR-0203), for a test member; null for the
/// published form.
/// </param>
internal sealed record FormRenderTarget(string Instance, string Page, Guid? Content, string? Culture, string? Theme = null, Guid? Redirect = null, int? Preview = null);

/// <summary>The target of a request to the render endpoint, which the view component renders for.</summary>
internal static class FormRenderTargets
{
    // The key under which a request holds its target.
    private const string Key = "Jiron.FormBuilder.Rendering.Target";

    /// <summary>Sets the request's target.</summary>
    public static void Set(HttpContext httpContext, FormRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(target);
        httpContext.Items[Key] = target;
    }

    /// <summary>The request's target, or null for a page.</summary>
    public static FormRenderTarget? Get(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(Key, out var target) ? target as FormRenderTarget : null;
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
/// <param name="Html">
/// The message, sanitised; null for the thank-you in the page's words, a site's or the form's own among them (ADR-0230).
/// </param>
/// <param name="Reference">The entry's reference, or null.</param>
/// <param name="IsPending">Whether the visitor must still confirm their email address.</param>
/// <param name="ContinueUrl">The redirect after the message, which the visitor follows by a link (ADR-0100); null for none.</param>
/// <param name="Copy">The copy of the answers below a message that offers one (ADR-0123); null for a message that does not.</param>
internal sealed record FormPostOutcome(string? Html, string? Reference, bool IsPending, string? ContinueUrl = null, FormPostCopy? Copy = null);

/// <summary>The copy of the answers below an outcome (ADR-0123).</summary>
/// <param name="Entry">The entry's answers, on the one view that shows them; null on any later view, which says so.</param>
internal sealed record FormPostCopy(EntryCopy? Entry);

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

    /// <summary>
    /// The first page that holds an error the post left, which shows, unless the posted page is the summary page, which
    /// shows the errors of the pages before it in its rows (ADR-0122); null when the post left no error at a field.
    /// </summary>
    public Guid? ErrorPage { get; init; }

    /// <summary>
    /// Each ranking's order by the field's key, as the page showed it or as a move left it (ADR-0100), which places the
    /// options a ranking of the top few leaves unranked.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> Orders { get; init; } = new Dictionary<Guid, IReadOnlyList<string>>();

    /// <summary>The stored files each file field's answer holds by the field's key, which the page offers to keep (ADR-0102).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<FormPostFile>> Files { get; init; } = new Dictionary<Guid, IReadOnlyList<FormPostFile>>();

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

    /// <summary>Each ranking's order in the row by the child field's key, as <see cref="FormPostState.Orders"/>.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> Orders { get; init; } = new Dictionary<Guid, IReadOnlyList<string>>();

    /// <summary>The stored files of the row's file fields by the child field's key, as <see cref="FormPostState.Files"/>.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<FormPostFile>> Files { get; init; } = new Dictionary<Guid, IReadOnlyList<FormPostFile>>();

    /// <summary>Whether the visitor just added the row (ADR-0100): its fields start as on a first render, defaults and all.</summary>
    public bool IsNew { get; init; }
}

/// <summary>A stored file a post kept for its field (ADR-0102): the upload token the page posts back, and the file's name and size.</summary>
/// <param name="Token">The upload token (ADR-0087).</param>
/// <param name="Name">The visitor's file name.</param>
/// <param name="Size">Its size in bytes.</param>
internal sealed record FormPostFile(string Token, string Name, long Size);

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
