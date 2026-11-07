using System.Security.Claims;
using MedicalApp.Common.Authentication;

namespace MedicalApp.UnitTests.Authentication;

public sealed class KeycloakRoleClaimsTransformationTests
{
    private readonly KeycloakRoleClaimsTransformation _transformation = new();

    [Fact]
    public async Task ReadsRolesOnlyFromApiClient()
    {
        var principal = Principal(
            new Claim("resource_access", """{"medicalapp-api":{"roles":["patient"]},"other":{"roles":["doctor","administrator"]}}"""),
            new Claim("realm_access", """{"roles":["doctor","administrator","admin"]}"""),
            new Claim(ApiRoles.ClaimType, ApiRoles.Doctor),
            new Claim(ClaimTypes.Role, ApiRoles.Administrator));

        var result = await _transformation.TransformAsync(principal);

        result.FindAll(ApiRoles.ClaimType).Select(c => c.Value).Should().BeEquivalentTo([ApiRoles.Patient]);
        result.IsInRole(ApiRoles.Doctor).Should().BeFalse();
        result.IsInRole(ApiRoles.Administrator).Should().BeFalse();
        result.IsInRole(ApiRoles.Patient).Should().BeTrue();
    }

    [Fact]
    public async Task SupportsBothRolesAndIgnoresUnknownRoles()
    {
        var principal = Principal(new Claim("resource_access", """{"medicalapp-api":{"roles":["doctor","patient","manage-users"]}}"""));

        var result = await _transformation.TransformAsync(principal);

        result.FindAll(ApiRoles.ClaimType).Select(c => c.Value).Should().BeEquivalentTo([ApiRoles.Doctor, ApiRoles.Patient]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"medicalapp-api":{"roles":"doctor"}}""")]
    [InlineData("""{"medicalapp-api":["doctor"]}""")]
    [InlineData("[]")]
    public async Task MalformedClaimsGrantNoRoles(string value)
    {
        var result = await _transformation.TransformAsync(Principal(new Claim("resource_access", value)));

        result.FindAll(ApiRoles.ClaimType).Should().BeEmpty();
        result.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task IsIdempotentAndPreservesRawClaims()
    {
        var principal = Principal(
            new Claim("iss", "https://identity.example/realms/medicalapp"),
            new Claim("sub", "subject"),
            new Claim("resource_access", """{"medicalapp-api":{"roles":["doctor"]}}"""));

        var once = await _transformation.TransformAsync(principal);
        var twice = await _transformation.TransformAsync(once);

        twice.Should().BeSameAs(once);
        twice.FindAll(ApiRoles.ClaimType).Should().ContainSingle();
        twice.FindFirst("sub")!.Value.Should().Be("subject");
        twice.FindFirst("iss")!.Value.Should().Be("https://identity.example/realms/medicalapp");
    }

    [Fact]
    public async Task LeavesAnonymousPrincipalUnchanged()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _transformation.TransformAsync(anonymous);

        result.Should().BeSameAs(anonymous);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "AuthenticationTypes.Federation"));
}
