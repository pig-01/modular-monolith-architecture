using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auth.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Auth.Tests;

public sealed class AuthConfigurationTests
{
    [Fact]
    public void Lifetime_reload_keeps_last_valid_values_when_numeric_or_malformed_values_are_rejected()
    {
        var configuration = SqlAuthFixture.BuildConfiguration();
        using var services = Build(configuration);
        var policy = services.GetRequiredService<AuthLifetimePolicy>();
        Assert.Equal(15, policy.Current.AccessTokenMinutes);
        configuration["Auth:Lifetimes:AccessTokenMinutes"] = "20";
        configuration.Reload();
        Assert.Equal(20, policy.Current.AccessTokenMinutes);
        configuration["Auth:Lifetimes:AccessTokenMinutes"] = "0";
        configuration.Reload();
        Assert.Equal(20, policy.Current.AccessTokenMinutes);
        configuration["Auth:Lifetimes:AccessTokenMinutes"] = "oops";
        configuration.Reload();
        Assert.Equal(20, policy.Current.AccessTokenMinutes);
        configuration["Auth:Lifetimes:AccessTokenMinutes"] = "10";
        configuration.Reload();
        Assert.Equal(10, policy.Current.AccessTokenMinutes);
    }

    [Fact]
    public void Invalid_initial_lifetimes_fail_instead_of_using_an_unvalidated_default()
    {
        var configuration = SqlAuthFixture.BuildConfiguration();
        configuration["Auth:Lifetimes:SessionDays"] = "oops";
        using var services = Build(configuration);
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<AuthLifetimePolicy>());
    }

    [Fact]
    public void Retained_decryption_certificate_reads_old_protected_data_after_certificate_and_key_rotation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AuthDpRollover_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var firstCertificate = CreateCertificate(directory, "first");
            var secondCertificate = CreateCertificate(directory, "second");
            var configuration = SqlAuthFixture.BuildConfiguration();
            configuration["DataProtection:ApplicationName"] = "RolloverTest";
            configuration["DataProtection:KeyDirectory"] = Path.Combine(directory, "ring");
            configuration["DataProtection:CertificatePath"] = firstCertificate;
            string protectedValue;
            using (var first = Build(configuration))
                protectedValue = first.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("test-purpose").Protect("old session retry value");

            configuration["DataProtection:CertificatePath"] = secondCertificate;
            configuration["DataProtection:PreviousCertificates:0:Path"] = firstCertificate;
            using (var second = Build(configuration))
            {
                second.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(90));
                var protector = second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test-purpose");
                Assert.Equal("old session retry value", protector.Unprotect(protectedValue));
                Assert.Equal("new session retry value", protector.Unprotect(protector.Protect("new session retry value")));
            }
            using var restarted = Build(configuration);
            Assert.Equal("old session retry value", restarted.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test-purpose").Unprotect(protectedValue));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthModule(configuration, new TestEnvironment());
        return services.BuildServiceProvider();
    }

    private static string CreateCertificate(string directory, string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(directory, name + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        return path;
    }
}