using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// Tells the team recipients of the email settings that a delivery failed for good (section 4.8): at most once an hour per form, so a
/// host that is down does not fill their mailbox, with the action's type and its code and never an answer. A notice that fails tells of
/// nothing, and a failure to tell stops nothing.
/// </summary>
internal sealed partial class DeliveryNotices(
    IFormBuilderSettingsService settings,
    EmailOutbox outbox,
    IKeyValueRepository keyValues,
    IEntryRepository entries,
    IFormRepository forms,
    FormPages pages,
    TimeProvider time,
    ILogger<DeliveryNotices> logger)
{
    /// <summary>The least time between two notices of a form.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromHours(1);

    private const string LastPrefix = "jfb.deliverynotice.";

    private static readonly FrozenDictionary<string, (string Subject, string Text, string Where)> Words = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ("A delivery failed on {0}", "A delivery of the form {0} failed for good: {1} ({2}). More may have failed in the hour since this one.", "See Forms, Activity, Deliveries."),
        ["de"] = ("Eine Zustellung von {0} ist fehlgeschlagen", "Eine Zustellung des Formulars {0} ist endg" + (char)0xFC + "ltig fehlgeschlagen: {1} ({2}). In der Stunde danach k" + (char)0xF6 + "nnen weitere fehlgeschlagen sein.", "Siehe Formulare, Aktivit" + (char)0xE4 + "t, Zustellungen."),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tells of an action that failed for good.</summary>
    /// <param name="context">The action and its entry.</param>
    /// <param name="type">The action type's alias.</param>
    /// <param name="code">Why, as a code.</param>
    public Task TellOfActionAsync(ActionRunContext context, string type, string? code)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TellAsync(context.Scope.FormAlias, context.Scope.FormName, context.Scope.Site, context.Scope.Culture, type, code);
    }

    /// <summary>Tells of a mail that failed for good, unless it is one nobody needs to hear of: a test or a notice.</summary>
    /// <param name="mail">The mail.</param>
    public async Task TellOfMailAsync(StoredEmail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (mail.Kind is EmailKind.Notice or EmailKind.Test || mail.EntryId is not { } id)
        {
            return;
        }

        try
        {
            if (await entries.GetByIdAsync(id, CancellationToken.None) is { } entry && await forms.GetByIdAsync(entry.FormId, CancellationToken.None) is { } form)
            {
                await TellAsync(form.Alias, form.Name, (await pages.EntryPageAsync(entry)).Site, entry.Culture, StoredNames.Of(mail.Kind), mail.LastError?.Message ?? mail.LastError?.Code);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(logger, LoggedException.Of(exception));
        }
    }

    private async Task TellAsync(string alias, string name, Guid? site, string culture, string what, string? code)
    {
        try
        {
            var team = (await settings.GetAsync<EmailSettings>(site, CancellationToken.None)).Value.TeamRecipients;
            if (team.Count == 0)
            {
                return;
            }

            var key = LastPrefix + alias.ToLowerInvariant();
            var now = time.GetUtcNow();
            if (await keyValues.GetAsync(key, CancellationToken.None) is { } last
                && DateTimeOffset.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var told)
                && now - told < Pause)
            {
                return;
            }

            await keyValues.SetAsync(key, now.ToString("O", CultureInfo.InvariantCulture), CancellationToken.None);
            var words = Words.TryGetValue(culture.Split('-')[0], out var found) ? found : Words["en"];
            await outbox.QueueAsync(
                new OutgoingEmail
                {
                    Kind = EmailKind.Notice,
                    To = team,
                    Subject = string.Format(CultureInfo.InvariantCulture, words.Subject, name),
                    Body = $"<p>{WebUtility.HtmlEncode(string.Format(CultureInfo.InvariantCulture, words.Text, name, what, code ?? "-"))}</p><p>{WebUtility.HtmlEncode(words.Where)}</p>",
                    Culture = culture,
                    Site = site,
                },
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(logger, LoggedException.Of(exception));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Telling the team recipients of a failed delivery failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
