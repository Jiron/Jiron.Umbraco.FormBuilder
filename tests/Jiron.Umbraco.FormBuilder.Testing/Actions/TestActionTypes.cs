using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Testing.Actions;

/// <summary>The settings a stand-in takes: what the tests' forms give an action of a type a later step brings.</summary>
public sealed class StandInSettings
{
    /// <summary>A field to reply to, by its key.</summary>
    [FormBuilderSetting("Reply to")]
    public string? ReplyTo { get; init; }

    /// <summary>Who it goes to.</summary>
    [FormBuilderSetting("To")]
    public IReadOnlyList<string> To { get; init; } = [];

    /// <summary>The subject, with merge tags.</summary>
    [FormBuilderSetting("Subject", SupportsMergeTags = true)]
    public string? Subject { get; init; }
}

/// <summary>A stand-in for a built-in action type that a later step brings: it takes the tests' settings and does nothing.</summary>
/// <param name="alias">The type's alias.</param>
public abstract class StandInActionType(string alias)
    : FormBuilderActionTypeBase<StandInSettings>(new ActionTypeDeclaration { Alias = alias, Name = alias, Icon = "icon-message" })
{
    /// <inheritdoc />
    protected override Task<ActionResult> RunAsync(StandInSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
}

/// <summary>An action type for hosts that test what forms do with actions, whatever a mail would say: it sends nothing.</summary>
public sealed class StandInEmailActionType() : StandInActionType(Alias)
{
    /// <summary>The type's alias.</summary>
    public const string Alias = "testing.mail";
}

/// <summary>An action type under the alias <c>webhook</c>, until 8.4 brings the real one; see <see cref="StandInEmailActionType"/>.</summary>
public sealed class StandInWebhookActionType() : StandInActionType("webhook");

/// <summary>
/// An action type that writes each delivery as one line to the file <see cref="FormBuilderTestingOptions.DeliveriesFile"/> names,
/// shared by every host of a test: the load-balancing suite counts them (ADR-0158).
/// </summary>
/// <param name="options">The testing options.</param>
public sealed class RecordingActionType(IOptions<FormBuilderTestingOptions> options)
    : FormBuilderActionTypeBase<NoFieldSettings>(new ActionTypeDeclaration { Alias = "testing.record", Name = "Record the delivery", Icon = "icon-document" })
{
    /// <inheritdoc />
    protected override async Task<ActionResult> RunAsync(NoFieldSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        var file = options.Value.DeliveriesFile ?? throw new InvalidOperationException("The host has no file for its deliveries.");
        var line = JsonSerializer.Serialize(new { key = context.DeliveryKey, entry = context.Scope.Entry?.Key, server = Environment.ProcessId }) + Environment.NewLine;

        // Two hosts append to one file: a host that finds it held tries again in a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(line.AsMemory(), cancellationToken);
                return ActionResult.Succeeded;
            }
            catch (IOException) when (attempt < 50)
            {
                await Task.Delay(20, cancellationToken);
            }
        }
    }
}
