using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// One server of a site with option lists (ADR-0073): its sources, the library's lists in memory, a real runtime cache,
/// the distributed cache over a messenger that records what it is told, Umbraco's dictionary, content, document types
/// and data types, the calls to other hosts (ADR-0076), and loggers that keep what is logged.
/// </summary>
internal sealed class OptionListTestSite
{
    /// <summary>A site with these sources, or with the standard lists when none are given, that reaches no other host.</summary>
    public OptionListTestSite(params IFormBuilderOptionSource[] sources)
        : this(new OptionListTestHosts(), sources)
    {
    }

    /// <summary>A site that calls other hosts as <paramref name="hosts"/> says, with these sources or the standard lists.</summary>
    public OptionListTestSite(OptionListTestHosts hosts, params IFormBuilderOptionSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        IFormBuilderOptionSource[] registered = sources.Length > 0 ? sources : Standard();
        Collection = new FormBuilderOptionSourceCollection(() => registered);
        Sources = new OptionSourceRegistry(Collection);
        UmbracoReads = new OptionListUmbraco(Umbraco.Contents, Umbraco.Navigation, PublicAccess, ContentTypes, DataTypes, Umbraco.Languages, new MergeTagCultures(), Caches);
        var host = new FormBuilderOptions();
        foreach (var allowed in hosts.AllowedHosts ?? [])
        {
            host.Webhooks.AllowedHosts.Add(allowed);
        }

        host.Webhooks.AllowHttp = hosts.AllowHttp;
        var time = hosts.Time ?? TimeProvider.System;

        // Without a transport of the test's own, no name resolves, so nothing leaves the machine.
        Outbound = new OutboundHttp(hosts.Transport ?? new FormBuilderHttpTransport((_, _) => Task.FromResult(Array.Empty<IPAddress>()), null), MsOptions.Create(host), time);
        Fetcher = hosts.Limits is { } limits ? new UrlOptionFetcher(Outbound, Caches, time, UrlLogs) { Limits = limits } : new UrlOptionFetcher(Outbound, Caches, time, UrlLogs);
        Kinds = new OptionKindRegistry(Sources, UmbracoReads, Fetcher);
        Refresher = new FormBuilderOptionListCacheRefresher(Caches, Substitute.For<IJsonSerializer>(), Substitute.For<IEventAggregator>(), Substitute.For<ICacheRefresherNotificationFactory>());
        Lists = new OptionListService(Repository, Kinds, Caches, new DistributedCache(Messenger, new CacheRefresherCollection(() => [Refresher])), ListLogs);
    }

    /// <summary>The library's lists.</summary>
    public MemoryOptionLists Repository { get; } = new();

    /// <summary>Umbraco's languages and dictionary, which labels that name a dictionary item read, and its content.</summary>
    public TestUmbraco Umbraco { get; } = new();

    /// <summary>Which pages need a member to sign in.</summary>
    public IPublicAccessService PublicAccess { get; } = Substitute.For<IPublicAccessService>();

    /// <summary>The document types.</summary>
    public IContentTypeService ContentTypes { get; } = Substitute.For<IContentTypeService>();

    /// <summary>The data types.</summary>
    public IDataTypeService DataTypes { get; } = Substitute.For<IDataTypeService>();

    /// <summary>What content and data type lists read from Umbraco.</summary>
    public OptionListUmbraco UmbracoReads { get; }

    /// <summary>The calls to other hosts.</summary>
    public OutboundHttp Outbound { get; }

    /// <summary>What fetches and keeps URL lists' options.</summary>
    public UrlOptionFetcher Fetcher { get; }

    /// <summary>What the URL lists' fetches logged.</summary>
    public RecordingLogger<UrlOptionFetcher> UrlLogs { get; } = new();

