using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Identity;
using MedicalApp.Host.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MigrationService.Initializers;
using OpenIddict.Abstractions;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Identity;

/// <summary>
/// Accounts and tokens from the API's own authorization server. Tokens come from the real authorization code flow:
/// the sign-in page, the code redirect, and the token exchange with a PKCE verifier.
/// </summary>
public sealed partial class TestIdentity(WebApplicationFactory<MedicalApp.Common.Interfaces.IWebApiMarker> factory)
{
    public const string Issuer = "https://localhost/";
    public const string Password = "integration-test-password";
    public const string RedirectUri = "https://localhost:7255/swagger/oauth2-redirect.html";
    public const string SwaggerClientId = IdentityClientInitializer.SwaggerClientId;
    public const string ApiScopes = $"{OpenIddictConstants.Scopes.OpenId} {ApiRoles.Resource}";

    public static readonly string DoctorUserId = DevelopmentAccountInitializer.DoctorUserId.ToString();
    public static readonly string PatientUserId = DevelopmentAccountInitializer.PatientUserId.ToString();
    public static readonly string AdministratorUserId = DevelopmentAccountInitializer.AdministratorUserId.ToString();

    /// <summary>
    /// A signing key that test hosts keep for validation only, so tokens signed before a rotation stay valid.
    /// </summary>
    public static readonly Microsoft.IdentityModel.Tokens.RsaSecurityKey RetiredSigningKey = new(RSA.Create(2048)) { KeyId = "retired-test-key" };

