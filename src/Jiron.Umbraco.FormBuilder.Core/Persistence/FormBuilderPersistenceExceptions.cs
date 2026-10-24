namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Thrown when a write expected a version that changed meanwhile (optimistic concurrency, ADR-0002): someone else saved
/// first, and nothing was written. The caller reloads and decides again; the API answers <c>409 concurrency_conflict</c>.
/// </summary>
internal sealed class FormBuilderConcurrencyException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FormBuilderConcurrencyException()
        : base("The item was changed by someone else meanwhile.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public FormBuilderConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public FormBuilderConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Why the database refused a change (ADR-0036). Each becomes one error code of the API.</summary>
internal enum FormBuilderConflict
{
    /// <summary>Another row already has this alias, in any letter case (ADR-0033).</summary>
    DuplicateAlias,

    /// <summary>Another row already has this key.</summary>
    DuplicateKey,

    /// <summary>The folder the change refers to does not exist (or no longer), or is in the trash.</summary>
    MissingFolder,

    /// <summary>A folder would move into itself or into a folder below it.</summary>
    FolderIntoItself,

    /// <summary>A folder would end up deeper than <c>FolderRepository.MaxDepth</c> levels.</summary>
    FolderTooDeep,

    /// <summary>A folder still holds folders or forms.</summary>
    FolderNotEmpty,

    /// <summary>
    /// A form, a version, a catalogue field, a theme or a file is still in use: entries, values or published forms point
    /// at it.
    /// </summary>
    InUse,

    /// <summary>The form already has an entry with this submission id (section 4.4): the earlier entry answers the retry.</summary>
    DuplicateSubmission,

    /// <summary>The challenge already made another entry of the form; a challenge makes one entry (ADR-0012).</summary>
    ChallengeUsed,

    /// <summary>
    /// A file the entry names does not exist, belongs to another form, or belongs to another entry already (ADR-0006).
    /// </summary>
    MissingFile,

    /// <summary>A value names a field that is not in the catalogue of the entry's form.</summary>
    MissingField,

    /// <summary>The form version an entry names does not exist or belongs to another form.</summary>
    MissingVersion,

    /// <summary>The form is in the trash: only a restore, an unpublish or a delete applies to it (ADR-0044).</summary>
    Trashed,
}

/// <summary>
/// Thrown when the database refused a change because of the state of other rows (<see cref="Conflict"/> says which);
/// nothing was written. Missing rows are not conflicts: the repositories answer them with null or false.
/// </summary>
internal sealed class FormBuilderConflictException : Exception
{
    /// <summary>Creates the exception for <paramref name="conflict"/>.</summary>
    public FormBuilderConflictException(FormBuilderConflict conflict, string message)
        : base(message)
    {
        Conflict = conflict;
    }

    /// <summary>Creates the exception for <paramref name="conflict"/> with its cause, for example a unique index.</summary>
    public FormBuilderConflictException(FormBuilderConflict conflict, string message, Exception innerException)
        : base(message, innerException)
    {
        Conflict = conflict;
    }

    /// <summary>Why the change was refused.</summary>
    public FormBuilderConflict Conflict { get; }
}

/// <summary>
/// Thrown when a read ran past its time budget and was stopped (ADR-0003, ADR-0039); the API answers
/// <c>query_timeout</c>. The read ends without a result and is never run again; the gate is free for the next unit at once.
/// </summary>
internal sealed class FormBuilderQueryTimeoutException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FormBuilderQueryTimeoutException()
        : base("The query ran past its time budget and was stopped.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public FormBuilderQueryTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause: the database's error for the stopped statement.</summary>
    public FormBuilderQueryTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
