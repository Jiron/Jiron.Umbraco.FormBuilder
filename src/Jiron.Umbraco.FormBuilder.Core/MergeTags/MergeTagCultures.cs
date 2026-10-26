using System.Collections.Concurrent;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The culture formats merge tags print with, by culture name, written once per culture from .NET's data
/// (<see cref="DotNetCultureFormats"/>). A name .NET does not know gets the invariant culture's formats. The formats are
/// the same on every server, so each keeps its own copy.
/// </summary>
internal sealed class MergeTagCultures
{
    /// <summary>The most cultures kept; a site has a few, and any further one is written each time it is asked for.</summary>
    private const int Kept = 256;

    private readonly ConcurrentDictionary<string, CultureFormats> _formats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The formats of every culture from .NET's data.</summary>
    public MergeTagCultures()
    {
    }

    /// <summary>
    /// The formats given for some cultures, and .NET's for the others: the shared vectors print with cultures written as
    /// data, which pin the formatter rather than a platform's culture data (ADR-0064).
    /// </summary>
    /// <param name="given">The formats by culture name.</param>
    internal MergeTagCultures(IEnumerable<KeyValuePair<string, CultureFormats>> given)
    {
        ArgumentNullException.ThrowIfNull(given);
        foreach (var (name, formats) in given)
        {
            _formats[name] = formats;
        }
    }

    /// <summary>The formats of a culture.</summary>
    /// <param name="name">A culture name such as <c>de-CH</c>; empty for the invariant culture.</param>
    public CultureFormats For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_formats.TryGetValue(name, out var formats))
        {
            return formats;
        }

        formats = DotNetCultureFormats.From(Culture(name));
        if (_formats.Count < Kept)
        {
            _formats.TryAdd(name, formats);
        }

        return formats;
    }

    /// <summary>
    /// The culture of a name, or the invariant culture for a name .NET does not know. Only the cultures .NET has data for
    /// count: a well-formed name of none would otherwise get made-up formats.
    /// </summary>
    public static CultureInfo Culture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
