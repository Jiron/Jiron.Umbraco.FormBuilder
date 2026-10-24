using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>File ids are 128 random bits as 32 lowercase hex characters (ADR-0006), so no id ever names a path.</summary>
[TestFixture]
public sealed class FormBuilderFileIdTests
{
    [Test]
    public void New_MakesValidIds_EachOfItsOwn()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => FormBuilderFileId.New()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.All.Matches<string>(FormBuilderFileId.IsValid));
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        });
    }

    [TestCase("0123456789abcdef0123456789abcdef", true)]
    [TestCase("ffffffffffffffffffffffffffffffff", true)]
    [TestCase("0123456789ABCDEF0123456789abcdef", false, TestName = "uppercase")]
    [TestCase("0123456789abcdef0123456789abcde", false, TestName = "31 characters")]
    [TestCase("0123456789abcdef0123456789abcdef0", false, TestName = "33 characters")]
    [TestCase("0123456789abcdef0123456789abcdeg", false, TestName = "not hex")]
    [TestCase("..\\..\\..\\web.config\\0123456789ab", false, TestName = "a path")]
    [TestCase("0123456789abcdef0123456789abcd\n", false, TestName = "a line break")]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsValid_TakesOnlyLowercaseHexOfTheLength(string? id, bool valid)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderFileId.IsValid(id), Is.EqualTo(valid));
            Assert.That(() => FormBuilderFileId.ThrowIfInvalid(id), valid ? Throws.Nothing : Throws.ArgumentException.With.Property("ParamName").EqualTo("id"));
        });
    }
}
