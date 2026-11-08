using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Deliveries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Activity;

/// <summary>
/// Activity > Deliveries (section 4.4, ADR-0219): every mail and every run that queued none, problems first, with their
/// details and a retry. Reading needs <c>manageSettings</c>; a retry also needs <c>manageActions</c> on the delivery's form,
/// and a retry of an uncertain one an explicit confirmation, as it may already have arrived.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/delivery")]
[ApiExplorerSettings(GroupName = "Delivery")]
internal sealed class DeliveryController(DeliveryLog log, IFormBuilderFormService forms, IFormBuilderAccessService access, TimeProvider time) : FormBuilderManagementApiController
{
    /// <summary>The most deliveries one page holds.</summary>
    public const int MaxTake = 100;

    /// <summary>The configuration key of the limits of visitor mail.</summary>
    public const string MailLimitsKey = "JironFormBuilder:Limits:Mail";

    /// <summary>
    /// A page of the log, problems first (failed, trying again, uncertain, limited), each group the newest first, and whether
    /// the host pauses sending. <c>400</c> for a filter it does not know.
    /// </summary>
    /// <param name="states"><c>problems</c>, <c>waiting</c>, <c>delivered</c> or <c>skipped</c>; none for all.</param>
    /// <param name="kind"><c>email</c>, <c>webhook</c> or <c>other</c>; none for all.</param>
    /// <param name="form">Only this form's deliveries.</param>
    /// <param name="days">Only what was queued in the last so many days, 1 to 3,660; none for all.</param>
    /// <param name="search">Text a mail's recipients or a form's name holds, at most 200 characters.</param>
    /// <param name="skip">The deliveries to pass over.</param>
    /// <param name="take">The deliveries to read, 1 to 100.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(DeliveryListResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List(
        string? states = null,
        string? kind = null,
        Guid? form = null,
        int? days = null,
        string? search = null,
        long skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        DeliveryStates? statesFilter = null;
        DeliveryKinds? kindFilter = null;
        if (states is not null)
        {
            if (!EnumNames<DeliveryStates>.ByName.TryGetValue(states, out var parsed))
            {
                ModelState.AddModelError(nameof(states), "unknown");
            }

            statesFilter = parsed;
        }

        if (kind is not null)
        {
            if (!EnumNames<DeliveryKinds>.ByName.TryGetValue(kind, out var parsed))
            {
                ModelState.AddModelError(nameof(kind), "unknown");
            }

            kindFilter = parsed;
        }

        if (days is < 1 or > 3660)
        {
            ModelState.AddModelError(nameof(days), "range");
        }

        if (search is { Length: > DeliveryQuery.MaxSearchLength })
        {
            ModelState.AddModelError(nameof(search), "length");
        }

        if (skip < 0 || take is < 1 or > MaxTake)
        {
            ModelState.AddModelError(skip < 0 ? nameof(skip) : nameof(take), "range");
        }

        if (!ModelState.IsValid)
        {
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        var query = new DeliveryQuery
        {
            States = statesFilter,
            Kind = kindFilter,
            FormKey = form,
            FromUtc = days is { } span ? time.GetUtcNow().AddDays(-span) : null,
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
        };
        var page = await log.QueryAsync(query, new PageRequest(skip, take), cancellationToken);
        return Ok(new DeliveryListResponseModel
        {
            Items = [.. page.Items.Select(DeliveryResponseModel.Of)],
            Total = page.Total,
            Paused = log.Paused,
        });
    }

    /// <summary>A mail's details, whether this user may retry it, and for a limited one the limit that held it back.</summary>
    /// <param name="id">The mail.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("email/{id:long}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(DeliveryDetailResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmail(long id, CancellationToken cancellationToken = default) =>
        await log.GetEmailAsync(id, cancellationToken) is { } item
            ? Ok(await DetailAsync(item, cancellationToken))
            : FormBuilderProblems.DeliveryNotFound();

    /// <summary>A run's details and whether this user may retry it.</summary>
    /// <param name="id">The run.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("run/{id:long}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(DeliveryDetailResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(long id, CancellationToken cancellationToken = default) =>
        await log.GetRunAsync(id, cancellationToken) is { } item
            ? Ok(await DetailAsync(item, cancellationToken))
            : FormBuilderProblems.DeliveryNotFound();

