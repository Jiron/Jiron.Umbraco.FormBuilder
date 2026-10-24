using System.Diagnostics.CodeAnalysis;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The folder of the physical file store (ADR-0006, ADR-0041): <c>~/</c> and a path relative to the content root, or an
/// absolute path; never inside the web root, where anyone could download the files, and never around it, whether the
/// paths are compared as written or with their links followed.
/// </summary>
internal static class FileStoreRoot
{
    /// <summary>The most links one path may pass through.</summary>
    public const int MaxLinks = 32;

    private const string Setting = "JironFormBuilder:Files:Root";
    private const string ContentRootPrefix = "~/";

    /// <summary>The folder <paramref name="root"/> names on this site.</summary>
    /// <exception cref="InvalidOperationException">The folder may not hold the files; the message says why.</exception>
    public static string Resolve(string? root, IHostFolders folders) =>
        TryResolve(root, folders, out var path, out var problem) ? path : throw new InvalidOperationException(problem);

    /// <summary>The full path of the folder <paramref name="root"/> names on this site, or why it may not hold the files.</summary>
    public static bool TryResolve(string? root, IHostFolders folders, [NotNullWhen(true)] out string? path, [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return TryResolve(root, folders.ContentRoot, folders.WebRoot, out path, out problem);
    }

    /// <summary>
    /// The full path of the folder <paramref name="root"/> names, given the site's content root and web root, or why it may
    /// not hold the files. Paths are compared ignoring case, which refuses rather than allows on a file system that
    /// tells case apart.
    /// </summary>
    public static bool TryResolve(string? root, string contentRoot, string webRoot, [NotNullWhen(true)] out string? path, [NotNullWhen(false)] out string? problem)
    {
        path = Full(root, contentRoot);
        if (path is null)
        {
            problem = $"{Setting} must be {ContentRootPrefix} followed by a path relative to the site's content root, or an absolute path of a folder; it is '{root}'.";
            return false;
        }

        var web = Path.GetFullPath(webRoot);
        var realPath = Real(path, 0);
        var realWeb = Real(web, 0);
        if (realPath is null || realWeb is null)
        {
            problem = $"{Setting} or the web root ({web}) passes through a link that cannot be followed, so the two cannot be compared: choose a folder without one.";
            path = null;
            return false;
        }

        if (Within(path, web) || Within(realPath, realWeb))
        {
            problem = $"{Setting} lies inside the web root ({web}), where anyone could download the files: choose a folder outside it, such as the default {FormBuilderFilesOptions.DefaultRoot}.";
            path = null;
            return false;
        }

        if (Within(web, path) || Within(realWeb, realPath))
        {
            problem = $"{Setting} contains the web root ({web}): choose a folder of its own outside it, such as the default {FormBuilderFilesOptions.DefaultRoot}.";
            path = null;
            return false;
        }

        problem = null;
        return true;
    }

    private static string? Full(string? root, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(root) || IsDevicePath(root))
        {
            return null;
        }

        try
        {
            string? full;
            if (root.StartsWith(ContentRootPrefix, StringComparison.Ordinal))
            {
                // A rooted rest would leave the content root behind: Path.Combine keeps only the rooted part.
                var rest = root[ContentRootPrefix.Length..];
                full = rest.Length == 0 || Path.IsPathRooted(rest) ? null : Path.GetFullPath(Path.Combine(contentRoot, rest));
            }
            else
            {
                full = Path.IsPathFullyQualified(root) ? Path.GetFullPath(root) : null;
            }

            // On Windows a colon after the drive names a stream of a file, never a folder.
            return full is not null && OperatingSystem.IsWindows() && full.IndexOf(':', 2) >= 0 ? null : full;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // \\?\, \\.\ and \??\, also written with slashes, name a device path on Windows, which skips the normalising that the
    // comparisons rely on.
    private static bool IsDevicePath(string root) =>
        OperatingSystem.IsWindows()
        && root.Length >= 4
        && IsSeparator(root[0])
        && (IsSeparator(root[1]) || root[1] == '?')
        && (root[2] is '.' or '?')
        && IsSeparator(root[3]);

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    // The full path with every link among its existing parts followed: null after more than MaxLinks links, which a
    // loop reaches, or when a link cannot be read.
    private static string? Real(string path, int links)
    {
        var root = Path.GetPathRoot(path)!;
        var parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            string? target;
            try
            {
                target = new DirectoryInfo(current).LinkTarget;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (target is not null)
            {
                // A relative target is relative to the folder that holds the link.
                return links == MaxLinks
                    ? null
                    : Real(Path.GetFullPath(Path.Combine([Path.GetDirectoryName(current)!, target, .. parts[(i + 1)..]])), links + 1);
            }
        }

        return current;
    }

    // Whether path is folder itself or a folder below it.
    private static bool Within(string path, string folder) =>
        WithSeparator(path).StartsWith(WithSeparator(folder), StringComparison.OrdinalIgnoreCase);

    private static string WithSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}

