using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The checks of the built-in name, address and file types (ADR-0056, ADR-0058, ADR-0062), each run through its type's
/// <c>Validate</c> as the pipeline runs it, at its edges: the required parts as ADR-0056 reads them, and a file's
/// count, extension and size, with every group of file types. The shared vectors hold the browser's runtime to the
/// same; these restate the edges in C#.
/// </summary>
public sealed class BuiltInPeopleAndFileValidationTests
{
    private static readonly string[] TypesOfTheStep = ["name", "address", "file"];

    // Each check at its edges: the case, and the errors in order.
    public static IEnumerable<TestCaseData> Edges()
    {
        static TestCaseData Case(string name, ValidationCase given, params string[] errors) =>
            new TestCaseData(given, errors).SetName($"Checks({given.Type}: {name})");
        static ValidationCase Parts(string type, string? settings, bool required, params (string Part, string Text)[] parts) =>
            new(type, new FieldAnswers([], parts.Select(part => KeyValuePair.Create<string, IEnumerable<string>>(part.Part, [part.Text]))))
            {
                Settings = settings,
                Required = required,
            };
        static TestCaseData File(string name, string? settings, string file, params string[] errors) =>
            Case(name, ValidationCase.Uploads(settings, (file, 1024)), errors);

        (string, string)[] ada = [("first", "Ada"), ("last", "Lovelace")];
        yield return Case("required with every part", Parts("name", null, true, ada));
        yield return Case("required without its last name", Parts("name", null, true, ("first", "Ada")), "required");
        yield return Case("required without its first name", Parts("name", null, true, ("last", "Lovelace")), "required");
        yield return Case("optional with one part", Parts("name", null, false, ("first", "Ada")));
        yield return Case("required, asking for a title", Parts("name", """{"parts":["title","first","last"]}""", true, ada), "required");
        yield return Case("its required parts", Parts("name", """{"requiredParts":["last"]}""", false, ("last", "Lovelace")));
        yield return Case("without a required part once given", Parts("name", """{"requiredParts":["last"]}""", false, ("first", "Ada")), "required");
        yield return Case("required, needing only its required parts", Parts("name", """{"requiredParts":["last"]}""", true, ("last", "Lovelace")));
        yield return Case("a required part it does not ask for", Parts("name", """{"requiredParts":["middle","last"]}""", false, ada));
        yield return Case("a part it does not ask for is left", Parts("name", null, true, ("first", "Ada"), ("last", "Lovelace"), ("middle", "King")));

        (string, string)[] zurich = [("street", "Bahnhofstrasse 1"), ("postcode", "8001"), ("city", "Zurich")];
        yield return Case("required with the three", Parts("address", null, true, zurich));
        yield return Case("required without its street", Parts("address", null, true, ("postcode", "8001"), ("city", "Zurich")), "required");
        yield return Case("required without its postcode", Parts("address", null, true, ("street", "Bahnhofstrasse 1"), ("city", "Zurich")), "required");
        yield return Case("required without its city", Parts("address", null, true, ("street", "Bahnhofstrasse 1"), ("postcode", "8001")), "required");
        yield return Case("required without a region it asks for", Parts("address", """{"parts":["region"]}""", true, zurich));
        yield return Case("optional with one part", Parts("address", null, false, ("city", "Bern")));
        yield return Case("its required parts", Parts("address", """{"parts":["country"],"requiredParts":["country"]}""", false, ("country", "CH")));
        yield return Case("without a required part once given", Parts("address", """{"requiredParts":["postcode","city"]}""", false, ("street", "Bahnhofstrasse 1"), ("city", "Zurich")), "required");
        yield return Case("a required part it does not ask for", Parts("address", """{"requiredParts":["region"]}""", false, zurich));

        yield return File("an image by default", null, "photo.jpg");
        yield return File("a PDF by default", null, "report.pdf");
        yield return File("a document by default", null, "report.docx", "fileType");
        yield return File("an extension in capitals", null, "IMG_0001.HEIC");
        yield return File("the last point decides", null, "photo.jpg.exe", "fileType");
        yield return File("a name without a point", null, "jpg", "fileType");
        yield return File("a name ending with a point", null, "photo.", "fileType");
        yield return File("SVG is no image", null, "logo.svg", "fileType");
        yield return File("SVG as its own extension", """{"extensions":["svg"]}""", "logo.svg");
        yield return File("its own extension", """{"extensions":["dwg"]}""", "plan.dwg");
        yield return File("only its own extensions", """{"extensions":["dwg"]}""", "photo.jpg", "fileType");
        yield return File("a type and an extension", """{"types":["pdf"],"extensions":["dwg"]}""", "report.pdf");
        yield return File("office documents are no PDF", """{"types":["office"]}""", "report.pdf", "fileType");
        yield return File("plain text is no image", """{"types":["text"]}""", "photo.png", "fileType");
        foreach (var group in Enum.GetValues<FileTypeGroup>())
        {
            var settings = $$"""{"types":["{{group.ToString().ToLowerInvariant()}}"]}""";
            foreach (var extension in FileFieldType.ExtensionsOf(group))
            {
                yield return File($"{group}: {extension}", settings, "file." + extension);
            }
        }

        yield return Case("at its most files", ValidationCase.Uploads("""{"maxFiles":2}""", ("a.png", 1), ("b.png", 1)));
        yield return Case("past its most files", ValidationCase.Uploads("""{"maxFiles":2}""", ("a.png", 1), ("b.png", 1), ("c.png", 1)), "fileCount");
        yield return Case("past one file by default", ValidationCase.Uploads(null, ("a.png", 1), ("b.png", 1)), "fileCount");
        yield return Case("at the most files any field takes", ValidationCase.Uploads("""{"maxFiles":20}""", [.. Enumerable.Range(1, 20).Select(i => ($"{i}.png", 1L))]));
        yield return Case("at its most bytes", ValidationCase.Uploads("""{"maxBytes":1000}""", ("a.png", 1000)));
        yield return Case("past its most bytes", ValidationCase.Uploads("""{"maxBytes":1000}""", ("a.png", 1001)), "fileSize");
        yield return Case("one of two too big", ValidationCase.Uploads("""{"maxFiles":2,"maxBytes":1000}""", ("a.png", 10), ("b.png", 1001)), "fileSize");
        yield return Case("any size without a limit", ValidationCase.Uploads(null, ("film.png", 5_368_709_120)));
        yield return Case("an empty file", ValidationCase.Uploads(null, ("empty.png", 0)));
        yield return Case("every check fails, in order", ValidationCase.Uploads("""{"maxBytes":1000}""", ("a.png", 10), ("b.exe", 1001)), "fileCount", "fileType", "fileSize");
    }

