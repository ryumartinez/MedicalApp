using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Identity;

public sealed class KeycloakContainer : IAsyncDisposable
{
    public const string DoctorUserId = "01967a92-2ff8-7a00-8000-000000000001";
    public const string PatientUserId = "01967a92-2ff8-7a00-8000-000000000002";
    public const string AdministratorUserId = "01967a92-2ff8-7a00-8000-000000000003";

    private readonly string _password = Guid.NewGuid().ToString("N");
    private HttpClient _admin = null!;
    private readonly IContainer _container;
    private readonly X509Certificate2 _certificate;

    public KeycloakContainer()
    {
        var realmPath = Path.Combine(AppContext.BaseDirectory, "medicalapp-realm.json");
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        _certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        _container = new ContainerBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.6.0")
            .WithPortBinding(8443, true)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", _password)
            .WithEnvironment("KC_HTTPS_KEY_STORE_FILE", "/opt/keycloak/conf/test.p12")
            .WithEnvironment("KC_HTTPS_KEY_STORE_PASSWORD", _password)
            .WithCommand("start-dev", "--import-realm")
            .WithResourceMapping(new FileInfo(realmPath), "/opt/keycloak/data/import")
            .WithResourceMapping(_certificate.Export(X509ContentType.Pfx, _password), "/opt/keycloak/conf/test.p12")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Keycloak 26.6.0 on JVM"))
            .Build();
    }

    public string Address => $"https://{_container.Hostname}:{_container.GetMappedPublicPort(8443)}";

    public string Issuer => $"{Address}/realms/medicalapp";

