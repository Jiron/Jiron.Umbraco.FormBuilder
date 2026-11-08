using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;

/// <summary>
/// The settings sections, as their screens show and save them (section 4.8, IA 8 and 9, ADR-0218): one pair of endpoints
/// for every section, each in one scope, all sites or one site. Both need <c>manageSettings</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/settings")]
[ApiExplorerSettings(GroupName = "Settings")]
internal sealed class SettingsController(IEnumerable<ISettingsScreen> screens, SettingsSites sites) : FormBuilderManagementApiController
{
    /// <summary>
    /// A section in a scope: its values, the version a save sends back, the fields and their editors, the host's locks, for
    /// a section a site can set the sites and the fields this one sets, and what its screen says besides. <c>400</c> for a
    /// site that is none of the content roots, <c>404</c> for no such section.
    /// </summary>
    /// <param name="section">The section's alias, such as <c>general</c>.</param>
    /// <param name="site">The site, by the key of its root content; none for all sites.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{section}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(SettingsSectionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string section, Guid? site = null, CancellationToken cancellationToken = default)
    {
        if (Find(section) is not { } screen)
        {
            return FormBuilderProblems.SettingsSectionNotFound();
        }

        if (screen.PerSite && site is { } key && !sites.IsSite(key))
        {
            return FormBuilderProblems.SiteUnknown();
        }

        return Ok(await screen.ReadAsync(screen.PerSite ? site : null, cancellationToken));
    }

    /// <summary>
    /// Saves the fields a request sends, with the version it read: for all sites every other field stays as stored, and for
    /// a site its own fields are those it names. A value not allowed answers <c>400</c> with its field, a site that is none
    /// of the content roots <c>400</c> with nothing written, a section the host locks or a save made meanwhile <c>409</c>.
    /// </summary>
    /// <param name="section">The section's alias.</param>
    /// <param name="request">The version read, the scope and the fields to save.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{section}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(SettingsSectionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(string section, SettingsSectionRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Find(section) is not { } screen)
        {
            return FormBuilderProblems.SettingsSectionNotFound();
        }

        if (screen.PerSite && request.Site is { } key && !sites.IsSite(key))
        {
            return FormBuilderProblems.SiteUnknown();
        }

        return Ok(await screen.SaveAsync(screen.PerSite ? request : request with { Site = null }, RequestAccess.User.Key, cancellationToken));
    }

    private ISettingsScreen? Find(string section) => screens.FirstOrDefault(screen => string.Equals(screen.Section, section, StringComparison.Ordinal));
}
