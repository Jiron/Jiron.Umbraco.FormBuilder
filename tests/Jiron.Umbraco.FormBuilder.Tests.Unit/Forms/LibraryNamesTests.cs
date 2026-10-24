using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>The one rule for the names of forms and folders (ADR-0044, ADR-0047).</summary>
[TestFixture]
public sealed class LibraryNamesTests
{
    [TestCase("Contact", "Contact")]
    [TestCase("  Contact us  ", "Contact us")]
    [TestCase("Anmeldung: Herbstfest", "Anmeldung: Herbstfest")]
    public void AName_IsTrimmed(string name, string expected) =>
        Assert.That(LibraryNames.Check(name, "folder", 255), Is.EqualTo(expected));

    [Test]
    public void ANameAtTheLongestTheColumnHolds_IsAllowed() =>
        Assert.That(LibraryNames.Check(" " + new string('a', 255) + " ", "form", 255), Has.Length.EqualTo(255));

    [Test]
    public void ANameThatIsNotAllowed_IsRefusedAtItsPath_NamingWhatHasIt()
    {
        var loneSurrogate = "a" + (char)0xD800 + "b";

        Assert.Multiple(() =>
        {
            AssertRefused(() => LibraryNames.Check(" \t ", "folder", 255), DefinitionErrors.InvalidValue, "name", "A folder needs a name that is not only white space.");
            AssertRefused(() => LibraryNames.Check(new string('a', 256), "form", 255), DefinitionErrors.LimitExceeded, "name", "A form name has at most 255 characters.");
            AssertRefused(() => LibraryNames.Check("Two\nlines", "folder", 255), DefinitionErrors.InvalidValue, "name", "A folder name is one line of text, without control characters.");
            AssertRefused(() => LibraryNames.Check(loneSurrogate, "form", 255, "$.name"), DefinitionErrors.InvalidValue, "$.name", "A form name is one line of text, without control characters.");
            Assert.That(() => LibraryNames.Check(null!, "form", 255), Throws.ArgumentNullException);
        });
    }

    private static void AssertRefused(TestDelegate check, string code, string path, string message)
    {
        var refusal = Assert.Throws<FormBuilderValidationException>(check);
        Assert.That(refusal?.Problems.Select(problem => (problem.Code, problem.Path, problem.Message)), Is.EqualTo(new[] { (code, path, message) }));
    }
}
