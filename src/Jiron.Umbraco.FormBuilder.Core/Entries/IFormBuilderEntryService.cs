namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// A form's entries for a site's own server code (section 4.9, ADR-0233): read them, create one as a visitor's post would,
/// and change an entry's state. It is the site's code, so it reads every answer, sensitive ones too, and checks no user's
/// permissions: code that serves a user checks theirs first. Drafts are no entries here.
/// </summary>
public interface IFormBuilderEntryService
{
    /// <summary>An entry with its answers; null when there is none with this key, and for a draft.</summary>
    /// <param name="entryKey">The entry's key.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    Task<FormBuilderEntry?> GetAsync(Guid entryKey, CancellationToken cancellationToken = default);

    /// <summary>A page of a form's entries, newest first; null when the form does not exist.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="query">Which entries, and where the page starts.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="ArgumentException">The query's page size or cursor cannot be read.</exception>
    Task<FormBuilderEntryPage?> ListAsync(Guid formKey, FormBuilderEntryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an entry through the submission pipeline, as a headless server's post with its server key would: the form's
    /// published version reads and checks the answers, its rules, places and unique answers hold, its actions run and its
    /// outcome is decided. No challenge, honeypot or captcha is asked of it, and no address is kept. A form for members
    /// takes it when <see cref="FormBuilderNewEntry.Member"/> names the member. The same submission id with the same
    /// answers answers the entry it made again.
    /// </summary>
    /// <param name="entry">The form, the answers and what comes with them.</param>
    /// <param name="cancellationToken">Cancels the submission before it is stored.</param>
    Task<FormBuilderEntryResult> CreateAsync(FormBuilderNewEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an entry to another state as an action does, without a user: at once, without an undo window, with the actions
    /// that state sets off. An entry moves to submitted, approved, rejected or Spam; from Spam to submitted it is not spam
    /// after all, and its actions run. False when there is no such entry, it is a draft or unverified, it is in that state
    /// already, or another change moved it first.
    /// </summary>
    /// <param name="entryKey">The entry's key.</param>
    /// <param name="state">The state: <see cref="FormBuilderEntryState.Submitted"/>, <see cref="FormBuilderEntryState.Approved"/>,
    /// <see cref="FormBuilderEntryState.Rejected"/> or <see cref="FormBuilderEntryState.Spam"/>.</param>
    /// <param name="cancellationToken">Cancels the change before it is written.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is unverified or not a state.</exception>
    Task<bool> ChangeStateAsync(Guid entryKey, FormBuilderEntryState state, CancellationToken cancellationToken = default);
}

/// <summary>An entry's state (section 4.4).</summary>
public enum FormBuilderEntryState
{
    /// <summary>Waiting for the visitor to verify the email address.</summary>
    Unverified,

    /// <summary>Submitted; the state an entry arrives in.</summary>
    Submitted,

    /// <summary>Approved.</summary>
    Approved,

    /// <summary>Rejected.</summary>
    Rejected,

    /// <summary>In Spam, where it sets off nothing.</summary>
    Spam,
}

/// <summary>An entry, with its answers.</summary>
public sealed record FormBuilderEntry
{
    /// <summary>The entry's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The form's key.</summary>
    public required Guid FormKey { get; init; }

    /// <summary>The form's alias.</summary>
    public required string FormAlias { get; init; }

    /// <summary>The entry's number within its form, from 1.</summary>
    public required int Number { get; init; }

    /// <summary>The entry's reference in its form's format, such as <c>REQ-2026-00042</c>.</summary>
    public required string Reference { get; init; }

    /// <summary>The entry's state.</summary>
    public required FormBuilderEntryState State { get; init; }

    /// <summary>When it was submitted.</summary>
    public required DateTimeOffset SubmittedUtc { get; init; }

    /// <summary>The culture it was submitted in, such as <c>en-US</c>.</summary>
    public required string Culture { get; init; }

    /// <summary>Whether it is a test entry of the preview.</summary>
    public bool IsTest { get; init; }

    /// <summary>The member who submitted it, or null.</summary>
    public Guid? Member { get; init; }

    /// <summary>The root content of the site it came from, or null when none is known.</summary>
    public Guid? Site { get; init; }

    /// <summary>The content page the form was on, or null.</summary>
    public Guid? Page { get; init; }

    /// <summary>The answers, in the order of the form and then by row; a field without values has none.</summary>
    public required IReadOnlyList<FormBuilderEntryAnswer> Answers { get; init; }
}

/// <summary>A field's answer in an entry, in one row.</summary>
public sealed record FormBuilderEntryAnswer
{
    /// <summary>The field's key; a grid's for the values of its rows.</summary>
    public required Guid FieldKey { get; init; }

    /// <summary>The field's alias.</summary>
    public required string Field { get; init; }

    /// <summary>The field type's alias.</summary>
    public required string Type { get; init; }

    /// <summary>The row of a repeatable group's child, from 0; null for any other field.</summary>
    public int? Row { get; init; }

    /// <summary>
    /// The values as the field's type read them, in the wire form (ADR-0058): a choice's option values, a date as
    /// <c>yyyy-MM-dd</c>, a value of parts as its JSON object, a file's key.
    /// </summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>For a grid, the key of the row each value answers, one for each value; none for any other field.</summary>
    public IReadOnlyList<Guid> GridRows { get; init; } = [];

    /// <summary>The files a field that stores files holds, one for each value; none for any other field.</summary>
    public IReadOnlyList<FormBuilderEntryFile> Files { get; init; } = [];

    /// <summary>Whether the field is marked sensitive, so a site keeps its values out of logs and lists.</summary>
    public bool IsSensitive { get; init; }
}

/// <summary>A file of an entry.</summary>
/// <param name="Key">The file's key.</param>
/// <param name="Name">The name the visitor's file had.</param>
/// <param name="SizeBytes">Its size.</param>
public sealed record FormBuilderEntryFile(Guid Key, string Name, long SizeBytes);

/// <summary>Which of a form's entries a page lists, newest first.</summary>
public sealed record FormBuilderEntryQuery
{
    /// <summary>The most entries one page lists.</summary>
    public const int MaxTake = 100;

    /// <summary>The states listed; none lists every state.</summary>
    public IReadOnlyCollection<FormBuilderEntryState> States { get; init; } = [];

    /// <summary>The earliest submission time listed, or null.</summary>
    public DateTimeOffset? SubmittedFrom { get; init; }

    /// <summary>The submission time before which entries are listed, or null.</summary>
    public DateTimeOffset? SubmittedBefore { get; init; }

    /// <summary>Whether the preview's test entries are listed too.</summary>
    public bool IncludeTest { get; init; }

    /// <summary>How many entries the page lists: 1 to <see cref="MaxTake"/>.</summary>
    public int Take { get; init; } = 25;

    /// <summary>Where the page starts: the <see cref="FormBuilderEntryPage.Next"/> of the page before, or null for the first.</summary>
    public string? After { get; init; }
}

/// <summary>A page of entries.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="Next">Where the next page starts, or null on the last page.</param>
public sealed record FormBuilderEntryPage(IReadOnlyList<FormBuilderEntry> Entries, string? Next);

/// <summary>An answer for a new entry: its values, or the parts of a name or an address.</summary>
public sealed record FormBuilderNewAnswer
{
    /// <summary>An answer of values.</summary>
    /// <param name="values">The values, as a visitor's post sends them: one for most fields, several for a choice of several.</param>
    public FormBuilderNewAnswer(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    /// <summary>The values.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>The parts of a name or an address by their names, such as <c>first</c> and <c>last</c>; null for none.</summary>
    public IReadOnlyDictionary<string, string>? Parts { get; init; }
}

/// <summary>A new entry for <see cref="IFormBuilderEntryService.CreateAsync"/>.</summary>
public sealed record FormBuilderNewEntry
{
    /// <summary>The form's key.</summary>
    public required Guid Form { get; init; }

    /// <summary>
    /// The answers of the fields outside repeatable groups, by alias. A field that stores files takes none from code, as
    /// its files come only as a visitor's uploads.
    /// </summary>
    public IReadOnlyDictionary<string, FormBuilderNewAnswer> Answers { get; init; } = new Dictionary<string, FormBuilderNewAnswer>();

    /// <summary>Each repeatable group's rows by the group's alias, every row its children's answers by alias.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, FormBuilderNewAnswer>>> Rows { get; init; } =
        new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, FormBuilderNewAnswer>>>();

    /// <summary>The culture, such as <c>en-US</c>, held to the site's languages; null for the default language.</summary>
    public string? Culture { get; init; }

    /// <summary>The content page the entry belongs to, which names its site; null for none.</summary>
    public Guid? Page { get; init; }

    /// <summary>Context values by name; only those the form lists are stored.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();

    /// <summary>The member the entry is for, or null.</summary>
    public Guid? Member { get; init; }

    /// <summary>The submission's id, which makes a retry safe; null for the service to make one.</summary>
    public Guid? SubmissionId { get; init; }
}

/// <summary>What <see cref="IFormBuilderEntryService.CreateAsync"/> answers: the entry, or why there is none.</summary>
public sealed record FormBuilderEntryResult
{
    /// <summary>The entry; null when it was refused.</summary>
    public FormBuilderEntry? Entry { get; init; }

    /// <summary>Whether it is the entry the same submission id made before.</summary>
    public bool Replayed { get; init; }

    /// <summary>
    /// Why it was refused, as the public API's codes name it (<c>validation_failed</c>, <c>form_not_found</c>,
    /// <c>form_closed</c>, <c>entry_limit_reached</c>, ...); null for an entry.
    /// </summary>
    public string? Refusal { get; init; }

    /// <summary>The errors of <c>validation_failed</c> and <c>invalid_request</c>.</summary>
    public IReadOnlyList<FormBuilderEntryProblem> Problems { get; init; } = [];
}

/// <summary>An error of a refused entry, where the public API names it.</summary>
/// <param name="Code">The field's kind of error (<c>required</c>, <c>maxLength</c>, ...), <c>check</c>, <c>rule</c>, <c>custom</c> or <c>invalid_value</c>.</param>
/// <param name="Path">The field's alias, <c>group[row].child</c> for a repeatable group's child, or <c>""</c> for the form.</param>
/// <param name="Text">For <c>custom</c>, the words a handler gave it; otherwise null.</param>
public sealed record FormBuilderEntryProblem(string Code, string Path, string? Text = null);
