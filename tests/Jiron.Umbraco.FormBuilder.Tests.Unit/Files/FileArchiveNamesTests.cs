using System.IO.Compression;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The names in a ZIP (ROADMAP 7.3c, ADR-0145): each stays one plain segment in its entry's folder, whatever was stored, so an
/// unpacking program cannot be led out of the folder it unpacks to.
/// </summary>
public sealed class FileArchiveNamesTests
{
    private const char Backslash = (char)92;

    public static IEnumerable<TestCaseData> Names()
    {
        static TestCaseData Case(string name, string stored, string safe) => new TestCaseData(stored, safe).SetName($"AStoredName_IsOnePlainSegment({name})");

        yield return Case("a plain name", "report.pdf", "report.pdf");
        yield return Case("a zip slip", "../../evil.exe", "evil.exe");
        yield return Case("a zip slip with backslashes", $"..{Backslash}..{Backslash}evil.exe", "evil.exe");
        yield return Case("a slip in the middle", "a/../../b.pdf", "b.pdf");
        yield return Case("an absolute path", "/etc/passwd", "passwd");
        yield return Case("a Windows path", $"C:{Backslash}Windows{Backslash}evil.dll", "evil.dll");
        yield return Case("a drive without a separator", "C:evil.dll", "C_evil.dll");
        yield return Case("a UNC path", $"{Backslash}{Backslash}server{Backslash}share{Backslash}evil.exe", "evil.exe");
        yield return Case("an NTFS stream", "report.pdf:evil.exe", "report.pdf_evil.exe");
        yield return Case("only points", "..", "file");
        yield return Case("nothing", string.Empty, "file");
        yield return Case("points with a space between", ". .", "file");
        yield return Case("a path that ends in a separator", "../", "file");
        yield return Case("only marks", "***", "___");
        yield return Case("marks a system refuses", "a<b>c\"d|e?f*g.pdf", "a_b_c_d_e_f_g.pdf");
        yield return Case("trailing points", "evil.exe...", "evil.exe");
        yield return Case("leading points", "...hidden.pdf", "hidden.pdf");
        yield return Case("a dot file", ".htaccess", "htaccess");
        yield return Case("a device", "CON", "_CON");
        yield return Case("a device in small letters", "nul", "_nul");
        yield return Case("a device with an extension", "aux.txt", "_aux.txt");
        yield return Case("a numbered device", "COM1.pdf", "_COM1.pdf");
        yield return Case("a printer port", "lpt9", "_lpt9");
        yield return Case("a name that only starts as a device", "console.pdf", "console.pdf");
        yield return Case("a device past the numbers", "com0.pdf", "com0.pdf");
        yield return Case("control characters", $"re{(char)0}port.pdf", "report.pdf");
        yield return Case("a mark that reorders a name", $"photo{(char)0x202E}gpj.exe", "photogpj.exe");
    }

    [TestCaseSource(nameof(Names))]
    public void AStoredName_IsOnePlainSegment(string stored, string safe) => Assert.That(FileArchiveNames.Safe(stored), Is.EqualTo(safe));

    [Test]
    public void EachEntry_HasItsOwnFolder_AndANameItHolds_GetsANumber()
    {
        var names = new FileArchiveNames();

        Assert.Multiple(() =>
        {
            Assert.That(names.Next(12, "report.pdf"), Is.EqualTo("entry-12/report.pdf"));
            Assert.That(names.Next(13, "report.pdf"), Is.EqualTo("entry-13/report.pdf"), "another folder");
            Assert.That(names.Next(12, "report.pdf"), Is.EqualTo("entry-12/report (2).pdf"));
            Assert.That(names.Next(12, "REPORT.PDF"), Is.EqualTo("entry-12/REPORT (3).PDF"), "the same name on a system without case");
            Assert.That(names.Next(12, "notes"), Is.EqualTo("entry-12/notes"));
            Assert.That(names.Next(12, "notes"), Is.EqualTo("entry-12/notes (2)"), "without an extension");
            Assert.That(names.Next(12, "../notes"), Is.EqualTo("entry-12/notes (3)"), "what comes out the same");
        });
    }

    [Test]
    public async Task AnArchiveOfTheWorstNames_UnpacksInsideItsFolder()
    {
        string[] stored = ["../../evil.exe", $"..{Backslash}..{Backslash}evil.exe", "/etc/passwd", $"C:{Backslash}Windows{Backslash}evil.dll", "C:evil.dll", "CON", "..", "a/../../b.pdf"];
        var names = new FileArchiveNames();
        using var buffer = new MemoryStream();
        await using (var archive = await ZipArchive.CreateAsync(buffer, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null))
        {
            foreach (var name in stored)
            {
                var entry = archive.CreateEntry(names.Next(1, name));
                await using var into = await entry.OpenAsync();
                await into.WriteAsync("bytes"u8.ToArray());
            }
        }

        var target = Path.Combine(Path.GetTempPath(), "jfb-zip-slip-" + Guid.NewGuid().ToString("N"));
        try
        {
            buffer.Position = 0;
            await ZipFile.ExtractToDirectoryAsync(buffer, target);

            var unpacked = Directory.GetFiles(target, "*", SearchOption.AllDirectories);
            Assert.Multiple(() =>
            {
                Assert.That(unpacked, Has.Length.EqualTo(stored.Length), "each file, and each inside");
                Assert.That(unpacked.Select(path => Path.GetDirectoryName(path)), Is.All.EqualTo(Path.Combine(target, "entry-1")));
            });
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
