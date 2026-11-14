using MedicalApp.Common.Identity;
using MedicalApp.Common.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MedicalApp.UnitTests.Identity;

public sealed class IdentityStoresTests
{
    [Fact]
    public void AppliesAccountPolicy()
    {
        using var services = CreateServices();
        var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.Should().Be(12);
        options.Password.RequireDigit.Should().BeFalse();
        options.Password.RequireLowercase.Should().BeFalse();
        options.Password.RequireUppercase.Should().BeFalse();
        options.Password.RequireNonAlphanumeric.Should().BeFalse();
        options.User.RequireUniqueEmail.Should().BeTrue();
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Tokens.PasswordResetTokenProvider.Should().Be(PasswordSetupTokenProviderOptions.ProviderName);
    }

    [Fact]
    public void PasswordSetupLinksExpireAfterADayByDefault()
    {
        using var services = CreateServices();

        services.GetRequiredService<IOptions<PasswordSetupTokenProviderOptions>>().Value.TokenLifespan
            .Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void PasswordSetupLifetimeIsConfigurable()
    {
        using var services = CreateServices(new() { [PasswordSetupTokenProviderOptions.LifetimeKey] = "02:00:00" });

        services.GetRequiredService<IOptions<PasswordSetupTokenProviderOptions>>().Value.TokenLifespan
            .Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ProductionWithoutCertificatesFailsAndNamesTheSetting()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var configuration = new ConfigurationBuilder().Build();

        var register = () => new ServiceCollection().AddOpenIddict()
            .AddServer(server => server.AddServerCredentials(configuration, environment));

        register.Should().Throw<InvalidOperationException>().WithMessage($"*{ServerCredentials.SigningKey}*");
    }

    [Fact]
    public void ProductionLoadsConfiguredCertificatesInOrder()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        static string Pfx(string name)
        {
            using var key = System.Security.Cryptography.RSA.Create(3072);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                $"CN={name}", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
            return Convert.ToBase64String(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "secret"));
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ServerCredentials.SigningKey}:0:Base64"] = Pfx("new signing"),
            [$"{ServerCredentials.SigningKey}:0:Password"] = "secret",
            [$"{ServerCredentials.SigningKey}:1:Base64"] = Pfx("old signing"),
            [$"{ServerCredentials.SigningKey}:1:Password"] = "secret",
            [$"{ServerCredentials.EncryptionKey}:Base64"] = Pfx("encryption"),
            [$"{ServerCredentials.EncryptionKey}:Password"] = "secret"
        }).Build();

        ServerCredentials.LoadCertificates(configuration, ServerCredentials.SigningKey).Select(c => c.Subject)
            .Should().Equal("CN=new signing", "CN=old signing");
        ServerCredentials.LoadCertificates(configuration, ServerCredentials.EncryptionKey).Should().ContainSingle()
            .Which.HasPrivateKey.Should().BeTrue();
        var register = () => new ServiceCollection().AddOpenIddict()
            .AddServer(server => server.AddServerCredentials(configuration, environment));
        register.Should().NotThrow();
    }

    private static ServiceProvider CreateServices(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql("Host=unused"));
        services.AddIdentityStores(configuration);
        return services.BuildServiceProvider();
    }
}
