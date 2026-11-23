using Jiron.Umbraco.FormBuilder.AI.Profiles;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Connections;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;
using Umbraco.AI.Extensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;

/// <summary>
/// Gives a host a connection on the fake provider and a chat profile on it, once, and makes that profile the add-on's
/// choice in Settings > AI: what a site's administrator does with a real provider (ADR-0018).
/// </summary>
internal sealed class FakeAISetup(IAIConnectionService connections, IAIProfileService profiles, AIProfileChoice choice)
{
    /// <summary>The fake connection's alias.</summary>
    public const string ConnectionAlias = "jfb-fake";

    /// <summary>The fake chat profile's alias.</summary>
    public const string ProfileAlias = "jfb-fake-chat";

    /// <summary>The fake chat profile, created when missing; chosen in Settings > AI when <paramref name="choose"/> and nothing is chosen yet.</summary>
    public async Task<AIProfile> EnsureAsync(bool choose, CancellationToken cancellationToken = default)
    {
        var connection = await connections.GetConnectionByAliasAsync(ConnectionAlias, cancellationToken)
            ?? await connections.SaveConnectionAsync(
                new AIConnection { Alias = ConnectionAlias, Name = "Fake AI (tests and demo)", ProviderId = FormBuilderFakeProvider.ProviderId, Settings = new FormBuilderFakeProviderSettings() },
                cancellationToken);
        var profile = await profiles.GetProfileByAliasAsync(ProfileAlias, cancellationToken)
            ?? await profiles.SaveProfileAsync(
                new AIProfile
                {
                    Alias = ProfileAlias,
                    Name = "Fake chat (tests and demo)",
                    Model = new AIModelRef(FormBuilderFakeProvider.ProviderId, FormBuilderFakeProvider.ChatModelId),
                    ConnectionId = connection.Id,
                    Settings = new AIChatProfileSettings(),
                },
                cancellationToken);
        if (choose && await choice.GetChosenAsync(cancellationToken) is null)
        {
            await choice.ChooseAsync(profile.Id, null, cancellationToken);
        }

        return profile;
    }
}

/// <summary>
/// Offers the fake provider as the host's testing options say (<see cref="FormBuilderTestingOptions.FakeAI"/>):
/// Umbraco.AI finds the provider by its attribute, so a host that leaves it off excludes it; a host that offers it gets
/// the journal and the set-up, and with <see cref="FakeAIMode.Chosen"/> the set-up runs once Umbraco runs.
/// </summary>
public sealed class FormBuilderTestingAIComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var mode = builder.Config.GetSection(FormBuilderTestingOptions.SectionName).GetValue<FakeAIMode>(nameof(FormBuilderTestingOptions.FakeAI));
        if (mode == FakeAIMode.Off)
        {
            builder.AIProviders().Exclude<FormBuilderFakeProvider>();
            return;
        }

        builder.Services.TryAddSingleton<FakeChatJournal>();
        builder.Services.TryAddSingleton<FakeAISetup>();
        if (mode == FakeAIMode.Chosen)
        {
            builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, FakeAIStartup>();
            builder.Services.AddStartupSeeder(FakeAIStartup.SeederName);
        }
    }
}

/// <summary>
/// Sets up the fake connection and chat profile once Umbraco runs, and chooses the profile in Settings > AI when nothing
/// is chosen yet (<see cref="FakeAIMode.Chosen"/>). It logs <c>Jiron Form Builder fake AI ready</c> when done, which the
/// browser tests' harness waits for, or <c>Jiron Form Builder fake AI failed</c> with the reason. Either way it reports
/// its end to <see cref="StartupSeeding"/>.
/// </summary>
/// <remarks>
/// Umbraco.AI makes its tables in a handler of the same notification, and the handlers run one after the other in an
/// order that differs between systems (ROADMAP 26.1: on Linux this one came first and found no table). So the set-up
/// runs beside the handlers rather than inside this one, which would hold up Umbraco.AI's, and tries again each second
/// until the tables are there, for at most <see cref="Limit"/>; it logs that it waits, and why, after a first failure.
/// Hosts that share a database start together too, so a set-up that loses the race to create the profile finds it on
/// its next try.
/// </remarks>
internal sealed partial class FakeAIStartup(IRuntimeState runtimeState, FakeAISetup setup, TimeProvider time, StartupSeeding seeding, IHostApplicationLifetime lifetime, ILogger<FakeAIStartup> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    /// <summary>The name this seeder is announced and reports with (<see cref="StartupSeeding"/>).</summary>
    public const string SeederName = "fake AI";

    /// <summary>How long the set-up keeps trying: Umbraco.AI's migrations take a few seconds on a fresh database.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    /// <summary>The pause between two tries.</summary>
    internal static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run)
        {
            seeding.Report(SeederName);
            return Task.CompletedTask;
        }

        _ = Task.Run(SetUpAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it succeeds, a pause apart, for at most <paramref name="limit"/>: its result,
    /// or the last failure once the limit is reached. <paramref name="waiting"/> hears of the first failure.
    /// </summary>
    internal static async Task<(T? Result, Exception? Failure, int Attempts)> TryUntilAsync<T>(
        Func<CancellationToken, Task<T>> attempt,
        TimeSpan limit,
        TimeSpan pause,
        TimeProvider time,
        Action<Exception> waiting,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        for (var attempts = 1; ; attempts++)
        {
            try
            {
                return (await attempt(cancellationToken), null, attempts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (time.GetElapsedTime(started) + pause > limit)
                {
                    return (default, exception, attempts);
                }

                if (attempts == 1)
                {
                    waiting(exception);
                }
            }

            await Task.Delay(pause, time, cancellationToken);
        }
    }

    private async Task SetUpAsync()
    {
        try
        {
            var (profile, failure, attempts) = await TryUntilAsync(
                token => setup.EnsureAsync(choose: true, token),
                Limit,
                Pause,
                time,
                exception => LogWaiting(logger, exception.Message, Limit.TotalSeconds),
                lifetime.ApplicationStopping);
            if (profile is not null)
            {
                LogReady(logger, profile.Alias);
            }
            else
            {
                // The host still serves everything else; the harness reports this line, reason and all.
                LogFailed(logger, $"{failure!.Message} (still after {attempts} tries in {Limit.TotalSeconds:0} s)");
            }
        }
        catch (OperationCanceledException)
        {
            // The host stops.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(logger, exception.Message);
        }
        finally
        {
            seeding.Report(SeederName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder fake AI ready: the chat profile {Alias}")]
    private static partial void LogReady(ILogger logger, string alias);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder fake AI waits for Umbraco.AI's tables, at most {Seconds} s: {Reason}")]
    private static partial void LogWaiting(ILogger logger, string reason, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder fake AI failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);
}
