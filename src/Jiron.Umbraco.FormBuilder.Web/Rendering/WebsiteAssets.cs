using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The website assets embedded in the Web assembly (ADR-0026, ADR-0092): each original under its name below
/// <c>/jfb/assets/</c>, with its Brotli and gzip copies, its media type and its fingerprint. Read once, at first use.
/// </summary>
internal sealed class WebsiteAssets
{
    /// <summary>The path the assets are served under, below the site's path base.</summary>
    public const string BasePath = "/jfb/assets";

    /// <summary>The runtime's entry module.</summary>
    public const string Runtime = "runtime/jfb-runtime.js";

    /// <summary>The preview script, which only the preview's document loads (ADR-0203).</summary>
    public const string Preview = "preview/jfb-preview.js";

    /// <summary>The functional base rules every form loads, whatever its theme (ADR-0105).</summary>
    public const string BaseRules = "themes/base.css";

    /// <summary>The prefix of the assets' resource names (the Web project's <c>JfbEmbedWebsiteAssets</c> target).</summary>
    internal const string ResourcePrefix = "jfb/assets/";

    // The copies the build writes next to each original (scripts/compress.mjs), with the coding each is sent as.
    private const string BrotliExtension = ".br";
    private const string GzipExtension = ".gz";

    // The length of a fingerprint, in hexadecimal digits of the original's SHA-256.
    private const int FingerprintLength = 12;

    private readonly FrozenDictionary<string, WebsiteAsset> _assets;

    /// <summary>The assets this assembly holds.</summary>
    public WebsiteAssets()
        : this(Embedded(typeof(WebsiteAssets).Assembly))
    {
    }

    /// <summary>The assets among these files, each named as below <c>jfb/assets/</c>: the originals with their copies.</summary>
    /// <param name="files">The files, originals and copies, by name.</param>
    internal WebsiteAssets(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _assets = files
            .Where(file => !IsCopy(file.Key, files))
            .ToFrozenDictionary(
                file => file.Key,
                file => Asset(file.Key, file.Value, files.GetValueOrDefault(file.Key + BrotliExtension), files.GetValueOrDefault(file.Key + GzipExtension)),
                StringComparer.Ordinal);
    }

    /// <summary>Every asset, in no particular order.</summary>
    public IReadOnlyCollection<WebsiteAsset> All => _assets.Values;

    /// <summary>The asset of exactly this name (below <c>jfb/assets/</c>), or null: copies and other spellings have none.</summary>
    /// <param name="name">The name, as the request's path gives it.</param>
    public WebsiteAsset? Find(string name) => _assets.GetValueOrDefault(name);

    /// <summary>The address of an asset below the site's path base, with its fingerprint (<c>?v=</c>).</summary>
    /// <param name="pathBase">The site's path base.</param>
    /// <param name="name">The asset's name, which the assembly holds.</param>
    /// <exception cref="InvalidOperationException">The assembly holds no asset of that name: the client was not built.</exception>
    public string Url(PathString pathBase, string name)
    {
        var asset = Find(name) ?? throw new InvalidOperationException($"The website asset '{name}' is missing from the Web assembly: build the client (ADR-0026).");
        return $"{pathBase.Add(BasePath + "/" + name)}?v={asset.Fingerprint}";
    }

    /// <summary>
    /// The copy of the asset a request with this <c>Accept-Encoding</c> gets: Brotli when it accepts it and the build
    /// made one, else gzip, else the original. A coding is accepted when it, or <c>*</c>, has a quality above zero.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="accepted">The request's <c>Accept-Encoding</c>.</param>
    public static WebsiteAssetCopy CopyFor(WebsiteAsset asset, IList<StringWithQualityHeaderValue> accepted)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(accepted);
        if (asset.Brotli is { } brotli && Accepts(accepted, "br"))
        {
            return new WebsiteAssetCopy("br", brotli, $"\"{asset.Fingerprint}-br\"");
        }

        if (asset.Gzip is { } gzip && Accepts(accepted, "gzip"))
        {
            return new WebsiteAssetCopy("gzip", gzip, $"\"{asset.Fingerprint}-gzip\"");
        }

        return new WebsiteAssetCopy(null, asset.Original, $"\"{asset.Fingerprint}\"");
    }

    /// <summary>Whether <c>Accept-Encoding</c> names the coding, or else <c>*</c>, with a quality above zero.</summary>
    internal static bool Accepts(IList<StringWithQualityHeaderValue> accepted, string coding)
    {
        var named = accepted.FirstOrDefault(value => string.Equals(value.Value.Value, coding, StringComparison.OrdinalIgnoreCase));
        var chosen = named ?? accepted.FirstOrDefault(value => value.Value.Value == "*");
        return chosen is not null && (chosen.Quality ?? 1) > 0;
    }

    // The media type of an asset, by its extension: text says it is UTF-8.
    internal static string ContentTypeOf(string name) => Path.GetExtension(name) switch
    {
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };

    // A copy is a file whose name is another file's with a copy's extension.
    private static bool IsCopy(string name, IReadOnlyDictionary<string, byte[]> files) =>
        (name.EndsWith(BrotliExtension, StringComparison.Ordinal) || name.EndsWith(GzipExtension, StringComparison.Ordinal))
        && files.ContainsKey(name[..name.LastIndexOf('.')]);

    private static WebsiteAsset Asset(string name, byte[] original, byte[]? brotli, byte[]? gzip) => new(
        name,
        ContentTypeOf(name),
        Convert.ToHexStringLower(SHA256.HashData(original))[..FingerprintLength],
        original,
        brotli,
        gzip,
        NamedByContent(name));

    // The build names chunks and the files they import by a hash of their content (scripts/vite-shared.mjs), so such a
    // name is its own fingerprint.
    private static bool NamedByContent(string name)
    {
        var folders = name.Split('/')[..^1];
        return folders.Contains("chunks", StringComparer.Ordinal) || folders.Contains("assets", StringComparer.Ordinal);
    }

    // The resources of an assembly below the prefix, by their names after it.
    internal static Dictionary<string, byte[]> Embedded(Assembly assembly)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            files[resource[ResourcePrefix.Length..]] = copy.ToArray();
        }

        return files;
    }
}

/// <summary>A website asset (ADR-0092).</summary>
/// <param name="Name">Its name below <c>jfb/assets/</c>.</param>
/// <param name="ContentType">Its media type.</param>
/// <param name="Fingerprint">The first 12 hexadecimal digits of its original's SHA-256.</param>
/// <param name="Original">The original's bytes.</param>
/// <param name="Brotli">The Brotli copy's bytes, or null when the build made none.</param>
/// <param name="Gzip">The gzip copy's bytes, or null when the build made none.</param>
/// <param name="NamedByContent">Whether its name carries a hash of its content, so it is cached without a fingerprint.</param>
internal sealed record WebsiteAsset(string Name, string ContentType, string Fingerprint, byte[] Original, byte[]? Brotli, byte[]? Gzip, bool NamedByContent);

/// <summary>The copy of a website asset a request gets (ADR-0092).</summary>
/// <param name="Coding">The content coding (<c>br</c>, <c>gzip</c>), or null for the original.</param>
/// <param name="Bytes">Its bytes.</param>
/// <param name="ETag">Its entity tag, quoted.</param>
internal sealed record WebsiteAssetCopy(string? Coding, byte[] Bytes, string ETag);
