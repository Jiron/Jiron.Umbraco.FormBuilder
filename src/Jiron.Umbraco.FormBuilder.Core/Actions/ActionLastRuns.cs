using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// The last run of each action of a form's draft, for its card in the Actions view (ADR-0195): the newest run that has been
/// tried or has settled, of an entry of the form. What it answers names no entry, and the failure it holds is the type's
/// code and its message without personal data (8.1).
/// </summary>
internal sealed class ActionLastRuns(IFormBuilderFormService forms, IFormRepository stored, IActionRunRepository runs)
{
    /// <summary>The last runs of the draft's actions, an action without one left out; null when there is no such form.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyList<StoredActionRun>?> ListAsync(Guid formKey, CancellationToken cancellationToken = default)
    {
        if (await forms.GetDraftAsync(formKey, cancellationToken) is not { } draft || await stored.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        // A draft that cannot be read has no actions to show.
        var keys = draft.Definition?.Actions.Select(action => action.Key).ToList() ?? [];
        return keys.Count == 0 ? [] : await runs.LastOfActionsAsync(form.Id, keys, cancellationToken);
    }
}
