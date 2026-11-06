using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;

/// <summary>
/// The general settings, as Settings > General edits them (section 4.8, IA 8, ADR-0198): so far how long a form's History
/// keeps draft saves, which every site shares; 12.4a adds the rest of the section. Both need <c>manageSettings</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/settings/general")]
[ApiExplorerSettings(GroupName = "Settings")]
internal sealed class GeneralSettingsController(IFormBuilderSettingsService settings) : FormBuilderManagementApiController
{
    /// <summary>The global values, the version a save sends back, the limits of each value and the host's locks.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(GeneralSettingsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) =>
        Ok(GeneralSettingsResponseModel.Of(await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken)));

    /// <summary>
    /// Saves the groups the request holds, with the version it read; every other value of the section stays as it is. A
    /// value out of range answers <c>400</c> with its field, a section the host locks or a save made meanwhile <c>409</c>.
    /// </summary>
    /// <param name="request">The version read and the groups to save.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(GeneralSettingsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(GeneralSettingsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The groups sent change the stored values as the save reads them, so the rest stays as stored, also where this
        // server's copy is older than the row (ADR-0198).
        var saved = await settings.UpdateAsync<GeneralSettings>(
            current => request.History is { } history
                ? current with { History = new HistorySettings { DraftDays = history.DraftDays, DraftCount = history.DraftCount } }
                : current,
            request.Version,
            RequestAccess.User.Key,
            cancellationToken);
        return Ok(GeneralSettingsResponseModel.Of(saved));
    }
}

/// <summary>The general settings as Settings > General shows them.</summary>
public sealed class GeneralSettingsResponseModel
{
    /// <summary>The version of the stored values, 0 before the first save; a save sends it back.</summary>
    public required int Version { get; init; }

    /// <summary>How long a form's History keeps draft saves.</summary>
    public required HistorySettingsResponseModel History { get; init; }

    /// <summary>What the History values may be.</summary>
    public required HistoryLimitsResponseModel HistoryLimits { get; init; }

    /// <summary>The host's locks, by field: <c>*</c> for the whole section.</summary>
    public required IReadOnlyList<SettingsLockResponseModel> Locks { get; init; }

    internal static GeneralSettingsResponseModel Of(SettingsSnapshot<GeneralSettings> snapshot)
    {
        var history = snapshot.Value.History ?? new HistorySettings();
        return new()
        {
            Version = snapshot.Version,
            History = new() { DraftDays = history.DraftDays, DraftCount = history.DraftCount },
            HistoryLimits = new()
            {
                MinDraftDays = HistorySettings.MinDraftDays,
                MaxDraftDays = HistorySettings.MaxDraftDays,
                MaxDraftCount = HistorySettings.MaxDraftCount,
            },
            Locks = [.. snapshot.Locks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => SettingsLockResponseModel.Of(pair.Key, pair.Value))],
        };
    }
}

/// <summary>A save of the general settings: the version read, and the groups it changes.</summary>
public sealed class GeneralSettingsRequestModel
{
    /// <summary>The version the values were read at.</summary>
    [Range(0, int.MaxValue)]
    public required int Version { get; init; }

    /// <summary>How long a form's History keeps draft saves; null leaves it as it is.</summary>
    public HistorySettingsRequestModel? History { get; init; }
}

/// <summary>How long a form's History keeps draft saves (ADR-0045): every site shares them.</summary>
public sealed class HistorySettingsResponseModel
{
    /// <summary>How many days every draft save stays at least.</summary>
    public required int DraftDays { get; init; }

    /// <summary>How many of each form's newest draft saves stay however old they are.</summary>
    public required int DraftCount { get; init; }
}

/// <summary>How long a form's History is to keep draft saves (ADR-0045), for every site.</summary>
public sealed class HistorySettingsRequestModel
{
    /// <summary>How many days every draft save stays at least.</summary>
    public required int DraftDays { get; init; }

    /// <summary>How many of each form's newest draft saves stay however old they are.</summary>
    public required int DraftCount { get; init; }
}

/// <summary>What the History values may be, from the code.</summary>
public sealed class HistoryLimitsResponseModel
{
    /// <summary>The fewest days.</summary>
    public required int MinDraftDays { get; init; }

    /// <summary>The most days.</summary>
    public required int MaxDraftDays { get; init; }

    /// <summary>The most draft saves kept however old; the fewest is 0.</summary>
    public required int MaxDraftCount { get; init; }
}

/// <summary>A host rule on a settings field (ADR-0008).</summary>
public sealed class SettingsLockResponseModel
{
    /// <summary>The field's path within the section, such as <c>history.draftDays</c>; <c>*</c> for the whole section.</summary>
    public required string Field { get; init; }

    /// <summary><c>locked</c>, <c>restricted</c> (some values refused) or <c>clamped</c> (lowered to a limit).</summary>
    public required string Kind { get; init; }

    /// <summary>The configuration key behind it, such as <c>JironFormBuilder:LockedSections:0</c>.</summary>
    public required string ConfigurationKey { get; init; }

    internal static SettingsLockResponseModel Of(string field, SettingsLock rule) => new()
    {
        Field = field,
        Kind = rule.Kind switch
        {
            SettingsLockKind.Locked => "locked",
            SettingsLockKind.Restricted => "restricted",
            _ => "clamped",
        },
        ConfigurationKey = rule.ConfigurationKey,
    };
}
