using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>
/// What a library theme changes of its base theme (ADR-0215): tokens for both schemes, and colours for the dark one, each
/// by its name as the base theme declares it. Stored as <c>{ "light": { ... }, "dark": { ... } }</c> in <c>jfbTheme.tokens</c>.
/// </summary>
internal sealed record ThemeTokens
{
    /// <summary>The most tokens a theme changes in one scheme.</summary>
    public const int MaxTokens = 200;

    /// <summary>The longest value of a token.</summary>
    public const int MaxValueLength = 200;

    private static readonly FrozenDictionary<string, string> Empty = FrozenDictionary<string, string>.Empty;

    /// <summary>Nothing changed: the base theme as it is.</summary>
    public static ThemeTokens None { get; } = new();

    /// <summary>The tokens the theme sets in both schemes, by name.</summary>
    public IReadOnlyDictionary<string, string> Light { get; init; } = Empty;

    /// <summary>The colours the theme sets in the dark scheme, by name.</summary>
    public IReadOnlyDictionary<string, string> Dark { get; init; } = Empty;

    /// <summary>
    /// The tokens a stored text holds; a text that is not of this shape reads as none, so a theme written by hand renders
    /// as its base theme rather than not at all.
    /// </summary>
    /// <param name="json">The stored JSON.</param>
    public static ThemeTokens Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return None;
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject root ? new ThemeTokens { Light = Scheme(root["light"]), Dark = Scheme(root["dark"]) } : None;
        }
        catch (JsonException)
        {
            return None;
        }
    }

    /// <summary>The tokens as JSON with sorted names, so the same tokens are always the same text.</summary>
    public string Write()
    {
        var root = new JsonObject
        {
            ["light"] = Object(Light),
            ["dark"] = Object(Dark),
        };
        return root.ToJsonString();
    }

    /// <summary>
    /// The problems of these tokens over a base theme, each at its path: a token the base theme does not declare, a dark
    /// colour without a dark scheme or of a token that is no colour, and a value that is more than one declaration or
    /// holds an address.
    /// </summary>
    /// <param name="baseTheme">The base theme.</param>
    /// <param name="problems">Where the problems go.</param>
    /// <param name="path">The tokens' path, such as <c>$.tokens</c>.</param>
    public void Check(IFormBuilderTheme baseTheme, DefinitionProblems problems, string path)
    {
        ArgumentNullException.ThrowIfNull(baseTheme);
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(path);
        foreach (var (scheme, tokens) in new[] { ("light", Light), ("dark", Dark) })
        {
            if (tokens.Count > MaxTokens)
            {
                problems.Add(DefinitionErrors.LimitExceeded, $"{path}.{scheme}", $"A theme changes at most {MaxTokens} tokens in one scheme.");
                continue;
            }

            foreach (var (token, value) in tokens.OrderBy(each => each.Key, StringComparer.Ordinal))
            {
                var at = $"{path}.{scheme}['{token}']";
                if (!baseTheme.Tokens.ContainsKey(token))
                {
                    problems.Add(DefinitionErrors.UnknownProperty, at, $"The theme '{baseTheme.Name}' declares no token '{token}'.");
                }
                else if (scheme == "dark" && (!baseTheme.DarkScheme || !token.StartsWith("--jfb-color-", StringComparison.Ordinal)))
                {
                    problems.Add(DefinitionErrors.PropertyNotSupported, at, baseTheme.DarkScheme
                        ? $"Only colours have a dark value, and '{token}' is no colour."
                        : $"The theme '{baseTheme.Name}' has no dark scheme.");
                }
                else if (value.Length > MaxValueLength || !FormBuilderThemes.IsTokenValue(value))
                {
                    problems.Add(DefinitionErrors.InvalidValue, at, $"A token's value is one declaration of at most {MaxValueLength} characters, without an address.");
                }
            }
        }
    }

    /// <summary>
    /// The colours the theme's dark scheme sets: every colour it changes in either scheme, at its dark value, or the base
    /// theme's dark default when only the light one changed, so a light colour never shows in the dark scheme. Empty for
    /// a base theme without a dark scheme.
    /// </summary>
    /// <param name="baseTheme">The base theme.</param>
    public IReadOnlyDictionary<string, string> DarkScheme(IFormBuilderTheme baseTheme)
    {
        ArgumentNullException.ThrowIfNull(baseTheme);
        if (!baseTheme.DarkScheme)
        {
            return Empty;
        }

        var dark = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in Light.Keys.Concat(Dark.Keys).Where(token => token.StartsWith("--jfb-color-", StringComparison.Ordinal)))
        {
            if (Dark.TryGetValue(token, out var own))
            {
                dark[token] = own;
            }
            else if (baseTheme.DarkTokens.TryGetValue(token, out var fallback))
            {
                dark[token] = fallback;
            }
        }

        return dark;
    }

    private static FrozenDictionary<string, string> Scheme(JsonNode? node)
    {
        if (node is not JsonObject tokens)
        {
            return Empty;
        }

        var read = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in tokens)
        {
            if (value is JsonValue text && text.TryGetValue<string>(out var said))
            {
                read[name] = said;
            }
        }

        return read.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static JsonObject Object(IReadOnlyDictionary<string, string> tokens)
    {
        var json = new JsonObject();
        foreach (var (name, value) in tokens.OrderBy(each => each.Key, StringComparer.Ordinal))
        {
            json[name] = value;
        }

        return json;
    }
}
