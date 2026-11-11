using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Core.Webhooks;

/// <summary>The registrations of webhooks (ADR-0161), which the registrations of the action framework make.</summary>
internal static class FormBuilderWebhookServices
{
    /// <summary>Registers the secrets, the webhook action type, the integrations settings' validator and Umbraco's webhook events.</summary>
    public static IUmbracoBuilder AddFormBuilderWebhooks(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<FormBuilderSecrets>();
        builder.Services.TryAddSingleton<EntryEvents>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISettingsValidator<IntegrationSettings>, IntegrationSettingsValidator>());
        builder.JironFormBuilder().ActionTypes().Append<WebhookActionType>();
        builder.AddNotificationAsyncHandler<ActionRunFailedNotification, ActionFailedEventHandler>();
        builder.AddWebhookEvent<EntrySubmittedWebhookEvent>();
        builder.AddWebhookEvent<EntryApprovedWebhookEvent>();
        builder.AddWebhookEvent<EntryRejectedWebhookEvent>();
        builder.AddWebhookEvent<EntryDeletedWebhookEvent>();
        builder.AddWebhookEvent<EntryActionFailedWebhookEvent>();
        return builder;
    }
}

/// <summary>
/// Secrets as settings name them (ADR-0008, ADR-0161): a <c>$JironFormBuilder:Secrets:&lt;Name&gt;</c> reference stands for a value of
/// the host configuration, and never the value itself is stored or shown.
/// </summary>
/// <param name="options">The host configuration.</param>
internal sealed class FormBuilderSecrets(IOptionsMonitor<FormBuilderOptions> options)
{
    /// <summary>What a reference starts with.</summary>
    public const string Prefix = "$JironFormBuilder:Secrets:";

    /// <summary>Whether a text is a reference to a secret.</summary>
    public static bool IsReference(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal) && IsName(value[Prefix.Length..]);

    /// <summary>Whether a text is a secret's name: 1 to 64 letters, digits and '_'.</summary>
    public static bool IsName(string name) => name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The configuration key a reference names, without the value.</summary>
    public static string KeyOf(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.TrimStart('$');
    }

    /// <summary>The value a reference stands for, or null when the host configuration has none; a text that is no reference is its own value.</summary>
    public string? Resolve(string? value) => IsReference(value)
        ? options.CurrentValue.Secrets.TryGetValue(value![Prefix.Length..], out var secret) && !string.IsNullOrEmpty(secret) ? secret : null
        : value;

    /// <summary>Whether a value can be used: none at all can, and a reference only when the host configuration has its secret.</summary>
    public bool TryResolve(string? value, out string? resolved)
    {
        resolved = Resolve(value);
        return value is null || resolved is not null;
    }
}

/// <summary>
/// Publishes the entry notifications (ROADMAP 8.4): what a site's handlers hear of always, and Umbraco's webhooks while the integrations
/// settings have them on. It decides nothing: the entry stands, so a handler that fails is logged and the visitor never hears of it.
/// </summary>
/// <param name="events">Umbraco's notifications.</param>
/// <param name="settings">The integrations settings.</param>
/// <param name="time">The clock of the limit.</param>
/// <param name="logger">Where a failure goes.</param>
internal sealed partial class EntryEvents(IEventAggregator events, IFormBuilderSettingsService settings, TimeProvider time, ILogger<EntryEvents> logger)
{
    /// <summary>How long the handlers may hold up whoever made the change; they run on after it, and are logged.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>Publishes one, with whether webhooks fire for it.</summary>
    /// <typeparam name="T">The notification.</typeparam>
    /// <param name="make">Makes it, told whether webhooks fire.</param>
    public Task PublishAsync<T>(Func<bool, T> make)
        where T : EntryNotification
    {
        ArgumentNullException.ThrowIfNull(make);
        return GuardedAsync(typeof(T).Name, on => events.PublishAsync(make(on), CancellationToken.None));
    }

    /// <summary>
    /// Publishes several, in their order, under one limit: a change of many entries holds up whoever made it for a
    /// moment at most, however many it tells of.
    /// </summary>
    /// <typeparam name="T">The notification.</typeparam>
    /// <param name="make">Makes each, told whether webhooks fire.</param>
    public Task PublishEachAsync<T>(IReadOnlyList<Func<bool, T>> make)
        where T : EntryNotification
    {
        ArgumentNullException.ThrowIfNull(make);
        return make.Count == 0
            ? Task.CompletedTask
            : GuardedAsync(
                typeof(T).Name,
                async on =>
                {
                    foreach (var one in make)
                    {
                        await events.PublishAsync(one(on), CancellationToken.None);
                    }
                });
    }

