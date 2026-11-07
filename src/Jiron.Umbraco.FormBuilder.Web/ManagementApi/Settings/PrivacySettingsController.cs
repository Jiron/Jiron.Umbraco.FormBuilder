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
/// The privacy settings, as Privacy > Data collection edits them (IA 9, ADR-0211): so far whether the forms count what
/// visitors do with them, which every site shares; the rest of the screen comes with its features. Both need
/// <c>manageSettings</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/settings/privacy")]
[ApiExplorerSettings(GroupName = "Settings")]
internal sealed class PrivacySettingsController(IFormBuilderSettingsService settings) : FormBuilderManagementApiController
{
    /// <summary>The global values, the version a save sends back and the host's locks.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(PrivacySettingsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) =>
        Ok(PrivacySettingsResponseModel.Of(await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)));

    /// <summary>
    /// Saves the values the request holds, with the version it read; every other value of the section stays as it is. A
    /// section the host locks or a save made meanwhile answers <c>409</c>.
    /// </summary>
    /// <param name="request">The version read and the values to save.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(PrivacySettingsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(PrivacySettingsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The values sent change the stored ones as the save reads them, so the rest stays as stored (ADR-0198).
        var saved = await settings.UpdateAsync<PrivacySettings>(
            current => request.Insights is { } insights ? current with { Insights = insights } : current,
            request.Version,
            RequestAccess.User.Key,
            cancellationToken);
        return Ok(PrivacySettingsResponseModel.Of(saved));
    }
}

/// <summary>The privacy settings as Privacy > Data collection shows them.</summary>
public sealed class PrivacySettingsResponseModel
{
    /// <summary>The version of the stored values, 0 before the first save; a save sends it back.</summary>
    public required int Version { get; init; }

    /// <summary>Whether the forms count views, starts, pages and the fields that stop visitors; each form may turn it off.</summary>
    public required bool Insights { get; init; }

    /// <summary>The host's locks, by field: <c>*</c> for the whole section.</summary>
    public required IReadOnlyList<SettingsLockResponseModel> Locks { get; init; }

    internal static PrivacySettingsResponseModel Of(SettingsSnapshot<PrivacySettings> snapshot) => new()
    {
        Version = snapshot.Version,
        Insights = snapshot.Value.Insights,
        Locks = [.. snapshot.Locks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => SettingsLockResponseModel.Of(pair.Key, pair.Value))],
    };
}

/// <summary>A save of the privacy settings: the version read, and the values it changes.</summary>
public sealed class PrivacySettingsRequestModel
{
    /// <summary>The version the values were read at.</summary>
    [Range(0, int.MaxValue)]
    public required int Version { get; init; }

    /// <summary>Whether the forms count what visitors do with them; null leaves it as it is.</summary>
    public bool? Insights { get; init; }
}
