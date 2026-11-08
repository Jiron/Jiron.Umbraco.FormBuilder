using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>A row of the library's list of option lists (ADR-0214): the list's summary and how many forms use it.</summary>
/// <param name="List">The list, its kind and how many options a list that holds them has.</param>
/// <param name="Row">The stored list, for when it last changed.</param>
/// <param name="UsedBy">How many forms' drafts or live versions use it, forms in the recycle bin included.</param>
internal sealed record OptionListRow(OptionListSummary List, StoredOptionList Row, int UsedBy);

/// <summary>The forms that use a list, as one user may see them (ADR-0214).</summary>
/// <param name="Forms">The forms the user may open, by name.</param>
/// <param name="Others">How many more forms use it that the user may not open, named nowhere.</param>
internal sealed record OptionListUsers(IReadOnlyList<LibraryUser> Forms, int Others)
{
    /// <summary>How many forms use the list in all.</summary>
    public int Count => Forms.Count + Others;
}

/// <summary>A list's contents as an editor holds them, resolved for the preview, or why they give no options.</summary>
/// <param name="Options">The options; null when the contents do not read.</param>
/// <param name="Problems">Why the contents do not read, each at its path, as a save would say it.</param>
internal sealed record OptionListPreview(FormBuilderResolvedOptions? Options, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>What this server keeps of a URL list's address (ADR-0214).</summary>
/// <param name="FetchedUtc">When the copy in use was fetched; null when no fetch has given options yet.</param>
/// <param name="Count">How many options that copy holds.</param>
/// <param name="Failing">Whether the latest fetch failed, so the copy stands in, or no options are offered for now.</param>
internal sealed record UrlListFetch(DateTimeOffset? FetchedUtc, int Count, bool Failing);

/// <summary>
/// The library's option lists as their screens work with them (ROADMAP 12.1, ADR-0214): the rows with how many forms use
/// each, which forms those are as a user may see them, the changes a user makes, each audited, a duplicate under a free
/// alias, the preview of contents not saved yet, and a URL list's copy, which Fetch now drops on every server. The lists'
/// checks and caches are <see cref="OptionListService"/>'s.
/// </summary>
internal sealed class OptionListLibrary(
    OptionListService lists,
    IOptionListRepository repository,
    ILibraryUseRepository uses,
    IFormRepository forms,
    IFormBuilderAccessService access,
    FormBuilderOptionService options,
    UrlOptionFetcher urls,
    IAuditRepository audit)
{
    /// <summary>The audit action of a list created, a duplicate's included.</summary>
    public const string CreatedAction = "optionList.created";

    /// <summary>The audit action of a list saved.</summary>
    public const string SavedAction = "optionList.saved";

    /// <summary>The audit action of a list deleted.</summary>
    public const string DeletedAction = "optionList.deleted";

    /// <summary>The audit action of Fetch now.</summary>
    public const string FetchedAction = "optionList.fetched";

    private const string EntityType = "optionList";

    /// <summary>A page of the lists by name, each with how many forms use it.</summary>
    public async Task<Page<OptionListRow>> RowsAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await repository.ListAsync(null, page, cancellationToken);
        var counts = await uses.FormCountsAsync(LibraryUseKind.OptionList, cancellationToken);
        return new Page<OptionListRow>([.. rows.Items.Select(row => new OptionListRow(lists.Summary(row), row, counts.GetValueOrDefault(row.Key)))], rows.Total);
    }

    /// <summary>
    /// The forms whose draft or live version uses the list, those the user may open by name and the rest counted, so a
    /// restricted form's name never shows to someone who may not see it.
    /// </summary>
    public async Task<OptionListUsers> UsersAsync(AccessUser user, Guid key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await uses.UsersAsync(LibraryUseKind.OptionList, key, cancellationToken);
        if (all.Count == 0)
        {
            return new OptionListUsers([], 0);
        }

        var visibility = await access.GetFormVisibilityAsync(user, cancellationToken: cancellationToken);
        var seen = new HashSet<Guid>();
        if (visibility.IsEverything)
        {
            seen.UnionWith(all.Select(form => form.FormKey));
        }
        else if (!visibility.IsNothing)
        {
            foreach (var chunk in all.Select(form => form.FormKey).Chunk(Math.Min(SqlArgs.MaxListLength, PageRequest.MaxTake)))
            {
                var found = await forms.ListAsync(
                    new FormFilter { AllFolders = true, Trashed = null, Visibility = visibility, Keys = chunk },
                    new PageRequest(0, chunk.Length),
                    cancellationToken);
                seen.UnionWith(found.Items.Select(form => form.Key));
            }
        }

        var shown = all.Where(form => seen.Contains(form.FormKey)).ToList();
        return new OptionListUsers(shown, all.Count - shown.Count);
    }

    /// <summary>Creates a list for a user, audited; the problems that refuse it otherwise.</summary>
    public async Task<OptionListResult> CreateAsync(Guid key, OptionListInput input, Guid? userKey, CancellationToken cancellationToken = default)
    {
        var created = await lists.CreateAsync(key, input, cancellationToken);
        if (created.List is { } list)
        {
            await AuditAsync(CreatedAction, list, userKey, null);
        }

        return created;
    }

    /// <summary>
    /// Changes a list for a user, audited; no list and no problems when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile.
    /// </summary>
    public async Task<OptionListResult> UpdateAsync(Guid key, int expectedConcurrencyVersion, OptionListInput input, Guid? userKey, CancellationToken cancellationToken = default)
    {
        var changed = await lists.UpdateAsync(key, expectedConcurrencyVersion, input, cancellationToken);
        if (changed.List is { } list)
        {
            await AuditAsync(SavedAction, list, userKey, null);
        }

        return changed;
    }

    /// <summary>
    /// Deletes a list for a user, audited; false when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile, and with
    /// <see cref="FormBuilderConflict.InUse"/> while a form uses it.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(key, cancellationToken) is not { } row || !await lists.DeleteAsync(key, expectedConcurrencyVersion, cancellationToken))
        {
            return false;
        }

        await AuditAsync(DeletedAction, row, userKey, null);
        return true;
    }

    /// <summary>
    /// A copy of a list under a new key, with the name given and the first free alias after its own (<c>departments2</c>),
    /// audited as created; null when the list does not exist.
    /// </summary>
    public async Task<OptionListResult?> DuplicateAsync(Guid key, string name, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (await repository.GetAsync(key, cancellationToken) is not { Kind: { } kind } source)
        {
            return null;
        }

        var input = new OptionListInput(await FreeAliasAsync(source.Alias, cancellationToken), name, kind, source.Config, source.Items, source.CacheSeconds);
        var created = await lists.CreateAsync(Guid.NewGuid(), input, cancellationToken);
        if (created.List is { } list)
        {
            await AuditAsync(CreatedAction, list, userKey, source.Key);
        }

        return created;
    }

    /// <summary>
    /// The options of contents an editor holds, for the preview: as a stored list of those contents offers them in the
    /// culture, or the problems a save would refuse them with.
    /// </summary>
    public async Task<OptionListPreview> PreviewAsync(OptionListKind kind, string config, string items, int cacheSeconds, Guid owner, string culture, CancellationToken cancellationToken = default)
    {
        var contents = await lists.ReadContentsAsync(kind, config, items, cancellationToken);
        if (contents.Kind is not { } found || contents.Problems.Count > 0)
        {
            return new OptionListPreview(null, contents.Problems);
        }

        var keep = found.UsesCacheSeconds ? Math.Clamp(cacheSeconds, found.MinCacheSeconds, OptionListService.MaxCacheSeconds) : 0;
        return new OptionListPreview(await options.PreviewAsync(found, contents.Config, contents.Items, keep, owner, culture, cancellationToken), []);
    }

    /// <summary>What this server keeps of a URL list's address; null for a list that does not exist or fetches nothing.</summary>
    public async Task<UrlListFetch?> FetchStateAsync(Guid key, CancellationToken cancellationToken = default) =>
        await UrlSettingsAsync(key, cancellationToken) is { } settings ? State(urls.Kept(settings)) : null;

    /// <summary>
    /// Fetch now: every server drops the copy of a URL list's address, and this one fetches it again at once, audited; what
    /// it keeps then, or null for a list that does not exist or fetches nothing.
    /// </summary>
    public async Task<UrlListFetch?> FetchNowAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await UrlSettingsAsync(key, cancellationToken) is not { } settings || await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return null;
        }

        lists.FetchAgain(key, settings);
        _ = await options.GetListAsync(key, new FormBuilderOptionContext(string.Empty), cancellationToken);
        await AuditAsync(FetchedAction, row, userKey, null);
        return State(urls.Kept(settings));
    }

    private static UrlListFetch State(UrlOptionCopy? copy) => copy switch
    {
        null => new UrlListFetch(null, 0, Failing: false),
        { Failed: true } => new UrlListFetch(null, 0, Failing: true),
        _ => new UrlListFetch(copy.FetchedUtc, copy.Options.Count, Failing: copy.RetryUtc > copy.FetchedUtc),
    };

    private async Task<UrlOptionListSettings?> UrlSettingsAsync(Guid key, CancellationToken cancellationToken) =>
        await lists.GetAsync(key, cancellationToken) is { IsReadable: true, Kind: UrlOptionKind url } entry ? url.Read(entry.Config) : null;

    // The list's alias with the first number after it that no list has, its stem shortened to leave room for the number.
    private async Task<string> FreeAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var stem = alias.Length > 60 ? alias[..60] : alias;
        var taken = (await repository.ListAliasesAsync(stem, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var number = 2; ; number++)
        {
            var candidate = stem + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private Task<StoredAuditEntry> AuditAsync(string action, StoredOptionList list, Guid? userKey, Guid? duplicateOf)
    {
        var details = new AuditDetails().AddIdentifier("alias", list.Alias);
        if (list.Kind is { } kind)
        {
            details.Add("kind", kind);
        }

        if (duplicateOf is { } source)
        {
            details.Add("duplicateOf", source);
        }

        return audit.AddAsync(new NewAuditEntry(action, EntityType, list.Key, userKey, details), CancellationToken.None);
    }
}
