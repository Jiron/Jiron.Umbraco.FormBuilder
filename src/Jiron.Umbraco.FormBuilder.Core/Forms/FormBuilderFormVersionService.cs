using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The versions of a form (ADR-0045, ADR-0053): the History, one version, the comparison of two, a restore into a new
/// draft, pins and comments. Changes take the steps of every change to a form (<see cref="IFormBuilderFormService"/>):
/// a unit of work of their own, the cancelable notification, one unit with the change and its audit entry, then the
/// notification that it happened. A missing form or version gives null.
/// </summary>
internal interface IFormBuilderFormVersionService
{
    /// <summary>
    /// A page of a form's versions, newest first: the publishes, and the draft saves too when <paramref name="includeDrafts"/>,
    /// each with what changed (ADR-0045). Null when the form does not exist.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page holds more than <see cref="FormBuilderFormVersionService.MaxVersionsPerPage"/> versions.</exception>
    Task<Page<FormVersionEntry>?> ListVersionsAsync(Guid key, bool includeDrafts, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>A version of a form with its definition, or null when the form or the version does not exist.</summary>
    Task<FormVersionDocument?> GetVersionAsync(Guid key, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// What changed from one version of a form to another, both read and upgraded first (ADR-0045). Null when the form or
    /// a version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">A stored version cannot be read.</exception>
    Task<FormComparison?> CompareAsync(Guid key, int fromNumber, int toNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a version's definition into a new draft; the current draft stays in the History (ADR-0045). A version whose
    /// definition is the draft's writes nothing. Null when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderValidationException">A save refuses the version's definition, for example above a lowered limit.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the save.</exception>
    Task<FormVersionResult?> RestoreVersionAsync(Guid key, int expectedConcurrencyVersion, int number, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins or unpins a version, so the cleanup keeps it. The form's concurrency version stays as it is (ADR-0045). Null
    /// when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormVersion?> PinVersionAsync(Guid key, int number, bool isPinned, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a version's comment; null, empty or blank clears it. The form's concurrency version stays as it is
    /// (ADR-0045). Null when the form or the version does not exist.
    /// </summary>
    /// <exception cref="FormBuilderValidationException">The comment is longer than 500 characters.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FormVersion?> CommentVersionAsync(Guid key, int number, string? comment, Guid? userKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class FormBuilderFormVersionService(
    IFormRepository forms,
    FormDocuments documents,
    FormDefinitionSerializer serializer,
    FormBuilderChanges<FormBuilderFormVersionService> changes)
    : IFormBuilderFormVersionService
{
    /// <summary>The most versions one page of the version list holds, since each comes with what changed.</summary>
    public const int MaxVersionsPerPage = 50;

    /// <summary>The audit action of a restore.</summary>
    public const string VersionRestoredAction = "form.versionRestored";

    /// <summary>The audit action of a pin.</summary>
    public const string VersionPinnedAction = "form.versionPinned";

    /// <summary>The audit action of an unpin.</summary>
    public const string VersionUnpinnedAction = "form.versionUnpinned";

    /// <summary>The audit action of a changed comment.</summary>
    public const string VersionCommentedAction = "form.versionCommented";

    /// <inheritdoc />
    public async Task<Page<FormVersionEntry>?> ListVersionsAsync(Guid key, bool includeDrafts, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page.Take, MaxVersionsPerPage, nameof(page));
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        // One row past the page: the version before the page's last one.
        var listed = await forms.ListVersionsAsync(form.Id, includeDrafts ? null : FormVersionKind.Published, new PageRequest(page.Skip, page.Take + 1), cancellationToken);
        var rows = listed.Items;
        var items = rows.Take(page.Take).ToList();
        var before = new Dictionary<int, int?>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            before[item.Number] = item.Kind == FormVersionKind.Draft
                ? rows.Skip(i + 1).FirstOrDefault()?.Number
                : rows.Skip(i + 1).FirstOrDefault(row => row.Kind == FormVersionKind.Published)?.Number
                    ?? await forms.GetLastPublishedNumberAsync(form.Id, item.Number, cancellationToken);
        }

        var numbers = items.Select(item => item.Number).Concat(before.Values.OfType<int>()).ToHashSet();
        var definitions = (await forms.GetVersionsAsync(form.Id, numbers, cancellationToken))
            .ToDictionary(version => version.Info.Number, version => serializer.ReadStored(version.Definition).Definition);
        var entries = items
            .Select(item => new FormVersionEntry(Version(item, form), Changes(definitions, before[item.Number], item.Number)))
            .ToList();
        return new Page<FormVersionEntry>(entries, listed.Total);
    }

    /// <inheritdoc />
    public async Task<FormVersionDocument?> GetVersionAsync(Guid key, int number, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form || await forms.GetVersionAsync(form.Id, number, cancellationToken) is not { } version)
        {
            return null;
        }

        var read = serializer.ReadStored(version.Definition);
        var problems = read.Definition is { } definition ? serializer.CheckSave(definition) : read.Problems;
        return new FormVersionDocument(Version(version.Info, form), read.Definition, problems);
    }

    /// <inheritdoc />
    public async Task<FormComparison?> CompareAsync(Guid key, int fromNumber, int toNumber, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        var versions = await forms.GetVersionsAsync(form.Id, [fromNumber, toNumber], cancellationToken);
        if (versions.FirstOrDefault(version => version.Info.Number == fromNumber) is not { } older
            || versions.FirstOrDefault(version => version.Info.Number == toNumber) is not { } newer)
        {
            return null;
        }

        return new FormComparison(fromNumber, toNumber, DefinitionDiff.Compare(documents.Readable(older), documents.Readable(newer)));
    }

    /// <inheritdoc />
    public async Task<FormVersionResult?> RestoreVersionAsync(Guid key, int expectedConcurrencyVersion, int number, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormBuilderFormService.OwnUnit);
        if (await forms.GetAsync(key, cancellationToken) is not { } form || await forms.GetVersionAsync(form.Id, number, cancellationToken) is not { } version)
        {
            return null;
        }

        // A restore is a draft save of an older definition, held to the site's limits now (ADR-0042).
        FormChecks.Current(form, expectedConcurrencyVersion);
        var definition = documents.Readable(version);
        if (definition.Key != form.Key)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, JsonPaths.Property(JsonPaths.Root, "key"), "The version's key is not the form's.");
        }

        documents.CheckSaveable(definition);
        var document = documents.Document(definition);
        var draft = await documents.DraftOfAsync(form, cancellationToken);
        if (draft is { Info.SchemaVersion: FormDefinition.CurrentSchemaVersion } && draft.Info.DefinitionHash == document.DefinitionHash)
        {
            return new FormVersionResult(await documents.InfoAsync(form, cancellationToken), null);
        }

        await changes.CancelableAsync(new FormSavingNotification(definition, form.Alias, form.Name, isNew: false, userKey, new EventMessages()) { RestoredFromVersion = number });
        var saved = await changes.WriteAsync(
            () => forms.SaveDraftAsync(form.Id, expectedConcurrencyVersion, document, null, userKey, cancellationToken),
            stored => stored is null
                ? []
                : [FormDocuments.Entry(VersionRestoredAction, stored.Form, userKey, new AuditDetails().Add("version", stored.Version.Info.Number).Add("from", number))],
            cancellationToken);
        if (saved is null)
        {
            return null;
        }

        var info = await documents.InfoAsync(saved.Form, cancellationToken);
        await changes.PublishedAsync(new FormSavedNotification(info.Key, info.Alias, info.Name, saved.Version.Info.Number, isNew: false, userKey) { RestoredFromVersion = number }, FormBuilderFormService.Subject(info.Key));
        return new FormVersionResult(info, saved.Version.Info.Number);
    }

    /// <inheritdoc />
    public async Task<FormVersion?> PinVersionAsync(Guid key, int number, bool isPinned, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormBuilderFormService.OwnUnit);
        if (await VersionToChangeAsync(key, number, cancellationToken) is not { } found)
        {
            return null;
        }

        var (form, version) = found;
        if (version.IsPinned == isPinned)
        {
            return Version(version, form);
        }

        return await UpdateVersionAsync(
            form,
            version,
            version.Comment,
            isPinned,
            userKey,
            () => forms.PinVersionAsync(form.Id, number, isPinned, cancellationToken),
            isPinned ? VersionPinnedAction : VersionUnpinnedAction,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormVersion?> CommentVersionAsync(Guid key, int number, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(FormBuilderFormService.OwnUnit);
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment;
        FormChecks.Comment(text);
        if (await VersionToChangeAsync(key, number, cancellationToken) is not { } found)
        {
            return null;
        }

        var (form, version) = found;
        if (string.Equals(version.Comment, text, StringComparison.Ordinal))
        {
            return Version(version, form);
        }

        return await UpdateVersionAsync(
            form,
            version,
            text,
            version.IsPinned,
            userKey,
            () => forms.CommentVersionAsync(form.Id, number, text, cancellationToken),
            VersionCommentedAction,
            cancellationToken);
    }

    // A version as the service answers with it.
    private static FormVersion Version(FormVersionInfo info, StoredForm form) => new(
        info.Number,
        info.Kind,
        info.PublicRevision,
        info.Comment,
        info.IsPinned,
        info.CreatedUtc,
        info.CreatedByUserKey,
        info.PublishedUtc,
        info.PublishedByUserKey,
        form.DraftVersionId == info.Id,
        form.PublishedVersionId == info.Id);

    // What changed into a version from the one before it, or null for the first, or when one cannot be read.
    private static List<DefinitionChangeSummary>? Changes(Dictionary<int, FormDefinition?> definitions, int? before, int number) =>
        before is { } previous
        && definitions.GetValueOrDefault(previous) is { } older
        && definitions.GetValueOrDefault(number) is { } newer
            ? DefinitionDiff.Compare(older, newer).Select(change => change.Summarize()).ToList()
            : null;

    // The form and the version a pin or a comment changes: null when either is missing; a form in the trash is refused.
    private async Task<(StoredForm Form, FormVersionInfo Version)?> VersionToChangeAsync(Guid key, int number, CancellationToken cancellationToken)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.OutsideTrash(form);
        var versions = await forms.GetVersionsAsync(form.Id, [number], cancellationToken);
        return versions.Count == 1 ? (form, versions[0].Info) : null;
    }

    // A pin or a comment: the cancelable notification, one unit with the change and its audit entry, then the notification
    // that it happened. The form's row stays as it is.
    private async Task<FormVersion?> UpdateVersionAsync(
        StoredForm form,
        FormVersionInfo version,
        string? comment,
        bool isPinned,
        Guid? userKey,
        Func<Task<FormVersionInfo?>> change,
        string action,
        CancellationToken cancellationToken)
    {
        await changes.CancelableAsync(new FormVersionUpdatingNotification(form.Key, form.Alias, form.Name, version.Number, comment, isPinned, userKey, new EventMessages()));
        var updated = await changes.WriteAsync(
            change,
            info => info is null ? [] : [FormDocuments.Entry(action, form, userKey, new AuditDetails().Add("version", info.Number).Add("hasComment", info.Comment is not null))],
            cancellationToken);
        if (updated is null)
        {
            return null;
        }

        await changes.PublishedAsync(new FormVersionUpdatedNotification(form.Key, form.Alias, form.Name, updated.Number, updated.Comment, updated.IsPinned, userKey), FormBuilderFormService.Subject(form.Key));
        return Version(updated, form);
    }
}
