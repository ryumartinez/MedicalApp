using MedicalApp.Common.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MigrationService.Initializers;

/// <summary>
/// Creates or updates the API scope, the API roles, and the configured application clients. Runs in every environment.
/// Clients that this initializer does not manage are left unchanged.
/// </summary>
public class IdentityClientInitializer(
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    RoleManager<IdentityRole<Guid>> roles,
    IConfiguration configuration,
    ILogger<IdentityClientInitializer> logger)
{
    public const string SwaggerClientId = "medicalapp-swagger";
    public const string SwaggerRedirectUrisKey = "Authentication:Clients:Swagger:RedirectUris";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await EnsureScopeAsync(cancellationToken);
        await EnsureRolesAsync();
        await EnsureSwaggerClientAsync(cancellationToken);
    }

    private async Task EnsureScopeAsync(CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = ApiRoles.Resource,
            DisplayName = "MedicalApp API",
            Resources = { ApiRoles.Resource }
        };

        var scope = await scopes.FindByNameAsync(ApiRoles.Resource, cancellationToken);
        if (scope is null)
            await scopes.CreateAsync(descriptor, cancellationToken);
        else
            await scopes.UpdateAsync(scope, descriptor, cancellationToken);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in ApiRoles.All)
        {
            if (await roles.RoleExistsAsync(role))
                continue;

            var result = await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create the {role} role: {Describe(result)}");
        }
    }

    private async Task EnsureSwaggerClientAsync(CancellationToken cancellationToken)
    {
        var redirectUris = (configuration.GetSection(SwaggerRedirectUrisKey).Get<string?[]>() ?? [])
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Select(uri => uri!)
            .ToArray();
        if (redirectUris.Length == 0)
        {
            // Production gets no development redirect URIs by default. An existing registration stays as it is.
            logger.SkippingClient(SwaggerClientId, SwaggerRedirectUrisKey);
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = SwaggerClientId,
            DisplayName = "MedicalApp Swagger UI",
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + ApiRoles.Resource
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));

        var client = await applications.FindByClientIdAsync(SwaggerClientId, cancellationToken);
        if (client is null)
            await applications.CreateAsync(descriptor, cancellationToken);
        else
            await applications.UpdateAsync(client, descriptor, cancellationToken);
    }

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(error => error.Description));
}

internal static partial class IdentityClientInitializerLog
{
    [LoggerMessage(LogLevel.Information, "Skipping the {ClientId} client because {Key} is not configured.")]
    public static partial void SkippingClient(this ILogger logger, string clientId, string key);
}
