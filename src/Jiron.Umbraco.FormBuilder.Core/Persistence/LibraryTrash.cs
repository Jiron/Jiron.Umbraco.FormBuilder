namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// The one check of the trash for forms and folders (ADR-0047, ADR-0053), for the services before a change and for the
/// repositories inside the write: only a restore, an unpublish or a delete applies to a form in the trash, only a
/// restore or a delete to a folder, nothing is copied from either, and only what is in the trash is deleted for good.
/// A restore whose landing changed after the caller checked it is refused in the same words wherever it is found.
/// </summary>
internal static class LibraryTrash
{
    /// <summary>Refuses a change to a form or folder in the trash (<c>trashed</c>).</summary>
    /// <param name="trashedUtc">When it went to the trash, or null.</param>
    /// <param name="item">What it is, for the message: <c>form</c> or <c>folder</c>.</param>
    /// <exception cref="FormBuilderConflictException">It is in the trash.</exception>
    public static void CheckOutside(DateTimeOffset? trashedUtc, string item)
    {
        if (trashedUtc is not null)
        {
            throw Trashed(item);
        }
    }

    /// <summary>Refuses the permanent delete of a form or folder outside the trash (<c>not_trashed</c>).</summary>
    /// <param name="trashedUtc">When it went to the trash, or null.</param>
    /// <param name="item">What it is, for the message: <c>form</c> or <c>folder</c>.</param>
    /// <exception cref="FormBuilderConflictException">It is outside the trash.</exception>
    public static void CheckInside(DateTimeOffset? trashedUtc, string item)
    {
        if (trashedUtc is null)
        {
            throw NotTrashed(item);
        }
    }

    /// <summary>The refusal of a change to a form or folder in the trash, for a write that finds it there.</summary>
    public static FormBuilderConflictException Trashed(string item) =>
        new(FormBuilderConflict.Trashed, $"The {Item(item)} is in the trash; restore it first.");

    /// <summary>The refusal of a permanent delete of a form or folder outside the trash, for a write that finds it there.</summary>
    public static FormBuilderConflictException NotTrashed(string item) =>
        new(FormBuilderConflict.NotTrashed, $"Only a {Item(item)} in the trash is deleted; put it in the trash first.");

    /// <summary>
    /// The refusal of a restore whose landing changed since the caller checked it (<c>landing_changed</c>): the folder
    /// the form or folder goes back into went to the trash or came back meanwhile.
    /// </summary>
    public static FormBuilderConflictException LandingChanged(string item) =>
        new(FormBuilderConflict.LandingChanged, $"Where the {Item(item)} lands changed since it was checked: the folder it goes back into went to the trash or came back meanwhile. Check again, then restore it.");

    private static string Item(string item)
    {
        ArgumentException.ThrowIfNullOrEmpty(item);
        return item;
    }
}
