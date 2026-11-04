using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>How much an issue of the form check weighs (ADR-0177): an error keeps a form from going live, a warning and a hint never do.</summary>
internal enum FormCheckSeverity
{
    /// <summary>Publishing is refused until it is mended.</summary>
    Error,

    /// <summary>The form goes live, but something will not work as the editor may think.</summary>
    Warning,

    /// <summary>A suggestion.</summary>
    Hint,
}

/// <summary>
/// An issue the form check finds: its weight, its code, the JSON path of the element and the setting it is about (as
/// <see cref="DefinitionProblem"/> writes them), and a sentence in English; the designer says it by its code.
/// </summary>
internal sealed record FormCheckIssue(FormCheckSeverity Severity, string Code, string Path, string Message)
{
    /// <summary>A publish check's problem, which is an error.</summary>
    public static FormCheckIssue Error(DefinitionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new FormCheckIssue(FormCheckSeverity.Error, problem.Code, problem.Path, problem.Message);
    }

    /// <summary>The issue as a problem a refused publish names.</summary>
    public DefinitionProblem ToProblem() => new(Code, Path, Message);
}

/// <summary>
/// A check of the form check that weighs its own issues (ROADMAP 9.5b, ADR-0177). The publish checks' problems are all
/// errors; a check of this kind may also warn or hint, which never keeps a form from going live.
/// </summary>
internal interface IFormCheck
{
    /// <summary>The issues of <paramref name="definition"/>, as the site runs it; empty when it has none.</summary>
    ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken);
}

/// <summary>
/// The form check (ROADMAP 9.5b, ADR-0177): every publish check, whose problems are errors, then every check that weighs
/// its issues, over the definition as the site runs it, with the host's designer locks (ADR-0060). A lock that replaces a
/// value the form stores is a warning of its own, since only this check sees both (ADR-0008, ADR-0180). Publishing
/// refuses its errors; the designer asks it of the draft it holds, and lists every issue with its path.
/// </summary>
/// <param name="publishChecks">The publish checks, the structural validation first (ADR-0044).</param>
/// <param name="checks">The checks that weigh their issues.</param>
/// <param name="locks">The host's locks of field settings.</param>
/// <param name="actionLocks">The host's locks of action settings.</param>
internal sealed class FormCheck(IEnumerable<IFormPublishCheck> publishChecks, IEnumerable<IFormCheck> checks, FieldSettingLocks locks, ActionSettingLocks actionLocks)
{
    /// <summary>A host's lock that replaces a value the form stores (a warning).</summary>
    public const string LockReplacesValue = "lock_replaces_value";

    private readonly IReadOnlyList<IFormPublishCheck> _publishChecks = [.. publishChecks];
    private readonly IReadOnlyList<IFormCheck> _checks = [.. checks];

    /// <summary>
    /// Each setting of a field or an action that a host's lock gives another value than the one the form stores, where it
    /// is stored, in document order: the site runs the lock's value (ADR-0008). A hidden lock's setting is none, since the
    /// designer never shows it, so no editor expects its value.
    /// </summary>
    internal static IEnumerable<FormCheckIssue> Replaced(FormDefinition definition, FieldSettingLocks locks, ActionSettingLocks actionLocks)
    {
        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            foreach (var settingLock in FieldSettingLocks.Replacing(field.Settings, locks.Of(field.Type)).Where(settingLock => !settingLock.Hidden))
            {
                yield return Replacing(JsonPaths.Property(path, "settings"), settingLock);
            }
        }

        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var i = 0; i < definition.Actions.Count; i++)
        {
            var action = definition.Actions[i];
            foreach (var settingLock in FieldSettingLocks.Replacing(action.Settings, actionLocks.Of(action.Type)).Where(settingLock => !settingLock.Hidden))
            {
                yield return Replacing(JsonPaths.Property(JsonPaths.Index(actions, i), "settings"), settingLock);
            }
        }
    }

    /// <summary>
    /// Every issue of <paramref name="definition"/>: the errors first, then the warnings, then the hints, each in the order
    /// the checks found them, and at most <see cref="DefinitionProblems.Max"/> of each.
    /// </summary>
    public async Task<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var running = actionLocks.Apply(locks.Apply(definition));
        var errors = new DefinitionProblems();
        foreach (var check in _publishChecks)
        {
            if (errors.IsFull)
            {
                break;
            }

            foreach (var problem in await check.CheckAsync(running, cancellationToken))
            {
                errors.Add(problem);
            }
        }

        var warnings = Replaced(definition, locks, actionLocks).Take(DefinitionProblems.Max).ToList();
        var hints = new List<FormCheckIssue>();
        foreach (var check in _checks)
        {
            foreach (var issue in await check.CheckAsync(running, cancellationToken))
            {
                switch (issue.Severity)
                {
                    case FormCheckSeverity.Error:
                        errors.Add(issue.ToProblem());
                        break;
                    case FormCheckSeverity.Warning when warnings.Count < DefinitionProblems.Max:
                        warnings.Add(issue);
                        break;
                    case FormCheckSeverity.Hint when hints.Count < DefinitionProblems.Max:
                        hints.Add(issue);
                        break;
                }
            }
        }

        return [.. errors.ToReadOnlyList().Select(FormCheckIssue.Error), .. warnings, .. hints];
    }

    private static FormCheckIssue Replacing(string settings, FieldSettingLock settingLock) => new(
        FormCheckSeverity.Warning,
        LockReplacesValue,
        JsonPaths.Property(settings, settingLock.Setting.Name),
        $"The site locks this setting ({settingLock.ConfigurationKey}), so the form runs with the locked value instead of the one it stores.");
}