    [TestCaseSource(nameof(Edges))]
    public void APeopleOrFileType_ChecksItsSettings_AtTheirEdges(ValidationCase given, string[] errors) =>
        Assert.That(given.Errors(), Is.EqualTo(errors));

    [Test]
    public void TheEdges_CoverEveryTypeOfTheStep() =>
        Assert.That(Edges().Select(data => ((ValidationCase)data.Arguments[0]!).Type).Distinct(), Is.EquivalentTo(TypesOfTheStep));

    [Test]
    public void TheGroupsOfFileTypes_TakeTheExtensionsOfTheDecision()
    {
        // ADR-0062's table, restated: SVG is no image, because it can carry script.
        Assert.Multiple(() =>
        {
            Assert.That(FileFieldType.ExtensionsOf(FileTypeGroup.Images), Is.EqualTo(new[] { "jpg", "jpeg", "png", "gif", "webp", "avif", "heic", "heif" }));
            Assert.That(FileFieldType.ExtensionsOf(FileTypeGroup.Pdf), Is.EqualTo(new[] { "pdf" }));
            Assert.That(FileFieldType.ExtensionsOf(FileTypeGroup.Office), Is.EqualTo(new[] { "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "rtf" }));
            Assert.That(FileFieldType.ExtensionsOf(FileTypeGroup.Text), Is.EqualTo(new[] { "txt", "csv" }));
            Assert.That(FileFieldType.ExtensionsOf(FileTypeGroup.Archives), Is.EqualTo(new[] { "zip", "7z", "rar", "tar", "gz" }));
        });
    }

    [TestCase("plan.dwg", "dwg")]
    [TestCase("PLAN.DWG", "dwg")]
    [TestCase("archive.tar.gz", "gz")]
    [TestCase(".png", "png")]
    [TestCase("photo.", "")]
    [TestCase("README", "")]
    public void AFileNamesExtension_IsTheTextAfterItsLastPoint_InAsciiLowercase(string name, string extension) =>
        Assert.That(FileFieldType.ExtensionOf(name), Is.EqualTo(extension));

    [Test]
    public void AFileNamesExtension_LowercasesOnlyAsciiLetters() =>
        Assert.That(FileFieldType.ExtensionOf("file." + (char)0x130 + "MG"), Is.EqualTo((char)0x130 + "mg"), "a capital I with a dot stays as it is");
}