    /// <summary>
    /// Tells of an entry that arrived: that it is submitted, that its state changed when it came from Spam or was
    /// verified, then that it is approved when it was approved as it came in.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="formAlias">Its form's alias.</param>
    /// <param name="from">The state it left, Spam or unverified; null for an entry that was just submitted.</param>
    public async Task TellArrivedAsync(StoredEntry entry, string formAlias, EntryState? from = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await GuardedAsync(
            nameof(TellArrivedAsync),
            async on =>
            {
                await events.PublishAsync(new EntrySubmittedNotification { EntryKey = entry.Key, EntryNumber = entry.Number, State = StoredNames.Of(entry.State), FormAlias = formAlias, OccurredUtc = entry.SubmittedUtc, FiresWebhooks = on }, CancellationToken.None);
                if (from is { } left)
                {
                    await PublishChangedAsync(entry, formAlias, left);
                }

                await PublishMovedAsync(entry, formAlias, on);
            });
    }

    /// <summary>Tells of an entry whose state changed and stands (ADR-0233), then that it is approved or rejected when it reached one.</summary>
    /// <param name="entry">The entry, in the state it reached.</param>
    /// <param name="formAlias">Its form's alias.</param>
    /// <param name="from">The state it left.</param>
    public Task TellMovedAsync(StoredEntry entry, string formAlias, EntryState from)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return GuardedAsync(
            nameof(TellMovedAsync),
            async on =>
            {
                await PublishChangedAsync(entry, formAlias, from);
                await PublishMovedAsync(entry, formAlias, on);
            });
    }

    private Task PublishChangedAsync(StoredEntry entry, string formAlias, EntryState from) => from == entry.State
        ? Task.CompletedTask
        : events.PublishAsync(new EntryStateChangedNotification { EntryKey = entry.Key, EntryNumber = entry.Number, State = StoredNames.Of(entry.State), From = StoredNames.Of(from), FormAlias = formAlias, OccurredUtc = entry.StateChangedUtc }, CancellationToken.None);

    private Task PublishMovedAsync(StoredEntry entry, string formAlias, bool on) => entry.State switch
    {
        EntryState.Approved => events.PublishAsync(new EntryApprovedNotification { EntryKey = entry.Key, EntryNumber = entry.Number, State = StoredNames.Of(entry.State), FormAlias = formAlias, OccurredUtc = entry.StateChangedUtc, FiresWebhooks = on }, CancellationToken.None),
        EntryState.Rejected => events.PublishAsync(new EntryRejectedNotification { EntryKey = entry.Key, EntryNumber = entry.Number, State = StoredNames.Of(entry.State), FormAlias = formAlias, OccurredUtc = entry.StateChangedUtc, FiresWebhooks = on }, CancellationToken.None),
        _ => Task.CompletedTask,
    };

    // Handlers hold up whoever made the change for a moment at most, and their failure is logged.
    private async Task GuardedAsync(string what, Func<bool, Task> publish)
    {
        try
        {
            await Task.Run(
                    async () => await publish((await settings.GetAsync<IntegrationSettings>(cancellationToken: CancellationToken.None)).Value.UmbracoWebhookEvents),
                    CancellationToken.None)
                .WaitAsync(Limit, time, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPublishFailed(logger, what, LoggedException.Of(exception));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing {Notification} failed.")]
    private static partial void LogPublishFailed(ILogger logger, string notification, Exception exception);
}

/// <summary>Tells of an action that failed for good.</summary>
/// <param name="entryEvents">Publishes it.</param>
/// <param name="notices">Tells the team recipients.</param>
/// <param name="time">The clock.</param>
internal sealed class ActionFailedEventHandler(EntryEvents entryEvents, DeliveryNotices notices, TimeProvider time) : INotificationAsyncHandler<ActionRunFailedNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(ActionRunFailedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.WillRetry || notification.Context.Scope.Entry is not { } entry)
        {
            return;
        }

        await entryEvents.PublishAsync(
            on => new EntryActionFailedNotification
            {
                EntryKey = entry.Key,
                EntryNumber = entry.Number,
                State = entry.State,
                FormAlias = notification.Context.Scope.FormAlias,
                OccurredUtc = time.GetUtcNow(),
                ActionType = notification.Type,
                Code = notification.Code,
                FiresWebhooks = on,
            });
        await notices.TellOfActionAsync(notification.Context, notification.Type, notification.Code);
    }
}

