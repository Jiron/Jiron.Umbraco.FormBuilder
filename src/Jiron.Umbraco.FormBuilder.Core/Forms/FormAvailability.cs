using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>Why a form takes no entry now (ADR-0088).</summary>
internal enum FormUnavailable
{
    /// <summary>Before its opening time or from its closing time on (<c>form_closed</c>).</summary>
    Closed,

    /// <summary>It holds as many entries as its limit allows (<c>entry_limit_reached</c>).</summary>
    Full,
}

/// <summary>
/// Whether a form takes entries now (section 4.3.4, ADR-0084, ADR-0088): from its opening time, up to but not at its
/// closing time, by the site's clock, and while the entries that count toward its limit (<see cref="EntryLimits"/>)
/// are fewer than the limit. The submission pipeline, the upload endpoint and the form endpoint's <c>open</c> ask
/// alike; the pipeline counts again inside the transaction that stores the entry.
/// </summary>
/// <param name="forms">The forms, which give a form's row.</param>
/// <param name="entries">The entries, which count toward the limit.</param>
/// <param name="time">The site's clock.</param>
internal sealed class FormAvailability(IFormRepository forms, IEntryRepository entries, TimeProvider time)
{
    /// <summary>Whether the form is open at this instant by its opening and closing times.</summary>
    /// <param name="availability">The form's availability settings.</param>
    /// <param name="now">The instant.</param>
    public static bool IsOpen(FormAvailabilitySettings availability, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(availability);
        return (availability.OpensAt is not { } opens || now.UtcDateTime >= opens) && (availability.ClosesAt is not { } closes || now.UtcDateTime < closes);
    }

    /// <summary>Why the form takes no entry now, or null when it takes one; the entries are counted only when its times let it.</summary>
    /// <param name="availability">The form's availability settings.</param>
    /// <param name="formKey">The form's key; a form without a row counts as holding no entries.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<FormUnavailable?> CheckAsync(FormAvailabilitySettings availability, Guid formKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (!IsOpen(availability, time.GetUtcNow()))
        {
            return FormUnavailable.Closed;
        }

        return availability.MaxEntries is null || await forms.GetAsync(formKey, cancellationToken) is not { } form
            ? null
            : await CheckLimitAsync(availability, form.Id, cancellationToken);
    }

    /// <summary>Why the form takes no entry now, or null when it takes one, for a form whose row the caller holds.</summary>
    /// <param name="availability">The form's availability settings.</param>
    /// <param name="formId">The form's row.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<FormUnavailable?> CheckAsync(FormAvailabilitySettings availability, int formId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(availability);
        return IsOpen(availability, time.GetUtcNow()) ? await CheckLimitAsync(availability, formId, cancellationToken) : FormUnavailable.Closed;
    }

    private async Task<FormUnavailable?> CheckLimitAsync(FormAvailabilitySettings availability, int formId, CancellationToken cancellationToken) =>
        availability.MaxEntries is { } limit && await entries.CountTowardsLimitAsync(formId, cancellationToken) >= limit ? FormUnavailable.Full : null;
}
