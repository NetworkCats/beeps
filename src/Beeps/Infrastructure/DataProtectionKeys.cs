using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace Beeps.Infrastructure;

/// <summary>
/// Encrypts the Data Protection keys at rest when a certificate is configured (<c>DataProtection:CertificatePath</c>,
/// a PKCS#12 file, and <c>DataProtection:CertificatePassword</c>). The keys live in PostgreSQL, so without it anyone who
/// can read the database can forge session cookies; the certificate is kept outside the database.
/// </summary>
public static class DataProtectionKeys
{
    public const string CertificatePathKey = "DataProtection:CertificatePath";
    public const string CertificatePasswordKey = "DataProtection:CertificatePassword";

    public static IDataProtectionBuilder ProtectWithConfiguredCertificate(this IDataProtectionBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var path = configuration[CertificatePathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            return builder;
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, configuration[CertificatePasswordKey]);
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException($"{CertificatePathKey} must hold the private key, to read the keys back.");
        }

        // Unprotect with it too: in a container the certificate is not in any store the default lookup searches.
        return builder.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
    }
}
