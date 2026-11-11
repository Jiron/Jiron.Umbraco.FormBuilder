namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// Approved entries for a site's own pages (section 4.9, ADR-0233), such as a guestbook or a list of who signed up: what a
/// page may show of them, newest first. An entry shows once it is approved: by a reviewer, or as it arrives where the form
/// approves its entries automatically, so a form whose entries a page shows tells its visitors so. A page never gets an
/// entry's key, reference, member, address or files, nor the answers of sensitive fields, hidden fields, signatures and
/// consents, and test entries never show.
/// </summary>
public interface IFormBuilderEntryReader
{
    /// <summary>
    /// A page of a published form's approved entries, newest first, each answer as the entry's form version shows it in
    /// the culture asked for; null when the form is not published, in the trash or names no form.
    /// </summary>
    /// <param name="form">The form: its key, or its alias in any letter case.</param>
    /// <param name="request">The fields, the culture and where the page starts.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="ArgumentException">The request's page size or cursor cannot be read.</exception>
    Task<FormBuilderEntryReading?> ReadApprovedAsync(string form, FormBuilderEntryReadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Which answers of which approved entries a page shows.</summary>
public sealed record FormBuilderEntryReadRequest
{
    /// <summary>The most entries one page reads.</summary>
    public const int MaxTake = 100;

    /// <summary>
    /// The fields to show, by alias, in this order; null for every field a page may show, in the order of the form. A
    /// field a page may not show is left out.
    /// </summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>The culture the labels and options show in, such as <c>en-US</c>; null for each entry's own.</summary>
    public string? Culture { get; init; }

    /// <summary>How many entries: 1 to <see cref="MaxTake"/>.</summary>
    public int Take { get; init; } = 10;

    /// <summary>Where the page starts: the <see cref="FormBuilderEntryReading.Next"/> of the page before, or null for the first.</summary>
    public string? After { get; init; }
}

/// <summary>A page of approved entries.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="Next">Where the next page starts, or null on the last page.</param>
public sealed record FormBuilderEntryReading(IReadOnlyList<FormBuilderReadEntry> Entries, string? Next);

/// <summary>What a page shows of an approved entry.</summary>
/// <param name="Number">The entry's number within its form.</param>
/// <param name="SubmittedUtc">When it was submitted.</param>
/// <param name="Answers">The answers asked for that it has, in the order asked for.</param>
public sealed record FormBuilderReadEntry(int Number, DateTimeOffset SubmittedUtc, IReadOnlyList<FormBuilderReadAnswer> Answers);

/// <summary>An answer as a page shows it.</summary>
/// <param name="Field">The field's alias.</param>
/// <param name="Label">The field's label in the culture asked for.</param>
/// <param name="Texts">The answer as its type shows it: an option's label, a date in the culture's format; several for a choice of several.</param>
/// <param name="Row">The row of a repeatable group's child, from 0; null for any other field.</param>
public sealed record FormBuilderReadAnswer(string Field, string Label, IReadOnlyList<string> Texts, int? Row = null);
