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

Console.WriteLine("The medicalapp realm, clients, roles, and development accounts are ready.");

static bool CopyProperties(JsonObject destination, JsonObject source, IEnumerable<string> properties)
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
