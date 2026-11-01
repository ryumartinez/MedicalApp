using System.Net;
using System.Text.Json.Nodes;
using MedicalApp.IntegrationTests.Common;

namespace MedicalApp.IntegrationTests.Authentication;

[Collection<ProgressApiCollection>]
public sealed class SwaggerAuthorizationTests(ProgressApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DocumentsAuthorizationCodeFlowAndProtectsOperations()
    {
        using var client = fixture.CreateAnonymousClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", Ct))!;

        var flow = document["components"]!["securitySchemes"]!["Keycloak"]!["flows"]!["authorizationCode"]!;
        flow["authorizationUrl"]!.GetValue<string>().Should().Be($"{fixture.Keycloak.Issuer}/protocol/openid-connect/auth");
        flow["tokenUrl"]!.GetValue<string>().Should().Be($"{fixture.Keycloak.Issuer}/protocol/openid-connect/token");
        document["components"]!["securitySchemes"]!.AsObject().Select(x => x.Key).Should().Equal("Keycloak");

        var createAccount = document["paths"]!["/api/accounts"]!["post"]!;
        createAccount["security"]!.AsArray().Select(x => x!.AsObject().Single().Key).Should().Contain("Keycloak");

        var ui = await client.GetStringAsync("/swagger/index.html", Ct);
        ui.Should().Contain("medicalapp-swagger");
        ui.Should().MatchRegex("usePkceWithAuthorizationCodeGrant\"?\\s*:\\s*true");
    }

    [Fact]
    public async Task SwaggerClientLoginProducesUsableAccessToken()
    {
        // The fixture signs in through the same authorization endpoint, client, PKCE challenge, and redirect URI as Swagger UI.
        using var admin = await fixture.CreateClientAsync("development-administrator");

        using var response = await admin.GetAsync($"/api/accounts/{Guid.CreateVersion7()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
