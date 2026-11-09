using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Security;

namespace Jiron.Umbraco.FormBuilder.Web.Setup;

/// <summary>
/// A health probe on Umbraco's Health Check dashboard (section 4.7, ADR-0165), under the group "Jiron Form Builder". Umbraco finds
/// each check by its attribute and shows its statuses as written, so they come in the signed-in user's language.
/// </summary>
/// <param name="probe">What it checks.</param>
/// <param name="security">Who is signed in.</param>
internal abstract class FormBuilderHealthCheck(HealthProbe probe, IBackOfficeSecurityAccessor security) : HealthCheck
{
    /// <summary>The group the checks share.</summary>
    public const string GroupName = "Jiron Form Builder";

    /// <summary>The texts in the user's language.</summary>
    protected HealthWords Words => HealthWords.Of(security.BackOfficeSecurity?.CurrentUser?.Language);

    /// <inheritdoc />
    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var found = await probe.FindAsync(Words);
        return found.Count == 0
            ? [new HealthCheckStatus(probe.Passed(Words)) { ResultType = StatusResultType.Success }]
            : found.Select(finding => new HealthCheckStatus(finding.Message) { ResultType = StatusResultType.Warning }).ToList();
    }

    /// <inheritdoc />
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) => throw new InvalidOperationException($"The check has no action '{action?.Alias}'.");
}

/// <summary>Whether mail can be sent.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c01", "Email sending", Description = "Checks that Umbraco can send mail and that mail has a From address.", Group = GroupName)]
internal sealed class EmailHealthCheck(EmailHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Where uploaded files live.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c02", "Uploaded files", Description = "Checks that the website doesn't serve uploaded files, and that all servers of a site can reach them.", Group = GroupName)]
internal sealed class UploadsHealthCheck(UploadsHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Whether the Data Protection keys last.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c03", "Data Protection keys", Description = "Checks that the keys that protect forms and links last across restarts and servers.", Group = GroupName)]
internal sealed class KeyRingHealthCheck(KeyRingHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Whether the background jobs run.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c04", "Background jobs", Description = "Checks that the jobs that send emails and run actions are running.", Group = GroupName)]
internal sealed class JobsHealthCheck(JobsHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Whether deliveries go out.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c05", "Deliveries", Description = "Checks for emails and actions that failed in the last 24 hours, and whether sending is paused.", Group = GroupName)]
internal sealed class DeliveriesHealthCheck(DeliveriesHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Whether the migrations have run.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c06", "Database migrations", Description = "Checks that Jiron Form Builder's database is up to date; it is paused until it is.", Group = GroupName)]
internal sealed class MigrationsHealthCheck(MigrationsHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Whether each site has an address for its links.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c07", "Base URLs", Description = "Checks that each site has an address for the links in its emails.", Group = GroupName)]
internal sealed class BaseUrlHealthCheck(BaseUrlHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Privacy (ADR-0009, ADR-0224): whether entries keep more of visitors than the defaults, as the host allows.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c0a", "Privacy", Description = "Checks whether entries keep full IP addresses, whether entries of a state are kept for good, and whether a captcha loads before visitors are asked.", Group = GroupName)]
internal sealed class PrivacyHealthCheck(PrivacyHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>Forms as files (ADR-0220): whether each published form and its file are in step.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c09", "Forms as files", Description = "When forms are written to files for source control, checks that each published form and its file are in step.", Group = GroupName)]
internal sealed class MirrorHealthCheck(MirrorHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security)
{
    /// <summary>The alias of the action that writes the files of the forms in conflict, so the site's forms win.</summary>
    public const string KeepAction = "writeFiles";

    /// <inheritdoc />
    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var statuses = (await base.GetStatusAsync()).ToList();
        if (await probe.HasConflictAsync())
        {
            statuses[^1].Actions = [new HealthCheckAction(KeepAction, Id) { Name = Words.MirrorKeep, Description = Words.MirrorKeepHelp }];
        }

        return statuses;
    }

    /// <inheritdoc />
    public override async Task<HealthCheckStatus> ExecuteActionAsync(HealthCheckAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Alias != KeepAction)
        {
            return base.ExecuteAction(action);
        }

        return new HealthCheckStatus(Words.MirrorKept(await probe.KeepTheFormsAsync())) { ResultType = StatusResultType.Success };
    }
}

/// <summary>The "used on" index, whose action asks for a rebuild (ADR-0131). An action runs without a confirmation, so it is safe to repeat.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c08", "\"Used on\" index", Description = "Says whether the index of the pages that use each form is being rebuilt, and asks for a rebuild.", Group = GroupName)]
internal sealed class UsedOnHealthCheck(UsedOnHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security)
{
    /// <summary>The alias of the action that asks for a rebuild.</summary>
    public const string RebuildAction = "rebuild";

    /// <inheritdoc />
    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync() =>
        await probe.IsRebuildingAsync()
            ? [new HealthCheckStatus(Words.UsedOnRebuilding) { ResultType = StatusResultType.Info }]
            : [new HealthCheckStatus(probe.Passed(Words))
            {
                ResultType = StatusResultType.Success,
                Actions = [new HealthCheckAction(RebuildAction, Id) { Name = Words.UsedOnRebuild, Description = Words.UsedOnRebuildHelp }],
            }];

    /// <inheritdoc />
    public override async Task<HealthCheckStatus> ExecuteActionAsync(HealthCheckAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Alias != RebuildAction)
        {
            return base.ExecuteAction(action);
        }

        await probe.RequestRebuildAsync();
        return new HealthCheckStatus(Words.UsedOnRebuilding) { ResultType = StatusResultType.Info };
    }
}
