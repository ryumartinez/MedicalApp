using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace MedicalApp.Common.Identity;

/// <summary>
/// Calls the Keycloak administrator API through the provisioning service account.
/// The service account can manage users and read clients, but cannot change realm settings.
/// </summary>
public sealed class KeycloakAccountAdministration(
    HttpClient httpClient,
    KeycloakProvisioningTokenCache tokenCache,
    IOptions<KeycloakProvisioningOptions> options) : IIdentityProviderAccounts
{
    public const string HttpClientName = "KeycloakProvisioning";
    public const string ProvisioningAttribute = "medicalapp_provisioning_id";

    private readonly KeycloakProvisioningOptions _options = options.Value;
    private string RealmPath => $"admin/realms/{Uri.EscapeDataString(_options.Realm)}";

    public async Task<IdentityProviderAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get,
            $"{RealmPath}/users?username={Uri.EscapeDataString(username)}&exact=true&briefRepresentation=false", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken) ?? [];
        var user = users.Select(node => node!.AsObject())
            .SingleOrDefault(node => string.Equals(node["username"]?.GetValue<string>(), username, StringComparison.OrdinalIgnoreCase));
        return user is null ? null : await ToAccountAsync(user, cancellationToken);
    }

    public async Task<IdentityProviderAccount?> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, UserPath(subject), cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new InvalidOperationException("Keycloak returned an invalid user.");
        return await ToAccountAsync(user, cancellationToken);
    }

    public async Task<IdentityProviderAccount?> CreateAsync(NewIdentityProviderAccount account, string provisioningId,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{RealmPath}/users", cancellationToken);
        request.Content = JsonContent.Create(new JsonObject
        {
            ["username"] = account.Username,
            ["email"] = account.Email,
            ["firstName"] = account.FirstName,
            ["lastName"] = account.LastName,
            ["enabled"] = true,
            ["emailVerified"] = false,
            // The account holder sets a password after an operator issues a temporary one in Keycloak.
            ["requiredActions"] = new JsonArray("UPDATE_PASSWORD"),
            ["attributes"] = new JsonObject { [ProvisioningAttribute] = new JsonArray(provisioningId) }
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return null;
        response.EnsureSuccessStatusCode();

        var subject = response.Headers.Location?.Segments.LastOrDefault()?.TrimEnd('/')
            ?? throw new InvalidOperationException("Keycloak did not return the created user's location.");
        return await FindBySubjectAsync(subject, cancellationToken)
            ?? throw new InvalidOperationException("Keycloak did not return the created user.");
    }

    public async Task SetApiRolesAsync(string subject, IReadOnlySet<string> roles, CancellationToken cancellationToken)
    {
        var unknown = roles.Where(role => !ApiRoles.All.Contains(role)).ToList();
        if (unknown.Count != 0)
            throw new ArgumentException($"Unknown API roles: {string.Join(", ", unknown)}.", nameof(roles));

        var mappingPath = $"{UserPath(subject)}/role-mappings/clients/{Uri.EscapeDataString(await GetApiClientUuidAsync(cancellationToken))}";
        var current = await GetArrayAsync(mappingPath, cancellationToken);
        var currentNames = current.Select(node => node?["name"]?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal);

        var remove = new JsonArray(current
            .Where(node => node?["name"]?.GetValue<string>() is { } name && ApiRoles.All.Contains(name) && !roles.Contains(name))
            .Select(node => node!.DeepClone()).ToArray());
        if (remove.Count != 0)
            await SendAsync(HttpMethod.Delete, mappingPath, remove, cancellationToken);

        var add = new JsonArray();
        foreach (var role in roles.Where(role => !currentNames.Contains(role)))
            add.Add(await GetObjectAsync($"{RealmPath}/clients/{Uri.EscapeDataString(await GetApiClientUuidAsync(cancellationToken))}/roles/{Uri.EscapeDataString(role)}", cancellationToken));
        if (add.Count != 0)
            await SendAsync(HttpMethod.Post, mappingPath, add, cancellationToken);
    }

    public Task SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, UserPath(subject), new JsonObject { ["enabled"] = enabled }, cancellationToken);

    public Task LogoutAsync(string subject, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"{UserPath(subject)}/logout", null, cancellationToken);

    private string UserPath(string subject) => $"{RealmPath}/users/{Uri.EscapeDataString(subject)}";

    private async Task<IdentityProviderAccount> ToAccountAsync(JsonObject user, CancellationToken cancellationToken)
    {
        var subject = user["id"]!.GetValue<string>();
        var mappingPath = $"{UserPath(subject)}/role-mappings/clients/{Uri.EscapeDataString(await GetApiClientUuidAsync(cancellationToken))}";
        var roles = (await GetArrayAsync(mappingPath, cancellationToken))
            .Select(node => node?["name"]?.GetValue<string>())
            .OfType<string>()
            .Where(ApiRoles.All.Contains)
            .ToHashSet(StringComparer.Ordinal);
        var provisioningId = user["attributes"]?[ProvisioningAttribute]?.AsArray().FirstOrDefault()?.GetValue<string>();

        return new IdentityProviderAccount(
            subject,
            user["username"]!.GetValue<string>(),
            user["enabled"]?.GetValue<bool>() ?? false,
            provisioningId,
            roles);
    }

    private async Task<string> GetApiClientUuidAsync(CancellationToken cancellationToken) =>
        await tokenCache.GetApiClientUuidAsync(async () =>
        {
            var clients = await GetArrayAsync($"{RealmPath}/clients?clientId={ApiRoles.ClientId}", cancellationToken);
            return clients.Single()!["id"]!.GetValue<string>();
        });

    private async Task<JsonArray> GetArrayAsync(string path, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, path, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken) ?? [];
    }

    private async Task<JsonObject> GetObjectAsync(string path, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, path, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new InvalidOperationException("Keycloak returned an invalid response.");
    }

    private async Task SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, path, cancellationToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var token = await tokenCache.GetTokenAsync(httpClient, _options, cancellationToken);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}

/// <summary>
/// Caches the provisioning service account token and the API client's internal ID.
/// </summary>
public sealed class KeycloakProvisioningTokenCache(TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;
    private string? _apiClientUuid;

    public async Task<string> GetTokenAsync(HttpClient httpClient, KeycloakProvisioningOptions options, CancellationToken cancellationToken)
    {
        if (_token is { } cached && timeProvider.GetUtcNow() < _expiresAt)
            return cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is { } refreshed && timeProvider.GetUtcNow() < _expiresAt)
                return refreshed;

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
                throw new InvalidOperationException($"{KeycloakProvisioningOptions.SectionName}:ClientSecret is required for account provisioning.");

            using var response = await httpClient.PostAsync(
                $"realms/{Uri.EscapeDataString(options.Realm)}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret
                }), cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
                ?? throw new InvalidOperationException("Keycloak returned an invalid token response.");
            _token = body["access_token"]!.GetValue<string>();
            var lifetime = body["expires_in"]?.GetValue<int>() ?? 60;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(0, lifetime - 30));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> GetApiClientUuidAsync(Func<Task<string>> load) => _apiClientUuid ??= await load();
}
