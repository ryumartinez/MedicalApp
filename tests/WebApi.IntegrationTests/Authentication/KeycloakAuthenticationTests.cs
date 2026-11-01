using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FastEndpoints;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Services;
using MedicalApp.Host.Extensions;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MedicalApp.IntegrationTests.Authentication;

public sealed class KeycloakAuthenticationTests : IAsyncLifetime
{
    private readonly KeycloakContainer _keycloak = new();
    private WebApplication _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _keycloak.InitializeAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["services:keycloak:https:0"] = _keycloak.Address });
        builder.AddServiceDefaults();
        builder.AddKeycloakAuthentication();
        builder.Services.AddHttpClient("KeycloakBackchannel").ConfigurePrimaryHttpMessageHandler(_keycloak.CreateHandler);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ProtectedTestEndpoint).Assembly];
        });
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseCustomFastEndpoints();
        _app.MapGet("/protected", (ClaimsPrincipal principal) => Results.Ok(principal.FindFirst("sub")?.Value)).RequireAuthorization();
        await _app.StartAsync();
    }

    [Fact]
    public async Task AcceptsAccessTokenAndRejectsMissingForgedAndIdTokens()
    {
        var (accessToken, identityToken) = await _keycloak.GetTokensAsync();
        var idToken = new JwtSecurityTokenHandler().ReadJwtToken(identityToken);
        idToken.Audiences.Should().Contain("medicalapp-api");
        var accessJwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        accessJwt.Audiences.Should().Contain("medicalapp-api");
        accessJwt.Claims.Single(c => c.Type == "typ").Value.Should().Be("Bearer");
        using var client = _app.GetTestClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var anonymous = await client.GetAsync("/protected", cancellationToken);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var anonymousEndpoint = await client.GetAsync("/api/protected-test", cancellationToken);
        anonymousEndpoint.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var valid = await client.GetAsync("/protected", cancellationToken);
        valid.StatusCode.Should().Be(HttpStatusCode.OK, "the authentication challenge was {0}", string.Join(", ", valid.Headers.WwwAuthenticate));
        (await valid.Content.ReadAsStringAsync(cancellationToken)).Should().Contain("01967a92-2ff8-7a00-8000-000000000001");
        using var authorizedEndpoint = await client.GetAsync("/api/protected-test", cancellationToken);
        authorizedEndpoint.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identityToken);
        using var id = await client.GetAsync("/protected", cancellationToken);
        id.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var signatureStart = accessToken.LastIndexOf('.') + 1;
        var forged = accessToken[..signatureStart] + (accessToken[signatureStart] == 'A' ? 'B' : 'A') + accessToken[(signatureStart + 1)..];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        using var invalid = await client.GetAsync("/protected", cancellationToken);
        invalid.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _keycloak.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed class ProtectedTestEndpoint : EndpointWithoutRequest
{
    public override void Configure() => Get("/protected-test");

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(cancellation: ct);
}
