using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Deliveries;

/// <summary>Where a webhook run went, as the deliveries log names it without reaching a secret (ADR-0219).</summary>
internal enum DeliveryTargetKind
{
    /// <summary>The host of the action's own address.</summary>
    Host,

    /// <summary>The configuration key of a reference that holds the address.</summary>
    Reference,

    /// <summary>Slack: the site's address, or a reference's key.</summary>
    Slack,

    /// <summary>Microsoft Teams: the site's address, or a reference's key.</summary>
    Teams,
}

/// <summary>Where a webhook run went.</summary>
/// <param name="Kind">How it is named.</param>
/// <param name="Text">The host or the key; null for the integrations settings' address of a chat.</param>
internal sealed record DeliveryTarget(DeliveryTargetKind Kind, string? Text);

/// <summary>A row of the deliveries log with what its form's version says of its action.</summary>
/// <param name="Row">The row.</param>
/// <param name="ActionName">The action's name in the version that queued it; null where it has none, or for mail of no action.</param>
/// <param name="TypeName">The action type's name, or a <c>#key</c> of the backoffice's words; null where the site has no such type.</param>
/// <param name="Target">Where a webhook run went; null for anything else.</param>
internal sealed record DeliveryItem(DeliveryRow Row, string? ActionName, string? TypeName, DeliveryTarget? Target);

/// <summary>What a retry from the deliveries log came to.</summary>
/// <param name="Status">How it came out.</param>
/// <param name="Item">The delivery as it is now; null unless it exists.</param>
internal sealed record DeliveryRetry(ActionRunRetryStatus Status, DeliveryItem? Item);

