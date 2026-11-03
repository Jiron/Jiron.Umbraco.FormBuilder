using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The time zone a site's forms run in (ADR-0043, ADR-0084): <c>general.timeZone</c> of the site, through the settings
/// service and its cache. A stored zone this server cannot resolve, as after a move to a server without it, counts as
/// UTC, and the site logs a warning that names the setting, at most once an hour per site and zone on each server.
/// </summary>
internal sealed partial class SiteTimeZones(IFormBuilderSettingsService settings, TimeProvider time, ILogger<SiteTimeZones> logger)
{
    /// <summary>The setting a warning names.</summary>
    public const string Setting = FormBuilderSettingsSections.General + ".timeZone";

    // The sites and zones warned about lately, when; a few per site, emptied before it could grow past a bound.
    private const int MaxWarned = 256;
    private static readonly TimeSpan WarnEvery = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<(Guid? Site, string Zone), DateTimeOffset> _warned = new();

    /// <summary>The time zone of a site's forms; UTC when its stored zone does not resolve here.</summary>
    /// <param name="site">The key of the site's root content, or null for the values every site shares.</param>
    /// <param name="cancellationToken">Cancels the read of the settings.</param>
    public async Task<TimeZoneInfo> GetAsync(Guid? site, CancellationToken cancellationToken = default)
    {
        var stored = (await settings.GetAsync<GeneralSettings>(site, cancellationToken)).Value.TimeZone;
        if (CalendarValues.FindTimeZone(stored) is { } zone)
        {
            return zone;
        }

        var now = time.GetUtcNow();
        if (!_warned.TryGetValue((site, stored), out var last) || now - last >= WarnEvery)
        {
            if (_warned.Count >= MaxWarned)
            {
                _warned.Clear();
            }

            _warned[(site, stored)] = now;
            LogUnresolved(logger, Setting, site, stored);
        }

        return TimeZoneInfo.Utc;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The setting {Setting} of the site {SiteKey} names the time zone '{TimeZone}', which this server cannot resolve, so its forms run in UTC. Choose the site's time zone again in the Form Builder section's settings.")]
    private static partial void LogUnresolved(ILogger logger, string setting, Guid? siteKey, string timeZone);
}
