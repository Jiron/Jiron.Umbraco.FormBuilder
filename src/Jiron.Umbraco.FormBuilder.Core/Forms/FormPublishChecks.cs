using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

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
/// rules and redirects.
/// </summary>
internal sealed class StructuralPublishCheck(FormDefinitionSerializer serializer, FieldTypeRegistry types) : IFormPublishCheck
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
        return problems.Count > 0 ? problems : DefinitionStructureChecks.Check(definition, types);
    }
}
