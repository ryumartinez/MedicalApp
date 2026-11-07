namespace MedicalApp.Common.Identity;

public sealed class KeycloakProvisioningOptions
{
    public const string SectionName = "Authentication:Keycloak:Provisioning";

    public string ClientId { get; set; } = "medicalapp-provisioning";

    public string? ClientSecret { get; set; }

    /// <summary>
    /// Keycloak base URL without the realm path. Defaults to service discovery in development and to the authority host otherwise.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string Realm { get; set; } = "medicalapp";
}
