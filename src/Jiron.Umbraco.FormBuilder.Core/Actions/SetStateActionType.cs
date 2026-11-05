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
    [FormBuilderSetting("#jfbActionSettings_setStateState", Order = 1)]
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
    protected override Task<ActionTestResult> TestAsync(SetStateActionSettings settings, ActionTestContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionTestResult(ActionTestOutcome.Sent, $"An entry would move to {settings.State}. Nothing changed."));
}