    /// <summary>The server's caches, with a real runtime cache.</summary>
    public AppCaches Caches { get; } = new(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance));

    /// <summary>What tells the other servers of a change.</summary>
    public IServerMessenger Messenger { get; } = Substitute.For<IServerMessenger>();

    /// <summary>The collection of the site's sources.</summary>
    public FormBuilderOptionSourceCollection Collection { get; }

    /// <summary>The sources by name.</summary>
    public OptionSourceRegistry Sources { get; }

    /// <summary>The kinds.</summary>
    public OptionKindRegistry Kinds { get; }

    /// <summary>The cache refresher, as another server runs it.</summary>
    public FormBuilderOptionListCacheRefresher Refresher { get; }

    /// <summary>The library's lists as the service keeps them.</summary>
    public OptionListService Lists { get; }

    /// <summary>What the list service logged.</summary>
    public RecordingLogger<OptionListService> ListLogs { get; } = new();

    /// <summary>What the option service logged.</summary>
    public RecordingLogger<FormBuilderOptionService> Logs { get; } = new();

    /// <summary>The keys of the lists the other servers were told of, oldest first.</summary>
    public IReadOnlyList<Guid> Told => [.. Messenger.ReceivedCalls()
        .Select(call => call.GetArguments())
        .Where(arguments => ReferenceEquals(arguments[0], Refresher))
        .SelectMany(arguments => arguments[1] as OptionListCacheChange[] ?? [])
        .Select(change => change.Key)];

    /// <summary>The standard lists, in the order the collection holds them.</summary>
    public static IFormBuilderOptionSource[] Standard() =>
        [new MonthsOptionSource(), new WeekdaysOptionSource(), new AgreementOptionSource(), new SatisfactionOptionSource(), new YesNoNotSureOptionSource()];

    /// <summary>The option service of the site, with the real time unless a test gives its own.</summary>
    public FormBuilderOptionService Options(TimeProvider? time = null) =>
        new(Lists, Kinds, Umbraco.DictionaryTexts(), Caches, time ?? TimeProvider.System, Logs);
}

/// <summary>How a test site calls other hosts (ADR-0076).</summary>
/// <param name="AllowedHosts">The host configuration's allowed hosts; none by default.</param>
/// <param name="AllowHttp">Whether <c>http</c> is allowed.</param>
/// <param name="Transport">The transport; by default one that resolves no name.</param>
/// <param name="Time">The clock of the time limits and keep times; the real time by default.</param>
/// <param name="Limits">A fetch's time limit and cap, if not a URL list's own.</param>
internal sealed record OptionListTestHosts(string[]? AllowedHosts = null, bool AllowHttp = false, IFormBuilderHttpTransport? Transport = null, TimeProvider? Time = null, OutboundLimits? Limits = null);

/// <summary>The library's option lists in memory, with the refusals the database gives and a count of reads by key.</summary>
internal sealed class MemoryOptionLists : IOptionListRepository
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly Lock _lock = new();
    private readonly List<StoredOptionList> _rows = [];
    private int _reads;

    /// <summary>How often a list was read by its key.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>The refusal the next create or update gives, as the database's unique indexes do; null for none.</summary>
    public FormBuilderConflict? Conflict { get; set; }

    /// <summary>Stores a row as it is, past the service's checks: a list another version wrote, say.</summary>
    public StoredOptionList Write(Guid key, OptionListKind? kind, string config = "{}", string items = "[]", int cacheSeconds = 0)
    {
        lock (_lock)
        {
            var row = new StoredOptionList(_rows.Count + 1, key, "list" + (_rows.Count + 1), "A list", kind, config, items, cacheSeconds, 1, Now, Now);
            _rows.Add(row);
            return row;
        }
    }

    /// <inheritdoc />
    public Task<StoredOptionList> CreateAsync(NewOptionList list, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        Refuse();
        lock (_lock)
        {
            var row = new StoredOptionList(_rows.Count + 1, list.Key, list.Alias, list.Name, list.Kind, list.Config, list.Items, list.CacheSeconds, 1, Now, Now);
            _rows.Add(row);
            return Task.FromResult(row);
        }
    }

    /// <inheritdoc />
    public Task<StoredOptionList?> GetAsync(Guid key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        lock (_lock)
        {
            return Task.FromResult(_rows.FirstOrDefault(row => row.Key == key));
        }
    }

    /// <inheritdoc />
    public Task<StoredOptionList?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.FirstOrDefault(row => row.Id == id));
        }
    }

    /// <inheritdoc />
    public Task<StoredOptionList?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.FirstOrDefault(row => string.Equals(row.Alias, alias, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <inheritdoc />
    public Task<Page<StoredOptionList>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The option list tests list no lists.");

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<string>>([.. _rows.Select(row => row.Alias).Where(alias => alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)]);
        }
    }

    /// <inheritdoc />
    public Task<StoredOptionList?> UpdateAsync(OptionListUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        Refuse();
        lock (_lock)
        {
            var index = _rows.FindIndex(row => row.Id == update.Id);
            if (index < 0)
            {
                return Task.FromResult<StoredOptionList?>(null);
            }

            if (_rows[index].ConcurrencyVersion != update.ExpectedConcurrencyVersion)
            {
                throw new FormBuilderConcurrencyException("JFB_TEST: the list changed meanwhile.");
            }

            var changed = _rows[index] with
            {
                Alias = update.Alias,
                Name = update.Name,
                Kind = update.Kind,
                Config = update.Config,
                Items = update.Items,
                CacheSeconds = update.CacheSeconds,
                ConcurrencyVersion = update.ExpectedConcurrencyVersion + 1,
            };
            _rows[index] = changed;
            return Task.FromResult<StoredOptionList?>(changed);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _rows.FindIndex(row => row.Id == id);
            if (index >= 0 && _rows[index].ConcurrencyVersion != expectedConcurrencyVersion)
            {
                throw new FormBuilderConcurrencyException("JFB_TEST: the list changed meanwhile.");
            }

            if (index >= 0)
            {
                _rows.RemoveAt(index);
            }

            return Task.FromResult(index >= 0);
        }
    }

    private void Refuse()
    {
        if (Conflict is { } conflict)
        {
            Conflict = null;
            throw new FormBuilderConflictException(conflict, "JFB_TEST: the database refused the row.");
        }
    }
}

