using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Picker;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Picker;

/// <summary>
/// The form picker's settings (ADR-0129): which forms a picker allows (any when it limits nothing, a listed form, a form
/// in or below a listed folder), its keys read leniently, and its theme and page after a send read as set or none.
/// </summary>
public sealed class FormPickerConfigurationTests
{
    private static readonly Guid Form = Guid.Parse("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084");

    private static readonly Guid Folder = Guid.Parse("2b9e4c10-7d3a-4f5e-8a61-0c4d2e9b7f13");

    private static readonly Guid Above = Guid.Parse("9a3f6e21-5c8b-4d07-b1e4-7f2a0d6c3b58");

    [Test]
    public void APickerThatLimitsNothing_AllowsEveryForm() =>
        Assert.That(new FormPickerConfiguration().Allows(Form, []), Is.True);

    [Test]
    public void APickerWithFolders_AllowsTheFormsInOrBelowThem_AndNoOther()
    {
        var settings = new FormPickerConfiguration { AllowedFolders = [Above.ToString("D")] };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Allows(Form, [Above]), Is.True, "in the folder");
            Assert.That(settings.Allows(Form, [Folder, Above]), Is.True, "below it");
            Assert.That(settings.Allows(Form, [Folder]), Is.False, "in another folder");
            Assert.That(settings.Allows(Form, []), Is.False, "at the root");
        });
    }

    [Test]
    public void APickerWithForms_AllowsThem_AndThoseInItsFolders()
    {
        var settings = new FormPickerConfiguration { AllowedForms = [Form.ToString("N")], AllowedFolders = [Folder.ToString("D")] };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Allows(Form, []), Is.True, "a listed form, at the root");
            Assert.That(settings.Allows(Guid.NewGuid(), [Folder]), Is.True, "a form in a listed folder");
            Assert.That(settings.Allows(Guid.NewGuid(), [Above]), Is.False);
        });
    }

    [Test]
    public void TheKeyFormatsTheBackofficeReads_AreThoseTheServerReads()
    {
        // tests/fixtures/picker/key-formats.json holds both sides to one list; picked-keys.test.ts reads it too.
        var path = RepositoryRoot.Path(typeof(FormPickerConfigurationTests).Assembly, "tests", "fixtures", "picker", "key-formats.json");
        using var formats = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Multiple(() =>
        {
            Assert.That(formats.RootElement.GetArrayLength(), Is.GreaterThan(30));
            foreach (var format in formats.RootElement.EnumerateArray())
            {
                var text = format.GetProperty("text").GetString();
                var read = Guid.TryParse(text, out var key) ? key.ToString("D") : null;
                Assert.That(read, Is.EqualTo(format.GetProperty("key").GetString()), text);
            }
        });
    }

    [Test]
    public void ItsValues_AreReadLeniently()
    {
        var settings = new FormPickerConfiguration
        {
            AllowedForms = ["not a key", string.Empty, Guid.Empty.ToString("D"), Form.ToString("D")],
            Theme = "  ",
            Redirect = "not a key",
        };
        var set = new FormPickerConfiguration { Theme = " bootstrap5 ", Redirect = Above.ToString("D") };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Forms, Is.EquivalentTo(new[] { Form }));
            Assert.That(settings.Folders, Is.Empty);
            Assert.That((settings.ThemeAlias, settings.RedirectKey), Is.EqualTo(((string?)null, (Guid?)null)));
            Assert.That((set.ThemeAlias, set.RedirectKey), Is.EqualTo(("bootstrap5", (Guid?)Above)));
        });
    }
}
