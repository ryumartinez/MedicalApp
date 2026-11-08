using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace KeycloakInitializer;

/// <summary>
/// Reconciles the local medicalapp realm through the Keycloak administrator API.
/// Creates missing state and updates managed settings without removing accounts or unrelated configuration.
/// </summary>
public static class KeycloakRealmReconciler
{
    public const string ProvisioningClientId = "medicalapp-provisioning";
    public const string ProvisioningAttribute = "medicalapp_provisioning_id";

    // The provisioning service account manages users and reads client roles. It cannot change realm settings or its own roles.
    private static readonly string[] ProvisioningRoles = ["manage-users", "view-users", "query-users", "view-clients", "query-clients"];

    /// <param name="client">A client authenticated as the Keycloak administrator.</param>
    /// <param name="realm">The desired realm definition.</param>
    /// <param name="provisioningSecret">The secret the API uses for the provisioning client.</param>
    /// <param name="developmentPassword">The password given to realm-file development accounts that have none.</param>
    public static async Task ReconcileAsync(HttpClient client, JsonObject realm, string provisioningSecret, string developmentPassword)
    {
        if (string.IsNullOrWhiteSpace(provisioningSecret) || provisioningSecret.Length < 32)
            throw new InvalidOperationException("The provisioning client secret must contain at least 32 characters.");
        if (string.IsNullOrWhiteSpace(developmentPassword) || developmentPassword.Length < 8)
            throw new InvalidOperationException("The development account password must contain at least 8 characters.");

        const string realmPath = "/admin/realms/medicalapp";
        using var realmResponse = await client.GetAsync(realmPath);
        if (realmResponse.StatusCode == HttpStatusCode.NotFound)
        {
            using var createRealm = await client.PostAsJsonAsync("/admin/realms", realm);
            createRealm.EnsureSuccessStatusCode();
        }
        else
        {
            realmResponse.EnsureSuccessStatusCode();
            var current = await realmResponse.Content.ReadFromJsonAsync<JsonObject>()
                ?? throw new InvalidOperationException("Keycloak returned an invalid realm.");
            var realmProperties = new[]
            {
                "enabled", "registrationAllowed", "accessTokenLifespan", "ssoSessionIdleTimeout", "ssoSessionMaxLifespan",
                "ssoSessionIdleTimeoutRememberMe", "ssoSessionMaxLifespanRememberMe", "clientSessionIdleTimeout", "clientSessionMaxLifespan"
            };
            if (CopyProperties(current, realm, realmProperties))
            {
                using var update = await client.PutAsJsonAsync(realmPath, current);
                update.EnsureSuccessStatusCode();
            }
        }

        var clientIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in realm["clients"]!.AsArray())
        {
            var desired = item!.AsObject();
            var name = desired["clientId"]!.GetValue<string>();
            var path = $"{realmPath}/clients";
            var matches = await client.GetFromJsonAsync<JsonArray>($"{path}?clientId={Uri.EscapeDataString(name)}")
                ?? throw new InvalidOperationException("Keycloak returned an invalid client list.");
            var current = matches.Select(node => node!.AsObject())
                .SingleOrDefault(node => node["clientId"]?.GetValue<string>() == name);
            if (current is null)
            {
                using var create = await client.PostAsJsonAsync(path, desired);
                create.EnsureSuccessStatusCode();
                matches = await client.GetFromJsonAsync<JsonArray>($"{path}?clientId={Uri.EscapeDataString(name)}")
                    ?? throw new InvalidOperationException("Keycloak returned an invalid client list.");
                current = matches.Select(node => node!.AsObject())
                    .Single(node => node["clientId"]?.GetValue<string>() == name);
            }
            var id = current["id"]!.GetValue<string>();
            clientIds[name] = id;
            var clientProperties = new[]
            {
                "enabled", "protocol", "bearerOnly", "publicClient", "standardFlowEnabled", "directAccessGrantsEnabled",
                "implicitFlowEnabled", "serviceAccountsEnabled", "fullScopeAllowed", "redirectUris", "webOrigins"
            };
            var changed = CopyProperties(current, desired, clientProperties);
            if (desired["attributes"] is JsonObject desiredAttributes)
            {
                if (current["attributes"] is not JsonObject attributes)
                {
                    attributes = new JsonObject();
                    current["attributes"] = attributes;
                }
                changed |= CopyProperties(attributes, desiredAttributes, desiredAttributes.Select(x => x.Key));
            }
            if (changed)
            {
                using var update = await client.PutAsJsonAsync($"{path}/{Uri.EscapeDataString(id)}", current);
                update.EnsureSuccessStatusCode();
            }

            var availableScopes = desired["defaultClientScopes"] is JsonArray
                ? await client.GetFromJsonAsync<JsonArray>($"{realmPath}/client-scopes")
                : null;
            foreach (var scopeType in new[] { "default-client-scopes", "optional-client-scopes" })
            {
                var scopesPath = $"{path}/{Uri.EscapeDataString(id)}/{scopeType}";
                var scopes = await client.GetFromJsonAsync<JsonArray>(scopesPath)
                    ?? throw new InvalidOperationException("Keycloak returned an invalid client scope list.");
                foreach (var scope in scopes.Select(node => node!.AsObject()).Where(node => node["name"]?.GetValue<string>() == "offline_access"))
                {
                    using var remove = await client.DeleteAsync($"{scopesPath}/{Uri.EscapeDataString(scope["id"]!.GetValue<string>())}");
                    remove.EnsureSuccessStatusCode();
                }

                if (scopeType != "default-client-scopes" || desired["defaultClientScopes"] is not JsonArray requiredScopes)
                    continue;
                foreach (var scopeName in requiredScopes.Select(node => node!.GetValue<string>()))
                {
                    if (scopes.Any(node => node?["name"]?.GetValue<string>() == scopeName))
                        continue;
                    var scope = availableScopes?.Select(node => node!.AsObject())
                        .SingleOrDefault(node => node["name"]?.GetValue<string>() == scopeName)
                        ?? throw new InvalidOperationException($"Required Keycloak client scope '{scopeName}' is missing.");
                    using var add = await client.PutAsync($"{scopesPath}/{Uri.EscapeDataString(scope["id"]!.GetValue<string>())}", null);
                    add.EnsureSuccessStatusCode();
                }
            }

            if (desired["protocolMappers"] is not JsonArray desiredMappers)
                continue;
            var mapperPath = $"{path}/{Uri.EscapeDataString(id)}/protocol-mappers/models";
            var mappers = await client.GetFromJsonAsync<JsonArray>(mapperPath)
                ?? throw new InvalidOperationException("Keycloak returned an invalid mapper list.");
            foreach (var mapperItem in desiredMappers)
            {
                var desiredMapper = mapperItem!.AsObject();
                var mapperName = desiredMapper["name"]!.GetValue<string>();
                var mapper = mappers.Select(node => node!.AsObject())
                    .SingleOrDefault(node => node["name"]?.GetValue<string>() == mapperName);
                if (mapper is null)
                {
                    using var createMapper = await client.PostAsJsonAsync(mapperPath, desiredMapper);
                    createMapper.EnsureSuccessStatusCode();
                    continue;
                }
                var mapperId = mapper["id"]!.GetValue<string>();
                if (CopyProperties(mapper, desiredMapper, ["protocol", "protocolMapper", "consentRequired", "config"]))
                {
                    using var updateMapper = await client.PutAsJsonAsync($"{mapperPath}/{Uri.EscapeDataString(mapperId)}", mapper);
                    updateMapper.EnsureSuccessStatusCode();
                }
            }
        }

