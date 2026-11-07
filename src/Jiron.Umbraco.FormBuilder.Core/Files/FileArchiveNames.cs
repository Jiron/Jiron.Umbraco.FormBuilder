using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The names of the files in a ZIP (ADR-0145): each entry's files in a folder of their own, under a name no unpacking program can
/// be led out of that folder by, on any system. A stored name is cleaned when it comes, and this does not count on that: a row may
/// be older than a rule, or come from an import. One instance holds the names of one archive, so two files of a folder never meet.
/// </summary>
internal sealed class FileArchiveNames
{
    private const string Fallback = "file";
    private const char Replacement = '_';

    private static readonly FrozenSet<string> DeviceNames = new[]
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The next file's path in the archive, as a ZIP writes one: <c>entry-12/report.pdf</c>, with a number on a name its folder holds already.</summary>
    /// <param name="entryId">The entry that holds the file.</param>
    /// <param name="name">The file's stored name.</param>
    public string Next(long entryId, string name) => NextIn($"entry-{entryId}", name);

    /// <summary>The next file's path in a folder of the archive, such as <c>entry-42/report.pdf</c> for an export's entry number 42 (ADR-0210).</summary>
    /// <param name="folder">The folder, one plain segment the caller makes.</param>
    /// <param name="name">The file's stored name.</param>
    public string NextIn(string folder, string name)
    {
        var safe = Safe(name);
        var point = safe.LastIndexOf('.');
        var (stem, extension) = point < 0 ? (safe, string.Empty) : (safe[..point], safe[point..]);
        var path = $"{folder}/{safe}";
        for (var number = 2; !_taken.Add(path); number++)
        {
            path = $"{folder}/{stem} ({number}){extension}";
        }

        return path;
    }

    /// <summary>A name that is one plain segment on every system.</summary>
    /// <param name="name">The stored name.</param>
    public static string Safe(string name)
    {
        var cleaned = FileUploads.CleanName(name) ?? Fallback;
        var letters = cleaned.Select(letter => letter is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '/' ? Replacement : letter).ToArray();
        var plain = new string(letters).TrimEnd('.', ' ').TrimStart('.', ' ');
        if (plain.Length == 0)
        {
            return Fallback;
        }

        var point = plain.IndexOf('.');
        return DeviceNames.Contains(point < 0 ? plain : plain[..point]) ? Replacement + plain : plain;
    }
}
