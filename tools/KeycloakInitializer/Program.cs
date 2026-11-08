using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

var endpoint = new Uri(Environment.GetEnvironmentVariable("KEYCLOAK_ENDPOINT")
    ?? throw new InvalidOperationException("Keycloak endpoint is required."));
if (!endpoint.IsLoopback || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
    throw new InvalidOperationException("The local Keycloak initializer requires a loopback HTTP or HTTPS endpoint.");

var password = Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_PASSWORD")
    ?? throw new InvalidOperationException("Keycloak administrator password is required.");
var provisioningSecret = Environment.GetEnvironmentVariable("KEYCLOAK_PROVISIONING_SECRET")
    ?? throw new InvalidOperationException("Keycloak provisioning client secret is required.");
var developmentPassword = Environment.GetEnvironmentVariable("KEYCLOAK_DEV_PASSWORD")
    ?? throw new InvalidOperationException("The development account password (KEYCLOAK_DEV_PASSWORD) is required.");
var realmFile = Path.Combine(AppContext.BaseDirectory, "medicalapp-realm.json");
var realm = JsonNode.Parse(await File.ReadAllTextAsync(realmFile))?.AsObject()
    ?? throw new InvalidOperationException("The local Keycloak realm definition is invalid.");

// Aspire's local container certificate can be untrusted by the host. This process only accepts loopback endpoints.
using var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
        errors == System.Net.Security.SslPolicyErrors.None || request.RequestUri?.IsLoopback == true
};
using var client = new HttpClient(handler) { BaseAddress = endpoint };
using var tokenResponse = await client.PostAsync("/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
    new Dictionary<string, string>
    {
        ["client_id"] = "admin-cli",
        ["grant_type"] = "password",
        ["username"] = "admin",
        ["password"] = password
    }));
tokenResponse.EnsureSuccessStatusCode();
var token = await tokenResponse.Content.ReadFromJsonAsync<JsonObject>();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token?["access_token"]?.GetValue<string>()
    ?? throw new InvalidOperationException("Keycloak did not return an administrator access token."));

await KeycloakInitializer.KeycloakRealmReconciler.ReconcileAsync(client, realm, provisioningSecret, developmentPassword);

Console.WriteLine("The medicalapp realm, clients, roles, and development accounts are ready.");

