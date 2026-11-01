using MedicalApp.Common.Authentication;
using MedicalApp.Common.Authorization;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using FastEndpoints.Swagger;
using NSwag;
using NSwag.AspNetCore;
using Microsoft.IdentityModel.Tokens;

namespace MedicalApp.Host.Extensions;

public static class KeycloakAuthenticationExt
{
    public static void AddKeycloakAuthentication(this IHostApplicationBuilder builder)
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        var authority = isDevelopment ? null : ValidateAuthority(builder.Configuration["Authentication:Keycloak:Authority"]);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddKeycloakJwtBearer("keycloak", "medicalapp", options =>
            {
                options.Audience = "medicalapp-api";
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !isDevelopment;
                if (authority is not null)
                    options.Authority = authority;

                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                options.TokenValidationParameters.RoleClaimType = ApiRoles.ClaimType;
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var subject = context.Principal?.FindFirst("sub")?.Value;
                        var type = context.Principal?.FindFirst("typ")?.Value;
                        if (string.IsNullOrWhiteSpace(subject) || type != "Bearer")
                            context.Fail(new SecurityTokenValidationException("A Keycloak access token with a subject is required."));

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddSingleton<IClaimsTransformation, KeycloakRoleClaimsTransformation>();
        builder.Services.AddScoped<IAuthorizationHandler, MappedRoleHandler>();
        builder.Services.AddAuthorization(ProgressPolicies.Configure);

        AddProvisioning(builder, authority);
    }

    private static void AddProvisioning(IHostApplicationBuilder builder, string? authority)
    {
        // Development uses Aspire service discovery. Other environments derive the host from the HTTPS authority.
        var defaultBaseUrl = authority is null
            ? "https+http://keycloak"
            : authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)];

        builder.Services.Configure<KeycloakProvisioningOptions>(builder.Configuration.GetSection(KeycloakProvisioningOptions.SectionName));
        builder.Services.AddSingleton<KeycloakProvisioningTokenCache>();
        builder.Services.AddHttpClient<IIdentityProviderAccounts, KeycloakAccountAdministration>(
            KeycloakAccountAdministration.HttpClientName,
            (services, client) =>
            {
                var options = services.GetRequiredService<IOptions<KeycloakProvisioningOptions>>().Value;
                client.BaseAddress = new Uri((options.BaseUrl ?? defaultBaseUrl).TrimEnd('/') + "/");
            });
    }

    public const string SwaggerClientId = "medicalapp-swagger";
    public const string SwaggerSecurityScheme = "Keycloak";

    /// <summary>
    /// Registers the OpenAPI document with an OAuth2 authorization code scheme for the realm.
    /// Development uses the browser-reachable Keycloak endpoint from Aspire. Other environments use the HTTPS authority.
    /// </summary>
    public static void AddKeycloakSwagger(this IHostApplicationBuilder builder)
    {
        var browserAuthority = GetBrowserAuthority(builder);
        builder.Services.SwaggerDocument(options =>
        {
            options.EnableJWTBearerAuth = false;
            if (browserAuthority is null)
                return;

            options.DocumentSettings = settings => settings.AddAuth(SwaggerSecurityScheme, new OpenApiSecurityScheme
            {
                Type = OpenApiSecuritySchemeType.OAuth2,
                Description = "Sign in through Keycloak with the authorization code flow and PKCE.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = $"{browserAuthority}/protocol/openid-connect/auth",
                        TokenUrl = $"{browserAuthority}/protocol/openid-connect/token",
                        Scopes = new Dictionary<string, string> { ["openid"] = "OpenID Connect sign-in" }
                    }
                }
            });
        });
    }

    /// <summary>
    /// Serves Swagger UI with the public development client. The client's redirect URI is fixed in the realm definition.
    /// </summary>
    public static void UseKeycloakSwaggerGen(this IApplicationBuilder app) =>
        app.UseSwaggerGen(uiConfig: settings =>
        {
            settings.OAuth2Client = new OAuth2ClientSettings
            {
                ClientId = SwaggerClientId,
                AppName = "MedicalApp API",
                UsePkceWithAuthorizationCodeGrant = true
            };
            settings.OAuth2Client.Scopes.Add("openid");
        });

    private static string? GetBrowserAuthority(IHostApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
            return ValidateAuthority(builder.Configuration["Authentication:Keycloak:Authority"]);

        var configured = builder.Configuration["Authentication:Keycloak:BrowserAuthority"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        // Aspire publishes the host-reachable endpoint. Browser and API then agree on the issuer.
        var endpoint = builder.Configuration["services:keycloak:http:0"] ?? builder.Configuration["services:keycloak:https:0"];
        return string.IsNullOrWhiteSpace(endpoint) ? null : $"{endpoint.TrimEnd('/')}/realms/medicalapp";
    }

    private static string ValidateAuthority(string? authority)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.AbsolutePath.TrimEnd('/').EndsWith("/realms/medicalapp", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Authentication:Keycloak:Authority must be an HTTPS URL for the medicalapp realm.");

        return uri.AbsoluteUri.TrimEnd('/');
    }
}
