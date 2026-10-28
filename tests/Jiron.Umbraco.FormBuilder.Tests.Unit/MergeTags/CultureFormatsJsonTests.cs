using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// A culture's formats as the form endpoint writes them for the browser (ADR-0064, ADR-0085): in the shape the vectors
/// give both runtimes, so the browser prints with exactly the formats the server prints with.
/// </summary>
public sealed class CultureFormatsJsonTests
{
    private static readonly string VectorsPath = RepositoryRoot.Path(typeof(CultureFormatsJsonTests).Assembly, "tests", "vectors", "merge-tags.json");

    [Test]
    public void EveryCultureOfTheVectors_IsWrittenAsTheVectorsWriteIt()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(VectorsPath));
        var cultures = document.RootElement.GetProperty("defaults").GetProperty("cultures").EnumerateObject().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(cultures, Is.Not.Empty);
            foreach (var culture in cultures)
            {
                var written = Written(VectorReader.Formats(culture.Value));
                Assert.That(JsonNode.DeepEquals(written, JsonNode.Parse(culture.Value.GetRawText())), Is.True, $"{culture.Name}: {written.ToJsonString()}");
            }
        });
    }

    [TestCase("en-US")]
    [TestCase("de-CH")]
    [TestCase("ar-EG")]
    public void TheFormatsFromDotNet_ReadBackAsTheyWereWritten(string culture)
    {
        var written = Written(new MergeTagCultures().For(culture));

        using var read = JsonDocument.Parse(written.ToJsonString());
        Assert.That(JsonNode.DeepEquals(Written(VectorReader.Formats(read.RootElement)), written), Is.True);
    }

    private static JsonNode Written(CultureFormats formats)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            MergeTagCultures.Write(writer, formats);
        }

        return JsonNode.Parse(buffer.WrittenSpan)!;
    }
}
