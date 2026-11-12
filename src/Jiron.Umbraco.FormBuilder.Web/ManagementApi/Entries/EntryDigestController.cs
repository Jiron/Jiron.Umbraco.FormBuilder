using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>A user's choice of the summary email, with when and where it goes.</summary>
public sealed class EntryDigestResponseModel
{
    /// <summary>How often: <c>off</c>, <c>daily</c> or <c>weekly</c> (on Mondays).</summary>
    public required string Frequency { get; init; }

    /// <summary>When it goes out, in the site's time zone, as <c>HH:mm</c>.</summary>
    public required string SendsAt { get; init; }

    /// <summary>The site's time zone, its IANA name where there is one, such as <c>Europe/Berlin</c>.</summary>
    public required string TimeZone { get; init; }

    /// <summary>The user's own address, which it goes to; empty when the user has none.</summary>
    public required string Address { get; init; }
}

/// <summary>How often the user wants the summary email.</summary>
public sealed class EntryDigestRequestModel
{
    /// <summary><c>off</c>, <c>daily</c> or <c>weekly</c>.</summary>
    public string Frequency { get; init; } = string.Empty;
}

/// <summary>Where a test of the summary email went.</summary>
public sealed class EntryDigestTestResponseModel
{
    /// <summary>The user's own address.</summary>
    public required string To { get; init; }
}

/// <summary>
/// The summary email (IA 6.4, ADR-0209): the user's own choice of how often, the mail as it would go out for the period
/// due now, and a test of it, which goes out at once to the user's own address. Every endpoint answers any user of the
/// section, about the user alone: the mail counts only the forms the user may view entries of.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/entries/digest")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class EntryDigestController(
    EntryDigests digests,
    SiteTimeZones timeZones,
    IUserService users,
    TestLimits limits) : FormBuilderManagementApiController
{
    /// <summary>The user's choice, with when and where the summary goes.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryDigestResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) =>
        Ok(await ModelAsync(await digests.GetAsync(RequestAccess.User.Key, cancellationToken), cancellationToken));

    /// <summary>Keeps how often the user gets the summary; the first one covers the first period that ends after this.</summary>
    /// <param name="request">How often.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryDigestResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(EntryDigestRequestModel request, CancellationToken cancellationToken = default)
    {
        var frequency = FrequencyOf(request);
        await digests.SaveAsync(RequestAccess.User.Key, frequency, cancellationToken);
        return Ok(await ModelAsync(frequency, cancellationToken));
    }

    /// <summary>The summary as it would go out for the period due now, in the frequency asked for; nothing is sent.</summary>
    /// <param name="frequency"><c>daily</c> or <c>weekly</c>; <c>off</c> shows the daily one.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("preview")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EmailPreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(string? frequency = null, CancellationToken cancellationToken = default)
    {
        var chosen = FrequencyOf(new EntryDigestRequestModel { Frequency = frequency ?? "daily" });
        var mail = await digests.PreviewAsync(await UserAsync(), chosen, test: false, cancellationToken);
        return Ok(new EmailPreviewResponseModel
        {
            From = mail.From,
            To = mail.To,
            Cc = mail.Cc,
            Bcc = mail.Bcc,
            ReplyTo = mail.ReplyTo,
            Subject = mail.Subject,
            Html = mail.Html,
            Text = mail.Text,
            Direction = "ltr",
        });
    }

    /// <summary>Sends the summary of the period due now to the user's own address at once, marked as a test.</summary>
    /// <param name="request">How often, which picks the period.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("test")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryDigestTestResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Test(EntryDigestRequestModel request, CancellationToken cancellationToken = default)
    {
        if (limits.Refusal(RequestAccess.User.Key, Response) is { } limited)
        {
            return limited;
        }

        var frequency = FrequencyOf(request);
        var user = await UserAsync();
        if (await digests.SendTestAsync(user, frequency, cancellationToken) != EntryDigestOutcome.Queued)
        {
            throw new FormBuilderValidationException([new DefinitionProblem("no_address", "to", "The user has no address to send the summary to.")]);
        }

        return Ok(new EntryDigestTestResponseModel { To = user.Email });
    }

    // The frequency a request names, or the problem of one it can't.
    private static EntryDigestFrequency FrequencyOf(EntryDigestRequestModel? request) =>
        request?.Frequency switch
        {
            "off" => EntryDigestFrequency.Off,
            "daily" => EntryDigestFrequency.Daily,
            "weekly" => EntryDigestFrequency.Weekly,
            _ => throw new FormBuilderValidationException([new DefinitionProblem("invalid_frequency", "frequency", "The summary goes out off, daily or weekly.")]),
        };

    private async Task<IUser> UserAsync() =>
        await users.GetAsync(RequestAccess.User.Key) ?? throw new InvalidOperationException("The signed-in user is missing.");

    private async Task<EntryDigestResponseModel> ModelAsync(EntryDigestFrequency frequency, CancellationToken cancellationToken)
    {
        var zone = await timeZones.GetAsync(null, cancellationToken);
        var user = await users.GetAsync(RequestAccess.User.Key);
        return new EntryDigestResponseModel
        {
            Frequency = frequency switch
            {
                EntryDigestFrequency.Daily => "daily",
                EntryDigestFrequency.Weekly => "weekly",
                _ => "off",
            },
            SendsAt = EntryDigests.SendsAt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            TimeZone = zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id,
            Address = user?.Email ?? string.Empty,
        };
    }
}
