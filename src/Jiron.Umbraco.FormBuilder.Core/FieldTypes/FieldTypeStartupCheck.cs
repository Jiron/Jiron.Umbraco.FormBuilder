using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Stops the site when its field types cannot be its registry (ADR-0042, ADR-0055): an alias taken twice or outside the
/// pattern, a declaration that contradicts itself, or a settings class that cannot be read. It runs with the other
/// start-up checks, before the site serves anything, and names every problem at once.
/// </summary>
/// <param name="types">The registered field types, which the check creates.</param>
internal sealed class FieldTypeStartupCheck(FormBuilderFieldTypeCollection types) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        IReadOnlyList<string> problems;
        try
        {
            problems = FieldTypeRegistry.Problems([.. types]);
        }
        catch (InvalidOperationException exception)
        {
            // A type whose settings class cannot be read fails as it is created, and so does one the container cannot create.
            problems = [$"Jiron Form Builder cannot create its field types: {exception.Message}"];
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
