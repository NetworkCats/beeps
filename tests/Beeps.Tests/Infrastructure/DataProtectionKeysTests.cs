using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Beeps.Infrastructure;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Infrastructure;

public sealed class DataProtectionKeysTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("beeps-dp-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void WithACertificateTheStoredKeysAreEncryptedAndStillWork()
    {
        var repository = new InMemoryXmlRepository();
        using var services = Build(repository, Certificate());

        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        var token = protector.Protect("hello");

        var key = Assert.Single(repository.GetAllElements()).ToString();
        Assert.Contains("EncryptedData", key, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", key, StringComparison.Ordinal);
        // A second instance with the same certificate reads the key back.
        using var other = Build(repository, Certificate(reuse: true));
        Assert.Equal("hello", other.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(token));
    }

    [Fact]
    public void WithoutOneNothingChanges()
    {
        var repository = new InMemoryXmlRepository();
        using var services = Build(repository, null);

        services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("hello");

        Assert.Contains("<masterKey", Assert.Single(repository.GetAllElements()).ToString(), StringComparison.Ordinal);
    }

    private static ServiceProvider Build(InMemoryXmlRepository repository, string? certificatePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionKeys.CertificatePathKey] = certificatePath,
                [DataProtectionKeys.CertificatePasswordKey] = "secret",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("beeps").ProtectWithConfiguredCertificate(configuration);
        services.Configure<KeyManagementOptions>(o => o.XmlRepository = repository);
        return services.BuildServiceProvider();
    }

    private string Certificate(bool reuse = false)
    {
        var path = Path.Combine(directory, "keys.pfx");
        if (reuse && File.Exists(path))
        {
            return path;
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=beeps data protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, "secret"));
        return path;
    }
}
