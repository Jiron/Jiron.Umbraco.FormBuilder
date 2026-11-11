using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>What an entry notification says of its entry (ROADMAP 8.4): never an answer, as Umbraco sends it as it is.</summary>
public abstract class EntryNotification : INotification
{
    /// <summary>The entry's key.</summary>
    public required Guid EntryKey { get; init; }

    /// <summary>The entry's number within its form.</summary>
    public required int EntryNumber { get; init; }

    /// <summary>The entry's state as entries store it.</summary>
    public required string State { get; init; }

    /// <summary>The form's alias.</summary>
    public required string FormAlias { get; init; }

    /// <summary>When it happened.</summary>
    public required DateTimeOffset OccurredUtc { get; init; }

    /// <summary>Whether the integrations settings have Umbraco's webhook events on.</summary>
    internal bool FiresWebhooks { get; init; }

    /// <summary>What Umbraco's webhooks send: a stable payload (docs/webhooks.md).</summary>
    public virtual object Payload() => new { form = new { alias = FormAlias }, entry = new { key = EntryKey, number = EntryNumber, state = State }, occurredUtc = OccurredUtc };
}

/// <summary>An entry was submitted, and did not go to Spam.</summary>
public sealed class EntrySubmittedNotification : EntryNotification;

/// <summary>An entry was approved.</summary>
public sealed class EntryApprovedNotification : EntryNotification;

/// <summary>An entry was rejected.</summary>
public sealed class EntryRejectedNotification : EntryNotification;

/// <summary>An entry was deleted.</summary>
public sealed class EntryDeletedNotification : EntryNotification;

/// <summary>An action of an entry failed for good.</summary>
public sealed class EntryActionFailedNotification : EntryNotification
{
    /// <summary>The action's type.</summary>
    public required string ActionType { get; init; }

    /// <summary>Why it failed, as a stable code.</summary>
    public required string? Code { get; init; }

    /// <inheritdoc />
    public override object Payload() => new { form = new { alias = FormAlias }, entry = new { key = EntryKey, number = EntryNumber, state = State }, occurredUtc = OccurredUtc, action = new { type = ActionType, code = Code } };
}

/// <summary>
/// An entry's state changed, and the change stands (section 4.9, ADR-0233): told once the undo window of a user's move
/// has passed, at once for a move without a user (an action, a site's code), and when an entry leaves Spam or is
/// verified. An entry that arrives is told of by <see cref="EntrySubmittedNotification"/>, not by this. It sets off no
/// webhook of its own; <see cref="EntryApprovedNotification"/> and <see cref="EntryRejectedNotification"/> follow it for
/// those states.
/// </summary>
public sealed class EntryStateChangedNotification : EntryNotification
{
    /// <summary>The state the entry left, as entries store it.</summary>
    public required string From { get; init; }
}

/// <summary>
/// Entries are about to be deleted for good (section 4.9, ADR-0233): by a user, by retention or by the privacy tools'
/// erase. Told before the delete, one notification for each entry, so a handler can still read the entry, for example
/// to archive it through <see cref="IFormBuilderEntryService"/>. It cannot stop the delete, as deletes keep the site's
/// retention and privacy promises; an entry that a legal hold keeps after all, or that another delete took first, gets no
/// <see cref="EntryDeletedNotification"/>.
/// </summary>
public sealed class EntryDeletingNotification : EntryNotification;

/// <summary>An error a handler of <see cref="EntrySubmittingNotification"/> adds, in its own words.</summary>
/// <param name="Field">The field's alias; null for an error at the form.</param>
/// <param name="Row">The row of a repeatable group's child, from 0; null for any other field and for the form.</param>
/// <param name="Message">What the visitor reads, in the entry's culture.</param>
public sealed record EntrySubmittingError(string? Field, int? Row, string Message);

/// <summary>
/// An entry is about to be stored (section 4.9, ADR-0233): published once its answers passed every check of the form
/// and the protection, before anything is written, for a visitor's entry, a preview's test entry and one a site's code
/// creates. A page's check and a saved draft do not raise it.
/// <para>
/// A handler refuses the entry with errors in its own words: <see cref="AddError"/> at a field, <see cref="AddFormError"/>
/// or <see cref="CancelableNotification.CancelOperation(EventMessage)"/> at the form. The visitor keeps every answer and
/// reads the errors where the form's own errors show (the public API answers <c>422 validation_failed</c> with the code
/// <c>custom</c>). A cancel without words shows the words of a check without its own message.
/// </para>
/// <para>
/// A handler that throws, or that holds up the visitor longer than five seconds, is logged, and the entry is stored
/// without its word: a site's failure never loses a visitor's entry. The answers are the visitor's; a handler keeps them
/// out of its logs (D13).
/// </para>
/// </summary>
public sealed class EntrySubmittingNotification : CancelableNotification
{
    private readonly List<EntrySubmittingError> _errors = [];