        var apiClientId = Uri.EscapeDataString(clientIds["medicalapp-api"]);
        foreach (var roleItem in realm["roles"]!["client"]!["medicalapp-api"]!.AsArray())
        {
            var role = roleItem!.AsObject();
            var roleName = role["name"]!.GetValue<string>();
            var rolesPath = $"{realmPath}/clients/{apiClientId}/roles";
            using var roleResponse = await client.GetAsync($"{rolesPath}/{Uri.EscapeDataString(roleName)}");
            if (roleResponse.StatusCode == HttpStatusCode.NotFound)
            {
                using var createRole = await client.PostAsJsonAsync(rolesPath, role);
                createRole.EnsureSuccessStatusCode();
            }
            else
            {
                roleResponse.EnsureSuccessStatusCode();
            }
        }

        foreach (var userItem in realm["users"]!.AsArray())
        {
            var user = userItem!.AsObject();
            var id = user["id"]!.GetValue<string>();
            using var userResponse = await client.GetAsync($"{realmPath}/users/{Uri.EscapeDataString(id)}");
            if (userResponse.IsSuccessStatusCode)
                continue;
            if (userResponse.StatusCode != HttpStatusCode.NotFound)
                userResponse.EnsureSuccessStatusCode();

            var username = user["username"]!.GetValue<string>();
            var matches = await client.GetFromJsonAsync<JsonArray>($"{realmPath}/users?username={Uri.EscapeDataString(username)}&exact=true")
                ?? throw new InvalidOperationException("Keycloak returned an invalid user list.");
            if (matches.Count != 0)
                throw new InvalidOperationException($"The development account '{username}' already exists with another subject.");
            var import = new JsonObject
            {
                ["ifResourceExists"] = "FAIL",
                ["users"] = new JsonArray(user.DeepClone())
            };
            using var createUser = await client.PostAsJsonAsync($"{realmPath}/partialImport", import);
            createUser.EnsureSuccessStatusCode();
            using var createdUser = await client.GetAsync($"{realmPath}/users/{Uri.EscapeDataString(id)}");
            createdUser.EnsureSuccessStatusCode();
        }

        foreach (var userItem in realm["users"]!.AsArray())
            await EnsureDevelopmentPasswordAsync(client, realmPath, userItem!["id"]!.GetValue<string>(), developmentPassword);