/// <summary>
/// The base of the webhook events: Umbraco's event, firing as the notification says, with its payload. An entry's change happens on
/// whichever server took it, and each happens once, unlike Umbraco's own notifications that every server hears: the request goes in
/// the database on any of them, and Umbraco's job sends it from the scheduling server.
/// </summary>
/// <typeparam name="T">The notification.</typeparam>
public abstract class EntryWebhookEvent<T>(IWebhookFiringService firing, IWebhookService webhooks, IOptionsMonitor<WebhookSettings> settings)
    : WebhookEventBase<T>(firing, webhooks, settings, new SingleServerRoleAccessor())
    where T : EntryNotification
{
    /// <inheritdoc />
    public override object? ConvertNotificationToRequestPayload(T notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification.Payload();
    }

    /// <inheritdoc />
    public override bool ShouldFireWebhookForNotification(T notificationObject)
    {
        ArgumentNullException.ThrowIfNull(notificationObject);
        return notificationObject.FiresWebhooks;
    }
}

/// <summary>An entry was submitted, in Settings, Webhooks.</summary>
[WebhookEvent("Forms: entry submitted", global::Umbraco.Cms.Core.Constants.WebhookEvents.Types.Other)]
public sealed class EntrySubmittedWebhookEvent(IWebhookFiringService firing, IWebhookService webhooks, IOptionsMonitor<WebhookSettings> settings)
    : EntryWebhookEvent<EntrySubmittedNotification>(firing, webhooks, settings)
{
    /// <inheritdoc />
    public override string Alias => "jfbEntrySubmitted";
}

/// <summary>An entry was approved, in Settings, Webhooks.</summary>
[WebhookEvent("Forms: entry approved", global::Umbraco.Cms.Core.Constants.WebhookEvents.Types.Other)]
public sealed class EntryApprovedWebhookEvent(IWebhookFiringService firing, IWebhookService webhooks, IOptionsMonitor<WebhookSettings> settings)
    : EntryWebhookEvent<EntryApprovedNotification>(firing, webhooks, settings)
{
    /// <inheritdoc />
    public override string Alias => "jfbEntryApproved";
}

/// <summary>An entry was rejected, in Settings, Webhooks.</summary>
[WebhookEvent("Forms: entry rejected", global::Umbraco.Cms.Core.Constants.WebhookEvents.Types.Other)]
public sealed class EntryRejectedWebhookEvent(IWebhookFiringService firing, IWebhookService webhooks, IOptionsMonitor<WebhookSettings> settings)
    : EntryWebhookEvent<EntryRejectedNotification>(firing, webhooks, settings)
{
    /// <inheritdoc />
    public override string Alias => "jfbEntryRejected";
}

/// <summary>An entry was deleted, in Settings, Webhooks.</summary>
[WebhookEvent("Forms: entry deleted", global::Umbraco.Cms.Core.Constants.WebhookEvents.Types.Other)]
public sealed class EntryDeletedWebhookEvent(IWebhookFiringService firing, IWebhookService webhooks, IOptionsMonitor<WebhookSettings> settings)
    : EntryWebhookEvent<EntryDeletedNotification>(firing, webhooks, settings)
{
    /// <inheritdoc />
    public override string Alias => "jfbEntryDeleted";
}

/// <summary>An action of an entry failed for good, in Settings, Webhooks.</summary>
[WebhookEvent("Forms: action failed", global::Umbraco.Cms.Core.Constants.WebhookEvents.Types.Other)]
public sealed class EntryActionFailedWebhookEvent(IWebhookFiringService firing, IWebhookService webhooks, IOptionsMonitor<WebhookSettings> settings)
    : EntryWebhookEvent<EntryActionFailedNotification>(firing, webhooks, settings)
{
    /// <inheritdoc />
    public override string Alias => "jfbActionFailed";
}
