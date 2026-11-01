using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        builder.Services.AddAuthorization();
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
