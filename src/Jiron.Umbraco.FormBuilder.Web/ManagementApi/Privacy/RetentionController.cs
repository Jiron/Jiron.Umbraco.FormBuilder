using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Privacy;

/// <summary>
/// Privacy > Retention (IA 9, ADR-0222): the site's days per state with what the host allows, the forms with retention of
/// their own, and the dry run of what retention deletes within the next 7, 30 or 90 days by days not saved yet. Both read;
/// either <c>privacyTools</c> or <c>manageSettings</c> may call them. A change of the days is the privacy section's save,
/// which needs <c>manageSettings</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/retention")]
[ApiExplorerSettings(GroupName = "Privacy")]
internal sealed class RetentionController(
    IFormBuilderSettingsService settings,
    RetentionDryRun dryRun,
    IOptions<FormBuilderOptions> options) : FormBuilderManagementApiController
{
    /// <summary>The site's days per state, its section's version for a save, what the host allows, and the forms with days of their own.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings | FormBuilderPermissions.PrivacyTools, AnyOf = true)]
    [ProducesResponseType(typeof(RetentionResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default)
    {
        var section = await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken);
        var retention = section.Value.Retention ?? new SiteRetention();
        var own = await dryRun.OwnAsync(cancellationToken);
        return Ok(new RetentionResponseModel
        {
            Days = RetentionDaysResponseModel.Of(retention),
            Version = section.Version,
            KeepForGood = options.Value.Privacy.AllowRetentionOff,
            KeepForGoodKey = RetentionOffRule.ConfigurationKey,
            LockedBy = section.Locks.TryGetValue(SettingsLocks.WholeSection, out var whole) ? whole.ConfigurationKey : null,
            Forms =
            [
                .. own.Select(each => new RetentionFormResponseModel
                {
                    Key = each.Form.Key,
                    Name = each.Form.Name,
                    Trashed = each.Form.TrashedUtc is not null,
                    Submitted = each.Rules.Submitted,
                    Approved = each.Rules.Approved,
                    Rejected = each.Rules.Rejected,
                    Spam = each.Rules.Spam,
                    StoreDays = each.Rules.StoreDays,
                }),
            ],
        });
    }

    /// <summary>
    /// What retention deletes within the span by the days given, saved or not yet, per form with those a legal hold keeps
    /// apart; it deletes nothing. A day out of range, or a span other than 7, 30 or 90 days, answers <c>400</c>.
    /// </summary>
    /// <param name="request">The days per state and the span.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("dry-run")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings | FormBuilderPermissions.PrivacyTools, AnyOf = true)]
    [ProducesResponseType(typeof(RetentionDryRunResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DryRun(RetentionDryRunRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var site = request.Days?.ToSite() ?? (await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)).Value.Retention ?? new SiteRetention();
        var counted = await dryRun.CountAsync(site, request.Span, cancellationToken);
        return Ok(new RetentionDryRunResponseModel
        {
            Span = request.Span,
            Entries = counted.Entries,
            Held = counted.Held,
            Files = counted.Files,
            Forms =
            [
                .. counted.Forms.Select(form => new RetentionDryRunFormResponseModel
                {
                    Key = form.Form.Key,
                    Name = form.Form.Name,
                    Trashed = form.Form.TrashedUtc is not null,
                    Entries = form.Entries,
                    Held = form.Held,
                    Files = form.Files,
                }),
            ],
        });
    }
}

/// <summary>The days entries are kept in each state, 1 to 36,500, or 0 for kept for good, which the host allows or not.</summary>
public sealed class RetentionDaysResponseModel
{
    /// <summary>Submitted entries, those waiting for approval among them.</summary>
    public required int Submitted { get; init; }

    /// <summary>Approved entries.</summary>
    public required int Approved { get; init; }

    /// <summary>Rejected entries.</summary>
    public required int Rejected { get; init; }

    /// <summary>Entries in Spam.</summary>
    public required int Spam { get; init; }

    internal static RetentionDaysResponseModel Of(SiteRetention retention) => new()
    {
        Submitted = retention.Submitted,
        Approved = retention.Approved,
        Rejected = retention.Rejected,
        Spam = retention.Spam,
    };

}

/// <summary>The days to count a dry run with, each 1 to 36,500, or 0 for kept for good.</summary>
public sealed class RetentionDaysRequestModel
{
    /// <summary>Submitted entries, those waiting for approval among them.</summary>
    [Range(SiteRetention.ForGood, SiteRetention.MaxDays)]
    public required int Submitted { get; init; }

    /// <summary>Approved entries.</summary>
    [Range(SiteRetention.ForGood, SiteRetention.MaxDays)]
    public required int Approved { get; init; }

    /// <summary>Rejected entries.</summary>
    [Range(SiteRetention.ForGood, SiteRetention.MaxDays)]
    public required int Rejected { get; init; }

    /// <summary>Entries in Spam.</summary>
    [Range(SiteRetention.ForGood, SiteRetention.MaxDays)]
    public required int Spam { get; init; }

    internal SiteRetention ToSite() => new() { Submitted = Submitted, Approved = Approved, Rejected = Rejected, Spam = Spam };
}

/// <summary>Privacy > Retention as it reads (ADR-0222).</summary>
public sealed class RetentionResponseModel
{
    /// <summary>The site's days per state, with the host's rules applied.</summary>
    public required RetentionDaysResponseModel Days { get; init; }

    /// <summary>The privacy section's version, which its save sends back.</summary>
    public required int Version { get; init; }

    /// <summary>Whether the host lets a state keep its entries for good.</summary>
    public required bool KeepForGood { get; init; }

    /// <summary>The configuration key that would allow it.</summary>
    public required string KeepForGoodKey { get; init; }

    /// <summary>The configuration key that locks the whole privacy section, or none.</summary>
    public string? LockedBy { get; init; }

    /// <summary>The forms with retention of their own, by name, those in the Recycle Bin among them.</summary>
    public required IReadOnlyList<RetentionFormResponseModel> Forms { get; init; }
}

/// <summary>A form with retention of its own: its days per state where it sets them, and its days of storage.</summary>
public sealed class RetentionFormResponseModel
{
    /// <summary>The form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is in the Recycle Bin.</summary>
    public required bool Trashed { get; init; }

    /// <summary>Its days of submitted entries, or none for the site's.</summary>
    public int? Submitted { get; init; }

    /// <summary>Its days of approved entries, or none for the site's.</summary>
    public int? Approved { get; init; }

    /// <summary>Its days of rejected entries, or none for the site's.</summary>
    public int? Rejected { get; init; }

    /// <summary>Its days of entries in Spam, or none for the site's.</summary>
    public int? Spam { get; init; }

    /// <summary>The days it keeps any entry from its submission, or none.</summary>
    public int? StoreDays { get; init; }
}

/// <summary>A dry run to count.</summary>
public sealed class RetentionDryRunRequestModel
{
    /// <summary>The days per state to count with, not saved yet; none for those saved.</summary>
    public RetentionDaysRequestModel? Days { get; init; }

    /// <summary>The span: 7, 30 or 90 days.</summary>
    [AllowedValues(7, 30, 90)]
    public int Span { get; init; } = 30;
}

/// <summary>What retention deletes within the span (ADR-0222).</summary>
public sealed class RetentionDryRunResponseModel
{
    /// <summary>The span counted, in days.</summary>
    public required int Span { get; init; }

    /// <summary>The entries it deletes.</summary>
    public required long Entries { get; init; }

    /// <summary>The entries whose time comes within the span that a legal hold keeps past it.</summary>
    public required long Held { get; init; }

    /// <summary>The uploaded files of the entries it deletes.</summary>
    public required long Files { get; init; }

    /// <summary>The forms with entries to go or held, most to go first.</summary>
    public required IReadOnlyList<RetentionDryRunFormResponseModel> Forms { get; init; }
}

/// <summary>What retention deletes of one form within the span.</summary>
public sealed class RetentionDryRunFormResponseModel
{
    /// <summary>The form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it is in the Recycle Bin.</summary>
    public required bool Trashed { get; init; }

    /// <summary>The entries it deletes.</summary>
    public required long Entries { get; init; }

    /// <summary>The entries a legal hold keeps past the span.</summary>
    public required long Held { get; init; }

    /// <summary>The uploaded files of the entries it deletes.</summary>
    public required long Files { get; init; }
}
