using System.Collections.Concurrent;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Web.Setup;

/// <summary>Which screen fixes a finding, which the Overview links to for a user who may open it.</summary>
internal enum HealthLink
{
    /// <summary>Settings, Email.</summary>
    SettingsEmail,

    /// <summary>Settings, General.</summary>
    SettingsGeneral,

    /// <summary>Activity, Deliveries.</summary>
    Deliveries,

    /// <summary>Privacy, Data collection.</summary>
    PrivacyDataCollection,

    /// <summary>Privacy, Retention.</summary>
    PrivacyRetention,

    /// <summary>Settings, Spam protection.</summary>
    SettingsProtection,
}

/// <summary>What a health check found to be wrong.</summary>
/// <param name="Code">What it is, which never changes.</param>
/// <param name="Message">What is wrong and what to do, as a sentence in the user's language.</param>
/// <param name="Link">The screen that fixes it, when one does; otherwise the sentence says where.</param>
/// <param name="CoversEmail">Whether the checklist's first step says the same while it is open.</param>
internal sealed record HealthFinding(string Code, string Message, HealthLink? Link = null, bool CoversEmail = false);

/// <summary>
/// One thing the site's health depends on (section 4.7, ADR-0165). The Overview and Umbraco's Health Check dashboard both read
/// it, so they never disagree; a probe says only what is wrong, in the language asked for.
/// </summary>
internal abstract class HealthProbe
{
    /// <summary>What is wrong, or nothing.</summary>
    /// <param name="words">The texts in the user's language.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public abstract Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default);

    /// <summary>The sentence that says all is well.</summary>
    /// <param name="words">The texts in the user's language.</param>
    public abstract string Passed(HealthWords words);
}

/// <summary>
/// Mail (ADR-0007): Umbraco's gate says whether a mail server is set up, and passes without a From address, which the site then
/// needs from the settings or from Umbraco's own.
/// </summary>
/// <param name="sender">Umbraco's mail sender.</param>
/// <param name="settings">The settings.</param>
/// <param name="global">Umbraco's global settings.</param>
/// <param name="navigation">Finds the sites, each of which may have its own sender.</param>
internal sealed class EmailHealth(IEmailSender sender, IFormBuilderSettingsService settings, IOptionsMonitor<GlobalSettings> global, IDocumentNavigationQueryService navigation) : HealthProbe
{
    /// <summary>Whether Umbraco can send mail at all.</summary>
    public bool CanSend => sender.CanSendRequiredEmail();

