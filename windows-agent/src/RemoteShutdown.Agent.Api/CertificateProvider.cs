using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Api;

/// <summary>
/// Generates (once) and reloads a self-signed certificate for Kestrel, per docs/protocol.md §1/§4:
/// the phone pins this certificate's SHA-256 fingerprint at pairing time instead of relying on a CA.
///
/// The cert is installed into the CurrentUser\My certificate store rather than kept purely as
/// in-memory/PFX bytes: SChannel (which Kestrel's SslStream uses on Windows) needs the private key
/// reachable via a real, store-backed CNG key container to complete a TLS server handshake — an
/// ephemeral or freshly-reloaded-from-PFX key silently fails the handshake (verified empirically;
/// see git history for the PFX round-trip approach that didn't work). Same mechanism `dotnet
/// dev-certs https` relies on.
/// </summary>
public static class CertificateProvider
{
    private const string SubjectName = "CN=RemoteShutdownAgent";

    public static X509Certificate2 GetOrCreate(SettingsStore settings)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var existing = store.Certificates
            .Find(X509FindType.FindBySubjectDistinguishedName, SubjectName, validOnly: false)
            .OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey && c.NotAfter > DateTime.UtcNow.AddDays(30))
            .OrderByDescending(c => c.NotAfter)
            .FirstOrDefault();

        if (existing is not null)
            return existing;

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: false)); // TLS Server Authentication

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        // Re-import as persistent+exportable before adding to the store, so the key survives
        // process exit (the CreateSelfSigned result's key is otherwise ephemeral).
        var persisted = X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx), password: null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        store.Add(persisted);

        var fingerprint = Convert.ToHexString(persisted.GetCertHash(HashAlgorithmName.SHA256));
        settings.Set(SettingsStore.Keys.CertThumbprint, fingerprint);

        return persisted;
    }
}
