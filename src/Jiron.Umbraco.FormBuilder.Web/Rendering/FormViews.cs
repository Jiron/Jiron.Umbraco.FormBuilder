using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Which view writes each part of a form (ADR-0015, ADR-0093): the form's own override in <c>_forms/&lt;alias&gt;/</c> of
/// the view folder, else the folder's view, which the Razor class library ships under the same paths, so a site's file
/// at such a path wins over it. MVC's view engine answers whether a path exists and keeps its answers, dropping them when
/// a view changes under runtime compilation. The themes add their folders and their order with 6.3. In Development, a
/// view of the site's own whose contract header names an older version than the built-in view's, or none, is logged
/// once per view.
/// </summary>
/// <param name="engine">MVC's view engine.</param>
/// <param name="environment">The host, whose content root holds the site's own views.</param>
/// <param name="logger">Where an outdated view is logged.</param>
internal sealed partial class FormViews(ICompositeViewEngine engine, IWebHostEnvironment environment, ILogger<FormViews> logger)
{
    /// <summary>The folder of the views of every theme.</summary>
    public const string Root = "/Views/JironFormBuilder/";

    /// <summary>The view folder of the default theme.</summary>
    public const string DefaultFolder = "default";

    /// <summary>The folder below a view folder that holds a form's overrides.</summary>
    public const string Overrides = "_forms";

    // The views whose headers were looked at in this process.
    private readonly ConcurrentDictionary<string, bool> _checked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The paths a form's view is looked for at, the override first.</summary>
    /// <param name="alias">The form's alias.</param>
    /// <param name="view">The view's name below the folder, such as <c>Field</c> or <c>fields/email</c>.</param>
    public static IReadOnlyList<string> Candidates(string alias, string view) =>
    [
        $"~{Root}{DefaultFolder}/{Overrides}/{alias}/{view}.cshtml",
        $"~{Root}{DefaultFolder}/{view}.cshtml",
    ];

    /// <summary>The views of a form's parts.</summary>
    /// <param name="alias">The form's alias.</param>
    /// <param name="cancellationToken">Cancels reading a view's header.</param>
    /// <exception cref="InvalidOperationException">A part has no view at any of its paths.</exception>
    public async Task<(string Form, FormViewPaths Parts)> PartsAsync(string alias, CancellationToken cancellationToken)
    {
        var form = await FindAsync(alias, "Form", cancellationToken);
        return (form, new FormViewPaths
        {
            Page = await FindAsync(alias, "Page", cancellationToken),
            Group = await FindAsync(alias, "Group", cancellationToken),
            Field = await FindAsync(alias, "Field", cancellationToken),
            Navigation = await FindAsync(alias, "Navigation", cancellationToken),
            Progress = await FindAsync(alias, "Progress", cancellationToken),
            ErrorSummary = await FindAsync(alias, "ErrorSummary", cancellationToken),
            Outcome = await FindAsync(alias, "Outcome", cancellationToken),
            Notice = await FindAsync(alias, "Notice", cancellationToken),
        });
    }

    /// <summary>A view of the default folder, for what renders without a form, such as the note while the migrations are pending.</summary>
    /// <param name="view">The view's name below the folder, such as <c>Notice</c>.</param>
    /// <param name="cancellationToken">Cancels reading the view's header.</param>
    /// <exception cref="InvalidOperationException">The default folder has no such view.</exception>
    public async Task<string> DefaultAsync(string view, CancellationToken cancellationToken)
    {
        var path = $"~{Root}{DefaultFolder}/{view}.cshtml";
        if (!engine.GetView(executingFilePath: null, path, isMainPage: false).Success)
        {
            throw new InvalidOperationException($"No view writes '{view}'. The path looked at: {path}.");
        }

        await CheckAsync(path, cancellationToken);
        return path;
    }

    /// <summary>The view that writes the control of a field type, <c>fields/&lt;type&gt;.cshtml</c>.</summary>
    /// <param name="alias">The form's alias.</param>
    /// <param name="type">The field type's alias.</param>
    /// <param name="cancellationToken">Cancels reading the view's header.</param>
    /// <exception cref="InvalidOperationException">The type has no control view at any of its paths.</exception>
    public Task<string> ControlAsync(string alias, string type, CancellationToken cancellationToken) => FindAsync(alias, "fields/" + type, cancellationToken);

    private async Task<string> FindAsync(string alias, string view, CancellationToken cancellationToken)
    {
        var candidates = Candidates(alias, view);
        foreach (var path in candidates)
        {
            if (engine.GetView(executingFilePath: null, path, isMainPage: false).Success)
            {
                await CheckAsync(path, cancellationToken);
                return path;
            }
        }

        throw new InvalidOperationException($"No view writes '{view}' of the form '{alias}'. The paths looked at: {string.Join(", ", candidates)}.");
    }

    // A site's own view in Development: its header is read once, and an older version than the built-in views', or
    // none, is logged.
    private async Task CheckAsync(string path, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !_checked.TryAdd(path, true))
        {
            return;
        }

        var file = environment.ContentRootFileProvider.GetFileInfo(path[1..]);
        if (!file.Exists || file.IsDirectory)
        {
            return;
        }

        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        var version = await reader.ReadLineAsync(cancellationToken) is { } first && Header().Match(first) is { Success: true } header
            ? int.Parse(header.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : (int?)null;
        if (version is null || version < FormViewModel.ContractVersion)
        {
            LogOutdated(logger, path, version?.ToString(CultureInfo.InvariantCulture) ?? "none", FormViewModel.ContractVersion);
        }
    }

    // The contract header, such as @* jfb-view: Field 1 *@ or, for a site's own type, @* jfb-view: fields/acme.mood 1 *@.
    [GeneratedRegex(@"^\s*@\*\s*jfb-view:\s*[A-Za-z0-9/._-]+\s+(?<version>[0-9]{1,6})\s*\*@", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Header();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The view {Path} declares contract version {Version}, and the built-in view is version {Current}: compare it with the built-in view and the upgrade notes, then raise its header.")]
    private static partial void LogOutdated(ILogger logger, string path, string version, int current);
}
