using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Jiron.Umbraco.FormBuilder.Testing.Tls;

/// <summary>
/// The certificate of a test host with HTTPS (ROADMAP 7.3d, ADR-0006): made when the host starts, for this machine only, for a day.
/// Nothing trusts it and no certificate store holds it, so a test's browser ignores that it is self-signed. Windows' TLS cannot use
/// a key that no key store holds, so there its key is the one under the given name, in the user's own store, kept across a host that
/// was stopped by force; whoever starts the host removes it afterwards.
/// </summary>
public static class TestCertificate
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    /// <summary>Makes one.</summary>
    /// <param name="keyName">The name of its key on Windows.</param>
    /// <param name="time">The clock its day starts by.</param>
    public static X509Certificate2 Create(string keyName, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentNullException.ThrowIfNull(time);
        using var key = OpenKey(keyName);
        var request = new CertificateRequest("CN=Jiron Form Builder test host", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthentication)], critical: false));
        var now = time.GetUtcNow();
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
    }

    private static ECDsa OpenKey(string keyName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }

        using var key = CngKey.Exists(keyName) ? CngKey.Open(keyName) : CngKey.Create(CngAlgorithm.ECDsaP256, keyName);
        return new ECDsaCng(key);
    }
}
