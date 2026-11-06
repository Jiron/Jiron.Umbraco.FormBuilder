using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>The states the set-state action moves an entry to.</summary>
public enum SetStateTarget
{
    /// <summary>Approved.</summary>
    [FormBuilderChoice("#jfbActionChoices_setStateApproved")]
    Approved,

    /// <summary>Rejected.</summary>
    [FormBuilderChoice("#jfbActionChoices_setStateRejected")]
    Rejected,

    /// <summary>In Spam.</summary>
    [FormBuilderChoice("#jfbActionChoices_setStateSpam")]
    Spam,
}

/// <summary>The settings of the set-state action (section 4.4).</summary>
public sealed class SetStateActionSettings
{
    /// <summary>The state; the action's rule says when.</summary>
    [FormBuilderSetting("#jfbActionSettings_setStateState", Description = "#jfbActionSettings_setStateStateHelp", Order = 1)]
    public SetStateTarget State { get; init; }
}

/// <summary>
/// Moves an entry to a state under the action's rule (ROADMAP 8.5, section 4.4, ADR-0162): nobody can undo it from a toast, so what it
/// starts runs at once. An entry already there has nothing left to do, and one the state does not apply to fails for good.
/// </summary>
internal sealed class SetStateActionType(EntryStateChanges changes, IFormRepository forms) : FormBuilderActionTypeBase<SetStateActionSettings>(Declared)
{
    /// <summary>The type's alias.</summary>
    public const string Alias = "setState";

    /// <summary>The code of an entry in a state this move does not leave.</summary>
    public const string CannotMove = "cannot_move";

    private static readonly ActionTypeDeclaration Declared = new()
    {
        Alias = Alias,
        Name = "#jfbActionTypes_setState",
        Icon = "icon-axis-rotation",
        Test = ActionTestKind.DryRun,
    };

    /// <inheritdoc />
    /// <remarks>
    /// A move to Approved starts the Approved actions and one to Rejected the Rejected actions, so an action on each that
    /// moves an entry to the other's state would move it back and forth for ever, every action running each time: the form
    /// check refuses the pair (ADR-0192). A move to the state the trigger's entry is in already does nothing, which it says.
    /// </remarks>
    protected override void CheckSettings(SetStateActionSettings settings, ActionSettingsCheck check)
    {
        var action = check.Action;
        if (StateOf(action.Trigger) is not { } own)
        {
            return;
        }

        if (settings.State == own)
        {
            check.Warn("state", ActionCheckCodes.StateAlready, $"An entry is {own} already when the actions of that trigger run, so this action changes nothing.");
            return;
        }

        // An action that is off moves nothing, so it makes no pair.
        if (!action.Enabled || TriggerOf(settings.State) is not { } next)
        {
            return;
        }

        foreach (var other in check.Definition.Actions.Where(other => other.Enabled && other.Key != action.Key && other.Type == Alias && other.Trigger == next))
        {
            if (Read(other) is { } back && back.State == own)
            {
                check.Invalid("state", ActionCheckCodes.StateLoop, $"'{other.Name}' moves an entry back to {own}: the two would move it back and forth for ever, and every action of both triggers would run each time.");
                return;
            }
        }
    }

    /// <inheritdoc />
    protected override async Task<ActionResult> RunAsync(SetStateActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        if (context.Scope.Entry is not { } entry || await forms.GetByAliasAsync(context.Scope.FormAlias, cancellationToken) is not { } form)
        {
            return ActionResult.Failed(CannotMove);
        }

        var to = settings.State switch
        {
            SetStateTarget.Approved => EntryState.Approved,
            SetStateTarget.Rejected => EntryState.Rejected,
            _ => EntryState.Spam,
        };
        var change = await changes.ChangeAsync(form.Key, [entry.Key], to, null, cancellationToken);
        return change.Moved.Count > 0 || await changes.IsInAsync(entry.Key, to, cancellationToken) ? ActionResult.Succeeded : ActionResult.Failed(CannotMove);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A dry run: the state a run would move the entry to and how many actions that starts, as a line the backoffice words
    /// (ADR-0192).
    /// </remarks>
    protected override Task<ActionTestResult> TestAsync(SetStateActionSettings settings, ActionTestContext context, CancellationToken cancellationToken)
    {
        var state = settings.State switch
        {
            SetStateTarget.Approved => "approved",
            SetStateTarget.Rejected => "rejected",
            _ => "spam",
        };
        // What the move starts: the actions of the trigger it moves the entry to, those that are on.
        var starts = TriggerOf(settings.State) is { } next ? context.Scope.Form.Actions.Count(action => action.Enabled && action.Trigger == next) : 0;
        var then = starts > 0 ? $", which starts {starts} {settings.State} action{(starts == 1 ? string.Empty : "s")}" : string.Empty;
        var line = new ActionTestLine($"A run would move the entry to {settings.State}{then}.")
        {
            Code = "state_move",
            Args = [state, starts.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        };
        return Task.FromResult(new ActionTestResult(ActionTestOutcome.DryRun, Preview: [line]) { Code = ActionTestCodes.DryRun });
    }

    // The state an entry is in when a trigger's actions run, for the triggers a move starts; null for another.
    private static SetStateTarget? StateOf(ActionTrigger trigger) => trigger switch
    {
        ActionTrigger.Approved => SetStateTarget.Approved,
        ActionTrigger.Rejected => SetStateTarget.Rejected,
        _ => null,
    };

    // The trigger a move to the state starts; null for Spam, which starts none.
    private static ActionTrigger? TriggerOf(SetStateTarget state) => state switch
    {
        SetStateTarget.Approved => ActionTrigger.Approved,
        SetStateTarget.Rejected => ActionTrigger.Rejected,
        _ => null,
    };

    // Another action's settings; null for settings that do not read as this type's, which its own check refuses.
    private SetStateActionSettings? Read(FormAction action)
    {
        try
        {
            return ReadSettings(action.Settings);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
