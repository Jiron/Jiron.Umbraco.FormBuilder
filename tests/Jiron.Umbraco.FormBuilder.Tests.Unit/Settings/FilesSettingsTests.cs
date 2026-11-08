using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The files settings (ROADMAP 12.4a, ADR-0218): the same for every site, taking every type and the host's largest file by
/// default; at least one type, each once, is saved; the host's cap lowers a larger size and is named as a clamp; and a
/// file's extension is taken when its group is, or when it belongs to no group.
/// </summary>
[TestFixture]
public sealed class FilesSettingsTests
{
    private readonly FilesSettingsValidator _validator = new();

    [Test]
    public void TheSection_IsFiles_AtSchemaVersion1_SharedByEverySite_AndTakesEveryTypeUpToTheHostsLimit()
    {
        var host = new FieldHostLimits(10_000, []);

        Assert.Multiple(() =>
        {
            Assert.That(FilesSettings.Alias, Is.EqualTo("files"));
            Assert.That(FilesSettings.SchemaVersion, Is.EqualTo(1));
            Assert.That(PerSite<FilesSettings>(), Is.False);
            Assert.That(new FilesSettings().AllowedTypes, Is.EquivalentTo(Enum.GetValues<FileTypeGroup>()));
            Assert.That(new FilesSettings().LimitWithin(host), Is.EqualTo(10_000));
            Assert.That(new FilesSettings { MaxFileBytes = 4_000 }.LimitWithin(host), Is.EqualTo(4_000));
            Assert.That(new FilesSettings { MaxFileBytes = 40_000 }.LimitWithin(host), Is.EqualTo(10_000), "the host's cap wins");
        });
    }

    [Test]
    public void AnExtension_IsTaken_WhenItsGroupIs_OrWhenItBelongsToNone()
    {
        var images = new FilesSettings { AllowedTypes = [FileTypeGroup.Images] };

        Assert.Multiple(() =>
        {
            Assert.That(images.Takes("jpg"), Is.True);
            Assert.That(images.Takes("pdf"), Is.False);
            Assert.That(images.Takes("dwg"), Is.True, "an extension of no group stays the field's to allow");
            Assert.That(FileFieldType.GroupOf("docx"), Is.EqualTo(FileTypeGroup.Office));
            Assert.That(FileFieldType.GroupOf("dwg"), Is.Null);
        });
    }

    [Test]
    public void ASave_NeedsATypeEachOnce_AndASizeOfAByteOrMore()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Errors(new FilesSettings()), Is.Empty);
            Assert.That(Errors(new FilesSettings { AllowedTypes = [] }), Is.EqualTo(new[] { "files.allowedTypes" }));
            Assert.That(Errors(new FilesSettings { AllowedTypes = [FileTypeGroup.Pdf, FileTypeGroup.Pdf] }), Is.EqualTo(new[] { "files.allowedTypes" }));
            Assert.That(Errors(new FilesSettings { AllowedTypes = [(FileTypeGroup)99] }), Is.EqualTo(new[] { "files.allowedTypes" }));
            Assert.That(Errors(new FilesSettings { MaxFileBytes = 0 }), Is.EqualTo(new[] { "files.maxFileBytes" }));
            Assert.That(Errors(new FilesSettings { MaxFileBytes = 1 }), Is.Empty);
        });
    }

    [Test]
    public void TheHostsCap_LowersALargerSize_AndIsNamedAsAClamp()
    {
        var options = new FormBuilderOptions();
        options.Files.MaxFileBytes = 5_000;
        var rule = new FilesHostLimitRule(Options.Create(options));
        var locks = new SettingsLocks();

        var lowered = rule.Apply(new FilesSettings { MaxFileBytes = 9_000 }, locks);
        var kept = rule.Apply(new FilesSettings { MaxFileBytes = 3_000 }, new SettingsLocks());

        Assert.Multiple(() =>
        {
            Assert.That(lowered.MaxFileBytes, Is.EqualTo(5_000));
            Assert.That(kept.MaxFileBytes, Is.EqualTo(3_000));
            Assert.That(locks.All["maxFileBytes"], Is.EqualTo(new SettingsLock(SettingsLockKind.Clamped, "JironFormBuilder:Files:MaxFileBytes")));
        });
    }

    private static bool PerSite<T>()
        where T : ISettingsSection => T.PerSite;

    private string[] Errors(FilesSettings section)
    {
        var errors = new SettingsErrors(FilesSettings.Alias);
        _validator.Validate(section, errors);
        return [.. errors.Errors.Select(error => error.Field)];
    }
}
