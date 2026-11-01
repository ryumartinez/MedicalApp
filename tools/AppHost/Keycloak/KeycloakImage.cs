namespace AppHost.Keycloak;

internal static class KeycloakImage
{
    // The Aspire 13.4.3 integration defaults to 26.6; pin the corresponding release.
    // https://github.com/keycloak/keycloak/releases/tag/26.6.0
    public const string Tag = "26.6.0";
}
