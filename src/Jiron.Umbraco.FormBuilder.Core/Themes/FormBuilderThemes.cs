using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>The themes a site registered, in the order the backoffice lists them (ADR-0105).</summary>
public sealed class FormBuilderThemeCollection : BuilderCollectionBase<IFormBuilderTheme>
{
    /// <summary>A collection of the themes the builder creates.</summary>
    public FormBuilderThemeCollection(Func<IEnumerable<IFormBuilderTheme>> items)
        : base(items)
    {
    }
}

/// <summary>
/// The ordered collection of themes (ADR-0105), reached through <c>builder.JironFormBuilder().Themes()</c>. It holds the
/// built-in themes from the moment it is made, so a composer appends, inserts, replaces or removes a theme whether it
/// runs before Jiron Form Builder's own composer or after it, for example <c>.Append&lt;AcmeTheme&gt;()</c> or
/// <c>.Remove&lt;Bootstrap5Theme&gt;()</c>. Each theme is a singleton, created by the container.
/// </summary>
public sealed class FormBuilderThemeCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderThemeCollectionBuilder, FormBuilderThemeCollection, IFormBuilderTheme>
{
    /// <summary>A builder that holds the built-in themes.</summary>
    public FormBuilderThemeCollectionBuilder() => Append(BuiltInThemes.All);

    /// <inheritdoc />
    protected override FormBuilderThemeCollectionBuilder This => this;
}

/// <summary>
/// The themes by alias (ADR-0015, ADR-0105), checked as the registry is made: an alias that is not lower-case letters,
/// digits and hyphens or that two themes share, no <c>default</c>, a stylesheet on another host, a class map with a slot
/// or a class that is none, a token that is not <c>--jfb-*</c>, or a view folder that is no folder name stops the site,
/// with every problem named. A theme a form, a placement or the General settings name that the site lacks renders as
/// <c>default</c>, logged once per name.
/// </summary>
internal sealed partial class FormBuilderThemes
{
    /// <summary>The longest alias.</summary>
    public const int MaxAliasLength = 64;

    private static readonly FrozenSet<string> Slots = FormBuilderThemeSlots.All.ToFrozenSet(StringComparer.Ordinal);

    // The characters of a URL path besides letters, digits and escapes (RFC 3986), which a stylesheet's address keeps to.
    private const string PathCharacters = "-._~!$&'()*+,;=:@/%";

    private readonly FrozenDictionary<string, IFormBuilderTheme> _themes;

    private readonly ILogger<FormBuilderThemes> _logger;

    // The names a form or a setting gave that named no theme in this process, so each is logged once.
    private readonly ConcurrentDictionary<string, bool> _missing = new(StringComparer.Ordinal);

    /// <summary>A registry of the given themes, in the order the backoffice lists them.</summary>
    /// <exception cref="InvalidOperationException">The themes have problems; the message lists every one.</exception>
    public FormBuilderThemes(IEnumerable<IFormBuilderTheme> themes, ILogger<FormBuilderThemes> logger)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(logger);
        var list = themes.ToList();
        if (Problems(list) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }

