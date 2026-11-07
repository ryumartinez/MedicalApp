namespace MedicalApp.Common.Authentication;

/// <summary>
/// Roles defined on the <c>medicalapp-api</c> Keycloak client.
/// </summary>
public static class ApiRoles
{
    public const string ClientId = "medicalapp-api";

    /// <summary>
    /// Claim type that holds normalized API roles. Only <see cref="KeycloakRoleClaimsTransformation"/> issues it.
    /// </summary>
    public const string ClaimType = "medicalapp:api_role";

    public const string Doctor = "doctor";
    public const string Patient = "patient";
    public const string Administrator = "administrator";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Doctor, Patient, Administrator };
}
