using System.Security.Claims;
using MedicalApp.Common.Authentication;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.UnitTests.Authentication;

public sealed class ApiRoleClaimsTransformationTests
{
    private readonly ApiRoleClaimsTransformation _transformation = new();

    [Fact]
    public async Task KeepsOnlyKnownRolesFromTheRoleClaim()
    {
        var principal = AccessToken(
            new Claim(Claims.Role, ApiRoles.Patient),
            new Claim(Claims.Role, "manage-users"),
            new Claim(ClaimTypes.Role, ApiRoles.Administrator),
            new Claim("realm_access", """{"roles":["doctor"]}"""));

        var result = await _transformation.TransformAsync(principal);

        result.FindAll(ApiRoles.ClaimType).Select(c => c.Value).Should().BeEquivalentTo([ApiRoles.Patient]);
        result.IsInRole(ApiRoles.Patient).Should().BeTrue();
        result.IsInRole(ApiRoles.Administrator).Should().BeFalse();
        result.IsInRole(ApiRoles.Doctor).Should().BeFalse();
    }

    [Fact]
    public async Task DropsInjectedNormalizedRoleClaims()
    {
        var principal = AccessToken(new Claim(ApiRoles.ClaimType, ApiRoles.Administrator), new Claim(Claims.Role, ApiRoles.Doctor));

        var result = await _transformation.TransformAsync(principal);

        result.FindAll(ApiRoles.ClaimType).Select(c => c.Value).Should().Equal(ApiRoles.Doctor);
    }

    [Fact]
    public async Task SupportsBothRoles()
    {
        var principal = AccessToken(new Claim(Claims.Role, ApiRoles.Doctor), new Claim(Claims.Role, ApiRoles.Patient), new Claim(Claims.Role, ApiRoles.Doctor));

        var result = await _transformation.TransformAsync(principal);

        result.FindAll(ApiRoles.ClaimType).Select(c => c.Value).Should().BeEquivalentTo([ApiRoles.Doctor, ApiRoles.Patient]);
    }

    [Fact]
    public async Task IsIdempotentAndPreservesRawClaims()
    {
        var principal = AccessToken(
            new Claim(Claims.Issuer, "https://identity.example/"),
            new Claim(Claims.Subject, "subject"),
            new Claim(Claims.Role, ApiRoles.Doctor));

        var once = await _transformation.TransformAsync(principal);
        var twice = await _transformation.TransformAsync(once);

        twice.Should().BeSameAs(once);
        twice.FindAll(ApiRoles.ClaimType).Should().ContainSingle();
        twice.FindFirst(Claims.Subject)!.Value.Should().Be("subject");
        twice.FindFirst(Claims.Issuer)!.Value.Should().Be("https://identity.example/");
    }

    [Fact]
    public async Task RestoresTheIssuerClaimFromTheValidatedIssuer()
    {
        var principal = AccessToken(new Claim(Claims.Subject, "subject", ClaimValueTypes.String, "https://identity.example/"));

        var result = await _transformation.TransformAsync(principal);

        result.FindFirst(Claims.Issuer)!.Value.Should().Be("https://identity.example/");
    }

    [Fact]
    public async Task LeavesOtherPrincipalsUnchanged()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var cookie = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.Role, ApiRoles.Administrator)], "Identity.Application"));
        var idToken = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.Role, ApiRoles.Administrator)], "OpenIddict"));
        idToken.SetTokenType(TokenTypeIdentifiers.IdentityToken);

        (await _transformation.TransformAsync(anonymous)).Should().BeSameAs(anonymous);
        (await _transformation.TransformAsync(cookie)).Should().BeSameAs(cookie);
        (await _transformation.TransformAsync(idToken)).Should().BeSameAs(idToken);
        cookie.FindAll(ApiRoles.ClaimType).Should().BeEmpty();
    }

    private static ClaimsPrincipal AccessToken(params Claim[] claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "OpenIddict.Validation.AspNetCore"));
        principal.SetTokenType(TokenTypeIdentifiers.AccessToken);
        return principal;
    }
}
