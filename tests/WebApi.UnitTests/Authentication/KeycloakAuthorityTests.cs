using MedicalApp.Host.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MedicalApp.UnitTests.Authentication;

public sealed class KeycloakAuthorityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://identity.example/realms/medicalapp")]
    [InlineData("https+http://identity/realms/medicalapp")]
    [InlineData("https://identity.example/realms/other")]
    [InlineData("https://identity.example/realms/medicalapp?unexpected=true")]
    [InlineData("https://user@identity.example/realms/medicalapp")]
    public void ProductionRejectsInvalidAuthority(string? authority)
    {
        var builder = CreateBuilder(authority);
        Action register = builder.AddKeycloakAuthentication;

        register.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProductionUsesExplicitHttpsAuthorityAndStrictExpiration()
    {
        var builder = CreateBuilder("https://identity.example/realms/medicalapp");
        builder.AddKeycloakAuthentication();
        using var services = builder.Services.BuildServiceProvider();
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.Should().Be("https://identity.example/realms/medicalapp");
        options.RequireHttpsMetadata.Should().BeTrue();
        options.MapInboundClaims.Should().BeFalse();
        options.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.Zero);
    }

    private static HostApplicationBuilder CreateBuilder(string? authority)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Keycloak:Authority"] = authority });
        return builder;
    }
}
