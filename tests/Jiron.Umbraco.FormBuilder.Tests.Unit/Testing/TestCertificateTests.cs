using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Testing.Tls;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The certificate of a test host with HTTPS (ROADMAP 7.3d): for this machine, for a day, and on Windows under the one key it was told to use.</summary>
public sealed class TestCertificateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _keyName = "jfb-test-" + Guid.NewGuid().ToString("N");

    [TearDown]
    public void RemoveTheKey()
    {
        if (OperatingSystem.IsWindows() && CngKey.Exists(_keyName))
        {
            using var key = CngKey.Open(_keyName);
            key.Delete();
        }
    }

    [Test]
    public void ItServesThisMachine_ForADay()
    {
        using var certificate = TestCertificate.Create(_keyName, new TestTimeProvider(Now));

        var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        var usages = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages;
        Assert.Multiple(() =>
        {
            Assert.That(certificate.HasPrivateKey, Is.True);
            Assert.That(names.EnumerateIPAddresses().Select(address => address.ToString()), Is.EqualTo(new[] { "127.0.0.1" }));
            Assert.That(names.EnumerateDnsNames(), Is.EqualTo(new[] { "localhost" }));
            Assert.That(usages.Cast<Oid>().Select(usage => usage.Value), Is.EqualTo(new[] { "1.3.6.1.5.5.7.3.1" }), "a server's");
            Assert.That(new DateTimeOffset(certificate.NotBefore), Is.LessThan(Now));
            Assert.That(new DateTimeOffset(certificate.NotAfter), Is.EqualTo(Now.AddDays(1)));
        });
    }

    [Test]
    public void OnWindows_ASecondCertificate_UsesTheSameKey_SoOneNameIsAllThereIsToRemove()
    {
        Assume.That(OperatingSystem.IsWindows(), "Only Windows' TLS needs a key in a store.");

        using var first = TestCertificate.Create(_keyName, new TestTimeProvider(Now));
        using var second = TestCertificate.Create(_keyName, new TestTimeProvider(Now));

        Assert.That(second.GetECDsaPublicKey()!.ExportSubjectPublicKeyInfo(), Is.EqualTo(first.GetECDsaPublicKey()!.ExportSubjectPublicKeyInfo()));
    }
}
