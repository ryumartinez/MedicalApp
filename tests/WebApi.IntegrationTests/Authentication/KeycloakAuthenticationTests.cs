using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FastEndpoints;
using MedicalApp.Common.Authentication;
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
            options.Filter = type => type == typeof(ProtectedTestEndpoint);
        });
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseCustomFastEndpoints();
        _app.MapGet("/protected", (ClaimsPrincipal principal) => Results.Ok(principal.FindFirst("sub")?.Value)).RequireAuthorization();
        _app.MapGet("/roles", (ClaimsPrincipal principal) => Results.Ok(principal.FindAll(ApiRoles.ClaimType).Select(c => c.Value).ToArray())).RequireAuthorization();
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
        var roles = await client.GetFromJsonAsync<string[]>("/roles", cancellationToken);
        roles.Should().BeEquivalentTo([ApiRoles.Doctor]);
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

    [Fact]
    public async Task RejectsTokensFromAnotherRealm()
    {
        await _keycloak.CreateRealmCopyAsync("other");
        var (accessToken, _) = await _keycloak.GetTokensAsync("development-doctor", realm: "other");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Issuer.Should().EndWith("/realms/other");
        jwt.Audiences.Should().Contain("medicalapp-api");

        (await GetStatusAsync(accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectsTokensWithoutApiAudience()
    {
        await _keycloak.CreateTestClientAsync("test-no-audience", apiAudience: false);
        var (accessToken, _) = await _keycloak.GetTokensAsync("development-doctor", "test-no-audience");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Issuer.Should().Be(_keycloak.Issuer);
        jwt.Audiences.Should().NotContain("medicalapp-api");

        (await GetStatusAsync(accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectsExpiredTokensWithoutClockSkew()
    {
        await _keycloak.CreateTestClientAsync("test-short-lived", apiAudience: true, accessTokenLifespanSeconds: 3);
        var (accessToken, _) = await _keycloak.GetTokensAsync("development-doctor", "test-short-lived");
        var expires = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo;
        (await GetStatusAsync(accessToken)).Should().Be(HttpStatusCode.OK);

        var wait = expires - DateTime.UtcNow + TimeSpan.FromSeconds(1);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, TestContext.Current.CancellationToken);

        (await GetStatusAsync(accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AcceptsTokensSignedWithRotatedKey()
    {
        var (before, _) = await _keycloak.GetTokensAsync();
        (await GetStatusAsync(before)).Should().Be(HttpStatusCode.OK);

        await _keycloak.RotateSigningKeyAsync();
        var (after, _) = await _keycloak.GetTokensAsync();
        var handler = new JwtSecurityTokenHandler();
        handler.ReadJwtToken(after).Header.Kid.Should().NotBe(handler.ReadJwtToken(before).Header.Kid);

        // An unknown key ID makes the API refresh discovery metadata. Allow the refresh to finish.
        var status = HttpStatusCode.Unauthorized;
        for (var attempt = 0; attempt < 20 && status != HttpStatusCode.OK; attempt++)
        {
            status = await GetStatusAsync(after);
            if (status != HttpStatusCode.OK)
                await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        status.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpStatusCode> GetStatusAsync(string accessToken)
    {
        using var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.GetAsync("/protected", TestContext.Current.CancellationToken);
        return response.StatusCode;
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
