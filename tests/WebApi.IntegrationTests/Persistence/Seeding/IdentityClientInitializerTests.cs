using MedicalApp.Common.Authentication;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationService.Initializers;
using OpenIddict.Abstractions;

namespace MedicalApp.IntegrationTests.Persistence.Seeding;

// Client registrations, scopes, and roles survive the per-test reset, so each test arranges the state it needs.
public sealed class IdentityClientInitializerTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string OtherRedirectUri = "https://app.example/swagger/oauth2-redirect.html";

    [Fact]
    public async Task CreatesScopeRolesAndClientOnce()
    {
        await DeleteSwaggerClientAsync();

        await InitializeAsync(MigrationServices.SwaggerRedirectUri);
        await InitializeAsync(MigrationServices.SwaggerRedirectUri);

        await using var provider = Build(MigrationServices.SwaggerRedirectUri);
        using var scope = provider.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var client = await applications.FindByClientIdAsync(IdentityClientInitializer.SwaggerClientId, CancellationToken);
        client.Should().NotBeNull();
        (await applications.GetClientTypeAsync(client!, CancellationToken)).Should().Be(OpenIddictConstants.ClientTypes.Public);
        (await applications.GetRedirectUrisAsync(client!, CancellationToken)).Should().Equal(MigrationServices.SwaggerRedirectUri);
        (await applications.GetRequirementsAsync(client!, CancellationToken)).Should().Contain(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        (await applications.GetPermissionsAsync(client!, CancellationToken)).Should()
            .Contain(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode)
            .And.Contain(OpenIddictConstants.Permissions.Prefixes.Scope + ApiRoles.Resource)
            .And.NotContain(OpenIddictConstants.Permissions.GrantTypes.Password)
            .And.NotContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);

        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var apiScope = await scopes.FindByNameAsync(ApiRoles.Resource, CancellationToken);
        (await scopes.GetResourcesAsync(apiScope!, CancellationToken)).Should().Equal(ApiRoles.Resource);

        (await GetQueryable<IdentityRole<Guid>>().Select(x => x.Name!).ToListAsync(CancellationToken))
            .Should().BeEquivalentTo(ApiRoles.All);
        (await CountClientsAsync(IdentityClientInitializer.SwaggerClientId)).Should().Be(1);
    }

    [Fact]
    public async Task ReplacesChangedRedirectUris()
    {
        await InitializeAsync(MigrationServices.SwaggerRedirectUri);

        await InitializeAsync(OtherRedirectUri);

        await using var provider = Build(OtherRedirectUri);
        using var scope = provider.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var client = await applications.FindByClientIdAsync(IdentityClientInitializer.SwaggerClientId, CancellationToken);
        (await applications.GetRedirectUrisAsync(client!, CancellationToken)).Should().Equal(OtherRedirectUri);
        (await CountClientsAsync(IdentityClientInitializer.SwaggerClientId)).Should().Be(1);
    }

    [Fact]
    public async Task LeavesUnrelatedClientsUnchanged()
    {
        const string clientId = "unrelated-test-client";
        await using (var provider = Build(MigrationServices.SwaggerRedirectUri))
        using (var scope = provider.CreateScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(clientId, CancellationToken) is { } existing)
                await applications.DeleteAsync(existing, CancellationToken);
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientType = OpenIddictConstants.ClientTypes.Public,
                RedirectUris = { new Uri("https://other.example/callback") }
            }, CancellationToken);
        }

        await InitializeAsync(OtherRedirectUri);

        await using (var provider = Build(OtherRedirectUri))
        using (var scope = provider.CreateScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var client = await applications.FindByClientIdAsync(clientId, CancellationToken);
            (await applications.GetRedirectUrisAsync(client!, CancellationToken)).Should().Equal("https://other.example/callback");
            await applications.DeleteAsync(client!, CancellationToken);
        }
    }

    [Fact]
    public async Task CreatesNoClientWithoutRedirectUris()
    {
        await DeleteSwaggerClientAsync();

        await InitializeAsync(redirectUri: null);

        (await CountClientsAsync(IdentityClientInitializer.SwaggerClientId)).Should().Be(0);
        (await GetQueryable<IdentityRole<Guid>>().CountAsync(CancellationToken)).Should().Be(ApiRoles.All.Count);

        // Later tests expect the client the API fixtures register.
        await InitializeAsync(MigrationServices.SwaggerRedirectUri);
    }

    private async Task InitializeAsync(string? redirectUri)
    {
        await using var provider = Build(redirectUri);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityClientInitializer>().InitializeAsync(CancellationToken);
    }

    private async Task DeleteSwaggerClientAsync()
    {
        await using var provider = Build(MigrationServices.SwaggerRedirectUri);
        using var scope = provider.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applications.FindByClientIdAsync(IdentityClientInitializer.SwaggerClientId, CancellationToken) is { } client)
            await applications.DeleteAsync(client, CancellationToken);
    }

    private async Task<int> CountClientsAsync(string clientId)
    {
        await using var provider = Build(MigrationServices.SwaggerRedirectUri);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .CountAsync(x => x.ClientId == clientId, CancellationToken);
    }

    private ServiceProvider Build(string? redirectUri)
    {
        var settings = MigrationServices.DevelopmentSettings();
        settings[$"{IdentityClientInitializer.SwaggerRedirectUrisKey}:0"] = redirectUri;
        return MigrationServices.Build(ConnectionString, Environments.Production, settings);
    }

    private string ConnectionString
    {
        get
        {
            using var scope = CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;
        }
    }
}
