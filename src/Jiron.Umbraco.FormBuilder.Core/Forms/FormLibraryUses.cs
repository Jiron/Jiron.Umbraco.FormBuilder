using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Email;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>What of the library a version of a form can use by key (ADR-0214), as <c>jfbFormLibraryUse</c> names it.</summary>
internal enum LibraryUseKind
{
    /// <summary>An option list, which a field takes its options from.</summary>
    OptionList,

    /// <summary>A theme, which the form's settings name by its alias (ADR-0215).</summary>
    Theme,

    /// <summary>An email layout, which an email action names (ADR-0215).</summary>
    EmailLayout,
}

/// <summary>An item of the library that a version of a form uses.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Key">Its key: a theme's is <see cref="FormLibraryUses.ThemeKey"/> of its alias.</param>
/// <param name="Count">How many of the version's elements use it: fields, for an option list; actions, for a layout.</param>
internal sealed record LibraryUse(LibraryUseKind Kind, Guid Key, int Count);

/// <summary>
/// What of the library a definition uses (ADR-0214, ADR-0215): the option lists its fields take their options from, the
/// children of repeatable groups included, each once with how many fields use it; the theme its settings name; and the
/// email layouts its email actions name, each with how many actions. In kind and then key order, so two reads of one
/// definition give one list.
/// </summary>
internal static class FormLibraryUses
{
    // The namespace of the themes' keys: a name-based UUID of a theme's alias in lower case, SHA-256 as RFC 9562 allows in its version 8.
    private static readonly Guid ThemeNamespace = new("4b2c86f1-0d37-4e9a-a5c3-71e98d0f2b64");

    /// <summary>The items the definition uses.</summary>
    public static IReadOnlyList<LibraryUse> Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var lists = DefinitionKeys.Fields(definition)
            .Select(field => field.Options)
            .OfType<FieldOptions>()
            .Where(options => options.Source == OptionSource.List)
            .Select(options => options.OptionList)
            .OfType<Guid>()
            .GroupBy(key => key)
            .OrderBy(group => group.Key)
            .Select(group => new LibraryUse(LibraryUseKind.OptionList, group.Key, group.Count()));
        var theme = definition.Settings.Appearance.Theme is { } alias && !string.IsNullOrWhiteSpace(alias)
            ? [new LibraryUse(LibraryUseKind.Theme, ThemeKey(alias), 1)]
            : Array.Empty<LibraryUse>();
        var layouts = definition.Actions
            .Where(action => action.Type == EmailActionType.Alias)
            .Select(action => LayoutOf(action.Settings))
            .OfType<Guid>()
            .GroupBy(key => key)
            .OrderBy(group => group.Key)
            .Select(group => new LibraryUse(LibraryUseKind.EmailLayout, group.Key, group.Count()));
        return [.. lists, .. theme, .. layouts];
    }

    /// <summary>
    /// The key a theme's uses go by: a name-based UUID of its alias in lower case, as aliases are unique without regard to
    /// case, so a version's rows need no read of the theme and name it whether or not the library holds it yet.
    /// </summary>
    /// <param name="alias">The theme's alias.</param>
    public static Guid ThemeKey(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        var name = Encoding.UTF8.GetBytes(alias.Trim().ToLowerInvariant());
        var hash = SHA256.HashData([.. ThemeNamespace.ToByteArray(bigEndian: true), .. name]);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    // The layout an email action's settings name, or none for settings that name none or name it otherwise.
    private static Guid? LayoutOf(SettingsObject? settings) =>
        settings is not null && settings.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.String && Guid.TryParse(layout.GetString(), out var key)
            ? key
            : null;
}