/// <summary>A logger that keeps what is logged, with the exception passed, if any.</summary>
/// <typeparam name="T">Whose logger it is.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly Lock _lock = new();
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    /// <summary>The entries so far, oldest first.</summary>
    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_lock)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>A source from code whose options a test gives, which keeps the requests it got.</summary>
/// <param name="name">Its name.</param>
/// <param name="options">Its options for a request; it may throw or wait.</param>
internal sealed class TestOptionSource(string name, Func<FormBuilderOptionRequest, CancellationToken, ValueTask<IReadOnlyList<FormBuilderOption>>> options) : IFormBuilderOptionSource
{
    private readonly Lock _lock = new();
    private readonly List<FormBuilderOptionRequest> _requests = [];

    /// <summary>A source that gives these options to every request.</summary>
    public TestOptionSource(string name, params FormBuilderOption[] given)
        : this(name, (_, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(given))
    {
    }

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string Label => "Test source";

    /// <summary>The requests so far, oldest first.</summary>
    public IReadOnlyList<FormBuilderOptionRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _requests.Add(request);
        }

        return options(request, cancellationToken);
    }
}

/// <summary>Fields and settings as the option list tests write them.</summary>
internal static class OptionListTesting
{
    /// <summary>The key of the field the tests resolve.</summary>
    public static readonly Guid FieldKey = Guid.Parse("73000000-0000-4000-8000-000000000001");

    /// <summary>The key of the parent field of a cascading field.</summary>
    public static readonly Guid ParentKey = Guid.Parse("73000000-0000-4000-8000-000000000002");

    /// <summary>A dropdown with these options.</summary>
    public static FormField Field(FieldOptions options) =>
        new() { Key = FieldKey, Type = "dropdown", Alias = "choice", Label = new LocalizedText("Choice"), Options = options };

    /// <summary>A dropdown whose options come from a source of the kind, with this configuration.</summary>
    public static FormField Field(OptionSource source, string? config = null, Guid? parent = null) =>
        Field(new FieldOptions { Source = source, Config = config is null ? null : SettingsObject.Parse(config), Parent = parent });

    /// <summary>A text in the default culture and pairs of a culture and its text.</summary>
    public static LocalizedText Text(string fallback, params string[] cultures)
    {
        var texts = new List<KeyValuePair<string, string>> { new(string.Empty, fallback) };
        for (var i = 0; i < cultures.Length; i += 2)
        {
            texts.Add(new KeyValuePair<string, string>(cultures[i], cultures[i + 1]));
        }

        return new LocalizedText(texts);
    }

    /// <summary>The values and labels of resolved options.</summary>
    public static IReadOnlyList<(string Value, string? Label)> Pairs(FormBuilderResolvedOptions resolved) =>
        [.. resolved.Options.Select(option => (option.Value, option.Label))];

    /// <summary>The values of resolved options.</summary>
    public static IReadOnlyList<string> Values(FormBuilderResolvedOptions resolved) => [.. resolved.Options.Select(option => option.Value)];
}
