using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// Options fetched as JSON from a web address (ADR-0076): the list at <c>listPath</c> in the answer, each element's
/// value at <c>valueFrom</c> and label at <c>labelFrom</c>. Only hosts the host configuration allows are called, and
/// what is fetched is kept per configuration, with the last copy in use while the address fails.
/// </summary>
/// <param name="fetcher">What fetches and keeps the options.</param>
internal sealed class UrlOptionKind(UrlOptionFetcher fetcher) : OptionKind<UrlOptionListSettings>(OptionListKind.Url, onField: true, keeping: OptionKeeping.ByConfiguration)
{
    /// <summary>The longest address.</summary>
    public const int MaxUrlLength = 2048;

    /// <inheritdoc />
    /// <remarks>Each server calls the address at most once a minute for a list, as every page view may ask.</remarks>
    public override int MinCacheSeconds => 60;

    /// <inheritdoc />
    protected override void CheckSettings(UrlOptionListSettings settings, OptionKindCheck check)
    {
        if (settings.Url is { } url && !IsAddress(url))
        {
            check.Invalid("url", "An address is absolute, http or https, with no user name, password or '#' part.");
        }

        foreach (var (setting, path) in new[] { ("listPath", settings.ListPath), ("valueFrom", settings.ValueFrom), ("labelFrom", settings.LabelFrom) })
        {
            if (OptionJsonPath.Parse(path) is null)
            {
                check.Invalid(setting, $"A path is $, then parts such as .name, ['a name'] or [0], not '{path}'.");
            }
        }
    }

    /// <inheritdoc />
    protected override ValueTask CheckReferencesAsync(UrlOptionListSettings settings, OptionKindCheck check, CancellationToken cancellationToken)
    {
        if (settings.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var address))
        {
            switch (fetcher.Outbound.Refusal(address))
            {
                case OutboundFailure.SchemeNotAllowed:
                    check.Invalid("url", "The site calls http addresses only when JironFormBuilder:Webhooks:AllowHttp is on; use https.");
                    break;
                case OutboundFailure.HostNotAllowed:
                    check.Invalid("url", $"The site does not call the host '{address.IdnHost}': its host configuration names the hosts it calls in JironFormBuilder:Webhooks:AllowedHosts.");
                    break;
                case OutboundFailure.PortNotAllowed:
                    var entry = address.HostNameType == UriHostNameType.IPv6 ? $"[{address.IdnHost}]" : address.IdnHost;
                    check.Invalid("url", string.Create(CultureInfo.InvariantCulture, $"The site does not call the host '{address.IdnHost}' on the port {address.Port}: its host configuration names the ports it calls besides the standard one in JironFormBuilder:Webhooks:AllowedHosts, as '{entry}:{address.Port}'."));
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(UrlOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken)
    {
        var keep = request.CacheSeconds is { } seconds ? TimeSpan.FromSeconds(Math.Max(seconds, MinCacheSeconds)) : UrlOptionFetcher.FieldKeep;
        return await fetcher.GetAsync(settings, keep, request.Owner, request.OwnerKey, cancellationToken);
    }

    // An address as the configuration may hold it, its length held by the setting's rule; the host configuration's rules
    // are the check on the site's.
    private static bool IsAddress(string url) =>
        url.AsSpan().IndexOfAny(" \t\r\n") < 0
        && Uri.TryCreate(url, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
        && address.UserInfo.Length == 0
        && address.Fragment.Length == 0
        && address.IdnHost.Length > 0;
}

/// <summary>
/// Fetches and keeps a URL list's options (ADR-0076): one copy per configuration and server, fresh for the keep time
/// the request gives; one fetch per configuration and server at a time, which runs without any caller's cancellation;
/// and while a fetch fails, the last copy for another minute, logged as such, or with no copy before it, no options for
/// that minute, without asking the address again (ADR-0081). A keep time is a minute at least.
/// </summary>
/// <param name="outbound">The calls to other hosts.</param>
/// <param name="appCaches">The runtime cache that keeps the copies.</param>
/// <param name="time">The clock of the keep times.</param>
/// <param name="logger">Where failed fetches are logged, with their reason and never the address.</param>
internal sealed partial class UrlOptionFetcher(OutboundHttp outbound, AppCaches appCaches, TimeProvider time, ILogger<UrlOptionFetcher> logger)
{
    /// <summary>How long a field's own URL source keeps what it fetched; a list keeps it for its <c>cacheSeconds</c>.</summary>
    public static readonly TimeSpan FieldKeep = TimeSpan.FromMinutes(10);

    /// <summary>How long the last copy is used after a fetch fails, before the next fetch.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    // A copy nobody asks for is gone after this; every fetch, failed or not, keeps the copy for this long again.
    private static readonly TimeSpan CopyDuration = TimeSpan.FromDays(31);

    private readonly ConcurrentDictionary<string, Lazy<Task<UrlOptionCopy>>> _fetches = new(StringComparer.Ordinal);

    /// <summary>The calls to other hosts, whose host rules the kind's check of the site holds an address to.</summary>
    public OutboundHttp Outbound => outbound;

    /// <summary>A fetch's time limit and cap: 4 seconds, below a source's 5, and 2 MiB.</summary>
    internal OutboundLimits Limits { get; init; } = new(TimeSpan.FromSeconds(4), 2 * 1024 * 1024);

    /// <summary>The options of the configuration, fresh for <paramref name="keep"/>.</summary>
    /// <param name="settings">The configuration.</param>
    /// <param name="keep">How long a copy is fresh.</param>
    /// <param name="owner">What asks, for the log: <c>field</c> or <c>list</c>.</param>
    /// <param name="ownerKey">Its key, for the log.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting; the fetch goes on for the others.</param>
    public async Task<IReadOnlyList<FormBuilderOption>> GetAsync(UrlOptionListSettings settings, TimeSpan keep, string owner, Guid ownerKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var key = OptionSiteCacheKeys.Url(settings);
        var copy = Fresh(key, keep);
        if (copy is null)
        {
            Lazy<Task<UrlOptionCopy>>? started = null;
            started = new Lazy<Task<UrlOptionCopy>>(() => Detached(() => FetchAsync(key, settings, keep, owner, ownerKey, () => _fetches.TryRemove(KeyValuePair.Create(key, started!)))));
            copy = await _fetches.GetOrAdd(key, started).Value.WaitAsync(cancellationToken);
        }

        return copy.Failed ? throw new OptionsUnavailableException() : copy.Options;
    }

    // A fetch shared by every caller, so it runs on the thread pool in no caller's context or cancellation.
    private static Task<T> Detached<T>(Func<Task<T>> run)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            return Task.Run(run, CancellationToken.None);
        }

        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(run, CancellationToken.None);
        }
    }