    /// <summary>
    /// Queues a mail that failed or is uncertain again, due now with its attempts starting over. <c>403</c> without
    /// <c>manageActions</c> on the form of its entry, <c>409</c> for an uncertain one without the confirmation or one that
    /// can't be retried.
    /// </summary>
    /// <param name="id">The mail.</param>
    /// <param name="request">Whether the user confirmed that an uncertain mail may arrive a second time.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("email/{id:long}/retry")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(DeliveryDetailResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryEmail(long id, ActionConfirmationRequestModel? request = null, CancellationToken cancellationToken = default)
    {
        if (await log.GetEmailAsync(id, cancellationToken) is not { } item)
        {
            return FormBuilderProblems.DeliveryNotFound();
        }

        if (!await MayRetryAsync(item.Row, cancellationToken))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
        }

        return await AnswerAsync(await log.RetryEmailAsync(id, request?.Confirmed == true, RequestAccess.User.Key, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Queues a run that failed or is uncertain again, as the entry's retry does. <c>403</c> without <c>manageActions</c> on its
    /// form, <c>409</c> for an uncertain one without the confirmation or one that can't be retried.
    /// </summary>
    /// <param name="id">The run.</param>
    /// <param name="request">Whether the user confirmed that an uncertain run may go out a second time.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("run/{id:long}/retry")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(DeliveryDetailResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryRun(long id, ActionConfirmationRequestModel? request = null, CancellationToken cancellationToken = default)
    {
        if (await log.GetRunAsync(id, cancellationToken) is not { } item)
        {
            return FormBuilderProblems.DeliveryNotFound();
        }

        if (!await MayRetryAsync(item.Row, cancellationToken))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
        }

        return await AnswerAsync(await log.RetryRunAsync(id, request?.Confirmed == true, RequestAccess.User.Key, cancellationToken), cancellationToken);
    }

    private async Task<IActionResult> AnswerAsync(DeliveryRetry retry, CancellationToken cancellationToken) => retry.Status switch
    {
        ActionRunRetryStatus.Queued when retry.Item is { } item => Ok(await DetailAsync(item, cancellationToken)),
        ActionRunRetryStatus.NeedsConfirmation => FormBuilderProblems.ActionRetryNeedsConfirmation(),
        ActionRunRetryStatus.NotRetryable => FormBuilderProblems.ActionRunNotRetryable(),
        _ => FormBuilderProblems.DeliveryNotFound(),
    };

    private async Task<DeliveryDetailResponseModel> DetailAsync(DeliveryItem item, CancellationToken cancellationToken)
    {
        var row = item.Row;
        var retryable = row.State is DeliveryState.Failed or DeliveryState.Uncertain;
        return new DeliveryDetailResponseModel
        {
            Delivery = DeliveryResponseModel.Of(item),
            Retryable = retryable,
            MayRetry = retryable && await MayRetryAsync(row, cancellationToken),
            Limit = row.State == DeliveryState.Limited ? LimitOf(row.Error?.Code) : null,
        };
    }

    // The limit a limited mail names; the count is what the host sets now, which held it back unless it changed since.
    private DeliveryLimitResponseModel? LimitOf(string? code)
    {
        var limits = log.MailLimits;
        return code switch
        {
            EmailRepository.RecipientLimit => new DeliveryLimitResponseModel { Kind = "recipient", Count = limits.PerRecipientPerDay, Key = $"{MailLimitsKey}:{nameof(limits.PerRecipientPerDay)}" },
            EmailRepository.SiteLimit => new DeliveryLimitResponseModel { Kind = "site", Count = limits.PerSitePerHour, Key = $"{MailLimitsKey}:{nameof(limits.PerSitePerHour)}" },
            _ => null,
        };
    }

    // manageActions on the delivery's form, as the entry's retry needs (ADR-0013); a mail of no entry needs manageSettings,
    // which reading the log takes already. A form that is gone leaves nothing to retry.
    private async Task<bool> MayRetryAsync(DeliveryRow row, CancellationToken cancellationToken)
    {
        if (row.FormKey is not { } formKey)
        {
            return row.IsEmail && row.EntryKey is null;
        }

        if (await forms.GetAsync(formKey, cancellationToken) is not { } info)
        {
            return false;
        }

        var held = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(info), cancellationToken);
        return held.HasFlag(FormBuilderPermissions.ManageActions);
    }
}