    private readonly Dictionary<string, string> _users = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Registers the scope, roles, and clients, and creates the development accounts with <see cref="Password"/>.
    /// Run after every database reset, which removes accounts but keeps client registrations and roles.
    /// </summary>
    public async Task SeedAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _users.Clear();
            using var scope = factory.Services.CreateScope();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{IdentityClientInitializer.SwaggerRedirectUrisKey}:0"] = RedirectUri,
                [DevelopmentAccountInitializer.PasswordKey] = Password
            }).Build();
            var provider = scope.ServiceProvider;
            await ActivatorUtilities.CreateInstance<IdentityClientInitializer>(provider, configuration).InitializeAsync(CancellationToken.None);
            await ActivatorUtilities.CreateInstance<DevelopmentAccountInitializer>(provider, configuration).SeedAsync(CancellationToken.None);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns the subject of an enabled test account with the given API roles and <see cref="Password"/>, creating it on first use.
    /// </summary>
    public async Task<string> EnsureUserAsync(string username, params string[] roles)
    {
        await _lock.WaitAsync();
        try
        {
            if (_users.TryGetValue(username, out var existing))
                return existing;

            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = username,
                Email = $"{username}@example.invalid",
                Enabled = true
            };
            Check(await users.CreateAsync(user, Password));
            if (roles.Length != 0)
                Check(await users.AddToRolesAsync(user, roles));

            _users[username] = user.Subject;
            return user.Subject;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gives an account <see cref="Password"/>, for accounts that provisioning created without one.
    /// </summary>
    public async Task SetPasswordAsync(string subject, string password = Password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(subject) ?? throw new InvalidOperationException($"Account {subject} does not exist.");
        if (await users.HasPasswordAsync(user))
            Check(await users.RemovePasswordAsync(user));
        Check(await users.AddPasswordAsync(user, password));
    }

    /// <summary>
    /// Adds a public code-flow client. Without the API scope permission, its tokens cannot carry the API audience.
    /// </summary>
    public async Task CreateTestClientAsync(string clientId, bool apiScope)
    {
        using var scope = factory.Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applications.FindByClientIdAsync(clientId) is not null)
            return;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris = { new Uri(RedirectUri) },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code
            },
            Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange }
        };
        if (apiScope)
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + ApiRoles.Resource);
        await applications.CreateAsync(descriptor);
    }

    public async Task<(string AccessToken, string IdToken)> GetTokensAsync(string username, string clientId = SwaggerClientId,
        string? password = null, string scopes = ApiScopes)
    {
        using var browser = CreateBrowser();
        var pkce = Pkce.Create();
        var code = await SignInForCodeAsync(browser, AuthorizeUrl(clientId, pkce, scopes), username, password ?? Password);
        var tokens = await ExchangeCodeAsync(browser, clientId, code, pkce);
        return (tokens["access_token"]!.GetValue<string>(), tokens["id_token"]?.GetValue<string>() ?? "");
    }

    /// <summary>
    /// A client that behaves like a browser on the authorization server: HTTPS, cookies, and no automatic redirects.
    /// </summary>
    public HttpClient CreateBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(Issuer),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    public static string AuthorizeUrl(string clientId, Pkce pkce, string scopes = ApiScopes, string redirectUri = RedirectUri,
        string? prompt = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["scope"] = scopes,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = pkce.Method,
            ["state"] = Guid.NewGuid().ToString("N"),
            ["prompt"] = prompt
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/" + IdentityServerExt.AuthorizationPath, query);
    }

    /// <summary>
    /// Starts an authorization request, signs in on the sign-in page, and returns the code from the client redirect.
    /// </summary>
    public static async Task<string> SignInForCodeAsync(HttpClient browser, string authorizeUrl, string username, string password)
    {
        using var authorize = await browser.GetAsync(authorizeUrl);
        var location = RedirectTarget(authorize);
        if (!location.AbsolutePath.Equals(IdentityServerExt.LoginPath, StringComparison.OrdinalIgnoreCase))
            return CodeFrom(location);

        using var login = await SubmitLoginAsync(browser, location, username, password);
        if (login.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException($"Sign-in failed: {(int)login.StatusCode} {LoginError(await login.Content.ReadAsStringAsync())}");

        using var resumed = await browser.GetAsync(RedirectTarget(login));
        return CodeFrom(RedirectTarget(resumed));
    }

    /// <summary>
    /// Opens the sign-in page at <paramref name="loginUrl"/> and posts the credentials with its antiforgery token.
    /// </summary>
    public static async Task<HttpResponseMessage> SubmitLoginAsync(HttpClient browser, Uri loginUrl, string username, string password)
    {
        var page = await browser.GetStringAsync(loginUrl);
        var form = new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = password,
            ["ReturnUrl"] = WebUtility.HtmlDecode(HiddenInput(page, "ReturnUrl")),
            ["__RequestVerificationToken"] = HiddenInput(page, "__RequestVerificationToken")
        };
        return await browser.PostAsync(loginUrl, new FormUrlEncodedContent(form));
    }

    public static async Task<JsonObject> ExchangeCodeAsync(HttpClient browser, string clientId, string code, Pkce pkce,
        string redirectUri = RedirectUri)
    {
        using var response = await PostTokenAsync(browser, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = pkce.Verifier
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {body}");
        return body!;
    }

    public static Task<HttpResponseMessage> PostTokenAsync(HttpClient browser, Dictionary<string, string> form) =>
        browser.PostAsync("/" + IdentityServerExt.TokenPath, new FormUrlEncodedContent(form));

    public static Uri RedirectTarget(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther))
            throw new InvalidOperationException($"Expected a redirect but got {(int)response.StatusCode}: {response.Content.ReadAsStringAsync().Result}");
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location : new Uri(new Uri(Issuer), location);
    }

    public static string CodeFrom(Uri redirect)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(redirect.Query);
        if (query.TryGetValue("code", out var code))
            return code.ToString();
        throw new InvalidOperationException($"The redirect has no code: {redirect}");
    }

    public static string? LoginError(string page) =>
        ErrorPattern().Match(page) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;

    private static string HiddenInput(string page, string name)
    {
        var match = Regex.Match(page, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        if (!match.Success)
            match = Regex.Match(page, $"<input[^>]*value=\"([^\"]*)\"[^>]*name=\"{Regex.Escape(name)}\"");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    [GeneratedRegex("<p class=\"error\" role=\"alert\">([^<]*)</p>")]
    private static partial Regex ErrorPattern();

    public sealed record Pkce(string Verifier, string Challenge, string Method)
    {
        public static Pkce Create()
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            return new Pkce(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), "S256");
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
