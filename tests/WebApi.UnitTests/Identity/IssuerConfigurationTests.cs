using MedicalApp.Common.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace MedicalApp.UnitTests.Identity;

public sealed class IssuerConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://identity.example/")]
    [InlineData("https+http://api")]
    [InlineData("identity.example")]
    [InlineData("https://identity.example/?unexpected=true")]
    [InlineData("https://identity.example/#fragment")]
    [InlineData("https://user@identity.example/")]
    public void ProductionRejectsInvalidIssuer(string? issuer)
    {
        var resolve = () => IdentityIssuer.Resolve(Configuration(issuer), Environment(Environments.Production));

        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*{IdentityIssuer.ConfigurationKey}*");
    }

    [Theory]
    [InlineData("https://identity.example", "https://identity.example/")]
    [InlineData("https://identity.example/", "https://identity.example/")]
    [InlineData("https://identity.example/auth//", "https://identity.example/auth/")]
    public void ProductionNormalizesValidIssuer(string issuer, string expected)
    {
        IdentityIssuer.Resolve(Configuration(issuer), Environment(Environments.Production)).AbsoluteUri.Should().Be(expected);
    }

    [Fact]
    public void DevelopmentDefaultsToLocalApiAddress()
    {
        IdentityIssuer.Resolve(Configuration(null), Environment(Environments.Development)).AbsoluteUri
            .Should().Be(IdentityIssuer.DevelopmentDefault);
    }

    [Fact]
    public void DevelopmentAcceptsExplicitHttpIssuer()
    {
        IdentityIssuer.Resolve(Configuration("http://localhost"), Environment(Environments.Development)).AbsoluteUri
            .Should().Be("http://localhost/");
    }

    [Theory]
    [InlineData("http://localhost/?query=1")]
    [InlineData("ftp://localhost/")]
    public void DevelopmentStillRejectsMalformedIssuer(string issuer)
    {
        var resolve = () => IdentityIssuer.Resolve(Configuration(issuer), Environment(Environments.Development));

        resolve.Should().Throw<InvalidOperationException>();
    }

    private static IConfiguration Configuration(string? issuer) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [IdentityIssuer.ConfigurationKey] = issuer })
        .Build();

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }
}