        All = [.. list];
        _themes = list.ToFrozenDictionary(theme => theme.Name, StringComparer.Ordinal);
        _logger = logger;
    }

    /// <summary>Every theme, in the order the backoffice lists them.</summary>
    public ImmutableArray<IFormBuilderTheme> All { get; }

    /// <summary>The theme <c>default</c>.</summary>
    public IFormBuilderTheme Default => _themes[BuiltInThemes.DefaultAlias];

    /// <summary>Whether a text is a theme's alias as the grammar has it, whether or not the site has the theme.</summary>
    /// <param name="alias">The text.</param>
    public static bool IsAlias([NotNullWhen(true)] string? alias) => alias is not null && Alias().IsMatch(alias);

    /// <summary>The theme of this alias, or null.</summary>
    /// <param name="alias">The alias.</param>
    public IFormBuilderTheme? Find(string? alias) => alias is null ? null : _themes.GetValueOrDefault(alias);

    /// <summary>
    /// The theme of the first alias given that is not blank, or <c>default</c> when none is given; an alias that names no
    /// theme renders as <c>default</c> and is logged once.
    /// </summary>
    /// <param name="aliases">The aliases in their order: the placement's, the form's, the site's.</param>
    public IFormBuilderTheme Resolve(params ReadOnlySpan<string?> aliases)
    {
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (Find(alias) is { } theme)
            {
                return theme;
            }

            if (_missing.TryAdd(alias, true))
            {
                LogMissing(_logger, alias);
            }

            return Default;
        }

        return Default;
    }

    /// <summary>What stops these themes from being the registry, one message per problem.</summary>
    /// <param name="themes">The themes.</param>
    public static IReadOnlyList<string> Problems(IReadOnlyList<IFormBuilderTheme> themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        var problems = new List<string>();
        foreach (var theme in themes)
        {
            var name = theme.GetType().FullName ?? theme.GetType().Name;
            if (theme.Name is not { } alias || !Alias().IsMatch(alias))
            {
                problems.Add($"The theme {name} has the alias '{theme.Name}': an alias is lower-case letters, digits and hyphens, starting with a letter, at most {MaxAliasLength} characters.");
            }

            if (string.IsNullOrWhiteSpace(theme.Label))
            {
                problems.Add($"The theme '{theme.Name}' ({name}) has no label.");
            }

            if (theme.Stylesheet is { } stylesheet && !IsLocalPath(stylesheet))
            {
                problems.Add($"The theme '{theme.Name}' has the stylesheet '{stylesheet}': a stylesheet is a local path on the site, such as /css/forms.css, since nothing loads from another host on a visitor's page.");
            }

            foreach (var (slot, classes) in theme.Classes ?? FrozenDictionary<string, string>.Empty)
            {
                if (!Slots.Contains(slot))
                {
                    problems.Add($"The theme '{theme.Name}' gives classes to the slot '{slot}', which is none of FormBuilderThemeSlots.");
                }
                else if (!AreClasses(classes))
                {
                    problems.Add($"The theme '{theme.Name}' gives the slot '{slot}' the classes '{classes}': classes are names without quotes, angle brackets or ampersands, separated by single spaces.");
                }
            }

            foreach (var (token, value) in theme.Tokens ?? FrozenDictionary<string, string>.Empty)
            {
                if (!Token().IsMatch(token) || !IsTokenValue(value))
                {
                    problems.Add($"The theme '{theme.Name}' has the token '{token}': a token is --jfb- and lower-case letters, digits and hyphens, with a value of one declaration and no address.");
                }
            }

            var dark = theme.DarkTokens ?? FrozenDictionary<string, string>.Empty;
            if (dark.Count > 0 && !theme.DarkScheme)
            {
                problems.Add($"The theme '{theme.Name}' has dark colours but no dark scheme: DarkTokens is for a theme whose DarkScheme is true.");
            }

            foreach (var (token, value) in dark)
            {
                if (!token.StartsWith("--jfb-color-", StringComparison.Ordinal) || theme.Tokens?.ContainsKey(token) != true || !IsTokenValue(value))
                {
                    problems.Add($"The theme '{theme.Name}' has the dark colour '{token}': a dark colour is one of its Tokens whose name starts with --jfb-color-, with a value of one declaration and no address.");
                }
            }

            if (theme.ViewFolder is not { } folder || !Folder().IsMatch(folder))
            {
                problems.Add($"The theme '{theme.Name}' has the view folder '{theme.ViewFolder}': a folder name of lower-case letters, digits and hyphens below Views/JironFormBuilder/.");
            }
        }

        foreach (var twice in themes.GroupBy(theme => theme.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add($"The alias '{twice.Key}' belongs to {twice.Count()} themes: {string.Join(", ", twice.Select(theme => theme.GetType().FullName))}.");
        }

        if (!themes.Any(theme => theme.Name == BuiltInThemes.DefaultAlias))
        {
            problems.Add($"No theme has the alias '{BuiltInThemes.DefaultAlias}', which a form falls back to: replace DefaultTheme with a theme of that alias, never remove it.");
        }

        return problems;
    }

    // A path on the site: one leading slash, no second one after it, only a URL path's characters.
    private static bool IsLocalPath(string path) =>
        path.Length > 1 && path[0] == '/' && path[1] != '/' && path.All(character => char.IsAsciiLetterOrDigit(character) || PathCharacters.Contains(character, StringComparison.Ordinal));

    // Class names separated by single spaces, each of visible ASCII without quotes, angle brackets or ampersands.
    private static bool AreClasses(string? classes) =>
        !string.IsNullOrEmpty(classes)
        && classes.Split(' ').All(name => name.Length > 0 && name.All(character => character is > ' ' and < (char)127 and not ('"' or '\'' or '<' or '>' or '&')));

    /// <summary>Whether a text is one declaration's value: no end of a declaration or a block, no markup, no control characters, no address.</summary>
    /// <param name="value">The value.</param>
    internal static bool IsTokenValue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Any(character => character is ';' or '{' or '}' or '<' or '>' || char.IsControl(character))
        && !value.Contains("url(", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Alias();

    [GeneratedRegex(@"^--jfb-[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Folder();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "A form or the settings name the theme '{Alias}', which the site does not have; the form renders with the default theme.")]
    private static partial void LogMissing(ILogger logger, string alias);
}

/// <summary>
/// Stops the site when its themes cannot be its registry (ADR-0105), before it serves anything, with every problem named.
/// </summary>
/// <param name="themes">The registered themes, which the check creates.</param>
internal sealed class ThemeStartupCheck(FormBuilderThemeCollection themes) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        var problems = FormBuilderThemes.Problems([.. themes]);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
