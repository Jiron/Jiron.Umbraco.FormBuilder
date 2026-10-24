using Jiron.Umbraco.FormBuilder.Core.Definitions;

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
internal sealed class StructuralPublishCheck(FormDefinitionSerializer serializer, FieldTypeCatalog types) : IFormPublishCheck
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ValueTask.FromResult(serializer.CheckPublish(definition, types));
    }
}
