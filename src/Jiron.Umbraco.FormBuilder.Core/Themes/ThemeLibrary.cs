using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>What a theme's editor saves (ADR-0215).</summary>
/// <param name="Alias">Its alias, fixed once created: lower-case letters, digits and hyphens, starting with a letter.</param>
/// <param name="Name">Its name.</param>
/// <param name="BaseTheme">The alias of the theme in code it builds on.</param>
/// <param name="Tokens">What it changes of the base theme.</param>
/// <param name="CustomCss">Its custom CSS as typed, or null.</param>
internal sealed record ThemeInput(string Alias, string Name, string BaseTheme, ThemeTokens Tokens, string? CustomCss);

/// <summary>A saved theme, or the problems that refused it, each at its path.</summary>
/// <param name="Theme">The theme saved; null when refused or gone.</param>
/// <param name="Problems">Why it was refused.</param>
internal sealed record ThemeResult(StoredTheme? Theme, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>A row of the library's list of themes (ADR-0215).</summary>
/// <param name="Theme">The theme.</param>
/// <param name="BaseLabel">What the backoffice calls its base theme; its alias when the site lacks it.</param>
/// <param name="UsedBy">How many forms' drafts or live versions use it, forms in the recycle bin included.</param>
/// <param name="IsDefault">Whether it is the site's default theme.</param>
/// <param name="Swatch">Its surface, text and accent colours, as the theme draws them.</param>
internal sealed record ThemeRow(StoredTheme Theme, string BaseLabel, int UsedBy, bool IsDefault, IReadOnlyList<string> Swatch);

/// <summary>A layer as the editor's preview applies it: the CSS, what the sanitiser took out, and the tokens' problems.</summary>
/// <param name="Css">The layer's CSS.</param>
/// <param name="Removed">What the sanitiser took out of the custom CSS.</param>
/// <param name="Problems">The problems the tokens or the base theme would be refused with.</param>
internal sealed record ThemePreview(string Css, IReadOnlyList<ThemeCssRemoval> Removed, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>How a delete ended.</summary>
internal enum ThemeDelete
{
    /// <summary>The theme is gone.</summary>
    Deleted,

    /// <summary>There was no such theme.</summary>
    NotFound,

    /// <summary>The theme is the site's default, which Settings > General names; it stays.</summary>
    IsDefault,
}

/// <summary>
/// The library's themes as their screens work with them (ROADMAP 12.1, ADR-0215): the rows with how many forms use each,
/// which forms those are as a user may see them, the changes a user makes, each checked over its base theme and audited,
/// a duplicate under a free alias, export and import as one JSON document, and the layer the editor's preview applies.
/// Every change tells every server through <see cref="FormBuilderThemeCacheRefresher"/>. A theme's key is the name-based
/// key of its alias (<see cref="FormLibraryUses.ThemeKey"/>), the key its uses go by.
/// </summary>
internal sealed class ThemeLibrary(
    IThemeRepository repository,
    FormBuilderThemes registry,
    LibraryUsers users,
    ILibraryUseRepository uses,
    IFormBuilderSettingsService settings,
    AppCaches appCaches,
    DistributedCache distributedCache,
    IAuditRepository audit)
{
    /// <summary>The audit action of a theme created, a duplicate's and an import's included.</summary>
    public const string CreatedAction = "theme.created";

    /// <summary>The audit action of a theme saved.</summary>
    public const string SavedAction = "theme.saved";

    /// <summary>The audit action of a theme deleted.</summary>
    public const string DeletedAction = "theme.deleted";

    /// <summary>The format an exported theme names.</summary>
    public const string Format = "jfb-theme";

    /// <summary>The version of the export format.</summary>
    public const int FormatVersion = 1;

    /// <summary>The alias the preview of a theme not saved yet is drawn under.</summary>
    public const string PreviewAlias = "jfb-preview";

    /// <summary>The largest exported theme an import reads.</summary>
    public const int MaxDocumentLength = 256 * 1024;

    private const string EntityType = "theme";

    // The colours a row's swatch shows, in its order.
    private static readonly ImmutableArray<string> SwatchTokens = ["--jfb-color-surface", "--jfb-color-text", "--jfb-color-accent"];

    /// <summary>A page of the themes by name, each with how many forms use it, whether it is the default, and its swatch.</summary>
    public async Task<Page<ThemeRow>> RowsAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var rows = await repository.ListAsync(null, page, cancellationToken);
        var counts = await uses.FormCountsAsync(LibraryUseKind.Theme, cancellationToken);
        var fallback = await DefaultAliasAsync(cancellationToken);
        return new Page<ThemeRow>(
            [
                .. rows.Items.Select(row => new ThemeRow(
                    row,
                    registry.Find(row.BaseTheme)?.Label ?? row.BaseTheme,
                    counts.GetValueOrDefault(row.Key),
                    string.Equals(row.Alias, fallback, StringComparison.OrdinalIgnoreCase),
                    Swatch(row))),
            ],
            rows.Total);
    }

    /// <summary>The theme of this key, or null.</summary>
    public Task<StoredTheme?> GetAsync(Guid key, CancellationToken cancellationToken = default) => repository.GetAsync(key, cancellationToken);

    /// <summary>The alias of the site's default theme, as Settings > General names it.</summary>
    public async Task<string> DefaultAliasAsync(CancellationToken cancellationToken = default) =>
        (await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken)).Value.Theme ?? BuiltInThemes.DefaultAlias;

    /// <summary>The forms whose draft or live version names the theme, as the user may see them.</summary>
    public Task<LibraryItemUsers> UsersAsync(AccessUser user, Guid key, CancellationToken cancellationToken = default) =>
        users.ForAsync(user, LibraryUseKind.Theme, key, cancellationToken);

    /// <summary>
    /// The problems of a theme as an editor holds it, each at its path: an alias outside the grammar or a theme in code's,
    /// a missing or long name, a base theme the site lacks or one of the library, tokens the base theme does not declare,
    /// and custom CSS that is too long.
    /// </summary>
    public IReadOnlyList<DefinitionProblem> Check(ThemeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new DefinitionProblems();
        if (!FormBuilderThemes.IsAlias(input.Alias))
        {
            problems.Add(DefinitionErrors.AliasInvalid, "$.alias", "An alias is a lower-case letter, then up to 63 lower-case letters, digits and hyphens.");
        }
        else if (registry.Find(input.Alias) is not null)
        {
            problems.Add(DefinitionErrors.AliasReserved, "$.alias", $"The site's code has a theme '{input.Alias}'; a theme of the library needs another alias.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            problems.Add(DefinitionErrors.InvalidValue, "$.name", "A theme has a name.");
        }
        else if (input.Name.Length > AliasedLibraryTable<ThemeDto>.MaxNameLength)
        {
            problems.Add(DefinitionErrors.LimitExceeded, "$.name", $"A name holds at most {AliasedLibraryTable<ThemeDto>.MaxNameLength} characters.");
        }

        if (registry.Find(input.BaseTheme) is not { } baseTheme)
        {
            problems.Add(DefinitionErrors.UnknownType, "$.baseTheme", $"The site's code has no theme '{input.BaseTheme}' to build on.");
        }
        else
        {
            input.Tokens.Check(baseTheme, problems, "$.tokens");
        }

        if (input.CustomCss is { Length: > ThemeCss.MaxLength })
        {
            problems.Add(DefinitionErrors.LimitExceeded, "$.customCss", $"Custom CSS holds at most {ThemeCss.MaxLength / 1024} KB.");
        }

        return problems.ToReadOnlyList();
    }

    /// <summary>Creates a theme for a user, audited, its key the name-based key of its alias; the problems that refuse it otherwise.</summary>
    public async Task<ThemeResult> CreateAsync(ThemeInput input, Guid? userKey, CancellationToken cancellationToken = default) =>
        await CreateAsync(input, userKey, duplicateOf: null, cancellationToken);

    /// <summary>
    /// Changes a theme for a user, audited; its alias stays. No theme and no problems when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile.
    /// </summary>
    public async Task<ThemeResult> UpdateAsync(Guid key, int expectedConcurrencyVersion, ThemeInput input, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return new ThemeResult(null, []);
        }

        var kept = input with { Alias = row.Alias };
        if (Check(kept) is { Count: > 0 } problems)
        {
            return new ThemeResult(null, problems);
        }

        try
        {
            var changed = await repository.UpdateAsync(new ThemeUpdate(row.Id, expectedConcurrencyVersion, kept.Name.Trim(), kept.BaseTheme, kept.Tokens.Write(), CssOf(kept.CustomCss)), cancellationToken);
            if (changed is not null)
            {
                await AuditAsync(SavedAction, changed, userKey, null);
            }

            return new ThemeResult(changed, []);
        }
        finally
        {
            Changed(row.Alias);
        }
    }

    /// <summary>
    /// Deletes a theme for a user, audited, unless it is the site's default. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it changed meanwhile, and with <see cref="FormBuilderConflict.InUse"/>
    /// while a form uses it.
    /// </summary>
    public async Task<ThemeDelete> DeleteAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            return ThemeDelete.NotFound;
        }

        if (string.Equals(row.Alias, await DefaultAliasAsync(cancellationToken), StringComparison.OrdinalIgnoreCase))
        {
            return ThemeDelete.IsDefault;
        }

        if (!await repository.DeleteAsync(row.Id, expectedConcurrencyVersion, cancellationToken))
        {
            return ThemeDelete.NotFound;
        }

        Changed(row.Alias);
        await AuditAsync(DeletedAction, row, userKey, null);
        return ThemeDelete.Deleted;
    }

    /// <summary>A copy of a theme with the name given and the first free alias after its own (<c>autumn-2</c>); null when it does not exist.</summary>
    public async Task<ThemeResult?> DuplicateAsync(Guid key, string name, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (await repository.GetAsync(key, cancellationToken) is not { } source)
        {
            return null;
        }

        var input = new ThemeInput(await FreeAliasAsync(source.Alias, cancellationToken), name, source.BaseTheme, ThemeTokens.Read(source.Tokens), source.CustomCss);
        return await CreateAsync(input, userKey, source.Key, cancellationToken);
    }

    /// <summary>A theme as the JSON document an export writes: its format and version, name, alias, base theme, tokens and custom CSS.</summary>
    public static string Export(StoredTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var tokens = JsonNode.Parse(ThemeTokens.Read(theme.Tokens).Write());
        var document = new JsonObject
        {
            ["format"] = Format,
            ["version"] = FormatVersion,
            ["name"] = theme.Name,
            ["alias"] = theme.Alias,
            ["baseTheme"] = theme.BaseTheme,
            ["tokens"] = tokens,
            ["customCss"] = theme.CustomCss,
        };
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// A new theme from an exported document, audited as created: its name, and the document's alias or the first free one
    /// after it. The problems that refuse the document otherwise, each at its path.
    /// </summary>
    public async Task<ThemeResult> ImportAsync(string document, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new DefinitionProblems();
        JsonObject? root = null;
        if (document.Length > MaxDocumentLength)
        {
            problems.Add(DefinitionErrors.LimitExceeded, "$", $"An exported theme holds at most {MaxDocumentLength / 1024} KB.");
        }
        else
        {
            try
            {
                root = JsonNode.Parse(document) as JsonObject;
            }
            catch (JsonException)
            {
                root = null;
            }

            if (root is null)
            {
                problems.Add(DefinitionErrors.InvalidJson, "$", "The file is no exported theme: it is not a JSON object.");
            }
            else if (Text(root, "format") != Format || root["version"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != FormatVersion)
            {
                problems.Add(DefinitionErrors.UnsupportedFormat, "$.format", $"The file is no exported theme of this version ({Format}, version {FormatVersion}).");
            }
        }

        if (root is null || problems.ToReadOnlyList().Count > 0)
        {
            return new ThemeResult(null, problems.ToReadOnlyList());
        }

        var alias = Text(root, "alias") ?? string.Empty;
        var name = Text(root, "name") ?? string.Empty;
        var tokens = root["tokens"] is JsonObject held ? ThemeTokens.Read(held.ToJsonString()) : ThemeTokens.None;
        var input = new ThemeInput(alias, name, Text(root, "baseTheme") ?? string.Empty, tokens, Text(root, "customCss"));
        if (FormBuilderThemes.IsAlias(alias) && registry.Find(alias) is null && await repository.GetByAliasAsync(alias, cancellationToken) is not null)
        {
            input = input with { Alias = await FreeAliasAsync(alias, cancellationToken) };
        }

        return await CreateAsync(input, userKey, duplicateOf: null, cancellationToken);
    }

    /// <summary>The layer an editor's preview applies, drawn under the theme's alias or, for one not saved yet, <see cref="PreviewAlias"/>.</summary>
    public ThemePreview Preview(string? alias, string baseTheme, ThemeTokens tokens, string? customCss)
    {
        ArgumentNullException.ThrowIfNull(baseTheme);
        ArgumentNullException.ThrowIfNull(tokens);
        var drawn = FormBuilderThemes.IsAlias(alias) ? alias : PreviewAlias;
        var problems = new DefinitionProblems();
        if (registry.Find(baseTheme) is not { } found)
        {
            problems.Add(DefinitionErrors.UnknownType, "$.baseTheme", $"The site's code has no theme '{baseTheme}' to build on.");
            return new ThemePreview(string.Empty, [], problems.ToReadOnlyList());
        }

        tokens.Check(found, problems, "$.tokens");
        var css = customCss is { Length: > ThemeCss.MaxLength } ? null : customCss;
        var stored = new StoredTheme(0, Guid.Empty, drawn, drawn, baseTheme, tokens.Write(), css, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return new ThemePreview(LibraryThemes.LayerOf(stored, found), ThemeCss.Sanitise(css, drawn).Removed, problems.ToReadOnlyList());
    }

    private async Task<ThemeResult> CreateAsync(ThemeInput input, Guid? userKey, Guid? duplicateOf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Check(input) is { Count: > 0 } problems)
        {
            return new ThemeResult(null, problems);
        }

        try
        {
            var created = await repository.CreateAsync(
                new NewTheme(FormLibraryUses.ThemeKey(input.Alias), input.Alias, input.Name.Trim(), input.BaseTheme, input.Tokens.Write(), CssOf(input.CustomCss)),
                cancellationToken);
            Changed(created.Alias);
            await AuditAsync(CreatedAction, created, userKey, duplicateOf);
            return new ThemeResult(created, []);
        }
        catch (FormBuilderConflictException exception) when (exception.Conflict is FormBuilderConflict.DuplicateAlias or FormBuilderConflict.DuplicateKey)
        {
            return new ThemeResult(null, [new DefinitionProblem(DefinitionErrors.AliasDuplicate, "$.alias", "Another theme has this alias (letter case does not count).")]);
        }
    }

    // The theme's surface, text and accent, as it draws them in the light scheme.
    private IReadOnlyList<string> Swatch(StoredTheme theme)
    {
        var tokens = ThemeTokens.Read(theme.Tokens).Light;
        var defaults = registry.Find(theme.BaseTheme)?.Tokens;
        return
        [
            .. SwatchTokens
                .Select(token => tokens.GetValueOrDefault(token) ?? defaults?.GetValueOrDefault(token))
                .OfType<string>(),
        ];
    }

    private static string? CssOf(string? css) => string.IsNullOrWhiteSpace(css) ? null : css;

    private static string? Text(JsonObject root, string name) =>
        root[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // The theme's alias with the first number after it that no theme has: autumn-2, autumn-3.
    private async Task<string> FreeAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var stem = alias.Length > 58 ? alias[..58] : alias;
        for (var number = 2; ; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem}-{number}");
            if (registry.Find(candidate) is null && await repository.GetByAliasAsync(candidate, cancellationToken) is null)
            {
                return candidate;
            }
        }
    }

    // Forgotten here at once, as the next read on this server may come in the same request, then on every other server.
    private void Changed(string alias)
    {
        var change = new ThemeCacheChange(alias);
        FormBuilderThemeCacheRefresher.Forget(appCaches.RuntimeCache, change);
        distributedCache.RefreshByPayload(FormBuilderThemeCacheRefresher.UniqueId, [change]);
    }

    private Task<StoredAuditEntry> AuditAsync(string action, StoredTheme theme, Guid? userKey, Guid? duplicateOf)
    {
        var details = new AuditDetails().AddIdentifier("alias", theme.Alias).AddIdentifier("baseTheme", theme.BaseTheme);
        if (duplicateOf is { } source)
        {
            details.Add("duplicateOf", source);
        }

        return audit.AddAsync(new NewAuditEntry(action, EntityType, theme.Key, userKey, details), CancellationToken.None);
    }
}
