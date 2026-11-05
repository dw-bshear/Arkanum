using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace Arkanum.Go.Proof.Tests;

internal static class TestCertificates
{
    private static readonly Dictionary<string, byte[]> Certificates = [];
    internal static X509Certificate2 Create(string name, bool server)
    {
        lock (Certificates)
        {
            var id = name + server;
            if (!Certificates.TryGetValue(id, out var pfx))
            {
                using var rsa = RSA.Create(2048);
                var req = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
                if (server) { var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); req.CertificateExtensions.Add(san.Build()); }
                using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(3));
                Certificates[id] = pfx = cert.Export(X509ContentType.Pfx);
            }
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
    }
}

// No validation callback: real TLS verifies chain, EKU, time and hostname.
// Per-run self-signed TLS leaves are explicitly trusted for these local tests only.
// Share certs across concurrent tests to avoid TLS root-store snapshot cache races.
internal sealed class TemporaryTrust : IDisposable
{
    private static readonly Dictionary<string, int> Users = [];
    private readonly X509Certificate2[] certificates;
    public TemporaryTrust(params X509Certificate2[] certificates)
    {
        this.certificates = certificates;
        lock (Users)
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadWrite);
            foreach (var certificate in certificates)
            {
                Users.TryGetValue(certificate.Thumbprint, out var count);
                if (count == 0) store.Add(certificate);
                Users[certificate.Thumbprint] = count + 1;
            }
        }
    }
    public void Dispose()
    {
        lock (Users)
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadWrite);
            foreach (var certificate in certificates)
            {
                if (--Users[certificate.Thumbprint] == 0) store.Remove(certificate);
            }
        }
    }
}
