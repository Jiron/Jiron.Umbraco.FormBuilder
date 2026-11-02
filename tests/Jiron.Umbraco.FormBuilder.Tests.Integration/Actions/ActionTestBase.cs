using System.Collections.Concurrent;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>An attempt of <see cref="ScriptedActionType"/>: what it does this time.</summary>
internal delegate Task<ActionResult> ActionScript(ActionRunContext context, CancellationToken cancellationToken);

/// <summary>
/// An action type whose attempts a test writes: each one runs the script of its action's name and counts what it did.
/// </summary>
internal class ScriptedActionType : FormBuilderActionTypeBase<ScriptedSettings>
{
    public const string Alias = "testing.scripted";

    public ScriptedActionType()
        : this(Alias, TimeSpan.FromSeconds(2))
    {
    }

    protected ScriptedActionType(string alias, TimeSpan timeout)
        : base(new ActionTypeDeclaration { Alias = alias, Name = "Scripted", Icon = "icon-script", AttemptTimeout = timeout, Test = ActionTestKind.AfterConfirmation })
    {
    }

    public ConcurrentDictionary<string, ActionScript> Scripts { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Started { get; } = new();

    public ConcurrentQueue<ActionTestContext> Tests { get; } = new();

    public ConcurrentQueue<string?> SettingsSeen { get; } = new();

    public ConcurrentQueue<string?> HeldSeen { get; } = new();

    public IReadOnlyList<string>? Missing { get; set; }

    public int StartedOf(string name) => Started.Count(each => each == name);

    protected override IReadOnlyList<string> MissingConfiguration(ScriptedSettings settings, FormAction action) => Missing ?? [];

    protected override void CheckSettings(ScriptedSettings settings, ActionSettingsCheck check)
    {
        if (settings.Target == "refused" || (settings.Target == "not-with-the-lock" && settings.Held == "locked-value"))
        {
            check.Invalid("target", "This target is refused.");
        }
    }

    protected override Task<ActionResult> RunAsync(ScriptedSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        var name = context.Action.Name ?? string.Empty;
        Started.Enqueue(name);
        SettingsSeen.Enqueue(settings.Target);
        HeldSeen.Enqueue(settings.Held);
        return Scripts.TryGetValue(name, out var script) ? script(context, cancellationToken) : Task.FromResult(ActionResult.Succeeded);
    }

    protected override Task<ActionTestResult> TestAsync(ScriptedSettings settings, ActionTestContext context, CancellationToken cancellationToken)
    {
        Tests.Enqueue(context);
        return Task.FromResult(new ActionTestResult(ActionTestOutcome.Sent, $"Sent to {settings.Target}", ["one line"]));
    }
}

/// <summary>The same with the longest time an attempt may take, for a test that must not be saved by the time limit.</summary>
internal sealed class PatientActionType : ScriptedActionType
{
    public new const string Alias = "testing.patient";

    public PatientActionType()
        : base(Alias, TimeSpan.FromMinutes(1))
    {
    }
}

/// <summary>The settings of <see cref="ScriptedActionType"/>.</summary>
public sealed class ScriptedSettings
{
    /// <summary>Where it goes; a value the tests lock and read back.</summary>
    [Core.FieldTypes.FormBuilderSetting("Target")]
    public string? Target { get; init; }

    /// <summary>A value the tests lock.</summary>
    [Core.FieldTypes.FormBuilderSetting("Held")]
    public string? Held { get; init; }
}

/// <summary>
/// A host for the action framework's tests (ROADMAP 8.1, ADR-0007, ADR-0158): a hand-moved clock, no worker of its own, so a
/// test runs each pass itself, sending not paused, and <see cref="ScriptedActionType"/> registered.
/// </summary>
public abstract class ActionTestBase : PublicApiTestBase
{
    private protected TestTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    private protected IActionRunRepository Runs => GetRequiredService<IActionRunRepository>();

    private protected IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private protected IEntryEventRepository Events => GetRequiredService<IEntryEventRepository>();

    private protected IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private protected OutboxPass Pass => GetRequiredService<OutboxPass>();

    internal ScriptedActionType Scripted => GetRequiredService<FormBuilderActionTypeCollection>().OfType<ScriptedActionType>().Single(type => type is not PatientActionType);

    internal PatientActionType Patient => GetRequiredService<FormBuilderActionTypeCollection>().OfType<PatientActionType>().Single();

    /// <summary>Whether the fixture keeps the host's own worker, for a test of the wake.</summary>
    private protected virtual bool KeepsTheWorker => false;

    /// <summary>
    /// Sends, unlike the other tests' hosts, and lets the answer "a migration is pending" from before the plan ran grow old, as
    /// this clock stands still.
    /// </summary>
    [SetUp]
    public void GetReadyToSend()
    {
        Pause(false);
        Clock.Advance(TimeSpan.FromMinutes(1));
    }

    /// <summary>The host's pause switch, as a change of the configuration would turn it.</summary>
    private protected void Pause(bool paused) => GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Actions.Paused = paused;

    private protected static FormAction Action(string name, ActionTrigger trigger = ActionTrigger.Submitted, bool stopsTheRest = false, string? target = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = ScriptedActionType.Alias,
        Name = name,
        Trigger = trigger,
        StopOnFailure = stopsTheRest,
        Settings = target is null ? null : SettingsObject.Parse($$"""{"target":"{{target}}"}"""),
    };

    /// <summary>Publishes a form with the actions.</summary>
    private protected Task<Guid> ActionFormAsync(params FormAction[] actions) => PublishAsync(change: definition => definition with { Actions = actions });

    /// <summary>Submits to the form and gives the entry made; the worker is not told to do anything.</summary>
    private protected async Task<StoredEntry> SubmitAsync(Guid form, string values = """{"name":"Ada"}""")
    {
        using var content = new StringContent($$"""{"revision":1,"values":{{values}}}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        Assert.That(Succeeded(answer), Is.True, await answer.Content.ReadAsStringAsync());
        var row = (await FormRows.GetAsync(form))!;
        var ids = await Entries.ListIdsAsync(row.Id, 1000);
        return (await Entries.GetByIdAsync(ids[^1]))!;
    }

    /// <summary>Runs passes until one leaves nothing that is due now, and the runs of the entry as they are then.</summary>
    private protected async Task<IReadOnlyList<StoredActionRun>> DrainAsync(long entryId)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            await Pass.RunAsync(CancellationToken.None);
            if ((await Runs.DueAsync(1)).Count == 0)
            {
                break;
            }
        }

        return await Runs.ListForEntryAsync(entryId);
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<TimeProvider>(Clock);
        builder.JironFormBuilder().ActionTypes().Append<ScriptedActionType>().Append<PatientActionType>();
        if (!KeepsTheWorker)
        {
            builder.Services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(OutboxWorker));
        }
    }
}
