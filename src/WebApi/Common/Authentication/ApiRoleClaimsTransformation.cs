using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.Common.Authentication;

/// <summary>
/// Rebuilds a validated access token principal so roles come only from its <c>role</c> claim and only take
/// values from <see cref="ApiRoles.All"/>. Any incoming claim that uses the normalized role claim type is discarded.
/// Other principals, such as the authorization server's sign-in cookie, are left unchanged.
/// </summary>
public sealed class ApiRoleClaimsTransformation : IClaimsTransformation
{
    public const string AuthenticationType = "medicalapp-api";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } source
            || source.AuthenticationType == AuthenticationType
            || principal.GetTokenType() != TokenTypeIdentifiers.AccessToken)
            return Task.FromResult(principal);

        var claims = source.Claims
            .Where(claim => claim.Type != ApiRoles.ClaimType)
            .Select(claim => claim.Clone())
            .ToList();
        claims.AddRange(ReadApiRoles(source).Select(role => new Claim(ApiRoles.ClaimType, role)));

        // Mappings and audit records use the raw iss claim. Restore it from the validated issuer when absent.
        if (!claims.Exists(claim => claim.Type == Claims.Issuer) && source.FindFirst(Claims.Subject)?.Issuer is { Length: > 0 } issuer
            && issuer != ClaimsIdentity.DefaultIssuer)
            claims.Add(new Claim(Claims.Issuer, issuer, ClaimValueTypes.String, issuer));

        var identity = new ClaimsIdentity(claims, AuthenticationType, Claims.Name, ApiRoles.ClaimType);
        return Task.FromResult(new ClaimsPrincipal(identity));
    }

    public static IReadOnlyList<string> ReadApiRoles(ClaimsIdentity identity) =>
        identity.FindAll(Claims.Role)
            .Select(claim => claim.Value)
            .Where(ApiRoles.All.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
