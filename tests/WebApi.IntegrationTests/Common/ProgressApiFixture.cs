using System.Net.Http.Headers;
using MedicalApp.IntegrationTests.Common.Infrastructure.Database;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using MedicalApp.IntegrationTests.Common.Infrastructure.Storage;
using MedicalApp.IntegrationTests.Common.Utilities;
using MedicalApp.IntegrationTests.Common.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Common;

/// <summary>
/// Runs the API against real PostgreSQL, a reconciled real Keycloak realm, and initialized real Garage storage.
/// Clients carry access tokens from the authorization code flow, never test authentication handlers.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class ProgressApiFixture : IAsyncLifetime
{
    private readonly TestDatabase _database = new();
    private WebApiTestFactory _factory = null!;

    public KeycloakContainer Keycloak { get; } = new();

    public GarageContainer Garage { get; } = new();

    public TestTimeProvider Clock { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_database.InitializeAsync(), Keycloak.InitializeAsync(), Garage.InitializeAsync());
        _factory = new WebApiTestFactory(_database.DbConnection, Keycloak, Garage, Clock);
    }

    public Task ResetAsync()
    {
        Clock.Offset = TimeSpan.Zero;
        return _database.ResetAsync();
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public HttpClient CreateAnonymousClient() => _factory.CreateClient();

    public HttpClient CreateClient(string accessToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task<HttpClient> CreateClientAsync(string username)
    {
        var (accessToken, _) = await Keycloak.GetTokensAsync(username);
        return CreateClient(accessToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
        await Keycloak.DisposeAsync();
        await Garage.DisposeAsync();
    }
}

[CollectionDefinition]
public class ProgressApiCollection : ICollectionFixture<ProgressApiFixture>;
