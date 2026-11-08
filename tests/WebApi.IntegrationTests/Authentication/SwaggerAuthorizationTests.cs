using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MedicalApp.Common.Authentication;
using MedicalApp.Host.Extensions;
using MedicalApp.IntegrationTests.Common;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.IntegrationTests.Authentication;

[Collection<ProgressApiCollection>]
public sealed class SwaggerAuthorizationTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task DocumentsAuthorizationCodeFlowAndProtectsOperations()
    {
        using var client = fixture.CreateAnonymousClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Ct))!;

        var schemes = document["components"]!["securitySchemes"]!.AsObject();
        schemes.Select(x => x.Key).Should().Equal(IdentityServerExt.SwaggerSecurityScheme);
        var flow = schemes[IdentityServerExt.SwaggerSecurityScheme]!["flows"]!["authorizationCode"]!;
        flow["authorizationUrl"]!.GetValue<string>().Should().Be($"{TestIdentity.Issuer}{IdentityServerExt.AuthorizationPath}");
        flow["tokenUrl"]!.GetValue<string>().Should().Be($"{TestIdentity.Issuer}{IdentityServerExt.TokenPath}");
        flow["scopes"]!.AsObject().Select(x => x.Key).Should().BeEquivalentTo([Scopes.OpenId, ApiRoles.Resource]);

        var createAccount = document["paths"]!["/api/accounts"]!["post"]!;
        createAccount["security"]!.AsArray().Select(x => x!.AsObject().Single().Key).Should().Contain(IdentityServerExt.SwaggerSecurityScheme);
        document["paths"]!.AsObject().Select(x => x.Key).Should().NotContain(path => path.Contains("connect"));

        var ui = await client.GetStringAsync("/swagger/index.html", Ct);
        ui.Should().Contain(TestIdentity.SwaggerClientId);
        ui.Should().MatchRegex("usePkceWithAuthorizationCodeGrant\"?\\s*:\\s*true");
    }

    [Fact]
    public async Task DiscoveryAdvertisesOnlyTheCodeFlowWithPkce()
    {
        using var browser = fixture.CreateBrowser();
        var discovery = JsonNode.Parse(await browser.GetStringAsync("/.well-known/openid-configuration", Ct))!;

        discovery["issuer"]!.GetValue<string>().Should().Be(TestIdentity.Issuer);
        discovery["grant_types_supported"]!.AsArray().Select(x => x!.GetValue<string>()).Should().Equal(GrantTypes.AuthorizationCode);
        discovery["response_types_supported"]!.AsArray().Select(x => x!.GetValue<string>()).Should().Equal(ResponseTypes.Code);
        discovery["code_challenge_methods_supported"]!.AsArray().Select(x => x!.GetValue<string>()).Should().Equal(CodeChallengeMethods.Sha256);
    }

    [Fact]
    public async Task SwaggerClientLoginProducesUsableAccessToken()
    {
        // The fixture signs in through the same authorization endpoint, client, PKCE challenge, and redirect URI as Swagger UI.
        using var admin = await fixture.CreateClientAsync("development-administrator");

        using var response = await admin.GetAsync($"/api/accounts/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RejectsUnregisteredRedirectUriWithoutIssuingACode()
    {
        using var browser = fixture.CreateBrowser();
        const string evil = "https://evil.example/callback";

        using var response = await browser.GetAsync(TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create(), redirectUri: evil), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task RejectsAuthorizationRequestsWithoutPkce()
    {
        using var browser = fixture.CreateBrowser();
        var url = TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create())
            .Replace("&code_challenge=", "&ignored=", StringComparison.Ordinal);

        using var response = await browser.GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(Errors.InvalidRequest).And.Contain("code_challenge");
    }

    [Fact]
    public async Task RejectsThePasswordGrant()
    {
        using var browser = fixture.CreateBrowser();

        using var response = await TestIdentity.PostTokenAsync(browser, new Dictionary<string, string>
        {
            ["grant_type"] = GrantTypes.Password,
            ["client_id"] = TestIdentity.SwaggerClientId,
            ["username"] = "development-doctor",
            ["password"] = TestIdentity.Password
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["error"]!.GetValue<string>().Should().Be(Errors.UnsupportedGrantType);
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("access_token");
    }

    [Fact]
    public async Task IssuesNoRefreshTokens()
    {
        using var browser = fixture.CreateBrowser();
        var pkce = TestIdentity.Pkce.Create();

        using var offline = await browser.GetAsync(
            TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, pkce, $"{TestIdentity.ApiScopes} {Scopes.OfflineAccess}"), Ct);
        offline.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        offline.Headers.Location.Should().BeNull();
        (await offline.Content.ReadAsStringAsync(Ct)).Should().Contain(Scopes.OfflineAccess);

        var code = await TestIdentity.SignInForCodeAsync(browser, TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, pkce),
            "development-doctor", TestIdentity.Password);
        var tokens = await TestIdentity.ExchangeCodeAsync(browser, TestIdentity.SwaggerClientId, code, pkce);
        tokens.ContainsKey("access_token").Should().BeTrue();
        tokens.ContainsKey("refresh_token").Should().BeFalse();
        tokens["expires_in"]!.GetValue<int>().Should().BeInRange(299, 300);
    }

    [Fact]
    public async Task AuthorizationCodesWorkOnce()
    {
        using var browser = fixture.CreateBrowser();
        var pkce = TestIdentity.Pkce.Create();
        var code = await TestIdentity.SignInForCodeAsync(browser, TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, pkce),
            "development-doctor", TestIdentity.Password);

        await TestIdentity.ExchangeCodeAsync(browser, TestIdentity.SwaggerClientId, code, pkce);
        var reuse = () => TestIdentity.ExchangeCodeAsync(browser, TestIdentity.SwaggerClientId, code, pkce);

        await reuse.Should().ThrowAsync<InvalidOperationException>().WithMessage("*invalid_grant*");
    }
}
