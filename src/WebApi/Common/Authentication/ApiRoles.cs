namespace MedicalApp.Common.Authentication;

/// <summary>
/// Roles that grant access to the <c>medicalapp-api</c> resource.
/// </summary>
public static class ApiRoles
{
    /// <summary>
    /// The API's audience, resource, and scope name.
    /// </summary>
    public const string Resource = "medicalapp-api";

    /// <summary>
    /// Claim type that holds normalized API roles. Only <see cref="ApiRoleClaimsTransformation"/> issues it.
    /// </summary>
    public const string ClaimType = "medicalapp:api_role";

    public const string Doctor = "doctor";
    public const string Patient = "patient";
    public const string Administrator = "administrator";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Doctor, Patient, Administrator };
}
