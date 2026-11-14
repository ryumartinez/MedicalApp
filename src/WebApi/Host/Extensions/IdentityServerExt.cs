using MedicalApp.Common.Authentication;
using MedicalApp.Common.Authorization;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using FastEndpoints.Swagger;
using NSwag;
using NSwag.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.Host.Extensions;

/// <summary>
/// The API is its own authorization server. OpenIddict issues tokens for Identity accounts, and OpenIddict validation
/// authenticates API calls with the same keys. The sign-in cookie is used only by the sign-in pages and connect endpoints.
/// </summary>
public static class IdentityServerExt
{
    public const string AuthorizationPath = "connect/authorize";
    public const string TokenPath = "connect/token";
    public const string EndSessionPath = "connect/endsession";
    public const string LoginPath = "/account/login";
    public const string SignedOutPath = "/account/logout";
    public const string ErrorPath = "/account/error";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);

    public static void AddIdentityServer(this IHostApplicationBuilder builder)
    {
        var issuer = IdentityIssuer.Resolve(builder.Configuration, builder.Environment);
        var services = builder.Services;

        services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetIssuer(issuer)
                    .SetAuthorizationEndpointUris(AuthorizationPath)
                    .SetTokenEndpointUris(TokenPath)
                    .SetEndSessionEndpointUris(EndSessionPath);

                // Code flow with PKCE only. Password, implicit, client credentials, and refresh tokens stay off.
                server.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .Configure(options => options.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
                server.RegisterScopes(Scopes.OpenId, ApiRoles.Resource);

                server.SetAccessTokenLifetime(AccessTokenLifetime)
                    .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));

                server.AddServerCredentials(builder.Configuration, builder.Environment);

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.AddAudiences(ApiRoles.Resource);
                validation.UseAspNetCore();
                validation.AddEventHandler<OpenIddictValidationEvents.ValidateTokenContext>(handler => handler
                    .UseInlineHandler(RequireSubject)
                    .SetOrder(int.MaxValue - 100_000)
                    .SetType(OpenIddictValidationHandlerType.Custom));
            });

        new IdentityBuilder(typeof(ApplicationUser), typeof(IdentityRole<Guid>), services).AddSignInManager<AppSignInManager>();

        // AddIdentity is not used: it would make the cookie the default scheme, and the cookie would then authorize API calls.
        services.AddAuthentication(options => options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(ConfigureSignInCookie));

        // A disabled account or a new password changes the security stamp. Check it on every request.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

        services.AddScoped<IdentityPrincipals>();
        services.AddScoped<IIdentityProviderAccounts, IdentityAccountAdministration>();
        services.AddSingleton<IClaimsTransformation, ApiRoleClaimsTransformation>();
        services.AddScoped<IAuthorizationHandler, MappedRoleHandler>();
        services.AddAuthorization(ProgressPolicies.Configure);

        services.AddRazorPages(options => options.RootDirectory = "/Features/Identity/Pages");
    }

    private static ValueTask RequireSubject(OpenIddictValidationEvents.ValidateTokenContext context)
    {
        if (context.Principal is { } principal && string.IsNullOrWhiteSpace(principal.GetClaim(Claims.Subject)))
            context.Reject(Errors.InvalidToken, "An access token with a subject is required.");

        return ValueTask.CompletedTask;
    }

    private static void ConfigureSignInCookie(CookieAuthenticationOptions options)
    {
        options.LoginPath = LoginPath;
        options.LogoutPath = SignedOutPath;
        options.AccessDeniedPath = ErrorPath;
        options.ExpireTimeSpan = SignInSession.IdleTimeout;
        options.SlidingExpiration = true;
        options.Cookie.Name = ".MedicalApp.SignIn";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        var validateStamp = options.Events.OnValidatePrincipal;
        options.Events.OnSigningIn = context =>
        {
            SignInSession.MarkSignedIn(context.Properties, context.Options.TimeProvider!.GetUtcNow());
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            if (SignInSession.HasExpired(context.Properties, context.Options.TimeProvider!.GetUtcNow()))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }

            await validateStamp(context);
        };
    }

    public const string SwaggerClientId = "medicalapp-swagger";
    public const string SwaggerSecurityScheme = "MedicalApp";

    /// <summary>
    /// Registers the OpenAPI document with an OAuth2 authorization code scheme for the application's own server.
    /// </summary>
    public static void AddIdentitySwagger(this IHostApplicationBuilder builder)
    {
        var issuer = IdentityIssuer.Resolve(builder.Configuration, builder.Environment);
        builder.Services.SwaggerDocument(options =>
        {
            options.EnableJWTBearerAuth = false;
            options.DocumentSettings = settings => settings.AddAuth(SwaggerSecurityScheme, new OpenApiSecurityScheme
            {
                Type = OpenApiSecuritySchemeType.OAuth2,
                Description = "Sign in with the authorization code flow and PKCE.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(issuer, AuthorizationPath).AbsoluteUri,
                        TokenUrl = new Uri(issuer, TokenPath).AbsoluteUri,
                        Scopes = new Dictionary<string, string>
                        {
                            [Scopes.OpenId] = "OpenID Connect sign-in",
                            [ApiRoles.Resource] = "MedicalApp API"
                        }
                    }
                }
            });
        });
    }

    /// <summary>
    /// Serves Swagger UI with the public development client. The migration service registers its redirect URI.
    /// </summary>
    public static void UseIdentitySwaggerGen(this IApplicationBuilder app) =>
        app.UseSwaggerGen(uiConfig: settings =>
        {
            settings.OAuth2Client = new OAuth2ClientSettings
            {
                ClientId = SwaggerClientId,
                AppName = "MedicalApp API",
                UsePkceWithAuthorizationCodeGrant = true
            };
            settings.OAuth2Client.Scopes.Add(Scopes.OpenId);
            settings.OAuth2Client.Scopes.Add(ApiRoles.Resource);
        });
}