    /// <summary>Creates the notification.</summary>
    /// <param name="messages">Where a cancel's message goes.</param>
    public EntrySubmittingNotification(EventMessages messages)
        : base(messages)
    {
    }

    /// <summary>The form's key.</summary>
    public required Guid FormKey { get; init; }

    /// <summary>The form's alias.</summary>
    public required string FormAlias { get; init; }

    /// <summary>The definition of the published version the entry is submitted with.</summary>
    public required FormDefinition Form { get; init; }

    /// <summary>The number of that version.</summary>
    public required int Version { get; init; }

    /// <summary>The entry's culture, such as <c>en-US</c>, in which a handler words its errors.</summary>
    public required string Culture { get; init; }

    /// <summary>The answers as their types read them, with the fields the rules hid, as merge tags see them.</summary>
    public required MergeTagAnswers Answers { get; init; }

    /// <summary>The member signed in, or null.</summary>
    public Guid? Member { get; init; }

    /// <summary>The root content of the site the entry comes from, or null when none is known.</summary>
    public Guid? Site { get; init; }

    /// <summary>The content page the form was on, or null.</summary>
    public Guid? Page { get; init; }

    /// <summary>The context values the form lists, by name.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init => field = value ?? new Dictionary<string, string>(); } = new Dictionary<string, string>();

    /// <summary>Whether the protection sends the entry to Spam, where it sets off nothing.</summary>
    public bool IsSpam { get; init; }

    /// <summary>Whether the entry is a test entry of the preview.</summary>
    public bool IsTest { get; init; }

    /// <summary>Whether a site's own code creates the entry (<see cref="IFormBuilderEntryService.CreateAsync"/>).</summary>
    public bool IsFromCode { get; init; }

    /// <summary>The errors added so far, in the order they were added.</summary>
    public IReadOnlyList<EntrySubmittingError> Errors => _errors;

    /// <summary>The answer of the field with this alias, null for none, for one the rules hid and for no such field.</summary>
    /// <param name="fieldAlias">The field's alias.</param>
    /// <param name="row">The row of a repeatable group's child, from 0.</param>
    public MergeTagAnswer? AnswerOf(string fieldAlias, int row = 0) =>
        FieldOf(fieldAlias) is { } field && !Answers.Hidden.Contains(field.Key) ? Answers.Find(field.Key, row) : null;

    /// <summary>Refuses the entry with an error at a field.</summary>
    /// <param name="fieldAlias">The field's alias; a repeatable group's child by its own alias with <paramref name="row"/>.</param>
    /// <param name="message">What the visitor reads, in <see cref="Culture"/>.</param>
    /// <param name="row">The row of a repeatable group's child, from 0; null for any other field.</param>
    /// <exception cref="ArgumentException">The message is blank, the form has no field with this alias, or the row does not fit the field.</exception>
    public void AddError(string fieldAlias, string message, int? row = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var (field, group) = Find(fieldAlias) ?? throw new ArgumentException($"The form has no field '{fieldAlias}'.", nameof(fieldAlias));
        if ((group is null && row is not null) || (group is not null && row is not >= 0))
        {
            throw new ArgumentException(group is null
                ? $"The field '{fieldAlias}' is in no repeatable group, so its error has no row."
                : $"The field '{fieldAlias}' is in a repeatable group, so its error names its row, from 0.", nameof(row));
        }

        _errors.Add(new EntrySubmittingError(field.Alias, row, message.Trim()));
    }

    /// <summary>Refuses the entry with an error at the form.</summary>
    /// <param name="message">What the visitor reads, in <see cref="Culture"/>.</param>
    /// <exception cref="ArgumentException">The message is blank.</exception>
    public void AddFormError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _errors.Add(new EntrySubmittingError(null, null, message.Trim()));
    }

    private FormField? FieldOf(string alias) => Find(alias)?.Field;

    private (FormField Field, FormField? Group)? Find(string alias)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);
        foreach (var field in Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (string.Equals(field.Alias, alias, StringComparison.Ordinal))
            {
                return (field, null);
            }

            if (field.Fields.FirstOrDefault(child => string.Equals(child.Alias, alias, StringComparison.Ordinal)) is { } found)
            {
                return (found, field);
            }
        }

        return null;
    }
}
