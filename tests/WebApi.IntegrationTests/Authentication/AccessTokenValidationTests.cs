using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using FastEndpoints;
using MedicalApp.Common.Authentication;
using MedicalApp.IntegrationTests.Common;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.IntegrationTests.Authentication;

[Collection<ProgressApiCollection>]
public sealed class AccessTokenValidationTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AcceptsAccessTokenAndRejectsMissingForgedAndIdTokens()
    {
        var (accessToken, idToken) = await fixture.Identity.GetTokensAsync("development-doctor");
        new JsonWebTokenHandler().ReadJsonWebToken(idToken).Audiences.Should().NotContain(ApiRoles.Resource);

        using var anonymous = fixture.CreateAnonymousClient();
        (await anonymous.GetAsync("/api/test/claims", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var valid = fixture.CreateClient(accessToken);
        var claims = await valid.GetFromJsonAsync<ClaimsResponse>("/api/test/claims", Ct);
        claims!.Subject.Should().Be(TestIdentity.DoctorUserId);
        claims.Issuer.Should().Be(TestIdentity.Issuer);
        claims.Roles.Should().Equal(ApiRoles.Doctor);

        (await StatusAsync(idToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(Tamper(accessToken))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ControlTokenFromTheServerKeysIsAccepted()
    {
        // The rejection tests below change one property of this token, so this proves each one fails for that reason.
        (await StatusAsync(Forge())).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RejectsTokensFromAnotherIssuer()
    {
        (await StatusAsync(Forge(token => token.Issuer = "https://identity.example/"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectsTokensWithoutSubject()
    {
        (await StatusAsync(Forge(token => token.Subject = new ClaimsIdentity([new Claim(Claims.Role, ApiRoles.Doctor)])))).Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectsTokensWithoutApiAudience()
    {
        (await StatusAsync(Forge(token => token.Audience = null))).Should().Be(HttpStatusCode.Unauthorized);

        // A real client without the API scope gets a token without the API audience.
        await fixture.Identity.CreateTestClientAsync("test-no-audience", apiScope: false);
        var (accessToken, _) = await fixture.Identity.GetTokensAsync("development-doctor", "test-no-audience", scopes: Scopes.OpenId);
        (await StatusAsync(accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiresAfterFiveMinutesWithoutClockSkew()
    {
        var (accessToken, _) = await fixture.Identity.GetTokensAsync("development-doctor");

        fixture.Clock.Offset = TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5);
        (await StatusAsync(accessToken)).Should().Be(HttpStatusCode.OK);

        fixture.Clock.Offset = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
        (await StatusAsync(accessToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AcceptsTokensSignedWithAnyConfiguredKeyDuringRotation()
    {
        // Test hosts sign with a new key and keep TestIdentity.RetiredSigningKey configured for validation.
        var (current, _) = await fixture.Identity.GetTokensAsync("development-doctor");
        var signedBeforeRotation = Forge(token => token.SigningCredentials = new SigningCredentials(TestIdentity.RetiredSigningKey, SecurityAlgorithms.RsaSha256));
        var unknownKey = Forge(token => token.SigningCredentials = new SigningCredentials(Key("unknown"), SecurityAlgorithms.RsaSha256));

        SigningKeyId(current).Should().NotBe(TestIdentity.RetiredSigningKey.KeyId);
        (await StatusAsync(current)).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(signedBeforeRotation)).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(unknownKey)).Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpStatusCode> StatusAsync(string accessToken)
    {
        using var client = fixture.CreateClient(accessToken);
        using var response = await client.GetAsync("/api/test/claims", Ct);
        return response.StatusCode;
    }

    /// <summary>
    /// Builds an access token with the server's own signing and encryption keys, like the server would.
    /// </summary>
    private string Forge(Action<SecurityTokenDescriptor>? change = null)
    {
        using var scope = fixture.CreateScope();
        var server = scope.ServiceProvider.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIdentity.Issuer,
            Audience = ApiRoles.Resource,
            TokenType = JsonWebTokenTypes.AccessToken,
            Subject = new ClaimsIdentity([new Claim(Claims.Subject, TestIdentity.DoctorUserId), new Claim(Claims.Role, ApiRoles.Doctor)]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = server.SigningCredentials.First(credentials => credentials.Key != TestIdentity.RetiredSigningKey),
            EncryptingCredentials = server.EncryptionCredentials[0]
        };
        change?.Invoke(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string Tamper(string token)
    {
        var position = token.Length / 2;
        return token[..position] + (token[position] == 'A' ? 'B' : 'A') + token[(position + 1)..];
    }

    private static RsaSecurityKey Key(string id) => new(RSA.Create(2048)) { KeyId = $"{id}-{Guid.NewGuid():N}" };

    /// <summary>
    /// Decrypts the token with the server's encryption key and returns the key ID of its inner signature.
    /// </summary>
    private string SigningKeyId(string token)
    {
        using var scope = fixture.CreateScope();
        var server = scope.ServiceProvider.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
        var handler = new JsonWebTokenHandler();
        var inner = handler.DecryptToken(handler.ReadJsonWebToken(token), new TokenValidationParameters
        {
            TokenDecryptionKeys = server.EncryptionCredentials.Select(credentials => credentials.Key)
        });
        return handler.ReadJsonWebToken(inner).Kid;
    }

    public sealed record ClaimsResponse(string? Subject, string? Issuer, string[] Roles);
}

/// <summary>
/// Reports the claims that the API sees for an authenticated caller.
/// </summary>
public sealed class ClaimsTestEndpoint : EndpointWithoutRequest<AccessTokenValidationTests.ClaimsResponse>
{
    public override void Configure() => Get("/test/claims");

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(new AccessTokenValidationTests.ClaimsResponse(
        User.FindFirst(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject)?.Value,
        User.FindFirst(OpenIddict.Abstractions.OpenIddictConstants.Claims.Issuer)?.Value,
        User.FindAll(ApiRoles.ClaimType).Select(claim => claim.Value).ToArray()), ct);
}
