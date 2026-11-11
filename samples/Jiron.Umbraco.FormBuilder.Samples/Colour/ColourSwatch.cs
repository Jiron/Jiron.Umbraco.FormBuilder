using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Samples.Colour;

/// <summary>One colour a colour field offers: its name, which the visitor reads, and its value, which the answer holds.</summary>
/// <param name="Name">The colour's name, such as "Blue".</param>
/// <param name="Value">The colour's value: a hash and six lower-case hexadecimal digits, such as <c>#1f6feb</c>.</param>
public sealed partial record ColourSwatch(string Name, string Value)
{
    /// <summary>The length of a colour's value.</summary>
    public const int ValueLength = 7;

    /// <summary>The longest name a colour has.</summary>
    public const int NameLength = 40;

    /// <summary>The most colours a field offers.</summary>
    public const int Most = 24;

    /// <summary>
    /// The colours of a setting's value, in order; a value that is not such a list, or an item that is not such a
    /// colour, gives none, which <see cref="Problem"/> names.
    /// </summary>
    /// <param name="colours">The setting's value.</param>
    /// <returns>The colours.</returns>
    public static ImmutableArray<ColourSwatch> Read(JsonElement colours)
    {
        if (colours.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var read = ImmutableArray.CreateBuilder<ColourSwatch>();
        foreach (var item in colours.EnumerateArray())
        {
            if (Of(item) is { } swatch)
            {
                read.Add(swatch);
            }
        }

        return read.ToImmutable();
    }

    /// <summary>What is wrong with a setting's value, in words for the editor who publishes the form; null when nothing is.</summary>
    /// <param name="colours">The setting's value.</param>
    /// <returns>The problem, or null.</returns>
    public static string? Problem(JsonElement colours)
    {
        if (colours.ValueKind != JsonValueKind.Array)
        {
            return "The colours are a list.";
        }

        var count = colours.GetArrayLength();
        if (count == 0)
        {
            return "Offer at least one colour.";
        }

        if (count > Most)
        {
            return $"Offer at most {Most} colours.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        foreach (var item in colours.EnumerateArray())
        {
            position++;
            if (Of(item) is not { } swatch)
            {
                return $"Colour {position} needs a name of at most {NameLength} characters and a value such as #1f6feb.";
            }

            if (!seen.Add(swatch.Value))
            {
                return $"{swatch.Value} is offered twice.";
            }
        }

        return null;
    }

    /// <summary>A setting's value of the given colours.</summary>
    /// <param name="colours">The colours.</param>
    /// <returns>The value.</returns>
    public static JsonElement Write(IEnumerable<ColourSwatch> colours) =>
        JsonSerializer.SerializeToElement(colours.Select(colour => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = colour.Name,
            ["value"] = colour.Value,
        }));

    private static ColourSwatch? Of(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var said = name.GetString()!.Trim();
        var written = value.GetString()!;
        return said.Length is > 0 and <= NameLength && Hex().IsMatch(written) ? new ColourSwatch(said, written) : null;
    }

    [GeneratedRegex("^#[0-9a-f]{6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Hex();
}
