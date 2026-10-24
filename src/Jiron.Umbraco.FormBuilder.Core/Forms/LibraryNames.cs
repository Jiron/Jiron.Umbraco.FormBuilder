using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>The one rule for the names of forms and folders (ADR-0044, ADR-0047).</summary>
internal static class LibraryNames
{
    /// <summary>
    /// The name once trimmed: 1 to <paramref name="maxLength"/> characters, not only white space, on one line, without
    /// control characters. The path is where a refusal points: the request's name, or the name in an export file.
    /// </summary>
    /// <param name="name">The name as given.</param>
    /// <param name="noun">What has the name, for the refusal's message: <c>form</c> or <c>folder</c>.</param>
    /// <param name="maxLength">The longest name the column holds.</param>
    /// <param name="path">Where a refusal points.</param>
    /// <exception cref="FormBuilderValidationException">The name is not allowed.</exception>
    public static string Check(string name, string noun, int maxLength, string path = "name")
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, path, $"A {noun} needs a name that is not only white space.");
        }

        if (trimmed.Length > maxLength)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.LimitExceeded, path, $"A {noun} name has at most {maxLength} characters.");
        }

        return trimmed.Any(char.IsControl) || !StrictStringConverter.IsWellFormed(trimmed)
            ? throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, path, $"A {noun} name is one line of text, without control characters.")
            : trimmed;
    }
}