        await EnsureProvisioningAttributeAsync(client, realmPath);
        await EnsureProvisioningClientAsync(client, realmPath, clientIds[ProvisioningClientId], provisioningSecret);
    }

    // Sets the password only when the account has none, so passwords that developers changed are kept.
    private static async Task EnsureDevelopmentPasswordAsync(HttpClient client, string realmPath, string userId, string password)
    {
        var userPath = $"{realmPath}/users/{Uri.EscapeDataString(userId)}";
        var credentials = await client.GetFromJsonAsync<JsonArray>($"{userPath}/credentials")
            ?? throw new InvalidOperationException("Keycloak returned an invalid credential list.");
        if (credentials.Any(node => node?["type"]?.GetValue<string>() == "password"))
            return;

        using var reset = await client.PutAsJsonAsync($"{userPath}/reset-password",
            new JsonObject { ["type"] = "password", ["value"] = password, ["temporary"] = false });
        reset.EnsureSuccessStatusCode();

        // Keep any other required actions a developer added.
        var user = await client.GetFromJsonAsync<JsonObject>(userPath)
            ?? throw new InvalidOperationException("Keycloak returned an invalid user.");
        if (user["requiredActions"] is not JsonArray actions)
            return;
        var remaining = new JsonArray(actions.Where(node => node?.GetValue<string>() != "UPDATE_PASSWORD")
            .Select(node => node?.DeepClone()).ToArray());
        if (remaining.Count == actions.Count)
            return;
        user["requiredActions"] = remaining;
        using var update = await client.PutAsJsonAsync(userPath, user);
        update.EnsureSuccessStatusCode();
    }

    private static async Task EnsureProvisioningAttributeAsync(HttpClient client, string realmPath)
    {
        var profile = await client.GetFromJsonAsync<JsonObject>($"{realmPath}/users/profile")
            ?? throw new InvalidOperationException("Keycloak returned an invalid user profile.");
        var attributes = profile["attributes"]?.AsArray() ?? throw new InvalidOperationException("Keycloak returned an invalid user profile.");
        if (attributes.Any(node => node?["name"]?.GetValue<string>() == ProvisioningAttribute))
            return;

        // Only administrators can read or change the marker. It identifies accounts created by API provisioning.
        attributes.Add(new JsonObject
        {
            ["name"] = ProvisioningAttribute,
            ["displayName"] = "MedicalApp provisioning ID",
            ["multivalued"] = false,
            ["permissions"] = new JsonObject { ["view"] = new JsonArray("admin"), ["edit"] = new JsonArray("admin") },
            ["validations"] = new JsonObject { ["length"] = new JsonObject { ["max"] = 64 } }
        });
        using var update = await client.PutAsJsonAsync($"{realmPath}/users/profile", profile);
        update.EnsureSuccessStatusCode();
    }

    private static async Task EnsureProvisioningClientAsync(HttpClient client, string realmPath, string clientId, string secret)
    {
        var clientPath = $"{realmPath}/clients/{Uri.EscapeDataString(clientId)}";
        var current = await client.GetFromJsonAsync<JsonObject>($"{clientPath}/client-secret");
        if (current?["value"]?.GetValue<string>() != secret)
        {
            var representation = await client.GetFromJsonAsync<JsonObject>(clientPath)
                ?? throw new InvalidOperationException("Keycloak returned an invalid provisioning client.");
            representation["secret"] = secret;
            using var update = await client.PutAsJsonAsync(clientPath, representation);
            update.EnsureSuccessStatusCode();
        }

        var serviceAccount = await client.GetFromJsonAsync<JsonObject>($"{clientPath}/service-account-user")
            ?? throw new InvalidOperationException("The provisioning client has no service account.");
        var serviceAccountId = Uri.EscapeDataString(serviceAccount["id"]!.GetValue<string>());
        var managementClients = await client.GetFromJsonAsync<JsonArray>($"{realmPath}/clients?clientId=realm-management")
            ?? throw new InvalidOperationException("Keycloak returned an invalid client list.");
        var managementId = Uri.EscapeDataString(managementClients.Single()!["id"]!.GetValue<string>());
        var mappingPath = $"{realmPath}/users/{serviceAccountId}/role-mappings/clients/{managementId}";
        var assigned = await client.GetFromJsonAsync<JsonArray>(mappingPath)
            ?? throw new InvalidOperationException("Keycloak returned an invalid role mapping.");
        var missing = new JsonArray();
        foreach (var roleName in ProvisioningRoles)
        {
            if (assigned.Any(node => node?["name"]?.GetValue<string>() == roleName))
                continue;
            var role = await client.GetFromJsonAsync<JsonObject>($"{realmPath}/clients/{managementId}/roles/{roleName}")
                ?? throw new InvalidOperationException($"Keycloak role '{roleName}' is missing.");
            missing.Add(role);
        }
        if (missing.Count == 0)
            return;
        using var add = await client.PostAsJsonAsync(mappingPath, missing);
        add.EnsureSuccessStatusCode();
    }

    private static bool CopyProperties(JsonObject destination, JsonObject source, IEnumerable<string> properties)
    {
        var changed = false;
        foreach (var name in properties)
        {
            if (!source.ContainsKey(name) || JsonNode.DeepEquals(destination[name], source[name]))
                continue;
            destination[name] = source[name]?.DeepClone();
            changed = true;
        }
        return changed;
    }
}
