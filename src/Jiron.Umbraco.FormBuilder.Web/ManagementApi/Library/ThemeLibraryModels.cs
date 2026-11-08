using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A page of the library's themes.</summary>
internal sealed record ThemeRowsResponseModel
{
    /// <summary>How many themes the library holds.</summary>
    public required long Total { get; init; }

    /// <summary>The page's themes, by name.</summary>
    public required IReadOnlyList<ThemeRowResponseModel> Items { get; init; }
}

/// <summary>A row of the library's themes.</summary>
internal sealed record ThemeRowResponseModel
{
    /// <summary>The theme's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias, which forms name it by.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>What the backoffice calls the theme it builds on.</summary>
    public required string BaseLabel { get; init; }

    /// <summary>How many forms' drafts or live versions use it, forms in the recycle bin included.</summary>
    public required int UsedBy { get; init; }

    /// <summary>Whether it is the site's default theme.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>Its surface, text and accent colours in the light scheme.</summary>
    public required IReadOnlyList<string> Swatch { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>A row as the service gives it.</summary>
    public static ThemeRowResponseModel Of(ThemeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new ThemeRowResponseModel
        {
            Key = row.Theme.Key,
            Alias = row.Theme.Alias,
            Name = row.Theme.Name,
            BaseLabel = row.BaseLabel,
            UsedBy = row.UsedBy,
            IsDefault = row.IsDefault,
            Swatch = row.Swatch,
            UpdatedUtc = row.Theme.UpdatedUtc,
        };
    }
}

/// <summary>What the theme editor needs besides a theme: the themes in code it may build on, and the site's default.</summary>
internal sealed record ThemeEditorResponseModel
{
    /// <summary>The themes in code, in the order the backoffice lists them.</summary>
    public required IReadOnlyList<ThemeBaseResponseModel> Bases { get; init; }

    /// <summary>The alias of the site's default theme.</summary>
    public required string DefaultTheme { get; init; }

    /// <summary>The most custom CSS a theme holds, in characters.</summary>
    public required int MaxCustomCssLength { get; init; }

    /// <summary>The alias the preview of a theme not saved yet is drawn under.</summary>
    public required string PreviewAlias { get; init; }
}

/// <summary>A theme in code a library theme may build on.</summary>
internal sealed record ThemeBaseResponseModel
{
    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>What the backoffice calls it: plain text or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Whether it offers the dark scheme, and so dark colours.</summary>
    public required bool DarkScheme { get; init; }

    /// <summary>The address of its stylesheet, which the preview loads; null for none.</summary>
    public string? Stylesheet { get; init; }

    /// <summary>The tokens it declares, in its order, with their defaults.</summary>
    public required IReadOnlyList<ThemeTokenResponseModel> Tokens { get; init; }
}

/// <summary>A token a theme in code declares.</summary>
internal sealed record ThemeTokenResponseModel
{
    /// <summary>Its name, such as <c>--jfb-color-accent</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Its default.</summary>
    public required string Default { get; init; }

    /// <summary>Its default in the dark scheme, for a colour of a theme with one; null otherwise.</summary>
    public string? DarkDefault { get; init; }
}

/// <summary>What a library theme changes of its base theme.</summary>
internal sealed record ThemeTokensResponseModel
{
    /// <summary>The tokens it sets in both schemes, by name.</summary>
    public required IReadOnlyDictionary<string, string> Light { get; init; }

    /// <summary>The colours it sets in the dark scheme, by name.</summary>
    public required IReadOnlyDictionary<string, string> Dark { get; init; }

    /// <summary>A theme's stored tokens.</summary>
    public static ThemeTokensResponseModel Of(ThemeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new ThemeTokensResponseModel { Light = tokens.Light, Dark = tokens.Dark };
    }
}

/// <summary>What a library theme changes of its base theme, as its editor saves it.</summary>
internal sealed record ThemeTokensRequestModel
{
    /// <summary>The tokens it sets in both schemes, by name.</summary>
    public required IReadOnlyDictionary<string, string> Light { get; init; }

    /// <summary>The colours it sets in the dark scheme, by name.</summary>
    public required IReadOnlyDictionary<string, string> Dark { get; init; }

    /// <summary>The tokens as the service holds them.</summary>
    public ThemeTokens ToTokens() => new() { Light = Light ?? new Dictionary<string, string>(), Dark = Dark ?? new Dictionary<string, string>() };
}

/// <summary>A library theme as its editor shows it.</summary>
internal sealed record ThemeResponseModel
{
    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias, fixed once created.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The alias of the theme in code it builds on.</summary>
    public required string BaseTheme { get; init; }

