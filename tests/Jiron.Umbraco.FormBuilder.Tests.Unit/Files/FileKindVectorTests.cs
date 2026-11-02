using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The length both checks read of a file (ROADMAP 6.4m, ADR-0152), beside the kinds' shared vectors, which
/// <see cref="Logic.VectorTests"/> runs.
/// </summary>
public sealed class FileKindVectorTests
{
    [Test]
    public void TheHead_IsAsLongAsTheBrowserReads()
    {
        Assert.That(FieldUploadCheck.HeadLength, Is.EqualTo(512), "the website runtime's HEAD_LENGTH");
    }
}
