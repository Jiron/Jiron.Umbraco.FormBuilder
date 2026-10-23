using Examine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Examine;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// The line the browser tests' start-up wait looks for (ROADMAP 3.4c, ADR-0031). About a minute after it starts,
/// Umbraco 17 rebuilds its search indexes: it clears them and runs every index populator in turn, and it logs nothing
/// when the last one is done. This populator is registered after Umbraco's own, for Umbraco's standard indexes, so it
/// runs last, indexes nothing, and logs <see cref="Marker"/>. The line appears whether the Content Delivery API is on
/// or off. Before it logs, it checks that it is still the last populator; when a later registration put another one
/// after it, it logs <see cref="NotLast"/> instead, so the wait fails with the reason rather than starting too early.
/// </summary>
public sealed partial class StartupMarkerPopulator : IndexPopulator
{
    /// <summary>The start of the line logged when a rebuild has run every populator.</summary>
    public const string Marker = "Jiron Form Builder test host: indexes rebuilt";

    /// <summary>The start of the line logged instead when another populator runs after this one.</summary>
    public const string NotLast = "Jiron Form Builder test host: the index marker is not the last index populator";

    private readonly IServiceProvider _services;
    private readonly ILogger<StartupMarkerPopulator> _logger;

    /// <summary>Registers the populator for <see cref="IndexNames"/>.</summary>
    public StartupMarkerPopulator(IServiceProvider services, ILogger<StartupMarkerPopulator> logger)
    {
        _services = services;
        _logger = logger;
        foreach (var name in IndexNames)
        {
            RegisterIndex(name);
        }
    }

    /// <summary>
    /// Umbraco's standard indexes, each of which has populators of its own: registering only for these leaves the set
    /// of indexes a rebuild clears as it is.
    /// </summary>
    public static IReadOnlyList<string> IndexNames { get; } =
    [
        Constants.UmbracoIndexes.InternalIndexName,
        Constants.UmbracoIndexes.ExternalIndexName,
        Constants.UmbracoIndexes.MembersIndexName,
        Constants.UmbracoIndexes.DeliveryApiContentIndexName,
    ];

    /// <inheritdoc />
    protected override void PopulateIndexes(IReadOnlyList<IIndex> indexes)
    {
        if (indexes.Count == 0)
        {
            return;
        }

        // The rebuilder runs the registered populators in this order, so the last one is the last to run.
        if (!ReferenceEquals(_services.GetServices<IIndexPopulator>().LastOrDefault(), this))
        {
            LogNotLast(_logger);
            return;
        }

        var names = string.Join(", ", indexes.Select(index => index.Name));
        LogRebuilt(_logger, names);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = Marker + " ({Indexes})")]
    private static partial void LogRebuilt(ILogger logger, string indexes);

    [LoggerMessage(Level = LogLevel.Error, Message = NotLast + "; the browser tests' start-up wait cannot rely on it")]
    private static partial void LogNotLast(ILogger logger);
}
