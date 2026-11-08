using System.Security.Claims;
using MedicalApp.Common.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.Common.Identity;

/// <summary>
/// Builds the principal that the authorization server turns into tokens. Roles go only into access tokens.
/// </summary>
public sealed class IdentityPrincipals(UserManager<ApplicationUser> users)
{
    public async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Subject)
            .SetClaim(Claims.Name, user.UserName);

        var roles = (await users.GetRolesAsync(user)).Where(ApiRoles.All.Contains).Order(StringComparer.Ordinal).ToArray();
        identity.SetClaims(Claims.Role, [.. roles]);

        var granted = scopes.ToArray();
        identity.SetScopes(granted);
        identity.SetResources(granted.Contains(ApiRoles.Resource, StringComparer.Ordinal) ? [ApiRoles.Resource] : []);
        identity.SetDestinations(Destinations);

        return new ClaimsPrincipal(identity);
    }

    private static IEnumerable<string> Destinations(Claim claim) => claim.Type switch
    {
        Claims.Name => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        Claims.Role => [OpenIddictConstants.Destinations.AccessToken],
        _ => [OpenIddictConstants.Destinations.AccessToken]
    };
}
