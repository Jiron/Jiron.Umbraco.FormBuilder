using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>
/// A theme of the library as the website renders it (ADR-0215): its base theme, whose stylesheet, class map and views it
/// takes, with the form carrying both themes' classes, and its layer on top: what it changes of the base theme's tokens
/// and its sanitised custom CSS, which the page loads after the base theme's stylesheet.
/// </summary>
internal sealed class LibraryTheme : IFormBuilderTheme
{
    /// <summary>A library theme over its base theme.</summary>
    /// <param name="stored">The theme's row.</param>
    /// <param name="baseTheme">The theme in code it builds on.</param>
    public LibraryTheme(StoredTheme stored, IFormBuilderTheme baseTheme)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(baseTheme);
        Base = baseTheme;
        Name = stored.Alias;
        Label = stored.Name;
        Version = stored.ConcurrencyVersion;
        var classes = new Dictionary<string, string>(baseTheme.Classes, StringComparer.Ordinal);
        classes[FormBuilderThemeSlots.Form] = string.Join(' ', new[] { $"jfb-theme-{baseTheme.Name}", baseTheme.Classes.GetValueOrDefault(FormBuilderThemeSlots.Form) }.Where(each => !string.IsNullOrEmpty(each)));
        Classes = classes.ToFrozenDictionary(StringComparer.Ordinal);
        Layer = LibraryThemes.LayerOf(stored, baseTheme);
        LayerTag = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Layer)))[..32];
    }

    /// <summary>The theme in code it builds on.</summary>
    public IFormBuilderTheme Base { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Label { get; }

    /// <inheritdoc />
    public string? Stylesheet => Base.Stylesheet;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Classes { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tokens => Base.Tokens;

    /// <inheritdoc />
    public bool DarkScheme => Base.DarkScheme;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> DarkTokens => Base.DarkTokens;

    /// <inheritdoc />
    public string ViewFolder => Base.ViewFolder;

    /// <summary>The row's version, which the layer's address carries, so a page never keeps an older layer.</summary>
    public int Version { get; }

    /// <summary>The layer's CSS.</summary>
    public string Layer { get; }

    /// <summary>The layer's entity tag: a hash of its CSS.</summary>
    public string LayerTag { get; }
}

/// <summary>A change to a library theme, for every server: its alias, or null for every theme.</summary>
/// <param name="Alias">The theme's alias as stored.</param>
internal sealed record ThemeCacheChange(string? Alias);

/// <summary>The keys of the library themes each server keeps.</summary>
internal static class ThemeCacheKeys
{
    /// <summary>What every key starts with.</summary>
    public const string Prefix = "Jiron.FormBuilder.Theme.";

    /// <summary>The theme of an alias, or that there is none.</summary>
    public static string Of(string alias) => Prefix + alias.ToLowerInvariant();

    /// <summary>The read under way of an alias.</summary>
    public static string Loading(string alias) => Of(alias) + ".loading";
}

/// <summary>
/// The library's themes as the website renders them (ADR-0215): read once per alias on each server, an alias the library
/// lacks as well, and forgotten everywhere through <see cref="FormBuilderThemeCacheRefresher"/> when a theme changes.
/// </summary>
/// <param name="themes">The library's themes.</param>
/// <param name="registry">The themes in code, which a library theme builds on.</param>
/// <param name="appCaches">The caches.</param>
internal sealed class LibraryThemes(IThemeRepository themes, FormBuilderThemes registry, AppCaches appCaches)
{
    // How long a theme stays without a change: the refresher drops it at once on every change; this bounds a missed one.
    private static readonly TimeSpan Kept = TimeSpan.FromHours(1);

    /// <summary>
    /// The library theme of this alias as the website renders it; null when the library has none, or none whose base
    /// theme the site still has.
    /// </summary>
    /// <param name="alias">The alias a form or placement names.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<LibraryTheme?> FindAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (!FormBuilderThemes.IsAlias(alias.ToLowerInvariant()))
        {
            return null;
        }

        var found = await RuntimeCacheLoads.GetAsync(
            appCaches.RuntimeCache,
            ThemeCacheKeys.Of(alias),
            ThemeCacheKeys.Loading(alias),
            // The load is shared by every request that waits for it: it is no one request's to cancel.
            async () => new Found(await themes.GetByAliasAsync(alias, CancellationToken.None) is { } stored && registry.Find(stored.BaseTheme) is { } baseTheme ? new LibraryTheme(stored, baseTheme) : null),
            Kept,
            cancellationToken);
        return found.Theme;
    }

    /// <summary>
    /// A library theme's layer: the tokens it changes on its forms, the dark scheme's colours under the base theme's dark
    /// selectors, then its custom CSS, sanitised. Tokens the base theme does not declare, and values that are not one
    /// declaration, are left out, so a theme written by hand never breaks out of its rules.
    /// </summary>
    /// <param name="stored">The theme's row.</param>
    /// <param name="baseTheme">Its base theme.</param>
    public static string LayerOf(StoredTheme stored, IFormBuilderTheme baseTheme)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(baseTheme);
        var tokens = ThemeTokens.Read(stored.Tokens);
        var scope = ThemeCss.ScopeOf(stored.Alias);
        var css = new StringBuilder();
        css.Append("/* Jiron Form Builder: the library theme ").Append(stored.Alias).Append(", on ").Append(baseTheme.Name).Append(" (ADR-0215) */\n");
        Block(css, string.Empty, scope, Valid(tokens.Light, baseTheme));
        var dark = Valid(tokens.DarkScheme(baseTheme), baseTheme);
        if (dark.Count > 0)
        {
            css.Append("@media screen {\n");
            Block(css, "  ", $"{scope}.jfb-scheme-dark,\n  .jfb-scheme-dark {scope}", dark);
            css.Append("}\n@media screen and (prefers-color-scheme: dark) {\n");
            Block(css, "  ", $"{scope}.jfb-scheme-auto,\n  .jfb-scheme-auto {scope}", dark);
            css.Append("}\n");
        }

        css.Append(ThemeCss.Sanitise(stored.CustomCss, stored.Alias).Css);
        return css.ToString();
    }

    private static List<KeyValuePair<string, string>> Valid(IReadOnlyDictionary<string, string> tokens, IFormBuilderTheme baseTheme) =>
        [.. tokens.Where(each => baseTheme.Tokens.ContainsKey(each.Key) && each.Value.Length <= ThemeTokens.MaxValueLength && FormBuilderThemes.IsTokenValue(each.Value)).OrderBy(each => each.Key, StringComparer.Ordinal)];

    private static void Block(StringBuilder css, string indent, string selector, List<KeyValuePair<string, string>> tokens)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        css.Append(indent).Append(selector).Append(" {\n");
        foreach (var (token, value) in tokens)
        {
            css.Append(indent).Append("  ").Append(token).Append(": ").Append(value.Trim()).Append(";\n");
        }

        css.Append(indent).Append("}\n");
    }

    // What a read found, kept as a value so an alias the library lacks is kept too.
    private sealed record Found(LibraryTheme? Theme);
}