    /// <summary>
    /// Whether mail has an address to come from, by the rule mail itself follows (<c>EmailOutbox</c>): the settings' address, globally
    /// or for a site, or Umbraco's own when it is one plain address.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> HasFromAsync(CancellationToken cancellationToken = default)
    {
        if (EmailAddresses.IsOne(global.CurrentValue.Smtp?.From) || !string.IsNullOrWhiteSpace((await settings.GetAsync<EmailSettings>(null, cancellationToken)).Value.SenderAddress))
        {
            return true;
        }

        if (navigation.TryGetRootKeys(out var roots))
        {
            foreach (var root in roots)
            {
                if (!string.IsNullOrWhiteSpace((await settings.GetAsync<EmailSettings>(root, cancellationToken)).Value.SenderAddress))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        var found = new List<HealthFinding>();
        if (!CanSend)
        {
            found.Add(new HealthFinding("emailNoServer", words.EmailNoServer, CoversEmail: true));
        }

        if (!await HasFromAsync(cancellationToken))
        {
            found.Add(new HealthFinding("emailNoFrom", words.EmailNoFrom, HealthLink.SettingsEmail, CoversEmail: true));
        }

        return found;
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.EmailPassed;
}

/// <summary>
/// Where uploaded files live (ADR-0041): never where the website serves files from, and on a site with more than one server not
/// in the default folder, which lies on each server's own disk. A store of the site's own is the host's choice.
/// </summary>
/// <param name="options">The host configuration.</param>
/// <param name="store">The file store.</param>
/// <param name="folders">The host's folders.</param>
/// <param name="servers">How many servers the site runs on.</param>
internal sealed class UploadsHealth(IOptions<FormBuilderOptions> options, IFormBuilderFileStore store, IHostFolders folders, SiteServers servers) : HealthProbe
{
    /// <inheritdoc />
    public override Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        var found = new List<HealthFinding>();
        var root = options.Value.Files.Root;
        if (store is PhysicalFileStore)
        {
            if (!FileStoreRoot.TryResolve(root, folders, out _, out _))
            {
                found.Add(new HealthFinding("uploadsReachable", words.UploadsReachable));
            }
            else if (FileStoreLoadBalancingCheck.IsDefault(root) && servers.MoreThanOne)
            {
                found.Add(new HealthFinding("uploadsOnEachServer", words.UploadsOnEachServer));
            }
        }

        return Task.FromResult<IReadOnlyList<HealthFinding>>(found);
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.UploadsPassed;
}

/// <summary>
/// The Data Protection key ring, which challenges and links need across restarts and servers. Without a store a site chose, ASP.NET
/// Core keeps the keys in the user's profile, on this server's disk, or only in memory when the account has no profile; a container's
/// profile goes with the container, and Azure App Service shares it between a site's instances.
/// </summary>
/// <param name="options">The key management options.</param>
/// <param name="servers">How many servers the site runs on.</param>
/// <param name="profile">The folder of the account's profile, or null; replaced in tests.</param>
internal sealed class KeyRingHealth(IOptions<KeyManagementOptions> options, SiteServers servers, KeyRingProfile profile) : HealthProbe
{
    /// <inheritdoc />
    public override Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HealthFinding> found = options.Value.XmlRepository is not null || profile.SharedByHost ? []
            : !profile.Exists ? [new HealthFinding("keyRingInMemory", words.KeyRingInMemory)]
            : profile.InContainer ? [new HealthFinding("keyRingInContainer", words.KeyRingInContainer)]
            : servers.MoreThanOne ? [new HealthFinding("keyRingOnEachServer", words.KeyRingOnEachServer)]
            : [];
        return Task.FromResult(found);
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.KeyRingPassed;
}

/// <summary>Whether the account the site runs under has a profile folder, where ASP.NET Core keeps its keys by default.</summary>
internal class KeyRingProfile
{
    /// <summary>True when one of the folders ASP.NET Core looks for exists.</summary>
    public virtual bool Exists =>
        new[] { Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("USERPROFILE"), Environment.GetEnvironmentVariable("HOME") }
            .Any(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder));

    /// <summary>True on Azure App Service, whose instances of a site share the folder ASP.NET Core keeps its keys in.</summary>
    public virtual bool SharedByHost => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

    /// <summary>True in a container, whose disk goes with it unless a volume was mounted, which ASP.NET Core's default does not ask for.</summary>
    public virtual bool InContainer => string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>How many servers the site runs on (ADR-0041): more than one for a subscriber, or when more than one is registered as active.</summary>
/// <param name="roles">This server's role.</param>
/// <param name="registrations">Umbraco's registered servers.</param>
/// <param name="logger">The log.</param>
internal sealed partial class SiteServers(IServerRoleAccessor roles, IServerRegistrationService registrations, ILogger<SiteServers> logger)
{
    /// <summary>Whether the site runs on more than one server; false when the registrations cannot be read.</summary>
    public bool MoreThanOne
    {
        get
        {
            var role = roles.CurrentServerRole;
            if (role == ServerRole.Single)
            {
                return false;
            }

            if (role == ServerRole.Subscriber)
            {
                return true;
            }

            try
            {
                return registrations.GetActiveServers(refresh: true)?.Count() > 1;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogServersUnreadable(logger, exception.GetType().Name);
                return false;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder's health check could not read the registered servers: {Reason}.")]
    private static partial void LogServersUnreadable(ILogger logger, string reason);
}

/// <summary>The background jobs, which one server runs every minute (ADR-0007, ADR-0165); several runs missed means they have stopped.</summary>
/// <param name="heartbeat">When they last ran.</param>
/// <param name="time">The clock.</param>
internal sealed class JobsHealth(JobHeartbeat heartbeat, TimeProvider time) : HealthProbe
{
    /// <summary>How long the jobs may not have run before the check says so: a few missed runs and a restart are normal.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default) =>
        time.GetUtcNow() - await heartbeat.LastRunAsync(cancellationToken) > Silence ? [new HealthFinding("jobsStopped", words.JobsStopped)] : [];

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.JobsPassed;
}

/// <summary>Deliveries that failed for good in the last day, and a host's switch that holds sending back (section 4.4).</summary>
/// <param name="emails">The mail.</param>
/// <param name="runs">The action runs.</param>
/// <param name="wake">Holds the host's pause switch.</param>
/// <param name="time">The clock.</param>
internal sealed class DeliveriesHealth(IEmailRepository emails, IActionRunRepository runs, OutboxWake wake, TimeProvider time) : HealthProbe
{
    /// <summary>How far back a failure counts.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        var found = new List<HealthFinding>();
        if (wake.Paused)
        {
            found.Add(new HealthFinding("sendingPaused", words.SendingPaused));
        }

        var since = time.GetUtcNow() - Window;
        var mail = await emails.CountFailedSinceAsync(since, EmailOutbox.Keep, cancellationToken);
        if (mail > 0)
        {
            found.Add(new HealthFinding("emailsFailing", words.EmailsFailing(mail), HealthLink.Deliveries));
        }

        var actions = await runs.CountFailedSinceAsync(since, cancellationToken);
        if (actions > 0)
        {
            found.Add(new HealthFinding("actionsFailing", words.ActionsFailing(actions), HealthLink.Deliveries));
        }

        return found;
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.DeliveriesPassed;
}

/// <summary>The database (ADR-0033): while the migrations are pending, Jiron Form Builder is paused.</summary>
/// <param name="state">The migration state.</param>
internal sealed class MigrationsHealth(IFormBuilderMigrationState state) : HealthProbe
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default) =>
        await state.IsPendingAsync(cancellationToken) ? [new HealthFinding("migrationsPending", words.MigrationsPending)] : [];

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.MigrationsPassed;
}

/// <summary>
/// The base URL of each site (ADR-0008, ADR-0067): a site with no setting, no <c>https</c> domain and no application URL has no
/// address for the links in its mails. A root counts as a site once it is published and has a template: a folder of data has no
/// address to give, and an unpublished root has no visitors yet.
/// </summary>
/// <param name="baseUrls">Finds a site's base URL.</param>
/// <param name="navigation">Finds the sites.</param>
/// <param name="contents">Names them.</param>
internal sealed class BaseUrlHealth(SiteBaseUrl baseUrls, IDocumentNavigationQueryService navigation, IContentService contents) : HealthProbe
{
    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        var found = new List<HealthFinding>();
        if (navigation.TryGetRootKeys(out var roots))
        {
            foreach (var root in roots)
            {
                if (contents.GetById(root) is { Published: true, TemplateId: not null } site && await baseUrls.GetAsync(root, cancellationToken) is null)
                {
                    found.Add(new HealthFinding("siteNoBaseUrl", words.SiteNoBaseUrl(site.Name ?? root.ToString("D", CultureInfo.InvariantCulture)), HealthLink.SettingsGeneral));
                }
            }
        }

        return found;
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.BaseUrlPassed;
}

/// <summary>
/// Privacy (ADR-0009, ADR-0224): what the host allows beyond the defaults, while a setting uses it. Full IP addresses, the
/// site's or a live form's own; entries of a state kept for good; and a captcha that loads before visitors are asked, the
/// settings' for all sites or a site's own. Each is the site's choice to make, and each is said while it is on.
/// </summary>
/// <param name="settings">The settings.</param>
/// <param name="options">The host configuration.</param>
/// <param name="forms">The live forms.</param>
/// <param name="cache">Their published versions.</param>
/// <param name="navigation">Finds the sites, each of which may consent to captchas its own way.</param>
internal sealed class PrivacyHealth(IFormBuilderSettingsService settings, IOptions<FormBuilderOptions> options, IFormRepository forms, PublishedFormCache cache, IDocumentNavigationQueryService navigation) : HealthProbe
{
    // A page of the live forms, read until the last.
    private const int FormsPage = 200;

    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var allowed = options.Value.Privacy;
        var found = new List<HealthFinding>();
        var privacy = (await settings.GetAsync<PrivacySettings>(null, cancellationToken)).Value;
        if (allowed.AllowFullIp)
        {
            if (privacy.IP == IPHandling.Full)
            {
                found.Add(new HealthFinding("fullIp", words.FullIp, HealthLink.PrivacyDataCollection));
            }
            else if (await FullIPFormsAsync(cancellationToken) is var count and > 0)
            {
                found.Add(new HealthFinding("fullIpForms", words.FullIpForms(count)));
            }
        }

        var retention = privacy.Retention ?? new SiteRetention();
        if (allowed.AllowRetentionOff && SiteRetention.ForGood is var forGood && (retention.Submitted == forGood || retention.Approved == forGood || retention.Rejected == forGood || retention.Spam == forGood))
        {
            found.Add(new HealthFinding("retentionOff", words.RetentionOff, HealthLink.PrivacyRetention));
        }

        if (allowed.AllowImplicitCaptchaConsent && await CaptchaUnaskedAsync(cancellationToken))
        {
            found.Add(new HealthFinding("captchaUnasked", words.CaptchaUnasked, HealthLink.SettingsProtection));
        }

        return found;
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.PrivacyPassed;

    // How many live forms keep full addresses of their own, as their published versions say.
    private async Task<int> FullIPFormsAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        for (var skip = 0; ; skip += FormsPage)
        {
            var page = await forms.ListAsync(FormFilter.Live, new PageRequest(skip, FormsPage), cancellationToken);
            foreach (var form in page.Items.Where(form => form.PublishedVersionId is not null))
            {
                if ((await cache.GetAsync(form.Key, cancellationToken))?.Published.Definition.Settings.Privacy.IP == IPHandling.Full)
                {
                    count++;
                }
            }

            if (page.Items.Count < FormsPage || skip + page.Items.Count >= page.Total)
            {
                return count;
            }
        }
    }

    // Whether the settings for all sites, or a site's own, load a captcha before visitors are asked.
    private async Task<bool> CaptchaUnaskedAsync(CancellationToken cancellationToken)
    {
        if ((await settings.GetAsync<ProtectionSettings>(null, cancellationToken)).Value.CaptchaConsent == CaptchaConsentMode.Implicit)
        {
            return true;
        }

        if (navigation.TryGetRootKeys(out var roots))
        {
            foreach (var root in roots)
            {
                if ((await settings.GetAsync<ProtectionSettings>(root, cancellationToken)).Value.CaptchaConsent == CaptchaConsentMode.Implicit)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// The "used on" index (ADR-0017, ADR-0131). It is never a warning: a rebuild under way only delays what the Info view shows, and
/// its action, on Umbraco's Health Check dashboard, asks for one when a site doubts the index.
/// </summary>
/// <param name="rebuild">The rebuild.</param>
internal sealed class UsedOnHealth(FormReferenceRebuild rebuild) : HealthProbe
{
    /// <summary>Whether a rebuild was asked for and has not finished.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<bool> IsRebuildingAsync(CancellationToken cancellationToken = default) => rebuild.IsDueAsync(cancellationToken);

    /// <summary>Asks for a rebuild from the first document; the reference job does it.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task RequestRebuildAsync(CancellationToken cancellationToken = default) => rebuild.RequestAsync(cancellationToken);

    /// <inheritdoc />
    public override Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HealthFinding>>([]);

    /// <inheritdoc />
    public override string Passed(HealthWords words) => words.UsedOnPassed;
}

/// <summary>
/// Forms as files (D25, ADR-0220): one finding for each file the mirror could not keep in step with its form, a conflict
/// first. Nothing while the mirror is off.
/// </summary>
/// <param name="mirror">The mirror.</param>
internal sealed class MirrorHealth(FormMirror mirror) : HealthProbe
{
    /// <summary>Whether a conflict waits.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> HasConflictAsync(CancellationToken cancellationToken = default) =>
        mirror.On && (await mirror.ProblemsAsync(cancellationToken)).Any(problem => problem.Kind == MirrorProblemKind.Conflict);

    /// <summary>Writes the file of each form in conflict from its published version; how many it wrote.</summary>
    /// <param name="cancellationToken">Cancels the writes.</param>
    public Task<int> KeepTheFormsAsync(CancellationToken cancellationToken = default) => mirror.KeepTheFormsAsync(cancellationToken);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        if (!mirror.On)
        {
            return [];
        }

        return [.. (await mirror.ProblemsAsync(cancellationToken)).Select(problem => problem.Kind switch
        {
            MirrorProblemKind.Conflict => new HealthFinding("mirrorConflict", words.MirrorConflict(problem.File)),
            MirrorProblemKind.Unwritable => new HealthFinding("mirrorUnwritable", words.MirrorUnwritable(problem.File)),
            MirrorProblemKind.NotPublished => new HealthFinding("mirrorNotPublished", words.MirrorNotPublished(problem.File)),
            _ => new HealthFinding("mirrorUnreadable", words.MirrorUnreadable(problem.File)),
        })];
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words) => mirror.On ? words.MirrorPassed : words.MirrorOff;
}

/// <summary>
/// Every health probe together, for the Overview: a probe that fails says so rather than taking the others down, because a health
/// screen is read when something is wrong. The start page opens often and the probes read the database and the registered servers, so
/// the answer stands for a minute per server; Umbraco's dashboard asks the probes itself.
/// </summary>
/// <param name="probes">The probes.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The log.</param>
internal sealed partial class FormBuilderHealth(IEnumerable<HealthProbe> probes, TimeProvider time, ILogger<FormBuilderHealth> logger)
{
    /// <summary>How long an answer stands.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(bool German, bool NamesSites), (DateTimeOffset At, IReadOnlyList<HealthFinding> Found)> _answers = new();

    /// <summary>All that is wrong, in the language asked for.</summary>
    /// <param name="culture">The user's language.</param>
    /// <param name="namesSites">Whether the user may read the names of the site's root content; others hear of "a site".</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<IReadOnlyList<HealthFinding>> FindAsync(string? culture, bool namesSites, CancellationToken cancellationToken = default)
    {
        var words = HealthWords.Of(culture, namesSites);
        var key = (words.German, namesSites);
        var now = time.GetUtcNow();
        if (_answers.TryGetValue(key, out var answer) && now - answer.At < Fresh)
        {
            return answer.Found;
        }

        var found = await ReadAsync(words, cancellationToken);
        _answers[key] = (now, found);
        return found;
    }

    private async Task<IReadOnlyList<HealthFinding>> ReadAsync(HealthWords words, CancellationToken cancellationToken)
    {
        var found = new List<HealthFinding>();
        foreach (var probe in probes)
        {
            try
            {
                found.AddRange(await probe.FindAsync(words, cancellationToken));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException && exception is not OperationCanceledException)
            {
                LogProbeFailed(logger, exception, probe.GetType().Name);
                found.Add(new HealthFinding("checkFailed", words.CheckFailed));
            }
        }

        return [.. found.DistinctBy(finding => finding.Message)];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's health check {Probe} failed.")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception, string probe);
}