    private UrlOptionCopy? Fresh(string key, TimeSpan keep)
    {
        var now = time.GetUtcNow();
        return appCaches.RuntimeCache.GetCacheItem<UrlOptionCopy>(key) is { } copy && (now < copy.FetchedUtc + keep || now < copy.RetryUtc) ? copy : null;
    }

    private async Task<UrlOptionCopy> FetchAsync(string key, UrlOptionListSettings settings, TimeSpan keep, string owner, Guid ownerKey, Action done)
    {
        try
        {
            // A fetch that ended as this one started has left a fresh copy.
            if (Fresh(key, keep) is { } fresh)
            {
                return fresh;
            }

            var address = Uri.TryCreate(settings.Url, UriKind.Absolute, out var parsed) ? parsed : throw new FormBuilderOutboundException(OutboundFailure.AddressInvalid);
            var body = await outbound.GetAsync(address, "application/json", Limits, CancellationToken.None);
            var copy = new UrlOptionCopy(Read(body, settings), time.GetUtcNow(), DateTimeOffset.MinValue);
            appCaches.RuntimeCache.Insert(key, () => copy, CopyDuration);
            return copy;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var reason = exception is FormBuilderOutboundException { Status: { } status } refused
                ? string.Create(CultureInfo.InvariantCulture, $"{refused.Reason} {status}")
                : exception is FormBuilderOutboundException outboundFailure ? outboundFailure.Reason.ToString() : exception.GetType().FullName;
            if (appCaches.RuntimeCache.GetCacheItem<UrlOptionCopy>(key) is { Failed: false } last)
            {
                var retry = last with { RetryUtc = time.GetUtcNow() + RetryAfter };
                appCaches.RuntimeCache.Insert(key, () => retry, CopyDuration);
                LogLastCopy(logger, owner, ownerKey, reason, last.FetchedUtc);
                return retry;
            }

            // No copy to stand in: the failure itself stands for a minute, so a failing address costs one call a minute.
            var failed = new UrlOptionCopy([], DateTimeOffset.MinValue, time.GetUtcNow() + RetryAfter) { Failed = true };
            appCaches.RuntimeCache.Insert(key, () => failed, CopyDuration);
            LogFailed(logger, owner, ownerKey, reason);
            throw;
        }
        finally
        {
            done();
        }
    }

