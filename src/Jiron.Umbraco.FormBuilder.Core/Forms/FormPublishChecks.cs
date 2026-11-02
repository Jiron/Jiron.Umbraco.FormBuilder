using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// A check a definition must pass before it goes live (ADR-0044). The checks run in the order they are registered, and
/// every problem of every check refuses the publish at once: the structural validation first (ADR-0035), the form
/// check from 9.5b on.
/// </summary>
internal interface IFormPublishCheck
{
    /// <summary>The problems that keep <paramref name="definition"/> from going live; empty when it may.</summary>
    ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken);
}

/// <summary>
/// The structural validation of ADR-0035 with the site's limits: the save checks, then aliases, references, cycles,
/// rules and redirects, with the fields' settings held to the host's limits for files (ADR-0087).
/// </summary>
/// <param name="serializer">The definitions' serializer with the site's limits.</param>
/// <param name="types">The field types the site has.</param>
/// <param name="host">The host's limits for uploaded files; those of a host that sets none when null.</param>
internal sealed class StructuralPublishCheck(FormDefinitionSerializer serializer, FieldTypeRegistry types, FieldHostLimits? host = null) : IFormPublishCheck
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Check(definition));

    /// <summary>
    /// The problems publishing refuses: those of a save, and when there are none, those of the structural validation
    /// (ADR-0035), which a draft keeps.
    /// </summary>
    public IReadOnlyList<DefinitionProblem> Check(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var problems = serializer.CheckSave(definition);
        return problems.Count > 0 ? problems : DefinitionStructureChecks.Check(definition, types, host);
    }
}

/// <summary>
/// Refuses an action whose type the site lacks (<c>unknown_type</c> at <c>actions[n].type</c>, ADR-0035), switched off or not,
/// and holds each action's settings to its type: the schema and the rules of its attributes, then the type's own check
/// (ADR-0158).
/// </summary>
/// <param name="types">The action types the site has.</param>
internal sealed class ActionPublishCheck(ActionTypeRegistry types) : IFormPublishCheck
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var problems = new DefinitionProblems();
        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var i = 0; i < definition.Actions.Count; i++)
        {
            var action = definition.Actions[i];
            var path = JsonPaths.Index(actions, i);
            if (types.Find(action.Type) is not { } type)
            {
                problems.Add(DefinitionErrors.UnknownType, JsonPaths.Property(path, "type"), $"The site has no action type '{action.Type}'.");
                continue;
            }

            var settings = JsonPaths.Property(path, "settings");
            if (FieldSettingsChecks.Check(type.SettingsSchema, action.Settings, settings, $"An action of the type '{action.Type}'", problems))
            {
                type.CheckSettings(new ActionSettingsCheck(action, definition, settings, problems));
            }
        }

        return ValueTask.FromResult(problems.ToReadOnlyList());
    }
}

/// <summary>Refuses a captcha provider the site lacks (ADR-0035, ADR-0142). One without its keys is the site's to mend, and its entries go to Spam until then.</summary>
/// <param name="providers">The providers the site has.</param>
internal sealed class CaptchaPublishCheck(FormBuilderCaptchaProviderCollection providers) : IFormPublishCheck
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<DefinitionProblem> problems = definition.Settings.Protection.Captcha is { } captcha && providers.Find(captcha.Provider) is null
            ? [new DefinitionProblem(DefinitionErrors.UnknownType, "$.settings.protection.captcha.provider", $"The site has no captcha provider '{captcha.Provider}'.")]
            : [];
        return ValueTask.FromResult(problems);
    }
}
