using Jiron.Umbraco.FormBuilder.Core.Deliveries;
using Jiron.Umbraco.FormBuilder.Core.Persistence;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Activity;

/// <summary>A page of the deliveries log, and whether the host pauses sending (ADR-0219).</summary>
internal sealed record DeliveryListResponseModel
{
    /// <summary>The page's deliveries, problems first, each group the newest first.</summary>
    public required IReadOnlyList<DeliveryResponseModel> Items { get; init; }

    /// <summary>How many deliveries match.</summary>
    public required long Total { get; init; }

    /// <summary>Whether the host pauses actions and email (<c>JironFormBuilder:Actions:Paused</c>), so nothing goes out.</summary>
    public required bool Paused { get; init; }
}

/// <summary>A mail, or a run that queued none, as the deliveries log shows it: never a subject, a body or an answer.</summary>
internal sealed record DeliveryResponseModel
{
    /// <summary><c>email</c> or <c>run</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The mail's or the run's id; the two are counted apart.</summary>
    public required long Id { get; init; }

    /// <summary>
    /// <c>waiting</c>, <c>sending</c>, <c>tryingAgain</c>, <c>delivered</c>, <c>failed</c>, <c>uncertain</c>, <c>limited</c>
    /// or <c>skipped</c>.
    /// </summary>
    public required string State { get; init; }

    /// <summary>What a mail is for (<c>action</c>, <c>resume</c>, <c>verification</c>, <c>test</c>, ...); null for a run.</summary>
    public string? EmailKind { get; init; }

    /// <summary>A mail's recipients; empty for a run, and for a recipient that stays hidden.</summary>
    public required IReadOnlyList<string> Recipients { get; init; }

    /// <summary>
    /// Whether the recipient is an address a visitor typed, which stays hidden from a user who may not see the form's
    /// sensitive answers.
    /// </summary>
    public required bool RecipientsHidden { get; init; }

    /// <summary>The action type's alias of the run, or of the run that queued the mail.</summary>
    public string? ActionType { get; init; }

    /// <summary>The action's name in the version that queued it; null where it has none.</summary>
    public string? ActionName { get; init; }

    /// <summary>The action type's name, or a <c>#key</c> of the backoffice's words.</summary>
    public string? TypeName { get; init; }

    /// <summary>Where a webhook run went; null for anything else.</summary>
    public DeliveryTargetResponseModel? Target { get; init; }

    /// <summary>The form of its entry.</summary>
    public Guid? FormKey { get; init; }

    /// <summary>The form's name.</summary>
    public string? FormName { get; init; }

    /// <summary>Its entry; null for a mail of no entry, or one whose entry is gone.</summary>
    public Guid? EntryKey { get; init; }

    /// <summary>The entry's number.</summary>
    public long? EntryNumber { get; init; }

    /// <summary>The attempts claimed so far.</summary>
    public required int Attempts { get; init; }

    /// <summary>When it is tried next; null once it is final.</summary>
    public DateTimeOffset? NextAttemptUtc { get; init; }

    /// <summary>Why its last attempt did not succeed, as a code, or which limit held a mail back.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Why, in the server's words, without personal data.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When it was queued.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>When a run's first attempt started, since a retry from the log.</summary>
    public DateTimeOffset? FirstAttemptUtc { get; init; }

    /// <summary>When a run's latest attempt started.</summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>When it reached a final state.</summary>
    public DateTimeOffset? FinishedUtc { get; init; }

    /// <summary>The model of an item of the log.</summary>
    /// <param name="item">The item.</param>
    /// <param name="hidesRecipients">Whether its recipient stays hidden from the user.</param>
    public static DeliveryResponseModel Of(DeliveryItem item, bool hidesRecipients = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        var row = item.Row;
        return new DeliveryResponseModel
        {
            Kind = row.IsEmail ? DeliveryKindNames.Email : DeliveryKindNames.Run,
            Id = row.Id,
            State = StoredNames.Of(row.State),
            EmailKind = row.EmailKind is { } kind ? StoredNames.Of(kind) : null,
            Recipients = hidesRecipients ? [] : row.Recipients?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            RecipientsHidden = hidesRecipients,
            ActionType = row.ActionType,
            ActionName = item.ActionName,
            TypeName = item.TypeName,
            Target = item.Target is { } target ? new DeliveryTargetResponseModel { Kind = StoredNames.Of(target.Kind), Text = target.Text } : null,
            FormKey = row.FormKey,
            FormName = row.FormName,
            EntryKey = row.EntryKey,
            EntryNumber = row.EntryNumber,
            Attempts = row.Attempts,
            NextAttemptUtc = row.NextAttemptUtc,
            ErrorCode = row.Error?.Code,
            ErrorMessage = row.Error?.Message,
            CreatedUtc = row.CreatedUtc,
            FirstAttemptUtc = row.StartedUtc,
            LastAttemptUtc = row.LastAttemptUtc,
            FinishedUtc = row.FinishedUtc,
        };
    }
}

/// <summary>The names of the two kinds of row, as the routes and the answers write them.</summary>
internal static class DeliveryKindNames
{
    /// <summary>A mail.</summary>
    public const string Email = "email";

    /// <summary>A run of an action.</summary>
    public const string Run = "run";
}

/// <summary>Where a webhook run went, never the address of a reference.</summary>
internal sealed record DeliveryTargetResponseModel
{
    /// <summary><c>host</c>, <c>reference</c>, <c>slack</c> or <c>teams</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The host, or the configuration key of the reference; null for a chat's address in the integrations settings.</summary>
    public string? Text { get; init; }
}

/// <summary>A delivery's details: the delivery, whether this user may retry it, and the limit that held a mail back.</summary>
internal sealed record DeliveryDetailResponseModel
{
    /// <summary>The delivery.</summary>
    public required DeliveryResponseModel Delivery { get; init; }

    /// <summary>
    /// Whether it can be retried, as it failed or is uncertain, and this user may: <c>manageActions</c> on its form, or for a
    /// mail of no entry, <c>manageSettings</c>, which reading it takes already (ADR-0013, ADR-0219).
    /// </summary>
    public required bool MayRetry { get; init; }

    /// <summary>Whether it failed or is uncertain, so that someone with <c>manageActions</c> on its form could retry it.</summary>
    public required bool Retryable { get; init; }

    /// <summary>For a limited mail, the limit that held it back; null otherwise.</summary>
    public DeliveryLimitResponseModel? Limit { get; init; }
}

/// <summary>The limit of visitor mail that held a mail back (ADR-0012, ADR-0159).</summary>
internal sealed record DeliveryLimitResponseModel
{
    /// <summary><c>recipient</c>: mails to one address in 24 hours; <c>site</c>: mails to visitor addresses in an hour.</summary>
    public required string Kind { get; init; }

    /// <summary>The limit the host sets now.</summary>
    public required int Count { get; init; }

    /// <summary>The configuration key that sets it.</summary>
    public required string Key { get; init; }
}