    /// <summary>What it changes of its base theme.</summary>
    public required ThemeTokensResponseModel Tokens { get; init; }

    /// <summary>Its custom CSS as typed; null for none.</summary>
    public string? CustomCss { get; init; }

    /// <summary>The version a change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>A stored theme.</summary>
    public static ThemeResponseModel Of(StoredTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new ThemeResponseModel
        {
            Key = theme.Key,
            Alias = theme.Alias,
            Name = theme.Name,
            BaseTheme = theme.BaseTheme,
            Tokens = ThemeTokensResponseModel.Of(ThemeTokens.Read(theme.Tokens)),
            CustomCss = theme.CustomCss,
            ConcurrencyVersion = theme.ConcurrencyVersion,
            UpdatedUtc = theme.UpdatedUtc,
        };
    }
}

/// <summary>A theme as its editor saves it.</summary>
internal sealed record ThemeRequestModel
{
    /// <summary>Its alias: lower-case letters, digits and hyphens, starting with a letter; a change keeps the one it has.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name, 1 to 255 characters.</summary>
    public required string Name { get; init; }

    /// <summary>The alias of the theme in code it builds on.</summary>
    public required string BaseTheme { get; init; }

    /// <summary>What it changes of its base theme.</summary>
    public required ThemeTokensRequestModel Tokens { get; init; }

    /// <summary>Its custom CSS as typed, at most 64 KB; null or blank for none.</summary>
    public string? CustomCss { get; init; }

    /// <summary>The theme as the service takes it.</summary>
    public ThemeInput ToInput() => new(Alias ?? string.Empty, Name ?? string.Empty, BaseTheme ?? string.Empty, Tokens?.ToTokens() ?? ThemeTokens.None, CustomCss);
}

/// <summary>A theme to create.</summary>
internal sealed record CreateThemeRequestModel
{
    /// <summary>The theme.</summary>
    public required ThemeRequestModel Theme { get; init; }
}

/// <summary>A change to a theme, based on the version read.</summary>
internal sealed record UpdateThemeRequestModel
{
    /// <summary>The version the change is based on.</summary>
    public required int ConcurrencyVersion { get; init; }

    /// <summary>The theme.</summary>
    public required ThemeRequestModel Theme { get; init; }
}

/// <summary>A copy's name.</summary>
internal sealed record DuplicateThemeRequestModel
{
    /// <summary>The copy's name, 1 to 255 characters.</summary>
    public required string Name { get; init; }
}

/// <summary>An exported theme to import.</summary>
internal sealed record ImportThemeRequestModel
{
    /// <summary>The exported JSON document, at most 256 KB.</summary>
    public required string Document { get; init; }
}

/// <summary>A theme as a file to save.</summary>
internal sealed record ThemeExportResponseModel
{
    /// <summary>The file's name, such as <c>autumn.jfb-theme.json</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>The JSON document.</summary>
    public required string Document { get; init; }
}

/// <summary>A theme as its editor holds it, for the preview.</summary>
internal sealed record PreviewThemeRequestModel
{
    /// <summary>The theme's alias; none for a theme not saved yet.</summary>
    public string? Alias { get; init; }

    /// <summary>The alias of the theme in code it builds on.</summary>
    public required string BaseTheme { get; init; }

    /// <summary>What it changes of its base theme.</summary>
    public required ThemeTokensRequestModel Tokens { get; init; }

    /// <summary>Its custom CSS as typed.</summary>
    public string? CustomCss { get; init; }
}

/// <summary>The layer the preview applies, what the sanitiser took out, and the problems a save would refuse.</summary>
internal sealed record ThemePreviewResponseModel
{
    /// <summary>The alias the layer is drawn under.</summary>
    public required string Alias { get; init; }

    /// <summary>The layer's CSS.</summary>
    public required string Css { get; init; }

    /// <summary>What the sanitiser took out of the custom CSS.</summary>
    public required IReadOnlyList<ThemeCssRemovalResponseModel> Removed { get; init; }

    /// <summary>The problems a save would refuse, each at its path.</summary>
    public required IReadOnlyList<LibraryProblemResponseModel> Problems { get; init; }
}

/// <summary>Something the sanitiser took out of a theme's custom CSS.</summary>
internal sealed record ThemeCssRemovalResponseModel
{
    /// <summary>The kind: <c>import</c>, <c>atRule</c>, <c>nested</c>, <c>address</c>, <c>script</c> or <c>fontFace</c>.</summary>
    public required string Code { get; init; }

    /// <summary>What went, shortened.</summary>
    public required string Text { get; init; }
}
