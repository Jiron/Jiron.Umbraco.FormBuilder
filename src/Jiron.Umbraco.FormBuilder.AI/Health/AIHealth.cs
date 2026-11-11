using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.AI.Profiles;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Umbraco.AI.Core.Connections;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Security;

namespace Jiron.Umbraco.FormBuilder.AI.Health;

/// <summary>
/// The AI add-on's health (ADR-0018), which Umbraco's Health Check dashboard and the Form Builder overview both read: the
/// profile Settings > AI resolves exists and can chat, its connection exists and is on, and the connection answers
/// Umbraco.AI's test, which lists models without spending tokens. A test's answer stands five minutes per connection and
/// version, so the overview never calls the provider on every visit.
/// </summary>
internal sealed class AIHealth(AIProfileChoice profiles, IAIConnectionService connections, TimeProvider time) : HealthProbe
{
    /// <summary>How long a connection's test answer stands.</summary>
    public static readonly TimeSpan TestFresh = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(Guid Id, int Version), (DateTimeOffset At, bool Passed)> _tests = new();

    /// <inheritdoc />
    public override async Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var said = AIHealthWords.Of(words.German);
        var state = await profiles.ResolveAsync(cancellationToken);
        if (state.Problem is { } problem || state.Profile is null)
        {
            return [new HealthFinding(state.Problem ?? AIProblems.NoProfile, said.Problem(state.Problem ?? AIProblems.NoProfile))];
        }

        var connection = await connections.GetConnectionAsync(state.Profile.ConnectionId, cancellationToken);
        if (connection is null)
        {
            return [new HealthFinding("ai_connection_missing", said.ConnectionMissing(state.Profile.Name))];
        }

        if (!connection.IsActive)
        {
            return [new HealthFinding("ai_connection_off", said.ConnectionOff(connection.Name))];
        }

        return await TestedAsync(connection, cancellationToken) ? [] : [new HealthFinding("ai_connection_failed", said.ConnectionFailed(connection.Name))];
    }

    /// <inheritdoc />
    public override string Passed(HealthWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        return AIHealthWords.Of(words.German).Passed;
    }

    private async Task<bool> TestedAsync(AIConnection connection, CancellationToken cancellationToken)
    {
        var key = (connection.Id, connection.Version);
        var now = time.GetUtcNow();
        if (_tests.TryGetValue(key, out var known) && now - known.At < TestFresh)
        {
            return known.Passed;
        }

        bool passed;
        try
        {
            passed = await connections.TestConnectionAsync(connection.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            passed = false;
        }

        _tests[key] = (now, passed);
        return passed;
    }
}

/// <summary>The AI add-on on Umbraco's Health Check dashboard, beside Jiron Form Builder's own checks.</summary>
[HealthCheck("6f0d6c1e-3b52-4c0a-9d44-1f7a2b8e5c20", "AI add-on", Description = "Checks that the AI add-on has a chat profile whose connection is on and answers.", Group = FormBuilderHealthCheck.GroupName)]
internal sealed class AIHealthCheck(AIHealth probe, IBackOfficeSecurityAccessor security) : FormBuilderHealthCheck(probe, security);

/// <summary>The AI health's sentences, in English and German.</summary>
internal sealed class AIHealthWords
{
    private readonly bool _german;

    private AIHealthWords(bool german) => _german = german;

    /// <summary>The sentences in German or in English.</summary>
    public static AIHealthWords Of(bool german) => new(german);

    /// <summary>All is well.</summary>
    public string Passed => _german
        ? "Das KI-Add-on hat ein Chat-Profil, dessen Verbindung eingeschaltet ist und antwortet."
        : "The AI add-on has a chat profile whose connection is on and answers.";

    /// <summary>The profile's problem, by its code.</summary>
    public string Problem(string code) => (code, _german) switch
    {
        (AIProblems.ProfileMissing, true) => "Das in Form Builder > Einstellungen > KI gew\u00e4hlte Profil gibt es nicht mehr. W\u00e4hlen Sie ein anderes.",
        (AIProblems.ProfileMissing, false) => "The profile chosen in Form Builder > Settings > AI no longer exists. Choose another one.",
        (AIProblems.ProfileCannotChat, true) => "Das in Form Builder > Einstellungen > KI gew\u00e4hlte Profil ist kein Chat-Profil. W\u00e4hlen Sie ein Chat-Profil.",
        (AIProblems.ProfileCannotChat, false) => "The profile chosen in Form Builder > Settings > AI is not a chat profile. Choose a chat profile.",
        (_, true) => "Das KI-Add-on hat kein Profil. W\u00e4hlen Sie eines in Form Builder > Einstellungen > KI, oder legen Sie in Umbraco.AI ein Standard-Chat-Profil fest.",
        _ => "The AI add-on has no profile to use. Choose one in Form Builder > Settings > AI, or set a default chat profile in Umbraco.AI.",
    };

    /// <summary>The profile's connection is gone.</summary>
    public string ConnectionMissing(string profile) => _german
        ? $"Die Verbindung des KI-Profils \u201e{profile}\u201c gibt es nicht mehr. W\u00e4hlen Sie in Umbraco.AI eine f\u00fcr das Profil."
        : $"The connection of the AI profile \u201c{profile}\u201d no longer exists. Choose one for the profile in Umbraco.AI.";

    /// <summary>The profile's connection is switched off.</summary>
    public string ConnectionOff(string connection) => _german
        ? $"Die KI-Verbindung \u201e{connection}\u201c ist ausgeschaltet. Schalten Sie sie in Umbraco.AI ein."
        : $"The AI connection \u201c{connection}\u201d is switched off. Switch it on in Umbraco.AI.";

    /// <summary>The profile's connection does not answer its test.</summary>
    public string ConnectionFailed(string connection) => _german
        ? $"Die KI-Verbindung \u201e{connection}\u201c hat auf den Test nicht geantwortet. Pr\u00fcfen Sie ihre Einstellungen in Umbraco.AI."
        : $"The AI connection \u201c{connection}\u201d did not answer its test. Check its settings in Umbraco.AI.";
}
