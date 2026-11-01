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
    private readonly string _password = Guid.NewGuid().ToString("N");
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

    public HttpClientHandler CreateHandler() => new()
    {
        ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == _certificate.Thumbprint
    };

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var client = new HttpClient(CreateHandler()) { BaseAddress = new Uri(Address) };
        using var tokenResponse = await client.PostAsync("/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "admin-cli",
                ["grant_type"] = "password",
                ["username"] = "admin",
                ["password"] = _password
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var adminToken = await tokenResponse.Content.ReadFromJsonAsync<JsonObject>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken!["access_token"]!.GetValue<string>());

        const string userPath = "/admin/realms/medicalapp/users/01967a92-2ff8-7a00-8000-000000000001";
        using var setPassword = await client.PutAsJsonAsync($"{userPath}/reset-password", new { type = "password", value = _password, temporary = false });
        setPassword.EnsureSuccessStatusCode();
        var user = await client.GetFromJsonAsync<JsonObject>(userPath) ?? throw new InvalidOperationException("Development account is missing.");
        user["requiredActions"] = new JsonArray();
        using var updateUser = await client.PutAsJsonAsync(userPath, user);
        updateUser.EnsureSuccessStatusCode();

        // Give the ID token the API audience so rejection also exercises the access-token type check.
        var clients = await client.GetFromJsonAsync<JsonArray>("/admin/realms/medicalapp/clients?clientId=medicalapp-swagger")
            ?? throw new InvalidOperationException("Swagger client is missing.");
        var clientId = clients.Single()!["id"]!.GetValue<string>();
        var mapperPath = $"/admin/realms/medicalapp/clients/{clientId}/protocol-mappers/models";
        var mappers = await client.GetFromJsonAsync<JsonArray>(mapperPath) ?? throw new InvalidOperationException("Audience mapper is missing.");
        var mapper = mappers.Select(x => x!.AsObject()).Single(x => x["name"]?.GetValue<string>() == "medicalapp-api-audience");
        mapper["config"]!["id.token.claim"] = "true";
        using var updateMapper = await client.PutAsJsonAsync($"{mapperPath}/{mapper["id"]!.GetValue<string>()}", mapper);
        updateMapper.EnsureSuccessStatusCode();
    }

    public async Task<(string AccessToken, string IdToken)> GetTokensAsync()
    {
        using var handler = CreateHandler();
        handler.AllowAutoRedirect = false;
        handler.CookieContainer = new CookieContainer();
        using var browser = new HttpClient(handler) { BaseAddress = new Uri(Address) };
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirectUri = "https://localhost:7255/swagger/oauth2-redirect.html";
        var query = new Dictionary<string, string>
        {
            ["client_id"] = "medicalapp-swagger",
            ["response_type"] = "code",
            ["scope"] = "openid",
            ["redirect_uri"] = redirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge
        };
        var encodedQuery = string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var html = await browser.GetStringAsync($"/realms/medicalapp/protocol/openid-connect/auth?{encodedQuery}");
        var action = Regex.Match(html, "<form[^>]*id=\"kc-form-login\"[^>]*action=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(action))
            throw new InvalidOperationException("Keycloak did not return its login form.");
        using var login = await browser.PostAsync(WebUtility.HtmlDecode(action), new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "development-doctor", ["password"] = _password, ["credentialId"] = "" }));
        if (login.StatusCode != HttpStatusCode.Redirect || login.Headers.Location is not { } location || !location.AbsoluteUri.StartsWith(redirectUri, StringComparison.Ordinal))
        {
            var (stdout, stderr) = await _container.GetLogsAsync();
            var errors = Regex.Matches(stdout + stderr, "error=\"([^\"]+)\"");
            var reason = errors.Count == 0 ? "unknown" : errors[^1].Groups[1].Value;
            throw new InvalidOperationException($"Keycloak did not return an authorization code: status {(int)login.StatusCode}, reason {reason}.");
        }
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query)["code"].ToString();
        using var tokenResponse = await browser.PostAsync("/realms/medicalapp/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "medicalapp-swagger",
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
        await _container.DisposeAsync();
        _certificate.Dispose();
        GC.SuppressFinalize(this);
    }
}
