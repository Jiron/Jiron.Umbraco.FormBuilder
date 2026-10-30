using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The marks that let the copy of an entry's answers show once (ADR-0123): the request that adds the entry's mark to
/// <c>jfbKeyValue</c> shows the copy, and no other on any server, since the store adds a key only when it is new. The
/// copy shows only while the entry is younger than a sent token's lifetime, so a mark older than that is useless, and
/// each new mark first deletes those; a mark is never older than its entry, so deleting it never lets a copy show twice.
/// </summary>
/// <param name="keyValues">The store of one-time flags.</param>
/// <param name="time">The site's clock.</param>
internal sealed class SentCopies(IKeyValueRepository keyValues, TimeProvider time)
{
    /// <summary>What the marks' keys start with, before the form's key and the submission id.</summary>
    public const string Prefix = "sent-copy:";

    /// <summary>
    /// True when this call took the entry's copy: the entry was submitted less than <see cref="SentTokens.Lifetime"/>
    /// ago and no call took its copy before.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The entry's submission id.</param>
    /// <param name="submitted">When the entry was submitted.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    public async Task<bool> TakeAsync(Guid form, Guid submissionId, DateTimeOffset submitted, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (now - submitted >= SentTokens.Lifetime)
        {
            return false;
        }

        await keyValues.DeleteOlderAsync(Prefix, (now - SentTokens.Lifetime).UtcDateTime, cancellationToken);
        return await keyValues.TryAddAsync(KeyOf(form, submissionId), null, cancellationToken);
    }

    /// <summary>The key of an entry's mark.</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The entry's submission id.</param>
    public static string KeyOf(Guid form, Guid submissionId) => Prefix + form.ToString("N") + ":" + submissionId.ToString("N");
}
