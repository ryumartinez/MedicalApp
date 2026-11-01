using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace MedicalApp.Common.Authentication;

/// <summary>
/// Rebuilds the validated principal so roles come only from <c>resource_access.medicalapp-api.roles</c>.
/// Any incoming claim that uses the normalized role claim type is discarded.
/// </summary>
public sealed class KeycloakRoleClaimsTransformation : IClaimsTransformation
{
    public const string AuthenticationType = "medicalapp-api";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } source
            || source.AuthenticationType == AuthenticationType)
            return Task.FromResult(principal);

        var claims = source.Claims
            .Where(claim => claim.Type != ApiRoles.ClaimType)
            .Select(claim => claim.Clone())
            .ToList();
        claims.AddRange(ReadApiRoles(source).Select(role => new Claim(ApiRoles.ClaimType, role)));

        var identity = new ClaimsIdentity(claims, AuthenticationType, "preferred_username", ApiRoles.ClaimType);
        return Task.FromResult(new ClaimsPrincipal(identity));
    }

    public static IReadOnlyList<string> ReadApiRoles(ClaimsIdentity identity)
    {
        var roles = new List<string>();
        foreach (var claim in identity.FindAll("resource_access"))
        {
            try
            {
                using var document = JsonDocument.Parse(claim.Value);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(ApiRoles.ClientId, out var client)
                    || client.ValueKind != JsonValueKind.Object
                    || !client.TryGetProperty("roles", out var values)
                    || values.ValueKind != JsonValueKind.Array)
                    continue;

                roles.AddRange(values.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()!)
                    .Where(ApiRoles.All.Contains));
            }
            catch (JsonException)
            {
                // A malformed claim grants no roles.
            }
        }

        return roles.Distinct(StringComparer.Ordinal).ToList();
    }
}