    // The options in an answer: the list at listPath, each element's value and label at their paths.
    private static List<FormBuilderOption> Read(byte[] body, UrlOptionListSettings settings)
    {
        if (OptionJsonPath.Parse(settings.ListPath) is not { } listPath || OptionJsonPath.Parse(settings.ValueFrom) is not { } valueFrom || OptionJsonPath.Parse(settings.LabelFrom) is not { } labelFrom)
        {
            throw new FormBuilderOutboundException(OutboundFailure.AnswerUnreadable);
        }

        ReadOnlyMemory<byte> json = body;
        if (json.Span.StartsWith(ByteOrderMark))
        {
            json = json[3..];
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (!listPath.TrySelect(document.RootElement, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                throw new FormBuilderOutboundException(OutboundFailure.AnswerUnreadable);
            }

            // One more than a field offers, so cleaning counts what is left out (ADR-0073).
            var options = new List<FormBuilderOption>();
            foreach (var element in list.EnumerateArray())
            {
                if (options.Count > OptionListService.MaxOptions)
                {
                    break;
                }

                var value = valueFrom.TrySelect(element, out var found) ? OptionJsonPath.Text(found) : null;
                var label = labelFrom.TrySelect(element, out var labelled) ? OptionJsonPath.Text(labelled) : null;
                options.Add(new FormBuilderOption(value ?? string.Empty, label));
            }

            return options;
        }
        catch (JsonException exception)
        {
            throw new FormBuilderOutboundException(OutboundFailure.AnswerUnreadable, innerException: exception);
        }
    }

    // UTF-8's byte order mark, which some services write before their JSON.
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    [LoggerMessage(Level = LogLevel.Warning, Message = "The JSON address of the {Owner} {OwnerKey} failed ({Reason}), so it offers the options fetched at {FetchedUtc} for now")]
    private static partial void LogLastCopy(ILogger logger, string owner, Guid ownerKey, string? reason, DateTimeOffset fetchedUtc);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The JSON address of the {Owner} {OwnerKey} failed ({Reason}) and has no options fetched before, so it offers none for a minute")]
    private static partial void LogFailed(ILogger logger, string owner, Guid ownerKey, string? reason);
}

/// <summary>
/// A URL list's options as a server keeps them, with when they were fetched and, after a failed fetch, until when the
/// last copy stands in; or, after a fetch failed with no copy before it, the failure until then (<see cref="Failed"/>).
/// </summary>
internal sealed record UrlOptionCopy(IReadOnlyList<FormBuilderOption> Options, DateTimeOffset FetchedUtc, DateTimeOffset RetryUtc)
{
    /// <summary>Whether this stands for a failed fetch with no options before it, until <see cref="RetryUtc"/>.</summary>
    public bool Failed { get; init; }
}

/// <summary>The configuration of a URL list (ADR-0076).</summary>
public sealed class UrlOptionListSettings
{
    /// <summary>The address, absolute, <c>https</c> (or <c>http</c> when the host allows it), on a host the site calls.</summary>
    [Required]
    [StringLength(UrlOptionKind.MaxUrlLength)]
    [FormBuilderSetting("#jfbOptionListSettings_url", Description = "#jfbOptionListSettings_urlHelp")]
    public string? Url { get; init; }

    /// <summary>Where the list is in the answer; the answer itself by default.</summary>
    [StringLength(OptionJsonPath.MaxLength)]
    [FormBuilderSetting("#jfbOptionListSettings_urlListPath", Description = "#jfbOptionListSettings_urlListPathHelp", Order = 1)]
    public string ListPath { get; init; } = "$";

    /// <summary>Where an option's value is in each element of the list.</summary>
    [StringLength(OptionJsonPath.MaxLength)]
    [FormBuilderSetting("#jfbOptionListSettings_urlValueFrom", Description = "#jfbOptionListSettings_urlValueFromHelp", Order = 2)]
    public string ValueFrom { get; init; } = "$.value";

    /// <summary>Where an option's label is in each element of the list; the value shows without one.</summary>
    [StringLength(OptionJsonPath.MaxLength)]
    [FormBuilderSetting("#jfbOptionListSettings_urlLabelFrom", Description = "#jfbOptionListSettings_urlLabelFromHelp", Order = 3)]
    public string LabelFrom { get; init; } = "$.label";
}
