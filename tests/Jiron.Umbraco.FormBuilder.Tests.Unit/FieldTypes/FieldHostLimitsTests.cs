using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The host's limits for files as a field type's settings check sees them (ADR-0087): the defaults of a site that sets
/// none, a site's own list of blocked extensions in place of the defaults, and limits a host cannot have.
/// </summary>
public sealed class FieldHostLimitsTests
{
    [Test]
    public void ASiteThatSetsNothing_HasTheDefaults()
    {
        var limits = FieldHostLimits.Of(new FormBuilderFilesOptions());

        Assert.Multiple(() =>
        {
            Assert.That(limits.MaxFileBytes, Is.EqualTo(52_428_800));
            Assert.That(limits.BlockedExtensions, Is.EquivalentTo(FormBuilderFilesOptions.DefaultBlockedExtensions));
            Assert.That(FieldHostLimits.Default.MaxFileBytes, Is.EqualTo(52_428_800));
            Assert.That(FieldHostLimits.Default.BlockedExtensions, Is.EquivalentTo(FormBuilderFilesOptions.DefaultBlockedExtensions));
        });
    }

    [Test]
    public void ASitesOwnBlockedExtensions_ReplaceTheDefaults()
    {
        var files = new FormBuilderFilesOptions { MaxFileBytes = 1000 };
        files.BlockedExtensions.Add("dwg");

        var limits = FieldHostLimits.Of(files);

        Assert.Multiple(() =>
        {
            Assert.That(limits.MaxFileBytes, Is.EqualTo(1000));
            Assert.That(limits.BlockedExtensions, Is.EquivalentTo(new[] { "dwg" }));
            Assert.That(limits.BlockedExtensions.Contains("DWG"), Is.False, "extensions are compared as written, in lowercase");
        });
    }

    [Test]
    public void ALimitBelowOneByte_IsNoHostsLimit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FieldHostLimits(0, []), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new FieldHostLimits(1, null!), Throws.ArgumentNullException);
            Assert.That(new FieldHostLimits(1, ["exe", "exe"]).BlockedExtensions, Has.Count.EqualTo(1));
        });
    }
}
