using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.AI.Marks;

/// <summary>
/// The "Drafted by AI, please review" mark (ADR-0018): the version of each form the add-on drafted, kept in
/// <c>jfbKeyValue</c>. The designer shows the mark while the open draft is still that version, so the editor's first save
/// ends it without a write here; a form deleted for good, however it goes, takes its mark with it.
/// </summary>
internal sealed class DraftMarks(IKeyValueRepository keyValues)
{
    /// <summary>Marks a form's draft version as the add-on's.</summary>
    public Task MarkAsync(Guid formKey, int draftVersion, CancellationToken cancellationToken) =>
        keyValues.SetAsync(KeyOf(formKey), draftVersion.ToString(CultureInfo.InvariantCulture), cancellationToken);

    /// <summary>The version the add-on drafted of a form, or null for a form it did not draft.</summary>
    public async Task<int?> GetAsync(Guid formKey, CancellationToken cancellationToken) =>
        int.TryParse(await keyValues.GetAsync(KeyOf(formKey), cancellationToken), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;

    /// <summary>Removes a form's mark.</summary>
    public Task RemoveAsync(Guid formKey, CancellationToken cancellationToken) => keyValues.DeleteAsync(KeyOf(formKey), cancellationToken);

    // Keys are lowercase (jfbKeyValue's rule), which a GUID's "D" form is.
    private static string KeyOf(Guid formKey) => FormBuilderAI.MarkPrefix + formKey.ToString("D");
}

/// <summary>
/// Removes the mark of a form deleted for good: on its own, with its folder, or as the trash is emptied, so no mark
/// outlives its form and none shows on a form imported later with the same key.
/// </summary>
internal sealed class DraftMarkCleanup(DraftMarks marks)
    : INotificationAsyncHandler<FormDeletedNotification>, INotificationAsyncHandler<FolderDeletedNotification>, INotificationAsyncHandler<TrashEmptiedNotification>
{
    /// <inheritdoc />
    public Task HandleAsync(FormDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return marks.RemoveAsync(notification.FormKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(FolderDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RemoveAsync(notification.Forms, cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(TrashEmptiedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RemoveAsync(notification.Forms, cancellationToken);
    }

    private async Task RemoveAsync(IEnumerable<FormSummary> forms, CancellationToken cancellationToken)
    {
        foreach (var form in forms)
        {
            await marks.RemoveAsync(form.Key, cancellationToken);
        }
    }
}