/// <summary>
/// The deliveries log (section 4.4, ADR-0219): every mail and every run that queued none, with what the version says of its
/// action, and a retry of a failed or uncertain one. A run's retry is the entry's (<see cref="ActionRunRetries"/>); a mail's
/// sets it queued and due, audited as <see cref="EmailRetriedAction"/>. Who may do either is the endpoints' to check.
/// </summary>
internal sealed class DeliveryLog(
    FormBuilderDatabase database,
    IDeliveryRepository deliveries,
    IEmailRepository emails,
    IFormRepository forms,
    IAuditRepository audit,
    FormDocuments documents,
    ActionTypeRegistry types,
    ActionSettingLocks locks,
    ActionRunRetries runs,
    IOptionsMonitor<FormBuilderOptions> options)
{
    /// <summary>The audit action of a mail's retry; the details name the mail, its entry and whether it was uncertain.</summary>
    public const string EmailRetriedAction = "email.retried";

    /// <summary>The audit entity type of a mail of no entry; the item is null, as a mail has no key.</summary>
    public const string EmailEntityType = "email";

    /// <summary>Whether the host pauses actions and email (<c>JironFormBuilder:Actions:Paused</c>).</summary>
    public bool Paused => options.CurrentValue.Actions.Paused;

    /// <summary>The limits of visitor mail the host sets.</summary>
    public FormBuilderMailLimitsOptions MailLimits => options.CurrentValue.Limits.Mail;

    /// <summary>One page of the log.</summary>
    public async Task<Page<DeliveryItem>> QueryAsync(DeliveryQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await deliveries.QueryAsync(query, page, cancellationToken);
        var versions = new Dictionary<int, FormDefinition?>();
        var items = new List<DeliveryItem>(rows.Items.Count);
        foreach (var row in rows.Items)
        {
            items.Add(await ItemAsync(row, versions, cancellationToken));
        }

        return new Page<DeliveryItem>(items, rows.Total);
    }

    /// <summary>A mail of the log, or null when it does not exist.</summary>
    public async Task<DeliveryItem?> GetEmailAsync(long emailId, CancellationToken cancellationToken = default) =>
        await deliveries.GetEmailAsync(emailId, cancellationToken) is { } row ? await ItemAsync(row, [], cancellationToken) : null;

    /// <summary>A run of the log, or null when it does not exist.</summary>
    public async Task<DeliveryItem?> GetRunAsync(long runId, CancellationToken cancellationToken = default) =>
        await deliveries.GetRunAsync(runId, cancellationToken) is { } row ? await ItemAsync(row, [], cancellationToken) : null;

    /// <summary>Queues a failed or uncertain run again, as the entry's retry does.</summary>
    /// <exception cref="FormBuilderConcurrencyException">The run's state changed between the read and the retry.</exception>
    public async Task<DeliveryRetry> RetryRunAsync(long runId, bool confirmed, Guid userKey, CancellationToken cancellationToken = default)
    {
        if (await deliveries.GetRunAsync(runId, cancellationToken) is not { FormKey: { } formKey, EntryKey: { } entryKey })
        {
            return new DeliveryRetry(ActionRunRetryStatus.NotFound, null);
        }

        var retry = await runs.RetryAsync(formKey, entryKey, runId, confirmed, userKey, cancellationToken);
        return new DeliveryRetry(retry.Status, await GetRunAsync(runId, cancellationToken));
    }

    /// <summary>Queues a failed or uncertain mail again, due now with its attempts at 0; an uncertain one only when confirmed.</summary>
    /// <exception cref="FormBuilderConcurrencyException">The mail's state changed between the read and the retry.</exception>
    public async Task<DeliveryRetry> RetryEmailAsync(long emailId, bool confirmed, Guid userKey, CancellationToken cancellationToken = default)
    {
        if (await deliveries.GetEmailAsync(emailId, cancellationToken) is not { } row || await emails.GetAsync(emailId, cancellationToken) is not { } mail)
        {
            return new DeliveryRetry(ActionRunRetryStatus.NotFound, null);
        }

        if (mail.State == EmailState.Uncertain && !confirmed)
        {
            return new DeliveryRetry(ActionRunRetryStatus.NeedsConfirmation, await ItemAsync(row, [], cancellationToken));
        }

        if (mail.State is not (EmailState.Failed or EmailState.Uncertain))
        {
            return new DeliveryRetry(ActionRunRetryStatus.NotRetryable, await ItemAsync(row, [], cancellationToken));
        }

        var requeued = await database.WriteAsync(
            async _ =>
            {
                if (await emails.RequeueAsync(emailId, mail.State, cancellationToken) is null)
                {
                    return false;
                }

                var details = new AuditDetails().Add("email", emailId).Add("entry", row.EntryKey).Add("uncertain", mail.State == EmailState.Uncertain);
                var entry = row.FormKey is { } form
                    ? new NewAuditEntry(EmailRetriedAction, FormAudit.EntityType, form, userKey, details)
                    : new NewAuditEntry(EmailRetriedAction, EmailEntityType, null, userKey, details);
                await audit.AddAsync(entry, cancellationToken);
                return true;
            },
            cancellationToken);
        return requeued
            ? new DeliveryRetry(ActionRunRetryStatus.Queued, await GetEmailAsync(emailId, cancellationToken))
            : new DeliveryRetry(ActionRunRetryStatus.NotFound, null);
    }

    /// <summary>Where a webhook action goes, from its settings: never the address of a reference, and only the host of its own.</summary>
    internal static DeliveryTarget? TargetOf(FormAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Type != WebhookActionType.Alias || action.Settings is not { } settings)
        {
            return null;
        }

        var target = settings.TryGetProperty("target", out var kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
        var url = settings.TryGetProperty("url", out var address) && address.ValueKind == JsonValueKind.String ? address.GetString() : null;
        var reference = FormBuilderSecrets.IsReference(url) ? FormBuilderSecrets.KeyOf(url!) : null;
        return target switch
        {
            "slack" => new DeliveryTarget(DeliveryTargetKind.Slack, reference),
            "teams" => new DeliveryTarget(DeliveryTargetKind.Teams, reference),
            _ when reference is not null => new DeliveryTarget(DeliveryTargetKind.Reference, reference),
            _ when Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 =>
                new DeliveryTarget(DeliveryTargetKind.Host, uri.IsDefaultPort ? uri.IdnHost : $"{uri.IdnHost}:{uri.Port}"),
            _ => null,
        };
    }

    // The row with its action's name and target, each version read once per page.
    private async Task<DeliveryItem> ItemAsync(DeliveryRow row, Dictionary<int, FormDefinition?> versions, CancellationToken cancellationToken)
    {
        var typeName = row.ActionType is { } alias ? types.Find(alias)?.Declaration.Name : null;
        if (row.FormVersionId is not { } versionId || row.ActionKey is not { } actionKey)
        {
            return new DeliveryItem(row, null, typeName, null);
        }

        if (!versions.TryGetValue(versionId, out var definition))
        {
            definition = versions[versionId] = await forms.GetVersionByIdAsync(versionId, cancellationToken) is { } version ? documents.Readable(version) : null;
        }

        if (definition?.Actions.FirstOrDefault(action => action.Key == actionKey) is not { } found)
        {
            return new DeliveryItem(row, null, typeName, null);
        }

        // The run used the host's locks, so the target is the one they leave; a mail's recipients are its own.
        return new DeliveryItem(row, string.IsNullOrWhiteSpace(found.Name) ? null : found.Name, typeName, row.IsEmail ? null : TargetOf(locks.Apply(found)));
    }
}