/// <summary>The notification Umbraco sends when the library themes' cache is refreshed.</summary>
/// <param name="messageObject">The payload.</param>
/// <param name="messageType">The kind of message.</param>
internal sealed class FormBuilderThemeCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);

/// <summary>Forgets a changed library theme on every server (ADR-0215), as the option lists' refresher does (ADR-0073).</summary>
internal sealed class FormBuilderThemeCacheRefresher(
    AppCaches appCaches,
    IJsonSerializer serializer,
    IEventAggregator eventAggregator,
    ICacheRefresherNotificationFactory factory)
    : PayloadCacheRefresherBase<FormBuilderThemeCacheRefresherNotification, ThemeCacheChange>(appCaches, serializer, eventAggregator, factory)
{
    /// <summary>The refresher's id, which every server knows it by.</summary>
    public static readonly Guid UniqueId = new("3f1c9b0e-7a24-4d5e-9c61-2e8b47d0a915");

    /// <inheritdoc />
    public override Guid RefresherUniqueId => UniqueId;

    /// <inheritdoc />
    public override string Name => "Jiron Form Builder themes";

    /// <inheritdoc />
    public override void Refresh(ThemeCacheChange[] payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        foreach (var change in payloads)
        {
            Forget(AppCaches.RuntimeCache, change);
        }

        base.Refresh(payloads);
    }

    /// <summary>Forgets a changed theme on this server, or every theme: what the library does at once, before it tells the others.</summary>
    /// <param name="cache">This server's runtime cache.</param>
    /// <param name="change">The change.</param>
    internal static void Forget(IAppPolicyCache cache, ThemeCacheChange change)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(change);
        cache.ClearByKey(change.Alias is { } alias ? ThemeCacheKeys.Of(alias) : ThemeCacheKeys.Prefix);
    }

    /// <inheritdoc />
    public override void RefreshAll()
    {
        AppCaches.RuntimeCache.ClearByKey(ThemeCacheKeys.Prefix);
        base.RefreshAll();
    }
}
