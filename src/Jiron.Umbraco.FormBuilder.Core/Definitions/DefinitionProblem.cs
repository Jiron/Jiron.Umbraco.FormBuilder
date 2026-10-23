namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// A problem with a definition: a stable code (<see cref="DefinitionErrors"/>), the JSON path of the part it concerns
/// (<c>$.pages[0].groups[1].fields[2].label['de']</c>) and a message for developers. The backoffice shows its own text
/// for each code.
/// </summary>
/// <param name="Code">The stable code.</param>
/// <param name="Path">The JSON path, <c>$</c> for the whole document.</param>
/// <param name="Message">What is wrong, in English, for logs and developers.</param>
internal sealed record DefinitionProblem(string Code, string Path, string Message);

/// <summary>
/// The codes with which a save refuses a definition it cannot read (ADR-0002, ADR-0034). Publishing adds the codes of
/// the structural validation (4.2b).
/// </summary>
internal static class DefinitionErrors
{
    /// <summary>The text is not JSON, or not valid UTF-8.</summary>
    public const string InvalidJson = "invalid_json";

    /// <summary>An object has a property the model does not have.</summary>
    public const string UnknownProperty = "unknown_property";

    /// <summary>A required property is missing.</summary>
    public const string MissingProperty = "missing_property";

    /// <summary>An object names a property twice.</summary>
    public const string DuplicateProperty = "duplicate_property";

    /// <summary>A value has the wrong JSON type, for example a number where a string belongs.</summary>
    public const string InvalidType = "invalid_type";

    /// <summary>A value has the right type but is not allowed: an unknown word, a malformed key, a number out of range.</summary>
    public const string InvalidValue = "invalid_value";

    /// <summary>Two elements of the form have the same key.</summary>
    public const string DuplicateKey = "duplicate_key";

    /// <summary>The document's schema version is newer than this package reads, or older than any upgrader.</summary>
    public const string UnsupportedSchemaVersion = "unsupported_schema_version";

    /// <summary>The document, a text or a list is larger than its limit.</summary>
    public const string LimitExceeded = "limit_exceeded";
}
