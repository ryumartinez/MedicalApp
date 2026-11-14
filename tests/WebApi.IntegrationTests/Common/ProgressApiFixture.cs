using System.Net.Http.Headers;
using MedicalApp.IntegrationTests.Common.Infrastructure.Database;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using MedicalApp.IntegrationTests.Common.Infrastructure.Storage;
using MedicalApp.IntegrationTests.Common.Utilities;
using MedicalApp.IntegrationTests.Common.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Common;

/// <summary>
/// Runs the API, which is also the authorization server, against real PostgreSQL and initialized real Garage storage.
/// Clients carry access tokens from the authorization code flow, never test authentication handlers.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class ProgressApiFixture : IAsyncLifetime
{
    private readonly TestDatabase _database = new();
    private WebApiTestFactory _factory = null!;

    public TestIdentity Identity { get; private set; } = null!;

    public GarageContainer Garage { get; } = new();

    public TestTimeProvider Clock { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_database.InitializeAsync(), Garage.InitializeAsync());
        _factory = new WebApiTestFactory(_database.DbConnection, Garage, Clock);
        Identity = new TestIdentity(_factory);
        await Identity.SeedAsync();
    }

    /// <summary>
    /// Clears the database, then restores the client registrations and development accounts that every test expects.
    /// </summary>
    public async Task ResetAsync()
    {
        Clock.Offset = TimeSpan.Zero;
        await _database.ResetAsync();
        await Identity.SeedAsync();
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public HttpClient CreateAnonymousClient() => _factory.CreateClient();

    /// <summary>
    /// A cookie-aware HTTPS client for the authorization server's pages and endpoints.
    /// </summary>
    public HttpClient CreateBrowser() => Identity.CreateBrowser();

    public HttpClient CreateClient(string accessToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task<HttpClient> CreateClientAsync(string username)
    {
        var (accessToken, _) = await Identity.GetTokensAsync(username);
        return CreateClient(accessToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
        await Garage.DisposeAsync();
    }
}

[CollectionDefinition]
public class ProgressApiCollection : ICollectionFixture<ProgressApiFixture>;