    public string ProvisioningSecret { get; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    /// <summary>
    /// A client authenticated as the Keycloak master administrator, for test setup and assertions.
    /// </summary>
    public HttpClient Admin => _admin;

    public HttpClientHandler CreateHandler() => new()
    {
        ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == _certificate.Thumbprint
    };

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _admin = new HttpClient(new AdminTokenHandler(this) { InnerHandler = CreateHandler() }) { BaseAddress = new Uri(Address) };

        // Run the same reconciliation as local orchestration, including the provisioning client.
        var realm = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "medicalapp-realm.json")))!.AsObject();
        await KeycloakInitializer.KeycloakRealmReconciler.ReconcileAsync(_admin, realm, ProvisioningSecret);

        foreach (var userId in new[] { DoctorUserId, PatientUserId, AdministratorUserId })
            await SetPasswordAsync(userId);

        // Give the ID token the API audience so rejection also exercises the access-token type check.
        var clients = await _admin.GetFromJsonAsync<JsonArray>("/admin/realms/medicalapp/clients?clientId=medicalapp-swagger")
            ?? throw new InvalidOperationException("Swagger client is missing.");
        var clientId = clients.Single()!["id"]!.GetValue<string>();
        var mapperPath = $"/admin/realms/medicalapp/clients/{clientId}/protocol-mappers/models";
        var mappers = await _admin.GetFromJsonAsync<JsonArray>(mapperPath) ?? throw new InvalidOperationException("Audience mapper is missing.");
        var mapper = mappers.Select(x => x!.AsObject()).Single(x => x["name"]?.GetValue<string>() == "medicalapp-api-audience");
        mapper["config"]!["id.token.claim"] = "true";
        using var updateMapper = await _admin.PutAsJsonAsync($"{mapperPath}/{mapper["id"]!.GetValue<string>()}", mapper);
        updateMapper.EnsureSuccessStatusCode();
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient(CreateHandler()) { BaseAddress = new Uri(Address) };
        using var tokenResponse = await client.PostAsync("/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "admin-cli",
                ["grant_type"] = "password",
                ["username"] = "admin",
                ["password"] = _password
            }), cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var adminToken = await tokenResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return adminToken!["access_token"]!.GetValue<string>();
    }

    // Master realm administrator tokens expire after one minute, so each request gets a recent one.
    private sealed class AdminTokenHandler(KeycloakContainer keycloak) : DelegatingHandler
    {
        private string? _token;
        private DateTimeOffset _issuedAt;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_token is null || DateTimeOffset.UtcNow - _issuedAt > TimeSpan.FromSeconds(30))
            {
                _token = await keycloak.GetAdminTokenAsync(cancellationToken);
                _issuedAt = DateTimeOffset.UtcNow;
            }

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Gives the account the shared test password and clears required actions so it can sign in.
    /// </summary>
    public async Task SetPasswordAsync(string userId, string realm = "medicalapp")
    {
        var userPath = $"/admin/realms/{realm}/users/{userId}";
        using var setPassword = await _admin.PutAsJsonAsync($"{userPath}/reset-password", new { type = "password", value = _password, temporary = false });
        setPassword.EnsureSuccessStatusCode();
        var user = await _admin.GetFromJsonAsync<JsonObject>(userPath) ?? throw new InvalidOperationException("The account is missing.");
        user["requiredActions"] = new JsonArray();
        using var updateUser = await _admin.PutAsJsonAsync(userPath, user);
        updateUser.EnsureSuccessStatusCode();
    }

    public Task<(string AccessToken, string IdToken)> GetTokensAsync() => GetTokensAsync("development-doctor");

    private readonly Dictionary<string, string> _testUsers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _usersLock = new(1, 1);

    /// <summary>
    /// Returns the subject of a test account with the given API roles and the shared password, creating it on first use.
    /// </summary>
    public async Task<string> EnsureUserAsync(string username, params string[] roles)
    {
        await _usersLock.WaitAsync();
        try
        {
            if (_testUsers.TryGetValue(username, out var existing))
                return existing;

            using var create = await _admin.PostAsJsonAsync("/admin/realms/medicalapp/users", new JsonObject
            {
                ["username"] = username,
                ["email"] = $"{username}@example.invalid",
                ["firstName"] = "Test",
                ["lastName"] = username,
                ["enabled"] = true,
                ["emailVerified"] = true
            });
            create.EnsureSuccessStatusCode();
            var subject = create.Headers.Location!.Segments[^1];
            await SetPasswordAsync(subject);

            var clients = await _admin.GetFromJsonAsync<JsonArray>("/admin/realms/medicalapp/clients?clientId=medicalapp-api");
            var apiId = clients!.Single()!["id"]!.GetValue<string>();
            var mapped = new JsonArray();
            foreach (var role in roles)
                mapped.Add(await _admin.GetFromJsonAsync<JsonObject>($"/admin/realms/medicalapp/clients/{apiId}/roles/{role}"));
            using var assign = await _admin.PostAsJsonAsync($"/admin/realms/medicalapp/users/{subject}/role-mappings/clients/{apiId}", mapped);
            assign.EnsureSuccessStatusCode();

            _testUsers[username] = subject;
            return subject;
        }
        finally
        {
            _usersLock.Release();
        }
    }

    /// <summary>
    /// Adds a public authorization code client. Without the audience mapper and roles scope, its tokens lack the API audience.
    /// </summary>
    public async Task CreateTestClientAsync(string clientId, bool apiAudience, int? accessTokenLifespanSeconds = null)
    {
        var attributes = new JsonObject { ["pkce.code.challenge.method"] = "S256" };
        if (accessTokenLifespanSeconds is { } lifespan)
            attributes["access.token.lifespan"] = lifespan.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var client = new JsonObject
        {
            ["clientId"] = clientId,
            ["publicClient"] = true,
            ["standardFlowEnabled"] = true,
            ["directAccessGrantsEnabled"] = false,
            ["redirectUris"] = new JsonArray(RedirectUri),
            ["attributes"] = attributes,
            ["defaultClientScopes"] = apiAudience ? new JsonArray("basic", "roles") : new JsonArray("basic"),
            ["optionalClientScopes"] = new JsonArray()
        };
        if (apiAudience)
        {
            client["protocolMappers"] = new JsonArray(new JsonObject
            {
                ["name"] = "medicalapp-api-audience",
                ["protocol"] = "openid-connect",
                ["protocolMapper"] = "oidc-audience-mapper",
                ["config"] = new JsonObject { ["included.client.audience"] = "medicalapp-api", ["access.token.claim"] = "true" }
            });
        }
        using var create = await _admin.PostAsJsonAsync("/admin/realms/medicalapp/clients", client);
        create.EnsureSuccessStatusCode();
        await RemoveDefaultScopesAsync("medicalapp", clientId, apiAudience ? ["basic", "roles"] : ["basic"]);
    }

    /// <summary>
    /// Creates another realm from the local definition. Its tokens have another issuer and signing keys.
    /// </summary>
    public async Task CreateRealmCopyAsync(string realm)
    {
        var definition = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "medicalapp-realm.json")))!.AsObject();
        definition["realm"] = realm;
        // User IDs are unique across realms, so the copied accounts receive new IDs.
        foreach (var user in definition["users"]!.AsArray())
            user!.AsObject().Remove("id");
        using var create = await _admin.PostAsJsonAsync("/admin/realms", definition);
        create.EnsureSuccessStatusCode();
        var users = await _admin.GetFromJsonAsync<JsonArray>($"/admin/realms/{realm}/users?username=development-doctor&exact=true");
        await SetPasswordAsync(users!.Single()!["id"]!.GetValue<string>(), realm);
    }

    /// <summary>
    /// Adds a higher-priority RSA key so Keycloak signs new tokens with a new key ID.
    /// </summary>
    public async Task RotateSigningKeyAsync()
    {
        var realm = await _admin.GetFromJsonAsync<JsonObject>("/admin/realms/medicalapp") ?? throw new InvalidOperationException("Realm is missing.");
        using var create = await _admin.PostAsJsonAsync("/admin/realms/medicalapp/components", new JsonObject
        {
            ["name"] = $"rotated-{Guid.NewGuid():N}",
            ["providerId"] = "rsa-generated",
            ["providerType"] = "org.keycloak.keys.KeyProvider",
            ["parentId"] = realm["id"]!.GetValue<string>(),
            ["config"] = new JsonObject
            {
                ["priority"] = new JsonArray("1000"),
                ["keySize"] = new JsonArray("2048"),
                ["active"] = new JsonArray("true"),
                ["enabled"] = new JsonArray("true")
            }
        });
        create.EnsureSuccessStatusCode();
    }

    // Keycloak adds realm default scopes to new clients. Keep only the requested ones.
    private async Task RemoveDefaultScopesAsync(string realm, string clientId, string[] keep)
    {
        var clients = await _admin.GetFromJsonAsync<JsonArray>($"/admin/realms/{realm}/clients?clientId={clientId}");
        var id = clients!.Single()!["id"]!.GetValue<string>();
        foreach (var type in new[] { "default-client-scopes", "optional-client-scopes" })
        {
            var scopes = await _admin.GetFromJsonAsync<JsonArray>($"/admin/realms/{realm}/clients/{id}/{type}");
            foreach (var scope in scopes!.Select(x => x!.AsObject()).Where(x => type != "default-client-scopes" || !keep.Contains(x["name"]!.GetValue<string>())))
            {
                using var remove = await _admin.DeleteAsync($"/admin/realms/{realm}/clients/{id}/{type}/{scope["id"]!.GetValue<string>()}");
                remove.EnsureSuccessStatusCode();
            }
        }
    }

    public const string RedirectUri = "https://localhost:7255/swagger/oauth2-redirect.html";

    public async Task<(string AccessToken, string IdToken)> GetTokensAsync(string username, string clientId = "medicalapp-swagger", string realm = "medicalapp")
    {
        using var handler = CreateHandler();
        handler.AllowAutoRedirect = false;
        handler.CookieContainer = new CookieContainer();
        using var browser = new HttpClient(handler) { BaseAddress = new Uri(Address) };
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirectUri = RedirectUri;
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["scope"] = "openid",
            ["redirect_uri"] = redirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge
        };
        var encodedQuery = string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var html = await browser.GetStringAsync($"/realms/{realm}/protocol/openid-connect/auth?{encodedQuery}");
        var action = Regex.Match(html, "<form[^>]*id=\"kc-form-login\"[^>]*action=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(action))
            throw new InvalidOperationException("Keycloak did not return its login form.");
        using var login = await browser.PostAsync(WebUtility.HtmlDecode(action), new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = username, ["password"] = _password, ["credentialId"] = "" }));
        if (login.StatusCode != HttpStatusCode.Redirect || login.Headers.Location is not { } location || !location.AbsoluteUri.StartsWith(redirectUri, StringComparison.Ordinal))
        {
            var (stdout, stderr) = await _container.GetLogsAsync();
            var errors = Regex.Matches(stdout + stderr, "error=\"([^\"]+)\"");
            var reason = errors.Count == 0 ? "unknown" : errors[^1].Groups[1].Value;
            throw new InvalidOperationException($"Keycloak did not return an authorization code: status {(int)login.StatusCode}, reason {reason}.");
        }
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query)["code"].ToString();
        using var tokenResponse = await browser.PostAsync($"/realms/{realm}/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonObject>() ?? throw new InvalidOperationException("Keycloak did not return tokens.");
        return (tokens["access_token"]!.GetValue<string>(), tokens["id_token"]!.GetValue<string>());
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async ValueTask DisposeAsync()
    {
        _admin?.Dispose();
        await _container.DisposeAsync();
        _certificate.Dispose();
        GC.SuppressFinalize(this);
    }
}
