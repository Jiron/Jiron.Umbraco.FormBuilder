using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>The forms that use an item of the library, as one user may see them (ADR-0214).</summary>
/// <param name="Forms">The forms the user may open, by name.</param>
/// <param name="Others">How many more forms use it that the user may not open, named nowhere.</param>
/// <param name="OtherUses">How many elements of those forms use it, in the version of each that uses it most.</param>
internal sealed record LibraryItemUsers(IReadOnlyList<LibraryUser> Forms, int Others, int OtherUses)
{
    /// <summary>How many forms use the item in all.</summary>
    public int Count => Forms.Count + Others;
}

/// <summary>
/// Which forms use an item of the library, for its "Used by" (ADR-0214, ADR-0215): those the user may open by name and
/// the rest counted, so a restricted form's name never shows to someone who may not see it. The option lists, themes and
/// email layouts share it.
/// </summary>
/// <param name="uses">What each form uses.</param>
/// <param name="forms">The forms, which say which of them a user may open.</param>
/// <param name="access">Who may see which forms.</param>
internal sealed class LibraryUsers(ILibraryUseRepository uses, IFormRepository forms, IFormBuilderAccessService access)
{
    /// <summary>The forms whose draft or live version uses the item, as the user may see them.</summary>
    /// <param name="user">The user asking.</param>
    /// <param name="kind">The item's kind.</param>
    /// <param name="key">The item's key.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<LibraryItemUsers> ForAsync(AccessUser user, LibraryUseKind kind, Guid key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await uses.UsersAsync(kind, key, cancellationToken);
        if (all.Count == 0)
        {
            return new LibraryItemUsers([], 0, 0);
        }

        var visibility = await access.GetFormVisibilityAsync(user, cancellationToken: cancellationToken);
        var seen = new HashSet<Guid>();
        if (visibility.IsEverything)
        {
            seen.UnionWith(all.Select(form => form.FormKey));
        }
        else if (!visibility.IsNothing)
        {
            foreach (var chunk in all.Select(form => form.FormKey).Chunk(Math.Min(SqlArgs.MaxListLength, PageRequest.MaxTake)))
            {
                var found = await forms.ListAsync(
                    new FormFilter { AllFolders = true, Trashed = null, Visibility = visibility, Keys = chunk },
                    new PageRequest(0, chunk.Length),
                    cancellationToken);
                seen.UnionWith(found.Items.Select(form => form.Key));
            }
        }

        var shown = all.Where(form => seen.Contains(form.FormKey)).ToList();
        return new LibraryItemUsers(shown, all.Count - shown.Count, all.Where(form => !seen.Contains(form.FormKey)).Sum(form => form.Uses));
    }
}
