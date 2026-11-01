using System.Net;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// An extra spam signal of a site's own (ADR-0012, ADR-0140), registered through
/// <c>builder.JironFormBuilder().SpamChecks()</c>. It adds its score to a submission's; at 100 or more the submission goes
/// to Spam. A check that throws or takes longer than 5 seconds counts for nothing, so a broken check never loses an entry,
/// and a check is not asked once the score has already decided. Each check is a singleton, created by the container.
/// </summary>
public interface IFormBuilderSpamCheck
{
    /// <summary>The check's signal for a submission, or null when it has nothing against it.</summary>
    /// <param name="context">The submission.</param>
    /// <param name="cancellationToken">Cancelled when the submission is, or when the check has taken too long.</param>
    Task<FormBuilderSpamSignal?> CheckAsync(FormBuilderSpamCheckContext context, CancellationToken cancellationToken);
}

/// <summary>What a spam check found.</summary>
/// <param name="Score">What it adds to the submission's score, from 0 to 1,000; anything outside is held to that.</param>
/// <param name="Reason">
/// Why, as the entry stores it: lower-case ASCII letters, digits, <c>_</c> and <c>-</c>, up to 64 characters, such as
/// <c>known_sender</c>. Any other reason is stored as <c>spam_check</c>.
/// </param>
public sealed record FormBuilderSpamSignal(int Score, string Reason);

/// <summary>A submission as a spam check sees it.</summary>
public sealed record FormBuilderSpamCheckContext
{
    /// <summary>The form's key.</summary>
    public required Guid Form { get; init; }

    /// <summary>The culture the page was in.</summary>
    public required string Culture { get; init; }

    /// <summary>The content page the form was on, or null.</summary>
    public Guid? Page { get; init; }

    /// <summary>The texts of the answers by their field's alias; a repeatable group's rows follow one another.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Answers { get; init; }

    /// <summary>The request's address as the host tells it, or null. Use it for this check alone; the entry does not keep it from here.</summary>
    public IPAddress? Address { get; init; }

    /// <summary>Whether a server key sent the submission, so no visitor's browser stands behind it.</summary>
    public bool IsFromServerKey { get; init; }
}

/// <summary>The spam checks a site has, in the order they are asked (ADR-0140).</summary>
public sealed class FormBuilderSpamCheckCollection : BuilderCollectionBase<IFormBuilderSpamCheck>
{
    /// <summary>A collection of the checks the builder creates.</summary>
    public FormBuilderSpamCheckCollection(Func<IEnumerable<IFormBuilderSpamCheck>> items)
        : base(items)
    {
    }
}

/// <summary>The ordered collection of spam checks (ADR-0140), reached through <c>builder.JironFormBuilder().SpamChecks()</c>; it starts empty.</summary>
public sealed class FormBuilderSpamCheckCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderSpamCheckCollectionBuilder, FormBuilderSpamCheckCollection, IFormBuilderSpamCheck>
{
    /// <inheritdoc />
    protected override FormBuilderSpamCheckCollectionBuilder This => this;
}
