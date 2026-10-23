using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// Checks and canonicalises settings objects (<see cref="SettingsObject"/>): integers of at most 15 digits only, no
/// <c>null</c> inside an array, no property named twice, no name or text with a lone surrogate; properties sorted, and
/// those with nothing in them left out.
/// </summary>
internal static partial class SettingsJson
{
    /// <summary>Checks a settings object and returns its canonical text, or the first problem.</summary>
    public static bool TryCanonicalize(
        JsonElement value,
        string path,
        [NotNullWhen(true)] out string? canonical,
        [NotNullWhen(false)] out DefinitionProblem? problem)
    {
        canonical = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            problem = new DefinitionProblem(DefinitionErrors.InvalidType, path, $"Settings are an object, not {JsonPaths.Describe(value.ValueKind)}.");
            return false;
        }

        problem = Check(value, JsonPaths.Location.At(path));
        if (problem is not null)
        {
            return false;
        }

        if (CanonicalJson.IsLeftOut(value))
        {
            problem = new DefinitionProblem(DefinitionErrors.InvalidValue, path, "Settings with nothing in them are written as no settings.");
            return false;
        }

        canonical = CanonicalJson.Write(value, sortProperties: true);
        return true;
    }

    /// <summary>Whether a JSON number is an integer in canonical form with at most 15 digits.</summary>
    public static bool IsCanonicalInteger(string raw, int maxDigits) =>
        CanonicalInteger().IsMatch(raw) && raw != "-0" && raw.TrimStart('-').Length <= maxDigits;

    private static DefinitionProblem? Check(JsonElement value, JsonPaths.Location location)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (JsonPaths.TryGetName(property) is not { } name)
                    {
                        return new DefinitionProblem(DefinitionErrors.InvalidValue, location.ToString(), "A property name holds a lone surrogate.");
                    }

                    var child = location.Property(name);
                    if (!names.Add(name))
                    {
                        return new DefinitionProblem(DefinitionErrors.DuplicateProperty, child.ToString(), $"The property \"{name}\" appears twice.");
                    }

                    if (property.Value.ValueKind != JsonValueKind.Null && Check(property.Value, child) is { } problem)
                    {
                        return problem;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var child = location.Item(index++);
                    if (item.ValueKind == JsonValueKind.Null)
                    {
                        return new DefinitionProblem(DefinitionErrors.InvalidType, child.ToString(), "An array in settings holds no null.");
                    }

                    if (Check(item, child) is { } problem)
                    {
                        return problem;
                    }
                }

                return null;
            case JsonValueKind.Number:
                return IsCanonicalInteger(value.GetRawText(), 15)
                    ? null
                    : new DefinitionProblem(DefinitionErrors.InvalidValue, location.ToString(), "Numbers in settings are integers of at most 15 digits; a decimal is a string in the wire form.");
            case JsonValueKind.String:
                return JsonPaths.TryGetString(value) is null
                    ? new DefinitionProblem(DefinitionErrors.InvalidValue, location.ToString(), "The string holds a lone surrogate.")
                    : null;
            default:
                return null;
        }
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalInteger();
}
