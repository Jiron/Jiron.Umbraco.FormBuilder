using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Editors;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// How the form picker reports what it picks to Umbraco's reference walk (ADR-0017): one reference per picked form, the
/// form's key under the UDI type <see cref="UdiType"/>, so the walk finds forms picked in blocks and nested content too.
/// Umbraco keeps no relation for it (a form has no node), and logs that at Debug level; <see cref="FormReferenceIndex"/>
/// keeps them.
/// </summary>
internal sealed class FormPickerReferences : IDataValueReferenceFactory, IDataValueReference
{
    /// <summary>The UDI type of a form, registered once at start-up.</summary>
    public const string UdiType = "jfb-form";

    /// <inheritdoc />
    public IDataValueReference GetDataValueReference() => this;

    /// <inheritdoc />
    public bool IsForEditor(IDataEditor? dataEditor) => dataEditor?.Alias == FormPickerPropertyEditor.EditorAlias;

    /// <inheritdoc />
    public IEnumerable<UmbracoEntityReference> GetReferences(object? value) =>
        (FormPickerValues.Read(value) ?? []).Select(key => new UmbracoEntityReference(new GuidUdi(UdiType, key)));

    /// <summary>Registers the UDI type, once however often it is called.</summary>
    public static void RegisterUdiType() => UdiParser.RegisterUdiType(UdiType, global::Umbraco.Cms.Core.UdiType.GuidUdi);
}

/// <summary>
/// The "used on" index (ADR-0017): for a content item, every form its properties name, read by Umbraco's reference walk
/// over each property's edited and published value in each culture (a form used only in a draft counts: publishing the
/// draft would show it), and written over the item's rows in <c>jfbReference</c>.
/// </summary>
/// <param name="factories">Umbraco's reference walk, which reaches into blocks.</param>
/// <param name="editors">Umbraco's property editors.</param>
/// <param name="references">The index's rows.</param>
internal sealed class FormReferenceIndex(DataValueReferenceFactoryCollection factories, PropertyEditorCollection editors, IReferenceRepository references)
{
    /// <summary>Every form a content item's properties name, each once per property and culture.</summary>
    /// <param name="content">The content item.</param>
    public IReadOnlyList<ContentFormReference> Of(IContentBase content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var found = new List<ContentFormReference>();
        foreach (var property in content.Properties)
        {
            if (!editors.TryGet(property.PropertyType.PropertyEditorAlias, out var editor))
            {
                continue;
            }

            foreach (var value in property.Values)
            {
                foreach (var reference in factories.GetReferences(editor, [value.EditedValue, value.PublishedValue]))
                {
                    if (reference.Udi is GuidUdi { EntityType: FormPickerReferences.UdiType } form)
                    {
                        found.Add(new ContentFormReference(form.Guid, property.Alias, value.Culture ?? string.Empty));
                    }
                }
            }
        }

        return [.. found.Distinct()];
    }

    /// <summary>Writes a content item's rows anew from its properties.</summary>
    /// <param name="content">The content item.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task<int> IndexAsync(IContentBase content, CancellationToken cancellationToken = default) =>
        references.ReplaceContentAsync(content.Key, Of(content), cancellationToken);

    /// <summary>
    /// Writes a document's rows anew from a copy, only while its current version is still the one read before the copy
    /// was: the rebuild's write (ADR-0131). Null when the document has changed or gone, and nothing was written.
    /// </summary>
    /// <param name="content">The copy of the document.</param>
    /// <param name="read">The version read before the copy.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task<int?> IndexUnchangedAsync(IContentBase content, DocumentVersion read, CancellationToken cancellationToken = default) =>
        references.ReplaceUnchangedContentAsync(content.Id, read, content.Key, Of(content), cancellationToken);

    /// <summary>Removes a deleted content item's rows.</summary>
    /// <param name="contentKey">The content item's key.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task<int> RemoveAsync(Guid contentKey, CancellationToken cancellationToken = default) =>
        references.DeleteContentAsync(contentKey, cancellationToken);
}

/// <summary>
/// Keeps the "used on" index current (ADR-0017): a content item that is saved, published, unpublished, moved to the
/// recycle bin, moved (a restore among them) or copied gets its rows anew, and a deleted one loses them. It runs once
/// Umbraco's own unit has completed, on the server that made the change; the table is shared, so a load-balanced site
/// needs nothing more. A write that fails is logged and leaves the content as saved: the rebuild repairs the rows.
/// </summary>
internal sealed partial class FormReferenceHandler(FormReferenceIndex index, ILogger<FormReferenceHandler> logger)
    : INotificationAsyncHandler<ContentSavedNotification>,
        INotificationAsyncHandler<ContentPublishedNotification>,
        INotificationAsyncHandler<ContentUnpublishedNotification>,
        INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
        INotificationAsyncHandler<ContentMovedNotification>,
        INotificationAsyncHandler<ContentCopiedNotification>,
        INotificationAsyncHandler<ContentDeletedNotification>
{
    /// <inheritdoc />
    public Task HandleAsync(ContentSavedNotification notification, CancellationToken cancellationToken) =>
        IndexAsync(notification.SavedEntities, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken) =>
        IndexAsync(notification.PublishedEntities, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(ContentUnpublishedNotification notification, CancellationToken cancellationToken) =>
        IndexAsync(notification.UnpublishedEntities, cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(ContentMovedToRecycleBinNotification notification, CancellationToken cancellationToken) =>
        IndexAsync(notification.MoveInfoCollection.Select(move => move.Entity), cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(ContentMovedNotification notification, CancellationToken cancellationToken) =>
        IndexAsync(notification.MoveInfoCollection.Select(move => move.Entity), cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(ContentCopiedNotification notification, CancellationToken cancellationToken) =>
        IndexAsync([notification.Copy], cancellationToken);

    /// <inheritdoc />
    public async Task HandleAsync(ContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        foreach (var content in notification.DeletedEntities)
        {
            await GuardAsync(content.Key, () => index.RemoveAsync(content.Key, cancellationToken), cancellationToken);
        }
    }

    private async Task IndexAsync(IEnumerable<IContent> items, CancellationToken cancellationToken)
    {
        foreach (var content in items)
        {
            await GuardAsync(content.Key, () => index.IndexAsync(content, cancellationToken), cancellationToken);
        }
    }

    private async Task GuardAsync(Guid contentKey, Func<Task<int>> write, CancellationToken cancellationToken)
    {
        try
        {
            await write();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogIndexFailed(logger, contentKey, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not update the forms used by content {ContentKey}; a rebuild of the references repairs them")]
    private static partial void LogIndexFailed(ILogger logger, Guid contentKey, Exception exception);
}
